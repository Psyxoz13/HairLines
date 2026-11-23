using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using Debug = UnityEngine.Debug;

namespace HairLines
{
    [ExecuteAlways]
    [RequireComponent(typeof(LinePrimitiveRenderer))]
    [DefaultExecutionOrder(-90)]
    [AddComponentMenu("Hair Lines/Hair Simulation")]
    public class LineHairSimulation : MonoBehaviour
    {
        public bool IsReady => _ready;

        public int StretchSpringCount => _constraints?.StretchCount ?? 0;
        public int BendSpringCount => _constraints?.BendCount ?? 0;
        public int VertexCount => _model?.VertexCount ?? 0;
        public int ControlVertexCount => _constraints?.ControlCount ?? _controlCount;
        public int SimulationStateCount => _simulationStateCount;
        public float SimulationStateFraction => VertexCount > 0
            ? _simulationStateCount / (float)VertexCount
            : 0f;
        public int XpbdLocalEdgeCount => _constraints?.XpbdLocalEdgeCount ?? 0;
        public bool VolumeActive => _volume != null && _volume.Active;
        public int VolumeResolution
        {
            get => _volumeResolution;
            set => _volumeResolution = Mathf.Clamp(value, 8, 64);
        }

        public bool FusedLocalSolver
        {
            get => _fusedLocalSolver;
            set
            {
                if (_fusedLocalSolver == value)
                {
                    return;
                }

                _fusedLocalSolver = value;
                if (_ready)
                {
                    Release();
                }

                RefreshFusedLocalBackend();
            }
        }

        public bool FusedLocalSolverActive => _fusedLocalActive;

        public long FusedMappingBytes
        {
            get
            {
                if (!_ready || _constraints == null || !_constraints.HasFusedLocalGraph)
                {
                    return 0L;
                }

                long strands = Math.Max(1L, _model?.Strands?.Length ?? 0);
                return strands * sizeof(uint) +
                       (long)_constraints.FusedLocalRest.Length * sizeof(float);
            }
        }
        public int FusedLanesPerStrand => _fusedLocalActive ? _fusedLanesPerStrand : 0;
        public int FusedStrandsPerGroup => _fusedLocalActive ? _fusedStrandsPerGroup : 0;
        public string FusedLocalSolverStatus => _fusedLocalActive
            ? $"{_fusedLanesPerStrand} lanes × {_fusedStrandsPerGroup} strands/group"
            : _fusedFallbackReason ?? "off";

        public HairSimulationControlMode SimulationControlMode
        {
            get => _simulationControlMode;
            set
            {
                if (_simulationControlMode == value)
                {
                    return;
                }

                _simulationControlMode = value;
                RebuildConstraints();
            }
        }

        public HairSimulationGuideAsset GuideMapAsset
        {
            get => _guideMapAsset;
            set
            {
                if (ReferenceEquals(_guideMapAsset, value))
                {
                    return;
                }

                _guideMapAsset = value;
                RebuildConstraints();
            }
        }

        public bool GuideControlActive => _guideControlActive;

        public string GuideFallbackReason => _guideControlActive ? null : _guideFallbackReason;

        public long GuideMappingBytes
        {
            get
            {
                if (!_ready || _model == null)
                {
                    return 0L;
                }

                long strands = Math.Max(1L, _model.Strands?.Length ?? 0);
                long guides = Math.Max(1L, _guideControlActive ? _guideCount : 0);
                long vertices = _guideControlActive ? _model.VertexCount : 1L;
                long bytes = strands * sizeof(int) * 2L;
                bytes += guides * sizeof(int) * 2L;
                bytes += strands * sizeof(int) * 2L;
                bytes += strands * sizeof(float) * 2L;
                bytes += strands * sizeof(uint);
                bytes += vertices * sizeof(float);
                return bytes;
            }
        }
        public int SimulationGuideCount => _guideControlActive ? _guideCount : 0;
        public float SimulationGuideFraction => _guideControlActive && _model != null &&
                                                 _model.StrandCount > 0
            ? _guideCount / (float)_model.StrandCount
            : 0f;

        public bool GuideLengthSweep
        {
            get => _guideLengthSweep;
            set
            {
                if (_guideLengthSweep == value)
                {
                    return;
                }

                _guideLengthSweep = value;
                if (_ready && _runtimeComputeShader != null)
                {
                    _runtimeComputeShader.SetInt(Ids.GuideLengthSweep, value ? 1 : 0);
                }
            }
        }

        public long GpuBytes
        {
            get
            {
                if (!_ready || _model == null)
                {
                    return 0;
                }
                long n = _model.VertexCount;
                long stateCount = Math.Max(1L, _simulationStateCount);
                long localIncidentEntries = _constraints != null
                    ? _constraints.XpbdSlotLocalIncidentEdges.Length
                    : 0;
                long adjacencyStartCount = stateCount + 1L;
                long edgeCount = _xpbdEnabled && _constraints != null
                    ? _constraints.XpbdEdgeCount
                    : 0L;
                long strandCount = Math.Max(1L, _model.Strands?.Length ?? 0);
                long boundsGroups = Math.Max(1L, _groups);
                long boundsPartialCount = Math.Max(2L, boundsGroups * 2L);
                long bytes = 0L;

                bytes += stateCount * (5L * 3L * sizeof(float));
                bytes += Math.Max(1L, _controlCount) * 4L * sizeof(float);
                bytes += Math.Max(1L, _controlCount) * sizeof(uint);
                bytes += n * sizeof(uint) * 2L;
                bytes += n * sizeof(uint) * 2L;
                bytes += n * sizeof(float);
                bytes += n * sizeof(uint) * 2L;
                bytes += n * sizeof(float) * 2L;
                bytes += MaxHairGroups * Marshal.SizeOf<HairSimulationGpuParameters>();
                bytes += strandCount * 2L * sizeof(int);

                long edgeSlots = Math.Max(1L, edgeCount);
                long edgeVertexSlots = Math.Max(1L, edgeCount * 2L);
                long xpbdStartSlots = _xpbdEnabled ? adjacencyStartCount : 1L;
                long localIncidentSlots = _xpbdEnabled
                    ? Math.Max(1L, localIncidentEntries)
                    : 1L;
                bytes += edgeVertexSlots * sizeof(uint);
                bytes += edgeSlots * (sizeof(float) + sizeof(uint));
                bytes += xpbdStartSlots * sizeof(uint);
                bytes += localIncidentSlots * sizeof(uint);
                bytes += edgeSlots * (sizeof(float) + sizeof(float) * 3L * 2L);

                bytes += boundsPartialCount * sizeof(float) * 4L;
                bytes += 2L * sizeof(float) * 4L;
                bytes += sizeof(uint);
                bytes += GuideMappingBytes;
                bytes += FusedMappingBytes;
                bytes += _pinSet.GpuBytes;
                bytes += _sceneCollisionField?.GpuBytes ?? 0L;
                bytes += _volume?.GpuBytes ?? 0L;
                return bytes;
            }
        }

        public bool SceneCollisionSupported => HairSceneCollisionField.IsSupported(_sceneFieldBuild);
        public bool SceneCollisionReady => _sceneCollisionField?.IsValid ?? false;
        public int SceneCollisionInstanceCount => _sceneCollisionField?.InstanceCount ?? 0;
        public int ActiveSceneCollisionResolution => _sceneCollisionField?.Resolution ?? 0;
        public float ConstraintResidual => _constraintResidual;
        public float MaxConstraintResidual => _constraintResidual;
        public bool ConstraintResidualWarning => _constraintResidual > _residualWarningThreshold;
        public int LastScheduledIterationCount => _lastScheduledIterationCount;
        public int LastLegacyIterationCount => _lastLegacyIterationCount;

        public LayerMask SceneCollisionMask { get => _sceneCollisionMask; set => _sceneCollisionMask = value; }
        public int SceneCollisionResolution
        {
            get => _sceneCollisionResolution;
            set => _sceneCollisionResolution = Mathf.Clamp(value, 16, 128);
        }
        public float SceneCollisionBoundsPadding
        {
            get => _sceneCollisionBoundsPadding;
            set => _sceneCollisionBoundsPadding = Mathf.Max(0f, value);
        }
        public HairSceneFieldBuild SceneFieldBuild { get => _sceneFieldBuild; set => _sceneFieldBuild = value; }
        public HairSceneFieldFlood SceneFieldFlood { get => _sceneFieldFlood; set => _sceneFieldFlood = value; }
        public int SceneFieldFloodIterations
        {
            get => _sceneFieldFloodIterations;
            set => _sceneFieldFloodIterations = Mathf.Clamp(value, 0, 64);
        }
        public bool SceneFieldDiagonalFlood { get => _sceneFieldDiagonalFlood; set => _sceneFieldDiagonalFlood = value; }

        public bool SimulateInEditMode { get => _simulateInEditMode; set => _simulateInEditMode = value; }

        public float PrerollTime { get => _prerollTime; set => _prerollTime = Mathf.Clamp(value, 0f, MaxPrerollTime); }
        public HairSimulationCacheMode CacheMode { get => _cacheMode; set => _cacheMode = value; }
        public string CachePath { get => _cachePath; set => _cachePath = value; }
        public float CacheTime
        {
            get => (float)_cacheTime;
            set
            {
                if (_cacheWriter != null)
                {
                    return;
                }

                _cacheTime = float.IsFinite(value) ? Mathf.Max(0f, value) : 0f;
                _cacheHold = true;
            }
        }
        public int CacheFrameCount => _cacheWriter?.FrameCount ?? _cacheReader?.FrameCount ?? 0;
        public float CacheDuration => _cacheReader?.Duration ?? (_cacheWriter != null ? (float)_cacheTime : 0f);
        public long CacheBytes => _cacheWriter?.Bytes ?? 0L;
        public string CacheError => _cacheError;
        public bool PlayingCache => _cacheReader != null;
        public HairPin[] Pins { get => _pins; set => _pins = value ?? Array.Empty<HairPin>(); }
        public int PinnedVertexCount => _pinSet.Count;

#if UNITY_EDITOR
        private bool? _editorSimulation;

        public static void SetEditorSimulation(LinePrimitiveRenderer renderer, bool? simulate)
        {
            LineHairSimulation simulation = renderer != null ? renderer.GetComponent<LineHairSimulation>() : null;
            if (simulation != null)
            {
                simulation._editorSimulation = simulate;
            }
        }
#endif
        public ComputeShader ComputeShader { get => _computeShader; set => _computeShader = value; }
        public bool SimulationLodEnabled
        {
            get => _simulationLodEnabled;
            set
            {
                _simulationLodEnabled = value;
                InvalidateSimulationLodCache();
            }
        }
        public float SimulationLodMinimumBudgetFraction
        {
            get => _simulationLodMinimumBudgetFraction;
            set
            {
                _simulationLodMinimumBudgetFraction = Mathf.Clamp(value, 0.05f, 1f);
                InvalidateSimulationLodCache();
            }
        }

        public int SimulationLodMaxFrameInterval
        {
            get => _simulationLodMaxFrameInterval;
            set
            {
                _simulationLodMaxFrameInterval = Mathf.Clamp(value, 1, 8);
                InvalidateSimulationLodCache();
            }
        }
        public int SimulationBudgetBucketCount => _budgetBuckets.Count;

        [Tooltip("Ray Tracing: six axis rays per voxel through an acceleration structure; needs inline " +
                 "ray tracing. Triangle Splat: distances written into the voxels around each triangle and " +
                 "flooded through the volume; needs only compute shaders.")]
        [SerializeField]
        private HairSceneFieldBuild _sceneFieldBuild = HairSceneFieldBuild.RayTracing;

        [Tooltip("How the triangle splat fills the voxels its triangles did not cover. Jump: the whole " +
                 "volume, each voxel from its closest seed. Linear: one cell per pass from the surface.")]
        [SerializeField]
        private HairSceneFieldFlood _sceneFieldFlood = HairSceneFieldFlood.Jump;

        [Tooltip("Linear flood passes. Each pass carries the distance one more cell away from the surface.")]
        [SerializeField, Range(0, 64)]
        private int _sceneFieldFloodIterations = 8;

        [Tooltip("Flood across edge and corner neighbours as well as face neighbours. Slower, and the " +
                 "distance follows diagonal surfaces more closely.")]
        [SerializeField]
        private bool _sceneFieldDiagonalFlood;

        [Tooltip("Renderer layers included in the scene collision field.")]
        [SerializeField]
        private LayerMask _sceneCollisionMask = ~0;

        [Tooltip("Voxel resolution per axis of the scene collision field.")]
        [SerializeField, Range(16, 128)]
        private int _sceneCollisionResolution = 48;

        [Tooltip("Extra size around the simulated hair bounds in metres.")]
        [SerializeField, Min(0f)]
        private float _sceneCollisionBoundsPadding = 0.05f;

        [Tooltip("Cells per axis of the hair volume that couples strands to each other. The grid is " +
                 "a cube around the rest groom padded by half the longest strand.")]
        [SerializeField, Range(8, 64)]
        private int _volumeResolution = 32;

        [Tooltip("Run the solver outside Play mode.")]
        [SerializeField, FormerlySerializedAs("simulateInEditMode")]
        private bool _simulateInEditMode = false;

        [Tooltip("Seconds simulated with the character held still before the first frame after every " +
                 "start or reset, so strands appear already settled under gravity instead of dropping " +
                 "from their groomed pose. Costs this many fixed steps in that one frame.")]
        [SerializeField, Range(0f, MaxPrerollTime)]
        private float _prerollTime = DefaultPrerollTime;

        [Tooltip("Off: simulate. Record: write every shown frame that solved a step to the cache file. " +
                 "Play: replay the file instead of simulating; strands the solver does not own are " +
                 "reconstructed as usual around the recorded pin targets.")]
        [SerializeField]
        private HairSimulationCacheMode _cacheMode = HairSimulationCacheMode.Off;

        [Tooltip("Cache file. A relative path is placed under StreamingAssets.")]
        [SerializeField]
        private string _cachePath = "HairLines/Hair Simulation.hlsc";

        [Tooltip("Hair clips, ties and skin attachments. Each pin holds the strand vertices that rest " +
                 "inside its sphere; the first pin in the list wins where spheres overlap.")]
        [SerializeField]
        private HairPin[] _pins = Array.Empty<HairPin>();

        [Tooltip("Which strands own solver state. Guide control needs a baked guide map " +
                 "whose fingerprints still match this groom; it falls back to full control.")]
        [SerializeField]
        private HairSimulationControlMode _simulationControlMode =
            HairSimulationControlMode.FullControl;

        [Tooltip("Simulation guide sidecar. This is a physics budget, not authored guide " +
                 "geometry: HairGuideSet and the groom blob are never modified by it.")]
        [SerializeField]
        private HairSimulationGuideAsset _guideMapAsset;

        [Tooltip("Restore the rest arc length of reconstructed strands with one forward sweep. " +
                 "Disable only to measure the raw two-guide blend.")]
        [SerializeField]
        private bool _guideLengthSweep = true;

        [Tooltip("Scale the solver iteration budget using the renderer camera LOD. The physical fixed-step frequency stays constant for stable motion. Disabled by default.")]
        [SerializeField]
        private bool _simulationLodEnabled = false;

        [Tooltip("Minimum fraction of the configured solver iterations at the far LOD. Substeps stay unchanged so the physical timestep cannot jump.")]
        [SerializeField, Range(0.05f, 1f)]
        private float _simulationLodMinimumBudgetFraction = 0.5f;

        [Tooltip("Legacy compatibility value. Simulation LOD no longer stretches a physics step across render frames, because doing so causes visible impulses at LOD boundaries.")]
        [SerializeField, Range(1, 8)]
        private int _simulationLodMaxFrameInterval = 2;

        [Tooltip("Read back the normalized maximum constraint residual periodically for diagnostics.")]
        [SerializeField]
        private bool _monitorConstraintResidual = true;

        [Tooltip("Frames between GPU residual readbacks.")]
        [SerializeField, Min(1)]
        private int _residualReadbackInterval = 8;

        [Tooltip("Residual above this normalized value is reported through ConstraintResidualWarning.")]
        [SerializeField, Min(0f)]
        private float _residualWarningThreshold = 0.05f;

        [Tooltip("World-space distance that identifies a teleport and resets the solver.")]
        [SerializeField, Min(0f)]
        private float _teleportDistance = 0.5f;

        [Tooltip("Rotation change in degrees that identifies a teleport and resets the solver.")]
        [SerializeField, Range(0f, 180f)]
        private float _teleportAngle = 45f;

        [Tooltip("Relative scale change that identifies a teleport and resets the solver.")]
        [SerializeField, Range(0f, 1f)]
        private float _teleportScaleRatio = 0.25f;

        [SerializeField, HideInInspector, FormerlySerializedAs("boundsPadding")]
        private float _boundsPadding = RetiredCullingBoundsPadding;

        [Tooltip("LineHairSimulation.compute. Found automatically when empty.")]
        [SerializeField, FormerlySerializedAs("computeShader")]
        private ComputeShader _computeShader;

        [Tooltip("HairSceneCollision.compute. Found automatically when empty.")]
        [SerializeField]
        private ComputeShader _sceneCollisionShader;

        [Tooltip("HairSceneSplat.compute. Found automatically when empty.")]
        [SerializeField]
        private ComputeShader _sceneSplatShader;

        private ComputeShader _runtimeComputeShader;
        private ComputeShader _runtimeComputeShaderSource;
        private ComputeShader _runtimeSceneCollisionShader;
        private ComputeShader _runtimeSceneCollisionShaderSource;

        private LinePrimitiveRenderer _renderer;
        private LinePrimitiveModel _model;
        private HairConstraints _constraints;
        private HairConstraints _constraintGraphCache;
        private LinePrimitiveModel _constraintGraphCacheModel;
        private int _constraintGraphCacheHash;
        private bool _constraintGraphCacheXpbd;
        private Matrix4x4 _constraintGraphCacheTransform;
        private HairSceneCollisionField _sceneCollisionField;
        private readonly HairPins _pinSet = new HairPins();
        private LinePrimitiveModel _pinCaptureModel;
        private int _initializations;
        private HairSimulationCache.Writer _cacheWriter;
        private HairSimulationCache.Reader _cacheReader;
        private HairSimulationCacheMode _openedCacheMode;
        private string _openedCachePath;
        private string _cacheError;
        private double _cacheTime;
        private bool _cacheHold;
        private bool _cacheResetSinceShown;
        private Matrix4x4 _solvedWorldToLocal = Matrix4x4.identity;
        private Vector3[] _cachePositions;
        private Vector4[] _cachePinTargets;
        private HairSimulationVolume _volume;
        private HairSimulationVolume.Kernels _volumeKernels;
        private int _builtVolumeHash;
        private Texture3D _sceneCollisionFallback;

        private GraphicsBuffer _velocity, _inertia, _stepStart;
        private GraphicsBuffer _qA, _qB;
        private GraphicsBuffer _controlIndices,
            _interpolationSlotAnchors, _interpolationT;
        private GraphicsBuffer _strandNeighbours;
        private GraphicsBuffer _vertexGroups, _strandLocalIndex, _simulationParameters;
        private GraphicsBuffer _strandRanges;
        private GraphicsBuffer _xpbdEdgeVertices, _xpbdEdgeRest, _xpbdEdgeType;
        private GraphicsBuffer _xpbdIncidentStart, _xpbdIncidentEdges;
        private GraphicsBuffer _xpbdLambda, _xpbdDeltaA, _xpbdDeltaB;
        private GraphicsBuffer _simulationInfluenceBuffer;
        private GraphicsBuffer _rootDistanceBuffer;
        private GraphicsBuffer _boundsPartials, _boundsResult;
        private GraphicsBuffer _residual;
        private GraphicsBuffer _strandVertexRanges, _guideVertexRanges;
        private GraphicsBuffer _strandGuideIds, _strandGuideWeights, _strandGuideSelf;
        private GraphicsBuffer _vertexArcT;
        private GraphicsBuffer _fusedStrandBase, _fusedLocalRest;
        private GraphicsBuffer _cosseratFrames;
        private int _cosseratGroupMask;
        private CommandBuffer _simulationCommandBuffer;

        private HairSimulationGuideMap _guideMap;
        private HairSimulationGuideAsset _guideMapCacheAsset;
        private LinePrimitiveModel _guideMapCacheModel;
        private bool _guideMapCacheValid;
        private string _guideMapCacheError;
        private ulong _guideMapCacheHash;
        private bool _guideControlActive;
        private int _guideCount;
        private string _guideFallbackReason;

        private bool _fusedLocalSolver = true;
        private bool _fusedLocalActive;
        private int _fusedLanesPerStrand;
        private int _fusedStrandsPerGroup;
        private int _fusedGroupCount;
        private string _fusedFallbackReason;

        private int _kReset, _kPredict, _kLimitStrain, _kApplyInfluence,
            _kProjectCollisions, _kFinalize, _kFinalizeGuides, _kXpbdFused, _kCosseratFused, _kPinTargets,
            _kClearVelocity;
        private int[] _pinKernels = Array.Empty<int>();
        private int _kReduceBounds, _kFinalizeBounds, _kResetResidual, _kMeasureResidual;
        private int _kXpbdReset, _kXpbdProject, _kXpbdGather;
        private int _groups;
        private int _simulationStateGroups;
        private int _simulationStateCount;
        private int _controlGroups;
        private int _controlCount;
        private int _strandGroups;
        private int _xpbdEdgeGroups;
        private int _xpbdLocalEdgeGroups;
        private int _qCur, _qNext;
        private GraphicsBuffer[] _q;

