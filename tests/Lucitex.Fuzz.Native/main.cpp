#include <OpenEXR/ImfRgbaFile.h>
#include <jpeglib.h>
#include <ktx.h>
#include <png.h>

#include <algorithm>
#include <chrono>
#include <csetjmp>
#include <cstdint>
#include <cstdio>
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

struct jpeg_error_mgr_wrapper
{
    jpeg_error_mgr pub;
    std::jmp_buf setjmp_buffer;
};

void jpeg_error_exit(j_common_ptr cinfo)
{
    auto* err = reinterpret_cast<jpeg_error_mgr_wrapper*>(cinfo->err);
    std::longjmp(err->setjmp_buffer, 1);
}

void jpeg_emit_message_quiet(j_common_ptr, int)
{
}

void jpeg_output_message_quiet(j_common_ptr)
{
}

bool validate_jpeg(const char* path)
{
    FILE* file = nullptr;
    if (fopen_s(&file, path, "rb") != 0 || file == nullptr)
    {
        return false;
    }

    jpeg_decompress_struct cinfo{};
    jpeg_error_mgr_wrapper jerr{};
    cinfo.err = jpeg_std_error(&jerr.pub);
    jerr.pub.error_exit = jpeg_error_exit;
    jerr.pub.emit_message = jpeg_emit_message_quiet;
    jerr.pub.output_message = jpeg_output_message_quiet;

    if (setjmp(jerr.setjmp_buffer))
    {
        jpeg_destroy_decompress(&cinfo);
        std::fclose(file);
        return false;
    }

    jpeg_create_decompress(&cinfo);
    jpeg_stdio_src(&cinfo, file);
    jpeg_read_header(&cinfo, TRUE);

    if (cinfo.image_width == 0 || cinfo.image_height == 0 || cinfo.image_width > 65536 || cinfo.image_height > 65536)
    {
        jpeg_destroy_decompress(&cinfo);
        std::fclose(file);
        return false;
    }

    jpeg_start_decompress(&cinfo);

    const auto row_stride = static_cast<std::uint64_t>(cinfo.output_width) * cinfo.output_components;
    const auto total_bytes = row_stride * cinfo.output_height;
    if (total_bytes > max_decoded_bytes)
    {
        jpeg_destroy_decompress(&cinfo);
        std::fclose(file);
        return false;
    }

    std::vector<JSAMPLE> row(static_cast<std::size_t>(row_stride));
    JSAMPROW row_pointer[1] = { row.data() };
    while (cinfo.output_scanline < cinfo.output_height)
    {
        jpeg_read_scanlines(&cinfo, row_pointer, 1);
    }

    jpeg_finish_decompress(&cinfo);
    jpeg_destroy_decompress(&cinfo);
    std::fclose(file);
    return true;
}
}

int main(int argc, char** argv)
{
    if (argc != 3 && argc != 5)
    {
        std::cerr << "usage: lucitex_native_oracle <png|exr|ktx2|jpg> <path>\n";
        std::cerr << "       lucitex_native_oracle bench <png|exr|ktx2|jpg> <path> <iterations>\n";
        return 64;
    }

    if (argc == 5 && std::strcmp(argv[1], "bench") == 0)
    {
        const auto iterations = std::stoi(argv[4]);
        if (iterations <= 0)
        {
            return 64;
        }

        const auto decode = [&]()
        {
            if (std::strcmp(argv[2], "png") == 0)
            {
                return validate_png(argv[3]);
            }

            if (std::strcmp(argv[2], "exr") == 0)
            {
                return validate_exr(argv[3]);
            }

            if (std::strcmp(argv[2], "ktx2") == 0)
            {
                return validate_ktx2(argv[3]);
            }

            if (std::strcmp(argv[2], "jpg") == 0)
            {
                return validate_jpeg(argv[3]);
            }

            return false;
        };

        for (auto i = 0; i < std::min(10, iterations); ++i)
        {
            if (!decode())
            {
                return 1;
            }
        }

        const auto started = std::chrono::steady_clock::now();
        for (auto i = 0; i < iterations; ++i)
        {
            if (!decode())
            {
                return 1;
            }
        }
        const auto elapsed = std::chrono::steady_clock::now() - started;
        const auto elapsed_ns = std::chrono::duration_cast<std::chrono::nanoseconds>(elapsed).count();
        std::cout << "iterations=" << iterations << " elapsed_ns=" << elapsed_ns
                  << " ns_per_iteration=" << elapsed_ns / iterations << '\n';
        return 0;
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

    if (std::strcmp(argv[1], "jpg") == 0)
    {
        return validate_jpeg(argv[2]) ? 0 : 1;
    }

    std::cerr << "unknown format\n";
    return 64;
}
