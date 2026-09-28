#include "NativeChecks.h"
#include <cstdio>
#include <jpeglib.h>
#include <jerror.h>
#include <algorithm>
#include <csetjmp>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <memory>

namespace oracle
{
struct jpeg_error
{
    jpeg_error_mgr manager{};
    std::jmp_buf jump;
    bool warned = false;
    char message[JMSG_LENGTH_MAX]{};
};

struct jpeg_state
{
    jpeg_decompress_struct decoder{};
    jpeg_error error{};
    bool created = false;

    ~jpeg_state()
    {
        if (created) { jpeg_destroy_decompress(&decoder); }
    }
};

void jpeg_fail(j_common_ptr decoder)
{
    auto* error = reinterpret_cast<jpeg_error*>(decoder->err);
    (*decoder->err->format_message)(decoder, error->message);
    std::longjmp(error->jump, 1);
}

void jpeg_warning(j_common_ptr decoder, int level)
{
    if (level < 0)
    {
        auto* error = reinterpret_cast<jpeg_error*>(decoder->err);
        error->warned = true;
        (*decoder->err->format_message)(decoder, error->message);
    }
}

void jpeg_quiet(j_common_ptr) {}

struct jpeg_encoder_state
{
    jpeg_compress_struct encoder{};
    jpeg_error error{};
    unsigned char* output = nullptr;
    unsigned long length = 0;
    bool created = false;
    ~jpeg_encoder_state()
    {
        if (created) { jpeg_destroy_compress(&encoder); }
        std::free(output);
    }
};

void encode_jpeg(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result)
{
    extent(request.width, request.height, 1, limits);
    const auto size = static_cast<uint64_t>(request.width) * request.height * 3;
    decoded_size(size, limits);
    require(request.input_length == size && limits.max_channels >= 3, LUCITEX_INVALID_REQUEST, "Expected RGB8 JPEG pixels.");
    require(std::isfinite(request.quality) && request.quality >= 1 && request.quality <= 100,
        LUCITEX_INVALID_REQUEST, "JPEG quality must be between 1 and 100.");
    const auto state = std::make_unique<jpeg_encoder_state>();
    auto& encoder = state->encoder;
    auto& error = state->error;
    encoder.err = jpeg_std_error(&error.manager);
    error.manager.error_exit = jpeg_fail;
    error.manager.emit_message = jpeg_warning;
    error.manager.output_message = jpeg_quiet;
    if (setjmp(error.jump)) {
        if (error.manager.msg_code == JERR_OUT_OF_MEMORY) throw failure{LUCITEX_RESOURCE_LIMIT, "JPEG allocation failed."};
        throw failure{LUCITEX_REJECTED, error.message};
    }
    state->created = true;
    jpeg_create_compress(&encoder);
    jpeg_mem_dest(&encoder, &state->output, &state->length);
    encoder.image_width = request.width;
    encoder.image_height = request.height;
    encoder.input_components = 3;
    encoder.in_color_space = JCS_RGB;
    jpeg_set_defaults(&encoder);
    jpeg_set_quality(&encoder, static_cast<int>(request.quality), TRUE);
    encoder.comp_info[0].h_samp_factor = encoder.comp_info[0].v_samp_factor = 2;
    encoder.comp_info[1].h_samp_factor = encoder.comp_info[1].v_samp_factor = 1;
    encoder.comp_info[2].h_samp_factor = encoder.comp_info[2].v_samp_factor = 1;
    encoder.optimize_coding = TRUE;
    encoder.dct_method = JDCT_ISLOW;
    encoder.mem->max_memory_to_use = static_cast<long>(std::min<uint64_t>(limits.max_working_set, std::numeric_limits<long>::max()));
    jpeg_start_compress(&encoder, TRUE);
    while (encoder.next_scanline < encoder.image_height) {
        auto* row = const_cast<JSAMPLE*>(request.input + static_cast<size_t>(encoder.next_scanline) * request.width * 3);
        require(jpeg_write_scanlines(&encoder, &row, 1) == 1, LUCITEX_REJECTED, "JPEG encoding stalled.");
    }
    jpeg_finish_compress(&encoder);
    require(!error.warned, LUCITEX_REJECTED, error.message);
    require(state->length <= limits.max_input_bytes && state->length <= limits.max_working_set,
        LUCITEX_RESOURCE_LIMIT, "JPEG reference exceeds the output budget.");
    std::memcpy(allocate_output(state->length, result), state->output, state->length);
    result.width = request.width;
    result.height = request.height;
    result.decoded_bytes = size;
    result.subresources = 1;
}

void validate_jpeg(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result)
{
    if (request.operation == LUCITEX_ENCODE_REFERENCE) { encode_jpeg(request, limits, result); return; }
    require(request.input_length <= std::numeric_limits<unsigned long>::max(), LUCITEX_RESOURCE_LIMIT, "JPEG input exceeds the library address range.");
    const auto state = std::make_unique<jpeg_state>();
    auto& decoder = state->decoder;
    auto& error = state->error;
    decoder.err = jpeg_std_error(&error.manager);
    error.manager.error_exit = jpeg_fail;
    error.manager.emit_message = jpeg_warning;
    error.manager.output_message = jpeg_quiet;
    if (setjmp(error.jump))
    {
        if (error.manager.msg_code == JERR_OUT_OF_MEMORY) { throw failure{LUCITEX_RESOURCE_LIMIT, "JPEG allocation failed."}; }
        throw failure{LUCITEX_REJECTED, error.message};
    }
    state->created = true;
    jpeg_create_decompress(&decoder);
    jpeg_mem_src(&decoder, request.input, static_cast<unsigned long>(request.input_length));
    jpeg_read_header(&decoder, TRUE);
    if (!decoder.arith_code)
    {
        for (int i = 0; i < decoder.comps_in_scan; ++i)
        {
            const auto* component = decoder.cur_comp_info[i];
            if (decoder.Ss == 0 && decoder.Ah == 0)
            {
                require(component->dc_tbl_no >= 0 && component->dc_tbl_no < NUM_HUFF_TBLS &&
                    decoder.dc_huff_tbl_ptrs[component->dc_tbl_no], LUCITEX_REJECTED, "JPEG scan references an undefined DC Huffman table.");
            }
            if (decoder.Se > 0)
            {
                require(component->ac_tbl_no >= 0 && component->ac_tbl_no < NUM_HUFF_TBLS &&
                    decoder.ac_huff_tbl_ptrs[component->ac_tbl_no], LUCITEX_REJECTED, "JPEG scan references an undefined AC Huffman table.");
            }
        }
    }
    extent(decoder.image_width, decoder.image_height, 1, limits);
    const auto pixels = static_cast<uint64_t>(decoder.image_width) * decoder.image_height;
    require(pixels <= limits.max_decoded_bytes / 4, LUCITEX_RESOURCE_LIMIT, "JPEG output exceeds the byte budget.");
    decoded_size(pixels * 4, limits);
    decoder.mem->max_memory_to_use = static_cast<long>(std::min<uint64_t>(limits.max_working_set, std::numeric_limits<long>::max()));
    if (request.operation == LUCITEX_DECODE_PIXELS) {
        decoder.out_color_space = JCS_RGB;
        decoder.do_fancy_upsampling = FALSE;
        decoder.dct_method = JDCT_ISLOW;
    }
    jpeg_start_decompress(&decoder);
    const auto row_stride = static_cast<uint64_t>(decoder.output_width) * decoder.output_components;
    const auto total_bytes = row_stride * decoder.output_height;
    decoded_size(total_bytes, limits);
    require(row_stride <= std::numeric_limits<JDIMENSION>::max(), LUCITEX_RESOURCE_LIMIT, "JPEG row is not addressable.");
    auto rows = (*decoder.mem->alloc_sarray)(reinterpret_cast<j_common_ptr>(&decoder), JPOOL_IMAGE,
        static_cast<JDIMENSION>(row_stride), 1);
    auto* output = request.operation == LUCITEX_DECODE_PIXELS ? allocate_output(total_bytes, result) : nullptr;
    while (decoder.output_scanline < decoder.output_height)
    {
        const auto offset = static_cast<size_t>(decoder.output_scanline) * row_stride;
        require(jpeg_read_scanlines(&decoder, rows, 1) == 1, LUCITEX_REJECTED, "JPEG scanline decoding stalled.");
        if (output) std::memcpy(output + offset, rows[0], static_cast<size_t>(row_stride));
    }
    jpeg_finish_decompress(&decoder);
    result.width = decoder.output_width;
    result.height = decoder.output_height;
    result.decoded_bytes = total_bytes;
    result.subresources = 1;
    if (error.warned)
    {
        result.warnings = 1;
        throw failure{LUCITEX_REJECTED, error.message};
    }
}
}
