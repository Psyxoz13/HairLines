Shader "Hidden/HairLines/LineDepth"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "HairLineStrand.hlsl"

        StructuredBuffer<float3> _LinePositions;
        StructuredBuffer<float>  _LineOpacity;
        TEXTURE2D_X_FLOAT(_HairOpaqueDepth);

        float _DepthOpacityThreshold;
        float _DepthStochasticOpacity;
        float _DepthTipTaperLength;
        float _DepthTipWidth;

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            nointerpolation float opacity : TEXCOORD0;
            nointerpolation float stochasticThreshold : TEXCOORD1;
            float along : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        struct Attributes
        {
            uint vertexID : SV_VertexID;
            uint instanceID : SV_InstanceID;
        };

        float HashToFloat(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return float(value) * (1.0 / 4294967296.0);
        }

        Varyings Vert(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);

            uint index = input.vertexID;

            StrandPoint strand = LoadStrandPoint(index);
            float3 positionOS = _LinePositions[index] +
                                HairCopyDisplacement(strand.first, strand.along, input.instanceID);

            Varyings output = (Varyings)0;
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformWorldToHClip(TransformObjectToWorld(positionOS));
            output.opacity = _LineOpacity[index];
            output.stochasticThreshold = HashToFloat(index);
            output.along = strand.along;
            return output;
        }

        float DepthThreshold(Varyings input)
        {
            return lerp(_DepthOpacityThreshold, input.stochasticThreshold,
                        step(0.5, _DepthStochasticOpacity));
        }

        float4 Frag(Varyings input) : SV_Target
        {
            clip(input.opacity - DepthThreshold(input));
            HairTipTaperClip(input.along, _DepthTipTaperLength, _DepthTipWidth, input.positionCS.xy);
            return 0.0;
        }

        float4 FragMergeNormal(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            clip(input.opacity - DepthThreshold(input));
            HairTipTaperClip(input.along, _DepthTipTaperLength, _DepthTipWidth, input.positionCS.xy);
            float opaqueDepth = LOAD_TEXTURE2D_X(_HairOpaqueDepth, int2(input.positionCS.xy)).r;
            if (input.positionCS.z > opaqueDepth)
            {
                discard;
            }
            return input.positionCS.z;
        }

        float4 FragMergeReversed(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            clip(input.opacity - DepthThreshold(input));
            HairTipTaperClip(input.along, _DepthTipTaperLength, _DepthTipWidth, input.positionCS.xy);
            float opaqueDepth = LOAD_TEXTURE2D_X(_HairOpaqueDepth, int2(input.positionCS.xy)).r;
            if (input.positionCS.z < opaqueDepth)
            {
                discard;
            }
            return input.positionCS.z;
        }
        ENDHLSL

        Pass
        {
            Name "HairLinesDepth"
            Cull Off
            ZTest LEqual
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "HairLinesDepthMergeNormal"
            Cull Off
            ZTest LEqual
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment FragMergeNormal
            ENDHLSL
        }

        Pass
        {
            Name "HairLinesDepthMergeReversed"
            Cull Off
            ZTest Greater
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment FragMergeReversed
            ENDHLSL
        }
    }

    Fallback Off
}
