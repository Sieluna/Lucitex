#pragma once

#include "OracleApi.h"
#include <cstddef>
#include <cstdint>
#include <exception>

namespace oracle
{
struct failure
{
    uint32_t status;
    const char* message;
};

void require(bool condition, uint32_t status, const char* message);
void extent(uint64_t width, uint64_t height, uint64_t depth, const lucitex_oracle_limits& limits);
void decoded_size(uint64_t size, const lucitex_oracle_limits& limits);
uint8_t* allocate_output(uint64_t size, lucitex_oracle_result& result);
void validate_png(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result);
void validate_exr(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result);
void validate_ktx2(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result);
void validate_jpeg(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result);
void webp(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result);
}
