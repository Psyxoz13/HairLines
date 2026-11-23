Shader "Hidden/HairLines/MaskPreview"
{
    Properties
    {
        _MaskTex ("Mask", 2D) = "black" {}
        _Opacity ("Opacity", Range(0, 1)) = 0.55
        _Mode ("Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            float _Opacity;
            float _Mode;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float3 HueToRgb(float hue)
            {
                float3 wrapped = frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0));
                return saturate(abs(wrapped * 6.0 - 3.0) - 1.0);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 texel = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv);

                if (_Mode < 0.5)
                {
                    float3 color = lerp(float3(0.05, 0.05, 0.10), float3(0.25, 0.90, 1.00), saturate(texel.r));
                    return float4(color, _Opacity);
                }

                if (_Mode > 1.5)
                {
                    bool painted = texel.g > 0.5;
                    bool grown = texel.r > 0.002;

                    if (painted && !grown)
                    {
                        return float4(1.0, 0.10, 0.12, _Opacity);
                    }

                    if (!grown)
                    {
                        return float4(0.10, 0.10, 0.12, _Opacity * 0.5);
                    }

                    float group = round(texel.r * 255.0) - 1.0;
                    return float4(HueToRgb(frac(group * 0.1731 + 0.07)) * 0.8, _Opacity);
                }

                float2 direction = texel.rg * 2.0 - 1.0;
                float strength = saturate(length(direction));
                float hue = atan2(direction.y, direction.x) / 6.2831853 + 0.5;
                float3 color = lerp(float3(0.12, 0.12, 0.14), HueToRgb(hue), strength);
                return float4(color, _Opacity);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
