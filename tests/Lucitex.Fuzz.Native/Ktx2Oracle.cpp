#include "NativeChecks.h"
#include <ktx.h>
#include <algorithm>
#include <memory>

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

void validate_ktx2(const lucitex_oracle_request& request, const lucitex_oracle_limits& limits, lucitex_oracle_result& result)
{
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
