#ifndef HAIRLINES_DENSITY_VOLUME_SAMPLING_INCLUDED
#define HAIRLINES_DENSITY_VOLUME_SAMPLING_INCLUDED

TEXTURE3D(_HairVolumeTex);
SAMPLER(sampler_HairVolumeTex);

#define HAIR_VOLUME_STEPS 24

#define HAIR_VOLUME_SELF_BIAS 2.0

struct HairVolumeCaster
{
    float4 origin;
    float4 scale;
    float4 slabU;
    float4 slabW;
};

int _HairVolumeCount;
StructuredBuffer<HairVolumeCaster> _HairVolumeCasters;
float4 _HairVolumeParams;

TEXTURE3D(_HairVolumeConeTexA);
TEXTURE3D(_HairVolumeConeTexB);
SAMPLER(sampler_HairVolumeConeTexA);
TEXTURE3D(_HairSkyVisibilityTex);
float _HairSkyOcclusion;
float4 _HairSkyParams;

bool HairVolumeCoordinates(HairVolumeCaster volume, float3 positionWS, out float3 uvw)
{
    uvw = (positionWS - volume.origin.xyz) * volume.scale.xyz;
    bool inside = all(uvw >= 0.0) && all(uvw <= 1.0);

    uvw.x = clamp(volume.slabU.x + uvw.x * volume.slabU.y, volume.slabU.z, volume.slabU.w);
    uvw.z = clamp(volume.slabW.x + uvw.z * volume.slabW.y, volume.slabW.z, volume.slabW.w);
    uvw.y = saturate(uvw.y);
    return inside;
}

bool HairVolumeRange(HairVolumeCaster volume, float3 positionWS, float3 direction, float maxDistance,
                     out float enter, out float exit)
{
    float3 uvw = (positionWS - volume.origin.xyz) * volume.scale.xyz;
    float3 step = direction * volume.scale.xyz;

    enter = 0.0;
    exit = min(maxDistance, _HairVolumeParams.y);

    [unroll]
    for (int axis = 0; axis < 3; axis++)
    {
        if (abs(step[axis]) < 1e-9)
        {
            if (uvw[axis] < 0.0 || uvw[axis] > 1.0)
                return false;

            continue;
        }

        float inverse = 1.0 / step[axis];
        float near = (0.0 - uvw[axis]) * inverse;
        float far = (1.0 - uvw[axis]) * inverse;

        enter = max(enter, min(near, far));
        exit = min(exit, max(near, far));
    }

    return exit > enter;
}

bool HairVolumeOutOfReach(HairVolumeCaster volume, float3 positionWS, float maxDistance)
{
    float3 uvw = (positionWS - volume.origin.xyz) * volume.scale.xyz;
    float3 outside = max(max(-uvw, uvw - 1.0), 0.0);
    float reach = maxDistance * volume.scale.x;
    return dot(outside, outside) >= reach * reach;
}

float HairVolumeFibreCount(HairVolumeCaster volume, float3 positionWS, float3 toLight, float jitter,
                           int steps, float maxDistance)
{
    float enter, exit;
    if (!HairVolumeRange(volume, positionWS, toLight, maxDistance, enter, exit))
        return 0.0;

    float voxel = max(volume.origin.w, 1e-6);

    enter = max(enter, 0.0) + voxel * HAIR_VOLUME_SELF_BIAS;
    if (exit <= enter)
        return 0.0;

    steps = max(steps, 1);
    float stride = (exit - enter) / steps;

    float travelled = enter + stride * jitter;
    float count = 0.0;

    [loop]
    for (int step = 0; step < steps; step++)
    {
        float3 uvw;
        HairVolumeCoordinates(volume, positionWS + toLight * travelled, uvw);
        count += SAMPLE_TEXTURE3D_LOD(_HairVolumeTex, sampler_HairVolumeTex, uvw, 0).r;
        travelled += stride;
    }

    return count * (stride / voxel);
}

