using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace HairLines
{
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Hair Lines/Hair Renderer")]
    [DisallowMultipleComponent]
    public class LinePrimitiveRenderer : MonoBehaviour
    {
        public static IReadOnlyList<LinePrimitiveRenderer> Instances => _instances;

        public static Material FallbackMaterial
        {
            get
            {
                if (_fallbackMaterial == null)
                {
                    var shader = Shader.Find(LineShaderIds.LineShaderName);
                    if (shader == null)
                    {
                        Debug.LogError($"[HairLines] Shader '{LineShaderIds.LineShaderName}' not found.");
                        return null;
                    }

                    _fallbackMaterial = new Material(shader)
                    {
                        name = "HairLines Default (built-in)",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                }

                return _fallbackMaterial;
            }
        }

        public SourceMode Source { get => _source; set => _source = value; }
        public HairScalpSettings Scalp => _scalp;
        public IReadOnlyList<HairGroup> Groups => _groups;
        public bool HasGroups => _groups.Count > 0;
        public HairGroomAsset GroomAsset { get => _groomAsset; set => _groomAsset = value; }
        public float DisplayFraction
        {
            get => _displayFraction;
            set
            {
                float clamped = Mathf.Clamp(value, 0.01f, 1f);
                if (Mathf.Approximately(clamped, _displayFraction))
                {
                    return;
                }

                _displayFraction = clamped;
                Rebuild();
            }
        }
        public bool LodEnabled { get => _lodEnabled; set { _lodEnabled = value; InvalidateLodCache(); } }
        public float LodFullDetailCoverage
        {
            get => _lodFullDetailCoverage;
            set { _lodFullDetailCoverage = Mathf.Clamp(value, 0.001f, 4f); InvalidateLodCache(); }
        }
        public float LodMinimumDetailCoverage
        {
            get => _lodMinimumDetailCoverage;
            set { _lodMinimumDetailCoverage = Mathf.Clamp(value, 0f, 4f); InvalidateLodCache(); }
        }
        public float LodMinimumStrandFraction
        {
            get => _lodMinimumStrandFraction;
            set { _lodMinimumStrandFraction = Mathf.Clamp01(value); InvalidateLodCache(); }
        }
        public AnimationCurve LodCurve { get => _lodCurve; set { _lodCurve = value; InvalidateLodCache(); } }

        public float LodStrandWidthPixels
        {
            get => _lodStrandWidthPixels;
            set { _lodStrandWidthPixels = Mathf.Clamp(value, 0.25f, 8f); InvalidateLodCache(); }
        }
        public Material Material { get => _material; set { _material = value; _propertiesDirty = true; } }
        public Material EffectiveMaterial => _material != null ? _material : FallbackMaterial;
        public int DrawRangeCount => _model != null ? _model.DrawRangeCount : 0;

        public bool HasDrawMaterial
        {
            get
            {
                for (int drawRange = 0; drawRange < DrawRangeCount; drawRange++)
                {
                    if (GetDrawRangeMaterial(drawRange) != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public LinePrimitiveModel Model => _model;
        public bool IsRebuilding => _pendingBuild != null;

#if UNITY_EDITOR
        public static void SetEditorVisibility(LinePrimitiveRenderer renderer, bool visible)
        {
            if (renderer == null)
            {
                return;
            }

            if (visible)
            {
                _editorHiddenRenderers.Remove(renderer);
            }
            else
            {
                _editorHiddenRenderers.Add(renderer);
            }
        }

        public static bool IsEditorHidden(LinePrimitiveRenderer renderer)
        {
            return renderer != null && _editorHiddenRenderers.Contains(renderer);
        }

        public static bool IsEditorHidden(LinePrimitiveRenderer renderer, Camera camera)
        {
            if (IsEditorHidden(renderer))
            {
                return true;
            }

            UnityEngine.SceneManagement.Scene scene = renderer.gameObject.scene;
            UnityEngine.SceneManagement.Scene cameraScene = camera != null ? camera.scene : default;

            return cameraScene.IsValid()
                ? scene != cameraScene
                : UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(scene);
        }
#endif

        public static bool DrawsForPreview(Camera camera)
        {
#if UNITY_EDITOR
            if (camera == null || !camera.scene.IsValid())
            {
                return false;
            }

            for (int i = 0; i < _instances.Count; i++)
            {
                if (_instances[i] != null && _instances[i].gameObject.scene == camera.scene)
                {
                    return true;
                }
            }
#endif
            return false;
        }

        public MaterialPropertyBlock PropertyBlock
        {
            get
            {
                if (_propertiesDirty)
                {
                    UpdateProperties();
                }

                return _propertyBlock;
            }
        }

        public Matrix4x4 DrawMatrix => _worldSpaceBuffers ? SolvedToCurrentPose : transform.localToWorldMatrix;

        private Matrix4x4 SolvedToCurrentPose => transform.localToWorldMatrix * _solvedWorldToLocal;

        public Matrix4x4 GetPreviousDrawMatrix()
        {
            int frame = Time.frameCount;
            if (_motionMatrixFrame != frame)
            {
                Matrix4x4 current = DrawMatrix;
                _previousDrawMatrix = _motionMatrixFrame == frame - 1 ? _lastDrawMatrix : current;
                _lastDrawMatrix = current;
                _motionMatrixFrame = frame;
            }

            return _previousDrawMatrix;
        }

        public GraphicsBuffer GetPreviousPositionBuffer()
        {
            if (_model == null)
            {
                return null;
            }

            GraphicsBuffer previous = _model.PreviousPositionBuffer;
            return previous != null && _model.PreviousPositionsFrame == Time.frameCount
                ? previous
                : _model.PositionBuffer;
        }

        public Bounds WorldBounds
        {
            get
            {
                if (_worldSpaceBuffers)
                {
                    return TransformBounds(_overrideBounds, SolvedToCurrentPose);
                }

                if (_model != null)
                {
                    return TransformBounds(_model.LocalBounds, transform.localToWorldMatrix);
                }

                return _worldBoundsValid ? _worldBounds : new Bounds(transform.position, Vector3.zero);
            }
        }

        public Bounds Bounds => WorldBounds;

        public float CullingBoundsPadding
        {
            get => _cullingBoundsPadding;
            set => _cullingBoundsPadding = Mathf.Max(0f, value);
        }

        internal Bounds CullingBounds
        {
            get
            {
                Bounds bounds = WorldBounds;
                bounds.Expand(_cullingBoundsPadding * 2f);
                return bounds;
            }
        }

        internal bool RequiresRealtimeUpdates
        {
            get
            {
                return isActiveAndEnabled && _model != null && _model.EdgeCount > 0;
            }
        }

        public float GetLodStrandFraction(Camera camera)
        {
            if (!_lodEnabled || camera == null || _model == null || _model.StrandCount <= 1)
            {
                return 1f;
            }

            int cameraId = camera.GetInstanceID();
            int frame = Time.frameCount;
            if (_lodCacheFrame == frame && _lodCacheCameraId == cameraId)
            {
                return _lodCacheFraction;
            }

            float fraction = EvaluateLodStrandFraction(camera, WorldBounds);

            _lodCacheCameraId = cameraId;
            _lodCacheFrame = frame;
            _lodCacheFraction = fraction;
            return fraction;
        }

        public float GetSimulationLodStrandFraction(Camera camera)
        {
            if (!_lodEnabled || camera == null || _model == null || _model.StrandCount <= 1)
            {
                return 1f;
            }

            Bounds restBounds = TransformBounds(_model.LocalBounds, transform.localToWorldMatrix);
            return EvaluateLodStrandFraction(camera, restBounds);
        }

        private float EvaluateLodStrandFraction(Camera camera, Bounds bounds)
        {
            float normalized = 1f - ScreenCoverage(camera, bounds);
            float curve = _lodCurve != null && _lodCurve.length > 0
                ? Mathf.Clamp01(_lodCurve.Evaluate(normalized))
                : normalized;
            float fraction = Mathf.Lerp(1f, Mathf.Clamp01(_lodMinimumStrandFraction), curve);

            fraction = Mathf.Min(fraction, WidthLodStrandFraction(camera, bounds));
            fraction = Mathf.Max(fraction, Mathf.Clamp01(_lodMinimumStrandFraction));
            return fraction;
        }

        public float GetShadowLodStrandFraction(Camera camera)
        {
            if (!_lodEnabled || camera == null)
            {
                return 1f;
            }

            float coverage = ScreenCoverage(camera);
            float fraction = GetLodStrandFraction(camera) * Mathf.Lerp(0.35f, 1f, coverage);
            return Mathf.Clamp(fraction, 0.02f, 1f);
        }

        public float WidthLodStrandFraction(Camera camera)
        {
            if (camera == null || _model == null || _model.StrandCount <= 1)
            {
                return 1f;
            }

            return WidthLodStrandFraction(camera, WorldBounds);
        }

        private float WidthLodStrandFraction(Camera camera, Bounds bounds)
        {
            if (camera == null || _model == null || _model.StrandCount <= 1)
            {
                return 1f;
            }

            float distance = Mathf.Max(0.01f,
                Vector3.Distance(camera.transform.position, bounds.center) - bounds.extents.magnitude);

            float pixelHeight = Mathf.Max(1, camera.pixelHeight);
            float worldPerPixel = camera.orthographic
                ? 2f * camera.orthographicSize / pixelHeight
                : 2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance / pixelHeight;

            Vector3 size = _model.LocalBounds.size;
            float area = 2f * (size.x * size.y + size.y * size.z + size.z * size.x);
            if (area <= 1e-6f)
            {
                return 1f;
            }

            float spacing = Mathf.Sqrt(area / _model.StrandCount);
            if (spacing <= 1e-9f)
            {
                return 1f;
            }

            float strandsAcross = Mathf.Max(_lodStrandWidthPixels, 0.25f) * worldPerPixel / spacing;
            float bundle = Mathf.Max(1f, strandsAcross * strandsAcross);
            return Mathf.Clamp01(1f / bundle);
        }

        public float GetThickStrandDensity(float fibreDiameterMillimetres)
        {
            if (_model == null || _model.StrandCount <= 0)
            {
                return 1f;
            }

            Vector3 size = _model.LocalBounds.size;
            float area = 2f * (size.x * size.y + size.y * size.z + size.z * size.x);
            if (area <= 1e-6f)
            {
                return 1f;
            }

            float radius = fibreDiameterMillimetres * 0.0005f;
            float fibreArea = Mathf.PI * radius * radius;
            return Mathf.Clamp01(fibreArea * _model.StrandCount / area);
        }

        public float GetLodCoverageScale(Camera camera)
        {
            return 1f / Mathf.Max(GetLodStrandFraction(camera), 0.02f);
        }

        public float GetShadowFibreWeight(Camera camera)
        {
            return 1f / Mathf.Max(GetShadowLodStrandFraction(camera), 0.02f);
        }

        public float GetLodDepthScale(Camera camera)
        {
            return Mathf.Clamp01(GetLodStrandFraction(camera));
        }

        public float ScreenCoverage(Camera camera)
        {
            if (camera == null)
            {
                return 1f;
            }

            return ScreenCoverage(camera, WorldBounds);
        }

        private float ScreenCoverage(Camera camera, Bounds bounds)
        {
            if (camera == null)
            {
                return 1f;
            }

            Vector3 closest = bounds.ClosestPoint(camera.transform.position);
            float distance = Mathf.Max(Vector3.Distance(camera.transform.position, closest),
                                       camera.nearClipPlane);

            float frustumHeight = camera.orthographic
                ? camera.orthographicSize * 2f
                : 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            float coverage = bounds.size.magnitude / Mathf.Max(frustumHeight, 1e-4f);
            float full = Mathf.Max(_lodFullDetailCoverage, 1e-4f);
            float minimum = Mathf.Clamp(_lodMinimumDetailCoverage, 0f, full - 1e-4f);
            return Mathf.Clamp01(Mathf.InverseLerp(minimum, full, coverage));
        }

        public float GetLodStrandFraction()
        {
            return GetLodStrandFraction(Camera.main);
        }

        private void EnsureLodEdgeCountCache(Camera camera, float fraction)
        {
            if (_model == null)
            {
                return;
            }

            int frame = Time.frameCount;
            int cameraId = camera != null ? camera.GetInstanceID() : _lodEdgeCountCacheCameraId;
            bool valid = _lodEdgeCounts != null &&
                         _lodEdgeCounts.Length == _model.DrawRangeCount &&
                         _lodEdgeCountCacheFrame == frame &&
                         _lodEdgeCountCacheCameraId == cameraId &&
                         Mathf.Abs(_lodEdgeCountCacheFraction - fraction) <= 1e-6f;
            if (valid)
            {
                return;
            }

            if (_lodEdgeCounts == null || _lodEdgeCounts.Length != _model.DrawRangeCount)
            {
                _lodEdgeCounts = new int[_model.DrawRangeCount];
            }

            _lodTotalEdgeCount = 0;
            for (int drawRange = 0; drawRange < _model.DrawRangeCount; drawRange++)
            {
                int group = _model.GroupForDrawRange(drawRange);
                float groupFraction = GetGroupLodFraction(group, fraction);
                int edgeCount = _model.EdgeCountForDrawRange(drawRange, groupFraction);
                _lodEdgeCounts[drawRange] = edgeCount;
                _lodTotalEdgeCount += edgeCount;
            }

            _lodEdgeCountCacheCameraId = cameraId;
            _lodEdgeCountCacheFrame = frame;
            _lodEdgeCountCacheFraction = fraction;
        }

        private void InvalidateLodCache()
        {
            _lodCacheFrame = -1;
            _lodEdgeCountCacheFrame = -1;
        }

        private float GetGroupLodFraction(int group, float fraction)
        {
            if (group < 0 || group >= _groups.Count)
            {
                return fraction;
            }

            HairGrowSettings settings = SettingsOf(group);
            float priority = settings != null ? settings.LodPriority : 0.5f;
            float offset = (priority - 0.5f) * (1f - fraction) * 0.8f;
            return Mathf.Clamp01(fraction + offset);
        }

        public int GetLodEdgeCount(Camera camera)
        {
            if (_model == null || _model.EdgeCount == 0)
            {
                return 0;
            }

            return GetLodEdgeCount(camera, GetLodStrandFraction(camera));
        }

        internal int GetLodEdgeCount(Camera camera, float fraction)
        {
            if (_model == null || _model.EdgeCount == 0)
            {
                return 0;
            }

            EnsureLodEdgeCountCache(camera, fraction);
            return _lodTotalEdgeCount;
        }

        public int GetDrawRangeVertexCount(Camera camera, int drawRange)
        {
            if (_model == null || drawRange < 0 || drawRange >= _model.DrawRangeCount)
            {
                return 0;
            }

            return GetDrawRangeVertexCount(drawRange, GetLodStrandFraction(camera));
        }

        internal int GetDrawRangeVertexCount(int drawRange, float fraction)
        {
            if (_model == null || drawRange < 0 || drawRange >= _model.DrawRangeCount)
            {
                return 0;
            }

            EnsureLodEdgeCountCache(null, fraction);
            int edgeCount = _lodEdgeCounts != null && drawRange < _lodEdgeCounts.Length
                ? _lodEdgeCounts[drawRange]
                : _model.EdgeCountForDrawRange(drawRange, fraction);
            return edgeCount * 2;
        }

        public Material GetDrawRangeMaterial(int drawRange)
        {
            if (_model == null || drawRange < 0 || drawRange >= _model.DrawRangeCount)
            {
                return null;
            }

            int group = _model.GroupForDrawRange(drawRange);
            Material groupMaterial = group < _groups.Count && _groups[group] != null
                ? _groups[group].Material
                : null;

            return groupMaterial != null ? groupMaterial : EffectiveMaterial;
        }

        public GraphicsBuffer GetDrawRangeIndexBuffer(int drawRange)
        {
            return _model?.DrawRangeIndexBuffer(drawRange);
        }

        public int GetDrawRangeInstanceCount(int drawRange)
        {
            return _model != null ? _model.InstanceCountForDrawRange(drawRange) : 1;
        }

        public MaterialPropertyBlock GetDrawRangePropertyBlock(int drawRange)
        {
            if (_propertiesDirty)
            {
                UpdateProperties();
            }

            return drawRange >= 0 && drawRange < _drawPropertyBlocks.Count
                ? _drawPropertyBlocks[drawRange]
                : null;
        }

        [Tooltip("Where the strands come from: grown procedurally, or loaded from a line model file.")]
        [SerializeField, FormerlySerializedAs("source")]
        private SourceMode _source = SourceMode.Procedural;

        [SerializeField]
        private HairScalpSettings _scalp = new HairScalpSettings();

        [Tooltip("Baked groom to draw. Produced by Bake, or imported from a .hair or .hairgroom file.")]
        [SerializeField]
        private HairGroomAsset _groomAsset;

        [Tooltip("Material using the HairLines/Line Primitive shader. When empty, a default one is used.")]
        [SerializeField, FormerlySerializedAs("material")]
        private Material _material;

        [Tooltip("Share of the strands grown while you work. The root order is progressive, so a " +
                 "fraction is the first strands of the full groom, vertex for vertex. Raise it " +
                 "back to 1 to see the groom as it ships.")]
        [SerializeField, Range(0.01f, 1f)]
        private float _displayFraction = 1f;

        [Tooltip("Reduce strand density with camera distance to keep distant hair affordable.")]
        [SerializeField]
        private bool _lodEnabled = true;

        [Tooltip("Share of the frame height the groom has to cover to keep every strand. " +
                 "0.5 means half the frame. Screen coverage rather than distance, so one " +
                 "setting behaves the same at any resolution and field of view.")]
        [SerializeField, Range(0.001f, 4f)]
        private float _lodFullDetailCoverage = 0.5f;

        [Tooltip("Share of the frame height below which the minimum strand density is used.")]
        [SerializeField, Range(0f, 4f)]
        private float _lodMinimumDetailCoverage = 0.05f;

        [Tooltip("Minimum fraction of strands retained after the LOD range.")]
        [SerializeField, Range(0.01f, 1f)]
        private float _lodMinimumStrandFraction = 0.2f;

        [Tooltip("How wide on screen, in pixels, the bundle one drawn strand stands for may get. " +
                 "Below this the strands it replaced could not have been told apart anyway. " +
                 "Two pixels is the value Huang et al. (2025) use throughout.")]
        [SerializeField, Range(0.25f, 8f)]
        private float _lodStrandWidthPixels = 2f;

        [Tooltip("Falloff curve for strand density across the LOD range. The horizontal axis is distance; the vertical axis is reduction.")]
        [SerializeField]
        private AnimationCurve _lodCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Extra margin in metres around the culling bounds. Simulated bounds arrive " +
                 "a few frames late through an async readback, and strands can swing past the " +
                 "last reported box; this margin keeps the groom from being culled or losing " +
                 "its shadow at the edge of the frame.")]
        [SerializeField, Min(0f)]
        private float _cullingBoundsPadding = 0.1f;

        private LinePrimitiveModel _model;
        private MaterialPropertyBlock _propertyBlock;
        private readonly List<MaterialPropertyBlock> _drawPropertyBlocks = new List<MaterialPropertyBlock>();

        private readonly List<HairGroup> _groups = new List<HairGroup>();
        private readonly List<HairGroup> _groupComponents = new List<HairGroup>();
        private readonly Dictionary<HairGroup, HairGroupBuildCache> _groupCache =
            new Dictionary<HairGroup, HairGroupBuildCache>();
        private HairMaskKey[] _operatorKeys = new HairMaskKey[4];
        private readonly List<HairGroup> _staleGroups = new List<HairGroup>();
        private readonly Vector4[] _groupTints = new Vector4[MaxGroups];

        private Bounds _overrideBounds;
        private Matrix4x4 _solvedWorldToLocal = Matrix4x4.identity;
        private const double RecentlyRenderedTolerance = 0.2;
        private int _lastRenderedFrame = int.MinValue;
        private double _lastRenderedTime = double.NegativeInfinity;
        private Matrix4x4 _lastDrawMatrix = Matrix4x4.identity;
        private Matrix4x4 _previousDrawMatrix = Matrix4x4.identity;
        private int _motionMatrixFrame = int.MinValue;
        private Bounds _worldBounds;
        private bool _worldBoundsValid;
        private bool _propertiesDirty = true;
        private bool _worldSpaceBuffers;
        private int _lodCacheCameraId;
        private int _lodCacheFrame = -1;
        private float _lodCacheFraction = 1f;
        private int[] _lodEdgeCounts;
        private int _lodEdgeCountCacheCameraId;
        private int _lodEdgeCountCacheFrame = -1;
        private float _lodEdgeCountCacheFraction = -1f;
        private int _lodTotalEdgeCount;
        private PendingBuild _pendingBuild;
        private bool _rebuildQueued;

        private const int MaxGroups = 16;

        private static readonly List<LinePrimitiveRenderer> _instances = new List<LinePrimitiveRenderer>();
#if UNITY_EDITOR
        private static readonly HashSet<LinePrimitiveRenderer> _editorHiddenRenderers =
            new HashSet<LinePrimitiveRenderer>();
#endif
        private static Material _fallbackMaterial;

        private static readonly ProfilerMarker _lateUpdateMarker =
            new ProfilerMarker("HairLines.Renderer.LateUpdate");
        private static readonly ProfilerMarker _rebuildMarker =
            new ProfilerMarker("HairLines.Renderer.Rebuild");
        private static readonly ProfilerMarker _proceduralBuildMarker =
            new ProfilerMarker("HairLines.Renderer.BeginProceduralRebuild");
        private static readonly ProfilerMarker _buildCompleteMarker =
            new ProfilerMarker("HairLines.Renderer.PollRebuildComplete");
        private static readonly ProfilerMarker _updatePropertiesMarker =
            new ProfilerMarker("HairLines.Renderer.UpdateProperties");

        private void OnEnable()
        {
            if (!_worldBoundsValid)
            {
                _worldBounds = new Bounds(transform.position, Vector3.zero);
                _worldBoundsValid = true;
            }

            if (!_instances.Contains(this))
            {
                _instances.Add(this);
            }

            if (_model == null)
            {
                Rebuild();
            }
        }

        private void LateUpdate()
        {
            using ProfilerMarker.AutoScope scope = _lateUpdateMarker.Auto();
            PollRebuild();
        }

        public void MarkPropertiesDirty()
        {
            _propertiesDirty = true;
        }

        public void RefreshGroupTints()
        {
            UpdateGroupTints();
        }

        public void RefreshGroups()
        {
            CollectGroups();
            UpdateGroupTints();
            InvalidateLodCache();
        }

        public void SetWorldSpaceBuffers(Bounds worldBounds)
        {
            _worldSpaceBuffers = true;
            _overrideBounds = worldBounds;
            _worldBounds = worldBounds;
            _worldBoundsValid = true;
        }

        public void SetSolvedPose(Matrix4x4 worldToLocal)
        {
            _solvedWorldToLocal = worldToLocal;
        }

        internal void MarkRendered()
        {
            _lastRenderedFrame = Time.frameCount;
            _lastRenderedTime = Time.realtimeSinceStartupAsDouble;
        }

        internal bool WasRecentlyRendered =>
            _lastRenderedFrame >= Time.frameCount - 1 ||
            Time.realtimeSinceStartupAsDouble - _lastRenderedTime <= RecentlyRenderedTolerance;

        public void ClearWorldSpaceBuffers()
        {
            _worldSpaceBuffers = false;
            _worldBounds = _model != null
                ? TransformBounds(_model.LocalBounds, transform.localToWorldMatrix)
                : new Bounds(transform.position, Vector3.zero);
            _worldBoundsValid = true;
        }

        public void RandomizeSeed()
        {
            CollectGroups();

            foreach (HairGroup group in _groups)
            {
#if UNITY_EDITOR
                UnityEditor.Undo.RecordObject(group, "Randomize Seed");
#endif
                group.RandomizeSeed();
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(group);
#endif
            }

            Rebuild();
        }

        public void Rebuild()
        {
            using ProfilerMarker.AutoScope scope = _rebuildMarker.Auto();
            if (_pendingBuild != null)
            {
                _rebuildQueued = true;
                return;
            }

            BeginRebuild();
        }

        private void BeginRebuild()
        {
            CollectGroups();

            try
            {
                if (_source == SourceMode.Procedural)
                {
                    BeginProceduralRebuild();
                }
                else
                {
                    BuildFromAssetNow();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[HairLines] Failed to build line primitives: {e.Message}", this);
            }
        }

        public void CompletePendingBuild()
        {
            while (_pendingBuild != null)
            {
                _pendingBuild.Work.CompleteWithoutResult();
                PollRebuild();
            }
        }

        private void PollRebuild()
        {
            PendingBuild pending = _pendingBuild;
            if (pending == null || !pending.Work.IsCompleted)
            {
                return;
            }

            _pendingBuild = null;
            UnregisterEditorBuildPoll();
            bool restart = _rebuildQueued;
            _rebuildQueued = false;

            try
            {
                using ProfilerMarker.AutoScope scope = _buildCompleteMarker.Auto();
                if (restart)
                {
                    pending.Work.CompleteWithoutResult();
                }
                else
                {
                    InstallBuiltModel(pending.Work.Complete(_worldSpaceBuffers ? null : _model));
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[HairLines] Failed to finish line primitive Job: {e.Message}", this);
            }
            finally
            {
                pending.Dispose();
            }

            if (restart && isActiveAndEnabled)
            {
                BeginRebuild();
            }
        }

        public void Release()
        {
            StopPendingBuild();
            ReleaseBuiltModel();
        }

        private void ReleaseBuiltModel()
        {
            InvalidateLodCache();
            if (_model != null)
            {
                _worldBounds = _worldSpaceBuffers
                    ? _overrideBounds
                    : TransformBounds(_model.LocalBounds, transform.localToWorldMatrix);
                _worldBoundsValid = true;
            }

            _model?.Dispose();
            _model = null;

            foreach (MaterialPropertyBlock propertyBlock in _drawPropertyBlocks)
            {
                propertyBlock.Clear();
            }

            _drawPropertyBlocks.Clear();
            _propertyBlock?.Clear();

            _worldSpaceBuffers = false;
        }

        private void BeginProceduralRebuild()
        {
            using ProfilerMarker.AutoScope scope = _proceduralBuildMarker.Auto();
            HairSurface surface = null;
            HairScalpSphere scalpSphere;

            if (_scalp.Mode == HairScalpMode.Sphere)
            {
                scalpSphere = new HairScalpSphere(_scalp.SphereCenter, _scalp.SphereRadius);
            }
            else
            {
                if (!HairSurface.TryCreate(_scalp.Renderer, _scalp.Submesh, out surface, out string error))
                {
                    Debug.LogError($"[HairLines] {error}", this);
                    return;
                }

                Matrix4x4 fitMatrix = transform.worldToLocalMatrix * surface.LocalToWorld;
                scalpSphere = surface.FitInteriorSphere(fitMatrix);
            }

            try
            {
                var settingsByGroup = new List<HairGrowSettings>(_groups.Count);
                var rootsByGroup = new List<HairRootFrame[]>(_groups.Count);
                var combByGroup = new List<Vector2[]>(_groups.Count);
                var weightsByGroup = new List<float[]>(_groups.Count);
                var colorsByGroup = new List<uint[]>(_groups.Count);
                var guideFramesByGroup = new List<HairRootFrame[]>(_groups.Count);
                bool grounds = false;
                Matrix4x4 surfaceToLocal = surface != null
                    ? transform.worldToLocalMatrix * surface.LocalToWorld
                    : Matrix4x4.identity;

                for (int group = 0; group < _groups.Count; group++)
                {
                    HairGrowSettings settings = SettingsOf(group);
                    HairRootFrame[] guideFrames = EvaluateGuideFrames(settings, surface, surfaceToLocal);
                    HairGroupBuildCache inputs = ResolveGroupInputs(group, settings,
                                                                   DisplayedStrandCount(settings),
                                                                   surface, surfaceToLocal, guideFrames);

                    settingsByGroup.Add(settings);
                    rootsByGroup.Add(inputs.Roots);
                    combByGroup.Add(inputs.Comb);
                    weightsByGroup.Add(inputs.OperatorWeights);
                    colorsByGroup.Add(inputs.Colors);
                    guideFramesByGroup.Add(guideFrames);
                    grounds |= HasGround(settings);
                }

                Vector3[] groundPositions = grounds && surface != null
                    ? GroundPositions(surface, surfaceToLocal)
                    : null;
                LineHairBuildJob work = LineHairBuildJob.Schedule(rootsByGroup, settingsByGroup,
                                                                 guideFramesByGroup, scalpSphere,
                                                                 combByGroup, weightsByGroup, colorsByGroup,
                                                                 groundPositions,
                                                                 groundPositions != null ? surface.Triangles : null);
                _pendingBuild = new PendingBuild(work);
                RegisterEditorBuildPoll();
            }
            finally
            {
                surface?.Dispose();
            }
        }

        private void BuildFromAssetNow()
        {
            if (_groomAsset == null)
            {
                Debug.LogWarning("[HairLines] No groom asset assigned, so nothing is drawn.", this);
                InstallBuiltModel(null);
                return;
            }

            LinePrimitiveModel model = _groomAsset.CreateModel();
            InstallBuiltModel(model);
        }

        private void InstallBuiltModel(LinePrimitiveModel model)
        {
            ReleaseBuiltModel();
            _model = model;

            _worldBounds = _model != null
                ? TransformBounds(_model.LocalBounds, transform.localToWorldMatrix)
                : new Bounds(transform.position, Vector3.zero);
            _worldBoundsValid = true;
            UpdateGroupTints();
            GetComponent<LineHairSimulation>()?.RefreshGroups();
        }

        private void CollectGroups()
        {
            _groups.Clear();
            GetComponentsInChildren(false, _groupComponents);

            foreach (HairGroup group in _groupComponents)
            {
                if (_groups.Count < MaxGroups)
                {
                    _groups.Add(group);
                }
            }

            PruneRootCache();
        }

        private void PruneRootCache()
        {
            if (_groupCache.Count == 0)
            {
                return;
            }

            _staleGroups.Clear();

            foreach (HairGroup group in _groupCache.Keys)
            {
                if (group == null || !_groups.Contains(group))
                {
                    _staleGroups.Add(group);
                }
            }

            foreach (HairGroup group in _staleGroups)
            {
                _groupCache.Remove(group);
            }

            _staleGroups.Clear();
        }

        private int DisplayedStrandCount(HairGrowSettings settings)
        {
            return Mathf.Clamp(Mathf.CeilToInt(settings.StrandCount * _displayFraction),
                               1, settings.StrandCount);
        }

        private HairGroupBuildCache ResolveGroupInputs(int group, HairGrowSettings settings, int strandCount,
                                                       HairSurface surface, Matrix4x4 surfaceToLocal,
                                                       HairRootFrame[] guideFrames)
        {
            HairGroup owner = _groups[group];

            if (!_groupCache.TryGetValue(owner, out HairGroupBuildCache cache))
            {
                cache = new HairGroupBuildCache();
                _groupCache[owner] = cache;
            }

            int guideCount = guideFrames != null ? guideFrames.Length : 0;
            int sampled = Mathf.Max(1, strandCount - guideCount);

            if (surface == null)
            {
                var sphereKey = new HairRootKey(_scalp, settings.Seed + group);

                if (cache.NeedsRoots(sphereKey, sampled, Matrix4x4.identity))
                {
                    cache.SetRoots(sphereKey, sampled, Matrix4x4.identity,
                                   HairSphereScalp.SampleRoots(_scalp.SphereCenter, _scalp.SphereRadius,
                                                               _scalp.SphereCoverage, sampled,
                                                               settings.Seed + group));
                }
            }
            else
            {
                var key = new HairRootKey(surface, _scalp.Submesh, settings.DensityMask, settings.Seed,
                                          guideFrames);

                if (cache.NeedsBindings(key, settings.StrandCount))
                {
                    var distribution = new HairSurfaceDistribution(surface, ResolveGroupMask(group));
                    cache.SetBindings(key, HairSurfaceSampler.Sample(distribution, settings.StrandCount,
                                                                     settings.Seed));
                }

                if (cache.NeedsRoots(key, sampled + guideCount, surfaceToLocal))
                {
                    cache.SetRoots(key, sampled + guideCount, surfaceToLocal,
                                   LeadWithGuides(guideFrames,
                                                  HairSurfaceSampler.Evaluate(surface, cache.Bindings,
                                                                              sampled, surfaceToLocal)));
                }
            }

            RefreshComb(settings, cache);
            RefreshOperatorWeights(settings, cache);
            RefreshColors(settings, cache);
            return cache;
        }

        private static bool HasGround(HairGrowSettings settings)
        {
            List<HairGroomOperator> operators = settings.Operators;

            for (int i = 0; i < operators.Count; i++)
            {
                if (operators[i] is HairGroundOperator && operators[i].Enabled)
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector3[] GroundPositions(HairSurface surface, Matrix4x4 surfaceToLocal)
        {
            Vector3[] source = surface.VertexPositions;
            var positions = new Vector3[source.Length];

            for (int i = 0; i < source.Length; i++)
            {
                positions[i] = surfaceToLocal.MultiplyPoint3x4(source[i]);
            }

            return positions;
        }

        private static void RefreshColors(HairGrowSettings settings, HairGroupBuildCache cache)
        {
            HairColorMap root = settings.RootColorMap;
            HairColorMap tip = settings.TipColorMap;
            HairColorMap tint = settings.TintMap;
            HairMaskKey rootKey = HairMaskKey.Of(root);
            HairMaskKey tipKey = HairMaskKey.Of(tip);
            HairMaskKey tintKey = HairMaskKey.Of(tint);

            if (!cache.NeedsColors(rootKey, tipKey, tintKey))
            {
                return;
            }

            if (!root.IsUsable && !tip.IsUsable && !tint.IsUsable)
            {
                cache.SetColors(rootKey, tipKey, tintKey, null);
                return;
            }

            root.Invalidate();
            tip.Invalidate();
            tint.Invalidate();
            HairRootFrame[] roots = cache.Roots;
            var colors = new uint[roots.Length * 3];

            for (int i = 0; i < roots.Length; i++)
            {
                Vector2 uv = roots[i].Uv;
                colors[i * 3] = root.IsUsable ? root.EvaluatePacked(uv) : 0u;
                colors[i * 3 + 1] = tip.IsUsable ? tip.EvaluatePacked(uv) : 0u;
                colors[i * 3 + 2] = tint.IsUsable ? tint.EvaluatePacked(uv) : 0xFFFFFFFFu;
            }

            cache.SetColors(rootKey, tipKey, tintKey, colors);
        }

        private static HairRootFrame[] LeadWithGuides(HairRootFrame[] guideFrames, HairRootFrame[] sampled)
        {
            if (guideFrames == null || guideFrames.Length == 0)
            {
                return sampled;
            }

            var combined = new HairRootFrame[guideFrames.Length + sampled.Length];
            Array.Copy(guideFrames, combined, guideFrames.Length);
            Array.Copy(sampled, 0, combined, guideFrames.Length, sampled.Length);
            return combined;
        }

        private static void RefreshComb(HairGrowSettings settings, HairGroupBuildCache cache)
        {
            HairMaskKey key = HairMaskKey.Of(settings.CombMap);

            if (!cache.NeedsComb(key))
            {
                return;
            }

            settings.CombMap?.Invalidate();
            HairRootFrame[] roots = cache.Roots;
            var comb = new Vector2[roots.Length];

            if (settings.CombMap != null && settings.CombMap.IsUsable)
            {
                for (int i = 0; i < roots.Length; i++)
                {
                    comb[i] = settings.CombMap.Evaluate(roots[i].Uv);
                }
            }

            cache.SetComb(key, comb);
        }

        private void RefreshOperatorWeights(HairGrowSettings settings, HairGroupBuildCache cache)
        {
            List<HairGroomOperator> operators = settings.Operators;
            int active = 0;

            for (int i = 0; i < operators.Count; i++)
            {
                if (operators[i] != null && operators[i].Enabled)
                {
                    active++;
                }
            }

            if (_operatorKeys.Length < active)
            {
                _operatorKeys = new HairMaskKey[Mathf.NextPowerOfTwo(Mathf.Max(active, 4))];
            }

            int slot = 0;

            for (int i = 0; i < operators.Count; i++)
            {
                HairGroomOperator groomOperator = operators[i];

                if (groomOperator == null || !groomOperator.Enabled)
                {
                    continue;
                }

                _operatorKeys[slot++] = HairMaskKey.Of(groomOperator.Mask, true);
            }

            if (!cache.NeedsWeights(_operatorKeys, active))
            {
                return;
            }

            HairRootFrame[] roots = cache.Roots;
            var weights = new float[roots.Length * active];
            slot = 0;

            for (int i = 0; i < operators.Count; i++)
            {
                HairGroomOperator groomOperator = operators[i];

                if (groomOperator == null || !groomOperator.Enabled)
                {
                    continue;
                }

                groomOperator.InvalidateMask();

                for (int root = 0; root < roots.Length; root++)
                {
                    weights[root * active + slot] = groomOperator.WeightAt(roots[root]);
                }

                slot++;
            }

            cache.SetWeights(_operatorKeys, active, weights);
        }

        private static HairRootFrame[] EvaluateGuideFrames(HairGrowSettings settings, HairSurface surface,
                                                           Matrix4x4 surfaceToLocal)
        {
            HairGuideSet guides = settings.Guides;
            if (surface == null || guides == null || guides.IsEmpty)
            {
                return System.Array.Empty<HairRootFrame>();
            }

            var bindings = new HairRootBinding[guides.Count];
            for (int i = 0; i < guides.Count; i++)
            {
                bindings[i] = guides.Guides[i].Binding;
            }

            return HairSurfaceSampler.Evaluate(surface, bindings, surfaceToLocal);
        }

        private HairGrowSettings SettingsOf(int group)
        {
            return _groups[group].ResolvedSettings;
        }

        private IHairDensityMask ResolveGroupMask(int group)
        {
            HairDensityMask mask = SettingsOf(group).DensityMask;

            if (mask.IsUsable)
            {
                mask.Invalidate();
                return mask;
            }

            if (mask.HasTexture)
            {
                Debug.LogWarning("[HairLines] The density mask texture is not readable, so it is ignored. " +
                                 "Enable Read/Write in its import settings.", this);
            }

            return null;
        }

        private void UpdateGroupTints()
        {
            for (int i = 0; i < MaxGroups; i++)
            {
                Color tint = i < _groups.Count && _groups[i] != null ? _groups[i].Tint : Color.white;
                _groupTints[i] = new Vector4(tint.r, tint.g, tint.b, tint.a);
            }

            _propertiesDirty = true;
        }

        private void StopPendingBuild()
        {
            PendingBuild pending = _pendingBuild;
            _pendingBuild = null;
            _rebuildQueued = false;
            UnregisterEditorBuildPoll();
            pending?.Dispose();
        }

        private void RegisterEditorBuildPoll()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.update -= PollRebuild;
                UnityEditor.EditorApplication.update += PollRebuild;
            }
#endif
        }

        private void UnregisterEditorBuildPoll()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= PollRebuild;
#endif
        }

        private class PendingBuild : IDisposable
        {
            public LineHairBuildJob Work { get; }

            public PendingBuild(LineHairBuildJob work)
            {
                Work = work;
            }

            public void Dispose()
            {
                Work.Dispose();
            }
        }

        private void UpdateProperties()
        {
            using ProfilerMarker.AutoScope scope = _updatePropertiesMarker.Auto();
            if (_model == null || _model.IsEmpty)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            FillPropertyBlock(_propertyBlock, Vector4.one, -1);

            while (_drawPropertyBlocks.Count < _model.DrawRangeCount)
            {
                _drawPropertyBlocks.Add(new MaterialPropertyBlock());
            }

            for (int drawRange = 0; drawRange < _model.DrawRangeCount; drawRange++)
            {
                int group = _model.GroupForDrawRange(drawRange);
                FillPropertyBlock(_drawPropertyBlocks[drawRange],
                                  _groupTints[Mathf.Clamp(group, 0, MaxGroups - 1)], drawRange);
            }

            _propertiesDirty = false;
        }

        private void FillPropertyBlock(MaterialPropertyBlock propertyBlock, Vector4 groupTint,
                                       int drawRange)
        {
            propertyBlock.Clear();
            propertyBlock.SetBuffer(LineShaderIds.Positions, _model.PositionBuffer);
            propertyBlock.SetBuffer(LineShaderIds.Colors, _model.ColorBuffer);
            propertyBlock.SetBuffer(LineShaderIds.StrandInfo, _model.StrandInfoBuffer);
            propertyBlock.SetFloat(LineShaderIds.HasVertexColor, _model.HasVertexColor ? 1f : 0f);
            propertyBlock.SetBuffer(LineShaderIds.Opacity, _model.VertexOpacityBuffer);
            propertyBlock.SetVector(LineShaderIds.GroupTint, groupTint);

            propertyBlock.SetBuffer(LineShaderIds.StrandDebug, _model.StrandDebugBuffer);
            propertyBlock.SetBuffer(LineShaderIds.StrandAttributes, _model.StrandAttributeBuffer);
            propertyBlock.SetFloat(LineShaderIds.HasStrandAttributes, _model.HasStrandAttributes ? 1f : 0f);
            propertyBlock.SetVector(LineShaderIds.StrandRange,
                drawRange >= 0 ? _model.StrandRangeForDrawRange(drawRange, 1f) : Vector4.zero);
            propertyBlock.SetVector(LineShaderIds.HairDebugRange,
                drawRange >= 0 ? _model.DebugRangeForDrawRange(drawRange) : Vector4.zero);

            propertyBlock.SetBuffer(LineShaderIds.CopyOffsets, _model.CopyOffsetBuffer);
            propertyBlock.SetVector(LineShaderIds.CopyParams,
                drawRange >= 0 ? _model.CopyParamsForDrawRange(drawRange) : Vector4.zero);
            propertyBlock.SetVector(LineShaderIds.CopySpread,
                drawRange >= 0 ? _model.CopySpreadForDrawRange(drawRange) : Vector4.zero);
        }

        private static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(local.center);
            Vector3 extents = local.extents;

            var transformed = new Vector3(
                Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y + Mathf.Abs(matrix.m02) * extents.z,
                Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z,
                Mathf.Abs(matrix.m20) * extents.x + Mathf.Abs(matrix.m21) * extents.y + Mathf.Abs(matrix.m22) * extents.z);

            return new Bounds(center, transformed * 2f);
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            SetEditorVisibility(this, true);
#endif
            _instances.Remove(this);

            StopPendingBuild();
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            SetEditorVisibility(this, true);
#endif
            Release();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            InvalidateLodCache();
            _lodCurve ??= AnimationCurve.Linear(0f, 0f, 1f, 1f);
            _lodFullDetailCoverage = Mathf.Clamp(_lodFullDetailCoverage, 0.001f, 4f);
            _lodMinimumDetailCoverage = Mathf.Clamp(_lodMinimumDetailCoverage, 0f,
                                                    _lodFullDetailCoverage - 0.001f);
            _lodMinimumStrandFraction = Mathf.Clamp01(_lodMinimumStrandFraction);
            _cullingBoundsPadding = Mathf.Max(0f, _cullingBoundsPadding);
            _propertiesDirty = true;

            if (_model != null && isActiveAndEnabled)
            {
                Rebuild();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_model == null)
            {
                return;
            }

            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.4f);
            Bounds bounds = WorldBounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            if (_cullingBoundsPadding > 0f)
            {
                Bounds culling = CullingBounds;
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.15f);
                Gizmos.DrawWireCube(culling.center, culling.size);
            }
        }
#endif

        public enum SourceMode
        {
            Procedural,
            Asset,
        }
    }
}
