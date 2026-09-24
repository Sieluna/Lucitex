#include "NativeChecks.h"
#include <OpenEXR/ImfInputFile.h>
#include <OpenEXR/ImfIO.h>
#include <OpenEXR/ImfHeader.h>
#include <OpenEXR/ImfChannelList.h>
#include <OpenEXR/ImfFrameBuffer.h>
#include <IexBaseExc.h>
#include <cstring>
#include <limits>
#include <vector>

namespace oracle
{
class memory_input final : public Imf::IStream
{
public:
    memory_input(const uint8_t* data, uint64_t size) : Imf::IStream("memory"), _data(data), _size(size) {}

    bool read(char buffer[], int count) override
    {
        require(count >= 0 && static_cast<uint64_t>(count) <= _size - _position, LUCITEX_REJECTED, "Truncated EXR stream.");
        std::memcpy(buffer, _data + _position, count);
        _position += count;
        return _position != _size;
    }

    uint64_t tellg() override { return _position; }
    int64_t size() override { return static_cast<int64_t>(_size); }

    void seekg(uint64_t position) override
    {
        require(position <= _size, LUCITEX_REJECTED, "EXR seek outside the input.");
        _position = position;
    }

private:
    const uint8_t* _data;
    uint64_t _size;
    uint64_t _position = 0;
};

void validate_exr(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result)
{
    const uint8_t magic[] = {0x76, 0x2f, 0x31, 0x01};
    require(request.input_length >= 8 && std::memcmp(request.input, magic, 4) == 0, LUCITEX_REJECTED, "Invalid EXR header.");
    require((request.input[5] & 0x1a) == 0, LUCITEX_UNSUPPORTED, "Tiled, multipart and deep EXR are not supported by this oracle.");
    require(request.input_length <= static_cast<uint64_t>(std::numeric_limits<int64_t>::max()), LUCITEX_RESOURCE_LIMIT, "EXR input size is not addressable.");
    try
    {
        memory_input stream(request.input, request.input_length);
        Imf::ContextInitializer context;
        context.setInputStream(&stream)
            .strictHeaderValidation(true)
            .disableChunkReconstruction(true)
            .setErrorHandler([](exr_const_context_t, exr_result_t, const char*) {});
        Imf::InputFile file("memory", context, 0);
        const auto window = file.header().dataWindow();
        const auto width = static_cast<int64_t>(window.max.x) - window.min.x + 1;
        const auto height = static_cast<int64_t>(window.max.y) - window.min.y + 1;
        require(width > 0 && height > 0, LUCITEX_REJECTED, "Empty EXR data window.");
        extent(width, height, 1, limits);
        const auto count = static_cast<uint64_t>(width) * height;
        const auto& channels = file.header().channels();
        std::vector<std::vector<uint8_t>> planes;
        Imf::FrameBuffer frame;
        uint64_t bytes = 0;
        for (auto it = channels.begin(); it != channels.end(); ++it)
        {
            const auto& channel = it.channel();
            require(channel.xSampling == 1 && channel.ySampling == 1, LUCITEX_UNSUPPORTED, "EXR subsampling is not supported by this oracle.");
            const auto sample_bytes = channel.type == Imf::HALF ? 2ULL : 4ULL;
            require(count <= (limits.max_decoded_bytes - bytes) / sample_bytes && planes.size() < limits.max_channels,
                LUCITEX_RESOURCE_LIMIT, "EXR channels exceed the byte or channel budget.");
            bytes += count * sample_bytes;
            decoded_size(bytes, limits);
            planes.emplace_back(static_cast<size_t>(count * sample_bytes));
            frame.insert(it.name(), Imf::Slice::Make(channel.type, planes.back().data(), window,
                static_cast<size_t>(sample_bytes), static_cast<size_t>(width * sample_bytes)));
        }
        require(!planes.empty(), LUCITEX_REJECTED, "EXR has no channels.");
        file.setFrameBuffer(frame);
        file.readPixels(window.min.y, window.max.y);
        result.width = static_cast<uint32_t>(width);
        result.height = static_cast<uint32_t>(height);
        result.decoded_bytes = bytes;
        result.subresources = 1;
    }
    catch (const Iex::BaseExc& error) { throw failure{LUCITEX_REJECTED, error.what()}; }
}
}