void HairVolumeSampleRay(float3 positionWS, float3 toLight, float jitter, int steps, float maxDistance,
                         out float transmittance, out float fibreCount)
{
    transmittance = 1.0;
    fibreCount = 0.0;

    if (_HairVolumeParams.x < 0.5)
        return;

    float reach = min(maxDistance, _HairVolumeParams.y);
    for (int caster = 0; caster < _HairVolumeCount; caster++)
    {
        HairVolumeCaster volume = _HairVolumeCasters[caster];
        if (HairVolumeOutOfReach(volume, positionWS, reach))
            continue;

        float n = HairVolumeFibreCount(volume, positionWS, toLight, jitter, steps, maxDistance);
        transmittance *= exp(n * volume.scale.w);
        fibreCount += n;
    }

    fibreCount *= _HairVolumeParams.z;
}

void HairVolumeSample(float3 positionWS, float3 toLight, float jitter,
                      out float transmittance, out float fibreCount)
{
    HairVolumeSampleRay(positionWS, toLight, jitter, HAIR_VOLUME_STEPS, _HairVolumeParams.y,
                        transmittance, fibreCount);
}

float HairConeSampleLevel(HairVolumeCaster volume, float3 uvw01, float level, float resolution)
{
    uvw01 = saturate(uvw01);

    uint k = (uint)(level + 0.5);
    float f = 1.0 / (float)(1u << k);
    float spanU = volume.slabU.y;
    float spanW = volume.slabW.y;
    float startU = volume.slabU.x + spanU * (1.0 - 2.0 * f);
    float halfU = 0.5 * spanU / resolution;
    float halfV = 1.0 / resolution;
    float halfW = spanW / resolution;

    float3 uvw;
    uvw.x = clamp(startU + uvw01.x * spanU * f, startU + halfU, startU + spanU * f - halfU);
    uvw.y = clamp(uvw01.y * 2.0 * f, halfV, 2.0 * f - halfV);
    uvw.z = clamp(volume.slabW.x + uvw01.z * spanW * 2.0 * f,
                  volume.slabW.x + halfW, volume.slabW.x + spanW * 2.0 * f - halfW);

    [branch]
    if ((k & 1u) != 0u)
        return SAMPLE_TEXTURE3D_LOD(_HairVolumeConeTexA, sampler_HairVolumeConeTexA, uvw, 0).r;
    return SAMPLE_TEXTURE3D_LOD(_HairVolumeConeTexB, sampler_HairVolumeConeTexA, uvw, 0).r;
}

float HairVolumeConeTransmittance(HairVolumeCaster volume, float3 positionWS, float3 direction, int steps,
                                  float maxDistance, float tanHalfAngle)
{
    float enter, exit;
    if (!HairVolumeRange(volume, positionWS, direction, maxDistance, enter, exit))
        return 1.0;

    float voxel = max(volume.origin.w, 1e-6);
    float resolution = max(_HairVolumeParams.w, 2.0);
    float maxLevel = log2(resolution);

    float t = max(enter, 0.0) + voxel * HAIR_VOLUME_SELF_BIAS;
    float count = 0.0;

    [loop]
    for (int step = 0; step < steps && t < exit; step++)
    {
        float width = 2.0 * t * tanHalfAngle;
        float stride = min(max(0.5 * width, 0.5 * voxel), exit - t);
        float level = clamp(floor(log2(width / voxel) + 1e-3), 1.0, maxLevel);
        float3 at = positionWS + direction * (t + 0.5 * stride);
        float3 uvw01 = (at - volume.origin.xyz) * volume.scale.xyz;
        count += HairConeSampleLevel(volume, uvw01, level, resolution) * (stride / voxel);
        t += stride;
    }

    return exp(count * volume.scale.w);
}