        private readonly Vector4[] _shapeA = new Vector4[MaxTotalShapes];
        private readonly Vector4[] _shapeB = new Vector4[MaxTotalShapes];
        private readonly Vector4[] _shapeRot = new Vector4[MaxTotalShapes];
        private readonly HairSimulationSettings[] _resolvedGroupSettings = new HairSimulationSettings[MaxHairGroups];
        private readonly bool[] _resolvedGroupEnabled = new bool[MaxHairGroups];
        private readonly int[] _runtimeSubsteps = new int[MaxHairGroups];
        private readonly int[] _runtimeIterations = new int[MaxHairGroups];
        private readonly HairSimulationGpuParameters[] _gpuParameters = new HairSimulationGpuParameters[MaxHairGroups];
        private readonly List<HairGroupSimulation> _groupSimulationComponents = new List<HairGroupSimulation>();
        private readonly List<HairSimulationBudgetBucket> _budgetBuckets =
            new List<HairSimulationBudgetBucket>(MaxHairGroups);
        private int _budgetBucketHash = int.MinValue;
        private int _lastScheduledIterationCount;
        private int _lastLegacyIterationCount;
        private bool _groupCacheDirty = true;
        private int _resolvedSettingsFrame = -1;
        private int _resolvedGroupCount;
        private int _builtTopologyHash;
        private int _builtInfluenceHash;
        private int _builtComplianceHash;
        private bool _xpbdEnabled;
        private bool _hasNonUnitSimulationInfluence;
        private int _simulationLodFrame = -1;
        private float _simulationLodFraction = 1f;
        private float _simulationBudgetFraction = 1f;
        private float _simulationLodSmoothedFraction = 1f;
        private double _simulationLodSampleTime = -1d;
        private bool _hasSimulationLodSample;

        private double _editorTime;
        private float _simulationAccumulator;
        private bool _ready;
        private AsyncGPUReadbackRequest _boundsReadback;
        private bool _boundsReadbackPending;
        private Bounds _simulationBounds;
        private bool _sceneCollisionWarningIssued;
        private bool _wasDormant;
        private bool _offscreen;
        private int _shapeCount;
        private int _nextBoundsUpdateFrame;
        private readonly HashSet<int> _invalidColliderWarnings = new HashSet<int>();
        private AsyncGPUReadbackRequest _residualReadback;
        private bool _residualReadbackPending;
        private float _constraintResidual;
        private int _nextResidualReadbackFrame;
        private Vector3 _lastSimulationPosition;
        private Quaternion _lastSimulationRotation;
        private Vector3 _lastSimulationScale;
        private bool _hasSimulationTransform;
        private int _staticUniformFrame = -1;
        private int _uploadedParameterHash;
        private int _uploadedShapeHash;
        private int _shapeInputHash;
        private bool _hasUploadedParameters;
        private bool _hasUploadedShapes;
        private bool _hasBuiltShapes;
        private readonly uint[] _uploadedShapeCounts = new uint[MaxHairGroups];

        private static WindZone[] _cachedWindZones = Array.Empty<WindZone>();
        private static int _windZoneCacheFrame = -1;
        private Vector3 _sampledUnityWind;
        private float _sampledUnityWindTurbulence = -1f;

        private static readonly ProfilerMarker _lateUpdateMarker =
            new ProfilerMarker("HairLines.Simulation.LateUpdate");
        private static readonly ProfilerMarker _stepMarker =
            new ProfilerMarker("HairLines.Simulation.Step");
        private static readonly ProfilerMarker _advanceMarker =
            new ProfilerMarker("HairLines.Simulation.Advance");
        private static readonly ProfilerMarker _initializeMarker =
            new ProfilerMarker("HairLines.Simulation.Initialize");
        private static readonly ProfilerMarker _constraintBuildMarker =
            new ProfilerMarker("HairLines.Simulation.BuildConstraints");
        private static readonly ProfilerMarker _sceneCollisionMarker =
            new ProfilerMarker("HairLines.Simulation.PrepareSceneCollision");
        private static readonly ProfilerMarker _staticUniformsMarker =
            new ProfilerMarker("HairLines.Simulation.UpdateStaticUniforms");
        private static readonly ProfilerMarker _simulateMarker =
            new ProfilerMarker("HairLines.Simulation.Simulate");
        private static readonly ProfilerMarker _predictMarker =
            new ProfilerMarker("HairLines.Simulation.Predict");
        private static readonly ProfilerMarker _iterateMarker =
            new ProfilerMarker("HairLines.Simulation.Iterate");
        private static readonly ProfilerMarker _strainMarker =
            new ProfilerMarker("HairLines.Simulation.LimitStrain");
        private static readonly ProfilerMarker _collisionProjectionMarker =
            new ProfilerMarker("HairLines.Simulation.ProjectCollisions");
        private static readonly ProfilerMarker _finalizeMarker =
            new ProfilerMarker("HairLines.Simulation.Finalize");
        private static readonly ProfilerMarker _boundsMarker =
            new ProfilerMarker("HairLines.Simulation.UpdateBounds");
        private static readonly ProfilerMarker _boundsReadbackMarker =
            new ProfilerMarker("HairLines.Simulation.ApplyBoundsReadback");
        private static readonly ProfilerMarker _reduceBoundsMarker =
            new ProfilerMarker("HairLines.Simulation.ReduceBounds");
        private static readonly ProfilerMarker _finalizeBoundsMarker =
            new ProfilerMarker("HairLines.Simulation.FinalizeBounds");
        private static readonly ProfilerMarker _resetStateMarker =
            new ProfilerMarker("HairLines.Simulation.ResetState");
        private static readonly ProfilerMarker _settleMarker =
            new ProfilerMarker("HairLines.Simulation.Settle");
        private static readonly ProfilerMarker _resetKernelMarker =
            new ProfilerMarker("HairLines.Simulation.ResetKernel");
        private static readonly ProfilerMarker _ensureInitializedMarker =
            new ProfilerMarker("HairLines.Simulation.EnsureInitialized");
        private static readonly ProfilerMarker _bindBuffersMarker =
            new ProfilerMarker("HairLines.Simulation.BindBuffers");
        private static readonly ProfilerMarker _buildParametersMarker =
            new ProfilerMarker("HairLines.Simulation.BuildGpuParameters");
        private static readonly ProfilerMarker _buildShapesMarker =
            new ProfilerMarker("HairLines.Simulation.BuildCollisionShapes");

        private static readonly ProfilingSampler _gpuPredictSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.Predict");
        private static readonly ProfilingSampler _gpuXpbdResetSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.XpbdResetLambdas");
        private static readonly ProfilingSampler _gpuXpbdProjectLocalSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.XpbdProjectLocal");
        private static readonly ProfilingSampler _gpuXpbdFusedSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.XpbdFusedLocal");
        private static readonly ProfilingSampler _gpuCosseratFusedSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.CosseratFusedLocal");
        private static readonly ProfilingSampler _gpuVolumeSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.Volume");
        private static readonly ProfilingSampler _gpuXpbdGatherSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.XpbdGather");
        private static readonly ProfilingSampler _gpuInfluenceSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.ApplyInfluence");
        private static readonly ProfilingSampler _gpuStrainSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.LimitStrain");
        private static readonly ProfilingSampler _gpuCollisionSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.ProjectCollisions");
        private static readonly ProfilingSampler _gpuFinalizeSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.Finalize");
        private static readonly ProfilingSampler _gpuFinalizeGuidesSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.FinalizeGuides");
        private static readonly ProfilingSampler _gpuResidualResetSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.ResetResidual");
        private static readonly ProfilingSampler _gpuResidualMeasureSampler =
            new ProfilingSampler("HairLines.Simulation.GPU.MeasureResidual");

        public const int MaxShapesPerGroup = 8;
        private const int MaxHairGroups = 16;
        private const int MaxTotalShapes = MaxShapesPerGroup * MaxHairGroups;
        private const int MaxSimulationStepsPerFrame = 4;
        private const float SimulationLodSmoothingRate = 8f;
        private const float SimulationLodHysteresis = 0.05f;
        private const float MinSimulationLodSampleDelta = 1f / 120f;
        private const float MaxSimulationLodSampleDelta = 0.1f;

        private const float RetiredCullingBoundsPadding = -1f;

        private const uint InvalidGuide = 0xffffffffu;

        private const int FusedGroupLanes = 64;
        private const float MaxPrerollTime = 5f;
        private const float DefaultPrerollTime = 1f;
        private const float MinSimulationStep = 1f / 240f;

        private float EffectiveMaxDeltaTime
        {
            get
            {
                float longest = 0f;

                for (int i = 0; i < _resolvedGroupCount; i++)
                {
                    if (!_resolvedGroupEnabled[i])
                    {
                        continue;
                    }

                    longest = Mathf.Max(longest, Mathf.Max(_resolvedGroupSettings[i].MaxDeltaTime,
                                                           _resolvedGroupSettings[i].SimulationStep));
                }

                return longest > 0f ? longest : 1f / 30f;
            }
        }

        private void OnEnable()
        {
            _renderer = GetComponent<LinePrimitiveRenderer>();
            MigrateCullingBoundsPadding();
            _editorTime = Time.realtimeSinceStartupAsDouble;
            _hasSimulationTransform = false;
            ResetSimulationLodSmoothing();
            _constraintResidual = 0f;
            _residualReadbackPending = false;
            _xpbdEnabled = false;
            _invalidColliderWarnings.Clear();
            _renderer?.RefreshGroups();
            RefreshGroups();
        }

        private void LateUpdate()
        {
            using ProfilerMarker.AutoScope scope = _lateUpdateMarker.Auto();
            ApplyCompletedBoundsReadback();
            ApplyCompletedResidualReadback();

            bool shouldSimulate = Application.isPlaying || _simulateInEditMode;
#if UNITY_EDITOR
            shouldSimulate = _editorSimulation ?? shouldSimulate;
#endif
            if (!shouldSimulate)
            {
                _simulationAccumulator = 0f;
                _wasDormant = false;
                _hasSimulationTransform = false;
                _editorTime = Time.realtimeSinceStartupAsDouble;
                if (_ready)
                {
                    Release();
                }
                return;
            }

            if (_renderer == null)
            {
                _simulationAccumulator = 0f;
                _wasDormant = true;
                _hasSimulationTransform = false;
                return;
            }

            RefreshResolvedGroupSettings();
            if (!HasEnabledSimulationGroup() || !_renderer.RequiresRealtimeUpdates)
            {
                if (_ready)
                {
                    Release();
                }
                _simulationAccumulator = 0f;
                _wasDormant = true;
                _hasSimulationTransform = false;
                return;
            }

            if (_wasDormant)
            {
                _wasDormant = false;
                _editorTime = Time.realtimeSinceStartupAsDouble;
            }

            if (!_renderer.WasRecentlyRendered)
            {
                if (!_offscreen)
                {
                    _offscreen = true;
                    if (_ready)
                    {
                        ResetToRestUnlessRebuilt();
                    }
                }
                _simulationAccumulator = 0f;
                return;
            }

            if (_offscreen)
            {
                _offscreen = false;
                _hasSimulationTransform = false;
                if (_ready)
                {
                    ResetToRestUnlessRebuilt();
                }

                _editorTime = Time.realtimeSinceStartupAsDouble;
            }

            float dt;
            if (Application.isPlaying)
            {
                dt = Time.deltaTime;
            }
            else
            {
                double now = Time.realtimeSinceStartupAsDouble;
                dt = (float)(now - _editorTime);
                _editorTime = now;
            }

            Advance(dt);
        }

        public void RebuildConstraints()
        {
            _constraintGraphCache = null;
            _constraintGraphCacheModel = null;
            _constraintGraphCacheHash = 0;
            _constraintGraphCacheXpbd = false;
            _constraintGraphCacheTransform = default;
            _guideFallbackReason = null;
            _pinSet.ClearCapture();
            Release();
        }

        public void RefreshGroups()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<LinePrimitiveRenderer>();
            }

