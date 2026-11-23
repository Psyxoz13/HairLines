#ifndef HAIRLINES_SHADOW_SAMPLING_INCLUDED
#define HAIRLINES_SHADOW_SAMPLING_INCLUDED

TEXTURE2D_ARRAY(_HairShadowDepthTex);
TEXTURE2D_ARRAY(_HairShadowLayerTex);
SAMPLER(sampler_HairShadowLayerTex);

struct HairShadowCaster
{
    float4x4 worldToLight;
    float4   lightDir;
    float4   layers;
    float4   filter;
    float4   density;
};

int _HairShadowCount;
StructuredBuffer<HairShadowCaster> _HairShadowCasters;

float4   _HairShadowParams;

float _HairLodDepthScale;

bool HairShadowProject(int caster, float3 positionWS, out float2 uv, out float depth)
{
    float4 clip = mul(_HairShadowCasters[caster].worldToLight, float4(positionWS, 1.0));
    uv = clip.xy / clip.w * 0.5 + 0.5;
    #if UNITY_UV_STARTS_AT_TOP
        uv.y = 1.0 - uv.y;
    #endif
    depth = dot(positionWS, _HairShadowCasters[caster].lightDir.xyz) + _HairShadowCasters[caster].lightDir.w;

    bool inside = all(uv >= 0.0) && all(uv <= 1.0);
    uv = saturate(uv);
    return inside;
}

float HairShadowFibreCount(int caster, float2 uv, float depth, float depthScale)
{
    float front = SAMPLE_TEXTURE2D_ARRAY_LOD(_HairShadowDepthTex, sampler_HairShadowLayerTex, uv, caster, 0).r;
    float dz = (depth - front - _HairShadowParams.w) * depthScale;
    if (dz <= 0.0)
        return 0.0;

    float4 n = SAMPLE_TEXTURE2D_ARRAY_LOD(_HairShadowLayerTex, sampler_HairShadowLayerTex, uv, caster, 0);
    float4 b = _HairShadowCasters[caster].layers;

    if (dz < b.x) return lerp(0.0, n.x, dz / max(b.x, 1e-6));
    if (dz < b.y) return lerp(n.x, n.y, (dz - b.x) / max(b.y - b.x, 1e-6));
    if (dz < b.z) return lerp(n.y, n.z, (dz - b.y) / max(b.z - b.y, 1e-6));
    if (dz < b.w) return lerp(n.z, n.w, (dz - b.z) / max(b.w - b.z, 1e-6));
    return n.w;
}

float HairShadowExtinction(int caster, float count)
{
    return exp(count * _HairShadowCasters[caster].density.x);
}

static const float2 kHairShadowDisc[4] =
{
    float2( 0.944,  0.106), float2( 0.204,  0.876),
    float2(-0.808,  0.398), float2(-0.470, -0.766),
};

void HairShadowSample(float3 positionWS, float radiusScale, float2 rotation, float depthScale,
                      out float transmittance, out float fibreCount)
{
    transmittance = 1.0;
    fibreCount = 0.0;

    if (_HairShadowParams.z < 0.5)
        return;

    for (int caster = 0; caster < _HairShadowCount; caster++)
    {
        float2 uv;
        float depth;
        if (!HairShadowProject(caster, positionWS, uv, depth))
            continue;

        float n = HairShadowFibreCount(caster, uv, depth, depthScale);
        float sumT = HairShadowExtinction(caster, n);
        float sumN = n;

        float radiusTexels = _HairShadowCasters[caster].filter.z * radiusScale;
        if (radiusTexels > 0.01)
        {
            float2 scale = _HairShadowCasters[caster].filter.xy * radiusTexels;
            [unroll]
            for (int i = 0; i < 4; i++)
            {
                float2 o = kHairShadowDisc[i];
                float2 r = float2(o.x * rotation.x - o.y * rotation.y,
                                  o.x * rotation.y + o.y * rotation.x);
                float2 tapped = saturate(uv + r * scale);
                float ni = HairShadowFibreCount(caster, tapped, depth, depthScale);
                sumT += HairShadowExtinction(caster, ni);
                sumN += ni;
            }
            sumT *= 1.0 / 5.0;
            sumN *= 1.0 / 5.0;
        }

        transmittance *= sumT;
        fibreCount += sumN;
    }

    fibreCount *= _HairShadowParams.x;
}

float2 HairShadowRotation(float2 positionSS)
{
    float angle = InterleavedGradientNoise(positionSS, 0) * 6.2831853;
    float s, c;
    sincos(angle, s, c);
    return float2(c, s);
}

void HairShadowSampleSelf(float3 positionWS, float2 positionSS,
                          out float transmittance, out float fibreCount)
{
    HairShadowSample(positionWS, 0.25, HairShadowRotation(positionSS),
                     max(_HairLodDepthScale, 1e-3), transmittance, fibreCount);
}

half3 HairForwardScattering(half3 strandColor, half fibreCount, half scatterDepth)
{
    return pow(max(strandColor, (half)1e-4), fibreCount / max(scatterDepth, (half)1e-3));
}

half HairShadowOnStrand(half sceneAttenuation, half selfTransmittance)
{
    if (_HairShadowParams.z < 0.5)
        return sceneAttenuation;

    return sceneAttenuation * lerp((half)1.0, selfTransmittance, saturate((half)_HairShadowParams.y));
}

half3 HairShadowOnStrand(half sceneAttenuation, half3 selfTransmittance)
{
    if (_HairShadowParams.z < 0.5)
        return sceneAttenuation.xxx;

    return sceneAttenuation * lerp(half3(1.0, 1.0, 1.0), selfTransmittance,
                                   saturate((half)_HairShadowParams.y));
}

#endif
