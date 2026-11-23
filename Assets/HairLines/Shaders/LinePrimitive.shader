Shader "HairLines/Line Primitive"
{
    Properties
    {
        [KeywordEnum(Hair, Radiance, Tangent)] _Shading ("Shading model", Float) = 0

        [KeywordEnum(RGB, Melanin)] _ColorSource ("Colour source", Float) = 0
        [HDR] _RootColor ("Root colour", Color) = (0.055, 0.030, 0.019, 1)
        [HDR] _TipColor ("Tip colour", Color) = (0.115, 0.062, 0.035, 1)
        [PowerSlider(3.0)] _RootEumelanin ("Root eumelanin", Range(0, 8)) = 1.3
        [PowerSlider(3.0)] _TipEumelanin ("Tip eumelanin", Range(0, 8)) = 1.3
        [PowerSlider(3.0)] _RootPheomelanin ("Root pheomelanin", Range(0, 8)) = 0
        [PowerSlider(3.0)] _TipPheomelanin ("Tip pheomelanin", Range(0, 8)) = 0
        [HDR] _ColorTint ("Tint", Color) = (1, 1, 1, 1)
        _Roughness ("Roughness", Range(0.02, 1)) = 0.32
        _RoughnessVariation ("Roughness variation per strand", Range(0, 1)) = 0.15
        _CuticleShift ("Cuticle tilt (rad)", Range(0, 0.2)) = 0.035
        _CuticleShiftVariation ("Cuticle tilt variation per strand (rad)", Range(0, 0.1)) = 0.015
        _Specular ("Specular (R and TRT)", Range(0, 4)) = 1
        _Transmission ("Rim light / transmission (TT)", Range(0, 4)) = 1
        _Scatter ("Multiple scattering (local)", Range(0, 4)) = 1
        _RimLight ("Rim light (forward scattering)", Range(0, 4)) = 1
        _ScatterDepth ("Scatter depth (fibres)", Range(1, 48)) = 8
        _RootShadow ("Root shadow", Range(0, 1)) = 0.35
        _Ambient ("Ambient", Range(0, 4)) = 1

        _TipTaperLength ("Taper length (fraction of strand)", Range(0, 1)) = 0.3
        _TipWidth ("Width at the tip", Range(0, 1)) = 0.15

        _OpacityThreshold ("Opacity threshold (tau)", Range(0, 1)) = 0.5
        [Toggle(_STOCHASTIC_OPACITY)] _StochasticOpacity ("Stochastic opacity test", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "LinePrimitiveSubpixel"

            Cull Off
            ZTest LEqual
            ZWrite On
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #pragma shader_feature_local_fragment _SHADING_HAIR _SHADING_RADIANCE _SHADING_TANGENT
            #pragma shader_feature_local_fragment _ _STOCHASTIC_OPACITY
            #pragma shader_feature_local_fragment _COLORSOURCE_RGB _COLORSOURCE_MELANIN

            #pragma multi_compile _ _HAIR_MOTION_VECTORS

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "HairShadowSampling.hlsl"
            #include "HairDensityVolumeSampling.hlsl"
            #include "HairLineStrand.hlsl"

            StructuredBuffer<float3> _LinePositions;
            StructuredBuffer<float4> _LineColors;
            StructuredBuffer<float>  _LineOpacity;
            StructuredBuffer<float4> _LineStrandDebug;
            StructuredBuffer<uint3> _LineStrandAttributes;
            float _HairHasStrandAttributes;
            float4 _HairStrandRange;
            float4 _HairDebugRange;
            int    _HairDebugMode;
            float _HairHasVertexColor;

            #if defined(_HAIR_MOTION_VECTORS)
            StructuredBuffer<float3> _LinePrevPositions;
            float4x4 _HairPrevObjectToWorld;
            #endif

            float3 LoadTangent(uint index, StrandPoint strand)
            {
                float3 head = _LinePositions[min(index + 1u, strand.last)];
                float3 tail = _LinePositions[max(index, strand.first + 1u) - 1u];
                float3 direction = head - tail;
                return dot(direction, direction) > 1e-16 ? normalize(direction) : float3(0, 1, 0);
            }

            float4 _HairGroupTint;

            float4 _RootColor;
            float4 _TipColor;
            float4 _ColorTint;
            float  _Roughness;
            float  _RoughnessVariation;
            float  _CuticleShift;
            float  _CuticleShiftVariation;
            float  _RootEumelanin;
            float  _TipEumelanin;
            float  _RootPheomelanin;
            float  _TipPheomelanin;
            float  _Specular;
            float  _Transmission;
            float  _Scatter;
            float  _ScatterDepth;
            float  _RimLight;
            float  _RootShadow;
            float  _Ambient;
            float  _OpacityThreshold;
            float  _TipTaperLength;
            float  _TipWidth;

            float  _HairLodCoverageScale;

            float4 _HairSubpixelSize;

            float _HairMainLightSourceAngle;
            float4 _HairLightSources[MAX_VISIBLE_LIGHTS];
            float _HairLightShadowStrengths[MAX_VISIBLE_LIGHTS];
            float4 _HairSHAr, _HairSHAg, _HairSHAb, _HairSHBr, _HairSHBg, _HairSHBb, _HairSHC;
            float4 _HairSkyWeightsPos, _HairSkyWeightsNeg;

            float3 HairSampleSH(float3 N)
            {
                float4 coefficients[7];
                coefficients[0] = _HairSHAr;
                coefficients[1] = _HairSHAg;
                coefficients[2] = _HairSHAb;
                coefficients[3] = _HairSHBr;
                coefficients[4] = _HairSHBg;
                coefficients[5] = _HairSHBb;
                coefficients[6] = _HairSHC;
                return max(SampleSH9(coefficients, N), 0.0);
            }

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 tangentWS  : TEXCOORD1;
                float4 color      : TEXCOORD2;
                nointerpolation float opacity : TEXCOORD3;
                nointerpolation float2 strandRandom : TEXCOORD4;
                nointerpolation float stochasticThreshold : TEXCOORD5;
                nointerpolation float strandIndex : TEXCOORD8;
                float3 gradient   : TEXCOORD9;
                #if defined(_HAIR_MOTION_VECTORS)
                float4 positionCSNoJitter     : TEXCOORD6;
                float4 prevPositionCSNoJitter : TEXCOORD7;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float HashToFloat(uint x)
            {
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return float(x) * (1.0 / 4294967296.0);
            }

            float3 HairUnpackColor(uint packed)
            {
                return SRGBToLinear(float3(packed & 0xFFu, (packed >> 8) & 0xFFu, (packed >> 16) & 0xFFu)
                                    * (1.0 / 255.0));
            }

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                uint index = input.vertexID;

                StrandPoint strand = LoadStrandPoint(index);
                float3 positionOS = _LinePositions[index];
                float3 tangentOS  = LoadTangent(index, strand);

                uint copy = input.instanceID;
                positionOS += HairCopyDisplacement(strand.first, strand.along, copy);

                float3 shade = _HairHasVertexColor > 0.5 ? _LineColors[index].rgb : strand.shade.xxx;
                shade *= HairCopyShade(strand.first, copy);

                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.tangentWS  = TransformObjectToWorldDir(tangentOS);
                output.opacity    = _LineOpacity[index];
                output.stochasticThreshold = HashToFloat(index);

                uint strandSeed = strand.first * 0x9E3779B9u + copy * 0x85EBCA6Bu;
                output.strandRandom = float2(HashToFloat(strandSeed), HashToFloat(strandSeed ^ 0x68E31DA4u));
                output.strandIndex = _HairStrandRange.x +
                                     floor((index - _HairStrandRange.y) / max(_HairStrandRange.z, 1.0));

                float3 rootColor = _RootColor.rgb;
                float3 tipColor = _TipColor.rgb;
                float3 tintMap = float3(1.0, 1.0, 1.0);
                if (_HairHasStrandAttributes > 0.5)
                {
                    uint3 attributes = _LineStrandAttributes[(uint)output.strandIndex];
                    rootColor = (attributes.x >> 24) != 0u ? HairUnpackColor(attributes.x) : rootColor;
                    tipColor = (attributes.y >> 24) != 0u ? HairUnpackColor(attributes.y) : tipColor;
                    tintMap = (attributes.z >> 24) != 0u ? HairUnpackColor(attributes.z) : tintMap;
                }

                output.color      = float4(shade * _HairGroupTint.rgb * tintMap, strand.along);
                output.gradient   = lerp(rootColor, tipColor, strand.along);

                #if defined(_HAIR_MOTION_VECTORS)
                {
                    float3 prevOS = _LinePrevPositions[index]
                                  + HairCopyDisplacement(strand.first, strand.along, copy);
                    float3 prevWS = mul(_HairPrevObjectToWorld, float4(prevOS, 1.0)).xyz;
                    output.positionCSNoJitter     = mul(_NonJitteredViewProjMatrix, float4(output.positionWS, 1.0));
                    output.prevPositionCSNoJitter = mul(_PrevViewProjMatrix, float4(prevWS, 1.0));
                }
                #endif
                return output;
            }

            void HairStrandVariation(float2 random, out float roughness, out float shift)
            {
                roughness = max(_Roughness * (1.0 + (random.x * 2.0 - 1.0) * _RoughnessVariation), 0.02);
                shift = _CuticleShift + (random.y * 2.0 - 1.0) * _CuticleShiftVariation;
            }

            void HairMelaninColors(float eumelanin, float pheomelanin,
                                   out half3 absorptionColor, out half3 diffuseColor)
            {
                const float3 eumelaninSigmaA   = float3(0.419, 0.697, 1.37);
                const float3 pheomelaninSigmaA = float3(0.187, 0.4, 1.05);
                float3 sigmaA = max(eumelanin, 0.0) * eumelaninSigmaA
                              + max(pheomelanin, 0.0) * pheomelaninSigmaA;

                const float betaN = 0.3;
                float denominator = (((((0.245 * betaN) + 5.574) * betaN - 10.73) * betaN + 2.532) * betaN
                                  - 0.215) * betaN + 5.969;

                absorptionColor = (half3)exp(-4.0 * sigmaA);
                diffuseColor    = (half3)exp(-sqrt(sigmaA) * denominator);
            }

            #if defined(_HAIR_MOTION_VECTORS)
            float2 HairMotionVector(float4 positionCS, float4 prevPositionCS)
            {
                float2 velocity = positionCS.xy / positionCS.w - prevPositionCS.xy / prevPositionCS.w;
                #if UNITY_UV_STARTS_AT_TOP
                    velocity.y = -velocity.y;
                #endif
                return velocity * 0.5;
            }
            #endif

            half HairLobe(float beta, float sourceVariance, float theta)
            {
                float b = max(beta, 1e-3);
                float variance = b * b + sourceVariance;
                return (half)(exp(-0.5 * theta * theta / variance) / (2.50662827 * sqrt(variance)));
            }

            half HairFresnel(half f0, half cosTheta)
            {
                half m = saturate(1.0 - cosTheta);
                half m2 = m * m;
                return f0 + (1.0 - f0) * (m2 * m2 * m);
            }

            float HairLightSourceAngle(float4 source, float3 positionWS)
            {
                if (source.w < 0.0)
                    return -source.w;
                float lightDistance = length(source.xyz - positionWS);
                return asin(saturate(source.w / max(lightDistance, 1e-4)));
            }

            uint HairAdditionalLightIndex(uint loopIndex)
            {
                #if USE_CLUSTER_LIGHT_LOOP
                    return loopIndex;
                #else
                    return (uint)GetPerObjectLightIndex(loopIndex);
                #endif
            }

            struct HairLobes
            {
                half3 direct;
                half3 scatter;
            };

            HairLobes HairBsdf(half3 T, half3 V, half3 L, half3 baseColor, half3 scatterAlbedo,
                               float sourceAngle, float roughness, float shift)
            {
                half VoL    = dot(V, L);
                half sinTL  = clamp(dot(T, L), -1.0, 1.0);
                half sinTV  = clamp(dot(T, V), -1.0, 1.0);
                half cosTD  = cos(0.5 * abs(asin(sinTV) - asin(sinTL)));
                cosTD = max(cosTD, (half)1e-3);
                float sourceVariance = 0.25 * sourceAngle * sourceAngle * saturate(1.0 - (float)sinTL * sinTL);

                half3 Lp = L - sinTL * T;
                half3 Vp = V - sinTV * T;
                float lenSq = max(dot(Lp, Lp) * dot(Vp, Vp), 1e-8);
                half cosPhi = clamp(dot(Lp, Vp) * rsqrt(lenSq), -1.0, 1.0);
                half cosHalfPhi = sqrt(saturate(0.5 + 0.5 * cosPhi));

                half nPrime = 1.19 / cosTD + 0.36 * cosTD;

                float r2 = roughness * roughness;
                float betaR   = r2;
                float betaTT  = 0.5 * r2;
                float betaTRT = 2.0 * r2;

                float alphaR   = -2.0 * shift;
                float alphaTT  = shift;
                float alphaTRT = 4.0 * shift;

                const half f0 = 0.0465;
                half3 color = max(baseColor, (half)1e-4);

                half specular     = _Specular;
                half transmission = _Transmission;

                HairLobes lobes;
                lobes.direct = 0.0;
                lobes.scatter = 0.0;

                {
                    half sa, ca;
                    sincos((half)alphaR, sa, ca);
                    half shiftR = 2.0 * sa * (ca * cosHalfPhi * sqrt(saturate(1.0 - sinTV * sinTV)) + sa * sinTV);
                    half Mp = HairLobe(betaR * 1.41421356 * cosHalfPhi, sourceVariance, sinTL + sinTV - shiftR);
                    half Np = 0.25 * cosHalfPhi;
                    half Fp = HairFresnel(f0, sqrt(saturate(0.5 + 0.5 * VoL)));
                    lobes.direct += Mp * Np * Fp * (2.0 * specular);
                }

                {
                    half Mp = HairLobe(betaTT, sourceVariance, sinTL + sinTV - alphaTT);
                    half a = 1.0 / nPrime;
                    half h = cosHalfPhi * (1.0 + a * (0.6 - 0.8 * cosPhi));
                    half f = HairFresnel(f0, cosTD * sqrt(saturate(1.0 - h * h)));
                    half Fp = (1.0 - f) * (1.0 - f);
                    half3 Tp = pow(color, 0.5 * sqrt(saturate(1.0 - h * h * a * a)) / cosTD);
                    half Np = exp(-3.65 * cosPhi - 3.98);
                    lobes.scatter += Mp * Np * Fp * Tp * transmission;
                }

                {
                    half Mp = HairLobe(betaTRT, sourceVariance, sinTL + sinTV - alphaTRT);
                    half f = HairFresnel(f0, cosTD * 0.5);
                    half Fp = (1.0 - f) * (1.0 - f) * f;
                    half3 Tp = pow(color, 0.8 / cosTD);
                    half Np = exp(17.0 * cosPhi - 16.78);
                    lobes.direct += Mp * Np * Fp * Tp * specular;
                }

                if (_Scatter > 0.0)
                {
                    half3 fakeNormal = normalize(V - T * dot(V, T));
                    half wrapped = saturate((dot(fakeNormal, L) + 1.0) * 0.25);
                    lobes.scatter += scatterAlbedo * wrapped * (half)(_Scatter * (1.0 / PI));
                }

                if (_RimLight > 0.0)
                {
                    half forward = saturate(-dot(V, L));
                    forward *= forward;
                    lobes.scatter += scatterAlbedo * forward * (half)(_RimLight * (1.0 / PI));
                }

                lobes.direct = max(lobes.direct, 0.0);
                lobes.scatter = max(lobes.scatter, 0.0);
                return lobes;
            }

            float4 HairOutput(float3 radiance)
            {
                float coverage = max(_HairLodCoverageScale, 1.0);
                return float4(radiance * coverage, coverage);
            }

            float3 HairDebugHue(float hue)
            {
                float3 wrapped = frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0));
                return saturate(abs(wrapped * 6.0 - 3.0) - 1.0);
            }

            float3 HairDebugColor(Varyings input)
            {
                float4 strandDebug = _LineStrandDebug[(uint)max(input.strandIndex, 0.0)];

                if (_HairDebugMode == 1)
                {
                    return HairDebugHue(_HairDebugRange.x * 0.1731 + 0.07);
                }

                if (_HairDebugMode == 2)
                {
                    return float3(strandDebug.x, strandDebug.y, 0.0);
                }

                if (_HairDebugMode == 3)
                {
                    return HairDebugHue(input.strandRandom.x);
                }

                if (_HairDebugMode == 4)
                {
                    uint clumpId = (uint)max(strandDebug.z, 0.0);
                    return strandDebug.z < 0.5
                        ? float3(0.12, 0.12, 0.14)
                        : HairDebugHue(HashToFloat(clumpId * 0x9E3779B9u));
                }

                if (_HairDebugMode == 5)
                {
                    float fraction = saturate(_HairStrandRange.w);
                    return lerp(float3(1.0, 0.16, 0.10), float3(0.16, 1.0, 0.30), fraction);
                }

                if (_HairDebugMode == 6)
                {
                    float compensation = saturate((max(_HairLodCoverageScale, 1.0) - 1.0) * 0.25);
                    return lerp(float3(0.05, 0.06, 0.12), float3(1.0, 0.35, 0.10), compensation);
                }

                if (_HairDebugMode == 7)
                {
                    float share = saturate(strandDebug.w / max(_HairDebugRange.y, 1e-4));
                    return lerp(float3(0.08, 0.10, 0.35), float3(1.0, 0.95, 0.55), share);
                }

                return float3(0.0, 0.0, 0.0);
            }

            float4 ShadeFragment(Varyings input)
            {
                #if defined(_STOCHASTIC_OPACITY)
                    float xi = input.stochasticThreshold;
                #else
                    float xi = _OpacityThreshold;
                #endif
                clip(input.opacity - xi);
                HairTipTaperClip(input.color.a, _TipTaperLength, _TipWidth, input.positionCS.xy);

                float3 T = normalize(input.tangentWS);

                if (_HairDebugMode > 0)
                {
                    return HairOutput(HairDebugColor(input));
                }

                #if defined(_SHADING_TANGENT)
                    return HairOutput(T * 0.5 + 0.5);
                #else
                    float3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);

                    #if defined(_SHADING_RADIANCE)
                        return HairOutput(input.color.rgb * _ColorTint.rgb);
                    #else
                        float along = saturate(input.color.a);
                        half3 tint = input.color.rgb * _ColorTint.rgb;
                        #if defined(_COLORSOURCE_MELANIN)
                            half3 absorptionColor, diffuseColor;
                            HairMelaninColors(lerp(_RootEumelanin, _TipEumelanin, along),
                                              lerp(_RootPheomelanin, _TipPheomelanin, along),
                                              absorptionColor, diffuseColor);
                            half3 baseColor = absorptionColor * tint;
                            half3 scatterColor = diffuseColor * tint;
                            half3 scatterAlbedo = scatterColor;
                        #else
                            half3 baseColor = (half3)input.gradient * tint;
                            half3 scatterColor = baseColor;
                            half3 scatterAlbedo = sqrt(max(baseColor, (half)1e-4));
                        #endif

                        float strandRoughness, strandShift;
                        HairStrandVariation(input.strandRandom, strandRoughness, strandShift);
                        half ao = lerp(_RootShadow, 1.0, saturate(input.color.a));

                        float selfShadow, fibreCount;
                        HairShadowSampleSelf(input.positionWS, input.positionCS.xy, selfShadow, fibreCount);

                        half3 forwardScatter = HairForwardScattering(scatterColor, fibreCount, _ScatterDepth);

                        float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                        Light mainLight = GetMainLight(shadowCoord);
                        half  directAtten  = HairShadowOnStrand(mainLight.shadowAttenuation, selfShadow);
                        half3 scatterAtten = HairShadowOnStrand(mainLight.shadowAttenuation, forwardScatter);

                        HairLobes lobes = HairBsdf((half3)T, (half3)V, (half3)mainLight.direction, baseColor,
                                                   scatterAlbedo, _HairMainLightSourceAngle,
                                                   strandRoughness, strandShift);
                        half3 radiance = (lobes.direct * directAtten
                                        + lobes.scatter * scatterAtten)
                                       * (half3)mainLight.color * (half)mainLight.distanceAttenuation;

                        #if defined(_ADDITIONAL_LIGHTS)
                        {
                            InputData inputData = (InputData)0;
                            inputData.positionWS = input.positionWS;

                            inputData.normalizedScreenSpaceUV = input.positionCS.xy * _HairSubpixelSize.zw;

                            float jitter = InterleavedGradientNoise(input.positionCS.xy, 1);

                            uint lightCount = GetAdditionalLightsCount();
                            LIGHT_LOOP_BEGIN(lightCount)
                                Light light = GetAdditionalLight(lightIndex, input.positionWS);
                                uint hairLightIndex = HairAdditionalLightIndex(lightIndex);
                                float4 lightSource = _HairLightSources[hairLightIndex];
                                half lightShadowStrength = (half)_HairLightShadowStrengths[hairLightIndex];

                                float volumeShadow, volumeFibres;
                                HairVolumeSample(input.positionWS, light.direction, jitter,
                                                 volumeShadow, volumeFibres);

                                half3 volumeScatter = HairForwardScattering(scatterColor, volumeFibres,
                                                                            _ScatterDepth);
                                half  lightDirect  = HairVolumeOnStrand(light.shadowAttenuation,
                                                                        (half)volumeShadow,
                                                                        lightShadowStrength);
                                half3 lightScatter = HairVolumeOnStrand(light.shadowAttenuation,
                                                                        volumeScatter,
                                                                        lightShadowStrength);

                                HairLobes add = HairBsdf((half3)T, (half3)V, (half3)light.direction, baseColor,
                                                         scatterAlbedo,
                                                         HairLightSourceAngle(lightSource, input.positionWS),
                                                         strandRoughness, strandShift);
                                radiance += (add.direct * lightDirect
                                           + add.scatter * lightScatter)
                                          * (half3)light.color * (half)light.distanceAttenuation;
                            LIGHT_LOOP_END
                        }
                        #endif

                        if (_Ambient > 0.0)
                        {
                            float3 fakeNormal = normalize(V - T * dot(V, T));

                            half skyVisibility = 1.0;
                            if (_HairSkyOcclusion > 0.5 && _HairVolumeParams.x > 0.5)
                            {
                                skyVisibility = HairVolumeSkyVisibility(input.positionWS,
                                    InterleavedGradientNoise(input.positionCS.xy, 2),
                                    _HairSkyWeightsPos.xyz, _HairSkyWeightsNeg.xyz);
                            }

                            radiance += (half3)HairSampleSH(fakeNormal) * scatterColor * (half)_Ambient
                                      * skyVisibility;
                        }

                        return HairOutput(max(radiance * ao, 0.0));
                    #endif
                #endif
            }

            #if defined(_HAIR_MOTION_VECTORS)
            struct HairTargets
            {
                float4 color  : SV_Target0;
                float4 motion : SV_Target1;
            };

            HairTargets Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                HairTargets output;
                output.color = ShadeFragment(input);
                output.motion = float4(HairMotionVector(input.positionCSNoJitter,
                                                        input.prevPositionCSNoJitter), 0.0, 0.0);
                return output;
            }
            #else
            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return ShadeFragment(input);
            }
            #endif
            ENDHLSL
        }
    }

    CustomEditor "HairLines.EditorTools.LinePrimitiveShaderGUI"
    Fallback Off
}
