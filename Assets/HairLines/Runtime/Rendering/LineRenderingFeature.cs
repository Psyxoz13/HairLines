using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using Unity.Profiling;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace HairLines
{
    [DisallowMultipleRendererFeature("Hair Lines")]
    public class LineRenderingFeature : ScriptableRendererFeature
    {
        internal static bool HasActiveShadowDemand =>
            _shadowDemandEnabled && _shadowDemandFrame >= Time.frameCount - 1;

        internal static bool HasActiveSceneShadowDemand =>
            _sceneShadowDemandEnabled && _sceneShadowDemandFrame >= Time.frameCount - 1;

        internal static bool HasActiveHairDemand =>
            _hairDemandEnabled && _hairDemandFrame >= Time.frameCount - 1;

        internal static bool HasActiveMotionVectorDemand =>
            _motionVectorDemandFrame >= Time.frameCount - 1;

        public static class ShadowDiagnostics
        {
            public static int CasterCount;
            public static int TileSize;
            public static float HalfSize;
            public static float SoftnessTexels;
        }

        public static class VolumeDiagnostics
        {
            public static int CasterCount;
            public static int Resolution;
            public static float VoxelSize;
        }

        public HairRenderSettings Settings => _settings;

        [SerializeField, FormerlySerializedAs("settings")]
        private HairRenderSettings _settings = new HairRenderSettings();

#pragma warning disable 0649
        [SerializeField, HideInInspector, FormerlySerializedAs("resolveShader")]
        private Shader _resolveShader;

        [SerializeField, HideInInspector, FormerlySerializedAs("shadowShader")]
        private Shader _shadowShader;

        [SerializeField, HideInInspector, FormerlySerializedAs("depthShader")]
        private Shader _depthShader;

        [SerializeField, HideInInspector]
        private ComputeShader _densityShader;

        [SerializeField, HideInInspector]
        private ComputeShader _receiversShader;
#pragma warning restore 0649

        private Material _resolveMaterial;
        private Material _depthMaterial;
        private Material _shadowMaterial;
        private LinePrimitivePass _pass;
        private LinePrimitivePass _earlyDepthPass;
        private LinePrimitivePass _shadowPass;
        private LinePrimitivePass _motionVectorPass;
        private LinePrimitivePass _sceneViewDepthPass;
        private static int _motionVectorDemandFrame = int.MinValue;
        private static int _shadowDemandFrame = int.MinValue;
        private static bool _shadowDemandEnabled;
        private static int _sceneShadowDemandFrame = int.MinValue;
        private static bool _sceneShadowDemandEnabled;
        private static int _hairDemandFrame = int.MinValue;
        private static bool _hairDemandEnabled;

        private static readonly ProfilerMarker _addRenderPassesMarker =
            new ProfilerMarker("HairLines.Rendering.AddRenderPasses");
        private static readonly ProfilerMarker _recordGraphMarker =
            new ProfilerMarker("HairLines.Rendering.RecordRenderGraph");
        private static readonly ProfilerMarker _collectVisibleMarker =
            new ProfilerMarker("HairLines.Rendering.CollectVisible");
        private static readonly ProfilerMarker _recordShadowsMarker =
            new ProfilerMarker("HairLines.Rendering.RecordShadows");
        private static readonly ProfilerMarker _recordHairMarker =
            new ProfilerMarker("HairLines.Rendering.RecordHair");
        private static readonly ProfilerMarker _rasterizeLinesMarker =
            new ProfilerMarker("HairLines.Rendering.RasterizeLines");
        private static readonly ProfilerMarker _resolveMarker =
            new ProfilerMarker("HairLines.Rendering.Resolve");
        private static readonly ProfilerMarker _recordShadowMapMarker =
            new ProfilerMarker("HairLines.Rendering.RecordShadowMap");
        private static readonly ProfilerMarker _recordFilterMarker =
            new ProfilerMarker("HairLines.Rendering.RecordShadowFilter");
        private static readonly ProfilerMarker _drawShadowStrandsMarker =
            new ProfilerMarker("HairLines.Rendering.DrawShadowStrands");
        private static readonly ProfilerMarker _collectShadowCastersMarker =
            new ProfilerMarker("HairLines.Rendering.CollectShadowCasters");
        private static readonly ProfilerMarker _recordVolumeMarker =
            new ProfilerMarker("HairLines.Rendering.RecordDensityVolume");
        private static readonly ProfilingSampler _splatVolumeSampler =
            new ProfilingSampler("HairLines.Rendering.DensityVolume.Splat");
        private static readonly ProfilingSampler _resolveVolumeSampler =
            new ProfilingSampler("HairLines.Rendering.DensityVolume.Resolve");
        private static readonly ProfilingSampler _levelsVolumeSampler =
            new ProfilingSampler("HairLines.Rendering.DensityVolume.SkyOcclusion");
        private static readonly ProfilingSampler _gpuShadowDepthSampler =
            new ProfilingSampler("HairLines.Rendering.GPU.ShadowDepth");
        private static readonly ProfilingSampler _gpuShadowLayersSampler =
            new ProfilingSampler("HairLines.Rendering.GPU.ShadowLayers");
        private static readonly ProfilingSampler _gpuShadowFilterSampler =
            new ProfilingSampler("HairLines.Rendering.GPU.ShadowFilter");
        private static readonly ProfilingSampler _gpuSubpixelDepthSampler =
            new ProfilingSampler("HairLines.Rendering.GPU.SubpixelDepthPrepass");
        private static readonly ProfilingSampler _gpuRasterSampler =
            new ProfilingSampler("HairLines.Rendering.GPU.Raster");
        private static readonly ProfilingSampler _gpuDepthPublishSampler =
            new ProfilingSampler("HairLines.Rendering.GPU.DepthPublish");
        private static readonly ProfilingSampler _gpuEarlyDepthSampler =
            new ProfilingSampler("HairLines.Rendering.GPU.EarlyDepth");
        private static readonly ProfilingSampler _gpuResolveSampler =
            new ProfilingSampler("HairLines.Rendering.GPU.Resolve");
        private static readonly ProfilingSampler _gpuCascadeCastersSampler =
            new ProfilingSampler("HairLines.Rendering.GPU.CascadeCasters");

        private static readonly ProfilingSampler _groupHairSampler = new ProfilingSampler("HairLines");
        private static readonly ProfilingSampler _groupShadowsSampler =
            new ProfilingSampler("HairLines Shadows");
        private static readonly ProfilingSampler _groupShadowMapSampler =
            new ProfilingSampler("HairLines Shadow Map");
        private static readonly ProfilingSampler _groupDensityVolumeSampler =
            new ProfilingSampler("HairLines Density Volume");
        private static readonly ProfilingSampler _groupCascadeCastersSampler =
            new ProfilingSampler("HairLines Cascade Casters");
        private static readonly ProfilingSampler _groupEarlyDepthSampler =
            new ProfilingSampler("HairLines Early Depth");
        private static readonly ProfilingSampler _groupMotionVectorsSampler =
            new ProfilingSampler("HairLines Motion Vectors");

        public override void Create()
        {
            _pass ??= new LinePrimitivePass();
            _earlyDepthPass ??= new LinePrimitivePass();
            _shadowPass ??= new LinePrimitivePass();
            _motionVectorPass ??= new LinePrimitivePass();
            _sceneViewDepthPass ??= new LinePrimitivePass();
        }

#if UNITY_EDITOR
        public static bool HideHairFromSceneViewDepth { get; set; }

        private static readonly Dictionary<Camera, (RenderTexture Texture, RTHandle Handle)> _sceneViewDepthCopies =
            new Dictionary<Camera, (RenderTexture, RTHandle)>();

        public static RenderTexture GetSceneViewDepthWithoutHair(Camera camera)
        {
            return camera != null && _sceneViewDepthCopies.TryGetValue(camera, out var copy) ? copy.Texture : null;
        }

        private static RTHandle SceneViewDepthCopy(Camera camera, int width, int height)
        {
            if (_sceneViewDepthCopies.TryGetValue(camera, out var copy) && copy.Texture != null &&
                copy.Texture.width == width && copy.Texture.height == height)
            {
                return copy.Handle;
            }

            ReleaseSceneViewDepthCopy(camera);
            var texture = new RenderTexture(width, height, 0, GraphicsFormat.R32_SFloat)
            {
                name = "HairLinesSceneViewDepthWithoutHair",
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.Create();
            RTHandle handle = RTHandles.Alloc(texture);
            _sceneViewDepthCopies[camera] = (texture, handle);
            return handle;
        }

        private static void ReleaseSceneViewDepthCopy(Camera camera)
        {
            if (!_sceneViewDepthCopies.TryGetValue(camera, out var copy))
            {
                return;
            }

            copy.Handle?.Release();
            CoreUtils.Destroy(copy.Texture);
            _sceneViewDepthCopies.Remove(camera);
        }
#endif

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            using ProfilerMarker.AutoScope scope = _addRenderPassesMarker.Auto();
            Camera camera = renderingData.cameraData.camera;
            if (camera == null ||
                (camera.cameraType == CameraType.Preview && !LinePrimitiveRenderer.DrawsForPreview(camera)) ||
                camera.cameraType == CameraType.Reflection ||
                (camera.cameraType == CameraType.SceneView && !_settings.RenderInSceneView))
            {
                return;
            }

            camera.depthTextureMode |= DepthTextureMode.Depth;

            Shader.SetGlobalInt(LineShaderIds.HairDebugMode, (int)_settings.DebugMode);

            RegisterShadowDemand(Settings.CastShadows);
            RegisterSceneShadowDemand(Settings.CastShadows && Settings.CastShadowsOnScene &&
                                      HasSceneShadowLight(renderingData));
            RegisterHairDemand();
            if (LinePrimitiveRenderer.Instances.Count == 0)
            {
                return;
            }

            if (_resolveMaterial == null)
            {
                var shader = _resolveShader != null ? _resolveShader : Shader.Find(LineShaderIds.ResolveShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[HairLines] Shader '{LineShaderIds.ResolveShaderName}' not found; the feature is disabled.");
                    return;
                }
                _resolveMaterial = CoreUtils.CreateEngineMaterial(shader);
            }

            if (_depthMaterial == null)
            {
                Shader depthShader = _depthShader != null
                    ? _depthShader
                    : Shader.Find(LineShaderIds.DepthShaderName);
                if (depthShader != null)
                {
                    _depthMaterial = CoreUtils.CreateEngineMaterial(depthShader);
                }
            }

            if (_shadowMaterial == null)
            {
                var shader = _shadowShader != null ? _shadowShader : Shader.Find(LineShaderIds.ShadowShaderName);
                if (shader != null)
                {
                    _shadowMaterial = CoreUtils.CreateEngineMaterial(shader);
                }
            }

            bool allowHairShadows = Settings.CastShadows;

            if (Settings.VolumeShadows && _densityShader == null)
            {
                _densityShader = FindComputeShader("HairDensityVolume");
            }

            bool sceneShadowsWanted = allowHairShadows && Settings.CastShadowsOnScene;
            if (sceneShadowsWanted && _receiversShader == null)
            {
                _receiversShader = FindComputeShader("HairShadowReceivers");
            }

            if (allowHairShadows || Settings.VolumeShadows)
            {
                _shadowPass.Setup(LinePrimitivePass.Phase.Shadows, _settings, _resolveMaterial, _shadowMaterial,
                                  density: _densityShader);
                _shadowPass.renderPassEvent = RenderPassEvent.AfterRenderingShadows;
                renderer.EnqueuePass(_shadowPass);
            }

            if (!allowHairShadows)
            {
                LinePrimitivePass.DisableShadowGlobals();
            }

            if (!Settings.VolumeShadows)
            {
                LinePrimitivePass.DisableVolumeGlobals();
            }

            if (_depthMaterial != null &&
                Settings.InjectionPoint >= RenderPassEvent.AfterRenderingOpaques)
            {
                _earlyDepthPass.Setup(LinePrimitivePass.Phase.EarlyDepth, _settings,
                                      _resolveMaterial, _shadowMaterial, _depthMaterial);
                _earlyDepthPass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
                renderer.EnqueuePass(_earlyDepthPass);
            }

            _pass.Setup(LinePrimitivePass.Phase.Hair, _settings, _resolveMaterial, _shadowMaterial,
                        _depthMaterial);
            _pass.SetShadowReceivers(sceneShadowsWanted ? _receiversShader : null);
            _pass.renderPassEvent = Settings.InjectionPoint;
            renderer.EnqueuePass(_pass);

            if (Settings.MotionVectors != HairMotionVectors.Off &&
                Settings.InjectionPoint <= RenderPassEvent.BeforeRenderingPostProcessing)
            {
                _motionVectorPass.Setup(LinePrimitivePass.Phase.MotionVectors, _settings,
                                        _resolveMaterial, _shadowMaterial);
                _motionVectorPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                renderer.EnqueuePass(_motionVectorPass);
            }

#if UNITY_EDITOR
            if (HideHairFromSceneViewDepth && camera.cameraType == CameraType.SceneView &&
                Settings.InjectionPoint >= RenderPassEvent.AfterRenderingOpaques)
            {
                _sceneViewDepthPass.Setup(LinePrimitivePass.Phase.SceneViewDepthRestore, _settings,
                                          _resolveMaterial, _shadowMaterial);
                _sceneViewDepthPass.renderPassEvent = RenderPassEvent.AfterRendering;
                renderer.EnqueuePass(_sceneViewDepthPass);
            }
#endif
        }

        private static void RegisterMotionVectorDemand()
        {
            _motionVectorDemandFrame = Time.frameCount;
        }

        private static void RegisterShadowDemand(bool enabled)
        {
            if (_shadowDemandFrame != Time.frameCount)
            {
                _shadowDemandFrame = Time.frameCount;
                _shadowDemandEnabled = false;
            }

            _shadowDemandEnabled |= enabled;
        }

        private static void RegisterSceneShadowDemand(bool enabled)
        {
            if (_sceneShadowDemandFrame != Time.frameCount)
            {
                _sceneShadowDemandFrame = Time.frameCount;
                _sceneShadowDemandEnabled = false;
            }

            _sceneShadowDemandEnabled |= enabled;
        }

        private static bool HasSceneShadowLight(RenderingData renderingData)
        {
            LightData lightData = renderingData.lightData;
            if (lightData.mainLightIndex < 0 || lightData.mainLightIndex >= lightData.visibleLights.Length)
            {
                return false;
            }

            VisibleLight visibleLight = lightData.visibleLights[lightData.mainLightIndex];
            if (visibleLight.lightType != LightType.Directional)
            {
                return false;
            }

            Light mainLight = visibleLight.light;
            return mainLight != null && mainLight.shadows != LightShadows.None && mainLight.shadowStrength > 0f;
        }

        private static void RegisterHairDemand()
        {
            if (_hairDemandFrame != Time.frameCount)
            {
                _hairDemandFrame = Time.frameCount;
                _hairDemandEnabled = false;
            }

            _hairDemandEnabled = true;
        }

        private static ComputeShader FindComputeShader(string name)
        {
#if UNITY_EDITOR
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets(name + " t:ComputeShader"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                if (shader != null && shader.name == name)
                {
                    return shader;
                }
            }
#endif
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            _earlyDepthPass?.Dispose();
            _shadowPass?.Dispose();
            _motionVectorPass?.Dispose();
            _sceneViewDepthPass?.Dispose();
            _sceneViewDepthPass = null;
#if UNITY_EDITOR
            foreach (Camera camera in new List<Camera>(_sceneViewDepthCopies.Keys))
            {
                ReleaseSceneViewDepthCopy(camera);
            }
#endif
            _pass = null;
            _earlyDepthPass = null;
            _shadowPass = null;
            _motionVectorPass = null;
            CoreUtils.Destroy(_resolveMaterial);
            _resolveMaterial = null;
            CoreUtils.Destroy(_depthMaterial);
            _depthMaterial = null;
            CoreUtils.Destroy(_shadowMaterial);
            _shadowMaterial = null;
            LinePrimitivePass.DisableShadowGlobals();
            HairShadowReceivers.ReleaseAll();
        }

        private static class MainLightShadowKeywords
        {
            private static readonly GlobalKeyword _mainLightShadows =
                GlobalKeyword.Create(ShaderKeywordStrings.MainLightShadows);
            private static readonly GlobalKeyword _mainLightShadowCascades =
                GlobalKeyword.Create(ShaderKeywordStrings.MainLightShadowCascades);
            private static readonly GlobalKeyword _mainLightShadowScreen =
                GlobalKeyword.Create(ShaderKeywordStrings.MainLightShadowScreen);

            public static void Restore(RasterCommandBuffer cmd, UniversalShadowData shadowData)
            {
                bool mainLightShadows = shadowData != null && shadowData.supportsMainLightShadows;
                int cascades = shadowData != null ? shadowData.mainLightShadowCascadesCount : 1;
                cmd.SetKeyword(_mainLightShadowScreen, false);
                cmd.SetKeyword(_mainLightShadows, mainLightShadows && cascades == 1);
                cmd.SetKeyword(_mainLightShadowCascades, mainLightShadows && cascades > 1);
            }
        }

        private sealed class HairFrameResources : ContextItem
        {
            internal TextureHandle opaqueDepth = TextureHandle.nullHandle;
            internal TextureHandle sceneViewDepthWithoutHair = TextureHandle.nullHandle;

            internal TextureHandle subpixelColor = TextureHandle.nullHandle;
            internal TextureHandle subpixelDepth = TextureHandle.nullHandle;
            internal TextureHandle subpixelMotion = TextureHandle.nullHandle;
            internal Vector4 subpixelSize;
            internal int supersample = 1;

            internal TextureHandle mainShadows = TextureHandle.nullHandle;
            internal TextureHandle mainShadowsWithoutHair = TextureHandle.nullHandle;

            public HairFrameResources()
            {
            }

            public override void Reset()
            {
                opaqueDepth = TextureHandle.nullHandle;
                sceneViewDepthWithoutHair = TextureHandle.nullHandle;
                subpixelColor = TextureHandle.nullHandle;
                subpixelDepth = TextureHandle.nullHandle;
                subpixelMotion = TextureHandle.nullHandle;
                subpixelSize = Vector4.zero;
                supersample = 1;
                mainShadows = TextureHandle.nullHandle;
                mainShadowsWithoutHair = TextureHandle.nullHandle;
            }
        }

        private class LinePrimitivePass : ScriptableRenderPass
        {
            private const int DepthPrimePass = 0;
            private const int ResolvePass = 1;
            private const int MotionVectorResolvePass = 2;

            private static readonly GlobalKeyword _motionVectorsKeyword =
                GlobalKeyword.Create(LineShaderIds.MotionVectorsKeyword);
            private const int DepthOnlyPass = 0;
            private const int EarlyDepthMergeNormalPass = 1;
            private const int EarlyDepthMergeReversedPass = 2;

#if UNITY_ANDROID || UNITY_IOS || UNITY_VISIONOS || UNITY_SWITCH || UNITY_WEBGL
            private const bool PrepassIsWorthIt = false;
#else
            private const bool PrepassIsWorthIt = true;
#endif

            private const int ShadowDepthPass = 0;
            private const int ShadowDepthMinPass = 1;
            private const int ShadowLayerPass = 2;
            private const int ShadowBlurPass = 3;
            private const int CascadeCasterPass = 4;
            private const int VolumeSplatPass = 5;

            private const int ShadowLayerCount = 4;

            private const float OpacityThreshold = 0.5f;

            public enum Phase
            {
                Shadows,
                EarlyDepth,
                Hair,
                MotionVectors,
                SceneViewDepthRestore,
            }

            private Phase _phase;
            private HairRenderSettings _settings;
            private Material _resolve;
            private Material _depth;
            private Material _shadow;
            private CopyDepthPass _copyCameraDepth;
            private CopyDepthPass _copyCameraDepthToDepth;
            private Shader _copyDepthShader;
            private bool _copyDepthWarningIssued;
            private readonly Plane[] _frustumPlanes = new Plane[6];
            private readonly List<LinePrimitiveRenderer> _visibleScratch =
                new List<LinePrimitiveRenderer>();
            private readonly List<LinePrimitiveRenderer> _shadowCasterScratch =
                new List<LinePrimitiveRenderer>();
            private readonly List<LinePrimitiveRenderer> _volumeCasterScratch =
                new List<LinePrimitiveRenderer>();

            private static bool _shadowGlobalsEnabled;

            private class LinesPassData
            {
                internal List<LinePrimitiveRenderer> renderers;
                internal Camera camera;
                internal TextureHandle opaqueDepth;
                internal Material resolve;
                internal bool primeDepth;
                internal bool hasOpaqueDepth;
                internal UniversalShadowData shadowData;
                internal float fibreDiameter;
                internal float mainLightSourceAngle;
                internal Vector4[] lightSources;
                internal float[] lightShadowStrengths;

                internal Vector4 subpixelSize;

                internal bool writeMotion;

                internal TextureHandle mainShadows;
                internal TextureHandle mainShadowsWithoutHair;
            }

            private class MotionVectorPassData
            {
                internal TextureHandle hairColor;
                internal TextureHandle hairDepth;
                internal TextureHandle hairMotion;
                internal Material resolve;
                internal Vector4 texSize;
                internal float invSupersample;
                internal float mode;
            }

            private class ResolvePassData
            {
                internal TextureHandle hairColor;
                internal TextureHandle hairDepth;
                internal TextureHandle opaqueDepth;
                internal bool depthAware;
                internal Material resolve;
                internal Vector4 texSize;
                internal float taps;
                internal float invSigmaSq;
                internal float invSupersample;
            }

            private class SubpixelPrepassData
            {
                internal List<LinePrimitiveRenderer> renderers;
                internal Camera camera;
                internal Material depth;
                internal Material resolve;
                internal TextureHandle sceneDepth;
                internal bool primeDepth;
            }

            private class DepthPassData
            {
                internal List<LinePrimitiveRenderer> renderers;
                internal Camera camera;
                internal Material depth;
                internal TextureHandle dependency;
                internal TextureHandle opaqueDepth;
                internal int shaderPass;
            }

            private struct CasterShadow
            {
                internal Matrix4x4 ViewProjection;
                internal Vector4 LightDir;
                internal Vector4 Layers;
                internal Vector4 Filter;
                internal Vector4 Density;
                internal int Slice;
                internal float HalfSize;
            }

            private struct CasterVolume
            {
                internal Vector3 Origin;
                internal float Voxel;
                internal float Extent;
                internal float Density;
                internal int Column;
                internal int Row;
            }

            private class VolumeSplatPassData
            {
                internal LinePrimitiveRenderer renderer;
                internal Camera camera;
                internal Material shadow;
                internal CasterVolume volume;
                internal int resolution;
                internal int tiles;
                internal int points;
            }

            private class VolumeResolvePassData
            {
                internal ComputeShader density;
                internal int kernel;
                internal TextureHandle splat;
                internal TextureHandle resolved;
                internal CasterVolume volume;
                internal int slice;
                internal int resolution;
                internal int tiles;
            }

            private class VolumeLevelsPassData
            {
                internal ComputeShader density;
                internal int downsampleKernel;
                internal int skyKernel;
                internal TextureHandle resolved;
                internal TextureHandle coneA;
                internal TextureHandle coneB;
                internal TextureHandle sky;
                internal CasterVolume[] volumes;
                internal Vector4[] skyWeightsPos;
                internal Vector4[] skyWeightsNeg;
                internal int count;
                internal int resolution;
                internal Vector4 volumeParams;
                internal Vector4 skyParams;
                internal bool grid;
            }

            private class ShadowPassData
            {
                internal LinePrimitiveRenderer renderer;
                internal Camera camera;
                internal Material shadow;
                internal int shaderPass;
                internal CasterShadow caster;
            }

            private sealed class CascadeCasters
            {
                internal readonly List<LinePrimitiveRenderer> renderers = new List<LinePrimitiveRenderer>();
                internal Matrix4x4 viewProjection;
                internal Rect viewport;
                internal Vector4 light;
                internal float coverage;
            }

            private class CascadeCasterPassData
            {
                internal CascadeCasters[] cascades;
                internal int cascadeCount;
                internal Camera camera;
                internal Material shadow;
            }

            private class CascadeCopyPassData
            {
                internal TextureHandle source;
                internal TextureHandle destination;
            }

            private class ShadowBlurPassData
            {
                internal TextureHandle source;
                internal Material shadow;
                internal Vector4 blurParams;
                internal int slice;
                internal int shaderPass;
            }

            private CasterShadow[] _casterShadows = new CasterShadow[4];
            private HairShadowCasterData[] _casterData = new HairShadowCasterData[4];

            private CasterVolume[] _casterVolumes = new CasterVolume[4];
            private HairVolumeCasterData[] _volumeData = new HairVolumeCasterData[4];
            private Vector4[] _skyWeightsPos = new Vector4[4];
            private Vector4[] _skyWeightsNeg = new Vector4[4];

            private static void EnsureCapacity<T>(ref T[] array, int count)
            {
                if (array.Length < count)
                {
                    System.Array.Resize(ref array, Mathf.NextPowerOfTwo(count));
                }
            }
            private ComputeShader _density;
            private int _volumeResolveKernel = -1;
            private int _volumeDownsampleKernel = -1;
            private int _volumeSkyKernel = -1;
            private static bool _volumeGlobalsEnabled;

            private ComputeShader _receivers;

            public void Setup(Phase phase, HairRenderSettings settings, Material resolve, Material shadow,
                              Material depth = null,
                              ComputeShader density = null)
            {
                _phase = phase;
                _settings = settings;
                _resolve = resolve;
                _depth = depth;
                _shadow = shadow;
                SetDensityShader(density);

                bool needsDepth = phase == Phase.EarlyDepth || phase == Phase.Hair;
                ConfigureInput(needsDepth ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
            }

            public void SetShadowReceivers(ComputeShader receivers)
            {
                _receivers = receivers;
            }

            public static void DisableShadowGlobals()
            {
                if (!_shadowGlobalsEnabled)
                {
                    return;
                }

                Shader.SetGlobalVector(LineShaderIds.HairShadowParams, Vector4.zero);
                _shadowGlobalsEnabled = false;
            }

            private const int MaxShadowAtlas = 8192;

            private const int MaxCascades = 4;
            private readonly CascadeCasters[] _cascadeCasters =
            {
                new CascadeCasters(), new CascadeCasters(), new CascadeCasters(), new CascadeCasters(),
            };

            private void SetDensityShader(ComputeShader density)
            {
                if (_density == density)
                {
                    return;
                }

                _density = density;
                _volumeResolveKernel = FindKernel(density, "Resolve");
                _volumeDownsampleKernel = FindKernel(density, "Downsample");
                _volumeSkyKernel = FindKernel(density, "SkyVisibility");
            }

            private static int FindKernel(ComputeShader shader, string name)
            {
                return shader != null && shader.HasKernel(name) ? shader.FindKernel(name) : -1;
            }

            public static void DisableVolumeGlobals()
            {
                if (!_volumeGlobalsEnabled)
                {
                    return;
                }

                Shader.SetGlobalVector(LineShaderIds.HairVolumeParams, Vector4.zero);
                VolumeDiagnostics.CasterCount = 0;
                _volumeGlobalsEnabled = false;
            }

            private void RecordDensityVolume(RenderGraph renderGraph, Camera camera)
            {
                using ProfilerMarker.AutoScope scope = _recordVolumeMarker.Auto();

                if (!_settings.VolumeShadows || _density == null || _shadow == null || _volumeResolveKernel < 0)
                {
                    DisableVolumeGlobals();
                    return;
                }

                List<LinePrimitiveRenderer> casters = CollectVolumeCasters(camera);
                if (casters == null)
                {
                    DisableVolumeGlobals();
                    return;
                }

                int count = casters.Count;
                int resolution = Mathf.Max(8, (int)_settings.VolumeResolution);

                int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count)));
                int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));
                int atlasWidth = resolution * columns;
                int atlasDepth = resolution * rows;

                EnsureCapacity(ref _casterVolumes, count);
                for (int i = 0; i < count; i++)
                {
                    _casterVolumes[i] = FitVolume(casters[i], resolution, i, columns);
                }

                int tiles = Mathf.CeilToInt(Mathf.Sqrt(resolution));
                int splatSize = tiles * resolution;
                var splatDesc = new TextureDesc(splatSize, splatSize)
                {
                    dimension = TextureDimension.Tex2DArray,
                    slices = count,
                    format = SplatFormat(),
                    clearBuffer = true,
                    clearColor = Color.clear,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "HairVolumeSplat",
                };
                var densityDesc = new TextureDesc(atlasWidth, resolution)
                {
                    dimension = TextureDimension.Tex3D,
                    slices = atlasDepth,
                    format = GraphicsFormat.R16_SFloat,
                    enableRandomWrite = true,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "HairVolumeDensity",
                };

                TextureHandle splat = renderGraph.CreateTexture(splatDesc);
                TextureHandle resolved = renderGraph.CreateTexture(densityDesc);

                for (int i = 0; i < count; i++)
                {
                    RecordVolumeSplat(renderGraph, camera, casters[i], _casterVolumes[i], splat, i,
                                      resolution, tiles);
                }

                for (int i = 0; i < count; i++)
                {
                    RecordVolumeResolve(renderGraph, splat, resolved, _casterVolumes[i], i, resolution,
                                        tiles, i == count - 1);
                }

                HairSkyOcclusion skyOcclusion = _settings.SkyOcclusion;
                bool levels = skyOcclusion == HairSkyOcclusion.ConeTrace ||
                              skyOcclusion == HairSkyOcclusion.PrecomputedGrid;
                if (levels && (_volumeDownsampleKernel < 0 ||
                               (skyOcclusion == HairSkyOcclusion.PrecomputedGrid && _volumeSkyKernel < 0)))
                {
                    skyOcclusion = HairSkyOcclusion.Off;
                    levels = false;
                }

                Vector4 volumeParams = PublishVolumeUniforms(count, columns, rows, atlasWidth, atlasDepth,
                                                             resolution, skyOcclusion);

                if (levels)
                {
                    RecordVolumeLevels(renderGraph, casters, resolved, count, resolution, atlasWidth,
                                       columns, rows, volumeParams, SkyParams(skyOcclusion),
                                       skyOcclusion == HairSkyOcclusion.PrecomputedGrid);
                }

                VolumeDiagnostics.CasterCount = count;
                VolumeDiagnostics.Resolution = resolution;
                VolumeDiagnostics.VoxelSize = count > 0 ? _casterVolumes[0].Voxel : 0f;
            }

            private void RecordVolumeLevels(RenderGraph renderGraph, List<LinePrimitiveRenderer> casters,
                                            TextureHandle resolved, int count, int resolution, int atlasWidth,
                                            int columns, int rows, Vector4 volumeParams, Vector4 skyParams,
                                            bool grid)
            {
                var levelDesc = new TextureDesc(atlasWidth, Mathf.Max(1, resolution / 2))
                {
                    dimension = TextureDimension.Tex3D,
                    slices = Mathf.Max(1, rows * resolution / 2),
                    format = GraphicsFormat.R16_SFloat,
                    enableRandomWrite = true,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "HairVolumeConeA",
                };
                TextureHandle coneA = renderGraph.CreateTexture(levelDesc);
                levelDesc.name = "HairVolumeConeB";
                TextureHandle coneB = renderGraph.CreateTexture(levelDesc);

                using var builder = renderGraph.AddComputePass<VolumeLevelsPassData>(
                    "HairLines.VolumeSkyOcclusion", out var passData);

                passData.density = _density;
                passData.downsampleKernel = _volumeDownsampleKernel;
                passData.skyKernel = _volumeSkyKernel;
                passData.resolved = resolved;
                passData.coneA = coneA;
                passData.coneB = coneB;
                passData.volumes = _casterVolumes;
                passData.count = count;
                passData.resolution = resolution;
                passData.volumeParams = volumeParams;
                passData.skyParams = skyParams;
                passData.grid = grid;

                builder.UseTexture(resolved, AccessFlags.Read);
                builder.UseTexture(coneA, AccessFlags.ReadWrite);
                builder.UseTexture(coneB, AccessFlags.ReadWrite);
                builder.SetGlobalTextureAfterPass(coneA, LineShaderIds.HairVolumeConeTexA);
                builder.SetGlobalTextureAfterPass(coneB, LineShaderIds.HairVolumeConeTexB);

                if (grid)
                {
                    int cell = Mathf.Max(1, (int)skyParams.w);
                    var skyDesc = new TextureDesc(columns * cell, cell)
                    {
                        dimension = TextureDimension.Tex3D,
                        slices = rows * cell,
                        format = GraphicsFormat.R16_SFloat,
                        enableRandomWrite = true,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        name = "HairSkyVisibility",
                    };
                    passData.sky = renderGraph.CreateTexture(skyDesc);
                    builder.UseTexture(passData.sky, AccessFlags.ReadWrite);
                    builder.SetGlobalTextureAfterPass(passData.sky, LineShaderIds.HairSkyVisibilityTex);

                    EnsureCapacity(ref _skyWeightsPos, count);
                    EnsureCapacity(ref _skyWeightsNeg, count);
                    for (int i = 0; i < count; i++)
                    {
                        SkyWeights(SampleAmbientProbe(casters[i].WorldBounds.center),
                                   out _skyWeightsPos[i], out _skyWeightsNeg[i]);
                    }
                    passData.skyWeightsPos = _skyWeightsPos;
                    passData.skyWeightsNeg = _skyWeightsNeg;
                }

                builder.AllowPassCulling(false);
                builder.SetRenderFunc((VolumeLevelsPassData data, ComputeGraphContext ctx) => BuildLevels(data, ctx));
            }

            private static void BuildLevels(VolumeLevelsPassData data, ComputeGraphContext ctx)
            {
                using var scope = new ProfilingScope(ctx.cmd, _levelsVolumeSampler);
                ComputeShader cs = data.density;
                int res = data.resolution;
                int levels = Mathf.RoundToInt(Mathf.Log(res, 2f));
                int kernel = data.downsampleKernel;

                for (int level = 1; level <= levels; level++)
                {
                    bool odd = (level & 1) == 1;
                    TextureHandle target = odd ? data.coneA : data.coneB;
                    TextureHandle source = level == 1 ? data.resolved : (odd ? data.coneB : data.coneA);
                    ctx.cmd.SetComputeTextureParam(cs, kernel, LineShaderIds.HairConeSourceTex, source);
                    ctx.cmd.SetComputeTextureParam(cs, kernel, LineShaderIds.HairVolumeCone, target);

                    int size = res >> level;
                    int previous = res >> (level - 1);

                    for (int i = 0; i < data.count; i++)
                    {
                        CasterVolume volume = data.volumes[i];
                        int cellX = volume.Column * res;
                        int levelZ = volume.Row * res / 2;

                        Vector4 from = level == 1
                            ? new Vector4(cellX, 0f, volume.Row * res, 0f)
                            : new Vector4(cellX + res - 2 * previous, 0f, levelZ, 0f);
                        Vector4 to = new Vector4(cellX + res - 2 * size, 0f, levelZ, size);

                        ctx.cmd.SetComputeVectorParam(cs, LineShaderIds.HairConeSource, from);
                        ctx.cmd.SetComputeVectorParam(cs, LineShaderIds.HairConeTarget, to);
                        int groups = Groups(size, 4);
                        ctx.cmd.DispatchCompute(cs, kernel, groups, groups, groups);
                    }
                }

                if (!data.grid || LinePrimitiveGlobalBuffers.VolumeCasters == null)
                {
                    return;
                }

                int sky = data.skyKernel;
                int cell = Mathf.Max(1, (int)data.skyParams.w);
                ctx.cmd.SetComputeTextureParam(cs, sky, LineShaderIds.HairVolumeConeTexA, data.coneA);
                ctx.cmd.SetComputeTextureParam(cs, sky, LineShaderIds.HairVolumeConeTexB, data.coneB);
                ctx.cmd.SetComputeTextureParam(cs, sky, LineShaderIds.HairVolumeTex, data.resolved);
                ctx.cmd.SetComputeTextureParam(cs, sky, LineShaderIds.HairSkyVisibilityOut, data.sky);
                ctx.cmd.SetComputeBufferParam(cs, sky, LineShaderIds.HairVolumeCasters,
                                              LinePrimitiveGlobalBuffers.VolumeCasters);
                ctx.cmd.SetComputeIntParam(cs, LineShaderIds.HairVolumeCount, data.count);
                ctx.cmd.SetComputeVectorParam(cs, LineShaderIds.HairVolumeParams, data.volumeParams);
                ctx.cmd.SetComputeVectorParam(cs, LineShaderIds.HairSkyParams, data.skyParams);

                for (int i = 0; i < data.count; i++)
                {
                    CasterVolume volume = data.volumes[i];
                    ctx.cmd.SetComputeVectorParam(cs, LineShaderIds.HairSkyWeightsPos, data.skyWeightsPos[i]);
                    ctx.cmd.SetComputeVectorParam(cs, LineShaderIds.HairSkyWeightsNeg, data.skyWeightsNeg[i]);
                    ctx.cmd.SetComputeVectorParam(cs, LineShaderIds.HairSkyGrid,
                        new Vector4(cell, volume.Column * cell, volume.Row * cell, i));
                    int groups = Groups(cell, 4);
                    ctx.cmd.DispatchCompute(cs, sky, groups, groups, groups);
                }
            }

            private List<LinePrimitiveRenderer> CollectVolumeCasters(Camera camera)
            {
                _volumeCasterScratch.Clear();
                GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);

                IReadOnlyList<LinePrimitiveRenderer> instances = LinePrimitiveRenderer.Instances;
                for (int i = 0; i < instances.Count; i++)
                {
                    LinePrimitiveRenderer r = instances[i];
                    if (r == null || !r.isActiveAndEnabled || r.Model == null || r.Model.EdgeCount == 0)
                    {
                        continue;
                    }
#if UNITY_EDITOR
                    if (LinePrimitiveRenderer.IsEditorHidden(r, camera))
                    {
                        continue;
                    }
#endif
                    if ((camera.cullingMask & (1 << r.gameObject.layer)) == 0)
                    {
                        continue;
                    }
                    if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, r.CullingBounds))
                    {
                        continue;
                    }

                    r.MarkRendered();
                    _volumeCasterScratch.Add(r);
                }

                return _volumeCasterScratch.Count > 0 ? _volumeCasterScratch : null;
            }

            private CasterVolume FitVolume(LinePrimitiveRenderer renderer, int resolution, int index,
                                          int columns)
            {
                Bounds bounds = renderer.CullingBounds;
                float extent = StableVolumeExtent(renderer, bounds);
                float voxel = extent / resolution;

                Vector3 corner = bounds.center - Vector3.one * (extent * 0.5f);
                corner = new Vector3(Mathf.Floor(corner.x / voxel) * voxel,
                                     Mathf.Floor(corner.y / voxel) * voxel,
                                     Mathf.Floor(corner.z / voxel) * voxel);

                float alpha = Mathf.Clamp(_settings.FibreDiameter * 0.001f / voxel, 0f, 0.95f);

                return new CasterVolume
                {
                    Origin = corner,
                    Voxel = voxel,
                    Extent = extent,
                    Density = Mathf.Log(1f - alpha) * _settings.DensityScale,
                    Column = index % columns,
                    Row = index / columns,
                };
            }

            private static readonly ConditionalWeakTable<LinePrimitiveModel, StrongBox<float>> _volumeExtent =
                new ConditionalWeakTable<LinePrimitiveModel, StrongBox<float>>();

            private static float StableVolumeExtent(LinePrimitiveRenderer renderer, Bounds bounds)
            {
                const float Margin = 1.1f;
                Vector3 size = bounds.size;
                float needed = Mathf.Max(1e-3f, Mathf.Max(size.x, Mathf.Max(size.y, size.z))) * Margin;

                LinePrimitiveModel model = renderer.Model;
                if (model == null)
                {
                    return needed;
                }

                if (!_volumeExtent.TryGetValue(model, out StrongBox<float> stored))
                {
                    Vector3 scale = renderer.transform.lossyScale;
                    float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                    float sphere = 2f * model.LocalBounds.extents.magnitude * maxScale +
                                   2f * renderer.CullingBoundsPadding;
                    stored = new StrongBox<float>(Mathf.Max(1e-3f, sphere) * Margin);
                    _volumeExtent.Add(model, stored);
                }

                for (int i = 0; i < 8 && stored.Value < needed; i++)
                {
                    stored.Value *= 2f;
                }

                return stored.Value;
            }

            private void RecordVolumeSplat(RenderGraph renderGraph, Camera camera, LinePrimitiveRenderer renderer,
                                           CasterVolume volume, TextureHandle splat, int slice,
                                           int resolution, int tiles)
            {
                using var builder = renderGraph.AddRasterRenderPass<VolumeSplatPassData>(
                    "HairLines.VolumeSplat", out var passData);

                passData.renderer = renderer;
                passData.camera = camera;
                passData.shadow = _shadow;
                passData.volume = volume;
                passData.resolution = resolution;
                passData.tiles = tiles;
                passData.points = SplatPointsPerSegment(renderer, volume.Voxel);

                builder.SetRenderAttachment(splat, 0, AccessFlags.Write, 0, slice);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((VolumeSplatPassData data, RasterGraphContext ctx) => DrawSplat(data, ctx));
            }

            private static void DrawSplat(VolumeSplatPassData data, RasterGraphContext ctx)
            {
                using var scope = new ProfilingScope(ctx.cmd, _splatVolumeSampler);
                ctx.cmd.ClearRenderTarget(false, true, Color.clear);

                LinePrimitiveRenderer r = data.renderer;
                LinePrimitiveModel model = r != null ? r.Model : null;
                if (model == null || data.shadow == null)
                {
                    return;
                }
#if UNITY_EDITOR
                if (LinePrimitiveRenderer.IsEditorHidden(r))
                {
                    return;
                }
#endif

                float fraction = r.GetShadowLodStrandFraction(data.camera);
                float weight = r.GetShadowFibreWeight(data.camera);

                CasterVolume volume = data.volume;
                ctx.cmd.SetGlobalVector(LineShaderIds.HairVolumeOrigin,
                    new Vector4(volume.Origin.x, volume.Origin.y, volume.Origin.z, volume.Voxel));
                ctx.cmd.SetGlobalVector(LineShaderIds.HairSplatParams,
                    new Vector4(data.resolution, data.tiles, data.points, weight));

                for (int drawRange = 0; drawRange < model.DrawRangeCount; drawRange++)
                {
                    Vector2Int span = model.VertexRangeForDrawRange(drawRange, fraction);
                    if (span.y <= 0)
                    {
                        continue;
                    }

                    MaterialPropertyBlock properties = r.GetDrawRangePropertyBlock(drawRange);
                    if (properties == null)
                    {
                        continue;
                    }

                    properties.SetVector(LineShaderIds.HairVolumeRange, new Vector4(span.x, span.y, 0f, 0f));
                    int copies = Mathf.Max(1, r.GetDrawRangeInstanceCount(drawRange));
                    ctx.cmd.DrawProcedural(r.DrawMatrix, data.shadow, VolumeSplatPass, MeshTopology.Points,
                                           span.y, copies * data.points, properties);
                }
            }

            private static int SplatPointsPerSegment(LinePrimitiveRenderer renderer, float voxel)
            {
                const int MaxPoints = 16;
                LinePrimitiveModel model = renderer.Model;
                if (model == null)
                {
                    return 1;
                }

                Vector3 scale = renderer.DrawMatrix.lossyScale;
                float longest = LongestSegment(model) *
                                Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                return Mathf.Clamp(Mathf.CeilToInt(longest / Mathf.Max(0.5f * voxel, 1e-6f)), 1, MaxPoints);
            }

            private static readonly ConditionalWeakTable<LinePrimitiveModel, StrongBox<float>> _longestSegment =
                new ConditionalWeakTable<LinePrimitiveModel, StrongBox<float>>();

            private static float LongestSegment(LinePrimitiveModel model)
            {
                if (_longestSegment.TryGetValue(model, out StrongBox<float> cached))
                {
                    return cached.Value;
                }

                float longest = 0f;
                Vector3[] positions = model.Positions;
                Vector2Int[] strands = model.Strands;
                if (positions != null && strands != null)
                {
                    for (int s = 0; s < strands.Length; s++)
                    {
                        int end = Mathf.Min(strands[s].x + strands[s].y, positions.Length) - 1;
                        for (int v = strands[s].x; v < end; v++)
                        {
                            longest = Mathf.Max(longest, (positions[v + 1] - positions[v]).sqrMagnitude);
                        }
                    }
                }

                longest = Mathf.Sqrt(longest);
                _longestSegment.Add(model, new StrongBox<float>(longest));
                return longest;
            }

            private static GraphicsFormat SplatFormat()
            {
                return SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat, GraphicsFormatUsage.Blend)
                    ? GraphicsFormat.R32_SFloat
                    : GraphicsFormat.R16_SFloat;
            }

            private void RecordVolumeResolve(RenderGraph renderGraph, TextureHandle splat, TextureHandle resolved,
                                             CasterVolume volume, int slice, int resolution, int tiles,
                                             bool publish)
            {
                using var builder = renderGraph.AddComputePass<VolumeResolvePassData>(
                    "HairLines.VolumeResolve", out var passData);

                passData.density = _density;
                passData.kernel = _volumeResolveKernel;
                passData.splat = splat;
                passData.resolved = resolved;
                passData.volume = volume;
                passData.slice = slice;
                passData.resolution = resolution;
                passData.tiles = tiles;

                builder.UseTexture(splat, AccessFlags.Read);
                builder.UseTexture(resolved, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                if (publish)
                {
                    builder.SetGlobalTextureAfterPass(resolved, LineShaderIds.HairVolumeTex);
                }

                builder.SetRenderFunc((VolumeResolvePassData data, ComputeGraphContext ctx) =>
                {
                    using (new ProfilingScope(ctx.cmd, _resolveVolumeSampler))
                    {
                        ComputeShader cs = data.density;
                        int res = data.resolution;
                        ctx.cmd.SetComputeTextureParam(cs, data.kernel, LineShaderIds.HairVolumeSplat, data.splat);
                        ctx.cmd.SetComputeTextureParam(cs, data.kernel, LineShaderIds.HairVolumeDensity, data.resolved);
                        ctx.cmd.SetComputeVectorParam(cs, LineShaderIds.HairSplatResolve,
                            new Vector4(res, data.tiles, data.slice, 1f / Mathf.Max(data.volume.Voxel, 1e-6f)));
                        ctx.cmd.SetComputeVectorParam(cs, LineShaderIds.HairVolumeSlab,
                            new Vector4(data.volume.Column * res, 0f, data.volume.Row * res, 0f));
                        int groups = Groups(res, 4);
                        ctx.cmd.DispatchCompute(cs, data.kernel, groups, groups, groups);
                    }
                });
            }

            private static int Groups(int count, int size)
            {
                return Mathf.Max(1, (count + size - 1) / size);
            }

            private static void SkyWeights(SHCoefficients sh, out Vector4 positive, out Vector4 negative)
            {
                positive = new Vector4(
                    SkyLuminance(sh, 1f, 0f, 0f), SkyLuminance(sh, 0f, 1f, 0f), SkyLuminance(sh, 0f, 0f, 1f), 0f);
                negative = new Vector4(
                    SkyLuminance(sh, -1f, 0f, 0f), SkyLuminance(sh, 0f, -1f, 0f), SkyLuminance(sh, 0f, 0f, -1f), 0f);
            }

            private static float SkyLuminance(SHCoefficients sh, float x, float y, float z)
            {
                float r = SkyChannel(sh.SHAr, sh.SHBr, sh.SHC.x, x, y, z);
                float g = SkyChannel(sh.SHAg, sh.SHBg, sh.SHC.y, x, y, z);
                float b = SkyChannel(sh.SHAb, sh.SHBb, sh.SHC.z, x, y, z);
                return 0.2126f * r + 0.7152f * g + 0.0722f * b;
            }

            private static float SkyChannel(Vector4 a, Vector4 b, float c, float x, float y, float z)
            {
                float value = a.x * x + a.y * y + a.z * z + a.w
                            + b.x * x * y + b.y * y * z + b.z * z * z + b.w * z * x
                            + c * (x * x - y * y);
                return Mathf.Max(value, 0f);
            }

            private Vector4 SkyParams(HairSkyOcclusion mode)
            {
                switch (mode)
                {
                    case HairSkyOcclusion.RayMarch:
                        HairSkyRayMarchSettings ray = _settings.SkyRayMarch;
                        return new Vector4(ray.Steps, ray.Distance * 0.01f, 0f, 0f);
                    case HairSkyOcclusion.ConeTrace:
                        HairSkyConeTraceSettings cone = _settings.SkyConeTrace;
                        return new Vector4(cone.Steps, cone.Distance * 0.01f,
                                           Mathf.Tan(0.5f * cone.Angle * Mathf.Deg2Rad), 0f);
                    case HairSkyOcclusion.PrecomputedGrid:
                        HairSkyGridSettings grid = _settings.SkyGrid;
                        return new Vector4(grid.Steps, grid.Distance * 0.01f,
                                           Mathf.Tan(0.5f * grid.Angle * Mathf.Deg2Rad), grid.Resolution);
                    default:
                        return Vector4.zero;
                }
            }

            private Vector4 PublishVolumeUniforms(int count, int columns, int rows, int atlasWidth,
                                                  int atlasDepth, int resolution,
                                                  HairSkyOcclusion skyOcclusion)
            {
                float halfU = atlasWidth > 0 ? 0.5f / atlasWidth : 0f;
                float halfW = atlasDepth > 0 ? 0.5f / atlasDepth : 0f;
                float spanU = 1f / columns;
                float spanW = 1f / rows;
                float longest = 0f;

                EnsureCapacity(ref _volumeData, count);
                for (int i = 0; i < count; i++)
                {
                    CasterVolume volume = _casterVolumes[i];
                    float inverse = 1f / Mathf.Max(volume.Extent, 1e-6f);
                    float offsetU = volume.Column * spanU;
                    float offsetW = volume.Row * spanW;

                    _volumeData[i] = new HairVolumeCasterData
                    {
                        Origin = new Vector4(volume.Origin.x, volume.Origin.y, volume.Origin.z,
                                             volume.Voxel),
                        Scale = new Vector4(inverse, inverse, inverse, volume.Density),
                        SlabU = new Vector4(offsetU, spanU, offsetU + halfU, offsetU + spanU - halfU),
                        SlabW = new Vector4(offsetW, spanW, offsetW + halfW, offsetW + spanW - halfW),
                    };
                    longest = Mathf.Max(longest, volume.Extent);
                }

                Shader.SetGlobalInt(LineShaderIds.HairVolumeCount, count);
                LinePrimitiveGlobalBuffers.PublishVolumeCasters(_volumeData, count);

                var volumeParams = new Vector4(1f, longest * 1.733f, _settings.DensityScale, resolution);
                Shader.SetGlobalVector(LineShaderIds.HairVolumeParams, volumeParams);
                Shader.SetGlobalFloat(LineShaderIds.HairSkyOcclusion, (float)skyOcclusion);
                Shader.SetGlobalVector(LineShaderIds.HairSkyParams, SkyParams(skyOcclusion));
                _volumeGlobalsEnabled = true;
                return volumeParams;
            }

            private bool RecordShadowMap(RenderGraph renderGraph, ContextContainer frameData, Camera camera,
                                         out TextureHandle layerMap)
            {
                using ProfilerMarker.AutoScope scope = _recordShadowMapMarker.Auto();
                layerMap = default;

                if (!_settings.CastShadows || _shadow == null)
                {
                    return false;
                }

                var lightData = frameData.Get<UniversalLightData>();
                if (lightData.mainLightIndex < 0 || lightData.mainLightIndex >= lightData.visibleLights.Length)
                {
                    return false;
                }

                var visibleLight = lightData.visibleLights[lightData.mainLightIndex];
                if (visibleLight.lightType != LightType.Directional)
                {
                    return false;
                }

                var mainLight = visibleLight.light;
                if (mainLight == null || mainLight.shadows == LightShadows.None)
                {
                    return false;
                }

                Vector3 lightDir = visibleLight.light.transform.forward;
                if (mainLight.shadowStrength <= 0f)
                {
                    return false;
                }

                var casters = CollectShadowCasters(camera, out _);
                if (casters == null)
                {
                    return false;
                }

                int casterCount = casters.Count;
                EnsureCapacity(ref _casterShadows, casterCount);
                int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(casterCount)));
                int rows = Mathf.Max(1, Mathf.CeilToInt(casterCount / (float)columns));

                int tile = Mathf.Max(1, (int)_settings.ShadowResolution);
                int longest = Mathf.Max(columns, rows);
                if (tile * longest > MaxShadowAtlas)
                {
                    tile = Mathf.Max(1, MaxShadowAtlas / longest);
                }

                int atlasWidth = tile * columns;
                int atlasHeight = tile * rows;

                var depthDesc = new TextureDesc(tile, tile)
                {
                    dimension = TextureDimension.Tex2DArray,
                    slices = casterCount,
                    format = GraphicsFormat.R32_SFloat,
                    clearBuffer = true,
                    clearColor = new Color(1e6f, 1e6f, 1e6f, 1e6f),
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "HairShadowDepth",
                };
                var layerDesc = new TextureDesc(tile, tile)
                {
                    dimension = TextureDimension.Tex2DArray,
                    slices = casterCount,
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "HairShadowLayers",
                };

                for (int i = 0; i < casterCount; i++)
                {
                    _casterShadows[i] = FitCaster(casters[i], lightDir, tile, i, columns,
                                                  atlasWidth, atlasHeight);
                }

                TextureHandle depthMap = renderGraph.CreateTexture(depthDesc);
                layerMap = renderGraph.CreateTexture(layerDesc);

                for (int i = 0; i < casterCount; i++)
                {
                    RecordCasterPass(renderGraph, camera, casters[i], _casterShadows[i], depthMap,
                                     ShadowDepthPass, "HairLines.ShadowDepth", default);
                }

                depthMap = RecordTiledFilter(renderGraph, depthMap, depthDesc, casterCount, atlasWidth,
                                             ShadowDepthMinPass, "HairLines.ShadowDepthMin",
                                             "HairShadowDepthMin", LineShaderIds.HairShadowDepthTex,
                                             blur: false);

                for (int i = 0; i < casterCount; i++)
                {
                    RecordCasterPass(renderGraph, camera, casters[i], _casterShadows[i], layerMap,
                                     ShadowLayerPass, "HairLines.ShadowLayers", depthMap);
                }

                layerMap = RecordTiledFilter(renderGraph, layerMap, layerDesc, casterCount, atlasWidth,
                                             ShadowBlurPass, "HairLines.ShadowBlur",
                                             "HairShadowLayersBlur", LineShaderIds.HairShadowLayerTex,
                                             blur: true);

                ShadowDiagnostics.CasterCount = casterCount;
                ShadowDiagnostics.TileSize = tile;
                ShadowDiagnostics.HalfSize = casterCount > 0 ? _casterShadows[0].HalfSize : 0f;
                ShadowDiagnostics.SoftnessTexels = casterCount > 0 ? _casterShadows[0].Filter.z : 0f;

                PublishCasterUniforms(casterCount, mainLight.shadowStrength);
                return true;
            }

            private CasterShadow FitCaster(LinePrimitiveRenderer renderer, Vector3 lightDir, int tile,
                                           int index, int columns, int atlasWidth, int atlasHeight)
            {
                Bounds bounds = renderer.CullingBounds;

                Vector3 up = Mathf.Abs(Vector3.Dot(lightDir, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
                var rotation = Quaternion.LookRotation(lightDir, up);

                Vector3 axisX = rotation * Vector3.right;
                Vector3 axisY = rotation * Vector3.up;
                Vector3 extents = bounds.extents;

                float halfWidth = ProjectExtents(extents, axisX);
                float halfHeight = ProjectExtents(extents, axisY);
                float halfDepth = ProjectExtents(extents, lightDir);

                float halfSize = Mathf.Max(Mathf.Max(halfWidth, halfHeight), 1e-3f) * 1.05f;
                float texelWorldSize = 2f * halfSize / tile;

                Vector3 center = bounds.center;
                float snappedX = Mathf.Floor(Vector3.Dot(center, axisX) / texelWorldSize) * texelWorldSize;
                float snappedY = Mathf.Floor(Vector3.Dot(center, axisY) / texelWorldSize) * texelWorldSize;
                center = axisX * snappedX + axisY * snappedY + lightDir * Vector3.Dot(center, lightDir);

                const float NearPad = 0.05f;
                Vector3 eye = center - lightDir * (halfDepth + NearPad);

                var view = Matrix4x4.TRS(eye, rotation, Vector3.one).inverse;
                view.m20 = -view.m20; view.m21 = -view.m21; view.m22 = -view.m22; view.m23 = -view.m23;

                var projection = Matrix4x4.Ortho(-halfSize, halfSize, -halfSize, halfSize,
                                                 NearPad * 0.5f, 2f * halfDepth + NearPad * 1.5f);

                float depthExtent = Mathf.Max(2f * halfDepth, 1e-3f);
                float exponent = Mathf.Lerp(1f, 3f, _settings.LayerDistribution);
                float fibreDiameter = _settings.FibreDiameter * 0.001f;
                float fibreAlpha = Mathf.Clamp(fibreDiameter / Mathf.Max(texelWorldSize, 1e-6f), 1e-4f, 0.999f);

                return new CasterShadow
                {
                    ViewProjection = GL.GetGPUProjectionMatrix(projection, true) * view,
                    LightDir = new Vector4(lightDir.x, lightDir.y, lightDir.z, -Vector3.Dot(eye, lightDir)),
                    Layers = new Vector4(
                        depthExtent * Mathf.Pow(0.25f, exponent),
                        depthExtent * Mathf.Pow(0.50f, exponent),
                        depthExtent * Mathf.Pow(0.75f, exponent),
                        depthExtent),
                    Filter = new Vector4(1f / tile, 1f / tile,
                                         _settings.Softness * 0.001f / Mathf.Max(texelWorldSize, 1e-6f),
                                         0f),
                    Slice = index,
                    HalfSize = halfSize,
                    Density = new Vector4(Mathf.Log(1f - fibreAlpha) * _settings.DensityScale,
                                          0f, 0f, 0f),
                };
            }

            private void PublishCasterUniforms(int casterCount, float shadowStrength)
            {
                EnsureCapacity(ref _casterData, casterCount);
                for (int i = 0; i < casterCount; i++)
                {
                    _casterData[i] = new HairShadowCasterData
                    {
                        WorldToLight = _casterShadows[i].ViewProjection,
                        LightDir = _casterShadows[i].LightDir,
                        Layers = _casterShadows[i].Layers,
                        Filter = _casterShadows[i].Filter,
                        Density = _casterShadows[i].Density,
                    };
                }

                Shader.SetGlobalInt(LineShaderIds.HairShadowCount, casterCount);
                LinePrimitiveGlobalBuffers.PublishShadowCasters(_casterData, casterCount);

                Shader.SetGlobalVector(LineShaderIds.HairShadowParams, new Vector4(
                    _settings.DensityScale, shadowStrength, 1f, _settings.ShadowDepthBias));
                _shadowGlobalsEnabled = true;
            }

            private void RecordCasterPass(RenderGraph renderGraph, Camera camera,
                                          LinePrimitiveRenderer renderer, CasterShadow caster,
                                          TextureHandle target, int shaderPass, string passName,
                                          TextureHandle dependency)
            {
                using var builder = renderGraph.AddRasterRenderPass<ShadowPassData>(passName, out var passData);

                passData.renderer = renderer;
                passData.camera = camera;
                passData.shadow = _shadow;
                passData.shaderPass = shaderPass;
                passData.caster = caster;

                builder.SetRenderAttachment(target, 0, AccessFlags.Write, 0, caster.Slice);
                if (dependency.IsValid())
                {
                    builder.UseTexture(dependency, AccessFlags.Read);
                    builder.UseAllGlobalTextures(true);
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((ShadowPassData data, RasterGraphContext ctx) => DrawStrands(data, ctx));
            }

            private TextureHandle RecordTiledFilter(RenderGraph renderGraph, TextureHandle source,
                                                    TextureDesc desc, int casterCount, int atlasWidth,
                                                    int shaderPass, string passName, string textureName,
                                                    int publishAs, bool blur)
            {
                bool any = false;
                for (int i = 0; i < casterCount && !any; i++)
                {
                    any = FilterSteps(_casterShadows[i].Filter.z, out _, out _, out _);
                }

                if (!any)
                {
                    using var passthrough = renderGraph.AddRasterRenderPass<ShadowBlurPassData>(
                        passName + "Publish", out _);
                    passthrough.SetRenderAttachment(source, 0, AccessFlags.ReadWrite);
                    passthrough.AllowPassCulling(false);
                    passthrough.SetGlobalTextureAfterPass(source, publishAs);
                    passthrough.SetRenderFunc((ShadowBlurPassData data, RasterGraphContext ctx) => { });
                    return source;
                }

                desc.clearBuffer = true;
                desc.name = textureName + "H";
                TextureHandle horizontal = renderGraph.CreateTexture(desc);
                desc.name = textureName + "V";
                TextureHandle vertical = renderGraph.CreateTexture(desc);

                for (int axis = 0; axis < 2; axis++)
                {
                    TextureHandle from = axis == 0 ? source : horizontal;
                    TextureHandle to = axis == 0 ? horizontal : vertical;

                    for (int i = 0; i < casterCount; i++)
                    {
                        CasterShadow caster = _casterShadows[i];
                        FilterSteps(caster.Filter.z, out int taps, out float step, out float invSigmaSq);
                        float uvStep = step * caster.Filter.x;

                        RecordFilterAxis(renderGraph, from, to,
                            new Vector4(axis == 0 ? uvStep : 0f, axis == 0 ? 0f : uvStep,
                                        blur ? invSigmaSq : 0f, taps),
                            caster.Slice, shaderPass, passName,
                            axis == 1 && i == casterCount - 1 ? publishAs : 0);
                    }
                }

                return vertical;
            }

            private static bool FilterSteps(float radiusTexels, out int taps, out float step, out float invSigmaSq)
            {
                const int MaxTaps = 8;
                taps = Mathf.Clamp(Mathf.CeilToInt(radiusTexels), 1, MaxTaps);
                step = radiusTexels / taps;
                invSigmaSq = 4f / (taps * taps);
                return radiusTexels >= 0.5f;
            }

            private void RecordFilterAxis(RenderGraph renderGraph, TextureHandle source, TextureHandle target,
                                  Vector4 filterParams, int slice, int shaderPass, string passName,
                                  int publishAs)
            {
                using ProfilerMarker.AutoScope scope = _recordFilterMarker.Auto();
                using var builder = renderGraph.AddRasterRenderPass<ShadowBlurPassData>(passName, out var passData);

                passData.source = source;
                passData.shadow = _shadow;
                passData.blurParams = filterParams;
                passData.slice = slice;
                passData.shaderPass = shaderPass;

                builder.SetRenderAttachment(target, 0, AccessFlags.Write, 0, slice);
                builder.UseTexture(source, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                if (publishAs != 0)
                {
                    builder.SetGlobalTextureAfterPass(target, publishAs);
                }

                builder.SetRenderFunc((ShadowBlurPassData data, RasterGraphContext ctx) =>
                {
                    using (new ProfilingScope(ctx.cmd, _gpuShadowFilterSampler))
                    {
                        ctx.cmd.SetGlobalVector(LineShaderIds.HairShadowBlurParams, data.blurParams);
                        ctx.cmd.SetGlobalFloat(LineShaderIds.HairShadowFilterSlice, data.slice);
                        ctx.cmd.SetGlobalTexture(LineShaderIds.HairShadowFilterSource, data.source);
                        Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1f, 1f, 0f, 0f),
                                            data.shadow, data.shaderPass);
                    }
                });
            }

            private static float ProjectExtents(Vector3 extents, Vector3 axis)
            {
                return Mathf.Abs(axis.x) * extents.x
                     + Mathf.Abs(axis.y) * extents.y
                     + Mathf.Abs(axis.z) * extents.z;
            }

            private static void DrawStrands(ShadowPassData data, RasterGraphContext ctx)
            {
                using ProfilerMarker.AutoScope scope = _drawShadowStrandsMarker.Auto();
                var r = data.renderer;
                if (r == null)
                {
                    return;
                }
#if UNITY_EDITOR
                if (LinePrimitiveRenderer.IsEditorHidden(r))
                {
                    return;
                }
#endif
                var model = r.Model;
                float lodFraction = r.GetShadowLodStrandFraction(data.camera);
                float fibreWeight = r.GetShadowFibreWeight(data.camera);
                if (model == null || r.GetLodEdgeCount(data.camera, lodFraction) == 0)
                {
                    return;
                }

                ProfilingSampler gpuSampler = data.shaderPass == ShadowLayerPass
                    ? _gpuShadowLayersSampler
                    : _gpuShadowDepthSampler;
                using (new ProfilingScope(ctx.cmd, gpuSampler))
                {
                    ctx.cmd.SetGlobalMatrix(LineShaderIds.HairShadowDrawMatrix, data.caster.ViewProjection);
                    ctx.cmd.SetGlobalVector(LineShaderIds.HairShadowDrawLightDir, data.caster.LightDir);
                    ctx.cmd.SetGlobalVector(LineShaderIds.HairShadowDrawLayers, data.caster.Layers);
                    ctx.cmd.SetGlobalFloat(LineShaderIds.HairShadowDrawSlice, data.caster.Slice);
                    ctx.cmd.SetGlobalFloat(LineShaderIds.HairShadowOpacityThreshold, OpacityThreshold);

                    for (int drawRange = 0; drawRange < r.DrawRangeCount; drawRange++)
                    {
                        int vertexCount = r.GetDrawRangeVertexCount(drawRange, lodFraction);
                        if (vertexCount == 0)
                        {
                            continue;
                        }

                        MaterialPropertyBlock properties = r.GetDrawRangePropertyBlock(drawRange);
                        if (properties == null)
                        {
                            continue;
                        }

                        GraphicsBuffer indices = r.GetDrawRangeIndexBuffer(drawRange);
                        if (indices == null)
                        {
                            continue;
                        }

                        properties.SetFloat(LineShaderIds.ShadowFibreWeight, fibreWeight);
                        ctx.cmd.DrawProcedural(indices, r.DrawMatrix, data.shadow, data.shaderPass,
                            MeshTopology.Lines, vertexCount, r.GetDrawRangeInstanceCount(drawRange), properties);
                    }
                }
            }

            private List<LinePrimitiveRenderer> CollectShadowCasters(Camera camera, out Bounds bounds)
            {
                using ProfilerMarker.AutoScope scope = _collectShadowCastersMarker.Auto();
                bounds = default;
                _shadowCasterScratch.Clear();
                GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);

                var instances = LinePrimitiveRenderer.Instances;
                for (int i = 0; i < instances.Count; i++)
                {
                    var r = instances[i];
                    if (r == null || !r.isActiveAndEnabled)
                    {
                        continue;
                    }
#if UNITY_EDITOR
                    if (LinePrimitiveRenderer.IsEditorHidden(r, camera))
                    {
                        continue;
                    }
#endif

                    var model = r.Model;
                    if (model == null || model.EdgeCount == 0)
                    {
                        continue;
                    }
                    if ((camera.cullingMask & (1 << r.gameObject.layer)) == 0 ||
                        r.GetLodEdgeCount(camera, r.GetLodStrandFraction(camera)) == 0)
                    {
                        continue;
                    }
                    if (r.PropertyBlock == null || !r.HasDrawMaterial)
                    {
                        continue;
                    }

                    Bounds casterBounds = r.CullingBounds;
                    if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, casterBounds))
                    {
                        continue;
                    }

                    if (_shadowCasterScratch.Count == 0)
                    {
                        bounds = casterBounds;
                    }
                    else
                    {
                        bounds.Encapsulate(casterBounds);
                    }
                    _shadowCasterScratch.Add(r);
                }

                return _shadowCasterScratch.Count > 0 ? _shadowCasterScratch : null;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using ProfilerMarker.AutoScope scope = _recordGraphMarker.Auto();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                Camera camera = cameraData.camera;

                if ((camera.cameraType == CameraType.Preview && !LinePrimitiveRenderer.DrawsForPreview(camera)) ||
                    camera.cameraType == CameraType.Reflection)
                {
                    return;
                }
                if (camera.cameraType == CameraType.SceneView && !_settings.RenderInSceneView)
                {
                    return;
                }

                if (_phase == Phase.Shadows)
                {
                    using (new RenderGraphProfilingScope(renderGraph, _groupShadowsSampler))
                    {
                        RecordShadows(renderGraph, frameData, cameraData, resourceData);
                    }
                    return;
                }

                if (_phase == Phase.MotionVectors)
                {
                    using (new RenderGraphProfilingScope(renderGraph, _groupMotionVectorsSampler))
                    {
                        RecordMotionVectors(renderGraph, frameData, cameraData, resourceData);
                    }
                    return;
                }

                if (_phase == Phase.SceneViewDepthRestore)
                {
                    RecordSceneViewDepthRestore(renderGraph, frameData, resourceData);
                    return;
                }

                List<LinePrimitiveRenderer> visible;
                using (ProfilerMarker.AutoScope collectScope = _collectVisibleMarker.Auto())
                {
                    visible = CollectVisible(camera);
                }
                if (_phase == Phase.EarlyDepth)
                {
                    if (visible == null)
                    {
                        return;
                    }
                    using (new RenderGraphProfilingScope(renderGraph, _groupEarlyDepthSampler))
                    {
                        RecordEarlyDepth(renderGraph, frameData, resourceData, cameraData, visible);
                    }
                    return;
                }

                using (new RenderGraphProfilingScope(renderGraph, _groupHairSampler))
                {
                    if (_receivers != null && _settings.InjectionPoint >= RenderPassEvent.AfterRenderingOpaques)
                    {
                        HairShadowReceivers.Record(renderGraph, cameraData, resourceData, _receivers);
                    }

                    if (visible == null || !resourceData.activeColorTexture.IsValid())
                    {
                        return;
                    }

                    RecordHair(renderGraph, frameData, cameraData, resourceData, visible);
                }
            }

            private void RecordEarlyDepth(RenderGraph renderGraph, ContextContainer frameData,
                                          UniversalResourceData resourceData,
                                          UniversalCameraData cameraData,
                                          List<LinePrimitiveRenderer> visible)
            {
                TextureHandle cameraDepth = resourceData.cameraDepthTexture;
                if (_depth == null || !cameraDepth.IsValid())
                {
                    return;
                }

                HairFrameResources frameResources = frameData.GetOrCreate<HairFrameResources>();
                if (!frameResources.opaqueDepth.IsValid())
                {
                    frameResources.opaqueDepth = RecordOpaqueDepthSnapshot(
                        renderGraph, frameData, cameraData, cameraDepth);
                }

                RenderTargetInfo cameraDepthInfo;
                try
                {
                    cameraDepthInfo = renderGraph.GetRenderTargetInfo(cameraDepth);
                }
                catch (System.Exception)
                {
                    return;
                }

                if (cameraDepthInfo.width <= 0 || cameraDepthInfo.height <= 0 ||
                    cameraDepthInfo.volumeDepth <= 0)
                {
                    return;
                }

                TextureDesc mergedDesc;
                try
                {
                    mergedDesc = renderGraph.GetTextureDesc(cameraDepth);
                }
                catch (System.Exception)
                {
                    bool xrReady = cameraData.xr.enabled && cameraDepthInfo.volumeDepth > 1;
                    mergedDesc = new TextureDesc(cameraDepthInfo.width, cameraDepthInfo.height,
                                                 dynamicResolution: false, xrReady: xrReady)
                    {
                        slices = Mathf.Max(1, cameraDepthInfo.volumeDepth),
                        dimension = cameraDepthInfo.volumeDepth > 1
                            ? TextureDimension.Tex2DArray
                            : TextureDimension.Tex2D,
                    };
                }

                if (mergedDesc.width <= 0 || mergedDesc.height <= 0 || mergedDesc.slices <= 0)
                {
                    mergedDesc.width = cameraDepthInfo.width;
                    mergedDesc.height = cameraDepthInfo.height;
                    mergedDesc.slices = Mathf.Max(1, cameraDepthInfo.volumeDepth);
                    mergedDesc.dimension = cameraDepthInfo.volumeDepth > 1
                        ? TextureDimension.Tex2DArray
                        : TextureDimension.Tex2D;
                }

                bool cameraDepthIsDepth = GraphicsFormatUtility.IsDepthFormat(cameraDepthInfo.format);
                if (cameraDepthIsDepth)
                {
                    using (var builder = renderGraph.AddRasterRenderPass<DepthPassData>(
                               "HairLines.EarlyDepthMerge", out var passData))
                    {
                        passData.renderers = visible;
                        passData.camera = cameraData.camera;
                        passData.depth = _depth;
                        passData.dependency = default;
                        passData.opaqueDepth = default;
                        passData.shaderPass = DepthPrimePass;

                        builder.SetRenderAttachmentDepth(cameraDepth, AccessFlags.ReadWrite);
                        builder.SetGlobalTextureAfterPass(cameraDepth,
                                                          Shader.PropertyToID("_CameraDepthTexture"));
                        builder.AllowGlobalStateModification(true);
                        if (cameraData.xr.enabled)
                        {
                            builder.SetExtendedFeatureFlags(ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
                        }
                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc((DepthPassData data, RasterGraphContext ctx) =>
                        {
                            using (new ProfilingScope(ctx.cmd, _gpuEarlyDepthSampler))
                            {
                                DrawDepthStrands(data, ctx);
                            }
                        });
                    }
                    return;
                }

                if (cameraDepthInfo.format != GraphicsFormat.R32_SFloat)
                {
                    return;
                }

                mergedDesc.name = "HairLinesEarlyCameraDepthMerged";
                mergedDesc.format = GraphicsFormat.R32_SFloat;
                mergedDesc.depthBufferBits = DepthBits.None;
                mergedDesc.msaaSamples = MSAASamples.None;
                mergedDesc.bindTextureMS = false;
                mergedDesc.clearBuffer = false;
                TextureHandle mergedDepth = renderGraph.CreateTexture(mergedDesc);

                TextureDesc hairDepthDesc = mergedDesc;
                hairDepthDesc.name = "HairLinesEarlyDepthAttachment";
                hairDepthDesc.format = GraphicsFormat.None;
                hairDepthDesc.depthBufferBits = DepthBits.Depth32;
                hairDepthDesc.clearBuffer = true;
                hairDepthDesc.clearColor = SystemInfo.usesReversedZBuffer ? Color.black : Color.white;
                TextureHandle hairDepth = renderGraph.CreateTexture(hairDepthDesc);

                renderGraph.AddBlitPass(cameraDepth, mergedDepth, Vector2.one, Vector2.zero,
                                        filterMode: RenderGraphUtils.BlitFilterMode.ClampNearest,
                                        passName: "HairLines.EarlyDepthSeed");

                using (var builder = renderGraph.AddRasterRenderPass<DepthPassData>(
                           "HairLines.EarlyDepthMerge", out var passData))
                {
                    passData.renderers = visible;
                    passData.camera = cameraData.camera;
                    passData.depth = _depth;
                    passData.dependency = default;
                    passData.opaqueDepth = cameraDepth;
                    passData.shaderPass = SystemInfo.usesReversedZBuffer
                        ? EarlyDepthMergeReversedPass
                        : EarlyDepthMergeNormalPass;
                    builder.UseTexture(passData.opaqueDepth, AccessFlags.Read);
                    builder.SetRenderAttachment(mergedDepth, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(hairDepth, AccessFlags.Write);
                    if (cameraData.xr.enabled)
                    {
                        builder.SetExtendedFeatureFlags(ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
                    }
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc((DepthPassData data, RasterGraphContext ctx) =>
                    {
                        using (new ProfilingScope(ctx.cmd, _gpuEarlyDepthSampler))
                        {
                            ctx.cmd.SetGlobalTexture(LineShaderIds.EarlyOpaqueDepth, data.opaqueDepth);
                            DrawDepthStrands(data, ctx);
                        }
                    });
                }

                RecordCameraDepthCopy(renderGraph, frameData, resourceData,
                                      "HairLines.EarlyCameraDepth", mergedDepth);
            }

            private void RecordShadows(RenderGraph renderGraph, ContextContainer frameData,
                               UniversalCameraData cameraData, UniversalResourceData resourceData)
            {
                using ProfilerMarker.AutoScope scope = _recordShadowsMarker.Auto();

                using (new RenderGraphProfilingScope(renderGraph, _groupDensityVolumeSampler))
                {
                    RecordDensityVolume(renderGraph, cameraData.camera);
                }

                bool shadowMap;
                using (new RenderGraphProfilingScope(renderGraph, _groupShadowMapSampler))
                {
                    shadowMap = RecordShadowMap(renderGraph, frameData, cameraData.camera, out _);
                }
                if (!shadowMap)
                {
                    DisableShadowGlobals();
                }

                using (new RenderGraphProfilingScope(renderGraph, _groupCascadeCastersSampler))
                {
                    RecordCascadeCasters(renderGraph, frameData, cameraData, resourceData);
                }
            }

            private void RecordCascadeCasters(RenderGraph renderGraph, ContextContainer frameData,
                                              UniversalCameraData cameraData, UniversalResourceData resourceData)
            {
                if (!_settings.CastShadows || !_settings.CastShadowsOnScene || _shadow == null)
                {
                    return;
                }

                var shadowData = frameData.Get<UniversalShadowData>();
                var lightData = frameData.Get<UniversalLightData>();
                var renderingData = frameData.Get<UniversalRenderingData>();
                int lightIndex = lightData.mainLightIndex;
                if (!shadowData.supportsMainLightShadows || lightIndex < 0 ||
                    lightIndex >= lightData.visibleLights.Length)
                {
                    return;
                }

                VisibleLight visibleLight = lightData.visibleLights[lightIndex];
                Light light = visibleLight.light;
                if (light == null || light.shadows == LightShadows.None ||
                    visibleLight.lightType != LightType.Directional)
                {
                    return;
                }

                TextureHandle atlas = resourceData.mainShadowsTexture;
                if (!atlas.IsValid())
                {
                    return;
                }

                int cascadeCount = Mathf.Clamp(shadowData.mainLightShadowCascadesCount, 1, MaxCascades);
                int atlasWidth = shadowData.mainLightShadowmapWidth;
                int atlasHeight = cascadeCount == 2
                    ? shadowData.mainLightShadowmapHeight >> 1
                    : shadowData.mainLightShadowmapHeight;
                TextureDesc atlasDesc = renderGraph.GetTextureDesc(atlas);
                if (atlasDesc.width != atlasWidth || atlasDesc.height != atlasHeight)
                {
                    return;
                }
                int resolution = ShadowUtils.GetMaxTileResolutionInAtlas(
                    shadowData.mainLightShadowmapWidth, shadowData.mainLightShadowmapHeight, cascadeCount);

                Camera camera = cameraData.camera;
                Vector3 lightDir = light.transform.forward;
                HairShadowReceivers.CameraReceivers receivers = null;
                bool receiversKnown = HairShadowReceivers.TryGet(camera, out receivers);

                int drawn = 0;
                for (int cascade = 0; cascade < MaxCascades; cascade++)
                {
                    _cascadeCasters[cascade].renderers.Clear();
                }

                for (int cascade = 0; cascade < cascadeCount; cascade++)
                {
                    CascadeCasters target = _cascadeCasters[cascade];
                    if (!ShadowUtils.ExtractDirectionalLightMatrix(ref renderingData.cullResults, shadowData,
                            lightIndex, cascade, atlasWidth, atlasHeight, resolution, light.shadowNearPlane,
                            out _, out ShadowSliceData slice))
                    {
                        continue;
                    }

                    Vector4 sphere = slice.splitData.cullingSphere;
                    IReadOnlyList<LinePrimitiveRenderer> instances = LinePrimitiveRenderer.Instances;
                    for (int i = 0; i < instances.Count; i++)
                    {
                        LinePrimitiveRenderer r = instances[i];
                        if (r == null || !r.isActiveAndEnabled || r.Model == null || r.Model.EdgeCount == 0 ||
                            (camera.cullingMask & (1 << r.gameObject.layer)) == 0 ||
                            r.PropertyBlock == null || !r.HasDrawMaterial)
                        {
                            continue;
                        }
#if UNITY_EDITOR
                        if (LinePrimitiveRenderer.IsEditorHidden(r, camera))
                        {
                            continue;
                        }
#endif
                        Bounds bounds = r.CullingBounds;
                        if (!CanShadowSphere(bounds, sphere, lightDir) ||
                            (receiversKnown && !HairShadowReceivers.ReceivesShadow(receivers, bounds, lightDir,
                                                                                    slice.resolution)))
                        {
                            continue;
                        }

                        r.MarkRendered();
                        target.renderers.Add(r);
                    }

                    if (target.renderers.Count == 0)
                    {
                        continue;
                    }

                    Vector4 bias = ShadowUtils.GetShadowBias(ref visibleLight, lightIndex, shadowData,
                                                             slice.projectionMatrix, slice.resolution);
                    float texel = 2f / Mathf.Max(Mathf.Abs(slice.projectionMatrix.m00), 1e-6f) /
                                  Mathf.Max(slice.resolution, 1);
                    Vector3 towardsLight = -lightDir;

                    target.viewProjection = GL.GetGPUProjectionMatrix(slice.projectionMatrix, true) *
                                            slice.viewMatrix;
                    target.viewport = new Rect(slice.offsetX, slice.offsetY, slice.resolution, slice.resolution);
                    target.light = new Vector4(towardsLight.x, towardsLight.y, towardsLight.z, bias.x);
                    target.coverage = Mathf.Clamp01(_settings.FibreDiameter * 0.001f / Mathf.Max(texel, 1e-6f)) *
                                      _settings.DensityScale;
                    drawn += target.renderers.Count;
                }

                if (drawn == 0)
                {
                    return;
                }

                if (CollectVisible(camera) != null)
                {
                    TextureDesc copyDesc = atlasDesc;
                    copyDesc.name = "HairLines Main Shadows Without Hair";
                    copyDesc.clearBuffer = false;
                    TextureHandle copy = renderGraph.CreateTexture(copyDesc);

                    using (var copyBuilder = renderGraph.AddUnsafePass<CascadeCopyPassData>(
                               "HairLines.MainShadowsWithoutHair", out var copyData))
                    {
                        copyData.source = atlas;
                        copyData.destination = copy;
                        copyBuilder.UseTexture(atlas, AccessFlags.Read);
                        copyBuilder.UseTexture(copy, AccessFlags.Write);
                        copyBuilder.AllowPassCulling(false);
                        copyBuilder.SetRenderFunc((CascadeCopyPassData data, UnsafeGraphContext ctx) =>
                            ctx.cmd.CopyTexture(data.source, data.destination));
                    }

                    HairFrameResources frameResources = frameData.GetOrCreate<HairFrameResources>();
                    frameResources.mainShadows = atlas;
                    frameResources.mainShadowsWithoutHair = copy;
                }

                using var builder = renderGraph.AddRasterRenderPass<CascadeCasterPassData>(
                    "HairLines.CascadeCasters", out var passData);
                passData.cascades = _cascadeCasters;
                passData.cascadeCount = cascadeCount;
                passData.camera = camera;
                passData.shadow = _shadow;

                builder.SetRenderAttachmentDepth(atlas, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((CascadeCasterPassData data, RasterGraphContext ctx) => DrawCascadeCasters(data, ctx));
            }

            private static void DrawCascadeCasters(CascadeCasterPassData data, RasterGraphContext ctx)
            {
                using (new ProfilingScope(ctx.cmd, _gpuCascadeCastersSampler))
                {
                    ctx.cmd.SetGlobalDepthBias(1f, 2.5f);
                    ctx.cmd.SetGlobalFloat(LineShaderIds.HairShadowOpacityThreshold, OpacityThreshold);

                    for (int cascade = 0; cascade < data.cascadeCount; cascade++)
                    {
                        CascadeCasters target = data.cascades[cascade];
                        if (target.renderers.Count == 0)
                        {
                            continue;
                        }

                        ctx.cmd.SetViewport(target.viewport);
                        ctx.cmd.SetGlobalMatrix(LineShaderIds.HairCascadeViewProj, target.viewProjection);
                        ctx.cmd.SetGlobalVector(LineShaderIds.HairCascadeLight, target.light);
                        ctx.cmd.SetGlobalFloat(LineShaderIds.HairCascadeCoverage, target.coverage);

                        for (int i = 0; i < target.renderers.Count; i++)
                        {
                            DrawCascadeCaster(target.renderers[i], data, ctx);
                        }
                    }

                    ctx.cmd.SetGlobalDepthBias(0f, 0f);
                }
            }

            private static void DrawCascadeCaster(LinePrimitiveRenderer r, CascadeCasterPassData data,
                                                  RasterGraphContext ctx)
            {
                if (r == null || r.Model == null)
                {
                    return;
                }

                float lodFraction = r.GetShadowLodStrandFraction(data.camera);
                float fibreWeight = r.GetShadowFibreWeight(data.camera);
                for (int drawRange = 0; drawRange < r.DrawRangeCount; drawRange++)
                {
                    int vertexCount = r.GetDrawRangeVertexCount(drawRange, lodFraction);
                    MaterialPropertyBlock properties = vertexCount > 0 ? r.GetDrawRangePropertyBlock(drawRange) : null;
                    GraphicsBuffer indices = properties != null ? r.GetDrawRangeIndexBuffer(drawRange) : null;
                    if (indices == null)
                    {
                        continue;
                    }

                    properties.SetFloat(LineShaderIds.ShadowFibreWeight, fibreWeight);
                    ctx.cmd.DrawProcedural(indices, r.DrawMatrix, data.shadow, CascadeCasterPass,
                        MeshTopology.Lines, vertexCount, r.GetDrawRangeInstanceCount(drawRange), properties);
                }
            }

            private static bool CanShadowSphere(Bounds bounds, Vector4 sphere, Vector3 lightDir)
            {
                Vector3 offset = bounds.center - (Vector3)sphere;
                float casterRadius = bounds.extents.magnitude;
                float along = Vector3.Dot(offset, lightDir);
                if (along - casterRadius > sphere.w)
                {
                    return false;
                }

                float radius = sphere.w + casterRadius;
                return offset.sqrMagnitude - along * along <= radius * radius;
            }

            private static SHCoefficients SampleAmbientProbe(Vector3 position)
            {
                LightProbes probes = LightmapSettings.lightProbes;
                SphericalHarmonicsL2 sh;
                if (probes != null && probes.count > 0)
                    LightProbes.GetInterpolatedProbe(position, null, out sh);
                else
                    sh = RenderSettings.ambientProbe;
                return new SHCoefficients(sh);
            }

            private void RecordHair(RenderGraph renderGraph, ContextContainer frameData,
                             UniversalCameraData cameraData, UniversalResourceData resourceData,
                             List<LinePrimitiveRenderer> visible)
            {
                using ProfilerMarker.AutoScope scope = _recordHairMarker.Auto();
                int factor = Mathf.Clamp(_settings.Supersampling, 1, 4);
                float invSigmaSq = 0f;
                int taps;
                HairReconstruction filter = _settings.Filter;
                switch (filter)
                {
                    case HairReconstruction.Gaussian:
                        float sigma = Mathf.Max(0.05f, _settings.GaussianSigmaPixels);
                        taps = sigma <= 0.65f ? 2 * factor : 3 * factor;
                        invSigmaSq = 1f / (sigma * sigma);
                        break;
                    case HairReconstruction.Box:
                        taps = factor;
                        break;
                    default:
                        factor = 1;
                        taps = 1;
                        break;
                }

                var camDesc = cameraData.cameraTargetDescriptor;

                int width = Mathf.Max(1, camDesc.width * factor);
                int height = Mathf.Max(1, camDesc.height * factor);

                bool xrReady = cameraData.xr.enabled;
                var colorDesc = new TextureDesc(width, height, dynamicResolution: false,
                                                xrReady: xrReady)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "HairLinesSubpixelColor",
                };
                var depthDesc = new TextureDesc(width, height, dynamicResolution: false,
                                                xrReady: xrReady)
                {
                    clearBuffer = true,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "HairLinesSubpixelDepth",
                };
                depthDesc.depthBufferBits = DepthBits.Depth32;

                TextureHandle hairColor = renderGraph.CreateTexture(colorDesc);
                TextureHandle hairDepth = renderGraph.CreateTexture(depthDesc);

                bool writeMotion = _settings.MotionVectors != HairMotionVectors.Off &&
                                   _settings.InjectionPoint <= RenderPassEvent.BeforeRenderingPostProcessing &&
                                   UrpBuildsMotionVectors(frameData, cameraData);
                TextureHandle hairMotion = TextureHandle.nullHandle;
                if (writeMotion)
                {
                    var motionDesc = new TextureDesc(width, height, dynamicResolution: false,
                                                     xrReady: xrReady)
                    {
                        format = GraphicsFormat.R16G16_SFloat,
                        clearBuffer = true,
                        clearColor = Color.clear,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        name = "HairLinesSubpixelMotion",
                    };
                    hairMotion = renderGraph.CreateTexture(motionDesc);
                    RegisterMotionVectorDemand();
                }

                bool canPublishHairDepth =
                    _settings.InjectionPoint >= RenderPassEvent.AfterRenderingOpaques;

                HairFrameResources frameResources = frameData.GetOrCreate<HairFrameResources>();
                frameResources.subpixelColor = hairColor;
                frameResources.subpixelDepth = hairDepth;
                frameResources.subpixelMotion = hairMotion;
                frameResources.subpixelSize = new Vector4(width, height, 1f / width, 1f / height);
                frameResources.supersample = factor;
                TextureHandle opaqueDepth = frameResources.opaqueDepth;
                if (!opaqueDepth.IsValid() && canPublishHairDepth &&
                    resourceData.activeDepthTexture.IsValid())
                {
                    opaqueDepth = RecordOpaqueDepthSnapshot(renderGraph, frameData, cameraData,
                                                            resourceData.activeDepthTexture);
                    frameResources.opaqueDepth = opaqueDepth;
                }

                bool primeDepth = opaqueDepth.IsValid();

                if (PrepassIsWorthIt && _depth != null)
                {
                    using (var builder = renderGraph.AddRasterRenderPass<SubpixelPrepassData>(
                               "HairLines.SubpixelDepthPrepass", out var prepassData))
                    {
                        prepassData.renderers = visible;
                        prepassData.camera = cameraData.camera;
                        prepassData.depth = _depth;
                        prepassData.resolve = _resolve;
                        prepassData.primeDepth = primeDepth;

                        builder.SetRenderAttachmentDepth(hairDepth, AccessFlags.Write);
                        if (primeDepth)
                        {
                            prepassData.sceneDepth = opaqueDepth;
                            builder.UseTexture(prepassData.sceneDepth, AccessFlags.Read);
                        }

                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc((SubpixelPrepassData data, RasterGraphContext ctx) =>
                        {
                            using (new ProfilingScope(ctx.cmd, _gpuSubpixelDepthSampler))
                            {
                                if (data.primeDepth)
                                {
                                    Blitter.BlitTexture(ctx.cmd, data.sceneDepth,
                                        new Vector4(1f, 1f, 0f, 0f), data.resolve, DepthPrimePass);
                                }

                                DrawDepthStrands(new DepthPassData
                                {
                                    renderers = data.renderers,
                                    camera = data.camera,
                                    depth = data.depth,
                                    shaderPass = DepthOnlyPass,
                                }, ctx);
                            }
                        });
                    }

                    primeDepth = false;
                }

                using (var builder = renderGraph.AddRasterRenderPass<LinesPassData>("HairLines.RasterizeLines", out var passData))
                {
                    passData.renderers = visible;
                    passData.camera = cameraData.camera;
                    passData.resolve = _resolve;
                    passData.primeDepth = primeDepth;
                    passData.opaqueDepth = opaqueDepth;
                    passData.hasOpaqueDepth = opaqueDepth.IsValid();
                    passData.shadowData = frameData.Get<UniversalShadowData>();
                    passData.fibreDiameter = _settings.FibreDiameter;
                    passData.subpixelSize = new Vector4(width, height, 1f / width, 1f / height);
                    passData.mainLightSourceAngle = HairLightSource.CollectSources(
                        frameData.Get<UniversalLightData>(), out passData.lightSources,
                        out passData.lightShadowStrengths);

                    builder.SetRenderAttachment(hairColor, 0, AccessFlags.Write);
                    passData.writeMotion = writeMotion;
                    if (writeMotion)
                    {
                        builder.SetRenderAttachment(hairMotion, 1, AccessFlags.Write);
                    }
                    builder.SetRenderAttachmentDepth(hairDepth,
                        PrepassIsWorthIt && _depth != null ? AccessFlags.Read : AccessFlags.Write);

                    if (passData.hasOpaqueDepth)
                    {
                        builder.UseTexture(passData.opaqueDepth, AccessFlags.Read);
                    }

                    passData.mainShadows = frameResources.mainShadows;
                    passData.mainShadowsWithoutHair = frameResources.mainShadowsWithoutHair;
                    if (passData.mainShadowsWithoutHair.IsValid())
                    {
                        builder.UseTexture(passData.mainShadowsWithoutHair, AccessFlags.Read);
                        builder.UseTexture(passData.mainShadows, AccessFlags.Read);
                    }

                    builder.UseAllGlobalTextures(true);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    if (cameraData.xr.enabled)
                    {
                        bool passSupportsFoveation = factor == 1 &&
                            resourceData.isActiveTargetBackBuffer;
                        builder.EnableFoveatedRasterization(
                            cameraData.xr.supportsFoveatedRendering && passSupportsFoveation);
                        builder.SetExtendedFeatureFlags(
                            ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
                    }

                    builder.SetRenderFunc((LinesPassData data, RasterGraphContext ctx) =>
                    {
                        using (new ProfilingScope(ctx.cmd, _gpuRasterSampler))
                        {
                            using ProfilerMarker.AutoScope scope = _rasterizeLinesMarker.Auto();
                            MainLightShadowKeywords.Restore(ctx.cmd, data.shadowData);
                            if (data.mainShadowsWithoutHair.IsValid())
                            {
                                ctx.cmd.SetGlobalTexture(LineShaderIds.MainLightShadowmapTexture,
                                                         data.mainShadowsWithoutHair);
                            }

                        ctx.cmd.SetGlobalVector(LineShaderIds.HairSubpixelSize, data.subpixelSize);
                        ctx.cmd.SetGlobalFloat(LineShaderIds.HairMainLightSourceAngle,
                                               data.mainLightSourceAngle);
                        ctx.cmd.SetGlobalVectorArray(LineShaderIds.HairLightSources, data.lightSources);
                        ctx.cmd.SetGlobalFloatArray(LineShaderIds.HairLightShadowStrengths,
                                                    data.lightShadowStrengths);
                        if (data.hasOpaqueDepth)
                        {
                            ctx.cmd.SetGlobalTexture(LineShaderIds.EarlyOpaqueDepth,
                                                     data.opaqueDepth);
                        }

                        if (data.primeDepth)
                        {
                            Blitter.BlitTexture(ctx.cmd, data.opaqueDepth,
                                new Vector4(1f, 1f, 0f, 0f), data.resolve, DepthPrimePass);
                        }

                        ctx.cmd.SetKeyword(_motionVectorsKeyword, data.writeMotion);

                        for (int i = 0; i < data.renderers.Count; i++)
                        {
                            LinePrimitiveRenderer r = data.renderers[i];
                            if (r == null)
                            {
                                continue;
                            }
                            LinePrimitiveModel model = r.Model;
                            float lodFraction = r.GetLodStrandFraction(data.camera);
                            if (model == null || r.GetLodEdgeCount(data.camera, lodFraction) == 0)
                            {
                                continue;
                            }

                            SHCoefficients ambient = SampleAmbientProbe(r.WorldBounds.center);
                            SkyWeights(ambient, out Vector4 skyWeightsPos, out Vector4 skyWeightsNeg);
                            float coverageScale = r.GetLodCoverageScale(data.camera);
                            float depthScale = r.GetLodDepthScale(data.camera);
                            float thickDensity = r.GetThickStrandDensity(data.fibreDiameter);
                            for (int drawRange = 0; drawRange < r.DrawRangeCount; drawRange++)
                            {
                                int vertexCount = r.GetDrawRangeVertexCount(drawRange, lodFraction);
                                if (vertexCount == 0)
                                {
                                    continue;
                                }

                                MaterialPropertyBlock properties = r.GetDrawRangePropertyBlock(drawRange);
                                if (properties == null)
                                {
                                    continue;
                                }

                                GraphicsBuffer indices = r.GetDrawRangeIndexBuffer(drawRange);
                                if (indices == null)
                                {
                                    continue;
                                }

                                properties.SetVector(LineShaderIds.StrandRange,
                                    model.StrandRangeForDrawRange(drawRange, lodFraction));
                                properties.SetFloat(LineShaderIds.LodCoverageScale, coverageScale);
                                properties.SetFloat(LineShaderIds.LodDepthScale, depthScale);
                                properties.SetFloat(LineShaderIds.ThickDensity, thickDensity);
                                properties.SetVector(LineShaderIds.HairSHAr, ambient.SHAr);
                                properties.SetVector(LineShaderIds.HairSHAg, ambient.SHAg);
                                properties.SetVector(LineShaderIds.HairSHAb, ambient.SHAb);
                                properties.SetVector(LineShaderIds.HairSHBr, ambient.SHBr);
                                properties.SetVector(LineShaderIds.HairSHBg, ambient.SHBg);
                                properties.SetVector(LineShaderIds.HairSHBb, ambient.SHBb);
                                properties.SetVector(LineShaderIds.HairSHC, ambient.SHC);
                                properties.SetVector(LineShaderIds.HairSkyWeightsPos, skyWeightsPos);
                                properties.SetVector(LineShaderIds.HairSkyWeightsNeg, skyWeightsNeg);
                                if (data.writeMotion)
                                {
                                    properties.SetBuffer(LineShaderIds.PrevPositions,
                                                         r.GetPreviousPositionBuffer());
                                    properties.SetMatrix(LineShaderIds.PrevObjectToWorld,
                                                         r.GetPreviousDrawMatrix());
                                }
                                ctx.cmd.DrawProcedural(indices, r.DrawMatrix,
                                    r.GetDrawRangeMaterial(drawRange), 0,
                                    MeshTopology.Lines, vertexCount, r.GetDrawRangeInstanceCount(drawRange), properties);
                            }
                        }

                        ctx.cmd.SetKeyword(_motionVectorsKeyword, false);
                        if (data.mainShadowsWithoutHair.IsValid())
                        {
                            ctx.cmd.SetGlobalTexture(LineShaderIds.MainLightShadowmapTexture, data.mainShadows);
                        }
                        }
                    });
                }

                if (_depth != null && resourceData.activeDepthTexture.IsValid() &&
                    canPublishHairDepth)
                {
#if UNITY_EDITOR
                    if (HideHairFromSceneViewDepth && cameraData.camera.cameraType == CameraType.SceneView)
                    {
                        frameResources.sceneViewDepthWithoutHair = RecordOpaqueDepthSnapshot(
                            renderGraph, frameData, cameraData, resourceData.activeDepthTexture);
                    }
#endif

                    using (var builder = renderGraph.AddRasterRenderPass<DepthPassData>(
                               "HairLines.Depth", out var depthPassData))
                    {
                        depthPassData.renderers = visible;
                        depthPassData.camera = cameraData.camera;
                        depthPassData.depth = _depth;
                        depthPassData.dependency = hairColor;
                        depthPassData.opaqueDepth = default;
                        depthPassData.shaderPass = DepthPrimePass;

                        if (depthPassData.dependency.IsValid())
                        {
                            builder.UseTexture(depthPassData.dependency, AccessFlags.Read);
                        }
                        builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture,
                                                          AccessFlags.ReadWrite);
                        if (cameraData.xr.enabled)
                        {
                            bool passSupportsFoveation = factor == 1 &&
                                resourceData.isActiveTargetBackBuffer;
                            builder.EnableFoveatedRasterization(
                                cameraData.xr.supportsFoveatedRendering && passSupportsFoveation);
                            builder.SetExtendedFeatureFlags(
                                ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
                        }
                        builder.AllowPassCulling(false);

                        builder.SetRenderFunc((DepthPassData data, RasterGraphContext ctx) =>
                        {
                            using (new ProfilingScope(ctx.cmd, _gpuDepthPublishSampler))
                            {
                                DrawDepthStrands(data, ctx);
                            }
                        });
                    }

                    RecordCameraDepthCopy(renderGraph, frameData, resourceData,
                                          "HairLines.CameraDepth", resourceData.activeDepthTexture);
                }

                using (var builder = renderGraph.AddRasterRenderPass<ResolvePassData>("HairLines.Resolve", out var passData))
                {
                    passData.hairColor = hairColor;
                    passData.resolve = _resolve;
                    passData.texSize = new Vector4(width, height, 1f / width, 1f / height);
                    passData.taps = taps;
                    passData.invSigmaSq = invSigmaSq;
                    passData.invSupersample = 1f / factor;

                    passData.depthAware = opaqueDepth.IsValid() && taps > factor;
                    passData.hairDepth = hairDepth;
                    passData.opaqueDepth = opaqueDepth;

                    builder.UseTexture(hairColor, AccessFlags.Read);
                    if (passData.depthAware)
                    {
                        builder.UseTexture(hairDepth, AccessFlags.Read);
                        builder.UseTexture(opaqueDepth, AccessFlags.Read);
                    }
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                    if (cameraData.xr.enabled)
                    {
                        builder.EnableFoveatedRasterization(false);
                        builder.SetExtendedFeatureFlags(
                            ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
                    }
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc((ResolvePassData data, RasterGraphContext ctx) =>
                    {
                        using (new ProfilingScope(ctx.cmd, _gpuResolveSampler))
                        {
                            using ProfilerMarker.AutoScope scope = _resolveMarker.Auto();
                            data.resolve.SetVector(LineShaderIds.HairTexSize, data.texSize);
                            data.resolve.SetFloat(LineShaderIds.FilterTaps, data.taps);
                            data.resolve.SetFloat(LineShaderIds.FilterInvSigmaSq, data.invSigmaSq);
                            data.resolve.SetFloat(LineShaderIds.InvSupersample, data.invSupersample);
                            data.resolve.SetFloat(LineShaderIds.HairResolveDepthAware,
                                                  data.depthAware ? 1f : 0f);
                            if (data.depthAware)
                            {
                                data.resolve.SetTexture(LineShaderIds.HairSubpixelDepth, data.hairDepth);
                                data.resolve.SetTexture(LineShaderIds.EarlyOpaqueDepth, data.opaqueDepth);
                            }

                            Blitter.BlitTexture(ctx.cmd, data.hairColor, new Vector4(1f, 1f, 0f, 0f), data.resolve, ResolvePass);
                        }
                    });
                }
            }

            private static bool UrpBuildsMotionVectors(ContextContainer frameData,
                                                       UniversalCameraData cameraData)
            {
                Camera camera = cameraData.camera;
                if (cameraData.antialiasing == AntialiasingMode.TemporalAntiAliasing &&
                    cameraData.postProcessEnabled &&
                    cameraData.cameraTargetDescriptor.msaaSamples == 1 &&
                    cameraData.renderType != CameraRenderType.Overlay &&
                    !camera.allowDynamicResolution)
                {
                    bool stacked = camera.TryGetComponent(out UniversalAdditionalCameraData extra) &&
                                   extra.cameraStack != null && extra.cameraStack.Count > 0;
                    if (!stacked)
                    {
                        return true;
                    }
                }

                UniversalPostProcessingData postProcessing = frameData.Get<UniversalPostProcessingData>();
                if (postProcessing != null && postProcessing.isEnabled)
                {
                    var motionBlur = VolumeManager.instance.stack.GetComponent<MotionBlur>();
                    if (motionBlur != null && motionBlur.IsActive() &&
                        motionBlur.mode.value == MotionBlurMode.CameraAndObjects)
                    {
                        return true;
                    }
                }

                return false;
            }

            private void RecordMotionVectors(RenderGraph renderGraph, ContextContainer frameData,
                                             UniversalCameraData cameraData,
                                             UniversalResourceData resourceData)
            {
                if (!frameData.Contains<HairFrameResources>())
                {
                    return;
                }

                HairFrameResources frameResources = frameData.Get<HairFrameResources>();
                if (!frameResources.subpixelMotion.IsValid() || !frameResources.subpixelColor.IsValid() ||
                    !resourceData.motionVectorColor.IsValid() || _resolve == null)
                {
                    return;
                }

                using var builder = renderGraph.AddRasterRenderPass<MotionVectorPassData>(
                    "HairLines.MotionVectors", out var passData);
                passData.hairColor = frameResources.subpixelColor;
                passData.hairDepth = frameResources.subpixelDepth;
                passData.hairMotion = frameResources.subpixelMotion;
                passData.resolve = _resolve;
                passData.texSize = frameResources.subpixelSize;
                passData.invSupersample = 1f / Mathf.Max(1, frameResources.supersample);
                passData.mode = (float)_settings.MotionVectors;

                builder.UseTexture(passData.hairColor, AccessFlags.Read);
                builder.UseTexture(passData.hairDepth, AccessFlags.Read);
                builder.UseTexture(passData.hairMotion, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.motionVectorColor, 0, AccessFlags.ReadWrite);
                if (cameraData.xr.enabled)
                {
                    builder.EnableFoveatedRasterization(false);
                    builder.SetExtendedFeatureFlags(ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
                }
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((MotionVectorPassData data, RasterGraphContext ctx) =>
                {
                    data.resolve.SetVector(LineShaderIds.HairTexSize, data.texSize);
                    data.resolve.SetFloat(LineShaderIds.InvSupersample, data.invSupersample);
                    data.resolve.SetFloat(LineShaderIds.HairMotionVectorMode, data.mode);
                    data.resolve.SetTexture(LineShaderIds.HairSubpixelDepth, data.hairDepth);
                    data.resolve.SetTexture(LineShaderIds.HairSubpixelMotion, data.hairMotion);
                    Blitter.BlitTexture(ctx.cmd, data.hairColor, new Vector4(1f, 1f, 0f, 0f),
                                        data.resolve, MotionVectorResolvePass);
                });
            }

            private TextureHandle RecordOpaqueDepthSnapshot(RenderGraph renderGraph,
                                                             ContextContainer frameData,
                                                             UniversalCameraData cameraData,
                                                             TextureHandle source)
            {
                if (!source.IsValid())
                {
                    return TextureHandle.nullHandle;
                }

                RenderTargetInfo sourceInfo;
                try
                {
                    sourceInfo = renderGraph.GetRenderTargetInfo(source);
                }
                catch (System.Exception)
                {
                    return TextureHandle.nullHandle;
                }

                if (sourceInfo.width <= 0 || sourceInfo.height <= 0 ||
                    sourceInfo.volumeDepth <= 0)
                {
                    return TextureHandle.nullHandle;
                }

                TextureDesc snapshotDesc;
                try
                {
                    snapshotDesc = renderGraph.GetTextureDesc(source);
                }
                catch (System.Exception)
                {
                    bool xrReady = cameraData.xr.enabled && sourceInfo.volumeDepth > 1;
                    snapshotDesc = new TextureDesc(sourceInfo.width, sourceInfo.height,
                                                   dynamicResolution: false, xrReady: xrReady)
                    {
                        slices = Mathf.Max(1, sourceInfo.volumeDepth),
                        dimension = sourceInfo.volumeDepth > 1
                            ? TextureDimension.Tex2DArray
                            : TextureDimension.Tex2D,
                    };
                }

                if (snapshotDesc.width <= 0 || snapshotDesc.height <= 0 ||
                    snapshotDesc.slices <= 0)
                {
                    snapshotDesc.width = sourceInfo.width;
                    snapshotDesc.height = sourceInfo.height;
                    snapshotDesc.slices = Mathf.Max(1, sourceInfo.volumeDepth);
                    snapshotDesc.dimension = sourceInfo.volumeDepth > 1
                        ? TextureDimension.Tex2DArray
                        : TextureDimension.Tex2D;
                }

                Shader shader = ResolveCopyDepthShader();
                if (shader == null)
                {
                    return TextureHandle.nullHandle;
                }

                EnsureCopyDepthPasses(shader);
                if (_copyCameraDepth == null)
                {
                    return TextureHandle.nullHandle;
                }

                snapshotDesc.name = "HairLinesOpaqueDepthSnapshot";
                snapshotDesc.format = GraphicsFormat.R32_SFloat;
                snapshotDesc.depthBufferBits = DepthBits.None;
                snapshotDesc.msaaSamples = MSAASamples.None;
                snapshotDesc.bindTextureMS = false;
                snapshotDesc.clearBuffer = false;
                snapshotDesc.filterMode = FilterMode.Point;
                snapshotDesc.wrapMode = TextureWrapMode.Clamp;

                TextureHandle snapshot = renderGraph.CreateTexture(snapshotDesc);
                _copyCameraDepth.Render(renderGraph, frameData, snapshot, source,
                                        bindAsCameraDepth: false,
                                        passName: "HairLines.OpaqueDepthSnapshot");
                return snapshot;
            }

            private void RecordSceneViewDepthRestore(RenderGraph renderGraph, ContextContainer frameData,
                                                     UniversalResourceData resourceData)
            {
                TextureHandle source = frameData.GetOrCreate<HairFrameResources>().sceneViewDepthWithoutHair;
                TextureHandle destination = resourceData.cameraDepth;
                Shader shader = ResolveCopyDepthShader();
                if (!destination.IsValid() || shader == null)
                {
                    return;
                }

                EnsureCopyDepthPasses(shader);
                if (source.IsValid())
                {
                    _copyCameraDepthToDepth.Render(renderGraph, frameData, destination, source,
                                                   bindAsCameraDepth: false,
                                                   passName: "HairLines.SceneViewDepthWithoutHair");
                }

#if UNITY_EDITOR
                RenderTargetInfo info;
                try
                {
                    info = renderGraph.GetRenderTargetInfo(destination);
                }
                catch (System.Exception)
                {
                    return;
                }

                if (info.width <= 0 || info.height <= 0 || info.volumeDepth != 1)
                {
                    return;
                }

                Camera camera = frameData.Get<UniversalCameraData>().camera;
                TextureHandle export = renderGraph.ImportTexture(SceneViewDepthCopy(camera, info.width, info.height));
                _copyCameraDepth.Render(renderGraph, frameData, export, destination,
                                        bindAsCameraDepth: false,
                                        passName: "HairLines.SceneViewDepthExport");
#endif
            }

            private void RecordCameraDepthCopy(RenderGraph renderGraph, ContextContainer frameData,
                                               UniversalResourceData resourceData, string passName,
                                               TextureHandle source)
            {
                TextureHandle destination = resourceData.cameraDepthTexture;
                if (!destination.IsValid() || !source.IsValid() || destination == source)
                {
                    return;
                }

                Shader shader = ResolveCopyDepthShader();
                if (shader == null)
                {
                    if (!_copyDepthWarningIssued)
                    {
                        Debug.LogWarning("[HairLines] URP CopyDepth shader is unavailable; " +
                                         "hair depth cannot be published to CameraDepthTexture.");
                        _copyDepthWarningIssued = true;
                    }
                    return;
                }

                EnsureCopyDepthPasses(shader);
                GraphicsFormat destinationFormat = GetRenderTargetFormatSafe(renderGraph, destination);
                if (destinationFormat == GraphicsFormat.None)
                {
                    return;
                }
                bool destinationIsDepth = GraphicsFormatUtility.IsDepthFormat(destinationFormat);
                CopyDepthPass copy = destinationIsDepth ? _copyCameraDepthToDepth : _copyCameraDepth;
                if (copy == null)
                {
                    return;
                }

                copy.Render(renderGraph, frameData, destination, source,
                            bindAsCameraDepth: true, passName: passName);
            }

            private static GraphicsFormat GetRenderTargetFormatSafe(RenderGraph renderGraph,
                                                                     TextureHandle texture)
            {
                try
                {
                    return renderGraph.GetRenderTargetInfo(texture).format;
                }
                catch (System.Exception)
                {
                    return GraphicsFormat.None;
                }
            }

            private Shader ResolveCopyDepthShader()
            {
                if (_copyDepthShader != null)
                {
                    return _copyDepthShader;
                }

                if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRendererResources>(
                        out UniversalRendererResources resources))
                {
                    _copyDepthShader = resources.copyDepthPS;
                }

                _copyDepthShader ??= Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
                return _copyDepthShader;
            }

            private void EnsureCopyDepthPasses(Shader shader)
            {
                if (_copyCameraDepth != null && _copyDepthShader == shader)
                {
                    return;
                }

                _copyCameraDepth?.Dispose();
                _copyCameraDepthToDepth?.Dispose();
                _copyCameraDepth = new CopyDepthPass(RenderPassEvent.AfterRendering, shader,
                                                      copyToDepth: false,
                                                      customPassName: "HairLines.CameraDepth");
                _copyCameraDepthToDepth = new CopyDepthPass(RenderPassEvent.AfterRendering, shader,
                                                             copyToDepth: true,
                                                             customPassName: "HairLines.CameraDepthToDepth");
                _copyDepthShader = shader;
                _copyDepthWarningIssued = false;
            }

            public void Dispose()
            {
                _copyCameraDepth?.Dispose();
                _copyCameraDepth = null;
                _copyCameraDepthToDepth?.Dispose();
                _copyCameraDepthToDepth = null;
                _copyDepthShader = null;
            }

            private static void DrawDepthStrands(DepthPassData data, RasterGraphContext ctx)
            {
                for (int i = 0; i < data.renderers.Count; i++)
                {
                    LinePrimitiveRenderer renderer = data.renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }
#if UNITY_EDITOR
                    if (LinePrimitiveRenderer.IsEditorHidden(renderer))
                    {
                        continue;
                    }
#endif

                    LinePrimitiveModel model = renderer.Model;
                    float lodFraction = renderer.GetLodStrandFraction(data.camera);
                    if (model == null || renderer.GetLodEdgeCount(data.camera, lodFraction) == 0)
                    {
                        continue;
                    }

                    for (int drawRange = 0; drawRange < renderer.DrawRangeCount; drawRange++)
                    {
                        int vertexCount = renderer.GetDrawRangeVertexCount(drawRange, lodFraction);
                        if (vertexCount == 0)
                        {
                            continue;
                        }

                        Material source = renderer.GetDrawRangeMaterial(drawRange);
                        MaterialPropertyBlock properties = renderer.GetDrawRangePropertyBlock(drawRange);
                        if (properties == null)
                        {
                            continue;
                        }
                        properties.SetFloat(LineShaderIds.DepthOpacityThreshold,
                            source != null && source.HasProperty(LineShaderIds.OpacityThreshold)
                                ? source.GetFloat(LineShaderIds.OpacityThreshold)
                                : 0.5f);
                        properties.SetFloat(LineShaderIds.DepthStochasticOpacity,
                            source != null && source.IsKeywordEnabled("_STOCHASTIC_OPACITY") ? 1f : 0f);
                        properties.SetFloat(LineShaderIds.DepthTipTaperLength,
                            source != null && source.HasProperty(LineShaderIds.TipTaperLength)
                                ? source.GetFloat(LineShaderIds.TipTaperLength)
                                : 0f);
                        properties.SetFloat(LineShaderIds.DepthTipWidth,
                            source != null && source.HasProperty(LineShaderIds.TipWidth)
                                ? source.GetFloat(LineShaderIds.TipWidth)
                                : 1f);

                        GraphicsBuffer indices = renderer.GetDrawRangeIndexBuffer(drawRange);
                        if (indices == null)
                        {
                            continue;
                        }

                        ctx.cmd.DrawProcedural(indices, renderer.DrawMatrix, data.depth,
                            data.shaderPass, MeshTopology.Lines, vertexCount, renderer.GetDrawRangeInstanceCount(drawRange), properties);
                    }
                }
            }

            private List<LinePrimitiveRenderer> CollectVisible(Camera camera)
            {
                GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);
                _visibleScratch.Clear();
                var instances = LinePrimitiveRenderer.Instances;
                for (int i = 0; i < instances.Count; i++)
                {
                    var r = instances[i];
                    if (r == null || !r.isActiveAndEnabled)
                    {
                        continue;
                    }
#if UNITY_EDITOR
                    if (LinePrimitiveRenderer.IsEditorHidden(r, camera))
                    {
                        continue;
                    }
#endif

                    var model = r.Model;
                    if (model == null || model.EdgeCount == 0)
                    {
                        continue;
                    }
                    if ((camera.cullingMask & (1 << r.gameObject.layer)) == 0)
                    {
                        continue;
                    }
                    if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, r.CullingBounds))
                    {
                        continue;
                    }

                    if (r.GetLodEdgeCount(camera, r.GetLodStrandFraction(camera)) == 0)
                    {
                        continue;
                    }

                    if (r.PropertyBlock == null || !r.HasDrawMaterial)
                    {
                        continue;
                    }

                    r.MarkRendered();
                    _visibleScratch.Add(r);
                }
                return _visibleScratch.Count > 0 ? _visibleScratch : null;
            }
        }
    }
}