            _groupCacheDirty = true;
            _resolvedSettingsFrame = -1;
            UpdateGroupSimulationCache();
        }

        private void UpdateGroupSimulationCache()
        {
            if (!_groupCacheDirty)
            {
                return;
            }

            _groupSimulationComponents.Clear();
            if (_renderer != null)
            {
                GetComponentsInChildren(false, _groupSimulationComponents);
                for (int i = _groupSimulationComponents.Count - 1; i >= 0; i--)
                {
                    HairGroupSimulation component = _groupSimulationComponents[i];
                    if (!component.isActiveAndEnabled || component.GetComponent<HairGroup>() == null)
                    {
                        _groupSimulationComponents.RemoveAt(i);
                    }
                }
            }

            _groupCacheDirty = false;
        }

        public void Step(float deltaTime)
        {
            using ProfilerMarker.AutoScope scope = _stepMarker.Auto();
            ApplyCompletedResidualReadback();
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                return;
            }
            if (_renderer == null)
            {
                _renderer = GetComponent<LinePrimitiveRenderer>();
            }

            if (_renderer == null)
            {
                return;
            }

            RefreshResolvedGroupSettings();
            if (!HasEnabledSimulationGroup() || !_renderer.RequiresRealtimeUpdates)
            {
                if (_ready)
                {
                    Release();
                }
                return;
            }

            if (!EnsureInitialized())
            {
                return;
            }

            float maxDeltaTime = ResolveMaxDeltaTime();
            ResetAfterTemporalShock(deltaTime, maxDeltaTime);
            deltaTime = Mathf.Clamp(deltaTime, 0f, maxDeltaTime);
            if (deltaTime <= 0f)
            {
                return;
            }

            ResetAfterTeleport();
            ReopenChangedCache();
            if (_cacheReader != null)
            {
                PlayCache(deltaTime);
                return;
            }

            PrepareSceneCollisionField();
            Simulate(deltaTime);
            RecordCacheFrame(deltaTime, true);
            UpdateSimulationBounds();
        }

        private void Advance(float deltaTime)
        {
            using ProfilerMarker.AutoScope scope = _advanceMarker.Auto();
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                return;
            }
            float maxDeltaTime = ResolveMaxDeltaTime();
            float fixedStep = ResolveSimulationStep();
            ResetAfterTemporalShock(deltaTime, maxDeltaTime);
            deltaTime = Mathf.Clamp(deltaTime, 0f, maxDeltaTime);
            if (deltaTime <= 0f)
            {
                return;
            }

            ResetAfterTeleport();
            if (_ready)
            {
                ReopenChangedCache();
            }

            if (_cacheMode == HairSimulationCacheMode.Play && EnsureInitialized() && _cacheReader != null)
            {
                _simulationAccumulator = 0f;
                PlayCache(deltaTime);
                return;
            }

            _simulationAccumulator = Mathf.Min(_simulationAccumulator + deltaTime, maxDeltaTime);

            int steps = 0;
            if (_simulationAccumulator >= fixedStep && !EnsureInitialized())
            {
                _simulationAccumulator = 0f;
                return;
            }

            if (_simulationAccumulator >= fixedStep)
            {
                PrepareSceneCollisionField();
            }

            while (_simulationAccumulator >= fixedStep && steps < MaxSimulationStepsPerFrame)
            {
                Simulate(fixedStep);
                _simulationAccumulator -= fixedStep;
                steps++;
            }

            if (steps > 0)
            {
                UpdateSimulationBounds();
            }

            if (_ready)
            {
                RecordCacheFrame(deltaTime, steps > 0);
            }

            if (steps == MaxSimulationStepsPerFrame && _simulationAccumulator >= fixedStep)
            {
                _simulationAccumulator %= fixedStep;
            }
        }

        private float ResolveMaxDeltaTime()
        {
            RefreshResolvedGroupSettings();
            float maxDeltaTime = float.PositiveInfinity;
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (!_resolvedGroupEnabled[i])
                {
                    continue;
                }
                maxDeltaTime = Mathf.Min(maxDeltaTime, Mathf.Max(_resolvedGroupSettings[i].MaxDeltaTime,
                                                                  _resolvedGroupSettings[i].SimulationStep));
            }
            return float.IsInfinity(maxDeltaTime) ? EffectiveMaxDeltaTime : maxDeltaTime;
        }

        private float ResolveSimulationStep()
        {
            RefreshResolvedGroupSettings();
            float simulationStep = float.PositiveInfinity;
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (!_resolvedGroupEnabled[i])
                {
                    continue;
                }
                simulationStep = Mathf.Min(simulationStep, _resolvedGroupSettings[i].SimulationStep);
            }
            return float.IsInfinity(simulationStep) ? 1f / 60f : simulationStep;
        }

        private void ResetAfterTeleport()
        {
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            Vector3 scale = transform.lossyScale;
            if (!_hasSimulationTransform)
            {
                _lastSimulationPosition = position;
                _lastSimulationRotation = rotation;
                _lastSimulationScale = scale;
                _hasSimulationTransform = true;
                return;
            }

            float positionDelta = Vector3.Distance(position, _lastSimulationPosition);
            float rotationDelta = Quaternion.Angle(rotation, _lastSimulationRotation);
            bool scaleChanged = HasScaleDiscontinuity(scale, _lastSimulationScale);
            bool teleported = positionDelta > _teleportDistance ||
                              rotationDelta > _teleportAngle ||
                              scaleChanged;

            _lastSimulationPosition = position;
            _lastSimulationRotation = rotation;
            _lastSimulationScale = scale;
            if (!teleported)
            {
                return;
            }

            _simulationAccumulator = 0f;
            if (_ready)
            {
                ResetState();
            }
        }

        private void ResetAfterTemporalShock(float deltaTime, float maxDeltaTime)
        {
            if (deltaTime > maxDeltaTime * 2f)
            {
                _simulationAccumulator = Mathf.Min(_simulationAccumulator,
                                                   Mathf.Max(0f, maxDeltaTime));
            }
        }

        private bool HasScaleDiscontinuity(Vector3 current, Vector3 previous)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                float currentMagnitude = Mathf.Abs(current[axis]);
                float previousMagnitude = Mathf.Abs(previous[axis]);
                if (previousMagnitude <= 1e-6f)
                {
                    if (currentMagnitude > 1e-4f)
                    {
                        return true;
                    }
                    continue;
                }

                float ratio = currentMagnitude / previousMagnitude;
                if (Mathf.Abs(ratio - 1f) > _teleportScaleRatio)
                {
                    return true;
                }
            }

            return false;
        }

        public void ResetState()
        {
            int initializations = _initializations;
            if (!EnsureInitialized() || initializations != _initializations)
            {
                return;
            }

            ResetToRest();
            if (_cacheReader == null)
            {
                Settle();
            }
        }

        private void OpenCache()
        {
            if (_cacheReader != null)
            {
                ClearCosseratFrames();
            }

            CloseCache();
            _openedCacheMode = _cacheMode;
            _openedCachePath = _cachePath;
            _cacheError = null;
            if (!_cacheHold || _cacheMode != HairSimulationCacheMode.Play)
            {
                _cacheTime = 0d;
            }

            _cacheHold = true;
            if (_cacheMode == HairSimulationCacheMode.Off || !_ready)
            {
                return;
            }

            string path = _cachePath;
            var layout = new HairSimulationCache.Layout(_simulationStateCount, _pinSet.Count, _model.VertexCount,
                                                        _model.StrandCount,
                                                        HairSimulationCache.HashSlots(_constraints.ControlIndices));
            _cachePositions = new Vector3[_simulationStateCount];
            _cachePinTargets = new Vector4[_pinSet.Count];
            try
            {
                path = HairSimulationCache.ResolvePath(_cachePath);
                if (_cacheMode == HairSimulationCacheMode.Record)
                {
                    long replaced = File.Exists(path) ? new FileInfo(path).Length : 0L;
                    _cacheWriter = new HairSimulationCache.Writer(path, layout);
                    if (replaced > 0L)
                    {
                        Debug.Log($"[HairLines] Recording replaces the cache '{path}' " +
                                  $"({replaced / (1024f * 1024f):0.0} MB).", this);
                    }
                }
                else
                {
                    _cacheReader = new HairSimulationCache.Reader(path, layout);
                    if (_cacheReader.FrameCount == 0)
                    {
                        _cacheError = "it holds no frames";
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is ArgumentException || exception is NotSupportedException)
            {
                _cacheError = exception.Message.TrimEnd('.');
            }

            if (_cacheError != null)
            {
                CloseCache();
                Debug.LogWarning($"[HairLines] Hair simulation cache '{path}' is not used: {_cacheError}. " +
                                 "The groom is simulated instead.", this);
            }
        }

        private void CloseCache()
        {
            _cacheWriter?.Dispose();
            _cacheWriter = null;
            _cacheReader?.Dispose();
            _cacheReader = null;
        }

        private void ClearCosseratFrames()
        {
            _cosseratFrames?.SetData(new Vector4[_cosseratFrames.count]);
        }

        private void PublishSolvedPose()
        {
            _solvedWorldToLocal = transform.worldToLocalMatrix;
            _renderer.SetSolvedPose(_solvedWorldToLocal);
        }

        private void ReopenChangedCache()
        {
            if (_openedCacheMode != _cacheMode || _openedCachePath != _cachePath)
            {
                OpenCache();
            }
        }

        private void AdvanceCacheClock(float deltaTime)
        {
            if (_cacheHold)
            {
                _cacheHold = false;
                return;
            }

            _cacheTime += deltaTime;
        }

        private void RecordCacheFrame(float deltaTime, bool solved)
        {
            if (_cacheWriter == null)
            {
                return;
            }

            bool first = _cacheWriter.FrameCount == 0;
            AdvanceCacheClock(deltaTime);
            if (!solved && !first)
            {
                return;
            }

            _q[_qCur].GetData(_cachePositions);
            for (int i = 0; i < _cachePositions.Length; i++)
            {
                _cachePositions[i] = _solvedWorldToLocal.MultiplyPoint3x4(_cachePositions[i]);
            }

            _pinSet.ReadTargets(_cachePinTargets);
            TransformPinTargets(_solvedWorldToLocal);
            try
            {
                _cacheWriter.Append((float)_cacheTime, _cachePositions, _cachePinTargets);
            }
            catch (IOException exception)
            {
                _cacheError = exception.Message.TrimEnd('.');
                CloseCache();
                Debug.LogWarning($"[HairLines] Recording the hair simulation cache stopped: {exception.Message}", this);
            }
        }

        private void PlayCache(float deltaTime)
        {
            CapturePreviousPositions(force: false);
            bool jumped = _cacheHold || _cacheResetSinceShown;
            _cacheResetSinceShown = false;
            AdvanceCacheClock(deltaTime);
            _cacheTime = Math.Min(_cacheTime, _cacheReader.Duration);
            try
            {
                _cacheReader.ReadShownAt((float)_cacheTime, _cachePositions, _cachePinTargets);
            }
            catch (IOException exception)
            {
                _cacheError = exception.Message.TrimEnd('.');
                ClearCosseratFrames();
                CloseCache();
                Debug.LogWarning($"[HairLines] Playing the hair simulation cache stopped: {exception.Message}", this);
                return;
            }

            Matrix4x4 objectToWorld = transform.localToWorldMatrix;
            for (int i = 0; i < _cachePositions.Length; i++)
            {
                _cachePositions[i] = objectToWorld.MultiplyPoint3x4(_cachePositions[i]);
            }

            TransformPinTargets(objectToWorld);
            ComputeShader cs = _runtimeComputeShader;
            _q[_qCur].SetData(_cachePositions);
            _stepStart.SetData(_cachePositions);
            _pinSet.WriteTargets(_cachePinTargets);
            UpdateStaticUniforms();
            cs.SetFloat(Ids.FrameDelta, Mathf.Max(deltaTime, 1e-6f));
            int finalizeKernel = _guideControlActive ? _kFinalizeGuides : _kFinalize;
            cs.SetBuffer(finalizeKernel, Ids.QCur, _q[_qCur]);
            cs.Dispatch(finalizeKernel, _guideControlActive ? _strandGroups : _groups, 1, 1);
            PublishSolvedPose();
            if (jumped)
            {
                CapturePreviousPositions(force: true);
            }

            UpdateSimulationBounds();
        }

        private void TransformPinTargets(Matrix4x4 matrix)
        {
            for (int i = 0; i < _cachePinTargets.Length; i++)
            {
                Vector4 target = _cachePinTargets[i];
                Vector3 point = matrix.MultiplyPoint3x4(target);
                _cachePinTargets[i] = new Vector4(point.x, point.y, point.z, target.w);
            }
        }

        private void Settle()
        {
            float step = ResolveSimulationStep();
            int steps = Mathf.RoundToInt(_prerollTime / step);
            if (!_ready || steps <= 0)
            {
                return;
            }

            using ProfilerMarker.AutoScope scope = _settleMarker.Auto();
            PrepareSceneCollisionField();
            for (int i = 0; i < steps; i++)
            {
                Simulate(step);
            }

            _runtimeComputeShader.SetBuffer(_kClearVelocity, Ids.V, _velocity);
            _runtimeComputeShader.Dispatch(_kClearVelocity, _simulationStateGroups, 1, 1);
            CapturePreviousPositions(force: true);
            _simulationAccumulator = 0f;
        }

        private void ResetToRestUnlessRebuilt()
        {
            int initializations = _initializations;
            if (EnsureInitialized() && initializations == _initializations)
            {
                ResetToRest();
            }
        }

        private void ResetToRest()
        {
            using ProfilerMarker.AutoScope scope = _resetStateMarker.Auto();
            InvalidateSimulationLodCache();
            if (!EnsureInitialized())
            {
                return;
            }
            BindBuffers();
            UpdateStaticUniforms();
            using (ProfilerMarker.AutoScope resetKernelScope = _resetKernelMarker.Auto())
            {
                _pinSet.BindPendingAtRest();
                _pinSet.UpdateTargets(_runtimeComputeShader, _kPinTargets, _model, transform.localToWorldMatrix);
                _runtimeComputeShader.SetBuffer(_kReset, Ids.QCur, _q[_qCur]);
                _runtimeComputeShader.SetBuffer(_kReset, Ids.QNext, _q[_qNext]);
                _runtimeComputeShader.Dispatch(_kReset, _simulationStateGroups, 1, 1);
                if (_xpbdEnabled && _constraints != null && _constraints.XpbdEdgeCount > 0)
                {
                    _runtimeComputeShader.SetInt(Ids.XpbdEdgeOffset, 0);
                    _runtimeComputeShader.SetInt(Ids.XpbdEdgeCount, _constraints.XpbdEdgeCount);
                    _runtimeComputeShader.Dispatch(_kXpbdReset, _xpbdEdgeGroups, 1, 1);
                }
                if (_guideControlActive)
                {
                    _runtimeComputeShader.SetBuffer(_kFinalizeGuides, Ids.QCur, _q[_qCur]);
                    _runtimeComputeShader.Dispatch(_kFinalizeGuides, _strandGroups, 1, 1);
                }
                else
                {
                    _runtimeComputeShader.SetBuffer(_kFinalize, Ids.QCur, _q[_qCur]);
                    _runtimeComputeShader.Dispatch(_kFinalize, _groups, 1, 1);
                }
            }
            PublishSolvedPose();
            CapturePreviousPositions(force: true);
            _cacheResetSinceShown = true;
            _simulationAccumulator = 0f;
            _boundsReadbackPending = false;
            _nextBoundsUpdateFrame = 0;
            SetRestBounds();
        }

        private bool EnsureInitialized()
        {
            using ProfilerMarker.AutoScope scope = _ensureInitializedMarker.Auto();
            if (_renderer == null)
            {
                _renderer = GetComponent<LinePrimitiveRenderer>();
            }
            UpdateGroupSimulationCache();
            LinePrimitiveModel model = _renderer != null ? _renderer.Model : null;

            if (model == null || model.IsEmpty)
            {
                if (_ready)
                {
                    Release();
                }

                return false;
            }

            RefreshResolvedGroupSettings();
            if (!HasEnabledSimulationGroup())
            {
                if (_ready)
                {
                    Release();
                }

                return false;
            }

            if (_ready && ReferenceEquals(model, _model) &&
                _runtimeComputeShader != null && _runtimeComputeShaderSource == _computeShader)
            {
                if (ComputeTopologyHash() == _builtTopologyHash)
                {
                    int influenceHash = ComputeInfluenceHash();
                    if (influenceHash != _builtInfluenceHash && _simulationInfluenceBuffer != null)
                    {
                        _simulationInfluenceBuffer.SetData(BuildSimulationInfluence(_model));
                        _builtInfluenceHash = influenceHash;
                    }
                    int volumeHash = ComputeVolumeHash();
                    if (volumeHash != _builtVolumeHash)
                    {
                        AllocateVolume(model);
                    }
                    int complianceHash = ComputeComplianceHash();
                    if (complianceHash != _builtComplianceHash)
                    {
                        bool xpbdEnabled = HasEnabledXpbdGroup();
                        if (xpbdEnabled != _xpbdEnabled)
                        {
                            Release();
                            return Initialize(model);
                        }
                        _builtComplianceHash = complianceHash;
                    }
                    return true;
                }
            }

            Release();
            return Initialize(model);
        }

        private bool Initialize(LinePrimitiveModel model)
        {
            using ProfilerMarker.AutoScope scope = _initializeMarker.Auto();
            if (_computeShader == null)
            {
                _computeShader = FindComputeShader();
                if (_computeShader == null)
                {
                    Debug.LogError("[HairLines] LineHairSimulation.compute is not assigned.", this);
                    enabled = false;
                    return false;
                }
            }

            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogError("[HairLines] Compute shaders are not supported on this device.", this);
                enabled = false;
                return false;
            }

            int n = model.VertexCount;

            if (n >= (1 << 29))
            {
                Debug.LogError($"[HairLines] Unsupported vertex count for simulation: {n}", this);
                enabled = false;
                return false;
            }

            _model = model;
            int xpbdEdges = 0;
            int topologyHash = 0;
            try
            {
            _runtimeComputeShader = CreateRuntimeShader(_computeShader, "Simulation");
            _runtimeComputeShaderSource = _computeShader;

            RefreshResolvedGroupSettings();
            _xpbdEnabled = HasEnabledXpbdGroup();
            topologyHash = ComputeTopologyHash();
            Matrix4x4 graphTransform = transform.localToWorldMatrix;
            int maxPinnedRootVertices = 1;
            bool anyBendingSprings = false;
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (!_resolvedGroupEnabled[i])
                {
                    continue;
                }
                HairSimulationSettings settings = _resolvedGroupSettings[i];
                maxPinnedRootVertices = Mathf.Max(maxPinnedRootVertices, settings.PinnedRootVertices);
                anyBendingSprings |= settings.BendingSprings;
            }

            bool[] simulatedStrands = ResolveGuideStrandMask(model);
            CapturePins(model);

            var timer = Stopwatch.StartNew();
            bool reuseConstraintGraph = ReferenceEquals(model, _constraintGraphCacheModel) &&
                                        topologyHash == _constraintGraphCacheHash &&
                                        _constraintGraphCacheXpbd == _xpbdEnabled &&
                                        SameConstraintGraphTransform(graphTransform,
                                                                      _constraintGraphCacheTransform) &&
                                        _constraintGraphCache != null;
            if (reuseConstraintGraph)
            {
                _constraints = _constraintGraphCache;
            }
            else
            {
                _constraintGraphCache = null;
                _constraintGraphCacheModel = null;
                _constraintGraphCacheHash = 0;
                _constraintGraphCacheXpbd = false;
                _constraintGraphCacheTransform = default;

                using (ProfilerMarker.AutoScope constraintScope = _constraintBuildMarker.Auto())
                {
                    _constraints = HairConstraints.Build(model, maxPinnedRootVertices, anyBendingSprings,
                                                           graphTransform, _xpbdEnabled,
                                                           ResolveSimulationVertexSkip(),
                                                           simulationVertexSkipByGroup:
                                                               ResolveSimulationVertexSkipByGroup(),
                                                           simulatedStrands: simulatedStrands,
                                                           pinnedVertices: _pinSet.PinnedVertices);
                    if (simulatedStrands != null && !_constraints.HasCompactSlotGraph)
                    {
                        Debug.LogWarning("[HairLines] Guide control rejected: the compact slot " +
                                         "graph is unavailable for this groom. Rebuilding with " +
                                         "full control.", this);
                        simulatedStrands = null;
                        _guideFallbackReason = "compact slot graph unavailable";
                        _constraints = HairConstraints.Build(model, maxPinnedRootVertices,
                                                               anyBendingSprings,
                                                               graphTransform, _xpbdEnabled,
                                                               ResolveSimulationVertexSkip(),
                                                               simulationVertexSkipByGroup:
                                                                   ResolveSimulationVertexSkipByGroup(),
                                                               pinnedVertices: _pinSet.PinnedVertices);
                    }
                }
                _constraintGraphCache = _constraints;
                _constraintGraphCacheModel = model;
                _constraintGraphCacheHash = topologyHash;
                _constraintGraphCacheXpbd = _xpbdEnabled;
                _constraintGraphCacheTransform = graphTransform;
            }
            timer.Stop();
            _guideControlActive = false;
            bool guideGraph = _constraints != null && _constraints.SimulatedStrands != null;

            bool slotMapValid = _constraints != null && _constraints.HasCompactSlotGraph &&
                                _constraints.ValidateCompactSlotMap(out string slotMapError);
            if (!slotMapValid)
            {
                Debug.LogError("[HairLines] The control-slot map failed validation; the groom " +
                               "keeps its rest pose. Rebuild the groom and its constraints.", this);
                _guideFallbackReason = "control-slot map failed validation";
                enabled = false;
                return false;
            }

            _guideControlActive = guideGraph;
            _simulationStateCount = Mathf.Max(1, _constraints.CompactSlotCount);
            Debug.Log($"[HairLines] Constraint graph {(reuseConstraintGraph ? "reused" : "built")}: " +
                      $"{_constraints.StretchCount:N0} stretch, " +
                      $"{_constraints.BendCount:N0} bend springs " +
                      $"over {_constraints.ControlCount:N0}/{n:N0} simulated vertices " +
                      $"({timer.ElapsedMilliseconds} ms).", this);

            int stateCount = Mathf.Max(1, _simulationStateCount);
            int graphVertexCount = Mathf.Max(0, _constraints.CompactSlotCount);
            const GraphicsBuffer.Target Structured = GraphicsBuffer.Target.Structured;
            _velocity = new GraphicsBuffer(Structured, stateCount, sizeof(float) * 3);
            _inertia = new GraphicsBuffer(Structured, stateCount, sizeof(float) * 3);
            _stepStart = new GraphicsBuffer(Structured, stateCount, sizeof(float) * 3);
            _qA = new GraphicsBuffer(Structured, stateCount, sizeof(float) * 3);
            _qB = new GraphicsBuffer(Structured, stateCount, sizeof(float) * 3);
            _controlIndices = new GraphicsBuffer(Structured,
                Mathf.Max(1, _constraints.ControlCount), sizeof(uint));
            _interpolationSlotAnchors = new GraphicsBuffer(Structured, n, sizeof(uint) * 2);
            _interpolationT = new GraphicsBuffer(Structured, n, sizeof(float));

            _vertexGroups = new GraphicsBuffer(Structured, n, sizeof(uint));
            _strandLocalIndex = new GraphicsBuffer(Structured, n, sizeof(uint));
            _simulationParameters = new GraphicsBuffer(Structured, MaxHairGroups,
                Marshal.SizeOf<HairSimulationGpuParameters>());
            _strandNeighbours = new GraphicsBuffer(Structured, n, sizeof(int) * 2);
            _strandRanges = new GraphicsBuffer(Structured, Mathf.Max(1, model.Strands.Length), sizeof(int) * 2);
            xpbdEdges = _xpbdEnabled ? _constraints.XpbdEdgeCount : 0;
            int localIncidentEntries = _xpbdEnabled
                ? _constraints.XpbdSlotLocalIncidentEdges.Length
                : 0;
            _xpbdEdgeVertices = new GraphicsBuffer(Structured, Mathf.Max(1, xpbdEdges * 2), sizeof(uint));
            _xpbdEdgeRest = new GraphicsBuffer(Structured, Mathf.Max(1, xpbdEdges), sizeof(float));
            _xpbdEdgeType = new GraphicsBuffer(Structured, Mathf.Max(1, xpbdEdges), sizeof(uint));
            _xpbdIncidentStart = new GraphicsBuffer(Structured,
                _xpbdEnabled ? graphVertexCount + 1 : 1, sizeof(uint));
            _xpbdIncidentEdges = new GraphicsBuffer(Structured,
                _xpbdEnabled ? Mathf.Max(1, localIncidentEntries) : 1,
                sizeof(uint));
            _xpbdLambda = new GraphicsBuffer(Structured, Mathf.Max(1, xpbdEdges), sizeof(float));
            _xpbdDeltaA = new GraphicsBuffer(Structured, Mathf.Max(1, xpbdEdges), sizeof(float) * 3);
            _xpbdDeltaB = new GraphicsBuffer(Structured, Mathf.Max(1, xpbdEdges), sizeof(float) * 3);
            _simulationInfluenceBuffer = new GraphicsBuffer(Structured, n, sizeof(float));
            _rootDistanceBuffer = new GraphicsBuffer(Structured, n, sizeof(float));
            _boundsPartials = new GraphicsBuffer(Structured,
                Mathf.Max(2, Mathf.CeilToInt(n / 256f) * 2), sizeof(float) * 4);
            _boundsResult = new GraphicsBuffer(Structured, 2, sizeof(float) * 4);
            _residual = new GraphicsBuffer(Structured, 1, sizeof(uint));
            _residual.SetData(new[] { 0u });

            int strandCount = _guideControlActive ? Mathf.Max(1, model.Strands.Length) : 1;
            _strandVertexRanges = new GraphicsBuffer(Structured, strandCount, sizeof(int) * 2);
            _guideVertexRanges = new GraphicsBuffer(Structured,
                Mathf.Max(1, _guideControlActive ? _guideCount : 0), sizeof(int) * 2);
            _strandGuideIds = new GraphicsBuffer(Structured, strandCount, sizeof(int) * 2);
            _strandGuideWeights = new GraphicsBuffer(Structured, strandCount, sizeof(float) * 2);
            _strandGuideSelf = new GraphicsBuffer(Structured, strandCount, sizeof(uint));
            _vertexArcT = new GraphicsBuffer(Structured,
                _guideControlActive ? n : 1, sizeof(float));
            UploadGuideMap(model);
            _pinSet.Allocate(n);

            bool fusedGraph = _fusedLocalSolver && _xpbdEnabled &&
                              _constraints.EnsureFusedLocalGraph() &&
                              _constraints.FusedLocalRest.Length > 0;
            int fusedStrandCount = fusedGraph ? Mathf.Max(1, model.Strands.Length) : 1;
            _fusedStrandBase = new GraphicsBuffer(Structured, fusedStrandCount, sizeof(uint));
            _fusedLocalRest = new GraphicsBuffer(Structured,
                Mathf.Max(1, fusedGraph ? _constraints.FusedLocalRest.Length : 0), sizeof(float));
            if (fusedGraph)
            {
                _fusedStrandBase.SetData(_constraints.FusedStrandBase);
                _fusedLocalRest.SetData(_constraints.FusedLocalRest);
            }
            else
            {
                _fusedStrandBase.SetData(new[] { HairConstraints.InvalidFusedStrand });
                _fusedLocalRest.SetData(new[] { -1f });
            }

            _cosseratFrames = new GraphicsBuffer(Structured, Mathf.Max(1, _constraints.ControlCount),
                                                 sizeof(float) * 4);
            _cosseratFrames.SetData(new Vector4[_cosseratFrames.count]);
            _cosseratGroupMask = ComputeCosseratGroupMask();

            _vertexGroups.SetData(BuildVertexGroups(model));
            _strandLocalIndex.SetData(_constraints.StrandLocalIndex);
            if (_constraints.ControlCount > 0)
            {
                _controlIndices.SetData(_constraints.ControlIndices);
            }
            else
            {
                _controlIndices.SetData(new[] { 0u });
            }
            _interpolationSlotAnchors.SetData(_constraints.InterpolationSlotAnchors);
            _interpolationT.SetData(_constraints.InterpolationT);
            _simulationParameters.SetData(_gpuParameters);
            _strandNeighbours.SetData(_constraints.StrandNeighbours);
            _strandRanges.SetData(_constraints.ControlStrands);
            if (xpbdEdges > 0)
            {
                _xpbdEdgeVertices.SetData(_constraints.XpbdSlotEdgeVertices);
                _xpbdEdgeRest.SetData(_constraints.XpbdSlotEdgeRest);
                _xpbdEdgeType.SetData(_constraints.XpbdSlotEdgeType);
            }
            else
            {
                _xpbdEdgeVertices.SetData(new[] { 0u });
                _xpbdEdgeRest.SetData(new[] { 0f });
                _xpbdEdgeType.SetData(new[] { 0u });
            }
            if (_xpbdEnabled)
            {
                int[] localIncidentStartData = _constraints.XpbdSlotLocalIncidentStart;
                uint[] localIncidentData = _constraints.XpbdSlotLocalIncidentEdges;
                _xpbdIncidentStart.SetData(localIncidentStartData);
                if (localIncidentData.Length > 0)
                {
                    _xpbdIncidentEdges.SetData(localIncidentData);
                }
                else
                {
                    _xpbdIncidentEdges.SetData(new[] { 0u });
                }
            }
            else
            {
                _xpbdIncidentStart.SetData(new[] { 0u });
                _xpbdIncidentEdges.SetData(new[] { 0u });
            }
            _xpbdLambda.SetData(new float[Mathf.Max(1, xpbdEdges)]);
            _xpbdDeltaA.SetData(new Vector3[Mathf.Max(1, xpbdEdges)]);
            _xpbdDeltaB.SetData(new Vector3[Mathf.Max(1, xpbdEdges)]);
            _simulationInfluenceBuffer.SetData(BuildSimulationInfluence(model));
            _rootDistanceBuffer.SetData(BuildRootDistance(model));
            _builtInfluenceHash = ComputeInfluenceHash();

            _kReset = _runtimeComputeShader.FindKernel("Reset");
            _kPredict = _runtimeComputeShader.FindKernel("Predict");
            _kLimitStrain = _runtimeComputeShader.FindKernel("LimitStrain");
            _kApplyInfluence = _runtimeComputeShader.FindKernel("ApplyInfluence");
            _kProjectCollisions = _runtimeComputeShader.FindKernel("ProjectCollisions");
            _kFinalize = _runtimeComputeShader.FindKernel("Finalize");
            _kFinalizeGuides = _runtimeComputeShader.FindKernel("FinalizeGuides");
            _kXpbdFused = _runtimeComputeShader.FindKernel("XpbdFusedLocal");
            _kCosseratFused = _runtimeComputeShader.FindKernel("CosseratFusedLocal");
            _kPinTargets = _runtimeComputeShader.FindKernel("PinTargets");
            _kClearVelocity = _runtimeComputeShader.FindKernel("ClearVelocity");
            _kReduceBounds = _runtimeComputeShader.FindKernel("ReduceBounds");
            _kFinalizeBounds = _runtimeComputeShader.FindKernel("FinalizeBounds");
            _kResetResidual = _runtimeComputeShader.FindKernel("ResetResidual");
            _kMeasureResidual = _runtimeComputeShader.FindKernel("MeasureResidual");
            _kXpbdReset = _runtimeComputeShader.FindKernel("XpbdResetLambdas");
            _kXpbdProject = _runtimeComputeShader.FindKernel("XpbdProjectEdges");
            _kXpbdGather = _runtimeComputeShader.FindKernel("XpbdGatherVertices");
            _pinKernels = new[]
            {
                _kReset, _kPredict, _kXpbdProject, _kXpbdGather, _kXpbdFused, _kCosseratFused, _kLimitStrain,
                _kApplyInfluence, _kProjectCollisions, _kFinalizeGuides, _kPinTargets,
            };
            _volumeKernels = new HairSimulationVolume.Kernels
            {
                Clear = _runtimeComputeShader.FindKernel("VolumeClear"),
                Splat = _runtimeComputeShader.FindKernel("VolumeSplat"),
                Resolve = _runtimeComputeShader.FindKernel("VolumeResolve"),
                Divergence = _runtimeComputeShader.FindKernel("VolumeDivergence"),
                Pressure = _runtimeComputeShader.FindKernel("VolumePressure"),
                Gradient = _runtimeComputeShader.FindKernel("VolumeGradient"),
            };
            AllocateVolume(model);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[HairLines] Failed to initialize GPU simulation: {exception.Message}", this);
                Release();
                return false;
            }

            _q = new[] { _qA, _qB };
            _qCur = 0; _qNext = 1;
            _groups = Mathf.CeilToInt(n / 256f);
            _controlCount = _constraints.ControlCount;
            _controlGroups = Mathf.CeilToInt(Mathf.Max(1, _controlCount) / 256f);
            _simulationStateGroups = Mathf.CeilToInt(Mathf.Max(1, _simulationStateCount) / 256f);
            _strandGroups = Mathf.CeilToInt(model.Strands.Length / 64f);
            _xpbdEdgeGroups = Mathf.CeilToInt(Mathf.Max(1, xpbdEdges) / 256f);
            _xpbdLocalEdgeGroups = Mathf.CeilToInt(
                Mathf.Max(1, _constraints.XpbdLocalEdgeCount) / 256f);
            RefreshFusedLocalBackend();
            _builtTopologyHash = topologyHash;
            _builtComplianceHash = ComputeComplianceHash();

            _ready = true;
            _initializations++;

            OpenCache();
            ResetToRest();
            if (_cacheReader == null)
            {
                Settle();
            }

            return true;
        }

        public string InspectGuideMap()
        {
            if (_simulationControlMode != HairSimulationControlMode.GuideControl)
            {
                return null;
            }

            if (_guideMapAsset == null)
            {
                return "no guide map is assigned";
            }

            if (_renderer == null)
            {
                _renderer = GetComponent<LinePrimitiveRenderer>();
            }

            LinePrimitiveModel model = _renderer != null ? _renderer.Model : null;
            if (model == null || model.IsEmpty || model.Strands == null)
            {
                return null;
            }

            return EnsureGuideMapOpened(model);
        }

        private string EnsureGuideMapOpened(LinePrimitiveModel model)
        {
            ulong mapHash = _guideMapAsset.MapHash;
            if (!ReferenceEquals(_guideMapCacheAsset, _guideMapAsset) ||
                !ReferenceEquals(_guideMapCacheModel, model) ||
                _guideMapCacheHash != mapHash)
            {
                _guideMapCacheValid = _guideMapAsset.TryOpen(model, out _guideMap,
                                                              out _guideMapCacheError);
                _guideMapCacheAsset = _guideMapAsset;
                _guideMapCacheModel = model;
                _guideMapCacheHash = mapHash;
                if (!_guideMapCacheValid)
                {
                    _guideMap = default;
                }
            }

            return _guideMapCacheValid ? null : _guideMapCacheError;
        }

        private bool[] ResolveGuideStrandMask(LinePrimitiveModel model)
        {
            _guideCount = 0;
            _guideFallbackReason = null;
            if (_simulationControlMode != HairSimulationControlMode.GuideControl ||
                model?.Strands == null || model.Strands.Length == 0)
            {
                return null;
            }

            if (_guideMapAsset == null)
            {
                _guideFallbackReason = "no guide map is assigned";
                Debug.LogWarning("[HairLines] Guide control requested without a guide map; " +
                                 "using full control.", this);
                return null;
            }

            string mapError = EnsureGuideMapOpened(model);
            if (mapError != null)
            {
                _guideFallbackReason = mapError;
                Debug.LogWarning($"[HairLines] Guide map rejected; using full control: " +
                                 $"{mapError}", this);
                return null;
            }

            IReadOnlyList<int> sources = _guideMap.GuideSourceStrands;
            if (sources == null || sources.Count == 0)
            {
                _guideFallbackReason = "guide map contains no guides";
                return null;
            }

            var mask = new bool[model.Strands.Length];
            for (int guide = 0; guide < sources.Count; guide++)
            {
                mask[sources[guide]] = true;
            }

            _guideCount = sources.Count;
            return mask;
        }

        private void CapturePins(LinePrimitiveModel model)
        {
            int hash = ComputePinHash();
            if (_pinSet.HasCapture && _pinSet.CapturedHash == hash && ReferenceEquals(_pinCaptureModel, model))
            {
                return;
            }

            _pinSet.Capture(model, transform.localToWorldMatrix, _pins, hash);
            _pinCaptureModel = model;
        }

        private int ComputePinHash()
        {
            return HairPins.ComputeHash(_pins);
        }

        private void RefreshFusedLocalBackend()
        {
            _fusedLocalActive = false;
            _fusedLanesPerStrand = 0;
            _fusedStrandsPerGroup = 0;
            _fusedGroupCount = 0;

            if (!_fusedLocalSolver)
            {
                _fusedFallbackReason = "off";
                return;
            }

            if (_constraints == null)
            {
                _fusedFallbackReason = "no constraint graph yet";
                return;
            }

            if (!_xpbdEnabled || _constraints.XpbdLocalEdgeCount <= 0)
            {
                _fusedFallbackReason = "no XPBD local rows";
                return;
            }

            if (!_constraints.EnsureFusedLocalGraph())
            {
                _fusedFallbackReason = "no validated fused map (long chains or a mixed graph)";
                return;
            }

            if (_fusedLocalRest == null || _fusedStrandBase == null ||
                _fusedLocalRest.count < _constraints.FusedLocalRest.Length)
            {
                _fusedFallbackReason = "map not uploaded yet (toggle takes effect on rebuild)";
                return;
            }

            int slots = Mathf.Clamp(_constraints.FusedMaxStrandSlots, 1,
                                     HairConstraints.MaxFusedStrandSlots);
            int lanes = Mathf.NextPowerOfTwo(slots);
            lanes = Mathf.Clamp(lanes, 1, FusedGroupLanes);
            _fusedLanesPerStrand = lanes;
            _fusedStrandsPerGroup = Mathf.Max(1, FusedGroupLanes / lanes);
            int strandCount = _model?.Strands?.Length ?? 0;
            _fusedGroupCount = Mathf.CeilToInt(strandCount / (float)_fusedStrandsPerGroup);
            _fusedLocalActive = _fusedGroupCount > 0;
            _fusedFallbackReason = _fusedLocalActive ? null : "no strands";
        }

        private void UploadGuideMap(LinePrimitiveModel model)
        {
            if (!_guideControlActive)
            {
                _strandVertexRanges.SetData(new Vector2Int[1]);
                _guideVertexRanges.SetData(new Vector2Int[1]);
                _strandGuideIds.SetData(new Vector2Int[1]);
                _strandGuideWeights.SetData(new Vector2[1]);
                _strandGuideSelf.SetData(new uint[1]);
                _vertexArcT.SetData(new float[1]);
                return;
            }

            int strandCount = Mathf.Max(1, model.Strands != null ? model.Strands.Length : 0);
            HairSimulationGuideBuildData data = _guideMap.Data;
            var guideOfStrand = new uint[strandCount];
            for (int strand = 0; strand < guideOfStrand.Length; strand++)
            {
                guideOfStrand[strand] = InvalidGuide;
            }
            for (int guide = 0; guide < data.GuideSourceStrands.Length; guide++)
            {
                guideOfStrand[data.GuideSourceStrands[guide]] = (uint)guide;
            }

            _strandVertexRanges.SetData(model.Strands);
            _guideVertexRanges.SetData(data.GuideSourceRanges);
            _strandGuideIds.SetData(data.StrandGuideIds2);
            _strandGuideWeights.SetData(data.StrandGuideWeights2);
            _strandGuideSelf.SetData(guideOfStrand);
            _vertexArcT.SetData(data.VertexArcParameters);
        }

        private void BindBuffers()
        {
            using ProfilerMarker.AutoScope scope = _bindBuffersMarker.Auto();
            var cs = _runtimeComputeShader;
            var model = _model;

            cs.SetBuffer(_kReset, Ids.X, model.PositionBuffer);
            cs.SetBuffer(_kReset, Ids.V, _velocity);
            cs.SetBuffer(_kReset, Ids.S, _inertia);
            cs.SetBuffer(_kReset, Ids.XStart, _stepStart);
            cs.SetBuffer(_kReset, Ids.RestLocal, model.RestPositionBuffer);
            cs.SetBuffer(_kReset, Ids.ControlIndices, _controlIndices);
            cs.SetBuffer(_kReset, Ids.QCur, _qA);
            cs.SetBuffer(_kReset, Ids.QNext, _qB);
            cs.SetBuffer(_kReset, Ids.CosseratFrames, _cosseratFrames);
            cs.SetBuffer(_kReset, Ids.VertexGroups, _vertexGroups);
            cs.SetBuffer(_kReset, Ids.StrandLocalIndex, _strandLocalIndex);
            cs.SetBuffer(_kReset, Ids.SimulationParameters, _simulationParameters);

            cs.SetBuffer(_kPredict, Ids.X, model.PositionBuffer);
            cs.SetBuffer(_kPredict, Ids.V, _velocity);
            cs.SetBuffer(_kPredict, Ids.S, _inertia);
            cs.SetBuffer(_kPredict, Ids.XStart, _stepStart);
            cs.SetBuffer(_kPredict, Ids.RestLocal, model.RestPositionBuffer);
            cs.SetBuffer(_kPredict, Ids.VertexGroups, _vertexGroups);
            cs.SetBuffer(_kPredict, Ids.StrandLocalIndex, _strandLocalIndex);
            cs.SetBuffer(_kPredict, Ids.ControlIndices, _controlIndices);
            cs.SetBuffer(_kPredict, Ids.SimulationParameters, _simulationParameters);
            _volume?.BindSampling(cs, _kPredict);

            cs.SetBuffer(_kXpbdReset, Ids.XpbdLambda, _xpbdLambda);
            cs.SetBuffer(_kXpbdProject, Ids.QCur, _qA);
            cs.SetBuffer(_kXpbdProject, Ids.XpbdEdgeVertices, _xpbdEdgeVertices);
            cs.SetBuffer(_kXpbdProject, Ids.XpbdEdgeRest, _xpbdEdgeRest);
            cs.SetBuffer(_kXpbdProject, Ids.XpbdEdgeType, _xpbdEdgeType);
            cs.SetBuffer(_kXpbdProject, Ids.XpbdIncidentStart, _xpbdIncidentStart);
            cs.SetBuffer(_kXpbdProject, Ids.XpbdLambda, _xpbdLambda);
            cs.SetBuffer(_kXpbdProject, Ids.XpbdDeltaA, _xpbdDeltaA);
            cs.SetBuffer(_kXpbdProject, Ids.XpbdDeltaB, _xpbdDeltaB);
            cs.SetBuffer(_kXpbdProject, Ids.RestLocal, model.RestPositionBuffer);
            cs.SetBuffer(_kXpbdProject, Ids.VertexGroups, _vertexGroups);
            cs.SetBuffer(_kXpbdProject, Ids.StrandLocalIndex, _strandLocalIndex);
            cs.SetBuffer(_kXpbdProject, Ids.SimulationParameters, _simulationParameters);
            cs.SetBuffer(_kXpbdProject, Ids.ControlIndices, _controlIndices);

            cs.SetBuffer(_kXpbdGather, Ids.QCur, _qA);
            cs.SetBuffer(_kXpbdGather, Ids.QNext, _qB);
            cs.SetBuffer(_kXpbdGather, Ids.XpbdIncidentStart, _xpbdIncidentStart);
            cs.SetBuffer(_kXpbdGather, Ids.XpbdIncidentEdges, _xpbdIncidentEdges);
            cs.SetBuffer(_kXpbdGather, Ids.XpbdDeltaA, _xpbdDeltaA);
            cs.SetBuffer(_kXpbdGather, Ids.XpbdDeltaB, _xpbdDeltaB);
            cs.SetBuffer(_kXpbdGather, Ids.RestLocal, model.RestPositionBuffer);
            cs.SetBuffer(_kXpbdGather, Ids.VertexGroups, _vertexGroups);
            cs.SetBuffer(_kXpbdGather, Ids.StrandLocalIndex, _strandLocalIndex);
            cs.SetBuffer(_kXpbdGather, Ids.SimulationParameters, _simulationParameters);
            cs.SetBuffer(_kXpbdGather, Ids.ControlIndices, _controlIndices);

            cs.SetBuffer(_kProjectCollisions, Ids.VertexGroups, _vertexGroups);
            cs.SetBuffer(_kProjectCollisions, Ids.StrandLocalIndex, _strandLocalIndex);
            cs.SetBuffer(_kProjectCollisions, Ids.RootDistance, _rootDistanceBuffer);
            cs.SetBuffer(_kProjectCollisions, Ids.SimulationParameters, _simulationParameters);
            cs.SetBuffer(_kProjectCollisions, Ids.XStart, _stepStart);
            cs.SetBuffer(_kProjectCollisions, Ids.ControlIndices, _controlIndices);

            cs.SetBuffer(_kApplyInfluence, Ids.QCur, _qA);
            cs.SetBuffer(_kApplyInfluence, Ids.XStart, _stepStart);
            cs.SetBuffer(_kApplyInfluence, Ids.RestLocal, model.RestPositionBuffer);
            cs.SetBuffer(_kApplyInfluence, Ids.SimulationInfluence, _simulationInfluenceBuffer);
            cs.SetBuffer(_kApplyInfluence, Ids.VertexGroups, _vertexGroups);
            cs.SetBuffer(_kApplyInfluence, Ids.SimulationParameters, _simulationParameters);
            cs.SetBuffer(_kApplyInfluence, Ids.ControlIndices, _controlIndices);

            cs.SetBuffer(_kFinalize, Ids.X, model.PositionBuffer);
            cs.SetBuffer(_kFinalize, Ids.V, _velocity);
            cs.SetBuffer(_kFinalize, Ids.XStart, _stepStart);
            cs.SetBuffer(_kFinalize, Ids.RestLocal, model.RestPositionBuffer);
            cs.SetBuffer(_kFinalize, Ids.SimulationInfluence, _simulationInfluenceBuffer);
            cs.SetBuffer(_kFinalize, Ids.StrandNeighbours, _strandNeighbours);
            cs.SetBuffer(_kFinalize, Ids.VertexGroups, _vertexGroups);
            cs.SetBuffer(_kFinalize, Ids.SimulationParameters, _simulationParameters);
            cs.SetBuffer(_kFinalize, Ids.InterpolationSlotAnchors, _interpolationSlotAnchors);
            cs.SetBuffer(_kFinalize, Ids.InterpolationT, _interpolationT);
            cs.SetBuffer(_kFinalize, Ids.RootDistance, _rootDistanceBuffer);

            cs.SetBuffer(_kFinalizeGuides, Ids.X, model.PositionBuffer);
            cs.SetBuffer(_kFinalizeGuides, Ids.V, _velocity);
            cs.SetBuffer(_kFinalizeGuides, Ids.XStart, _stepStart);
            cs.SetBuffer(_kFinalizeGuides, Ids.QCur, _qA);
            cs.SetBuffer(_kFinalizeGuides, Ids.RestLocal, model.RestPositionBuffer);
            cs.SetBuffer(_kFinalizeGuides, Ids.SimulationInfluence, _simulationInfluenceBuffer);
            cs.SetBuffer(_kFinalizeGuides, Ids.VertexGroups, _vertexGroups);
            cs.SetBuffer(_kFinalizeGuides, Ids.SimulationParameters, _simulationParameters);
            cs.SetBuffer(_kFinalizeGuides, Ids.InterpolationSlotAnchors, _interpolationSlotAnchors);
            cs.SetBuffer(_kFinalizeGuides, Ids.InterpolationT, _interpolationT);
            cs.SetBuffer(_kFinalizeGuides, Ids.StrandVertexRanges, _strandVertexRanges);
            cs.SetBuffer(_kFinalizeGuides, Ids.GuideVertexRanges, _guideVertexRanges);
            cs.SetBuffer(_kFinalizeGuides, Ids.StrandGuideIds, _strandGuideIds);
            cs.SetBuffer(_kFinalizeGuides, Ids.StrandGuideWeights, _strandGuideWeights);
            cs.SetBuffer(_kFinalizeGuides, Ids.StrandGuideSelf, _strandGuideSelf);
            cs.SetBuffer(_kFinalizeGuides, Ids.VertexArcT, _vertexArcT);
            cs.SetBuffer(_kFinalizeGuides, Ids.RootDistance, _rootDistanceBuffer);

            cs.SetBuffer(_kXpbdFused, Ids.QCur, _qA);
            cs.SetBuffer(_kXpbdFused, Ids.RestLocal, model.RestPositionBuffer);
            cs.SetBuffer(_kXpbdFused, Ids.ControlIndices, _controlIndices);
            cs.SetBuffer(_kXpbdFused, Ids.StrandRanges, _strandRanges);
            cs.SetBuffer(_kXpbdFused, Ids.StrandLocalIndex, _strandLocalIndex);
            cs.SetBuffer(_kXpbdFused, Ids.VertexGroups, _vertexGroups);
            cs.SetBuffer(_kXpbdFused, Ids.SimulationParameters, _simulationParameters);
            cs.SetBuffer(_kXpbdFused, Ids.XpbdIncidentStart, _xpbdIncidentStart);
            cs.SetBuffer(_kXpbdFused, Ids.FusedStrandBase, _fusedStrandBase);
            cs.SetBuffer(_kXpbdFused, Ids.FusedLocalRest, _fusedLocalRest);

            cs.SetBuffer(_kCosseratFused, Ids.QCur, _qA);
            cs.SetBuffer(_kCosseratFused, Ids.S, _inertia);
            cs.SetBuffer(_kCosseratFused, Ids.RestLocal, model.RestPositionBuffer);
            cs.SetBuffer(_kCosseratFused, Ids.ControlIndices, _controlIndices);
            cs.SetBuffer(_kCosseratFused, Ids.StrandRanges, _strandRanges);
            cs.SetBuffer(_kCosseratFused, Ids.StrandLocalIndex, _strandLocalIndex);
            cs.SetBuffer(_kCosseratFused, Ids.VertexGroups, _vertexGroups);
            cs.SetBuffer(_kCosseratFused, Ids.SimulationParameters, _simulationParameters);
            cs.SetBuffer(_kCosseratFused, Ids.CosseratFrames, _cosseratFrames);

            cs.SetBuffer(_kLimitStrain, Ids.StrandRanges, _strandRanges);
            cs.SetBuffer(_kLimitStrain, Ids.RestLocal, model.RestPositionBuffer);
            cs.SetBuffer(_kLimitStrain, Ids.X, _model.PositionBuffer);
            cs.SetBuffer(_kLimitStrain, Ids.XStart, _stepStart);
            cs.SetBuffer(_kLimitStrain, Ids.VertexGroups, _vertexGroups);
            cs.SetBuffer(_kLimitStrain, Ids.SimulationParameters, _simulationParameters);
            cs.SetBuffer(_kLimitStrain, Ids.StrandLocalIndex, _strandLocalIndex);
            cs.SetBuffer(_kLimitStrain, Ids.ControlIndices, _controlIndices);

            cs.SetBuffer(_kReduceBounds, Ids.X, model.PositionBuffer);
            cs.SetBuffer(_kReduceBounds, Ids.BoundsPartials, _boundsPartials);
            cs.SetBuffer(_kFinalizeBounds, Ids.BoundsPartials, _boundsPartials);
            cs.SetBuffer(_kFinalizeBounds, Ids.BoundsResult, _boundsResult);
            cs.SetBuffer(_kResetResidual, Ids.Residual, _residual);
            cs.SetBuffer(_kMeasureResidual, Ids.QCur, _qA);
            cs.SetBuffer(_kMeasureResidual, Ids.VertexGroups, _vertexGroups);
            cs.SetBuffer(_kMeasureResidual, Ids.SimulationParameters, _simulationParameters);
            cs.SetBuffer(_kMeasureResidual, Ids.RestLocal, model.RestPositionBuffer);
            cs.SetBuffer(_kMeasureResidual, Ids.Residual, _residual);
            cs.SetBuffer(_kMeasureResidual, Ids.ControlIndices, _controlIndices);
            cs.SetBuffer(_kMeasureResidual, Ids.XpbdEdgeVertices, _xpbdEdgeVertices);
            cs.SetBuffer(_kMeasureResidual, Ids.XpbdEdgeRest, _xpbdEdgeRest);
            cs.SetBuffer(_kMeasureResidual, Ids.XpbdEdgeType, _xpbdEdgeType);

            foreach (int kernel in _pinKernels)
            {
                _pinSet.SetBuffers(cs, kernel);
            }
        }

        private void BindSimulationCommandBuffers(CommandBuffer commands)
        {
            if (commands == null || _runtimeComputeShader == null || _model == null)
            {
                return;
            }

            ComputeShader cs = _runtimeComputeShader;
            GraphicsBuffer rest = _model.RestPositionBuffer;

            commands.SetComputeIntParam(cs, Ids.SimulationInfluenceIsUnit,
                                        _hasNonUnitSimulationInfluence ? 0 : 1);
            commands.SetComputeVectorArrayParam(cs, Ids.ShapeA, _shapeA);
            commands.SetComputeVectorArrayParam(cs, Ids.ShapeB, _shapeB);
            commands.SetComputeVectorArrayParam(cs, Ids.ShapeRot, _shapeRot);
            commands.SetComputeIntParam(cs, Ids.ShapeCount, _shapeCount);
            bool sceneValid = _sceneCollisionField?.IsValid ?? false;
            Bounds sceneBounds = sceneValid
                ? _sceneCollisionField.Bounds
                : new Bounds(Vector3.zero, Vector3.one);
            Vector3 sceneSize = sceneBounds.size;
            Vector3 sceneInverseSize = new Vector3(
                1f / Mathf.Max(sceneSize.x, 1e-6f),
                1f / Mathf.Max(sceneSize.y, 1e-6f),
                1f / Mathf.Max(sceneSize.z, 1e-6f));
            commands.SetComputeIntParam(cs, Ids.SceneCollisionValid, sceneValid ? 1 : 0);
            commands.SetComputeVectorParam(cs, Ids.SceneCollisionMinimum, sceneBounds.min);
            commands.SetComputeVectorParam(cs, Ids.SceneCollisionInverseSize, sceneInverseSize);
            commands.SetComputeVectorParam(cs, Ids.SceneCollisionCellSize,
                                           sceneValid ? _sceneCollisionField.CellSize : Vector3.one);

            commands.SetComputeBufferParam(cs, _kPredict, Ids.X, _model.PositionBuffer);
            commands.SetComputeBufferParam(cs, _kPredict, Ids.V, _velocity);
            commands.SetComputeBufferParam(cs, _kPredict, Ids.S, _inertia);
            commands.SetComputeBufferParam(cs, _kPredict, Ids.XStart, _stepStart);
            commands.SetComputeBufferParam(cs, _kPredict, Ids.RestLocal, rest);
            commands.SetComputeBufferParam(cs, _kPredict, Ids.VertexGroups, _vertexGroups);
            commands.SetComputeBufferParam(cs, _kPredict, Ids.StrandLocalIndex, _strandLocalIndex);
            commands.SetComputeBufferParam(cs, _kPredict, Ids.ControlIndices, _controlIndices);
            commands.SetComputeBufferParam(cs, _kPredict, Ids.SimulationParameters, _simulationParameters);
            _volume?.BindSampling(commands, cs, _kPredict);
            commands.SetComputeBufferParam(cs, _volumeKernels.Splat, Ids.V, _velocity);
            commands.SetComputeBufferParam(cs, _volumeKernels.Splat, Ids.ControlIndices, _controlIndices);
            commands.SetComputeBufferParam(cs, _volumeKernels.Splat, Ids.VertexGroups, _vertexGroups);
            commands.SetComputeBufferParam(cs, _volumeKernels.Splat, Ids.StrandLocalIndex, _strandLocalIndex);
            commands.SetComputeBufferParam(cs, _volumeKernels.Splat, Ids.SimulationParameters,
                                           _simulationParameters);

            commands.SetComputeBufferParam(cs, _kXpbdReset, Ids.XpbdLambda, _xpbdLambda);
            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.XpbdEdgeVertices, _xpbdEdgeVertices);
            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.XpbdEdgeRest, _xpbdEdgeRest);
            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.XpbdEdgeType, _xpbdEdgeType);
            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.XpbdIncidentStart, _xpbdIncidentStart);
            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.XpbdLambda, _xpbdLambda);
            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.XpbdDeltaA, _xpbdDeltaA);
            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.XpbdDeltaB, _xpbdDeltaB);
            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.RestLocal, rest);
            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.VertexGroups, _vertexGroups);
            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.StrandLocalIndex, _strandLocalIndex);
            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.SimulationParameters,
                                           _simulationParameters);
            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.ControlIndices, _controlIndices);

            commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.XpbdIncidentStart,
                                           _xpbdIncidentStart);
            commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.XpbdIncidentEdges,
                                           _xpbdIncidentEdges);
            commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.XpbdDeltaA, _xpbdDeltaA);
            commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.XpbdDeltaB, _xpbdDeltaB);
            commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.RestLocal, rest);
            commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.VertexGroups, _vertexGroups);
            commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.StrandLocalIndex, _strandLocalIndex);
            commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.SimulationParameters,
                                           _simulationParameters);
            commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.ControlIndices, _controlIndices);

            commands.SetComputeBufferParam(cs, _kLimitStrain, Ids.StrandRanges, _strandRanges);
            commands.SetComputeBufferParam(cs, _kLimitStrain, Ids.RestLocal, rest);
            commands.SetComputeBufferParam(cs, _kLimitStrain, Ids.XStart, _stepStart);
            commands.SetComputeBufferParam(cs, _kLimitStrain, Ids.VertexGroups, _vertexGroups);
            commands.SetComputeBufferParam(cs, _kLimitStrain, Ids.SimulationParameters,
                                           _simulationParameters);
            commands.SetComputeBufferParam(cs, _kLimitStrain, Ids.StrandLocalIndex, _strandLocalIndex);
            commands.SetComputeBufferParam(cs, _kLimitStrain, Ids.ControlIndices, _controlIndices);

            commands.SetComputeBufferParam(cs, _kApplyInfluence, Ids.XStart, _stepStart);
            commands.SetComputeBufferParam(cs, _kApplyInfluence, Ids.RestLocal, rest);
            commands.SetComputeBufferParam(cs, _kApplyInfluence, Ids.SimulationInfluence,
                                           _simulationInfluenceBuffer);
            commands.SetComputeBufferParam(cs, _kApplyInfluence, Ids.VertexGroups, _vertexGroups);
            commands.SetComputeBufferParam(cs, _kApplyInfluence, Ids.SimulationParameters,
                                           _simulationParameters);
            commands.SetComputeBufferParam(cs, _kApplyInfluence, Ids.ControlIndices, _controlIndices);

            commands.SetComputeBufferParam(cs, _kProjectCollisions, Ids.VertexGroups, _vertexGroups);
            commands.SetComputeBufferParam(cs, _kProjectCollisions, Ids.StrandLocalIndex,
                                           _strandLocalIndex);
            commands.SetComputeBufferParam(cs, _kProjectCollisions, Ids.RootDistance,
                                           _rootDistanceBuffer);
            commands.SetComputeBufferParam(cs, _kProjectCollisions, Ids.SimulationParameters,
                                           _simulationParameters);
            commands.SetComputeBufferParam(cs, _kProjectCollisions, Ids.XStart, _stepStart);
            commands.SetComputeBufferParam(cs, _kProjectCollisions, Ids.ControlIndices, _controlIndices);
            Texture sceneDistance = (_sceneCollisionField?.IsValid ?? false)
                ? _sceneCollisionField.DistanceTexture
                : GetSceneCollisionFallback();
            commands.SetComputeTextureParam(cs, _kProjectCollisions,
                                            Ids.SceneCollisionDistance, sceneDistance);

            commands.SetComputeBufferParam(cs, _kFinalize, Ids.X, _model.PositionBuffer);
            commands.SetComputeBufferParam(cs, _kFinalize, Ids.V, _velocity);
            commands.SetComputeBufferParam(cs, _kFinalize, Ids.XStart, _stepStart);
            commands.SetComputeBufferParam(cs, _kFinalize, Ids.RestLocal, rest);
            commands.SetComputeBufferParam(cs, _kFinalize, Ids.SimulationInfluence,
                                           _simulationInfluenceBuffer);
            commands.SetComputeBufferParam(cs, _kFinalize, Ids.StrandNeighbours, _strandNeighbours);
            commands.SetComputeBufferParam(cs, _kFinalize, Ids.VertexGroups, _vertexGroups);
            commands.SetComputeBufferParam(cs, _kFinalize, Ids.SimulationParameters,
                                           _simulationParameters);
            commands.SetComputeBufferParam(cs, _kFinalize, Ids.InterpolationSlotAnchors,
                                           _interpolationSlotAnchors);
            commands.SetComputeBufferParam(cs, _kFinalize, Ids.InterpolationT, _interpolationT);
            commands.SetComputeBufferParam(cs, _kFinalize, Ids.RootDistance, _rootDistanceBuffer);

            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.X, _model.PositionBuffer);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.V, _velocity);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.XStart, _stepStart);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.RestLocal, rest);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.SimulationInfluence,
                                           _simulationInfluenceBuffer);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.VertexGroups, _vertexGroups);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.SimulationParameters,
                                           _simulationParameters);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.InterpolationSlotAnchors,
                                           _interpolationSlotAnchors);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.InterpolationT,
                                           _interpolationT);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.StrandVertexRanges,
                                           _strandVertexRanges);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.GuideVertexRanges,
                                           _guideVertexRanges);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.StrandGuideIds,
                                           _strandGuideIds);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.StrandGuideWeights,
                                           _strandGuideWeights);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.StrandGuideSelf,
                                           _strandGuideSelf);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.VertexArcT, _vertexArcT);
            commands.SetComputeBufferParam(cs, _kFinalizeGuides, Ids.RootDistance,
                                          _rootDistanceBuffer);

            commands.SetComputeBufferParam(cs, _kXpbdFused, Ids.RestLocal, rest);
            commands.SetComputeBufferParam(cs, _kXpbdFused, Ids.ControlIndices, _controlIndices);
            commands.SetComputeBufferParam(cs, _kXpbdFused, Ids.StrandRanges, _strandRanges);
            commands.SetComputeBufferParam(cs, _kXpbdFused, Ids.StrandLocalIndex, _strandLocalIndex);
            commands.SetComputeBufferParam(cs, _kXpbdFused, Ids.VertexGroups, _vertexGroups);
            commands.SetComputeBufferParam(cs, _kXpbdFused, Ids.SimulationParameters,
                                           _simulationParameters);
            commands.SetComputeBufferParam(cs, _kXpbdFused, Ids.XpbdIncidentStart,
                                           _xpbdIncidentStart);
            commands.SetComputeBufferParam(cs, _kXpbdFused, Ids.FusedStrandBase, _fusedStrandBase);
            commands.SetComputeBufferParam(cs, _kXpbdFused, Ids.FusedLocalRest, _fusedLocalRest);

            commands.SetComputeBufferParam(cs, _kCosseratFused, Ids.S, _inertia);
            commands.SetComputeBufferParam(cs, _kCosseratFused, Ids.RestLocal, rest);
            commands.SetComputeBufferParam(cs, _kCosseratFused, Ids.ControlIndices, _controlIndices);
            commands.SetComputeBufferParam(cs, _kCosseratFused, Ids.StrandRanges, _strandRanges);
            commands.SetComputeBufferParam(cs, _kCosseratFused, Ids.StrandLocalIndex, _strandLocalIndex);
            commands.SetComputeBufferParam(cs, _kCosseratFused, Ids.VertexGroups, _vertexGroups);
            commands.SetComputeBufferParam(cs, _kCosseratFused, Ids.SimulationParameters,
                                           _simulationParameters);
            commands.SetComputeBufferParam(cs, _kCosseratFused, Ids.CosseratFrames, _cosseratFrames);

            commands.SetComputeBufferParam(cs, _kMeasureResidual, Ids.VertexGroups, _vertexGroups);
            commands.SetComputeBufferParam(cs, _kMeasureResidual, Ids.SimulationParameters,
                                           _simulationParameters);
            commands.SetComputeBufferParam(cs, _kMeasureResidual, Ids.RestLocal, rest);
            commands.SetComputeBufferParam(cs, _kMeasureResidual, Ids.Residual, _residual);
            commands.SetComputeBufferParam(cs, _kMeasureResidual, Ids.ControlIndices, _controlIndices);
            commands.SetComputeBufferParam(cs, _kMeasureResidual, Ids.XpbdEdgeVertices, _xpbdEdgeVertices);
            commands.SetComputeBufferParam(cs, _kMeasureResidual, Ids.XpbdEdgeRest, _xpbdEdgeRest);
            commands.SetComputeBufferParam(cs, _kMeasureResidual, Ids.XpbdEdgeType, _xpbdEdgeType);

            commands.SetComputeBufferParam(cs, _kReduceBounds, Ids.X, _model.PositionBuffer);
            commands.SetComputeBufferParam(cs, _kReduceBounds, Ids.BoundsPartials, _boundsPartials);
            commands.SetComputeBufferParam(cs, _kFinalizeBounds, Ids.BoundsPartials, _boundsPartials);
            commands.SetComputeBufferParam(cs, _kFinalizeBounds, Ids.BoundsResult, _boundsResult);
            commands.SetComputeBufferParam(cs, _kResetResidual, Ids.Residual, _residual);

            foreach (int kernel in _pinKernels)
            {
                _pinSet.SetBuffers(commands, cs, kernel);
            }
        }

        private void UpdateStaticUniforms()
        {
            using ProfilerMarker.AutoScope scope = _staticUniformsMarker.Auto();
            var cs = _runtimeComputeShader;
            RefreshResolvedGroupSettings();
            cs.SetInt(Ids.Count, _model.VertexCount);
            cs.SetInt(Ids.ControlCount, _controlCount);
            cs.SetInt(Ids.StateCount, _simulationStateCount);
            cs.SetInt(Ids.StrandCount, _model.Strands != null ? _model.Strands.Length : 0);
            cs.SetInt(Ids.GuideLengthSweep, _guideLengthSweep ? 1 : 0);
            cs.SetMatrix(Ids.ObjectToWorld, transform.localToWorldMatrix);
            cs.SetFloat(Ids.Time, Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartupAsDouble);
            cs.SetInt(Ids.SimulationInfluenceIsUnit, _hasNonUnitSimulationInfluence ? 0 : 1);

            int frame = Time.frameCount;
            if (_staticUniformFrame != frame)
            {
                bool hasUnityWind = HasUnityWindGroups();
                if (hasUnityWind)
                {
                    _sampledUnityWind = SampleUnityWind(out _sampledUnityWindTurbulence);
                }
                else
                {
                    _sampledUnityWind = Vector3.zero;
                    _sampledUnityWindTurbulence = -1f;
                }

                int parameterHash = ComputeGpuParameterHash();
                bool parametersChanged = !_hasUploadedParameters || parameterHash != _uploadedParameterHash;
                parametersChanged |= hasUnityWind;
                if (parametersChanged)
                {
                    BuildGpuParameters();
                }

                int shapeInputHash = ComputeShapeInputHash();
                bool shapesInputChanged = !_hasBuiltShapes || shapeInputHash != _shapeInputHash;
                if (shapesInputChanged)
                {
                    _shapeCount = BuildCollisionShapes();
                    _shapeInputHash = shapeInputHash;
                    _hasBuiltShapes = true;
                }

                int shapeHash = shapesInputChanged ? ComputeBuiltShapeHash() : _uploadedShapeHash;
                bool shapesChanged = !_hasUploadedShapes || shapesInputChanged || shapeHash != _uploadedShapeHash;
                bool shapeCountsChanged = HaveShapeCountsChanged();

                if (shapesChanged)
                {
                    cs.SetVectorArray(Ids.ShapeA, _shapeA);
                    cs.SetVectorArray(Ids.ShapeB, _shapeB);
                    cs.SetVectorArray(Ids.ShapeRot, _shapeRot);
                    _uploadedShapeHash = shapeHash;
                    _hasUploadedShapes = true;
                }

                if (parametersChanged || shapeCountsChanged)
                {
                    _simulationParameters.SetData(_gpuParameters);
                    StoreUploadedShapeCounts();
                    _uploadedParameterHash = parameterHash;
                    _hasUploadedParameters = true;
                }

                if (parametersChanged)
                {
                }

                _staticUniformFrame = frame;
            }

            cs.SetInt(Ids.ShapeCount, _shapeCount);
            BindSceneCollisionField(cs);
        }

        private static float MaxAbsScale(Vector3 s) =>
            Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));

        private void Store(int index, in ResolvedShape shape)
        {
            _shapeA[index] = new Vector4(shape.a.x, shape.a.y, shape.a.z, shape.radius);
            _shapeB[index] = new Vector4(shape.b.x, shape.b.y, shape.b.z, (float)shape.kind);
            _shapeRot[index] = new Vector4(shape.rotation.x, shape.rotation.y,
                                            shape.rotation.z, shape.rotation.w);
        }

        public static ResolvedShape Resolve(in CollisionShape shape, float extraOffset = 0f)
        {
            var result = new ResolvedShape { rotation = Quaternion.identity };
            var t = shape.transform;
            if (t == null)
            {
                return result;
            }

            float padding = Mathf.Max(0f, shape.padding + extraOffset);
            Vector3 scale = t.lossyScale;

            if (t.TryGetComponent(out SphereCollider sphere))
            {
                result.kind = ShapeKind.Sphere;
                result.a = t.TransformPoint(sphere.center);
                result.radius = sphere.radius * MaxAbsScale(scale) + padding;
            }
            else if (t.TryGetComponent(out CapsuleCollider capsule))
            {
                int dir = Mathf.Clamp(capsule.direction, 0, 2);
                float axisScale = Mathf.Abs(scale[dir]);
                float radialScale = Mathf.Max(Mathf.Abs(scale[(dir + 1) % 3]),
                                              Mathf.Abs(scale[(dir + 2) % 3]));
                float radius = capsule.radius * radialScale;
                float half = Mathf.Max(0f, capsule.height * 0.5f * axisScale - radius);

                Vector3 axis = dir == 0 ? Vector3.right : dir == 1 ? Vector3.up : Vector3.forward;
                Vector3 centre = t.TransformPoint(capsule.center);
                Vector3 axisWorld = t.TransformDirection(axis).normalized;

                result.kind = ShapeKind.Capsule;
                result.a = centre - axisWorld * half;
                result.b = centre + axisWorld * half;
                result.radius = radius + padding;
            }
            else if (t.TryGetComponent(out BoxCollider box))
            {
                result.kind = ShapeKind.Box;
                result.a = t.TransformPoint(box.center);
                result.b = Vector3.Scale(box.size * 0.5f,
                    new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                result.rotation = t.rotation;
                result.radius = padding;
            }
            else
            {
                result.unsupportedCollider = t.TryGetComponent(out Collider _);
            }

            result.valid = result.kind == ShapeKind.Box
                ? result.b.sqrMagnitude > 0f
                : result.radius > 0f;
            return result;
        }

        public static ResolvedShape Resolve(in BoneCapsule capsule, float extraOffset = 0f)
        {
            var result = new ResolvedShape { rotation = Quaternion.identity, kind = ShapeKind.Capsule };
            Transform bone = capsule.bone;
            if (bone == null)
            {
                return result;
            }

            result.a = bone.TransformPoint(capsule.start);
            result.b = bone.TransformPoint(capsule.end);
            result.radius = capsule.radius * RadialScale(bone.localToWorldMatrix, capsule.end - capsule.start) +
                            Mathf.Max(0f, extraOffset);
            result.valid = result.radius > 0f;
            return result;
        }

        private static float RadialScale(Matrix4x4 boneToWorld, Vector3 axis)
        {
            if (axis.sqrMagnitude <= 1e-20f)
            {
                return MaxAbsScale(boneToWorld.lossyScale);
            }

            axis.Normalize();
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            Vector3 mu = boneToWorld.MultiplyVector(u);
            Vector3 mv = boneToWorld.MultiplyVector(v);
            float a = mu.sqrMagnitude;
            float c = mv.sqrMagnitude;
            float b = Vector3.Dot(mu, mv);
            float half = (a - c) * 0.5f;
            return Mathf.Sqrt((a + c) * 0.5f + Mathf.Sqrt(half * half + b * b));
        }

        private void PrepareSceneCollisionField()
        {
            using ProfilerMarker.AutoScope scope = _sceneCollisionMarker.Auto();
            RefreshResolvedGroupSettings();
            if (!AnySceneCollisionEnabled())
            {
                ReleaseSceneCollisionField();
                return;
            }

            bool splat = _sceneFieldBuild == HairSceneFieldBuild.TriangleSplat;
            if (splat && _sceneSplatShader == null)
            {
                _sceneSplatShader = FindSceneSplatShader();
            }
            else if (!splat && _sceneCollisionShader == null)
            {
                _sceneCollisionShader = FindSceneCollisionShader();
            }

            ComputeShader source = splat ? _sceneSplatShader : _sceneCollisionShader;
            if (!HairSceneCollisionField.IsSupported(_sceneFieldBuild) || source == null)
            {
                if (!_sceneCollisionWarningIssued)
                {
                    Debug.LogWarning(splat
                        ? "[HairLines] Triangle splat scene collision requires compute shaders, writable " +
                          "3D half-float textures and HairSceneSplat.compute. Colliders remain active."
                        : "[HairLines] Ray-traced scene collision requires inline ray tracing, 3D render " +
                          "textures and HairSceneCollision.compute. Colliders remain active.", this);
                    _sceneCollisionWarningIssued = true;
                }
                ReleaseSceneCollisionField();
                return;
            }

            try
            {
                EnsureRuntimeSceneCollisionShader(source);
                _sceneCollisionField = HairSceneCollisionField.Acquire(_sceneCollisionField,
                    source, _sceneFieldBuild, _sceneCollisionMask, _sceneCollisionResolution,
                    _sceneCollisionBoundsPadding, GetInstanceID());
                _sceneCollisionField.Update(_runtimeSceneCollisionShader, _simulationBounds,
                    _sceneCollisionMask, _sceneCollisionResolution, _sceneCollisionBoundsPadding,
                    _sceneFieldFlood, _sceneFieldFloodIterations, _sceneFieldDiagonalFlood);
                _sceneCollisionWarningIssued = false;
            }
            catch (Exception exception)
            {
                if (!_sceneCollisionWarningIssued)
                {
                    Debug.LogWarning($"[HairLines] Scene collision field could not be built: " +
                                     $"{exception.Message}. Analytic collisions remain active.", this);
                    _sceneCollisionWarningIssued = true;
                }
                ReleaseSceneCollisionField();
            }
        }

        private bool AnySceneCollisionEnabled()
        {
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (_resolvedGroupEnabled[i] && _resolvedGroupSettings[i].CollideWithSceneGeometry)
                {
                    return true;
                }
            }

            return false;
        }

        private bool UsesColliders(HairSimulationSettings settings)
        {
            return !settings.CollideWithSceneGeometry || !settings.CollidersAsSceneFallback ||
                   !(_sceneCollisionField?.IsValid ?? false);
        }

        private void BindSceneCollisionField(ComputeShader shader)
        {
            bool valid = _sceneCollisionField?.IsValid ?? false;
            shader.SetInt(Ids.SceneCollisionValid, valid ? 1 : 0);
            Texture texture = valid
                ? _sceneCollisionField.DistanceTexture
                : GetSceneCollisionFallback();
            shader.SetTexture(_kProjectCollisions, Ids.SceneCollisionDistance, texture);

            Bounds fieldBounds = valid
                ? _sceneCollisionField.Bounds
                : new Bounds(Vector3.zero, Vector3.one);
            Vector3 size = fieldBounds.size;
            Vector3 inverseSize = new Vector3(1f / size.x, 1f / size.y, 1f / size.z);
            shader.SetVector(Ids.SceneCollisionMinimum, fieldBounds.min);
            shader.SetVector(Ids.SceneCollisionInverseSize, inverseSize);
            shader.SetVector(Ids.SceneCollisionCellSize,
                valid ? _sceneCollisionField.CellSize : Vector3.one);
        }

        private Texture3D GetSceneCollisionFallback()
        {
            if (_sceneCollisionFallback != null)
            {
                return _sceneCollisionFallback;
            }

            _sceneCollisionFallback = new Texture3D(1, 1, 1, TextureFormat.RFloat, false)
            {
                name = "Hair Scene Collision Fallback",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _sceneCollisionFallback.SetPixel(0, 0, 0, Color.white);
            _sceneCollisionFallback.Apply(false, true);
            return _sceneCollisionFallback;
        }

        private void ReleaseSceneCollisionField()
        {
            _sceneCollisionField?.Dispose();
            _sceneCollisionField = null;
        }

        private void CapturePreviousPositions(bool force)
        {
            if (_model == null || !LineRenderingFeature.HasActiveMotionVectorDemand)
            {
                return;
            }

            if (force || _model.PreviousPositionsFrame != Time.frameCount)
            {
                _model.CapturePreviousPositions();
            }
        }

        private void Simulate(float dt)
        {
            using ProfilerMarker.AutoScope scope = _simulateMarker.Auto();
            CapturePreviousPositions(force: false);
            var cs = _runtimeComputeShader;
            UpdateStaticUniforms();
            int requestedSubsteps = 1;
            int requestedIterations = 1;
            bool limitStrain = false;
            bool resolveFinalCollisions = false;
            bool anyCosseratGroup = false;
            bool anyDistanceGroup = false;
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (!_resolvedGroupEnabled[i])
                {
                    continue;
                }
                HairSimulationSettings settings = _resolvedGroupSettings[i];
                bool cosserat = settings.StrandModel == HairStrandModel.CosseratRods;
                anyCosseratGroup |= cosserat;
                anyDistanceGroup |= !cosserat;
                requestedSubsteps = Mathf.Max(requestedSubsteps, Mathf.Max(1, _runtimeSubsteps[i]));
                requestedIterations = Mathf.Max(requestedIterations, Mathf.Max(1, _runtimeIterations[i]));
                limitStrain |= settings.StrainLimit > 0f;
                resolveFinalCollisions |= settings.CollideWithSceneGeometry ||
                                          (settings.Colliders != null && settings.Colliders.Length > 0) ||
                                          (settings.BoneCapsules != null && settings.BoneCapsules.Length > 0);
            }

            bool useBudgetBuckets = RefreshBudgetBuckets();
            int substeps = useBudgetBuckets
                ? HairSimulationBudgetScheduler.GetMaxSubsteps(_budgetBuckets)
                : requestedSubsteps;
            if (substeps <= 0)
            {
                useBudgetBuckets = false;
                substeps = requestedSubsteps;
            }
            _lastScheduledIterationCount = 0;
            _lastLegacyIterationCount = requestedSubsteps * requestedIterations;
            bool hasXpbdLocalEdges = _constraints != null && _constraints.XpbdLocalEdgeCount > 0;
            CommandBuffer commands = _simulationCommandBuffer ??= new CommandBuffer
            {
                name = "HairLines Simulation",
            };
            commands.Clear();
            BindSimulationCommandBuffers(commands);
            commands.SetComputeIntParam(cs, Ids.Count, _model.VertexCount);
            commands.SetComputeIntParam(cs, Ids.ControlCount, _controlCount);
            commands.SetComputeIntParam(cs, Ids.StateCount, _simulationStateCount);
            commands.SetComputeIntParam(cs, Ids.StrandCount,
                _model.Strands != null ? _model.Strands.Length : 0);
            commands.SetComputeMatrixParam(cs, Ids.ObjectToWorld, transform.localToWorldMatrix);
            commands.SetComputeFloatParam(cs, Ids.Time,
                Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartupAsDouble);
            commands.SetComputeFloatParam(cs, Ids.FrameDelta, dt);
            commands.SetComputeIntParam(cs, Ids.ShapeCount, _shapeCount);
            _pinSet.UpdateTargets(commands, cs, _kPinTargets, _model, transform.localToWorldMatrix);

            if (_xpbdEnabled)
            {
                commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.RestLocal,
                                               _model.RestPositionBuffer);
                commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.XpbdIncidentStart,
                                               _xpbdIncidentStart);
                commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.XpbdIncidentStart,
                                               _xpbdIncidentStart);
                commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.XpbdIncidentEdges,
                                               _xpbdIncidentEdges);
            }

            bool fusedLocalSolve = _fusedLocalActive && _xpbdEnabled && hasXpbdLocalEdges &&
                                   hasXpbdLocalEdges;
            if (fusedLocalSolve)
            {
                commands.SetComputeIntParam(cs, Ids.FusedLanesPerStrand, _fusedLanesPerStrand);
                commands.SetComputeIntParam(cs, Ids.FusedStrandsPerGroup, _fusedStrandsPerGroup);
            }

            if (_volume != null)
            {
                _volume.SetUniforms(commands, cs, transform);
                if (_volume.Active)
                {
                    commands.SetComputeBufferParam(cs, _volumeKernels.Splat, Ids.QCur, _q[_qCur]);
                    using (new ProfilingScope(commands, _gpuVolumeSampler))
                    {
                        _volume.Record(commands, cs, _volumeKernels, _controlGroups);
                    }
                }
            }

            int cosseratGroupMask = ComputeCosseratGroupMask();
            if (cosseratGroupMask != _cosseratGroupMask && _cosseratFrames != null)
            {
                _cosseratFrames.SetData(new Vector4[_cosseratFrames.count]);
                _cosseratGroupMask = cosseratGroupMask;
            }

            for (int step = 0; step < substeps; step++)
            {
                int iterations = useBudgetBuckets
                    ? HairSimulationBudgetScheduler.GetIterationsForSubstep(_budgetBuckets, step)
                    : requestedIterations;
                if (iterations <= 0)
                {
                    continue;
                }
                _lastScheduledIterationCount += iterations;
                bool xpbdActiveThisSubstep = !useBudgetBuckets ||
                    HairSimulationBudgetScheduler.IsSolverActive(
                        _budgetBuckets, step, 0);
                commands.SetComputeIntParam(cs, Ids.CurrentSubstep, step);
                commands.SetComputeBufferParam(cs, _kPredict, Ids.QCur, _q[_qCur]);
                using (ProfilerMarker.AutoScope predictScope = _predictMarker.Auto())
                using (new ProfilingScope(commands, _gpuPredictSampler))
                {
                    commands.DispatchCompute(cs, _kPredict, _controlGroups, 1, 1);
                }

                if (_xpbdEnabled && xpbdActiveThisSubstep &&
                    _constraints != null && _constraints.XpbdEdgeCount > 0)
                {
                    int resetOffset = 0;
                    int resetCount = fusedLocalSolve ? 0 : _constraints.XpbdEdgeCount;
                    if (resetCount > 0)
                    {
                        commands.SetComputeIntParam(cs, Ids.XpbdEdgeOffset, resetOffset);
                        commands.SetComputeIntParam(cs, Ids.XpbdEdgeCount, resetCount);
                        using (new ProfilingScope(commands, _gpuXpbdResetSampler))
                        {
                            commands.DispatchCompute(cs, _kXpbdReset,
                                                     Mathf.CeilToInt(resetCount / 256f), 1, 1);
                        }
                    }
                }

                if (fusedLocalSolve)
                {
                    commands.SetComputeIntParam(cs, Ids.FusedIterations, iterations);
                    if (anyDistanceGroup)
                    {
                        commands.SetComputeBufferParam(cs, _kXpbdFused, Ids.QCur, _q[_qCur]);
                        using (ProfilerMarker.AutoScope fusedScope = _iterateMarker.Auto())
                        using (new ProfilingScope(commands, _gpuXpbdFusedSampler))
                        {
                            commands.DispatchCompute(cs, _kXpbdFused, _fusedGroupCount, 1, 1);
                        }
                    }

                    if (anyCosseratGroup)
                    {
                        commands.SetComputeBufferParam(cs, _kCosseratFused, Ids.QCur, _q[_qCur]);
                        using (ProfilerMarker.AutoScope rodScope = _iterateMarker.Auto())
                        using (new ProfilingScope(commands, _gpuCosseratFusedSampler))
                        {
                            commands.DispatchCompute(cs, _kCosseratFused, _fusedGroupCount, 1, 1);
                        }
                    }
                }

                for (int k = 0; k < iterations; k++)
                {
                    bool xpbdActiveThisIteration = !useBudgetBuckets ||
                        HairSimulationBudgetScheduler.IsSolverActive(
                            _budgetBuckets, step, k);
                    commands.SetComputeIntParam(cs, Ids.CurrentIteration, k);

                    if (fusedLocalSolve)
                    {
                        break;
                    }

                    if (_xpbdEnabled)
                    {
                        if (hasXpbdLocalEdges && xpbdActiveThisIteration)
                        {
                            commands.SetComputeIntParam(cs, Ids.XpbdEdgeOffset, 0);
                            commands.SetComputeIntParam(cs, Ids.XpbdEdgeCount,
                                                        _constraints.XpbdLocalEdgeCount);
                            commands.SetComputeBufferParam(cs, _kXpbdProject, Ids.QCur,
                                                           _q[_qCur]);
                            using (new ProfilingScope(commands, _gpuXpbdProjectLocalSampler))
                            {
                                commands.DispatchCompute(cs, _kXpbdProject,
                                                         _xpbdLocalEdgeGroups, 1, 1);
                            }
                        }

                        commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.QCur, _q[_qCur]);
                        commands.SetComputeBufferParam(cs, _kXpbdGather, Ids.QNext, _q[_qNext]);
                        using (ProfilerMarker.AutoScope xpbdScope = _iterateMarker.Auto())
                        using (new ProfilingScope(commands, _gpuXpbdGatherSampler))
                        {
                            commands.DispatchCompute(cs, _kXpbdGather, _controlGroups, 1, 1);
                        }
                    }

                    int rotated = _qCur;
                    _qCur = _qNext;
                    _qNext = rotated;
                }

                if (_hasNonUnitSimulationInfluence)
                {
                    commands.SetComputeBufferParam(cs, _kApplyInfluence, Ids.QCur, _q[_qCur]);
                    commands.SetComputeBufferParam(cs, _kApplyInfluence, Ids.XStart, _stepStart);
                    using (new ProfilingScope(commands, _gpuInfluenceSampler))
                    {
                        commands.DispatchCompute(cs, _kApplyInfluence, _controlGroups, 1, 1);
                    }
                }

                if (limitStrain && _strandGroups > 0 && step == substeps - 1)
                {
                    commands.SetComputeBufferParam(cs, _kLimitStrain, Ids.X, _q[_qCur]);
                    commands.SetComputeBufferParam(cs, _kLimitStrain, Ids.XStart, _stepStart);
                    using (ProfilerMarker.AutoScope strainScope = _strainMarker.Auto())
                    using (new ProfilingScope(commands, _gpuStrainSampler))
                    {
                        commands.DispatchCompute(cs, _kLimitStrain, _strandGroups, 1, 1);
                    }
                }

                if (resolveFinalCollisions)
                {
                    commands.SetComputeBufferParam(cs, _kProjectCollisions, Ids.X, _q[_qCur]);
                    commands.SetComputeBufferParam(cs, _kProjectCollisions, Ids.XStart, _stepStart);
                    using (ProfilerMarker.AutoScope collisionScope = _collisionProjectionMarker.Auto())
                    using (new ProfilingScope(commands, _gpuCollisionSampler))
                    {
                        commands.DispatchCompute(cs, _kProjectCollisions, _controlGroups, 1, 1);
                    }
                }
            }

            int finalizeKernel = _guideControlActive ? _kFinalizeGuides : _kFinalize;
            int finalizeGroups = _guideControlActive ? _strandGroups : _groups;
            commands.SetComputeBufferParam(cs, finalizeKernel, Ids.QCur, _q[_qCur]);
            using (ProfilerMarker.AutoScope finalizeScope = _finalizeMarker.Auto())
            using (new ProfilingScope(commands, _guideControlActive
                                                    ? _gpuFinalizeGuidesSampler
                                                    : _gpuFinalizeSampler))
            {
                commands.DispatchCompute(cs, finalizeKernel, finalizeGroups, 1, 1);
            }

            bool measureResidual = ShouldMeasureResidual();
            if (measureResidual)
            {
                using (new ProfilingScope(commands, _gpuResidualResetSampler))
                {
                    commands.DispatchCompute(cs, _kResetResidual, 1, 1, 1);
                }
                commands.SetComputeBufferParam(cs, _kMeasureResidual, Ids.QCur, _q[_qCur]);
                commands.SetComputeIntParam(cs, Ids.XpbdEdgeCount, _constraints.XpbdEdgeCount);
                using (new ProfilingScope(commands, _gpuResidualMeasureSampler))
                {
                    commands.DispatchCompute(cs, _kMeasureResidual, _xpbdEdgeGroups, 1, 1);
                }
            }

            Graphics.ExecuteCommandBuffer(commands);
            PublishSolvedPose();
            if (measureResidual)
            {
                _residualReadback = AsyncGPUReadback.Request(_residual);
                _residualReadbackPending = true;
                _nextResidualReadbackFrame = Time.frameCount + Mathf.Max(1, _residualReadbackInterval);
            }
        }

        private bool ShouldMeasureResidual()
        {
            return _monitorConstraintResidual &&
                   SystemInfo.supportsAsyncGPUReadback &&
                   !_residualReadbackPending &&
                   Time.frameCount >= _nextResidualReadbackFrame;
        }

        private void ApplyCompletedResidualReadback()
        {
            if (!_residualReadbackPending || !_residualReadback.done)
            {
                return;
            }

            _residualReadbackPending = false;
            if (_residualReadback.hasError || !_ready)
            {
                return;
            }

            var data = _residualReadback.GetData<uint>();
            if (data.Length == 0)
            {
                return;
            }

            float residual = BitConverter.Int32BitsToSingle((int)data[0]);
            if (!float.IsNaN(residual) && !float.IsInfinity(residual))
            {
                _constraintResidual = Mathf.Max(0f, residual);
            }
        }

        private void UpdateSimulationBounds()
        {
            using ProfilerMarker.AutoScope scope = _boundsMarker.Auto();
            ApplyCompletedBoundsReadback();
            if (_boundsReadbackPending || !SystemInfo.supportsAsyncGPUReadback ||
                Time.frameCount < _nextBoundsUpdateFrame)
            {
                return;
            }

            _runtimeComputeShader.SetInt(Ids.BoundsGroupCount, _groups);
            using (ProfilerMarker.AutoScope reduceScope = _reduceBoundsMarker.Auto())
            {
                _runtimeComputeShader.Dispatch(_kReduceBounds, _groups, 1, 1);
            }
            using (ProfilerMarker.AutoScope finalizeScope = _finalizeBoundsMarker.Auto())
            {
                _runtimeComputeShader.Dispatch(_kFinalizeBounds, 1, 1, 1);
            }
            _boundsReadback = AsyncGPUReadback.Request(_boundsResult);
#if UNITY_EDITOR
            _boundsReadback.forcePlayerLoopUpdate = true;
#endif
            _boundsReadbackPending = true;
            _nextBoundsUpdateFrame = Time.frameCount + 2;
        }

        private void ApplyCompletedBoundsReadback()
        {
            using ProfilerMarker.AutoScope scope = _boundsReadbackMarker.Auto();
            if (!_boundsReadbackPending || !_boundsReadback.done)
            {
                return;
            }

            _boundsReadbackPending = false;
            if (_boundsReadback.hasError || !_ready || _renderer == null)
            {
                return;
            }

            var data = _boundsReadback.GetData<Vector4>();
            if (data.Length < 2)
            {
                return;
            }

            Vector3 minimum = data[0];
            Vector3 maximum = data[1];
            if (!IsFinite(minimum) || !IsFinite(maximum) ||
                maximum.x < minimum.x || maximum.y < minimum.y || maximum.z < minimum.z)
            {
                return;
            }

            var bounds = new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
            _simulationBounds = bounds;
            _renderer.SetWorldSpaceBuffers(bounds);
        }

        private void MigrateCullingBoundsPadding()
        {
            if (_boundsPadding < 0f)
            {
                return;
            }

            float retired = _boundsPadding;
            _boundsPadding = RetiredCullingBoundsPadding;

            LinePrimitiveRenderer renderer = _renderer != null
                ? _renderer
                : GetComponent<LinePrimitiveRenderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.CullingBoundsPadding = retired;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(renderer);
                UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
            Debug.Log($"[HairLines] Bounds Padding moved from the simulation to the renderer " +
                      $"(Culling Bounds Padding = {retired:0.###} m). Culling belongs to " +
                      $"LinePrimitiveRenderer; the simulation keeps only the scene-collision " +
                      $"field padding. Save the scene to keep this.", renderer);
        }

        private void SetRestBounds()
        {
            if (_renderer == null || _model == null)
            {
                return;
            }

            var bounds = TransformBounds(_model.LocalBounds, transform.localToWorldMatrix);
            _simulationBounds = bounds;
            _renderer.SetWorldSpaceBuffers(bounds);
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private void Release()
        {
            _ready = false;
            _resolvedSettingsFrame = -1;
            _staticUniformFrame = -1;
            _hasUploadedParameters = false;
            _hasUploadedShapes = false;
            _hasBuiltShapes = false;
            _uploadedParameterHash = 0;
            _uploadedShapeHash = 0;
            _shapeInputHash = 0;
            for (int i = 0; i < MaxHairGroups; i++)
            {
            _uploadedShapeCounts[i] = 0u;
            }
            _simulationAccumulator = 0f;
            _boundsReadbackPending = false;
            _residualReadbackPending = false;
            _constraintResidual = 0f;
            _nextResidualReadbackFrame = 0;
            _hasSimulationTransform = false;
            _simulationLodFrame = -1;
            _simulationLodFraction = 1f;
            _simulationBudgetFraction = 1f;
            ResetSimulationLodSmoothing();
            _budgetBuckets.Clear();
            _budgetBucketHash = int.MinValue;
            _lastScheduledIterationCount = 0;
            _lastLegacyIterationCount = 0;
            _model = null;
            _constraints = null;
            _simulationBounds = default;
            ReleaseSceneCollisionField();
            ReleaseRuntimeShaders();
            _simulationCommandBuffer?.Release();
            _simulationCommandBuffer = null;
            if (_sceneCollisionFallback != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_sceneCollisionFallback);
                }
                else
                {
                    DestroyImmediate(_sceneCollisionFallback);
                }
                _sceneCollisionFallback = null;
            }

            _velocity?.Dispose(); _velocity = null;
            _inertia?.Dispose(); _inertia = null;
            _stepStart?.Dispose(); _stepStart = null;
            _qA?.Dispose(); _qA = null;
            _qB?.Dispose(); _qB = null;
            _controlIndices?.Dispose(); _controlIndices = null;
            _interpolationSlotAnchors?.Dispose(); _interpolationSlotAnchors = null;
            _interpolationT?.Dispose(); _interpolationT = null;
            _vertexGroups?.Dispose(); _vertexGroups = null;
            _strandLocalIndex?.Dispose(); _strandLocalIndex = null;
            _simulationParameters?.Dispose(); _simulationParameters = null;
            _strandNeighbours?.Dispose(); _strandNeighbours = null;
            _strandRanges?.Dispose(); _strandRanges = null;
            _strandVertexRanges?.Dispose(); _strandVertexRanges = null;
            _guideVertexRanges?.Dispose(); _guideVertexRanges = null;
            _strandGuideIds?.Dispose(); _strandGuideIds = null;
            _strandGuideWeights?.Dispose(); _strandGuideWeights = null;
            _strandGuideSelf?.Dispose(); _strandGuideSelf = null;
            _vertexArcT?.Dispose(); _vertexArcT = null;
            _fusedStrandBase?.Dispose(); _fusedStrandBase = null;
            _fusedLocalRest?.Dispose(); _fusedLocalRest = null;
            _cosseratFrames?.Dispose(); _cosseratFrames = null;
            _pinSet.ReleaseBuffers();
            CloseCache();
            _cosseratGroupMask = 0;
            _guideControlActive = false;
            _guideCount = 0;
            _fusedLocalActive = false;
            _fusedGroupCount = 0;
            _xpbdEdgeVertices?.Dispose(); _xpbdEdgeVertices = null;
            _xpbdEdgeRest?.Dispose(); _xpbdEdgeRest = null;
            _xpbdEdgeType?.Dispose(); _xpbdEdgeType = null;
            _xpbdIncidentStart?.Dispose(); _xpbdIncidentStart = null;
            _xpbdIncidentEdges?.Dispose(); _xpbdIncidentEdges = null;
            _volume?.Dispose(); _volume = null;
            _builtVolumeHash = 0;
            _xpbdLambda?.Dispose(); _xpbdLambda = null;
            _xpbdDeltaA?.Dispose(); _xpbdDeltaA = null;
            _xpbdDeltaB?.Dispose(); _xpbdDeltaB = null;
            _simulationInfluenceBuffer?.Dispose(); _simulationInfluenceBuffer = null;
            _rootDistanceBuffer?.Dispose(); _rootDistanceBuffer = null;
            _boundsPartials?.Dispose(); _boundsPartials = null;
            _boundsResult?.Dispose(); _boundsResult = null;
            _residual?.Dispose(); _residual = null;
            _q = null;
            _simulationStateCount = 0;
            _simulationStateGroups = 0;
            _controlCount = 0;
            _controlGroups = 0;

            if (_renderer != null)
            {
                _renderer.ClearWorldSpaceBuffers();
                _renderer.Model?.UploadGeometry();
                _renderer.MarkPropertiesDirty();
            }
        }

        private void RefreshResolvedGroupSettings()
        {
            int frame = Time.frameCount;
            if (!_groupCacheDirty && _resolvedSettingsFrame == frame)
            {
                return;
            }

            UpdateGroupSimulationCache();
            bool hasGroups = _renderer != null && _renderer.HasGroups;
            _resolvedGroupCount = hasGroups ? Mathf.Clamp(_renderer.Groups.Count, 0, MaxHairGroups) : 0;

            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                HairGroupSimulation simulation = null;
                if (i < _renderer.Groups.Count && _renderer.Groups[i] != null)
                {
                    HairGroup group = _renderer.Groups[i];
                    for (int component = 0; component < _groupSimulationComponents.Count; component++)
                    {
                        HairGroupSimulation candidate = _groupSimulationComponents[component];
                        if (candidate.GetComponent<HairGroup>() == group)
                        {
                            simulation = candidate;
                            break;
                        }
                    }
                }

                _resolvedGroupEnabled[i] = simulation != null;
                if (_resolvedGroupSettings[i] == null)
                {
                    _resolvedGroupSettings[i] = new HairSimulationSettings();
                }
                if (simulation != null)
                {
                    simulation.CopyResolvedSettingsTo(_resolvedGroupSettings[i]);
                    _resolvedGroupSettings[i].Sanitize();
                }
            }

            for (int i = _resolvedGroupCount; i < MaxHairGroups; i++)
            {
                _resolvedGroupEnabled[i] = false;
            }

            UpdateSimulationLod();
            _resolvedSettingsFrame = frame;
        }

        private void UpdateSimulationLod()
        {
            int frame = Time.frameCount;
            if (_simulationLodFrame == frame)
            {
                return;
            }

            _simulationLodFrame = frame;
            bool canUseRendererLod = _simulationLodEnabled && _renderer != null && _renderer.LodEnabled;
            if (!canUseRendererLod)
            {
                ResetSimulationLodSmoothing();
            }
            else
            {
                Camera lodCamera = Camera.main;
                if (lodCamera != null)
                {
                    float targetFraction = Mathf.Clamp01(
                        _renderer.GetSimulationLodStrandFraction(lodCamera));
                    SmoothSimulationLodFraction(targetFraction);
                }
                else
                {
                    SmoothSimulationLodFraction(1f);
                }
            }

            _simulationLodFraction = Mathf.Clamp01(_simulationLodSmoothedFraction);

            float minimumBudget = Mathf.Clamp(_simulationLodMinimumBudgetFraction, 0.05f, 1f);
            _simulationBudgetFraction = _simulationLodEnabled
                ? Mathf.Lerp(minimumBudget, 1f, _simulationLodFraction)
                : 1f;

            for (int i = 0; i < MaxHairGroups; i++)
            {
                if (i < _resolvedGroupCount && _resolvedGroupEnabled[i])
                {
                    HairSimulationSettings settings = _resolvedGroupSettings[i];
                    _runtimeSubsteps[i] = Mathf.Max(1, settings.Substeps);
                    _runtimeIterations[i] = ScaleRuntimeBudget(settings.Iterations, _simulationBudgetFraction);
                }
                else
                {
                    _runtimeSubsteps[i] = 0;
                    _runtimeIterations[i] = 0;
                }
            }
        }

        private void SmoothSimulationLodFraction(float targetFraction)
        {
            if (float.IsNaN(_simulationLodSmoothedFraction) ||
                float.IsInfinity(_simulationLodSmoothedFraction))
            {
                ResetSimulationLodSmoothing();
            }
            if (float.IsNaN(targetFraction) || float.IsInfinity(targetFraction))
            {
                targetFraction = _hasSimulationLodSample ? _simulationLodSmoothedFraction : 1f;
            }
            targetFraction = Mathf.Clamp01(targetFraction);
            double now = Time.realtimeSinceStartupAsDouble;
            if (!_hasSimulationLodSample)
            {
                _simulationLodSmoothedFraction = 1f;
                _simulationLodSampleTime = now;
                _hasSimulationLodSample = true;
                return;
            }

            float sampleDelta = (float)(now - _simulationLodSampleTime);
            if (float.IsNaN(sampleDelta) || float.IsInfinity(sampleDelta) || sampleDelta <= 0f)
            {
                sampleDelta = 1f / 60f;
            }
            sampleDelta = Mathf.Clamp(sampleDelta, MinSimulationLodSampleDelta,
                                      MaxSimulationLodSampleDelta);
            _simulationLodSampleTime = now;

            float difference = targetFraction - _simulationLodSmoothedFraction;
            if (Mathf.Abs(difference) <= SimulationLodHysteresis)
            {
                return;
            }

            float alpha = 1f - Mathf.Exp(-SimulationLodSmoothingRate * sampleDelta);
            _simulationLodSmoothedFraction = Mathf.Lerp(_simulationLodSmoothedFraction,
                                                        targetFraction, alpha);
        }

        private void ResetSimulationLodSmoothing()
        {
            _simulationLodSmoothedFraction = 1f;
            _simulationLodSampleTime = -1d;
            _hasSimulationLodSample = false;
        }

        private static int ScaleRuntimeBudget(int configured, float fraction)
        {
            configured = Mathf.Max(1, configured);
            return Mathf.Clamp(Mathf.RoundToInt(configured * Mathf.Clamp01(fraction)), 1, configured);
        }

        private void InvalidateSimulationLodCache()
        {
            _simulationLodFrame = -1;
            _resolvedSettingsFrame = -1;
            _staticUniformFrame = -1;
        }

        private bool HasEnabledSimulationGroup()
        {
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (_resolvedGroupEnabled[i])
                {
                    return true;
                }
            }

            return false;
        }

        private int ComputeTopologyHash()
        {
            int hash = 17 + _resolvedGroupCount;
            hash = hash * 31 + ResolveSimulationVertexSkip();
            hash = hash * 31 + (int)_simulationControlMode;
            hash = hash * 31 + ComputePinHash();
            if (_simulationControlMode == HairSimulationControlMode.GuideControl &&
                _guideMapAsset != null)
            {
                ulong mapHash = _guideMapAsset.MapHash;
                hash = hash * 31 + _guideMapAsset.GetInstanceID();
                hash = hash * 31 + (int)(mapHash & 0xffffffffUL);
                hash = hash * 31 + (int)(mapHash >> 32);
                hash = hash * 31 + _guideMapAsset.GuideCount;
                hash = hash * 31 + _guideMapAsset.SchemaVersion;
            }
            int[] skipByGroup = ResolveSimulationVertexSkipByGroup();
            for (int i = 0; i < skipByGroup.Length; i++)
            {
                hash = hash * 31 + skipByGroup[i];
            }
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                hash = hash * 31 + (_resolvedGroupEnabled[i] ? 1 : 0);
                if (!_resolvedGroupEnabled[i])
                {
                    continue;
                }
                HairSimulationSettings settings = _resolvedGroupSettings[i];
                hash = hash * 31 + settings.PinnedRootVertices;
                hash = hash * 31 + (settings.BendingSprings ? 1 : 0);
            }
            return hash;
        }

        private static bool SameConstraintGraphTransform(Matrix4x4 first, Matrix4x4 second)
        {
            Vector3 firstX = new Vector3(first.m00, first.m10, first.m20);
            Vector3 firstY = new Vector3(first.m01, first.m11, first.m21);
            Vector3 firstZ = new Vector3(first.m02, first.m12, first.m22);
            Vector3 secondX = new Vector3(second.m00, second.m10, second.m20);
            Vector3 secondY = new Vector3(second.m01, second.m11, second.m21);
            Vector3 secondZ = new Vector3(second.m02, second.m12, second.m22);

            return SameConstraintMetric(Vector3.Dot(firstX, firstX), Vector3.Dot(secondX, secondX)) &&
                   SameConstraintMetric(Vector3.Dot(firstY, firstY), Vector3.Dot(secondY, secondY)) &&
                   SameConstraintMetric(Vector3.Dot(firstZ, firstZ), Vector3.Dot(secondZ, secondZ)) &&
                   SameConstraintMetric(Vector3.Dot(firstX, firstY), Vector3.Dot(secondX, secondY)) &&
                   SameConstraintMetric(Vector3.Dot(firstX, firstZ), Vector3.Dot(secondX, secondZ)) &&
                   SameConstraintMetric(Vector3.Dot(firstY, firstZ), Vector3.Dot(secondY, secondZ));
        }

        private static bool SameConstraintMetric(float first, float second)
        {
            float tolerance = 1e-6f * Mathf.Max(1f, Mathf.Max(Mathf.Abs(first), Mathf.Abs(second)));
            return Mathf.Abs(first - second) <= tolerance;
        }

        private int ResolveSimulationVertexSkip()
        {
            int skip = 4;
            bool found = false;
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (_resolvedGroupEnabled[i])
                {
                    skip = Mathf.Min(skip, _resolvedGroupSettings[i].SimulationVertexSkip);
                    found = true;
                }
            }
            return Mathf.Clamp(found ? skip : 1, 0, 4);
        }

        private int[] ResolveSimulationVertexSkipByGroup()
        {
            var result = new int[MaxHairGroups];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = 4;
            }

            for (int i = 0; i < _resolvedGroupCount && i < result.Length; i++)
            {
                if (_resolvedGroupEnabled[i] && _resolvedGroupSettings[i] != null)
                {
                    result[i] = Mathf.Clamp(_resolvedGroupSettings[i].SimulationVertexSkip, 0, 4);
                }
            }

            return result;
        }

        private int ComputeInfluenceHash()
        {
            int hash = 17;
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (!_resolvedGroupEnabled[i])
                {
                    continue;
                }
                AnimationCurve curve = _resolvedGroupSettings[i].SimulationInfluence;
                if (curve == null)
                {
                    hash = hash * 31;
                    continue;
                }

                int keyCount = curve.length;
                hash = hash * 31 + keyCount;
                for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
                {
                    Keyframe key = curve[keyIndex];
                    hash = hash * 31 + key.time.GetHashCode();
                    hash = hash * 31 + key.value.GetHashCode();
                    hash = hash * 31 + key.inTangent.GetHashCode();
                    hash = hash * 31 + key.outTangent.GetHashCode();
                }
            }
            return hash;
        }

        private bool HasEnabledXpbdGroup()
        {
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (!_resolvedGroupEnabled[i])
                {
                    continue;
                }

                return true;
            }
            return false;
        }

        private bool IsVolumeRequested()
        {
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (_resolvedGroupEnabled[i] &&
                    (_resolvedGroupSettings[i].VolumeVelocity > 0f ||
                     _resolvedGroupSettings[i].VolumePressure > 0f))
                {
                    return true;
                }
            }

            return false;
        }

        private int ComputeVolumeHash()
        {
            int hash = 17;
            hash = hash * 31 + (IsVolumeRequested() ? 1 : 0);
            hash = hash * 31 + _volumeResolution;
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                hash = hash * 31 + (_resolvedGroupEnabled[i] ? _resolvedGroupSettings[i].PinnedRootVertices : 0);
            }

            return hash;
        }

        private void AllocateVolume(LinePrimitiveModel model)
        {
            var pinnedByGroup = new int[MaxHairGroups];
            for (int i = 0; i < MaxHairGroups; i++)
            {
                pinnedByGroup[i] = i < _resolvedGroupCount && _resolvedGroupSettings[i] != null
                    ? _resolvedGroupSettings[i].PinnedRootVertices
                    : 1;
            }

            _volume ??= new HairSimulationVolume();
            _volume.Allocate(IsVolumeRequested(), _volumeResolution, model, _constraints, pinnedByGroup);
            _builtVolumeHash = ComputeVolumeHash();
        }

        private int ComputeCosseratGroupMask()
        {
            int mask = 0;
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (_resolvedGroupEnabled[i] &&
                    _resolvedGroupSettings[i].StrandModel == HairStrandModel.CosseratRods)
                {
                    mask |= 1 << i;
                }
            }

            return mask;
        }

        private int ComputeComplianceHash()
        {
            int hash = 17 + _resolvedGroupCount;
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                hash = hash * 31 + (_resolvedGroupEnabled[i] ? 1 : 0);
                if (!_resolvedGroupEnabled[i])
                {
                    continue;
                }

                HairSimulationSettings settings = _resolvedGroupSettings[i];
                hash = hash * 31 + settings.StretchCompliance.GetHashCode();
                hash = hash * 31 + settings.BendCompliance.GetHashCode();
            }
            return hash;
        }

        private uint[] BuildVertexGroups(LinePrimitiveModel model)
        {
            uint[] groups = model.VertexGroups;
            if (groups != null && groups.Length == model.VertexCount)
            {
                return groups;
            }

            return new uint[model.VertexCount];
        }

        private bool RefreshBudgetBuckets()
        {
            int hash = HairSimulationBudgetScheduler.ComputeHash(
                _resolvedGroupCount, _resolvedGroupEnabled, _resolvedGroupSettings,
                _runtimeSubsteps, _runtimeIterations);
            if (hash != _budgetBucketHash)
            {
                HairSimulationBudgetScheduler.Build(
                    _resolvedGroupCount, _resolvedGroupEnabled, _resolvedGroupSettings,
                    _runtimeSubsteps, _runtimeIterations, _budgetBuckets);
                _budgetBucketHash = hash;
            }

            return _budgetBuckets.Count > 0;
        }

        private int ResolveVertexGroup(LinePrimitiveModel model, int vertex)
        {
            if (model.VertexGroups == null || vertex < 0 || vertex >= model.VertexGroups.Length)
            {
                return 0;
            }

            return Mathf.Clamp((int)model.VertexGroups[vertex], 0, Mathf.Max(0, _resolvedGroupCount - 1));
        }

        private int ComputeGpuParameterHash()
        {
            int hash = 17;
            hash = hash * 31 + _resolvedGroupCount;
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                hash = hash * 31 + (_resolvedGroupEnabled[i] ? 1 : 0);
                if (!_resolvedGroupEnabled[i])
                {
                    continue;
                }

                HairSimulationSettings settings = _resolvedGroupSettings[i];
                hash = hash * 31 + (settings.UseUnityWind ? 1 : 0);
                hash = hash * 31 + settings.Gravity.GetHashCode();
                hash = hash * 31 + settings.Wind.GetHashCode();
                hash = hash * 31 + settings.WindTurbulence.GetHashCode();
                hash = hash * 31 + settings.StretchCompliance.GetHashCode();
                hash = hash * 31 + settings.BendCompliance.GetHashCode();
                hash = hash * 31 + settings.Damping.GetHashCode();
                hash = hash * 31 + settings.StrainLimit.GetHashCode();
                hash = hash * 31 + settings.CollisionOffset.GetHashCode();
                hash = hash * 31 + settings.CollisionRootHold.GetHashCode();
                hash = hash * 31 + settings.PinnedRootVertices;
                hash = hash * 31 + (settings.BendingSprings ? 1 : 0);
                hash = hash * 31 + (int)settings.StrandModel;
                hash = hash * 31 + settings.VolumeVelocity.GetHashCode();
                hash = hash * 31 + settings.VolumePressure.GetHashCode();
                hash = hash * 31 + (settings.CollideWithSceneGeometry ? 1 : 0);
                hash = hash * 31 + settings.Substeps;
                hash = hash * 31 + settings.Iterations;
                hash = hash * 31 + Mathf.Max(1, _runtimeSubsteps[i]);
                hash = hash * 31 + Mathf.Max(1, _runtimeIterations[i]);
            }
            return hash;
        }

        private bool HasUnityWindGroups()
        {
            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (_resolvedGroupEnabled[i] && _resolvedGroupSettings[i].UseUnityWind)
                {
                    return true;
                }
            }

            return false;
        }

        private static WindZone[] GetCachedWindZones()
        {
            int frame = Time.frameCount;
            if (_windZoneCacheFrame >= 0 && frame >= _windZoneCacheFrame &&
                frame - _windZoneCacheFrame < 30)
            {
                return _cachedWindZones;
            }

            _windZoneCacheFrame = frame;
            _cachedWindZones = UnityEngine.Object.FindObjectsByType<WindZone>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            return _cachedWindZones;
        }

        private Vector3 SampleUnityWind(out float turbulence)
        {
            Vector3 force = Vector3.zero;
            float weightedTurbulence = 0f;
            float totalWeight = 0f;
            WindZone[] zones = GetCachedWindZones();
            float time = Application.isPlaying
                ? Time.time
                : (float)Time.realtimeSinceStartupAsDouble;
            Vector3 samplePosition = transform.position;

            for (int i = 0; i < zones.Length; i++)
            {
                WindZone zone = zones[i];
                if (zone == null || !zone.gameObject.activeInHierarchy)
                {
                    continue;
                }

                float weight = 1f;
                if (zone.mode == WindZoneMode.Spherical)
                {
                    float radius = Mathf.Max(0.0001f, zone.radius);
                    weight = 1f - Mathf.Clamp01(
                        Vector3.Distance(samplePosition, zone.transform.position) / radius);
                }

                if (weight <= 0f)
                {
                    continue;
                }

                float pulse = 1f;
                if (zone.windPulseMagnitude != 0f && zone.windPulseFrequency > 0f)
                {
                    float phase = zone.GetInstanceID() * 0.173f;
                    pulse += zone.windPulseMagnitude *
                             Mathf.Sin((time * zone.windPulseFrequency + phase) * Mathf.PI * 2f);
                }

                Vector3 direction = zone.mode == WindZoneMode.Spherical
                    ? (samplePosition - zone.transform.position).normalized
                    : zone.transform.forward;
                force += direction * (zone.windMain * 9.81f * pulse * weight);
                weightedTurbulence += Mathf.Max(0f, zone.windTurbulence) * weight;
                totalWeight += weight;
            }

            turbulence = totalWeight > 1e-6f
                ? Mathf.Clamp(weightedTurbulence / totalWeight, 0f, 3f)
                : -1f;
            if (force.sqrMagnitude <= 1e-8f)
            {
                turbulence = -1f;
            }

            return force;
        }

        private int ComputeBuiltShapeHash()
        {
            int hash = 17;
            for (int i = 0; i < MaxTotalShapes; i++)
            {
                hash = hash * 31 + _shapeA[i].GetHashCode();
                hash = hash * 31 + _shapeB[i].GetHashCode();
                hash = hash * 31 + _shapeRot[i].GetHashCode();
            }
            return hash;
        }

        private int ComputeShapeInputHash()
        {
            int hash = 17;
            for (int group = 0; group < MaxHairGroups; group++)
            {
                bool enabled = group < _resolvedGroupCount && _resolvedGroupEnabled[group];
                hash = hash * 31 + (enabled ? 1 : 0);
                HairSimulationSettings settings = group < _resolvedGroupCount
                    ? _resolvedGroupSettings[group]
                    : _resolvedGroupSettings[0];
                hash = hash * 31 + settings.CollisionOffset.GetHashCode();
                hash = hash * 31 + settings.CollisionRootHold.GetHashCode();
                CollisionShape[] colliders = settings.Colliders;
                int count = colliders != null && UsesColliders(settings) ? colliders.Length : 0;
                hash = hash * 31 + count;
                for (int colliderIndex = 0; colliderIndex < count; colliderIndex++)
                {
                    CollisionShape shape = colliders[colliderIndex];
                    Transform colliderTransform = shape.transform;
                    hash = hash * 31 + shape.padding.GetHashCode();
                    hash = hash * 31 + (colliderTransform != null ? colliderTransform.GetInstanceID() : 0);
                    if (colliderTransform != null)
                    {
                        hash = hash * 31 + colliderTransform.localToWorldMatrix.GetHashCode();
                    }
                }

                BoneCapsule[] capsules = settings.BoneCapsules;
                int capsuleCount = capsules != null && UsesColliders(settings) ? capsules.Length : 0;
                hash = hash * 31 + capsuleCount;
                for (int capsuleIndex = 0; capsuleIndex < capsuleCount; capsuleIndex++)
                {
                    BoneCapsule capsule = capsules[capsuleIndex];
                    hash = hash * 31 + capsule.start.GetHashCode();
                    hash = hash * 31 + capsule.end.GetHashCode();
                    hash = hash * 31 + capsule.radius.GetHashCode();
                    hash = hash * 31 + (capsule.bone != null ? capsule.bone.localToWorldMatrix.GetHashCode() : 0);
                }
            }
            return hash;
        }

        private bool HaveShapeCountsChanged()
        {
            for (int i = 0; i < MaxHairGroups; i++)
            {
                if (_gpuParameters[i].ShapeCount != _uploadedShapeCounts[i])
                {
                    return true;
                }
            }
            return false;
        }

        private void StoreUploadedShapeCounts()
        {
            for (int i = 0; i < MaxHairGroups; i++)
            {
                _uploadedShapeCounts[i] = _gpuParameters[i].ShapeCount;
            }
        }

        private int GetRuntimeSubsteps(int group, HairSimulationSettings settings)
        {
            if (group >= 0 && group < _resolvedGroupCount && _resolvedGroupEnabled[group])
            {
                return Mathf.Max(1, _runtimeSubsteps[group]);
            }

            return Mathf.Max(1, settings.Substeps);
        }

        private int GetRuntimeIterations(int group, HairSimulationSettings settings)
        {
            if (group >= 0 && group < _resolvedGroupCount && _resolvedGroupEnabled[group])
            {
                return Mathf.Max(1, _runtimeIterations[group]);
            }

            return Mathf.Max(1, settings.Iterations);
        }

        private void BuildGpuParameters()
        {
            using ProfilerMarker.AutoScope scope = _buildParametersMarker.Auto();
            for (int i = 0; i < MaxHairGroups; i++)
            {
                HairSimulationSettings settings = i < _resolvedGroupCount
                    ? _resolvedGroupSettings[i]
                    : _resolvedGroupSettings[0];
                if (!_resolvedGroupEnabled[i] && i < _resolvedGroupCount)
                {
                    settings = _resolvedGroupSettings[0];
                }
                Vector3 wind = Vector3.zero;
                float turbulence = -1f;
                if (settings.UseUnityWind)
                {
                    wind = _sampledUnityWind;
                    turbulence = _sampledUnityWindTurbulence;
                }
                int shapeOffset = i * MaxShapesPerGroup;
                _gpuParameters[i] = new HairSimulationGpuParameters
                {
                    Gravity = new Vector4(settings.Gravity.x, settings.Gravity.y, settings.Gravity.z, 0f),
                    Wind = new Vector4(wind.x, wind.y, wind.z, turbulence),
                    Compliance = new Vector4(settings.StretchCompliance, settings.BendCompliance,
                                              0f, 0f),
                    Solver = new Vector4(settings.Damping, settings.VolumeVelocity,
                                         settings.VolumePressure, 1f + settings.StrainLimit),
                    Collision = new Vector4(settings.CollisionOffset, 0f,
                                            settings.CollisionRootHold, 0f),
                    PinnedRootVertices = (uint)settings.PinnedRootVertices,
                    Flags = (settings.BendingSprings ? 1u : 0u) |
                            (_resolvedGroupEnabled[i] ? 4u : 0u) |
                            (settings.CollideWithSceneGeometry ? 8u : 0u) |
                            (settings.StrandModel == HairStrandModel.CosseratRods ? 16u : 0u),
                    ShapeOffset = (uint)shapeOffset,
                    ShapeCount = _gpuParameters[i].ShapeCount,
                    Substeps = (uint)GetRuntimeSubsteps(i, settings),
                    Iterations = (uint)GetRuntimeIterations(i, settings),
                };
            }
        }

        private int BuildCollisionShapes()
        {
            using ProfilerMarker.AutoScope scope = _buildShapesMarker.Auto();
            for (int i = 0; i < MaxTotalShapes; i++)
            {
                _shapeA[i] = Vector4.zero;
                _shapeB[i] = Vector4.zero;
                _shapeRot[i] = new Vector4(0f, 0f, 0f, 1f);
            }

            int total = 0;
            for (int group = 0; group < MaxHairGroups; group++)
            {
                HairSimulationSettings settings = group < _resolvedGroupCount
                    ? _resolvedGroupSettings[group]
                    : _resolvedGroupSettings[0];
                int offset = group * MaxShapesPerGroup;
                int count = 0;
                if (settings.Colliders != null && UsesColliders(settings))
                {
                    foreach (CollisionShape collider in settings.Colliders)
                    {
                        if (count >= MaxShapesPerGroup)
                        {
                            break;
                        }
                        ResolvedShape resolved = Resolve(collider, settings.CollisionOffset);
                        if (resolved.valid)
                        {
                            Store(offset + count++, resolved);
                        }
                        else
                        {
                            WarnUnsupportedCollider(collider, resolved);
                        }
                    }
                }

                if (settings.BoneCapsules != null && UsesColliders(settings))
                {
                    foreach (BoneCapsule capsule in settings.BoneCapsules)
                    {
                        if (count >= MaxShapesPerGroup)
                        {
                            break;
                        }

                        ResolvedShape resolved = Resolve(capsule, settings.CollisionOffset);
                        if (resolved.valid)
                        {
                            Store(offset + count++, resolved);
                        }
                    }
                }

                _gpuParameters[group].ShapeCount = (uint)count;
                total = Mathf.Max(total, offset + count);
            }

            return total;
        }

        private void WarnUnsupportedCollider(in CollisionShape shape, in ResolvedShape resolved)
        {
            Transform colliderTransform = shape.transform;
            int warningId = colliderTransform != null ? colliderTransform.GetInstanceID() : 0;
            if (!_invalidColliderWarnings.Add(warningId))
            {
                return;
            }

            if (colliderTransform == null)
            {
                Debug.LogWarning("[HairLines] A null collision transform was skipped.", this);
                return;
            }

            Collider collider = colliderTransform.GetComponent<Collider>();
            string colliderType = collider != null ? collider.GetType().Name : "no Collider component";
            string reason = resolved.unsupportedCollider ? "unsupported collider type" : "invalid collider reference";
            Debug.LogWarning($"[HairLines] Collision transform '{colliderTransform.name}' ({colliderType}) was skipped: {reason}. Supported types are SphereCollider, CapsuleCollider, and BoxCollider.", this);
        }

        private static float[] BuildRootDistance(LinePrimitiveModel model)
        {
            var values = new float[model.VertexCount];

            if (model.Strands == null)
            {
                return values;
            }

            for (int strand = 0; strand < model.Strands.Length; strand++)
            {
                int start = model.Strands[strand].x;
                int count = model.Strands[strand].y;
                values[start] = 0f;

                for (int vertex = 1; vertex < count; vertex++)
                {
                    values[start + vertex] = values[start + vertex - 1] +
                        Vector3.Distance(model.Positions[start + vertex - 1], model.Positions[start + vertex]);
                }
            }

            return values;
        }

        private float[] BuildSimulationInfluence(LinePrimitiveModel model)
        {
            int vertexCount = model.VertexCount;
            var values = new float[vertexCount];
            bool hasNonUnitInfluence = false;

            for (int i = 0; i < vertexCount; i++)
            {
                values[i] = 1f;
            }

            if (model.Strands == null)
            {
                _hasNonUnitSimulationInfluence = false;
                return values;
            }

            for (int strand = 0; strand < model.Strands.Length; strand++)
            {
                int start = model.Strands[strand].x;
                int count = model.Strands[strand].y;
                int denominator = Mathf.Max(1, count - 1);
                int group = ResolveVertexGroup(model, start);
                AnimationCurve curve = _resolvedGroupSettings[group].SimulationInfluence;

                for (int vertex = 0; vertex < count; vertex++)
                {
                    float along = vertex / (float)denominator;
                    float influence = curve == null || curve.length == 0
                        ? 1f
                        : Mathf.Clamp01(curve.Evaluate(along));
                    values[start + vertex] = influence;
                    hasNonUnitInfluence |= influence < 0.999999f;
                }
            }

            _hasNonUnitSimulationInfluence = hasNonUnitInfluence;
            return values;
        }

        private static Bounds TransformBounds(Bounds local, Matrix4x4 m)
        {
            Vector3 centre = m.MultiplyPoint3x4(local.center);
            Vector3 e = local.extents;
            Vector3 extents = new Vector3(
                Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
                Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
                Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
            return new Bounds(centre, extents * 2f);
        }

        private static ComputeShader FindComputeShader()
        {
#if UNITY_EDITOR
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("LineHairSimulation t:ComputeShader"))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var cs = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                if (cs != null)
                {
                    return cs;
                }
            }
