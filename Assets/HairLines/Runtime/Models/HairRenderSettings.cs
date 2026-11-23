using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace HairLines
{
    [Serializable]
    public class HairRenderSettings
    {
        public int Supersampling { get => _supersampling; set => _supersampling = Mathf.Clamp(value, 1, 4); }
        public HairReconstruction Filter { get => _filter; set => _filter = value; }
        public float GaussianSigmaPixels { get => _gaussianSigmaPixels; set => _gaussianSigmaPixels = Mathf.Clamp(value, 0.1f, 2f); }
        public RenderPassEvent InjectionPoint { get => _injectionPoint; set => _injectionPoint = value; }
        public bool RenderInSceneView { get => _renderInSceneView; set => _renderInSceneView = value; }
        public HairDebugMode DebugMode { get => _debugMode; set => _debugMode = value; }
        public HairMotionVectors MotionVectors { get => _motionVectors; set => _motionVectors = value; }

        public bool CastShadows { get => _castShadows; set => _castShadows = value; }
        public bool CastShadowsOnScene { get => _castShadowsOnScene; set => _castShadowsOnScene = value; }
        public HairShadowResolution ShadowResolution { get => _shadowResolution; set => _shadowResolution = value; }
        public float FibreDiameter { get => _fibreDiameter; set => _fibreDiameter = Mathf.Clamp(value, 0.005f, 0.5f); }
        public float DensityScale { get => _densityScale; set => _densityScale = Mathf.Clamp(value, 0.1f, 8f); }
        public float LayerDistribution { get => _layerDistribution; set => _layerDistribution = Mathf.Clamp01(value); }
        public float ShadowDepthBias { get => _shadowDepthBias; set => _shadowDepthBias = Mathf.Max(0f, value); }
        public float Softness { get => _softness; set => _softness = Mathf.Clamp(value, 0f, 40f); }
        public bool VolumeShadows { get => _volumeShadows; set => _volumeShadows = value; }
        public HairVolumeResolution VolumeResolution { get => _volumeResolution; set => _volumeResolution = value; }
        public HairSkyOcclusion SkyOcclusion { get => _skyOcclusion; set => _skyOcclusion = value; }
        public HairSkyRayMarchSettings SkyRayMarch => _skyRayMarch;
        public HairSkyConeTraceSettings SkyConeTrace => _skyConeTrace;
        public HairSkyGridSettings SkyGrid => _skyGrid;

        [Tooltip("Subpixel grid factor s. The paper rasterizes on a 2x grid; the target is s*s times the camera pixel count.")]
        [SerializeField, FormerlySerializedAs("supersampling"), Range(1, 4)]
        private int _supersampling = 2;

        [Tooltip("Reconstruction filter over the subpixel grid.")]
        [SerializeField, FormerlySerializedAs("filter")]
        private HairReconstruction _filter = HairReconstruction.Gaussian;

        [Tooltip("Standard deviation of the reconstruction filter, in final-image pixels. The paper uses 0.5.")]
        [SerializeField, FormerlySerializedAs("gaussianSigmaPixels"), Range(0.1f, 2f)]
        private float _gaussianSigmaPixels = 0.5f;

        [Tooltip("Where the strand pass is injected into the URP frame.")]
        [SerializeField, FormerlySerializedAs("injectionPoint")]
        private RenderPassEvent _injectionPoint = RenderPassEvent.BeforeRenderingTransparents;

        [Tooltip("Draw the strands in the Scene view as well as in the Game view.")]
        [SerializeField, FormerlySerializedAs("renderInSceneView")]
        private bool _renderInSceneView = true;

        [Tooltip("Replaces hair shading with a diagnostic colour. Off ships the groom as it looks.")]
        [SerializeField]
        private HairDebugMode _debugMode = HairDebugMode.Off;

        [Tooltip("Write the strands into URP's motion vector texture, so TAA, STP and object " +
                 "motion blur track moving hair. Only on cameras where URP builds motion vectors " +
                 "at all. Closest: the nearest strand under a pixel. Fastest: the fastest one.")]
        [SerializeField]
        private HairMotionVectors _motionVectors = HairMotionVectors.Closest;

        [Tooltip("Build a deep opacity map from the main light, so strands shadow each other " +
                 "and the scene. The strands are drawn procedurally, so URP's own shadow pass " +
                 "never sees them.")]
        [SerializeField, FormerlySerializedAs("castShadows")]
        private bool _castShadows = true;

        [Tooltip("Cast the hair shadow onto the rest of the scene, by drawing the strands into " +
                 "URP's main light cascades. Turn off to keep strand-on-strand shadowing only.")]
        [SerializeField, FormerlySerializedAs("castShadowsOnScene")]
        private bool _castShadowsOnScene = true;

        [Tooltip("Resolution of the light-space opacity map, in texels.")]
        [SerializeField, FormerlySerializedAs("shadowResolution")]
        private HairShadowResolution _shadowResolution = HairShadowResolution._1024;

        [Tooltip("Diameter of one fibre, in millimetres. Real hair is 0.05-0.10 mm. This is " +
                 "how much light a single strand blocks, so it - together with the map's " +
                 "texel size - fixes the density of the shadow in physical units; it does " +
                 "not need retuning when the map resolution or the model scale changes.")]
        [SerializeField, FormerlySerializedAs("fibreDiameter"), Range(0.005f, 0.5f)]
        private float _fibreDiameter = 0.07f;

        [Tooltip("Multiplies the optical depth. Raise it when the strand count " +
                 "under-represents a real head of hair - a groom of 20k guide strands stands " +
                 "in for 100k+ fibres, and that ratio is the honest place to put it.")]
        [SerializeField, FormerlySerializedAs("densityScale"), Range(0.1f, 8f)]
        private float _densityScale = 1f;

        [Tooltip("Where the four opacity layers sit inside the hair volume. 0 spaces them " +
                 "evenly; higher values crowd them towards the lit surface, where " +
                 "self-shadowing detail is visible. The depths themselves are derived from " +
                 "the hair's own extent along the light, so no distance has to be entered.")]
        [SerializeField, FormerlySerializedAs("layerDistribution"), Range(0f, 1f)]
        private float _layerDistribution = 0.6f;

        [Tooltip("Offset in metres before a strand starts shadowing itself.")]
        [SerializeField, FormerlySerializedAs("shadowDepthBias"), Min(0f)]
        private float _shadowDepthBias = 0.002f;

        [Tooltip("Width of the penumbra, in millimetres. The sun subtends half a degree, so " +
                 "at arm's length its own penumbra is about 5 mm, and a fibre is far below " +
                 "that - which is why a hair shadow is soft. Given in millimetres rather " +
                 "than in map texels so it stays the same shadow at any Shadow Resolution.")]
        [SerializeField, FormerlySerializedAs("softness"), Range(0f, 40f)]
        private float _softness = 6f;
        [Tooltip("Voxelize each groom's fibre density once per frame, so lights other than the " +
                 "main one shadow the hair and light its rim through it. The opacity map is a " +
                 "projection from one light and cannot answer a second; a world-space volume " +
                 "answers all of them from one build. Costs a raster and a compute pass per frame and a ray " +
                 "march per additional light, so it is off until a scene needs it.")]
        [SerializeField]
        private bool _volumeShadows;

        [Tooltip("Side of the density volume, in voxels. A head is about a quarter of a metre " +
                 "across, so 64 puts a voxel near four millimetres. Memory is this cubed per " +
                 "casting groom; the cost of reading it does not change with resolution.")]
        [SerializeField]
        private HairVolumeResolution _volumeResolution = HairVolumeResolution._64;

        [Tooltip("How the rest of the groom occludes the sky on a strand, read from the density " +
                 "volume. Off: no occlusion. Ray March: six axis rays through the full volume. " +
                 "Cone Trace: six cones over a prefiltered level chain, far fewer samples. " +
                 "Precomputed Grid: the cone trace on a coarse grid, one sample per pixel, for phones.")]
        [SerializeField]
        private HairSkyOcclusion _skyOcclusion = HairSkyOcclusion.RayMarch;

        [SerializeField]
        private HairSkyRayMarchSettings _skyRayMarch = new HairSkyRayMarchSettings();

        [SerializeField]
        private HairSkyConeTraceSettings _skyConeTrace = new HairSkyConeTraceSettings();

        [SerializeField]
        private HairSkyGridSettings _skyGrid = new HairSkyGridSettings();
    }
}
