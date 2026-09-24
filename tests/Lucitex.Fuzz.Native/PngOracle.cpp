#include "NativeChecks.h"
#include <png.h>
#include <memory>
#include <vector>

namespace oracle
{
void validate_png(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result)
{
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
    std::vector<png_byte> pixels(static_cast<size_t>(size));
    require(png_image_finish_read(&image, nullptr, pixels.data(), 0, nullptr) != 0, LUCITEX_REJECTED, "PNG payload rejected.");
    result.width = image.width;
    result.height = image.height;
    result.decoded_bytes = size;
    result.subresources = 1;
}
}
