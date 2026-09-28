#include "NativeChecks.h"
#include <png.h>
#include <zlib.h>
#include <cstring>
#include <memory>
#include <vector>

namespace oracle
{
void encode_png(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result)
{
    extent(request.width, request.height, 1, limits);
    const auto size = static_cast<uint64_t>(request.width) * request.height * 4;
    decoded_size(size, limits);
    require(request.input_length == size && limits.max_channels >= 4, LUCITEX_INVALID_REQUEST, "Expected RGBA8 PNG pixels.");
    png_image image{};
    image.version = PNG_IMAGE_VERSION;
    image.width = request.width;
    image.height = request.height;
    image.format = PNG_FORMAT_RGBA;
    const std::unique_ptr<png_image, decltype(&png_image_free)> owner(&image, png_image_free);
    png_alloc_size_t length = 0;
    require(png_image_write_to_memory(&image, nullptr, &length, 0, request.input, 0, nullptr) != 0,
        LUCITEX_REJECTED, "PNG reference sizing failed.");
    require(length <= limits.max_input_bytes && length <= limits.max_working_set, LUCITEX_RESOURCE_LIMIT, "PNG reference exceeds the output budget.");
    auto* output = allocate_output(length, result);
    require(png_image_write_to_memory(&image, output, &length, 0, request.input, 0, nullptr) != 0,
        LUCITEX_REJECTED, "PNG reference encoding failed.");
    result.output_length = length;
    result.width = request.width;
    result.height = request.height;
    result.decoded_bytes = size;
    result.subresources = 1;
}

void png_chunks(const lucitex_oracle_request& request)
{
    const auto read32 = [](const uint8_t* p) {
        return (static_cast<uint32_t>(p[0]) << 24) | (static_cast<uint32_t>(p[1]) << 16) |
            (static_cast<uint32_t>(p[2]) << 8) | p[3];
    };
    uint64_t offset = 8;
    while (offset < request.input_length)
    {
        require(request.input_length - offset >= 12, LUCITEX_REJECTED, "Truncated PNG chunk.");
        const auto* chunk = request.input + offset;
        const auto size = read32(chunk);
        require(size <= request.input_length - offset - 12, LUCITEX_REJECTED, "PNG chunk exceeds the input.");
        auto crc = crc32(0, chunk + 4, 4);
        crc = crc32(crc, chunk + 8, size);
        require(crc == read32(chunk + 8 + size), LUCITEX_REJECTED, "Invalid PNG chunk CRC.");
        if (std::memcmp(chunk + 4, "IEND", 4) == 0)
        {
            require(size == 0, LUCITEX_REJECTED, "Invalid PNG IEND size.");
            return;
        }
        offset += 12ULL + size;
    }
    throw failure{LUCITEX_REJECTED, "PNG is missing IEND."};
}

void validate_png(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result)
{
    if (request.operation == LUCITEX_ENCODE_REFERENCE) { encode_png(request, limits, result); return; }
    png_chunks(request);
    png_image image{};
    image.version = PNG_IMAGE_VERSION;
    const std::unique_ptr<png_image, decltype(&png_image_free)> owner(&image, png_image_free);
    require(png_image_begin_read_from_memory(&image, request.input, static_cast<size_t>(request.input_length)) != 0,
        LUCITEX_REJECTED, "PNG header rejected.");
    extent(image.width, image.height, 1, limits);
    image.format = PNG_FORMAT_RGBA;
    const auto pixels_count = static_cast<uint64_t>(image.width) * image.height;
    require(pixels_count <= limits.max_decoded_bytes / 4, LUCITEX_RESOURCE_LIMIT, "PNG output exceeds the byte budget.");
    const auto size = pixels_count * 4;
    decoded_size(size, limits);
    std::vector<png_byte> scratch;
    auto* pixels = request.operation == LUCITEX_DECODE_PIXELS ? allocate_output(size, result) : nullptr;
    if (!pixels) { scratch.resize(static_cast<size_t>(size)); pixels = scratch.data(); }
    require(png_image_finish_read(&image, nullptr, pixels, 0, nullptr) != 0, LUCITEX_REJECTED, "PNG payload rejected.");
    require((image.warning_or_error & PNG_IMAGE_WARNING) == 0, LUCITEX_REJECTED, "PNG decoded with validation warnings.");
    result.width = image.width;
    result.height = image.height;
    result.decoded_bytes = size;
    result.subresources = 1;
}
}
