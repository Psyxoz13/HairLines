#ifndef HAIRLINES_LINE_STRAND_INCLUDED
#define HAIRLINES_LINE_STRAND_INCLUDED

StructuredBuffer<uint> _LineStrandInfo;

struct StrandPoint
{
    uint first;
    uint last;
    float along;
    float shade;
};

StrandPoint LoadStrandPoint(uint index)
{
    uint packed = _LineStrandInfo[index];
    uint local = packed & 0xFFFu;
    uint count = max((packed >> 12) & 0xFFFu, 1u);

    StrandPoint result;
    result.first = index - local;
    result.last = result.first + count - 1u;
    result.along = count > 1u ? local / (float)(count - 1u) : 0.0;
    result.shade = ((packed >> 24) & 0xFFu) * (1.0 / 127.0);
    return result;
}

StructuredBuffer<float3> _HairCopyOffsets;

float4 _HairCopyParams;
float4 _HairCopySpread;

float HairCopyHash(uint value)
{
    value ^= value >> 16;
    value *= 0x7feb352du;
    value ^= value >> 15;
    value *= 0x846ca68bu;
    value ^= value >> 16;
    return float(value) * (1.0 / 4294967296.0);
}

uint HairCopySlot(uint strandFirst, uint instanceID)
{
    uint copies = (uint)_HairCopyParams.w;
    uint stride = max((uint)_HairCopyParams.z, 1u);
    uint ordinal = (strandFirst - (uint)_HairCopyParams.y) / stride;
    return (uint)_HairCopyParams.x + ordinal * copies + (instanceID - 1u);
}

float3 HairCopyDisplacement(uint strandFirst, float along, uint instanceID)
{
    if (instanceID == 0u || (uint)_HairCopyParams.w == 0u)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 direction = _HairCopyOffsets[HairCopySlot(strandFirst, instanceID)];
    return direction * lerp(_HairCopySpread.x, _HairCopySpread.y, along);
}

float HairCopyShade(uint strandFirst, uint instanceID)
{
    if (instanceID == 0u || _HairCopySpread.z <= 0.0)
    {
        return 1.0;
    }

    float noise = HairCopyHash(strandFirst * 0x9E3779B9u + instanceID * 0x85EBCA6Bu);
    return max(0.0, 1.0 + (noise * 2.0 - 1.0) * _HairCopySpread.z);
}

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

float HairTipWidth(float along, float taperLength, float tipWidth)
{
    float taperStart = 1.0 - taperLength;
    float taper = saturate((along - taperStart) / max(taperLength, 1e-4));
    return lerp(1.0, tipWidth, taper);
}

void HairTipTaperClip(float along, float taperLength, float tipWidth, float2 subpixel)
{
    clip(HairTipWidth(along, taperLength, tipWidth) - InterleavedGradientNoise(subpixel, 0));
}

#endif