#endif
            return null;
        }

        private static ComputeShader FindSceneCollisionShader()
        {
            ComputeShader shader = Resources.Load<ComputeShader>("HairSceneCollision");
#if UNITY_EDITOR
            if (shader == null)
            {
                foreach (string guid in UnityEditor.AssetDatabase.FindAssets("HairSceneCollision t:ComputeShader"))
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    if (shader != null)
                    {
                        break;
                    }
                }
            }
#endif
            return shader;
        }

        private static ComputeShader FindSceneSplatShader()
        {
            ComputeShader shader = Resources.Load<ComputeShader>("HairSceneSplat");
#if UNITY_EDITOR
            if (shader == null)
            {
                foreach (string guid in UnityEditor.AssetDatabase.FindAssets("HairSceneSplat t:ComputeShader"))
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    if (shader != null)
                    {
                        break;
                    }
                }
            }
#endif
            return shader;
        }

        private static ComputeShader CreateRuntimeShader(ComputeShader source, string purpose)
        {
            ComputeShader shader = Instantiate(source);
            shader.name = $"{source.name} ({purpose} Instance)";
            shader.hideFlags = HideFlags.HideAndDontSave;
            return shader;
        }

        private void EnsureRuntimeSceneCollisionShader(ComputeShader source)
        {
            if (_runtimeSceneCollisionShader != null &&
                _runtimeSceneCollisionShaderSource == source)
            {
                return;
            }

            ReleaseSceneCollisionField();
            DestroyRuntimeShader(ref _runtimeSceneCollisionShader);
            _runtimeSceneCollisionShader = CreateRuntimeShader(source, "Scene Collision");
            _runtimeSceneCollisionShaderSource = source;
        }

        private void ReleaseRuntimeShaders()
        {
            DestroyRuntimeShader(ref _runtimeComputeShader);
            DestroyRuntimeShader(ref _runtimeSceneCollisionShader);
            _runtimeComputeShaderSource = null;
            _runtimeSceneCollisionShaderSource = null;
        }

        private static void DestroyRuntimeShader(ref ComputeShader shader)
        {
            if (shader == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(shader);
            }
            else
            {
                DestroyImmediate(shader);
            }
            shader = null;
        }

        private void OnDisable()
        {
            Release();
        }

        private void OnDestroy()
        {
            Release();
            _constraintGraphCache = null;
            _constraintGraphCacheModel = null;
            _constraintGraphCacheHash = 0;
            _constraintGraphCacheXpbd = false;
            _constraintGraphCacheTransform = default;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            _computeShader = FindComputeShader();
            _sceneCollisionShader = FindSceneCollisionShader();
            _sceneSplatShader = FindSceneSplatShader();
        }

        private void OnValidate()
        {
            _resolvedSettingsFrame = -1;
            _staticUniformFrame = -1;
            _hasBuiltShapes = false;
            MigrateCullingBoundsPadding();
            _sceneCollisionResolution = Mathf.Clamp(_sceneCollisionResolution, 16, 128);
            _sceneCollisionBoundsPadding = Mathf.Max(0f, _sceneCollisionBoundsPadding);
            _simulationLodMinimumBudgetFraction = Mathf.Clamp(_simulationLodMinimumBudgetFraction, 0.05f, 1f);
            _simulationLodMaxFrameInterval = Mathf.Clamp(_simulationLodMaxFrameInterval, 1, 8);
            InvalidateSimulationLodCache();
            _residualReadbackInterval = Mathf.Max(1, _residualReadbackInterval);
            _residualWarningThreshold = Mathf.Max(0f, _residualWarningThreshold);
            _teleportDistance = Mathf.Max(0f, _teleportDistance);
            _teleportAngle = Mathf.Clamp(_teleportAngle, 0f, 180f);
            _teleportScaleRatio = Mathf.Clamp01(_teleportScaleRatio);
            if (_ready && _simulationInfluenceBuffer != null && _model != null)
            {
                RefreshResolvedGroupSettings();
                _simulationInfluenceBuffer.SetData(BuildSimulationInfluence(_model));
                _builtInfluenceHash = ComputeInfluenceHash();
            }
        }

        private void OnDrawGizmosSelected()
        {
            LinePrimitiveRenderer renderer = _renderer != null ? _renderer : GetComponent<LinePrimitiveRenderer>();

            bool drawSceneCollisionBounds = _sceneCollisionField?.IsValid ?? false;

            if (!drawSceneCollisionBounds && renderer != null && renderer.HasGroups)
            {
                RefreshResolvedGroupSettings();
                drawSceneCollisionBounds = AnySceneCollisionEnabled();
            }

            if (drawSceneCollisionBounds)
            {
                Bounds simulationBounds = _simulationBounds;
                if (simulationBounds.size.sqrMagnitude <= 1e-10f && renderer != null)
                {
                    simulationBounds = renderer.WorldBounds;
                }

                if (simulationBounds.size.sqrMagnitude > 1e-10f)
                {
                    simulationBounds.Expand(_sceneCollisionBoundsPadding * 2f);
                    Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.8f);
                    Gizmos.DrawWireCube(simulationBounds.center, simulationBounds.size);
                }
            }

            Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.6f);

            for (int i = 0; i < _resolvedGroupCount; i++)
            {
                if (!_resolvedGroupEnabled[i])
                {
                    continue;
                }

                HairSimulationSettings settings = _resolvedGroupSettings[i];
                if (!UsesColliders(settings))
                {
                    continue;
                }

                int drawn = 0;
                foreach (CollisionShape collider in settings.Colliders)
                {
                    ResolvedShape shape = Resolve(collider, settings.CollisionOffset);
                    if (shape.valid && drawn < MaxShapesPerGroup)
                    {
                        DrawShapeGizmo(shape);
                        drawn++;
                    }
                }

                foreach (BoneCapsule capsule in settings.BoneCapsules)
                {
                    ResolvedShape shape = Resolve(capsule, settings.CollisionOffset);
                    if (shape.valid && drawn < MaxShapesPerGroup)
                    {
                        DrawShapeGizmo(shape);
                        drawn++;
                    }
                }
            }
        }

        private static void DrawShapeGizmo(in ResolvedShape shape)
        {
            switch (shape.kind)
            {
                case ShapeKind.Sphere:
                    Gizmos.DrawWireSphere(shape.a, shape.radius);
                    break;

                case ShapeKind.Capsule:
                {
                    Gizmos.DrawWireSphere(shape.a, shape.radius);
                    Gizmos.DrawWireSphere(shape.b, shape.radius);

                    Vector3 axis = shape.b - shape.a;
                    if (axis.sqrMagnitude < 1e-10f)
                    {
                        break;
                    }
                    axis.Normalize();
                    Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                    Vector3 v = Vector3.Cross(axis, u);
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 offset = (i switch
                        {
                            0 => u,
                            1 => -u,
                            2 => v,
                            _ => -v,
                        }) * shape.radius;
                        Gizmos.DrawLine(shape.a + offset, shape.b + offset);
                    }
                    break;
                }

                case ShapeKind.Box:
                {
                    Matrix4x4 previous = Gizmos.matrix;
                    Gizmos.matrix = Matrix4x4.TRS(shape.a, shape.rotation, Vector3.one);
                    Gizmos.DrawWireCube(Vector3.zero, (shape.b + Vector3.one * shape.radius) * 2f);
                    Gizmos.matrix = previous;
                    break;
                }
            }
        }
