#include "NativeChecks.h"
#include <ktx.h>
#include <algorithm>
#include <memory>
#include <cstring>

namespace oracle
{
void ktx_result(KTX_error_code status)
{
    if (status == KTX_OUT_OF_MEMORY) { throw failure{LUCITEX_RESOURCE_LIMIT, "KTX allocation failed."}; }
    if (status == KTX_UNSUPPORTED_FEATURE || status == KTX_UNSUPPORTED_TEXTURE_TYPE)
    {
        throw failure{LUCITEX_UNSUPPORTED, "KTX feature is not supported by this oracle."};
    }
    require(status == KTX_SUCCESS, LUCITEX_REJECTED, "KTX2 input rejected.");
}

uint32_t ktx_u32(const uint8_t* p)
{
    return static_cast<uint32_t>(p[0]) | (static_cast<uint32_t>(p[1]) << 8) |
        (static_cast<uint32_t>(p[2]) << 16) | (static_cast<uint32_t>(p[3]) << 24);
}

uint64_t ktx_u64(const uint8_t* p)
{
    return ktx_u32(p) | (static_cast<uint64_t>(ktx_u32(p + 4)) << 32);
}

void ktx_index(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits)
{
    const auto* data = request.input;
    const uint8_t identifier[] = {0xab, 0x4b, 0x54, 0x58, 0x20, 0x32, 0x30, 0xbb, 0x0d, 0x0a, 0x1a, 0x0a};
    require(request.input_length >= 80 && std::memcmp(data, identifier, 12) == 0, LUCITEX_REJECTED, "Invalid KTX2 header.");
    const auto levels = std::max(1u, ktx_u32(data + 40));
    const auto layers = std::max(1u, ktx_u32(data + 32));
    const auto faces = ktx_u32(data + 36);
    extent(ktx_u32(data + 20), std::max(1u, ktx_u32(data + 24)), std::max(1u, ktx_u32(data + 28)), limits);
    require(faces == 1 || faces == 6, LUCITEX_REJECTED, "Invalid KTX2 face count.");
    require(levels <= limits.max_levels && layers <= limits.max_array_elements, LUCITEX_RESOURCE_LIMIT, "KTX2 topology exceeds the budget.");
    const uint64_t index_end = 80ULL + 24ULL * levels;
    require(index_end <= request.input_length, LUCITEX_REJECTED, "Truncated KTX2 level index.");
    const auto range = [&](uint64_t offset, uint64_t length) {
        require(offset >= index_end && offset <= request.input_length && length <= request.input_length - offset,
            LUCITEX_REJECTED, "KTX2 indexed data is outside the file.");
    };
    const auto dfd_offset = ktx_u32(data + 48);
    const auto dfd_length = ktx_u32(data + 52);
    range(dfd_offset, dfd_length);
    require(dfd_length >= 28 && ktx_u32(data + dfd_offset) == dfd_length, LUCITEX_REJECTED, "Invalid KTX2 DFD size.");
    for (auto field : {56, 64})
    {
        const uint64_t offset = field == 56 ? ktx_u32(data + field) : ktx_u64(data + field);
        const uint64_t length = field == 56 ? ktx_u32(data + field + 4) : ktx_u64(data + field + 8);
        require((offset == 0) == (length == 0), LUCITEX_REJECTED, "Invalid KTX2 metadata index.");
        if (length != 0) { range(offset, length); }
    }
    uint64_t decoded = 0;
    for (uint32_t level = 0; level < levels; ++level)
    {
        const auto* entry = data + 80 + 24ULL * level;
        const auto length = ktx_u64(entry + 8);
        const auto unpacked = ktx_u64(entry + 16);
        range(ktx_u64(entry), length);
        require(unpacked <= limits.max_decoded_bytes - decoded, LUCITEX_RESOURCE_LIMIT, "KTX2 levels exceed the byte budget.");
        decoded += unpacked;
        require(ktx_u32(data + 44) != 0 || length == unpacked, LUCITEX_REJECTED, "Invalid uncompressed KTX2 level length.");
    }
    if (ktx_u32(data + 12) == 0) { return; }
    ktxTextureCreateInfo info{};
    info.vkFormat = ktx_u32(data + 12);
    info.baseWidth = info.baseHeight = info.baseDepth = info.numLevels = info.numLayers = info.numFaces = 1;
    info.numDimensions = 2;
    ktxTexture2* canonical = nullptr;
    ktx_result(ktxTexture2_Create(&info, KTX_TEXTURE_CREATE_NO_STORAGE, &canonical));
    const auto cleanup = [](ktxTexture2* value) { ktxTexture_Destroy(ktxTexture(value)); };
    const std::unique_ptr<ktxTexture2, decltype(cleanup)> owner(canonical, cleanup);
    const auto* expected = reinterpret_cast<const uint8_t*>(canonical->pDfd) + 4;
    const auto* block = data + dfd_offset + 4;
    const auto block_size = ktx_u32(block + 4) >> 16;
    const auto expected_size = ktx_u32(expected + 4) >> 16;
    require(block_size == expected_size && block_size <= dfd_length - 4 && std::memcmp(block + 12, expected + 12, 12) == 0,
        LUCITEX_REJECTED, "KTX2 DFD block layout does not match vkFormat.");
    for (uint32_t offset = 24; offset < block_size; offset += 16)
    {
        require(std::memcmp(block + offset, expected + offset, 3) == 0 &&
            (block[offset + 3] & ~0x10) == (expected[offset + 3] & ~0x10) &&
            std::memcmp(block + offset + 4, expected + offset + 4, 4) == 0,
            LUCITEX_REJECTED, "KTX2 DFD sample layout does not match vkFormat.");
    }
    for (uint32_t level = 0; level < levels; ++level)
    {
        const auto blocks = [&](uint32_t size, uint8_t dimension) { return (std::max(1u, size >> level) + dimension) / (1u + dimension); };
        const uint64_t required = static_cast<uint64_t>(blocks(ktx_u32(data + 20), expected[12])) * blocks(ktx_u32(data + 24), expected[13]) *
            blocks(ktx_u32(data + 28), expected[14]) * expected[16] * layers * faces;
        require(ktx_u64(data + 96 + 24ULL * level) >= required, LUCITEX_REJECTED, "KTX2 level is shorter than its image dimensions require.");
    }
}

void validate_ktx2(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result)
{
    ktx_index(request, limits);
    ktxTexture2* texture = nullptr;
    const auto created = ktxTexture2_CreateFromMemory(request.input, static_cast<ktx_size_t>(request.input_length),
        KTX_TEXTURE_CREATE_NO_FLAGS, &texture);
    const auto cleanup = [](ktxTexture2* value) { ktxTexture_Destroy(ktxTexture(value)); };
    const std::unique_ptr<ktxTexture2, decltype(cleanup)> owner(texture, cleanup);
    ktx_result(created);
    extent(texture->baseWidth, std::max(1u, texture->baseHeight), std::max(1u, texture->baseDepth), limits);
    const auto layers = std::max(1u, texture->numLayers);
    const auto faces = std::max(1u, texture->numFaces);
    require(layers <= limits.max_array_elements && faces <= 6 && texture->numLevels <= limits.max_levels,
        LUCITEX_RESOURCE_LIMIT, "KTX2 topology exceeds the budget.");
    const auto bytes = ktxTexture_GetDataSizeUncompressed(ktxTexture(texture));
    decoded_size(bytes, limits);
    if (texture->supercompressionScheme != KTX_SS_NONE)
    {
        require(texture->dataSize <= limits.max_working_set - bytes, LUCITEX_RESOURCE_LIMIT, "KTX2 working allocation exceeds the budget.");
    }
    ktx_result(ktxTexture_LoadImageData(ktxTexture(texture), nullptr, 0));
    result.width = texture->baseWidth;
    result.height = texture->baseHeight;
    result.decoded_bytes = bytes;
    require(texture->numLevels == 0 || static_cast<uint64_t>(layers) * faces <= UINT64_MAX / texture->numLevels,
        LUCITEX_RESOURCE_LIMIT, "KTX2 subresource count overflows.");
    result.subresources = static_cast<uint64_t>(layers) * faces * texture->numLevels;
}
}
