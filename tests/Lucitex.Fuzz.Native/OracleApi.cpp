#include "NativeChecks.h"

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <new>

namespace oracle
{
void require(bool condition, uint32_t status, const char* message)
{
    if (!condition) { throw failure{status, message}; }
}

void extent(uint64_t width, uint64_t height, uint64_t depth, const lucitex_oracle_limits& limits)
{
    require(width > 0 && height > 0 && depth > 0, LUCITEX_REJECTED, "Empty image extent.");
    require(width <= limits.max_dimensions && height <= limits.max_dimensions && depth <= limits.max_dimensions &&
        width <= limits.max_pixels / height && width * height <= limits.max_pixels / depth,
        LUCITEX_RESOURCE_LIMIT, "Image extent exceeds the dimension or pixel budget.");
}

void decoded_size(uint64_t size, const lucitex_oracle_limits& limits)
{
    require(size <= limits.max_decoded_bytes && size <= limits.max_working_set && size <= std::numeric_limits<size_t>::max(),
        LUCITEX_RESOURCE_LIMIT, "Decoded allocation exceeds the byte budget.");
}

uint8_t* allocate_output(uint64_t size, lucitex_oracle_result& result)
{
    require(size > 0 && size <= std::numeric_limits<size_t>::max(), LUCITEX_RESOURCE_LIMIT, "Output size is not addressable.");
    auto* output = static_cast<uint8_t*>(std::malloc(static_cast<size_t>(size)));
    if (!output) { throw std::bad_alloc{}; }
    result.output = output;
    result.output_length = size;
    return output;
}

void message(lucitex_oracle_result& result, const char* text)
{
    const auto count = std::min(std::strlen(text), sizeof(result.message) - 1);
    std::memcpy(result.message, text, count);
    result.message[count] = '\0';
}
}

uint32_t LUCITEX_ORACLE_CALL lucitex_oracle_abi_version(void)
{
    return 1;
}

uint32_t LUCITEX_ORACLE_CALL lucitex_oracle_struct_size(uint32_t type)
{
    switch (type)
    {
        case 1: return sizeof(lucitex_oracle_request);
        case 2: return sizeof(lucitex_oracle_limits);
        case 3: return sizeof(lucitex_oracle_result);
        default: return 0;
    }
}

uint32_t LUCITEX_ORACLE_CALL lucitex_oracle_execute(
    const lucitex_oracle_request* request, const lucitex_oracle_limits* limits, lucitex_oracle_result* result)
{
    if (!result || result->struct_size != sizeof(*result)) { return LUCITEX_INVALID_REQUEST; }
    *result = {};
    result->struct_size = sizeof(*result);
    uint32_t status;
    try
    {
        oracle::require(request && limits && request->struct_size == sizeof(*request), LUCITEX_INVALID_REQUEST, "Invalid ABI request.");
        oracle::require(limits->max_dimensions > 0 && limits->max_pixels > 0 && limits->max_decoded_bytes > 0 &&
            limits->max_channels > 0 && limits->max_levels > 0 && limits->max_array_elements > 0 &&
            limits->max_working_set > 0 && limits->max_input_bytes > 0, LUCITEX_INVALID_REQUEST, "Limits must be positive.");
        oracle::require(request->input || request->input_length == 0, LUCITEX_INVALID_REQUEST, "Null input with nonzero length.");
        oracle::require(request->input_length <= limits->max_input_bytes && request->input_length <= std::numeric_limits<size_t>::max(),
            LUCITEX_RESOURCE_LIMIT, "Input exceeds the byte budget.");
        oracle::require(request->input_length > 0, LUCITEX_REJECTED, "Empty input.");
        oracle::require(request->operation <= LUCITEX_WEBP_ENCODE, LUCITEX_INVALID_REQUEST, "Unknown operation.");
        oracle::require(request->operation == LUCITEX_VALIDATE || request->format == LUCITEX_WEBP,
            LUCITEX_UNSUPPORTED, "Output operations require WebP.");
        switch (request->format)
        {
            case LUCITEX_PNG: oracle::validate_png(*request, *limits, *result); break;
            case LUCITEX_EXR: oracle::validate_exr(*request, *limits, *result); break;
            case LUCITEX_KTX2: oracle::validate_ktx2(*request, *limits, *result); break;
            case LUCITEX_JPEG: oracle::validate_jpeg(*request, *limits, *result); break;
            case LUCITEX_WEBP: oracle::webp(*request, *limits, *result); break;
            default: throw oracle::failure{LUCITEX_UNSUPPORTED, "Unknown format."};
        }
        return LUCITEX_ACCEPTED;
    }
    catch (const oracle::failure& error) { status = error.status; oracle::message(*result, error.message.c_str()); }
    catch (const std::bad_alloc&) { status = LUCITEX_RESOURCE_LIMIT; oracle::message(*result, "Native allocation failed."); }
    catch (const std::exception& error) { status = LUCITEX_INTERNAL_ERROR; oracle::message(*result, error.what()); }
    catch (...) { status = LUCITEX_INTERNAL_ERROR; oracle::message(*result, "Unknown native exception."); }
    lucitex_oracle_release(result);
    return status;
}

void LUCITEX_ORACLE_CALL lucitex_oracle_release(lucitex_oracle_result* result)
{
    if (result && result->struct_size == sizeof(*result))
    {
        std::free(result->output);
        result->output = nullptr;
        result->output_length = 0;
    }
}
