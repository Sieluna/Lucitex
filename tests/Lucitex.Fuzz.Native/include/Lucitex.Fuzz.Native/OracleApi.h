#pragma once

#include <stdint.h>

#if defined(_WIN32)
#if defined(LUCITEX_ORACLE_BUILD)
#define LUCITEX_ORACLE_API __declspec(dllexport)
#else
#define LUCITEX_ORACLE_API __declspec(dllimport)
#endif
#define LUCITEX_ORACLE_CALL __cdecl
#else
#define LUCITEX_ORACLE_API __attribute__((visibility("default")))
#define LUCITEX_ORACLE_CALL
#endif

enum lucitex_oracle_status
{
    LUCITEX_ACCEPTED = 0,
    LUCITEX_REJECTED = 1,
    LUCITEX_RESOURCE_LIMIT = 3,
    LUCITEX_UNSUPPORTED = 4,
    LUCITEX_INVALID_REQUEST = 64,
    LUCITEX_INTERNAL_ERROR = 70
};

enum lucitex_oracle_format
{
    LUCITEX_PNG = 1,
    LUCITEX_EXR = 2,
    LUCITEX_KTX2 = 3,
    LUCITEX_JPEG = 4,
    LUCITEX_WEBP = 5
};

enum lucitex_oracle_operation
{
    LUCITEX_VALIDATE = 0,
    LUCITEX_WEBP_RGBA = 1,
    LUCITEX_WEBP_YUV = 2,
    LUCITEX_WEBP_ENCODE = 3
};

typedef struct lucitex_oracle_request
{
    uint32_t struct_size;
    uint32_t operation;
    uint32_t format;
    uint32_t width;
    uint32_t height;
    float quality;
    uint64_t input_length;
    const uint8_t* input;
} lucitex_oracle_request;

typedef struct lucitex_oracle_limits
{
    uint64_t max_input_bytes;
    uint64_t max_dimensions;
    uint64_t max_pixels;
    uint64_t max_decoded_bytes;
    uint64_t max_channels;
    uint64_t max_levels;
    uint64_t max_array_elements;
    uint64_t max_working_set;
} lucitex_oracle_limits;

typedef struct lucitex_oracle_result
{
    uint32_t struct_size;
    uint32_t warnings;
    uint32_t width;
    uint32_t height;
    uint64_t decoded_bytes;
    uint64_t subresources;
    uint64_t output_length;
    uint8_t* output;
    char message[512];
} lucitex_oracle_result;

#ifdef __cplusplus
extern "C" {
#endif

LUCITEX_ORACLE_API uint32_t LUCITEX_ORACLE_CALL lucitex_oracle_abi_version(void);
LUCITEX_ORACLE_API uint32_t LUCITEX_ORACLE_CALL lucitex_oracle_struct_size(uint32_t type);
LUCITEX_ORACLE_API uint32_t LUCITEX_ORACLE_CALL lucitex_oracle_execute(
    const lucitex_oracle_request* request, const lucitex_oracle_limits* limits, lucitex_oracle_result* result);
LUCITEX_ORACLE_API void LUCITEX_ORACLE_CALL lucitex_oracle_release(lucitex_oracle_result* result);

#ifdef __cplusplus
}
#endif