#endif

        private sealed class HairSimulationBudgetBucket
        {
            internal int Substeps;
            internal int Iterations;
            internal bool Enabled;
            internal readonly List<int> Groups = new List<int>(2);

            internal bool IsActiveAt(int substep, int iteration)
            {
                return Enabled && substep < Substeps && iteration < Iterations;
            }
        }

        private static class HairSimulationBudgetScheduler
        {
            internal static int Build(
                int groupCount,
                bool[] enabled,
                HairSimulationSettings[] settings,
                int[] runtimeSubsteps,
                int[] runtimeIterations,
                List<HairSimulationBudgetBucket> output)
            {
                output.Clear();
                int count = Mathf.Clamp(groupCount, 0, enabled?.Length ?? 0);
                for (int group = 0; group < count; group++)
                {
                    HairSimulationSettings resolved = settings != null && group < settings.Length
                        ? settings[group]
                        : null;
                    bool groupEnabled = enabled[group];
                    int substeps = groupEnabled && runtimeSubsteps != null && group < runtimeSubsteps.Length
                        ? Mathf.Max(1, runtimeSubsteps[group])
                        : 0;
                    int iterations = groupEnabled && runtimeIterations != null && group < runtimeIterations.Length
                        ? Mathf.Max(1, runtimeIterations[group])
                        : 0;

                    HairSimulationBudgetBucket bucket = null;
                    for (int index = 0; index < output.Count; index++)
                    {
                        HairSimulationBudgetBucket candidate = output[index];
                        if (candidate.Substeps == substeps &&
                            candidate.Iterations == iterations &&
                            candidate.Enabled == groupEnabled)
                        {
                            bucket = candidate;
                            break;
                        }
                    }

                    if (bucket == null)
                    {
                        bucket = new HairSimulationBudgetBucket
                        {
                            Substeps = substeps,
                            Iterations = iterations,
                            Enabled = groupEnabled,
                        };
                        output.Add(bucket);
                    }

                    bucket.Groups.Add(group);
                }

                return output.Count;
            }

            internal static int ComputeHash(
                int groupCount,
                bool[] enabled,
                HairSimulationSettings[] settings,
                int[] runtimeSubsteps,
                int[] runtimeIterations)
            {
                int hash = 17;
                int count = Mathf.Clamp(groupCount, 0, enabled?.Length ?? 0);
                hash = hash * 31 + count;
                for (int group = 0; group < count; group++)
                {
                    bool groupEnabled = enabled[group];
                    int substeps = groupEnabled && runtimeSubsteps != null && group < runtimeSubsteps.Length
                        ? Mathf.Max(1, runtimeSubsteps[group])
                        : 0;
                    int iterations = groupEnabled && runtimeIterations != null && group < runtimeIterations.Length
                        ? Mathf.Max(1, runtimeIterations[group])
                        : 0;
                    hash = hash * 31 + (groupEnabled ? 1 : 0);
                    hash = hash * 31 + substeps;
                    hash = hash * 31 + iterations;
                }
                return hash;
            }

            internal static int GetMaxSubsteps(List<HairSimulationBudgetBucket> buckets)
            {
                int maximum = 0;
                for (int index = 0; index < buckets.Count; index++)
                {
                    HairSimulationBudgetBucket bucket = buckets[index];
                    if (bucket.Enabled)
                    {
                        maximum = Mathf.Max(maximum, bucket.Substeps);
                    }
                }
                return maximum;
            }

            internal static int GetIterationsForSubstep(
                List<HairSimulationBudgetBucket> buckets, int substep)
            {
                int maximum = 0;
                for (int index = 0; index < buckets.Count; index++)
                {
                    HairSimulationBudgetBucket bucket = buckets[index];
                    if (bucket.Enabled && substep < bucket.Substeps)
                    {
                        maximum = Mathf.Max(maximum, bucket.Iterations);
                    }
                }
                return maximum;
            }

            internal static bool IsSolverActive(
                List<HairSimulationBudgetBucket> buckets, int substep, int iteration)
            {
                for (int index = 0; index < buckets.Count; index++)
                {
                    HairSimulationBudgetBucket bucket = buckets[index];
                    if (bucket.IsActiveAt(substep, iteration))
                    {
                        return true;
                    }
                }
                return false;
            }

            internal static bool HasDisabledGroups(List<HairSimulationBudgetBucket> buckets)
            {
                for (int index = 0; index < buckets.Count; index++)
                {
                    if (!buckets[index].Enabled)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public enum ShapeKind
        {
            Sphere = 0,
            Capsule = 1,
            Box = 2,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HairSimulationGpuParameters
        {
            internal Vector4 Gravity;
            internal Vector4 Wind;
            internal Vector4 Compliance;
            internal Vector4 Solver;
            internal Vector4 Collision;
            internal uint PinnedRootVertices;
            internal uint Reserved;
            internal uint Flags;
            internal uint ShapeOffset;
            internal uint ShapeCount;
            internal uint Substeps;
            internal uint Iterations;
        }

        [System.Serializable]
        public struct CollisionShape
        {
            public Transform transform;
            [Tooltip("Extra clearance in metres, so strands sit slightly off the surface.")]
            public float padding;
        }

        [System.Serializable]
        public struct BoneCapsule
        {
            public Transform bone;
            [Tooltip("Capsule axis start in the bone's local space.")]
            public Vector3 start;
            [Tooltip("Capsule axis end in the bone's local space.")]
            public Vector3 end;
            [Tooltip("Radius in the bone's local units.")]
            public float radius;
        }

        public struct ResolvedShape
        {
            public ShapeKind kind;
            public Vector3 a;
            public Vector3 b;
            public Quaternion rotation;
            public float radius;
            public bool valid;
            public bool unsupportedCollider;
        }

        private static class Ids
        {
            public static readonly int X = Shader.PropertyToID("_X");
            public static readonly int V = Shader.PropertyToID("_V");
            public static readonly int S = Shader.PropertyToID("_S");
            public static readonly int XStart = Shader.PropertyToID("_XStart");
            public static readonly int QCur = Shader.PropertyToID("_QCur");
            public static readonly int QNext = Shader.PropertyToID("_QNext");
            public static readonly int RestLocal = Shader.PropertyToID("_RestLocal");
            public static readonly int XpbdEdgeVertices = Shader.PropertyToID("_XpbdEdgeVertices");
            public static readonly int XpbdEdgeRest = Shader.PropertyToID("_XpbdEdgeRest");
            public static readonly int XpbdEdgeType = Shader.PropertyToID("_XpbdEdgeType");
            public static readonly int XpbdIncidentStart = Shader.PropertyToID("_XpbdIncidentStart");
            public static readonly int XpbdIncidentEdges = Shader.PropertyToID("_XpbdIncidentEdges");
            public static readonly int XpbdLambda = Shader.PropertyToID("_XpbdLambda");
            public static readonly int XpbdDeltaA = Shader.PropertyToID("_XpbdDeltaA");
            public static readonly int XpbdDeltaB = Shader.PropertyToID("_XpbdDeltaB");
            public static readonly int XpbdEdgeCount = Shader.PropertyToID("_XpbdEdgeCount");
            public static readonly int XpbdEdgeOffset = Shader.PropertyToID("_XpbdEdgeOffset");
            public static readonly int VertexGroups = Shader.PropertyToID("_VertexGroups");
            public static readonly int StrandLocalIndex = Shader.PropertyToID("_StrandLocalIndex");
            public static readonly int ControlIndices = Shader.PropertyToID("_ControlIndices");
            public static readonly int ControlCount = Shader.PropertyToID("_ControlCount");
            public static readonly int StateCount = Shader.PropertyToID("_StateCount");
            public static readonly int InterpolationSlotAnchors = Shader.PropertyToID("_InterpolationSlotAnchors");
            public static readonly int InterpolationT = Shader.PropertyToID("_InterpolationT");
            public static readonly int SimulationParameters = Shader.PropertyToID("_SimulationParameters");
            public static readonly int StrandNeighbours = Shader.PropertyToID("_StrandNeighbours");
            public static readonly int StrandRanges = Shader.PropertyToID("_StrandRanges");
            public static readonly int StrandVertexRanges = Shader.PropertyToID("_StrandVertexRanges");
            public static readonly int GuideVertexRanges = Shader.PropertyToID("_GuideVertexRanges");
            public static readonly int StrandGuideIds = Shader.PropertyToID("_StrandGuideIds");
            public static readonly int StrandGuideWeights = Shader.PropertyToID("_StrandGuideWeights");
            public static readonly int StrandGuideSelf = Shader.PropertyToID("_StrandGuideSelf");
            public static readonly int VertexArcT = Shader.PropertyToID("_VertexArcT");
            public static readonly int GuideLengthSweep = Shader.PropertyToID("_GuideLengthSweep");
            public static readonly int FusedStrandBase = Shader.PropertyToID("_FusedStrandBase");
            public static readonly int FusedLocalRest = Shader.PropertyToID("_FusedLocalRest");
            public static readonly int CosseratFrames = Shader.PropertyToID("_CosseratFrames");
            public static readonly int FusedLanesPerStrand = Shader.PropertyToID("_FusedLanesPerStrand");
            public static readonly int FusedStrandsPerGroup = Shader.PropertyToID("_FusedStrandsPerGroup");
            public static readonly int FusedIterations = Shader.PropertyToID("_FusedIterations");
            public static readonly int RootDistance = Shader.PropertyToID("_RootDistance");
            public static readonly int SimulationInfluence = Shader.PropertyToID("_SimulationInfluence");
            public static readonly int SimulationInfluenceIsUnit = Shader.PropertyToID("_SimulationInfluenceIsUnit");
            public static readonly int StrandCount = Shader.PropertyToID("_StrandCount");

            public static readonly int ObjectToWorld = Shader.PropertyToID("_ObjectToWorld");
            public static readonly int ShapeA = Shader.PropertyToID("_ShapeA");
            public static readonly int ShapeB = Shader.PropertyToID("_ShapeB");
            public static readonly int ShapeRot = Shader.PropertyToID("_ShapeRot");
            public static readonly int ShapeCount = Shader.PropertyToID("_ShapeCount");
            public static readonly int Count = Shader.PropertyToID("_Count");
            public static readonly int FrameDelta = Shader.PropertyToID("_FrameDelta");
            public static readonly int Time = Shader.PropertyToID("_Time");
            public static readonly int CurrentSubstep = Shader.PropertyToID("_CurrentSubstep");
            public static readonly int CurrentIteration = Shader.PropertyToID("_CurrentIteration");
            public static readonly int BoundsPartials = Shader.PropertyToID("_BoundsPartials");
            public static readonly int BoundsResult = Shader.PropertyToID("_BoundsResult");
            public static readonly int BoundsGroupCount = Shader.PropertyToID("_BoundsGroupCount");
            public static readonly int Residual = Shader.PropertyToID("_Residual");
            public static readonly int SceneCollisionDistance = Shader.PropertyToID("_SceneCollisionDistance");
            public static readonly int SceneCollisionMinimum = Shader.PropertyToID("_SceneCollisionMinimum");
            public static readonly int SceneCollisionInverseSize = Shader.PropertyToID("_SceneCollisionInverseSize");
            public static readonly int SceneCollisionCellSize = Shader.PropertyToID("_SceneCollisionCellSize");
            public static readonly int SceneCollisionValid = Shader.PropertyToID("_SceneCollisionValid");
        }
    }
}
