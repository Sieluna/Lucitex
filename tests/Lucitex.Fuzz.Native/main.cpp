#include <OpenEXR/ImfRgbaFile.h>
#include <ktx.h>
#include <png.h>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <exception>
#include <iostream>
#include <limits>
#include <string>
#include <vector>

namespace
{
constexpr std::uint64_t max_decoded_bytes = 512ULL * 1024ULL * 1024ULL;

bool validate_png(const char* path)
{
    png_image image{};
    image.version = PNG_IMAGE_VERSION;
    if (png_image_begin_read_from_file(&image, path) == 0)
    {
        return false;
    }

    image.format = PNG_FORMAT_RGBA;
    const auto size = static_cast<std::uint64_t>(PNG_IMAGE_SIZE(image));
    if (size > max_decoded_bytes)
    {
        png_image_free(&image);
        return false;
    }

    std::vector<png_byte> pixels(static_cast<std::size_t>(size));
    const auto accepted = png_image_finish_read(&image, nullptr, pixels.data(), 0, nullptr) != 0;
    png_image_free(&image);
    return accepted;
}

bool validate_exr(const char* path)
{
    try
    {
        Imf::RgbaInputFile file(path);
        const auto window = file.dataWindow();
        const auto width = static_cast<std::int64_t>(window.max.x) - window.min.x + 1;
        const auto height = static_cast<std::int64_t>(window.max.y) - window.min.y + 1;
        if (width <= 0 || height <= 0 || width > 65536 || height > 65536)
        {
            return false;
        }

        const auto pixels_count = static_cast<std::uint64_t>(width) * static_cast<std::uint64_t>(height);
        if (pixels_count > max_decoded_bytes / sizeof(Imf::Rgba))
        {
            return false;
        }

        const auto base_delta = static_cast<std::int64_t>(window.min.x) +
            static_cast<std::int64_t>(window.min.y) * width;
        if (base_delta != 0)
        {
            return false;
        }

        std::vector<Imf::Rgba> pixels(static_cast<std::size_t>(pixels_count));
        file.setFrameBuffer(pixels.data(), 1, static_cast<std::size_t>(width));
        file.readPixels(window.min.y, window.max.y);
        return true;
    }
    catch (const std::exception&)
    {
        return false;
    }
}

bool validate_ktx2(const char* path)
{
    ktxTexture2* texture = nullptr;
    const auto result = ktxTexture2_CreateFromNamedFile(path, KTX_TEXTURE_CREATE_LOAD_IMAGE_DATA_BIT, &texture);
    if (result != KTX_SUCCESS)
    {
        return false;
    }

    const auto width = static_cast<std::uint64_t>(texture->baseWidth);
    const auto height = static_cast<std::uint64_t>(texture->baseHeight);
    const auto depth = static_cast<std::uint64_t>(std::max<ktx_uint32_t>(1, texture->baseDepth));
    const auto layers = static_cast<std::uint64_t>(std::max<ktx_uint32_t>(1, texture->numLayers));
    const auto faces = static_cast<std::uint64_t>(std::max<ktx_uint32_t>(1, texture->numFaces));

    const auto accepted = width > 0 && height > 0 && width <= 65536 && height <= 65536 &&
        (width * height * depth * layers * faces) <= max_decoded_bytes;

    ktxTexture_Destroy(ktxTexture(texture));
    return accepted;
}
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "usage: lucitex_native_oracle <png|exr|ktx2> <path>\n";
        return 64;
    }

    if (std::strcmp(argv[1], "png") == 0)
    {
        return validate_png(argv[2]) ? 0 : 1;
    }

    if (std::strcmp(argv[1], "exr") == 0)
    {
        return validate_exr(argv[2]) ? 0 : 1;
    }

    if (std::strcmp(argv[1], "ktx2") == 0)
    {
        return validate_ktx2(argv[2]) ? 0 : 1;
    }

    std::cerr << "unknown format\n";
    return 64;
}
