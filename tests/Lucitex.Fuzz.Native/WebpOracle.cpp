#include "NativeChecks.h"
#include <webp/decode.h>
#include <webp/encode.h>
#include <cmath>
#include <cstring>
#include <limits>
#include <memory>
#include <vector>

namespace oracle
{
uint32_t webp_u32(const uint8_t* data)
{
    return static_cast<uint32_t>(data[0]) | (static_cast<uint32_t>(data[1]) << 8) |
        (static_cast<uint32_t>(data[2]) << 16) | (static_cast<uint32_t>(data[3]) << 24);
}

void webp_container(const lucitex_oracle_request& request, const uint8_t*& payload, size_t& payload_size)
{
    require(request.input_length >= 12 && std::memcmp(request.input, "RIFF", 4) == 0 &&
        std::memcmp(request.input + 8, "WEBP", 4) == 0, LUCITEX_REJECTED, "Invalid WebP RIFF header.");
    const uint64_t end = static_cast<uint64_t>(webp_u32(request.input + 4)) + 8;
    require(end >= 20 && end <= request.input_length && (end & 1) == 0, LUCITEX_REJECTED, "Invalid WebP RIFF size.");
    uint8_t flags = 0;
    uint8_t metadata = 0;
    bool image = false;
    for (uint64_t offset = 12; offset < end;)
    {
        require(end - offset >= 8, LUCITEX_REJECTED, "Truncated WebP chunk header.");
        const auto* chunk = request.input + offset;
        const uint64_t size = webp_u32(chunk + 4);
        const uint64_t padded = size + (size & 1);
        require(padded <= end - offset - 8, LUCITEX_REJECTED, "WebP chunk exceeds its container.");
        require((size & 1) == 0 || chunk[8 + size] == 0, LUCITEX_REJECTED, "Invalid WebP chunk padding.");
        if (std::memcmp(chunk, "VP8X", 4) == 0)
        {
            require(offset == 12 && size == 10, LUCITEX_REJECTED, "Invalid WebP extended header.");
            flags = chunk[8];
            require((flags & 2) == 0, LUCITEX_UNSUPPORTED, "Animated WebP is not supported by this oracle.");
        }
        else if (std::memcmp(chunk, "ICCP", 4) == 0) { metadata |= 32; }
        else if (std::memcmp(chunk, "EXIF", 4) == 0) { metadata |= 8; }
        else if (std::memcmp(chunk, "XMP ", 4) == 0) { metadata |= 4; }
        else if (std::memcmp(chunk, "VP8L", 4) == 0 || std::memcmp(chunk, "VP8 ", 4) == 0)
        {
            require(!image, LUCITEX_REJECTED, "Duplicate WebP image chunk.");
            image = true;
            if (std::memcmp(chunk, "VP8L", 4) == 0)
            {
                payload = chunk + 8;
                payload_size = static_cast<size_t>(size);
            }
        }
        offset += 8 + padded;
    }
    require(image && (flags & 44) == metadata, LUCITEX_REJECTED, "WebP metadata flags do not match its chunks.");
}

void encode_webp(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result)
{
    require(std::isfinite(request.quality) && request.quality >= 0 && request.quality <= 100,
        LUCITEX_INVALID_REQUEST, "WebP quality must be between 0 and 100.");
    require(request.width > 0 && request.height > 0 && request.width <= WEBP_MAX_DIMENSION && request.height <= WEBP_MAX_DIMENSION,
        LUCITEX_INVALID_REQUEST, "Invalid WebP encoding extent.");
    extent(request.width, request.height, 1, limits);
    const auto size = static_cast<uint64_t>(request.width) * request.height * 4;
    decoded_size(size, limits);
    require(request.input_length == size, LUCITEX_INVALID_REQUEST, "RGBA input length does not match the extent.");
    uint8_t* encoded = nullptr;
    const auto length = WebPEncodeRGBA(request.input, request.width, request.height, request.width * 4, request.quality, &encoded);
    const std::unique_ptr<uint8_t, decltype(&WebPFree)> owner(encoded, WebPFree);
    require(length != 0 && encoded, LUCITEX_REJECTED, "WebP encoding failed.");
    require(length <= limits.max_input_bytes && length <= limits.max_working_set, LUCITEX_RESOURCE_LIMIT, "WebP encoded output exceeds the byte budget.");
    std::memcpy(allocate_output(length, result), encoded, length);
    result.width = request.width;
    result.height = request.height;
    result.decoded_bytes = size;
    result.subresources = 1;
}

void webp(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result)
{
    if (request.operation == LUCITEX_WEBP_ENCODE) { encode_webp(request, limits, result); return; }
    const auto* payload = request.input;
    auto payload_size = static_cast<size_t>(request.input_length);
    webp_container(request, payload, payload_size);
    WebPBitstreamFeatures features{};
    const auto status = WebPGetFeatures(request.input, static_cast<size_t>(request.input_length), &features);
    require(status != VP8_STATUS_OUT_OF_MEMORY, LUCITEX_RESOURCE_LIMIT, "WebP header allocation failed.");
    require(status == VP8_STATUS_OK, LUCITEX_REJECTED, "WebP header rejected.");
    require(!features.has_animation, LUCITEX_UNSUPPORTED, "Animated WebP is not supported by this oracle.");
    extent(features.width, features.height, 1, limits);
    const auto width = static_cast<uint64_t>(features.width);
    const auto height = static_cast<uint64_t>(features.height);
    const auto pixels = width * height;
    require(pixels <= limits.max_decoded_bytes / 4 && width <= std::numeric_limits<int>::max() / 4,
        LUCITEX_RESOURCE_LIMIT, "WebP output exceeds the byte budget.");
    const auto chroma_width = (width + 1) / 2;
    const auto chroma_height = (height + 1) / 2;
    const auto chroma_size = chroma_width * chroma_height;
    const auto size = request.operation == LUCITEX_WEBP_YUV ? pixels + 2 * chroma_size : pixels * 4;
    decoded_size(size, limits);
    std::vector<uint8_t> scratch;
    auto* output = request.operation == LUCITEX_VALIDATE ? nullptr : allocate_output(size, result);
    if (!output) { scratch.resize(static_cast<size_t>(size)); output = scratch.data(); }
    uint8_t* decoded;
    if (request.operation == LUCITEX_WEBP_YUV)
    {
        decoded = WebPDecodeYUVInto(payload, payload_size,
            output, static_cast<size_t>(pixels), static_cast<int>(width),
            output + pixels, static_cast<size_t>(chroma_size), static_cast<int>(chroma_width),
            output + pixels + chroma_size, static_cast<size_t>(chroma_size), static_cast<int>(chroma_width));
    }
    else
    {
        decoded = WebPDecodeRGBAInto(payload, payload_size, output,
            static_cast<size_t>(size), static_cast<int>(width * 4));
    }
    require(decoded != nullptr, LUCITEX_REJECTED, "WebP payload rejected.");
    result.width = static_cast<uint32_t>(width);
    result.height = static_cast<uint32_t>(height);
    result.decoded_bytes = size;
    result.subresources = 1;
}
}
