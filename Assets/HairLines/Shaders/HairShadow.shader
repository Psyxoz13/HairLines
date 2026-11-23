Shader "Hidden/HairLines/HairShadow"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "HairShadowSampling.hlsl"
        #include "HairLineStrand.hlsl"

        StructuredBuffer<float3> _LinePositions;
        StructuredBuffer<float>  _LineOpacity;

        float4x4 _HairShadowDrawMatrix;
        float4   _HairShadowDrawLightDir;
        float4   _HairShadowDrawLayers;
        float    _HairShadowDrawSlice;
        TEXTURE2D_ARRAY(_HairShadowFilterSource);
        float _HairShadowFilterSlice;

        float _HairShadowOpacityThreshold;
        float _HairShadowFibreWeight;

        struct StrandVaryings
        {
            float4 positionCS : SV_POSITION;
            float  depth      : TEXCOORD0;
            nointerpolation float opacity : TEXCOORD1;
        };

        StrandVaryings VertStrand(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
        {
            uint index = vertexID;

            StrandPoint strand = LoadStrandPoint(index);
            float3 positionOS = _LinePositions[index] +
                                HairCopyDisplacement(strand.first, strand.along, instanceID);
            float3 positionWS = TransformObjectToWorld(positionOS);

            StrandVaryings output;
            output.positionCS = mul(_HairShadowDrawMatrix, float4(positionWS, 1.0));
            output.depth = dot(positionWS, _HairShadowDrawLightDir.xyz) + _HairShadowDrawLightDir.w;
            output.opacity = _LineOpacity[index];
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "HairShadowDepth"
            Blend One One
            BlendOp Min

            HLSLPROGRAM
            #pragma vertex VertStrand
            #pragma fragment FragDepth

            float FragDepth(StrandVaryings input) : SV_Target
            {
                clip(input.opacity - _HairShadowOpacityThreshold);
                return input.depth;
            }
            ENDHLSL
        }

        Pass
        {
            Name "HairShadowDepthMin"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepthMin

            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _HairShadowBlurParams;

            float FragDepthMin(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                int taps = (int)_HairShadowBlurParams.w;
                float result = SAMPLE_TEXTURE2D_ARRAY_LOD(_HairShadowFilterSource, sampler_PointClamp, input.texcoord, _HairShadowFilterSlice, 0).r;

                [loop]
                for (int i = 1; i <= taps; i++)
                {
                    float2 o = _HairShadowBlurParams.xy * i;
                    float2 up = saturate(input.texcoord + o);
                    float2 down = saturate(input.texcoord - o);
                    result = min(result, SAMPLE_TEXTURE2D_ARRAY_LOD(_HairShadowFilterSource, sampler_PointClamp, up, _HairShadowFilterSlice, 0).r);
                    result = min(result, SAMPLE_TEXTURE2D_ARRAY_LOD(_HairShadowFilterSource, sampler_PointClamp, down, _HairShadowFilterSlice, 0).r);
                }

                return result;
            }
            ENDHLSL
        }

        Pass
        {
            Name "HairShadowLayers"
            Blend One One

            HLSLPROGRAM
            #pragma vertex VertStrand
            #pragma fragment FragLayers

            float4 FragLayers(StrandVaryings input) : SV_Target
            {
                clip(input.opacity - _HairShadowOpacityThreshold);

                float front = LOAD_TEXTURE2D_ARRAY(_HairShadowDepthTex, int2(input.positionCS.xy),
                                                   (int)_HairShadowDrawSlice).r;
                float dz = input.depth - front;

                return step(dz, _HairShadowDrawLayers) * max(_HairShadowFibreWeight, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "HairShadowBlur"
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur

            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _HairShadowBlurParams;

            float4 FragBlur(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                int taps = (int)_HairShadowBlurParams.w;
                float4 sum = SAMPLE_TEXTURE2D_ARRAY_LOD(_HairShadowFilterSource, sampler_LinearClamp, input.texcoord, _HairShadowFilterSlice, 0);
                float weightSum = 1.0;

                [loop]
                for (int i = 1; i <= taps; i++)
                {
                    float w = exp(-0.5 * i * i * _HairShadowBlurParams.z);
                    float2 o = _HairShadowBlurParams.xy * i;
                    float2 up = saturate(input.texcoord + o);
                    float2 down = saturate(input.texcoord - o);
                    sum += SAMPLE_TEXTURE2D_ARRAY_LOD(_HairShadowFilterSource, sampler_LinearClamp, up, _HairShadowFilterSlice, 0) * w;
                    sum += SAMPLE_TEXTURE2D_ARRAY_LOD(_HairShadowFilterSource, sampler_LinearClamp, down, _HairShadowFilterSlice, 0) * w;
                    weightSum += 2.0 * w;
                }

                return sum / weightSum;
            }
            ENDHLSL
        }

        Pass
        {
            Name "HairCascadeCaster"
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex VertCascade
            #pragma fragment FragCascade

            float4x4 _HairCascadeViewProj;
            float4 _HairCascadeLight;
            float _HairCascadeCoverage;

            struct CascadeVaryings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation float keep    : TEXCOORD0;
                nointerpolation float opacity : TEXCOORD1;
            };

            CascadeVaryings VertCascade(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                uint index = vertexID;

                StrandPoint strand = LoadStrandPoint(index);
                float3 positionOS = _LinePositions[index] +
                                    HairCopyDisplacement(strand.first, strand.along, instanceID);
                float3 positionWS = TransformObjectToWorld(positionOS);

                positionWS += _HairCascadeLight.xyz * _HairCascadeLight.w;

                CascadeVaryings output;
                output.positionCS = mul(_HairCascadeViewProj, float4(positionWS, 1.0));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                output.keep = HairCopyHash(index * 0x9E3779B9u + instanceID * 0x85EBCA6Bu + 0x5bd1e995u);
                output.opacity = _LineOpacity[index];
                return output;
            }

            void FragCascade(CascadeVaryings input)
            {
                clip(input.opacity - _HairShadowOpacityThreshold);
                clip(_HairCascadeCoverage * _HairShadowFibreWeight - input.keep);
            }
            ENDHLSL
        }

        Pass
        {
            Name "HairDensitySplat"
            Blend One One
            BlendOp Add
            ColorMask R

            HLSLPROGRAM
            #pragma vertex VertSplat
            #pragma fragment FragSplat

            float4 _HairVolumeRange;
            float4 _HairVolumeOrigin;
            float4 _HairSplatParams;

            struct SplatVaryings
            {
                float4 positionCS : SV_POSITION;
                float  metres     : TEXCOORD0;
                float  pointSize  : PSIZE;
            };

            SplatVaryings VertSplat(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                SplatVaryings output;
                output.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                output.metres = 0.0;
                output.pointSize = 1.0;

                uint index = (uint)_HairVolumeRange.x + vertexID;
                StrandPoint strand = LoadStrandPoint(index);
                if (index >= strand.last)
                    return output;

                uint points = max((uint)_HairSplatParams.z, 1u);
                uint copy = instanceID / points;
                uint sub = instanceID - copy * points;

                StrandPoint ahead = LoadStrandPoint(index + 1u);
                float3 from = TransformObjectToWorld(_LinePositions[index] +
                    HairCopyDisplacement(strand.first, strand.along, copy));
                float3 to = TransformObjectToWorld(_LinePositions[index + 1u] +
                    HairCopyDisplacement(ahead.first, ahead.along, copy));
                float3 positionWS = lerp(from, to, (sub + 0.5) / points);

                float resolution = _HairSplatParams.x;
                float3 voxel = (positionWS - _HairVolumeOrigin.xyz) / _HairVolumeOrigin.w;
                if (any(voxel < 0.0) || any(voxel >= resolution))
                    return output;

                float tiles = max(_HairSplatParams.y, 1.0);
                float slice = floor(voxel.z);
                float2 tile = float2(slice - tiles * floor(slice / tiles), floor(slice / tiles));
                float2 texel = tile * resolution + floor(voxel.xy);
                float2 ndc = (texel + 0.5) / (tiles * resolution) * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                    ndc.y = -ndc.y;
                #endif

                output.positionCS = float4(ndc, 0.5, 1.0);
                output.metres = length(to - from) / points * _HairSplatParams.w;
                return output;
            }

            float FragSplat(float4 positionCS : SV_POSITION, float metres : TEXCOORD0) : SV_Target
            {
                return metres;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
