Shader "Hidden/HairLines/LineResolve"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4 _HairTexSize;
        float  _FilterTaps;
        float  _FilterInvSigmaSq;
        float  _InvSupersample;

        TEXTURE2D_X_FLOAT(_HairSubpixelDepth);
        TEXTURE2D_X_FLOAT(_HairOpaqueDepth);
        float  _HairResolveDepthAware;
        ENDHLSL

        Pass
        {
            Name "HairLinesDepthPrime"

            ZTest Always
            ZWrite On
            Blend Off
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepthPrime

            float FragDepthPrime(Varyings input) : SV_Depth
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).r;
            }
            ENDHLSL
        }

        Pass
        {
            Name "HairLinesResolve"

            ZTest Always
            ZWrite Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragResolve

            #if UNITY_REVERSED_Z
                #define HAIR_NEARER(a, b) ((a) > (b))
                #define HAIR_FARTHER_OF(a, b) min(a, b)
                #define HAIR_NEAREST_DEPTH 1.0
            #else
                #define HAIR_NEARER(a, b) ((a) < (b))
                #define HAIR_FARTHER_OF(a, b) max(a, b)
                #define HAIR_NEAREST_DEPTH 0.0
            #endif

            float4 FragResolve(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 center = input.texcoord * _HairTexSize.xy;

                int taps = max((int)_FilterTaps, 1);
                int2 origin = int2(floor(center - 0.5 * taps));
                int2 maxCoord = int2(_HairTexSize.xy) - 1;

                bool depthAware = _HairResolveDepthAware > 0.5;
                float pixelScene = 0.0;
                float farthestHair = HAIR_NEAREST_DEPTH;
                bool anyHair = false;

                if (depthAware)
                {
                    int2 pixel = int2(floor(center * _InvSupersample));
                    pixelScene = LOAD_TEXTURE2D_X(_HairOpaqueDepth, pixel).r;

                    [loop] for (int hy = 0; hy < taps; ++hy)
                    {
                        [loop] for (int hx = 0; hx < taps; ++hx)
                        {
                            int2 coord = clamp(origin + int2(hx, hy), int2(0, 0), maxCoord);
                            if (LOAD_TEXTURE2D_X(_BlitTexture, coord).a <= 0.0)
                                continue;

                            float z = LOAD_TEXTURE2D_X(_HairSubpixelDepth, coord).r;
                            if (HAIR_NEARER(pixelScene, z))
                                continue;

                            farthestHair = HAIR_FARTHER_OF(farthestHair, z);
                            anyHair = true;
                        }
                    }

                    if (!anyHair)
                        return 0.0;
                }

                float4 sum = 0.0;
                float weightSum = 0.0;

                [loop] for (int y = 0; y < taps; ++y)
                {
                    [loop] for (int x = 0; x < taps; ++x)
                    {
                        int2 coord = clamp(origin + int2(x, y), int2(0, 0), maxCoord);
                        float4 value = LOAD_TEXTURE2D_X(_BlitTexture, coord);

                        if (depthAware)
                        {
                            if (value.a > 0.0)
                            {
                                float z = LOAD_TEXTURE2D_X(_HairSubpixelDepth, coord).r;
                                if (HAIR_NEARER(pixelScene, z))
                                    continue;
                            }
                            else
                            {
                                int2 parent = int2(floor(float2(coord) * _InvSupersample));
                                float tapScene = LOAD_TEXTURE2D_X(_HairOpaqueDepth, parent).r;
                                if (HAIR_NEARER(tapScene, farthestHair))
                                    continue;
                            }
                        }

                        float2 d = (float2(origin + int2(x, y)) + 0.5 - center) * _InvSupersample;
                        float w = exp(-0.5 * dot(d, d) * _FilterInvSigmaSq);
                        sum += value * w;
                        weightSum += w;
                    }
                }

                float4 filtered = sum / max(weightSum, 1e-6);

                float coverage = min(filtered.a, 1.0);
                float scale = filtered.a > 1e-6 ? coverage / filtered.a : 1.0;
                return float4(filtered.rgb * scale, coverage);
            }
            ENDHLSL
        }

        Pass
        {
            Name "HairLinesMotionVectors"

            ZTest Always
            ZWrite Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMotion

            #if UNITY_REVERSED_Z
                #define HAIR_NEARER(a, b) ((a) > (b))
            #else
                #define HAIR_NEARER(a, b) ((a) < (b))
            #endif

            TEXTURE2D_X_FLOAT(_HairSubpixelMotion);
            float _HairMotionVectorMode;

            float4 FragMotion(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 center = input.texcoord * _HairTexSize.xy;
                int s = max((int)round(1.0 / _InvSupersample), 1);
                int2 origin = int2(floor(center - 0.5 * s));
                int2 maxCoord = int2(_HairTexSize.xy) - 1;
                bool fastest = _HairMotionVectorMode > 1.5;

                bool found = false;
                float bestDepth = 0.0;
                float bestSpeed = 0.0;
                float2 best = 0.0;

                [loop] for (int y = 0; y < s; ++y)
                {
                    [loop] for (int x = 0; x < s; ++x)
                    {
                        int2 coord = clamp(origin + int2(x, y), int2(0, 0), maxCoord);
                        if (LOAD_TEXTURE2D_X(_BlitTexture, coord).a <= 0.0)
                            continue;

                        float2 motion = LOAD_TEXTURE2D_X(_HairSubpixelMotion, coord).xy;
                        if (fastest)
                        {
                            float speed = dot(motion, motion);
                            if (!found || speed > bestSpeed)
                            {
                                bestSpeed = speed;
                                best = motion;
                            }
                        }
                        else
                        {
                            float z = LOAD_TEXTURE2D_X(_HairSubpixelDepth, coord).r;
                            if (!found || HAIR_NEARER(z, bestDepth))
                            {
                                bestDepth = z;
                                best = motion;
                            }
                        }
                        found = true;
                    }
                }

                if (!found)
                    discard;

                return float4(best, 0.0, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