half HairVolumeSkyVisibilityTraced(float3 positionWS, float jitter, float3 skyWeightsPos,
                                   float3 skyWeightsNeg, bool cone)
{
    static const float3 kAxes[3] = { float3(1, 0, 0), float3(0, 1, 0), float3(0, 0, 1) };

    int steps = max((int)_HairSkyParams.x, 1);
    float reach = max(_HairSkyParams.y, 1e-3);
    float tanHalfAngle = max(_HairSkyParams.z, 1e-3);

    float3 throughPos = 1.0;
    float3 throughNeg = 1.0;

    float marchReach = min(reach, _HairVolumeParams.y);
    for (int caster = 0; caster < _HairVolumeCount; caster++)
    {
        HairVolumeCaster volume = _HairVolumeCasters[caster];
        if (HairVolumeOutOfReach(volume, positionWS, marchReach))
            continue;

        [unroll]
        for (int axis = 0; axis < 3; axis++)
        {
            float3 direction = kAxes[axis];
            if (cone)
            {
                throughPos[axis] *= HairVolumeConeTransmittance(volume, positionWS, direction, steps,
                                                                reach, tanHalfAngle);
                throughNeg[axis] *= HairVolumeConeTransmittance(volume, positionWS, -direction, steps,
                                                                reach, tanHalfAngle);
            }
            else
            {
                throughPos[axis] *= exp(HairVolumeFibreCount(volume, positionWS, direction, jitter,
                                                             steps, reach) * volume.scale.w);
                throughNeg[axis] *= exp(HairVolumeFibreCount(volume, positionWS, -direction, jitter,
                                                             steps, reach) * volume.scale.w);
            }
        }
    }

    float3 weightPos = skyWeightsPos + 1e-4;
    float3 weightNeg = skyWeightsNeg + 1e-4;
    float lit = dot(weightPos, throughPos) + dot(weightNeg, throughNeg);
    float total = weightPos.x + weightPos.y + weightPos.z + weightNeg.x + weightNeg.y + weightNeg.z;

    return (half)(lit / total);
}

half HairSkyVisibilityGrid(float3 positionWS)
{
    float grid = max(_HairSkyParams.w, 1.0);

    for (int caster = 0; caster < _HairVolumeCount; caster++)
    {
        HairVolumeCaster volume = _HairVolumeCasters[caster];
        float3 uvw01 = (positionWS - volume.origin.xyz) * volume.scale.xyz;
        if (any(uvw01 < 0.0) || any(uvw01 > 1.0))
            continue;

        float halfU = 0.5 * volume.slabU.y / grid;
        float halfV = 0.5 / grid;
        float halfW = 0.5 * volume.slabW.y / grid;

        float3 uvw;
        uvw.x = clamp(volume.slabU.x + uvw01.x * volume.slabU.y,
                      volume.slabU.x + halfU, volume.slabU.x + volume.slabU.y - halfU);
        uvw.y = clamp(uvw01.y, halfV, 1.0 - halfV);
        uvw.z = clamp(volume.slabW.x + uvw01.z * volume.slabW.y,
                      volume.slabW.x + halfW, volume.slabW.x + volume.slabW.y - halfW);
        return (half)SAMPLE_TEXTURE3D_LOD(_HairSkyVisibilityTex, sampler_HairVolumeConeTexA, uvw, 0).r;
    }

    return 1.0;
}

half HairVolumeSkyVisibility(float3 positionWS, float jitter, float3 skyWeightsPos,
                             float3 skyWeightsNeg)
{
    if (_HairVolumeParams.x < 0.5 || _HairSkyOcclusion < 0.5)
        return 1.0;

    if (_HairSkyOcclusion > 2.5)
        return HairSkyVisibilityGrid(positionWS);

    return HairVolumeSkyVisibilityTraced(positionWS, jitter, skyWeightsPos, skyWeightsNeg,
                                         _HairSkyOcclusion > 1.5);
}

half HairVolumeOnStrand(half sceneAttenuation, half selfTransmittance, half shadowStrength)
{
    if (_HairVolumeParams.x < 0.5)
        return sceneAttenuation;

    return sceneAttenuation * lerp((half)1.0, selfTransmittance, saturate(shadowStrength));
}

half3 HairVolumeOnStrand(half sceneAttenuation, half3 selfTransmittance, half shadowStrength)
{
    if (_HairVolumeParams.x < 0.5)
        return sceneAttenuation.xxx;

    return sceneAttenuation * lerp(half3(1.0, 1.0, 1.0), selfTransmittance,
                                   saturate(shadowStrength));
}

#endif
