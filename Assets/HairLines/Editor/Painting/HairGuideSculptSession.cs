using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using System.Collections.Generic;

namespace HairLines.EditorTools
{
    internal enum HairGuideEditMode
    {
        Points,
        Grab,
        Comb,
        Smooth,
        Length,
        Add,
        Puff,
        Twist,
        Curl,
        Clump,
        Density,
        Freeze,
    }

    internal enum HairGuideDensityMode
    {
        Auto,
        Add,
        Remove,
    }

    internal enum HairGuideSelectionMode
    {
        Click,
        Circle,
        Area,
        Brush,
    }

    internal enum HairGuideToolMode
    {
        Selection,
        Edit,
    }

    internal static class HairGuideSculptSettings
    {
        public static HairGuideToolMode ToolMode
        {
            get => (HairGuideToolMode)Mathf.Clamp(EditorPrefs.GetInt(ToolModeKey, 0), 0, 1);
            set => EditorPrefs.SetInt(ToolModeKey, (int)value);
        }

        public static HairGuideEditMode Mode
        {
            get => (HairGuideEditMode)Mathf.Clamp(EditorPrefs.GetInt(ModeKey, 1), 0,
                                                  (int)HairGuideEditMode.Freeze);
            set => EditorPrefs.SetInt(ModeKey, (int)value);
        }

        public static HairGuideSelectionMode SelectionMode
        {
            get => (HairGuideSelectionMode)Mathf.Clamp(EditorPrefs.GetInt(SelectionModeKey, 0), 0, 3);
            set => EditorPrefs.SetInt(SelectionModeKey, (int)value);
        }

        public static float Diameter
        {
            get => EditorPrefs.GetFloat(DiameterKey, 0.12f);
            set => EditorPrefs.SetFloat(DiameterKey, Mathf.Max(0.002f, value));
        }

        public static bool XRay
        {
            get => EditorPrefs.GetBool(XRayKey, true);
            set => EditorPrefs.SetBool(XRayKey, value);
        }

        public static bool ShowHair
        {
            get => EditorPrefs.GetBool(ShowHairKey, true);
            set => EditorPrefs.SetBool(ShowHairKey, value);
        }

        public static bool Simulate
        {
            get => EditorPrefs.GetBool(SimulateKey, false);
            set => EditorPrefs.SetBool(SimulateKey, value);
        }

        public static bool ShortcutsExpanded
        {
            get => EditorPrefs.GetBool(ShortcutsExpandedKey, false);
            set => EditorPrefs.SetBool(ShortcutsExpandedKey, value);
        }

        public static float SelectionBrushRadius
        {
            get => EditorPrefs.GetFloat(SelectionBrushRadiusKey, 25f);
            set => EditorPrefs.SetFloat(SelectionBrushRadiusKey, Mathf.Clamp(value, 2f, 500f));
        }

        public static float Strength
        {
            get => EditorPrefs.GetFloat(StrengthKey, 0.5f);
            set => EditorPrefs.SetFloat(StrengthKey, Mathf.Clamp01(value));
        }

        public static float Hardness
        {
            get => EditorPrefs.GetFloat(HardnessKey, 0.35f);
            set => EditorPrefs.SetFloat(HardnessKey, Mathf.Clamp01(value));
        }

        public static bool PreserveLength
        {
            get => EditorPrefs.GetBool(PreserveLengthKey, true);
            set => EditorPrefs.SetBool(PreserveLengthKey, value);
        }

        public static bool MirrorX
        {
            get => EditorPrefs.GetBool(MirrorXKey, false);
            set => EditorPrefs.SetBool(MirrorXKey, value);
        }

        public static bool SoftSelection
        {
            get => EditorPrefs.GetBool(SoftSelectionKey, false);
            set => EditorPrefs.SetBool(SoftSelectionKey, value);
        }

        public static float SoftRadius
        {
            get => EditorPrefs.GetFloat(SoftRadiusKey, 0.05f);
            set => EditorPrefs.SetFloat(SoftRadiusKey, Mathf.Max(0.001f, value));
        }

        public static HairGuideFalloff SoftFalloff
        {
            get => (HairGuideFalloff)Mathf.Clamp(EditorPrefs.GetInt(SoftFalloffKey, 0), 0,
                                                 (int)HairGuideFalloff.Constant);
            set => EditorPrefs.SetInt(SoftFalloffKey, (int)value);
        }

        public static bool SoftConnected
        {
            get => EditorPrefs.GetBool(SoftConnectedKey, false);
            set => EditorPrefs.SetBool(SoftConnectedKey, value);
        }

        public static float CurlRadius
        {
            get => EditorPrefs.GetFloat(CurlRadiusKey, 0.01f);
            set => EditorPrefs.SetFloat(CurlRadiusKey, Mathf.Max(0.0005f, value));
        }

        public static float CurlTurns
        {
            get => EditorPrefs.GetFloat(CurlTurnsKey, 3f);
            set => EditorPrefs.SetFloat(CurlTurnsKey, Mathf.Clamp(value, 0.25f, 20f));
        }

        public static HairGuideDensityMode DensityMode
        {
            get => (HairGuideDensityMode)Mathf.Clamp(EditorPrefs.GetInt(DensityModeKey, 0), 0,
                                                     (int)HairGuideDensityMode.Remove);
            set => EditorPrefs.SetInt(DensityModeKey, (int)value);
        }

        public static float DensityDistance
        {
            get => EditorPrefs.GetFloat(DensityDistanceKey, 0.015f);
            set => EditorPrefs.SetFloat(DensityDistanceKey, Mathf.Max(0.001f, value));
        }

        public static int DensityAttempts
        {
            get => EditorPrefs.GetInt(DensityAttemptsKey, 100);
            set => EditorPrefs.SetInt(DensityAttemptsKey, Mathf.Clamp(value, 1, 1000));
        }

        public static int ResampleSegments
        {
            get => EditorPrefs.GetInt(ResampleSegmentsKey, 16);
            set => EditorPrefs.SetInt(ResampleSegmentsKey, Mathf.Clamp(value, 1, 128));
        }

        private const string Prefix = "HairLines.GuideSculpt.";
        private const string ToolModeKey = Prefix + "ToolMode";
        private const string ModeKey = Prefix + "Mode";
        private const string SelectionModeKey = Prefix + "SelectionMode";
        private const string DiameterKey = Prefix + "Diameter";
        private const string SelectionBrushRadiusKey = Prefix + "SelectionBrushRadius";
        private const string ShortcutsExpandedKey = Prefix + "ShortcutsExpanded";
        private const string XRayKey = Prefix + "XRay";
        private const string ShowHairKey = Prefix + "ShowHair";
        private const string SimulateKey = Prefix + "Simulate";
        private const string StrengthKey = Prefix + "Strength";
        private const string HardnessKey = Prefix + "Hardness";
        private const string PreserveLengthKey = Prefix + "PreserveLength";
        private const string MirrorXKey = Prefix + "MirrorX";
        private const string SoftSelectionKey = Prefix + "SoftSelection";
        private const string SoftRadiusKey = Prefix + "SoftRadius";
        private const string SoftFalloffKey = Prefix + "SoftFalloff";
        private const string SoftConnectedKey = Prefix + "SoftConnected";
        private const string CurlRadiusKey = Prefix + "CurlRadius";
        private const string CurlTurnsKey = Prefix + "CurlTurns";
        private const string DensityModeKey = Prefix + "DensityMode";
        private const string DensityDistanceKey = Prefix + "DensityDistance";
        private const string DensityAttemptsKey = Prefix + "DensityAttempts";
        private const string ResampleSegmentsKey = Prefix + "ResampleSegments";
    }

    public class HairGuideSculptSession : EditorTool
    {
        public override GUIContent toolbarIcon => _icon ??= new GUIContent(
            EditorGUIUtility.IconContent("EditCollider").image, "Sculpt the guide curves that shape the hair");

        private GUIContent _icon;
        private HairSurface _surface;
        private LinePrimitiveRenderer _owner;
        private HairGroup _selectionGroup;
        private readonly HairScalpRaycaster _raycaster = new();

        private readonly HashSet<int> _selectedGuides = new();
        private readonly HashSet<Vector2Int> _selectedPoints = new();
        [SerializeField] private List<int> _committedGuides = new();
        [SerializeField] private List<Vector2Int> _committedPoints = new();
        private int _activeGuide = -1;
        private bool _pointDragged;
        private bool _pointDragActive;
        private bool _extrudePending;
        private bool _dragExtruded;
        private Vector2Int _dragPoint;
        private Plane _dragPlane;
        private Vector3 _dragLastWorld;
        private bool _strokeActive;
        private bool _strokeDirty;
        private Plane _strokePlane;
        private Vector3 _lastStrokeWorld;
        private Vector2 _lastStrokeMouse;
        private Vector3 _brushWorld;
        private bool _hasBrushWorld;
        private bool _selectionDragActive;
        private int _selectionControlId;
        private Vector2 _selectionStart;
        private Vector2 _selectionCurrent;
        private Vector2 _lastPaintMouse;
        private Camera _sceneCamera;
        private float[] _occluderDepth;
        private int _occluderWidth;
        private int _occluderHeight;
        private bool _selectionToggle;
        private bool _selectionAdditive;
        private float _mirrorAxis;
        private int _strokeUndoGroup;
        private bool _strokeInvert;
        private float[][] _strokeFactors;
        private float[][] _dragFactors;
        private bool _densityAdding;
        private System.Random _densityRandom;
        private Vector3[] _densityCorners;

        private const float LineWidth = 2.5f;
        private const float PointSize = 0.006f;
        private const float PickDistance = 24f;
        private const float PointPickDistance = 12f;
        private const int MaxGuidePoints = 129;
        private const float ScrollToDiameter = 0.04f;
        private const float AddCursorSize = 0.08f;
        private const float NeighbourDistanceFloor = 1e-4f;
        private const float OcclusionRelativeBias = 0.002f;
        private const float OcclusionBias = 0.001f;
        private const int DensityProbeCount = 8;

        private static readonly Color GuideColor = new Color(0.45f, 0.85f, 1f, 0.75f);
        private static readonly Color SelectedColor = new Color(1f, 0.85f, 0.35f, 1f);
        private static readonly Color BrushColor = new Color(1f, 0.65f, 0.2f, 0.95f);
        private static readonly Color BrushFalloffColor = new Color(1f, 0.65f, 0.2f, 0.35f);
        private static readonly Color PointColor = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color RootPointColor = new Color(0.55f, 0.55f, 0.55f, 0.9f);
        private static readonly Color FrozenColor = new Color(0.7f, 0.5f, 1f, 0.9f);
        private static readonly Color RemoveColor = new Color(1f, 0.4f, 0.35f, 0.95f);
        private static readonly Color MirrorBrushColor = new Color(1f, 0.65f, 0.2f, 0.45f);

        private static readonly HairGuideSelectionMode[] SelectionToolOrder =
        {
            HairGuideSelectionMode.Click,
            HairGuideSelectionMode.Brush,
            HairGuideSelectionMode.Circle,
            HairGuideSelectionMode.Area,
        };

        private static readonly HairGuideEditMode[] EditToolOrder =
        {
            HairGuideEditMode.Points,
            HairGuideEditMode.Grab,
            HairGuideEditMode.Comb,
            HairGuideEditMode.Smooth,
            HairGuideEditMode.Length,
            HairGuideEditMode.Puff,
            HairGuideEditMode.Twist,
            HairGuideEditMode.Curl,
            HairGuideEditMode.Clump,
            HairGuideEditMode.Add,
            HairGuideEditMode.Density,
            HairGuideEditMode.Freeze,
        };

        private static HairGuideSculptSession _activeTool;
        private static HairGroup _configuredGroup;
        private static LinePrimitiveRenderer _configuredOwner;

        internal static bool HasSelection => _activeTool != null && _activeTool._selectedGuides.Count > 0;
        internal static int SelectionCount => _activeTool?._selectedGuides.Count ?? 0;
        internal static int SelectedPointCount => _activeTool?._selectedPoints.Count ?? 0;

        private static UnityEngine.Rendering.CompareFunction GuideZTest => HairGuideSculptSettings.XRay
            ? UnityEngine.Rendering.CompareFunction.Always
            : UnityEngine.Rendering.CompareFunction.LessEqual;

        internal static void ApplyDisplay()
        {
            bool editing = _activeTool != null && _configuredOwner != null;
            LineRenderingFeature.HideHairFromSceneViewDepth = editing && !HairGuideSculptSettings.XRay;
            if (_configuredOwner != null)
            {
                LinePrimitiveRenderer.SetEditorVisibility(_configuredOwner,
                                                          !editing || HairGuideSculptSettings.ShowHair);
                LineHairSimulation.SetEditorSimulation(_configuredOwner,
                                                       editing ? HairGuideSculptSettings.Simulate : null);
            }
            SceneView.RepaintAll();
        }

        internal static void SubdivideSelectedSegments()
        {
            HairGroup group = _activeTool != null ? _configuredGroup : null;
            if (group != null)
            {
                _activeTool.SubdivideSelection(group, group.Settings.Guides);
            }
        }

        internal static void DeleteSelectedGuidePoints()
        {
            HairGroup group = _activeTool != null ? _configuredGroup : null;
            if (group != null)
            {
                _activeTool.DeleteSelectedPoints(group, group.Settings.Guides);
            }
        }

        internal static void Configure(HairGroup group)
        {
            ClearConfiguration();
            _configuredGroup = group;
            _configuredOwner = group != null ? group.GetComponentInParent<LinePrimitiveRenderer>() : null;
            EditorApplication.update += ValidateConfiguration;
        }

        internal static void SelectAllGuides()
        {
            HairGroup group = _activeTool != null ? _configuredGroup : null;
            if (group == null)
            {
                return;
            }

            _activeTool._selectedGuides.Clear();
            for (int i = 0; i < group.Settings.Guides.Count; i++)
            {
                _activeTool._selectedGuides.Add(i);
            }
            _activeTool._activeGuide = _activeTool.FindAnySelected();
            _activeTool.CommitSelection("Select All Hair Guides");
            SceneView.RepaintAll();
        }

        internal static void ClearGuideSelection()
        {
            if (_activeTool == null)
            {
                return;
            }

            _activeTool._selectedGuides.Clear();
            _activeTool._selectedPoints.Clear();
            _activeTool._activeGuide = -1;
            _activeTool.CommitSelection("Clear Hair Guide Selection");
            SceneView.RepaintAll();
        }

        internal static void InvertGuideSelection()
        {
            HairGroup group = _activeTool != null ? _configuredGroup : null;
            if (group == null)
            {
                return;
            }

            for (int i = 0; i < group.Settings.Guides.Count; i++)
            {
                if (!_activeTool._selectedGuides.Add(i))
                {
                    _activeTool._selectedGuides.Remove(i);
                }
            }
            _activeTool._activeGuide = _activeTool.FindAnySelected();
            _activeTool.CommitSelection("Invert Hair Guide Selection");
            SceneView.RepaintAll();
        }

        internal static List<HairGroup> TransferTargets()
        {
            var targets = new List<HairGroup>();
            if (_activeTool == null || _configuredOwner == null)
            {
                return targets;
            }

            foreach (HairGroup group in _configuredOwner.Groups)
            {
                if (group != null && group != _configuredGroup && group.Mode == HairGroupMode.Custom)
                {
                    targets.Add(group);
                }
            }

            return targets;
        }

        internal static void ResampleSelectedGuides(int segments)
        {
            HairGroup group = _activeTool != null ? _configuredGroup : null;
            if (group != null)
            {
                _activeTool.ResampleSelection(group, group.Settings.Guides, segments);
            }
        }

        internal static void TransferSelectedGuides(HairGroup target, bool copy)
        {
            HairGroup group = _activeTool != null ? _configuredGroup : null;
            if (group != null)
            {
                _activeTool.TransferSelection(group, target, copy);
            }
        }

        internal static void FreezeSelectedGuides(bool freeze)
        {
            HairGroup group = _activeTool != null ? _configuredGroup : null;
            if (group != null)
            {
                _activeTool.FreezeSelection(group, group.Settings.Guides, freeze);
            }
        }

        internal static void UnfreezeAllGuides()
        {
            HairGroup group = _activeTool != null ? _configuredGroup : null;
            if (group == null)
            {
                return;
            }

            Undo.RecordObject(group, "Unfreeze All Hair Guides");
            foreach (HairGuide guide in group.Settings.Guides.Guides)
            {
                guide?.ClearFreeze();
            }
            EditorUtility.SetDirty(group);
            SceneView.RepaintAll();
        }

        public override void OnActivated()
        {
            _activeTool = this;
            HairGuideSculptSettings.ToolMode = HairGuideToolMode.Selection;
            WriteSelection();
            Undo.undoRedoPerformed -= RestoreSelection;
            Undo.undoRedoPerformed += RestoreSelection;
            if (!TryGetContext(out HairGroup group, out LinePrimitiveRenderer owner))
            {
                CloseEditing();
                return;
            }

            _selectionGroup = group;
            _owner = owner;
            ApplyDisplay();
        }

        public override void OnWillBeDeactivated()
        {
            HairGroup group = _configuredGroup;
            if (_strokeDirty && group != null)
            {
                Commit(group);
            }

            ReleaseSurface();
            _raycaster.Release();
            LineRenderingFeature.HideHairFromSceneViewDepth = false;
            if (_configuredOwner != null)
            {
                LinePrimitiveRenderer.SetEditorVisibility(_configuredOwner, true);
                LineHairSimulation.SetEditorSimulation(_configuredOwner, null);
            }
            Undo.undoRedoPerformed -= RestoreSelection;
            _selectedGuides.Clear();
            _selectedPoints.Clear();
            WriteSelection();
            _activeGuide = -1;
            _selectionGroup = null;
            _strokeActive = false;
            _selectionDragActive = false;
            _pointDragActive = false;
            if (_activeTool == this)
            {
                _activeTool = null;
            }
            _owner = null;
            ClearConfiguration();
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView sceneView)
            {
                return;
            }

            if (!TryGetContext(out HairGroup group, out LinePrimitiveRenderer owner))
            {
                CloseEditing();
                return;
            }

            _owner = owner;
            _activeTool = this;
            _sceneCamera = sceneView.camera;

            if (_selectionGroup != group)
            {
                _selectionGroup = group;
                _selectedGuides.Clear();
                _selectedPoints.Clear();
                WriteSelection();
                _activeGuide = -1;
            }

            HairGuideSet guides = group.Settings.Guides;
            if (!EnsureSurface())
            {
                _selectedGuides.Clear();
                _activeGuide = -1;
                CloseEditing();
                return;
            }

            PruneSelection(guides);
            Event current = Event.current;

            int sceneSelectionBlocker = GUIUtility.GetControlID(FocusType.Passive);
            if (current.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(sceneSelectionBlocker);
            }

            HandleKeyboard(group, guides, sceneView, current);

            if (current.type == EventType.Repaint)
            {
                DrawGuides(guides);
            }

            if (HairGuideSculptSettings.ToolMode == HairGuideToolMode.Selection)
            {
                HandleSelection(guides, current);
                if (current.type == EventType.Repaint)
                {
                    DrawSelectionMarquee(HairGuideSculptSettings.SelectionMode == HairGuideSelectionMode.Circle);
                }
                return;
            }

            if (current.type == EventType.MouseMove)
            {
                sceneView.Repaint();
            }

            switch (HairGuideSculptSettings.Mode)
            {
                case HairGuideEditMode.Add:
                    HandleAdd(group, guides, current);
                    return;
                case HairGuideEditMode.Density:
                    HandleDensity(group, guides, current);
                    return;
                case HairGuideEditMode.Freeze:
                    HandleBrush(group, guides, sceneView, current);
                    return;
            }

            if (_selectedGuides.Count == 0)
            {
                return;
            }

            if (HairGuideSculptSettings.Mode == HairGuideEditMode.Points)
            {
                HandlePoints(group, guides, sceneView, current);
            }
            else
            {
                HandleBrush(group, guides, sceneView, current);
            }
        }

        private void DrawGuides(HairGuideSet guides)
        {
            Handles.zTest = GuideZTest;
            Vector3[][] world = WorldPoints(guides);
            float[][] soft = HairGuideSculptSettings.SoftSelection && _selectedGuides.Count > 0
                ? SoftWeights(guides, world)
                : null;

            for (int g = 0; g < guides.Count; g++)
            {
                HairGuide guide = guides.Guides[g];
                bool selected = _selectedGuides.Contains(g);
                float[] weights = soft?[g];
                if (!guide.HasFreeze && weights == null)
                {
                    Handles.color = selected ? SelectedColor : GuideColor;
                    Handles.DrawAAPolyLine(LineWidth, world[g]);
                    continue;
                }

                var colors = new Color[world[g].Length];
                for (int k = 0; k < colors.Length; k++)
                {
                    Color color = weights != null ? Color.Lerp(GuideColor, SelectedColor, weights[k])
                        : selected ? SelectedColor
                        : GuideColor;
                    colors[k] = Color.Lerp(color, FrozenColor, guide.FreezeAt(k));
                }

                Handles.color = Color.white;
                Handles.DrawAAPolyLine(LineWidth, colors, world[g]);
            }
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        }

        private bool PointDomain => HairGuideSculptSettings.ToolMode == HairGuideToolMode.Edit &&
                                    HairGuideSculptSettings.Mode == HairGuideEditMode.Points &&
                                    _selectedPoints.Count > 0;

        private float[][] SoftWeights(HairGuideSet guides, Vector3[][] world)
        {
            bool pointDomain = PointDomain;
            var seeds = new bool[guides.Count][];
            for (int g = 0; g < guides.Count; g++)
            {
                if (!_selectedGuides.Contains(g))
                {
                    continue;
                }

                seeds[g] = new bool[world[g].Length];
                for (int k = 0; k < seeds[g].Length; k++)
                {
                    seeds[g][k] = !pointDomain || _selectedPoints.Contains(new Vector2Int(g, k));
                }
            }

            return HairGuideSoftSelection.Weights(world, seeds, HairGuideSculptSettings.SoftRadius,
                                                  HairGuideSculptSettings.SoftFalloff,
                                                  HairGuideSculptSettings.SoftConnected);
        }

        private float[][] SelectionWeights(HairGuideSet guides)
        {
            if (HairGuideSculptSettings.SoftSelection)
            {
                return SoftWeights(guides, WorldPoints(guides));
            }

            bool pointDomain = PointDomain;
            var weights = new float[guides.Count][];
            foreach (int g in _selectedGuides)
            {
                weights[g] = new float[guides.Guides[g].PointCount];
                for (int k = 0; k < weights[g].Length; k++)
                {
                    weights[g][k] = !pointDomain || _selectedPoints.Contains(new Vector2Int(g, k)) ? 1f : 0f;
                }
            }

            return weights;
        }

        private float[][] EditFactors(HairGuideSet guides)
        {
            float[][] factors = SelectionWeights(guides);
            for (int g = 0; g < factors.Length; g++)
            {
                HairGuide guide = guides.Guides[g];
                if (factors[g] == null || !guide.HasFreeze)
                {
                    continue;
                }

                for (int k = 0; k < factors[g].Length; k++)
                {
                    factors[g][k] *= 1f - guide.FreezeAt(k);
                }
            }

            return factors;
        }

        private static void HoldFrozen(HairGuide guide, Vector3[] result, Vector3[] before)
        {
            if (!guide.HasFreeze)
            {
                return;
            }

            for (int k = 1; k < result.Length && k < before.Length; k++)
            {
                result[k] = Vector3.Lerp(result[k], before[k], guide.FreezeAt(k));
            }
        }

        private void HandleKeyboard(HairGroup group, HairGuideSet guides, SceneView sceneView, Event current)
        {
            if (current.type != EventType.KeyDown || GUIUtility.hotControl != 0)
            {
                return;
            }

            bool pointsTool = HairGuideSculptSettings.ToolMode == HairGuideToolMode.Edit &&
                              HairGuideSculptSettings.Mode == HairGuideEditMode.Points;

            if (current.alt && current.keyCode == KeyCode.S && pointsTool && _selectedPoints.Count > 0)
            {
                SubdivideSelection(group, guides);
                current.Use();
                return;
            }

            if (current.alt || current.control)
            {
                return;
            }

            if (current.keyCode == KeyCode.Tab)
            {
                HairGuideSculptSettings.ToolMode = HairGuideSculptSettings.ToolMode == HairGuideToolMode.Selection
                    ? HairGuideToolMode.Edit
                    : HairGuideToolMode.Selection;
                current.Use();
                SceneView.RepaintAll();
                return;
            }

            int slot = current.keyCode == KeyCode.Alpha0 ? 9
                : current.keyCode >= KeyCode.Alpha1 && current.keyCode <= KeyCode.Alpha9
                    ? current.keyCode - KeyCode.Alpha1
                    : -1;

            if (HairGuideSculptSettings.ToolMode == HairGuideToolMode.Selection && slot >= 0 &&
                slot < SelectionToolOrder.Length)
            {
                HairGuideSculptSettings.SelectionMode = SelectionToolOrder[slot];
                current.Use();
                SceneView.RepaintAll();
                return;
            }

            if (HairGuideSculptSettings.ToolMode == HairGuideToolMode.Edit && slot >= 0 &&
                slot < EditToolOrder.Length)
            {
                HairGuideSculptSettings.Mode = EditToolOrder[slot];
                current.Use();
                SceneView.RepaintAll();
                return;
            }

            if ((current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace) &&
                pointsTool && _selectedPoints.Count > 0)
            {
                DeleteSelectedPoints(group, guides);
                current.Use();
                return;
            }

            if ((current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace) &&
                _selectedGuides.Count > 0)
            {
                RecordEdit(group, "Delete Hair Guide");
                var toRemove = new List<int>(_selectedGuides);
                toRemove.Sort((a, b) => b.CompareTo(a));
                foreach (int index in toRemove)
                {
                    if (index >= 0 && index < guides.Count)
                    {
                        guides.Guides.RemoveAt(index);
                    }
                }
                _selectedGuides.Clear();
                _selectedPoints.Clear();
                _activeGuide = -1;
                WriteSelection();
                Commit(group);
                current.Use();
                return;
            }

            if (current.keyCode == KeyCode.F && _selectedGuides.Count > 0)
            {
                Bounds bounds = default;
                bool hasBounds = false;
                foreach (int index in _selectedGuides)
                {
                    if (index < 0 || index >= guides.Count)
                    {
                        continue;
                    }
                    Vector3[] points = WorldPoints(guides.Guides[index]);
                    if (points.Length == 0)
                    {
                        continue;
                    }
                    if (!hasBounds)
                    {
                        bounds = new Bounds(points[0], Vector3.zero);
                        hasBounds = true;
                    }
                    for (int i = 0; i < points.Length; i++)
                    {
                        bounds.Encapsulate(points[i]);
                    }
                }

                if (hasBounds)
                {
                    bounds.Expand(Mathf.Max(0.05f, bounds.size.magnitude * 0.25f));
                    sceneView.Frame(bounds, false);
                }
                current.Use();
            }
        }

        private void HandleSelection(HairGuideSet guides, Event current)
        {
            HairGuideSelectionMode mode = HairGuideSculptSettings.SelectionMode;
            if (mode == HairGuideSelectionMode.Brush)
            {
                HandleBrushSelection(guides, current);
                return;
            }

            if (mode != HairGuideSelectionMode.Click)
            {
                HandleShapeSelection(guides, mode, current);
                return;
            }

            if (current.type != EventType.MouseDown || current.button != 0 || current.alt ||
                GUIUtility.hotControl != 0)
            {
                return;
            }

            CaptureOccluders();
            int nearest = FindNearestGuide(guides, current.mousePosition, false);
            int mirrored = HairGuideSculptSettings.MirrorX
                ? FindNearestGuide(guides, current.mousePosition, true)
                : -1;
            ReleaseOccluders();
            bool additive = current.shift;
            bool toggle = current.control || current.command;
            bool clickedUnselected = nearest >= 0 && !_selectedGuides.Contains(nearest);
            bool selectionMode = _selectedGuides.Count == 0 ||
                                  HairGuideSculptSettings.Mode == HairGuideEditMode.Points ||
                                  additive || toggle || clickedUnselected;

            if (!selectionMode)
            {
                return;
            }

            ApplyClickSelection(nearest, mirrored, additive, toggle);
            CommitSelection("Select Hair Guides");
            current.Use();
            SceneView.RepaintAll();
        }

        private static bool TryGetContext(out HairGroup group, out LinePrimitiveRenderer owner)
        {
            group = null;
            owner = null;

            if (ToolManager.activeToolType != typeof(HairGuideSculptSession) || _configuredGroup == null ||
                _configuredOwner == null)
            {
                return false;
            }

            if (_configuredGroup.Mode != HairGroupMode.Custom)
            {
                return false;
            }

            owner = _configuredOwner;
            if (owner.Source != LinePrimitiveRenderer.SourceMode.Procedural ||
                owner.Scalp.Mode != HairScalpMode.Mesh || owner.Scalp.Renderer == null)
            {
                owner = null;
                return false;
            }

            HairGuideSet guides = _configuredGroup.Settings.Guides;
            if (guides == null || guides.IsEmpty)
            {
                owner = null;
                return false;
            }

            group = _configuredGroup;
            return true;
        }

        private static void ValidateConfiguration()
        {
            if (ToolManager.activeToolType != typeof(HairGuideSculptSession))
            {
                ClearConfiguration();
                return;
            }

            if (_configuredGroup == null || _configuredOwner == null ||
                Selection.count != 1 || Selection.activeGameObject != _configuredGroup.gameObject)
            {
                CloseEditing();
            }
        }

        private static void CloseEditing()
        {
            EditorApplication.update -= ValidateConfiguration;
            if (ToolManager.activeToolType == typeof(HairGuideSculptSession))
            {
                EditorApplication.delayCall += RestoreActiveTool;
            }
            else
            {
                ClearConfiguration();
            }
        }

        private static void RestoreActiveTool()
        {
            if (ToolManager.activeToolType == typeof(HairGuideSculptSession))
            {
                ToolManager.RestorePreviousPersistentTool();
            }

            ClearConfiguration();
        }

        private static void ClearConfiguration()
        {
            EditorApplication.update -= ValidateConfiguration;
            _configuredGroup = null;
            _configuredOwner = null;
        }

        private void HandleShapeSelection(HairGuideSet guides, HairGuideSelectionMode mode, Event current)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            EventType eventType = current.GetTypeForControl(controlId);

            switch (eventType)
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.MouseDown when current.button == 0 && !current.alt &&
                                             GUIUtility.hotControl == 0:
                    _selectionControlId = controlId;
                    _selectionStart = current.mousePosition;
                    _selectionCurrent = _selectionStart;
                    _selectionAdditive = current.shift;
                    _selectionToggle = current.control || current.command;
                    _selectionDragActive = true;
                    GUIUtility.hotControl = controlId;
                    CaptureOccluders();
                    current.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseDrag when _selectionDragActive &&
                                             GUIUtility.hotControl == _selectionControlId:
                    _selectionCurrent = current.mousePosition;
                    current.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseUp when _selectionDragActive &&
                                           GUIUtility.hotControl == _selectionControlId:
                    _selectionCurrent = current.mousePosition;
                    GUIUtility.hotControl = 0;
                    _selectionDragActive = false;
                    ApplyShapeSelection(guides, mode);
                    ReleaseOccluders();
                    CommitSelection("Select Hair Guides");
                    current.Use();
                    SceneView.RepaintAll();
                    break;
            }
        }

        private void HandleBrushSelection(HairGuideSet guides, Event current)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            float radius = HairGuideSculptSettings.SelectionBrushRadius;

            if (current.type == EventType.MouseMove)
            {
                SceneView.RepaintAll();
            }

            switch (current.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.ScrollWheel when current.shift:
                    float scroll = current.delta.y != 0f ? current.delta.y : current.delta.x;
                    HairGuideSculptSettings.SelectionBrushRadius *= Mathf.Max(0.05f, 1f - scroll * ScrollToDiameter);
                    current.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseDown when current.button == 0 && !current.alt && GUIUtility.hotControl == 0:
                    GUIUtility.hotControl = controlId;
                    _lastPaintMouse = current.mousePosition;
                    if (!current.shift && !current.control && !current.command)
                    {
                        _selectedGuides.Clear();
                    }
                    CaptureOccluders();
                    PaintSelection(guides, current.mousePosition, radius, current.control || current.command);
                    current.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                    PaintSelection(guides, current.mousePosition, radius, current.control || current.command);
                    current.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    ReleaseOccluders();
                    CommitSelection("Select Hair Guides");
                    current.Use();
                    break;

                case EventType.Repaint:
                    Handles.BeginGUI();
                    Handles.color = new Color(0.25f, 0.7f, 1f, 0.95f);
                    DrawScreenCircle(current.mousePosition, radius);
                    Handles.EndGUI();
                    break;
            }
        }

        private void PaintSelection(HairGuideSet guides, Vector2 mouse, float radius, bool remove)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(_lastPaintMouse, mouse) / (radius * 0.5f)));
            HairScalpMirror mirror = ScalpMirror;
            bool mirrorX = HairGuideSculptSettings.MirrorX;

            for (int g = 0; g < guides.Count; g++)
            {
                Vector3[] world = WorldPoints(guides.Guides[g]);
                Vector2[] points = ScreenPoints(world);
                bool[] visible = Visibility(world);
                Vector3[] mirroredWorld = mirrorX ? Mirrored(world, mirror) : null;
                Vector2[] mirroredPoints = mirrorX ? ScreenPoints(mirroredWorld) : null;
                bool[] mirroredVisible = mirrorX ? Visibility(mirroredWorld) : null;
                for (int step = 1; step <= steps; step++)
                {
                    Vector2 center = Vector2.Lerp(_lastPaintMouse, mouse, step / (float)steps);
                    if (!PolylineInCircle(points, visible, center, radius) &&
                        !(mirrorX && PolylineInCircle(mirroredPoints, mirroredVisible, center, radius)))
                    {
                        continue;
                    }

                    if (remove)
                    {
                        _selectedGuides.Remove(g);
                    }
                    else
                    {
                        _selectedGuides.Add(g);
                    }
                    break;
                }
            }

            _lastPaintMouse = mouse;
            _activeGuide = FindAnySelected();
            SceneView.RepaintAll();
        }

        private static Vector2[] ScreenPoints(Vector3[] worldPoints)
        {
            var points = new Vector2[worldPoints.Length];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = HandleUtility.WorldToGUIPoint(worldPoints[i]);
            }
            return points;
        }

        private static bool PolylineInCircle(Vector2[] points, bool[] visible, Vector2 center, float radius)
        {
            float radiusSquared = radius * radius;
            for (int i = 0; i < points.Length; i++)
            {
                bool shown = visible == null || visible[i];
                if (shown && (points[i] - center).sqrMagnitude <= radiusSquared)
                {
                    return true;
                }
                if (i > 0 && (shown || visible[i - 1]) &&
                    DistanceToSegmentSquared(center, points[i - 1], points[i]) <= radiusSquared)
                {
                    return true;
                }
            }
            return false;
        }

        private void CaptureOccluders()
        {
            _occluderDepth = null;
            RenderTexture depth = HairGuideSculptSettings.XRay
                ? null
                : LineRenderingFeature.GetSceneViewDepthWithoutHair(_sceneCamera);
            if (depth == null)
            {
                return;
            }

            UnityEngine.Rendering.AsyncGPUReadbackRequest request = UnityEngine.Rendering.AsyncGPUReadback.Request(depth);
            request.WaitForCompletion();
            if (request.hasError)
            {
                return;
            }

            _occluderDepth = request.GetData<float>().ToArray();
            _occluderWidth = depth.width;
            _occluderHeight = depth.height;
        }

        private void ReleaseOccluders()
        {
            _occluderDepth = null;
        }

        private bool[] Visibility(Vector3[] worldPoints)
        {
            if (_occluderDepth == null || _sceneCamera == null)
            {
                return null;
            }

            var visible = new bool[worldPoints.Length];
            for (int i = 0; i < visible.Length; i++)
            {
                visible[i] = !IsOccluded(worldPoints[i]);
            }
            return visible;
        }

        private bool IsOccluded(Vector3 world)
        {
            Vector3 screen = _sceneCamera.WorldToScreenPoint(world);
            if (screen.z <= 0f)
            {
                return true;
            }

            int x = Mathf.FloorToInt(screen.x * _occluderWidth / _sceneCamera.pixelWidth);
            int y = Mathf.FloorToInt(screen.y * _occluderHeight / _sceneCamera.pixelHeight);
            if (x < 0 || y < 0 || x >= _occluderWidth || y >= _occluderHeight)
            {
                return false;
            }

            float sceneDepth = EyeDepth(_occluderDepth[y * _occluderWidth + x]);
            return screen.z > sceneDepth * (1f + OcclusionRelativeBias) + OcclusionBias;
        }

        private float EyeDepth(float raw)
        {
            float near = _sceneCamera.nearClipPlane;
            float far = _sceneCamera.farClipPlane;
            if (SystemInfo.usesReversedZBuffer)
            {
                raw = 1f - raw;
            }

            return _sceneCamera.orthographic
                ? near + (far - near) * raw
                : near * far / (far - raw * (far - near));
        }

        private static void DrawScreenCircle(Vector2 center, float radius)
        {
            var circle = new Vector3[65];
            for (int i = 0; i < circle.Length; i++)
            {
                float angle = i / 64f * Mathf.PI * 2f;
                circle[i] = new Vector3(center.x + Mathf.Cos(angle) * radius,
                                        center.y + Mathf.Sin(angle) * radius);
            }
            Handles.DrawAAPolyLine(2f, circle);
        }

        private int FindNearestGuide(HairGuideSet guides, Vector2 mouse, bool mirrored)
        {
            int nearest = -1;
            float nearestDistance = PickDistance;
            HairScalpMirror mirror = ScalpMirror;

            for (int g = 0; g < guides.Count; g++)
            {
                Vector3[] points = WorldPoints(guides.Guides[g]);
                if (mirrored)
                {
                    points = Mirrored(points, mirror);
                }
                bool[] visible = Visibility(points);
                for (int k = 1; k < points.Length; k++)
                {
                    if (visible != null && !visible[k - 1] && !visible[k])
                    {
                        continue;
                    }

                    float distance = HandleUtility.DistanceToLine(points[k - 1], points[k]);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = g;
                    }
                }
            }

            return nearest;
        }

        private void ApplyClickSelection(int nearest, int mirrored, bool additive, bool toggle)
        {
            if (!additive && !toggle)
            {
                _selectedGuides.Clear();
                _activeGuide = -1;
            }

            foreach (int hit in new[] { nearest, mirrored == nearest ? -1 : mirrored })
            {
                if (hit < 0)
                {
                    continue;
                }

                if (toggle && !_selectedGuides.Add(hit))
                {
                    _selectedGuides.Remove(hit);
                }
                else
                {
                    _selectedGuides.Add(hit);
                }
            }

            _activeGuide = nearest >= 0 && _selectedGuides.Contains(nearest) ? nearest : FindAnySelected();
        }

        private void ApplyShapeSelection(HairGuideSet guides, HairGuideSelectionMode mode)
        {
            if (!_selectionAdditive && !_selectionToggle)
            {
                _selectedGuides.Clear();
            }

            HairScalpMirror mirror = ScalpMirror;
            for (int g = 0; g < guides.Count; g++)
            {
                Vector3[] world = WorldPoints(guides.Guides[g]);
                if (!GuideMatchesSelection(world, mode) &&
                    !(HairGuideSculptSettings.MirrorX && GuideMatchesSelection(Mirrored(world, mirror), mode)))
                {
                    continue;
                }

                if (_selectionToggle)
                {
                    if (!_selectedGuides.Add(g))
                    {
                        _selectedGuides.Remove(g);
                    }
                }
                else
                {
                    _selectedGuides.Add(g);
                }
            }

            _activeGuide = FindAnySelected();
        }

        private bool GuideMatchesSelection(Vector3[] worldPoints, HairGuideSelectionMode mode)
        {
            if (worldPoints == null || worldPoints.Length == 0)
            {
                return false;
            }

            Vector2[] points = ScreenPoints(worldPoints);
            bool[] visible = Visibility(worldPoints);

            if (mode == HairGuideSelectionMode.Circle)
            {
                return PolylineInCircle(points, visible, _selectionStart,
                                        Vector2.Distance(_selectionStart, _selectionCurrent));
            }

            Rect rect = MakeSelectionRect(_selectionStart, _selectionCurrent);
            for (int i = 0; i < points.Length; i++)
            {
                bool shown = visible == null || visible[i];
                if (shown && rect.Contains(points[i]))
                {
                    return true;
                }
                if (i > 0 && (shown || visible[i - 1]) && SegmentIntersectsRect(points[i - 1], points[i], rect))
                {
                    return true;
                }
            }

            return false;
        }

        private static float DistanceToSegmentSquared(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 1e-6f)
            {
                return (point - start).sqrMagnitude;
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return (point - (start + segment * t)).sqrMagnitude;
        }

        private static bool SegmentIntersectsRect(Vector2 start, Vector2 end, Rect rect)
        {
            if (rect.Contains(start) || rect.Contains(end))
            {
                return true;
            }

            Vector2 topLeft = new(rect.xMin, rect.yMin);
            Vector2 topRight = new(rect.xMax, rect.yMin);
            Vector2 bottomRight = new(rect.xMax, rect.yMax);
            Vector2 bottomLeft = new(rect.xMin, rect.yMax);
            return SegmentsIntersect(start, end, topLeft, topRight) ||
                   SegmentsIntersect(start, end, topRight, bottomRight) ||
                   SegmentsIntersect(start, end, bottomRight, bottomLeft) ||
                   SegmentsIntersect(start, end, bottomLeft, topLeft);
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross(b - a, c - a);
            float abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c);
            float cdB = Cross(d - c, b - c);
            return ((abC >= 0f && abD <= 0f) || (abC <= 0f && abD >= 0f)) &&
                   ((cdA >= 0f && cdB <= 0f) || (cdA <= 0f && cdB >= 0f));
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static Rect MakeSelectionRect(Vector2 start, Vector2 end)
        {
            return Rect.MinMaxRect(Mathf.Min(start.x, end.x), Mathf.Min(start.y, end.y),
                                   Mathf.Max(start.x, end.x), Mathf.Max(start.y, end.y));
        }

        private void DrawSelectionMarquee(bool round)
        {
            if (!_selectionDragActive)
            {
                return;
            }

            Handles.BeginGUI();
            Color previous = Handles.color;
            Handles.color = new Color(0.25f, 0.7f, 1f, 0.95f);

            if (!round)
            {
                Rect rect = MakeSelectionRect(_selectionStart, _selectionCurrent);
                EditorGUI.DrawRect(rect, new Color(0.25f, 0.7f, 1f, 0.12f));
                Handles.DrawAAPolyLine(2f,
                    new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin),
                    new Vector3(rect.xMax, rect.yMax), new Vector3(rect.xMin, rect.yMax),
                    new Vector3(rect.xMin, rect.yMin));
            }
            else
            {
                DrawScreenCircle(_selectionStart, Vector2.Distance(_selectionStart, _selectionCurrent));
            }

            Handles.color = previous;
            Handles.EndGUI();
        }

        private void HandlePoints(HairGroup group, HairGuideSet guides, SceneView sceneView, Event current)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Vector2 mouse = current.mousePosition;

            switch (current.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.MouseDown when current.button == 0 && !current.alt && GUIUtility.hotControl == 0:
                {
                    GUIUtility.hotControl = controlId;
                    bool toggle = current.control || current.command;
                    CaptureOccluders();

                    if (FindNearestPoint(guides, mouse, out Vector2Int point))
                    {
                        ApplyPointClick(point, current.shift, toggle);
                        BeginPointDrag(guides, point, sceneView.camera, mouse);
                        _extrudePending = current.shift && point.y > 0 &&
                                          point.y == guides.Guides[point.x].PointCount - 1;
                    }
                    else if (toggle && FindNearestSegment(guides, out int guideIndex, out int segment, out float t))
                    {
                        InsertPoint(group, guides, guideIndex, segment, t);
                    }
                    else
                    {
                        _selectionStart = mouse;
                        _selectionCurrent = mouse;
                        _selectionAdditive = current.shift;
                        _selectionToggle = toggle;
                        _selectionDragActive = true;
                    }

                    current.Use();
                    SceneView.RepaintAll();
                    break;
                }

                case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                    if (_pointDragActive)
                    {
                        Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
                        if (_dragPlane.Raycast(ray, out float distance))
                        {
                            Vector3 world = ray.GetPoint(distance);
                            DragSelectedPoint(group, guides, world - _dragLastWorld);
                            _dragLastWorld = world;
                        }
                    }
                    else if (_selectionDragActive)
                    {
                        _selectionCurrent = mouse;
                    }

                    current.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;

                    if (_selectionDragActive)
                    {
                        _selectionDragActive = false;
                        _selectionCurrent = mouse;
                        ApplyPointMarquee(guides);
                    }
                    ReleaseOccluders();

                    if (_pointDragActive)
                    {
                        _pointDragActive = false;
                        _extrudePending = false;
                        if (_pointDragged)
                        {
                            _pointDragged = false;
                            Commit(group);
                        }
                    }

                    CommitSelection("Select Hair Guide Points");

                    current.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.Repaint:
                    DrawPoints(guides, current);
                    DrawSelectionMarquee(false);
                    break;
            }
        }

        private void BeginPointDrag(HairGuideSet guides, Vector2Int point, Camera camera, Vector2 mouse)
        {
            Vector3 world = WorldPoints(guides.Guides[point.x])[point.y];
            Vector3 normal = camera != null ? camera.transform.forward : Vector3.forward;
            _dragPlane = new Plane(normal, world);
            Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
            _dragLastWorld = _dragPlane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : world;
            _dragPoint = point;
            _dragExtruded = false;
            _pointDragged = false;
            _pointDragActive = true;
            _dragFactors = HairGuideSculptSettings.SoftSelection ? EditFactors(guides) : null;
        }

        private void DragSelectedPoint(HairGroup group, HairGuideSet guides, Vector3 worldDelta)
        {
            if (_dragPoint.x >= guides.Count || worldDelta.sqrMagnitude < 1e-14f)
            {
                return;
            }

            HairGuide guide = guides.Guides[_dragPoint.x];

            if (!_pointDragged)
            {
                Undo.RecordObject(group, _extrudePending ? "Extrude Hair Guide" : "Edit Hair Guide");
                _pointDragged = true;
            }

            if (_extrudePending)
            {
                _extrudePending = false;
                if (guide.PointCount < MaxGuidePoints)
                {
                    Vector3[] extruded = ReadPoints(guide);
                    System.Array.Resize(ref extruded, extruded.Length + 1);
                    extruded[^1] = extruded[^2];
                    guide.SetPoints(extruded);
                    _dragPoint = new Vector2Int(_dragPoint.x, extruded.Length - 1);
                    _selectedPoints.Clear();
                    _selectedPoints.Add(_dragPoint);
                    _dragExtruded = true;
                }
            }

            if (_dragPoint.y <= 0 || _dragPoint.y >= guide.PointCount)
            {
                return;
            }

            if (_dragFactors != null && !_dragExtruded)
            {
                DragSoftSelection(guides, worldDelta);
                return;
            }

            HairRootFrame frame = WorldFrame(guide);
            Vector3 bitangent = frame.Bitangent;
            Vector3[] before = ReadPoints(guide);
            DragFrom(guide, frame, bitangent, _dragPoint.y, worldDelta);

            Vector3[] changed = ReadPoints(guide);
            if (HairGuideSculptSettings.PreserveLength && !_dragExtruded)
            {
                PreserveSegmentLengths(changed, SegmentLengths(before), before);
            }
            HoldFrozen(guide, changed, before);
            WritePoints(guide, changed);
        }

        private void DragSoftSelection(HairGuideSet guides, Vector3 worldDelta)
        {
            for (int g = 0; g < guides.Count && g < _dragFactors.Length; g++)
            {
                float[] factors = _dragFactors[g];
                HairGuide guide = guides.Guides[g];
                if (factors == null || factors.Length != guide.PointCount)
                {
                    continue;
                }

                HairRootFrame frame = WorldFrame(guide);
                Vector3 localDelta = ToLocalVector(frame, frame.Bitangent, worldDelta);
                Vector3[] before = ReadPoints(guide);
                Vector3[] result = (Vector3[])before.Clone();
                for (int k = 1; k < result.Length; k++)
                {
                    result[k] += localDelta * factors[k];
                }

                if (HairGuideSculptSettings.PreserveLength)
                {
                    PreserveSegmentLengths(result, SegmentLengths(before), before);
                }
                HoldFrozen(guide, result, before);
                WritePoints(guide, result);
            }
        }

        private void ApplyPointClick(Vector2Int point, bool additive, bool toggle)
        {
            if (toggle)
            {
                if (!_selectedPoints.Add(point))
                {
                    _selectedPoints.Remove(point);
                }
            }
            else if (additive)
            {
                _selectedPoints.Add(point);
            }
            else if (!_selectedPoints.Contains(point))
            {
                _selectedPoints.Clear();
                _selectedPoints.Add(point);
            }
        }

        private void ApplyPointMarquee(HairGuideSet guides)
        {
            if (!_selectionAdditive && !_selectionToggle)
            {
                _selectedPoints.Clear();
            }

            Rect rect = MakeSelectionRect(_selectionStart, _selectionCurrent);
            foreach (int g in _selectedGuides)
            {
                Vector3[] points = WorldPoints(guides.Guides[g]);
                bool[] visible = Visibility(points);
                for (int k = 0; k < points.Length; k++)
                {
                    if ((visible != null && !visible[k]) || !rect.Contains(HandleUtility.WorldToGUIPoint(points[k])))
                    {
                        continue;
                    }

                    var point = new Vector2Int(g, k);
                    if (_selectionToggle && !_selectedPoints.Add(point))
                    {
                        _selectedPoints.Remove(point);
                    }
                    else
                    {
                        _selectedPoints.Add(point);
                    }
                }
            }
        }

        private bool FindNearestPoint(HairGuideSet guides, Vector2 mouse, out Vector2Int nearest)
        {
            nearest = default;
            float nearestDistance = PointPickDistance;
            bool found = false;

            foreach (int g in _selectedGuides)
            {
                Vector3[] points = WorldPoints(guides.Guides[g]);
                bool[] visible = Visibility(points);
                for (int k = 0; k < points.Length; k++)
                {
                    if (visible != null && !visible[k])
                    {
                        continue;
                    }

                    float distance = Vector2.Distance(HandleUtility.WorldToGUIPoint(points[k]), mouse);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = new Vector2Int(g, k);
                        found = true;
                    }
                }
            }

            return found;
        }

        private bool FindNearestSegment(HairGuideSet guides, out int guideIndex, out int segment, out float t)
        {
            guideIndex = -1;
            segment = -1;
            t = 0f;
            float nearestDistance = PickDistance;
            Vector3 start = default;
            Vector3 end = default;

            foreach (int g in _selectedGuides)
            {
                Vector3[] points = WorldPoints(guides.Guides[g]);
                bool[] visible = Visibility(points);
                for (int k = 1; k < points.Length; k++)
                {
                    if (visible != null && !visible[k - 1] && !visible[k])
                    {
                        continue;
                    }

                    float distance = HandleUtility.DistanceToLine(points[k - 1], points[k]);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        guideIndex = g;
                        segment = k;
                        start = points[k - 1];
                        end = points[k];
                    }
                }
            }

            if (guideIndex < 0)
            {
                return false;
            }

            Vector3 closest = HandleUtility.ClosestPointToPolyLine(start, end);
            float length = Vector3.Distance(start, end);
            t = length > 1e-8f ? Mathf.Clamp01(Vector3.Distance(start, closest) / length) : 0.5f;
            return true;
        }

        private void InsertPoint(HairGroup group, HairGuideSet guides, int guideIndex, int segment, float t)
        {
            HairGuide guide = guides.Guides[guideIndex];
            if (guide.PointCount >= MaxGuidePoints)
            {
                return;
            }

            RecordEdit(group, "Split Hair Guide Segment");
            var points = new List<Vector3>(ReadPoints(guide));
            points.Insert(segment, Vector3.Lerp(points[segment - 1], points[segment], t));
            guide.SetPoints(points.ToArray());

            _selectedPoints.Clear();
            _selectedPoints.Add(new Vector2Int(guideIndex, segment));
            WriteSelection();
            Commit(group);
        }

        private void SubdivideSelection(HairGroup group, HairGuideSet guides)
        {
            RecordEdit(group, "Subdivide Hair Guides");
            var selection = new HashSet<Vector2Int>();
            bool changed = false;

            foreach (int g in _selectedGuides)
            {
                HairGuide guide = guides.Guides[g];
                Vector3[] points = ReadPoints(guide);
                var result = new List<Vector3>(points.Length * 2) { points[0] };
                if (_selectedPoints.Contains(new Vector2Int(g, 0)))
                {
                    selection.Add(new Vector2Int(g, 0));
                }

                for (int k = 1; k < points.Length; k++)
                {
                    bool selected = _selectedPoints.Contains(new Vector2Int(g, k));
                    bool room = result.Count + 1 + (points.Length - k) <= MaxGuidePoints;
                    if (selected && room && _selectedPoints.Contains(new Vector2Int(g, k - 1)))
                    {
                        selection.Add(new Vector2Int(g, result.Count));
                        result.Add((points[k - 1] + points[k]) * 0.5f);
                        changed = true;
                    }

                    if (selected)
                    {
                        selection.Add(new Vector2Int(g, result.Count));
                    }
                    result.Add(points[k]);
                }

                guide.SetPoints(result.ToArray());
            }

            _selectedPoints.Clear();
            _selectedPoints.UnionWith(selection);
            WriteSelection();
            if (changed)
            {
                Commit(group);
            }
        }

        private void DeleteSelectedPoints(HairGroup group, HairGuideSet guides)
        {
            RecordEdit(group, "Delete Hair Guide Points");
            bool changed = false;

            foreach (int g in _selectedGuides)
            {
                HairGuide guide = guides.Guides[g];
                var points = new List<Vector3>(ReadPoints(guide));
                for (int k = points.Count - 1; k > 0 && points.Count > 2; k--)
                {
                    if (_selectedPoints.Contains(new Vector2Int(g, k)))
                    {
                        points.RemoveAt(k);
                        changed = true;
                    }
                }

                guide.SetPoints(points.ToArray());
            }

            _selectedPoints.Clear();
            WriteSelection();
            if (changed)
            {
                Commit(group);
            }
        }

        private void DrawPoints(HairGuideSet guides, Event current)
        {
            bool idle = !_pointDragActive && !_selectionDragActive;
            bool hovering = FindNearestPoint(guides, current.mousePosition, out Vector2Int hovered) && idle;
            Handles.zTest = GuideZTest;

            foreach (int g in _selectedGuides)
            {
                HairGuide guide = guides.Guides[g];
                Vector3[] points = WorldPoints(guide);
                if (points.Length == 0)
                {
                    continue;
                }

                float size = HandleUtility.GetHandleSize(points[0]) * PointSize * 20f;
                for (int k = 0; k < points.Length; k++)
                {
                    var point = new Vector2Int(g, k);
                    Handles.color = hovering && hovered == point ? Handles.preselectionColor
                        : _selectedPoints.Contains(point) ? BrushColor
                        : k == 0 ? RootPointColor
                        : PointColor;
                    Handles.SphereHandleCap(0, points[k], Quaternion.identity, size, EventType.Repaint);
                }
            }

            if (idle && !hovering && (current.control || current.command) &&
                FindNearestSegment(guides, out int guideIndex, out int segment, out float t))
            {
                Vector3[] points = WorldPoints(guides.Guides[guideIndex]);
                Vector3 split = Vector3.Lerp(points[segment - 1], points[segment], t);
                Handles.color = Handles.preselectionColor;
                Handles.SphereHandleCap(0, split, Quaternion.identity,
                                        HandleUtility.GetHandleSize(split) * PointSize * 20f, EventType.Repaint);
            }
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        }

        private void HandleBrush(HairGroup group, HairGuideSet guides, SceneView sceneView, Event current)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            bool freeze = HairGuideSculptSettings.Mode == HairGuideEditMode.Freeze;
            if (!_strokeActive)
            {
                _hasBrushWorld = FindBrushHover(guides, current.mousePosition, sceneView.camera, freeze,
                                                out _brushWorld);
            }
            else if (PointOnStrokePlane(current.mousePosition, out Vector3 point))
            {
                _brushWorld = point;
                _hasBrushWorld = true;
            }

            switch (current.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.ScrollWheel when current.shift:
                    float scroll = current.delta.y != 0f ? current.delta.y : current.delta.x;
                    HairGuideSculptSettings.Diameter *= Mathf.Max(0.05f, 1f - scroll * ScrollToDiameter);
                    current.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseDown when current.button == 0 && !current.alt && _hasBrushWorld:
                    GUIUtility.hotControl = controlId;
                    _strokeUndoGroup = Undo.GetCurrentGroup();
                    Undo.RecordObject(group, freeze ? "Freeze Hair Guides" : "Sculpt Hair Guides");
                    _strokeInvert = current.control || current.command;
                    _strokeFactors = freeze ? null : EditFactors(guides);
                    if (freeze)
                    {
                        CaptureOccluders();
                    }
                    _strokePlane = new Plane(sceneView.camera.transform.forward, _brushWorld);
                    _lastStrokeWorld = _brushWorld;
                    PointOnStrokePlane(current.mousePosition, out _lastStrokeWorld);
                    _lastStrokeMouse = current.mousePosition;
                    _strokeActive = true;
                    _strokeDirty = false;
                    current.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == controlId && _strokeActive:
                    if (PointOnStrokePlane(current.mousePosition, out Vector3 world))
                    {
                        Vector3 worldDelta = world - _lastStrokeWorld;
                        Vector2 mouseDelta = current.mousePosition - _lastStrokeMouse;
                        _strokeDirty |= ApplyBrush(guides, current.mousePosition, mouseDelta,
                                                   worldDelta, world, sceneView.camera);
                        _lastStrokeWorld = world;
                        _lastStrokeMouse = current.mousePosition;
                        _brushWorld = world;
                    }
                    current.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    _strokeActive = false;
                    ReleaseOccluders();
                    if (_strokeDirty && freeze)
                    {
                        EditorUtility.SetDirty(group);
                    }
                    else if (_strokeDirty)
                    {
                        Commit(group);
                    }
                    _strokeDirty = false;
                    _strokeFactors = null;
                    Undo.CollapseUndoOperations(_strokeUndoGroup);
                    current.Use();
                    break;

                case EventType.Repaint when _hasBrushWorld:
                    DrawBrush(sceneView.camera, _brushWorld,
                              _strokeActive ? _strokeInvert : current.control || current.command);
                    break;
            }
        }

        private void HandleAdd(HairGroup group, HairGuideSet guides, Event current)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Renderer scalp = _owner.Scalp.Renderer;
            _raycaster.EnsureMesh(scalp, _owner.Scalp.Submesh);
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            bool hit = _raycaster.Raycast(scalp, ray, out Vector3 point, out Vector3 normal,
                                          out HairRootBinding binding);
            Vector3 mirrorPoint = default;
            Vector3 mirrorNormal = default;
            HairRootBinding mirrorBinding = default;
            bool mirrorHit = HairGuideSculptSettings.MirrorX &&
                             _raycaster.Raycast(scalp, ScalpMirror.Ray(ray), out mirrorPoint, out mirrorNormal,
                                                out mirrorBinding);

            switch (current.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.MouseDown when current.button == 0 && !current.alt:
                    GUIUtility.hotControl = controlId;
                    if (hit)
                    {
                        var bindings = new List<HairRootBinding> { binding };
                        if (mirrorHit)
                        {
                            bindings.Add(mirrorBinding);
                        }
                        AddGuides(group, guides, bindings, current.shift);
                    }
                    current.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    current.Use();
                    break;

                case EventType.Repaint when hit:
                    DrawAddCursor(point, normal, BrushColor);
                    if (mirrorHit)
                    {
                        DrawAddCursor(mirrorPoint, mirrorNormal, MirrorBrushColor);
                    }
                    break;
            }
        }

        private static void DrawAddCursor(Vector3 point, Vector3 normal, Color color)
        {
            float size = HandleUtility.GetHandleSize(point) * AddCursorSize;
            Handles.color = color;
            Handles.DrawWireDisc(point, normal, size);
            Handles.DrawLine(point, point + normal * size * 2f);
        }

        private void AddGuides(HairGroup group, HairGuideSet guides, List<HairRootBinding> bindings, bool additive)
        {
            RecordEdit(group, "Add Hair Guide");
            if (!additive)
            {
                _selectedGuides.Clear();
            }

            foreach (HairRootBinding binding in bindings)
            {
                guides.Guides.Add(GrowGuide(group, guides, binding));
                _selectedGuides.Add(guides.Count - 1);
            }

            _activeGuide = guides.Count - 1;
            WriteSelection();
            Commit(group);
        }

        private HairGuide GrowGuide(HairGroup group, HairGuideSet guides, in HairRootBinding binding)
        {
            HairRootFrame surfaceRoot = _surface.Evaluate(binding);
            HairRootFrame root = surfaceRoot.TransformedBy(_surface.LocalToWorld);
            Vector3[] points = ShapeFromNeighbours(guides, root);

            if (points == null)
            {
                HairGrowSettings settings = group.ResolvedSettings;
                points = LineHairGenerator.GrowGuideShape(surfaceRoot, settings, settings.Seed + guides.Count,
                                                          settings.SegmentsPerStrand + 1);
            }

            return new HairGuide(binding, surfaceRoot.Uv, points);
        }

        private void HandleDensity(HairGroup group, HairGuideSet guides, Event current)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Renderer scalp = _owner.Scalp.Renderer;
            _raycaster.EnsureMesh(scalp, _owner.Scalp.Submesh);
            bool hit = _raycaster.Raycast(scalp, HandleUtility.GUIPointToWorldRay(current.mousePosition),
                                          out Vector3 point, out Vector3 normal);
            bool invert = current.control || current.command;

            switch (current.GetTypeForControl(controlId))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;

                case EventType.ScrollWheel when current.shift:
                    float scroll = current.delta.y != 0f ? current.delta.y : current.delta.x;
                    HairGuideSculptSettings.Diameter *= Mathf.Max(0.05f, 1f - scroll * ScrollToDiameter);
                    current.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseDown when current.button == 0 && !current.alt && hit:
                    GUIUtility.hotControl = controlId;
                    _strokeUndoGroup = Undo.GetCurrentGroup();
                    RecordEdit(group, "Paint Hair Guide Density");
                    _densityRandom = new System.Random();
                    _densityAdding = DensityAdds(guides, point, invert);
                    _densityCorners = SurfaceCorners();
                    _strokeDirty = ApplyDensity(group, guides, point);
                    current.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                    if (hit)
                    {
                        _strokeDirty |= ApplyDensity(group, guides, point);
                    }
                    current.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    WriteSelection();
                    if (_strokeDirty)
                    {
                        Commit(group);
                    }
                    _strokeDirty = false;
                    _densityCorners = null;
                    Undo.CollapseUndoOperations(_strokeUndoGroup);
                    current.Use();
                    break;

                case EventType.Repaint when hit:
                    bool adding = GUIUtility.hotControl == controlId
                        ? _densityAdding
                        : DensityAdds(guides, point, invert);
                    Color color = adding ? BrushColor : RemoveColor;
                    DrawSurfaceBrush(point, normal, color);
                    if (HairGuideSculptSettings.MirrorX)
                    {
                        HairScalpMirror mirror = ScalpMirror;
                        DrawSurfaceBrush(mirror.Point(point), mirror.Direction(normal),
                                         new Color(color.r, color.g, color.b, MirrorBrushColor.a));
                    }
                    break;
            }
        }

        private static void DrawSurfaceBrush(Vector3 point, Vector3 normal, Color color)
        {
            float radius = HairGuideSculptSettings.Diameter * 0.5f;
            Handles.color = color;
            Handles.DrawWireDisc(point, normal, radius);

            float hardness = HairGuideSculptSettings.Hardness;
            if (hardness > 0.01f && hardness < 0.99f)
            {
                Handles.color = new Color(color.r, color.g, color.b, color.a * 0.4f);
                Handles.DrawWireDisc(point, normal, radius * hardness);
            }
        }

        private bool DensityAdds(HairGuideSet guides, Vector3 center, bool invert)
        {
            switch (HairGuideSculptSettings.DensityMode)
            {
                case HairGuideDensityMode.Add:
                    return !invert;
                case HairGuideDensityMode.Remove:
                    return invert;
            }

            float radius = HairGuideSculptSettings.Diameter * 0.25f;
            var near = new List<(float Distance, Vector3 Root)>();
            foreach (Vector3 root in RootPositions(guides))
            {
                float distance = Vector3.Distance(root, center);
                if (distance < radius)
                {
                    near.Add((distance, root));
                }
            }

            near.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            int checkedCount = Mathf.Min(DensityProbeCount, near.Count);
            float closest = float.MaxValue;
            for (int i = 0; i < checkedCount; i++)
            {
                for (int j = i + 1; j < checkedCount; j++)
                {
                    closest = Mathf.Min(closest, Vector3.Distance(near[i].Root, near[j].Root));
                }
            }

            return closest > HairGuideSculptSettings.DensityDistance;
        }

        private bool ApplyDensity(HairGroup group, HairGuideSet guides, Vector3 center)
        {
            return _densityAdding ? AddDensity(group, guides, center) : ReduceDensity(guides, center);
        }

        private bool AddDensity(HairGroup group, HairGuideSet guides, Vector3 center)
        {
            float radius = HairGuideSculptSettings.Diameter * 0.5f;
            float minimum = HairGuideSculptSettings.DensityDistance;
            float strength = HairGuideSculptSettings.Strength;
            HairScalpMirror mirror = ScalpMirror;
            List<Vector3> roots = RootPositions(guides);
            var added = new List<HairRootBinding>();
            int passes = HairGuideSculptSettings.MirrorX ? 2 : 1;

            for (int pass = 0; pass < passes; pass++)
            {
                Vector3 brush = pass == 0 ? center : mirror.Point(center);
                foreach ((HairRootBinding binding, Vector3 position) in SampleSurface(brush, radius))
                {
                    float falloff = BrushFalloff(Vector3.Distance(position, brush) / radius);
                    if (_densityRandom.NextDouble() > strength * falloff || NearAny(roots, position, minimum))
                    {
                        continue;
                    }

                    roots.Add(position);
                    added.Add(binding);
                }
            }

            foreach (HairRootBinding binding in added)
            {
                guides.Guides.Add(GrowGuide(group, guides, binding));
                _selectedGuides.Add(guides.Count - 1);
            }

            return added.Count > 0;
        }

        private IEnumerable<(HairRootBinding Binding, Vector3 Position)> SampleSurface(Vector3 center, float radius)
        {
            float density = HairGuideSculptSettings.DensityAttempts / (Mathf.PI * radius * radius);
            for (int t = 0; t * 3 + 2 < _densityCorners.Length; t++)
            {
                Vector3 corner0 = _densityCorners[t * 3];
                Vector3 corner1 = _densityCorners[t * 3 + 1];
                Vector3 corner2 = _densityCorners[t * 3 + 2];
                Vector3 min = Vector3.Min(corner0, Vector3.Min(corner1, corner2));
                Vector3 max = Vector3.Max(corner0, Vector3.Max(corner1, corner2));
                if ((Vector3.Max(min, Vector3.Min(center, max)) - center).sqrMagnitude > radius * radius)
                {
                    continue;
                }

                float expected = Vector3.Cross(corner1 - corner0, corner2 - corner0).magnitude * 0.5f * density;
                int count = Mathf.FloorToInt(expected) + (_densityRandom.NextDouble() < expected % 1f ? 1 : 0);
                for (int i = 0; i < count; i++)
                {
                    float u = (float)_densityRandom.NextDouble();
                    float v = (float)_densityRandom.NextDouble();
                    if (u + v > 1f)
                    {
                        u = 1f - u;
                        v = 1f - v;
                    }

                    Vector3 position = corner0 + (corner1 - corner0) * u + (corner2 - corner0) * v;
                    if ((position - center).sqrMagnitude <= radius * radius)
                    {
                        yield return (new HairRootBinding(t, new Vector2(u, v)), position);
                    }
                }
            }
        }

        private static bool NearAny(List<Vector3> roots, Vector3 position, float distance)
        {
            float squared = distance * distance;
            foreach (Vector3 root in roots)
            {
                if ((root - position).sqrMagnitude < squared)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ReduceDensity(HairGuideSet guides, Vector3 center)
        {
            float radius = HairGuideSculptSettings.Diameter * 0.5f;
            float minimum = HairGuideSculptSettings.DensityDistance;
            float strength = HairGuideSculptSettings.Strength;
            HairScalpMirror mirror = ScalpMirror;
            List<Vector3> roots = RootPositions(guides);
            var keep = new bool[roots.Count];
            var allow = new bool[roots.Count];
            for (int g = 0; g < keep.Length; g++)
            {
                keep[g] = true;
            }

            int passes = HairGuideSculptSettings.MirrorX ? 2 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                Vector3 brush = pass == 0 ? center : mirror.Point(center);
                for (int g = 0; g < roots.Count; g++)
                {
                    float distance = Vector3.Distance(roots[g], brush);
                    float chance = strength * BrushFalloff(distance / radius) * (1f - Frozen(guides.Guides[g]));
                    allow[g] = !keep[g] || (distance <= radius && _densityRandom.NextDouble() < chance);
                }

                for (int g = 0; g < roots.Count; g++)
                {
                    if (!keep[g] || !allow[g] || Vector3.Distance(roots[g], brush) > radius)
                    {
                        continue;
                    }

                    for (int other = 0; other < roots.Count; other++)
                    {
                        if (other != g && allow[other] && Vector3.Distance(roots[g], roots[other]) < minimum)
                        {
                            keep[other] = false;
                        }
                    }
                }
            }

            return RemoveGuides(guides, keep);
        }

        private static float Frozen(HairGuide guide)
        {
            float frozen = 0f;
            for (int k = 0; guide.HasFreeze && k < guide.PointCount; k++)
            {
                frozen = Mathf.Max(frozen, guide.FreezeAt(k));
            }

            return frozen;
        }

        private bool RemoveGuides(HairGuideSet guides, bool[] keep)
        {
            var remap = new int[keep.Length];
            int next = 0;
            for (int g = 0; g < keep.Length; g++)
            {
                remap[g] = keep[g] ? next++ : -1;
            }

            if (next == keep.Length)
            {
                return false;
            }

            for (int g = keep.Length - 1; g >= 0; g--)
            {
                if (!keep[g])
                {
                    guides.Guides.RemoveAt(g);
                }
            }

            var selected = new List<int>(_selectedGuides);
            _selectedGuides.Clear();
            foreach (int g in selected)
            {
                if (g < remap.Length && remap[g] >= 0)
                {
                    _selectedGuides.Add(remap[g]);
                }
            }

            var points = new List<Vector2Int>(_selectedPoints);
            _selectedPoints.Clear();
            foreach (Vector2Int point in points)
            {
                if (point.x < remap.Length && remap[point.x] >= 0)
                {
                    _selectedPoints.Add(new Vector2Int(remap[point.x], point.y));
                }
            }

            _activeGuide = FindAnySelected();
            return true;
        }

        private List<Vector3> RootPositions(HairGuideSet guides)
        {
            var roots = new List<Vector3>(guides.Count);
            foreach (HairGuide guide in guides.Guides)
            {
                roots.Add(WorldFrame(guide).Position);
            }

            return roots;
        }

        private Vector3[] SurfaceCorners()
        {
            Matrix4x4 toWorld = _surface.LocalToWorld;
            var corners = new Vector3[_surface.TriangleCount * 3];
            for (int t = 0; t < _surface.TriangleCount; t++)
            {
                corners[t * 3] = toWorld.MultiplyPoint3x4(Corner(t, Vector2.zero));
                corners[t * 3 + 1] = toWorld.MultiplyPoint3x4(Corner(t, Vector2.right));
                corners[t * 3 + 2] = toWorld.MultiplyPoint3x4(Corner(t, Vector2.up));
            }

            return corners;
        }

        private Vector3 Corner(int triangle, Vector2 barycentric)
        {
            return _surface.EvaluatePosition(new HairRootBinding(triangle, barycentric));
        }

        private void ResampleSelection(HairGroup group, HairGuideSet guides, int segments)
        {
            if (_selectedGuides.Count == 0)
            {
                return;
            }

            RecordEdit(group, "Resample Hair Guides");
            int segmentCount = Mathf.Clamp(segments, 1, MaxGuidePoints - 1);
            float[] fractions = group.Settings.BuildSegmentLengthFractions(segmentCount);
            foreach (int g in _selectedGuides)
            {
                guides.Guides[g].Resample(fractions);
            }

            _selectedPoints.Clear();
            WriteSelection();
            Commit(group);
        }

        private void TransferSelection(HairGroup source, HairGroup target, bool copy)
        {
            if (_selectedGuides.Count == 0 || target == null || target == source)
            {
                return;
            }

            Undo.RecordObjects(new Object[] { source, target, this }, copy ? "Copy Hair Guides" : "Move Hair Guides");
            var indices = new List<int>(_selectedGuides);
            indices.Sort();
            List<HairGuide> sourceGuides = source.Settings.Guides.Guides;
            foreach (int g in indices)
            {
                target.Settings.Guides.Guides.Add(sourceGuides[g].Clone());
            }

            if (!copy)
            {
                var keep = new bool[sourceGuides.Count];
                for (int g = 0; g < keep.Length; g++)
                {
                    keep[g] = !_selectedGuides.Contains(g);
                }
                RemoveGuides(source.Settings.Guides, keep);
            }

            WriteSelection();
            MarkChanged(target);
            Commit(source);
        }

        private void FreezeSelection(HairGroup group, HairGuideSet guides, bool freeze)
        {
            float[][] weights = SelectionWeights(guides);
            Undo.RecordObject(group, freeze ? "Freeze Hair Guides" : "Unfreeze Hair Guides");
            for (int g = 0; g < weights.Length; g++)
            {
                HairGuide guide = guides.Guides[g];
                if (weights[g] == null || (!freeze && !guide.HasFreeze))
                {
                    continue;
                }

                for (int k = 0; k < weights[g].Length; k++)
                {
                    float frozen = guide.FreezeAt(k);
                    guide.SetFreeze(k, freeze ? Mathf.Max(frozen, weights[g][k]) : frozen * (1f - weights[g][k]));
                }
            }

            EditorUtility.SetDirty(group);
            SceneView.RepaintAll();
        }

        private Vector3[] ShapeFromNeighbours(HairGuideSet guides, in HairRootFrame root)
        {
            int wanted = HairGuideSet.MaxInfluences;
            var indices = new int[wanted];
            var weights = new float[wanted];
            var frames = new HairRootFrame[wanted];
            for (int i = 0; i < wanted; i++)
            {
                indices[i] = -1;
                weights[i] = float.MaxValue;
            }

            for (int g = 0; g < guides.Count; g++)
            {
                HairGuide guide = guides.Guides[g];
                if (guide == null || guide.PointCount < 2)
                {
                    continue;
                }

                HairRootFrame frame = WorldFrame(guide);
                if (Vector3.Dot(root.Normal, frame.Normal) <= 0f)
                {
                    continue;
                }

                float distance = Vector3.Distance(root.Position, frame.Position);
                int slot = wanted;
                while (slot > 0 && weights[slot - 1] > distance)
                {
                    slot--;
                }

                if (slot >= wanted)
                {
                    continue;
                }

                for (int move = wanted - 1; move > slot; move--)
                {
                    indices[move] = indices[move - 1];
                    weights[move] = weights[move - 1];
                    frames[move] = frames[move - 1];
                }

                indices[slot] = g;
                weights[slot] = distance;
                frames[slot] = frame;
            }

            if (indices[0] < 0)
            {
                return null;
            }

            float total = 0f;
            float nearest = weights[0] + NeighbourDistanceFloor;
            for (int i = 0; i < wanted; i++)
            {
                if (indices[i] < 0)
                {
                    weights[i] = 0f;
                    continue;
                }

                float ratio = nearest / (weights[i] + NeighbourDistanceFloor);
                weights[i] = ratio * ratio;
                total += weights[i];
            }

            Vector3 rootBitangent = root.Bitangent;
            var points = new Vector3[guides.Guides[indices[0]].PointCount];
            for (int k = 0; k < points.Length; k++)
            {
                float along = k / (float)(points.Length - 1);
                Vector3 offset = Vector3.zero;
                for (int i = 0; i < wanted; i++)
                {
                    if (indices[i] < 0)
                    {
                        continue;
                    }

                    HairRootFrame frame = frames[i];
                    Vector3 local = guides.Guides[indices[i]].SampleAt(along);
                    offset += (ToWorld(frame, frame.Bitangent, local) - frame.Position) * (weights[i] / total);
                }

                points[k] = ToLocalVector(root, rootBitangent, offset);
            }

            return points;
        }

        private readonly struct BrushDab
        {
            public readonly Vector2 Mouse;
            public readonly Vector2 MouseDelta;
            public readonly Vector3 WorldDelta;
            public readonly Vector3 Center;
            public readonly Vector3 ViewAxis;
            public readonly float PixelRadius;
            public readonly float Strength;
            public readonly float Motion;
            public readonly float Direction;

            public BrushDab(Vector2 mouse, Vector2 mouseDelta, Vector3 worldDelta, Vector3 center, Vector3 viewAxis,
                            float pixelRadius, bool invert)
            {
                Mouse = mouse;
                MouseDelta = mouseDelta;
                WorldDelta = worldDelta;
                Center = center;
                ViewAxis = viewAxis;
                PixelRadius = pixelRadius;
                Strength = HairGuideSculptSettings.Strength;
                Motion = Mathf.Clamp01(mouseDelta.magnitude / HairGuideBrushes.StepPixels);
                Direction = invert ? -1f : 1f;
            }
        }

        private bool ApplyBrush(HairGuideSet guides, Vector2 mouse, Vector2 mouseDelta,
                                Vector3 worldDelta, Vector3 brushWorld, Camera camera)
        {
            float pixelRadius = BrushPixelRadius(camera, brushWorld);
            if (pixelRadius <= 0.5f || mouseDelta.sqrMagnitude < 0.01f)
            {
                return false;
            }

            var dab = new BrushDab(mouse, mouseDelta, worldDelta, brushWorld, camera.transform.forward, pixelRadius,
                                   _strokeInvert);
            bool freeze = HairGuideSculptSettings.Mode == HairGuideEditMode.Freeze;
            HairScalpMirror mirror = ScalpMirror;
            int passes = HairGuideSculptSettings.MirrorX ? 2 : 1;
            var screens = new Vector2[passes][][];
            var falloffs = new float[passes][][];

            for (int pass = 0; pass < passes; pass++)
            {
                screens[pass] = new Vector2[guides.Count][];
                falloffs[pass] = new float[guides.Count][];
                for (int g = 0; g < guides.Count; g++)
                {
                    HairGuide guide = guides.Guides[g];
                    if (!Brushable(guide, freeze ? null : _strokeFactors?[g], freeze))
                    {
                        continue;
                    }

                    HairRootFrame frame = PassFrame(guide, pass == 1, mirror, out Vector3 bitangent);
                    Vector3[] world = ToWorldPoints(frame, bitangent, ReadPoints(guide));
                    screens[pass][g] = ScreenPoints(world);
                    falloffs[pass][g] = ScreenFalloff(world, screens[pass][g], mouse, pixelRadius);
                }
            }

            bool changedAny = false;
            for (int pass = 0; pass < passes; pass++)
            {
                for (int g = 0; g < guides.Count; g++)
                {
                    float[] falloff = falloffs[pass][g];
                    if (falloff == null)
                    {
                        continue;
                    }

                    HairGuide guide = guides.Guides[g];
                    HairRootFrame frame = PassFrame(guide, pass == 1, mirror, out Vector3 bitangent);
                    Vector3[] before = ReadPoints(guide);
                    Vector3[] world = ToWorldPoints(frame, bitangent, before);
                    changedAny |= freeze
                        ? PaintFreeze(guide, falloff, dab.Direction * dab.Strength * dab.Motion)
                        : Sculpt(guide, _strokeFactors[g], frame, bitangent, before, world, screens[pass][g], falloff,
                                 dab);
                }
            }

            return changedAny;
        }

        private static bool Brushable(HairGuide guide, float[] factors, bool freeze)
        {
            return guide.PointCount >= 2 && (freeze || (factors != null && factors.Length == guide.PointCount));
        }

        private HairRootFrame PassFrame(HairGuide guide, bool mirrored, in HairScalpMirror mirror,
                                        out Vector3 bitangent)
        {
            HairRootFrame frame = WorldFrame(guide);
            bitangent = frame.Bitangent;
            if (!mirrored)
            {
                return frame;
            }

            bitangent = mirror.Direction(bitangent).normalized;
            return mirror.Frame(frame);
        }

        private static Vector3[] ToWorldPoints(in HairRootFrame frame, Vector3 bitangent, Vector3[] local)
        {
            var world = new Vector3[local.Length];
            for (int k = 0; k < world.Length; k++)
            {
                world[k] = ToWorld(frame, bitangent, local[k]);
            }

            return world;
        }

        private float[] ScreenFalloff(Vector3[] world, Vector2[] screen, Vector2 mouse, float pixelRadius)
        {
            bool[] visible = Visibility(world);
            var falloff = new float[screen.Length];
            for (int k = 0; k < screen.Length; k++)
            {
                falloff[k] = visible == null || visible[k] ? BrushWeight(screen[k], mouse, pixelRadius) : 0f;
            }

            return falloff;
        }

        private static float CurveFalloff(Vector2[] screen, Vector2 mouse, float pixelRadius)
        {
            float nearest = float.MaxValue;
            for (int k = 1; k < screen.Length; k++)
            {
                nearest = Mathf.Min(nearest, DistanceToSegmentSquared(mouse, screen[k - 1], screen[k]));
            }

            return BrushFalloff(Mathf.Sqrt(nearest) / Mathf.Max(pixelRadius, 0.001f));
        }

        private static bool Sculpt(HairGuide guide, float[] factors, in HairRootFrame frame, Vector3 bitangent,
                                   Vector3[] before, Vector3[] world, Vector2[] screen, float[] falloff,
                                   in BrushDab dab)
        {
            HairGuideEditMode mode = HairGuideSculptSettings.Mode;
            var weights = new float[world.Length];
            float step = dab.Strength * dab.Motion;

            switch (mode)
            {
                case HairGuideEditMode.Grab:
                    Weigh(weights, falloff, factors, dab.Strength);
                    HairGuideBrushes.Grab(world, weights, dab.WorldDelta);
                    break;

                case HairGuideEditMode.Comb:
                    if (dab.WorldDelta.sqrMagnitude < 1e-12f)
                    {
                        return false;
                    }

                    Weigh(weights, falloff, factors, step);
                    HairGuideBrushes.Comb(world, weights, dab.WorldDelta.normalized);
                    break;

                case HairGuideEditMode.Smooth:
                    Weigh(weights, falloff, factors, dab.Strength * Mathf.Max(0.08f, dab.Motion));
                    HairGuideBrushes.Smooth(world, weights);
                    break;

                case HairGuideEditMode.Length:
                    Weigh(weights, falloff, factors, 1f);
                    HairGuideBrushes.ScaleFromRoot(world, Mathf.Exp(-dab.MouseDelta.y * 0.015f * dab.Strength *
                                                                    Largest(weights)));
                    break;

                case HairGuideEditMode.Puff:
                    float curve = CurveFalloff(screen, dab.Mouse, dab.PixelRadius);
                    for (int k = 0; k < weights.Length; k++)
                    {
                        weights[k] = curve * factors[k] * step * HairGuideBrushes.PuffRate;
                    }
                    HairGuideBrushes.Puff(world, weights, frame.Normal);
                    break;

                case HairGuideEditMode.Twist:
                    Weigh(weights, falloff, factors, dab.Direction * HairGuideBrushes.TwistDegreesPerStep * step);
                    HairGuideBrushes.Twist(world, weights, dab.Center, dab.ViewAxis);
                    break;

                case HairGuideEditMode.Curl:
                    Weigh(weights, falloff, factors, step);
                    HairGuideBrushes.Curl(world, weights, frame.Normal,
                                          dab.Direction > 0f ? HairGuideSculptSettings.CurlRadius : 0f,
                                          HairGuideSculptSettings.CurlTurns);
                    break;

                case HairGuideEditMode.Clump:
                    for (int k = 0; k < weights.Length; k++)
                    {
                        float t = Vector2.Distance(screen[k], dab.Mouse) / Mathf.Max(dab.PixelRadius, 0.001f);
                        weights[k] = dab.Direction * step * t * falloff[k] * factors[k];
                    }
                    HairGuideBrushes.Clump(world, weights, dab.Mouse, dab.ViewAxis);
                    break;

                default:
                    return false;
            }

            if (Largest(weights) <= 0f)
            {
                return false;
            }

            var result = new Vector3[world.Length];
            result[0] = before[0];
            for (int k = 1; k < result.Length; k++)
            {
                result[k] = ToLocalVector(frame, bitangent, world[k] - frame.Position);
            }

            if (mode != HairGuideEditMode.Length && HairGuideSculptSettings.PreserveLength)
            {
                PreserveSegmentLengths(result, SegmentLengths(before), before);
            }

            HoldFrozen(guide, result, before);
            WritePoints(guide, result);
            return true;
        }

        private static void Weigh(float[] weights, float[] falloff, float[] factors, float scale)
        {
            for (int k = 0; k < weights.Length; k++)
            {
                weights[k] = falloff[k] * factors[k] * scale;
            }
        }

        private static float Largest(float[] weights)
        {
            float largest = 0f;
            foreach (float weight in weights)
            {
                largest = Mathf.Max(largest, Mathf.Abs(weight));
            }

            return largest;
        }

        private static bool PaintFreeze(HairGuide guide, float[] falloff, float amount)
        {
            if (amount < 0f && !guide.HasFreeze)
            {
                return false;
            }

            bool changed = false;
            for (int k = 0; k < falloff.Length; k++)
            {
                if (falloff[k] <= 0f)
                {
                    continue;
                }

                guide.SetFreeze(k, guide.FreezeAt(k) + amount * falloff[k]);
                changed = true;
            }

            return changed;
        }

        private bool FindBrushHover(HairGuideSet guides, Vector2 mouse, Camera camera, bool anyGuide,
                                    out Vector3 world)
        {
            world = default;
            float nearestSquared = float.MaxValue;
            bool found = false;

            for (int g = 0; g < guides.Count; g++)
            {
                if (!anyGuide && !_selectedGuides.Contains(g))
                {
                    continue;
                }

                Vector3[] points = WorldPoints(guides.Guides[g]);
                for (int k = 1; k < points.Length; k++)
                {
                    Vector2 a = HandleUtility.WorldToGUIPoint(points[k - 1]);
                    Vector2 b = HandleUtility.WorldToGUIPoint(points[k]);
                    Vector2 ab = b - a;
                    float denominator = ab.sqrMagnitude;
                    float t = denominator > 1e-6f
                        ? Mathf.Clamp01(Vector2.Dot(mouse - a, ab) / denominator)
                        : 0f;
                    Vector2 nearest = Vector2.Lerp(a, b, t);
                    float squared = (nearest - mouse).sqrMagnitude;
                    if (squared < nearestSquared)
                    {
                        nearestSquared = squared;
                        world = Vector3.Lerp(points[k - 1], points[k], t);
                        found = true;
                    }
                }
            }

            if (!found || camera == null)
            {
                return false;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
            if (!new Plane(camera.transform.forward, world).Raycast(ray, out float distance))
            {
                return false;
            }

            world = ray.GetPoint(distance);
            return true;
        }

        private static void DrawBrush(Camera camera, Vector3 world, bool invert)
        {
            float radius = HairGuideSculptSettings.Diameter * 0.5f;
            Vector3 normal = camera != null ? camera.transform.forward : Vector3.forward;

            Handles.color = invert && Inverts(HairGuideSculptSettings.Mode) ? RemoveColor : BrushColor;
            Handles.DrawWireDisc(world, normal, radius);

            float hardness = HairGuideSculptSettings.Hardness;
            if (hardness > 0.01f && hardness < 0.99f)
            {
                Handles.color = BrushFalloffColor;
                Handles.DrawWireDisc(world, normal, radius * hardness);
            }
        }

        private static bool Inverts(HairGuideEditMode mode)
        {
            return mode is HairGuideEditMode.Twist or HairGuideEditMode.Curl or HairGuideEditMode.Clump
                or HairGuideEditMode.Freeze or HairGuideEditMode.Density;
        }

        private static float BrushPixelRadius(Camera camera, Vector3 world)
        {
            if (camera == null)
            {
                return 0f;
            }

            float radius = HairGuideSculptSettings.Diameter * 0.5f;
            Vector2 center = HandleUtility.WorldToGUIPoint(world);
            Vector2 edge = HandleUtility.WorldToGUIPoint(world + camera.transform.right * radius);
            return Vector2.Distance(center, edge);
        }

        private static float BrushWeight(Vector2 point, Vector2 center, float radius)
        {
            return BrushFalloff(Vector2.Distance(point, center) / Mathf.Max(radius, 0.001f));
        }

        private static float BrushFalloff(float normalized)
        {
            if (normalized >= 1f)
            {
                return 0f;
            }

            float hardness = HairGuideSculptSettings.Hardness;
            if (normalized <= hardness || hardness >= 0.999f)
            {
                return 1f;
            }

            float t = Mathf.InverseLerp(hardness, 1f, normalized);
            return 1f - t * t * (3f - 2f * t);
        }

        private bool PointOnStrokePlane(Vector2 mouse, out Vector3 world)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
            if (_strokePlane.Raycast(ray, out float distance))
            {
                world = ray.GetPoint(distance);
                return true;
            }

            world = _lastStrokeWorld;
            return false;
        }

        private static Vector3[] ReadPoints(HairGuide guide)
        {
            var points = new Vector3[guide.PointCount];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = guide.PointAt(i);
            }
            return points;
        }

        private static void WritePoints(HairGuide guide, Vector3[] points)
        {
            for (int i = 1; i < points.Length; i++)
            {
                guide.SetPoint(i, points[i]);
            }
        }

        private static float[] SegmentLengths(Vector3[] points)
        {
            var lengths = new float[Mathf.Max(0, points.Length - 1)];
            for (int i = 0; i < lengths.Length; i++)
            {
                lengths[i] = Vector3.Distance(points[i], points[i + 1]);
            }
            return lengths;
        }

        private static void PreserveSegmentLengths(Vector3[] points, float[] lengths, Vector3[] fallback)
        {
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 direction = points[i] - points[i - 1];
                if (direction.sqrMagnitude < 1e-12f)
                {
                    direction = fallback[i] - fallback[i - 1];
                }
                if (direction.sqrMagnitude < 1e-12f)
                {
                    direction = Vector3.forward;
                }
                points[i] = points[i - 1] + direction.normalized * lengths[i - 1];
            }
        }

        private static void DragFrom(HairGuide guide, in HairRootFrame frame, Vector3 bitangent,
                                     int grabbed, Vector3 worldDelta)
        {
            Vector3 localDelta = ToLocalVector(frame, bitangent, worldDelta);

            for (int k = grabbed; k < guide.PointCount; k++)
            {
                float falloff = grabbed == guide.PointCount - 1
                    ? 1f
                    : Mathf.Clamp01((k - grabbed) / (float)(guide.PointCount - grabbed)) * 0.5f + 0.5f;

                guide.SetPoint(k, guide.PointAt(k) + localDelta * falloff);
            }
        }

        private HairRootFrame WorldFrame(HairGuide guide)
        {
            return _surface.Evaluate(guide.Binding).TransformedBy(_surface.LocalToWorld);
        }

        private HairScalpMirror ScalpMirror => new HairScalpMirror(_owner.Scalp.Renderer, _mirrorAxis);

        private static Vector3[] Mirrored(Vector3[] world, in HairScalpMirror mirror)
        {
            var mirrored = new Vector3[world.Length];
            for (int k = 0; k < world.Length; k++)
            {
                mirrored[k] = mirror.Point(world[k]);
            }

            return mirrored;
        }

        private Vector3[][] WorldPoints(HairGuideSet guides)
        {
            var world = new Vector3[guides.Count][];
            for (int g = 0; g < guides.Count; g++)
            {
                world[g] = WorldPoints(guides.Guides[g]);
            }

            return world;
        }

        private Vector3[] WorldPoints(HairGuide guide)
        {
            HairRootFrame frame = WorldFrame(guide);
            Vector3 bitangent = frame.Bitangent;
            var points = new Vector3[guide.PointCount];

            for (int k = 0; k < points.Length; k++)
            {
                points[k] = ToWorld(frame, bitangent, guide.PointAt(k));
            }

            return points;
        }

        private static Vector3 ToWorld(in HairRootFrame frame, Vector3 bitangent, Vector3 local)
        {
            return frame.Position + frame.Tangent * local.x + bitangent * local.y + frame.Normal * local.z;
        }

        private static Vector3 ToLocalVector(in HairRootFrame frame, Vector3 bitangent, Vector3 world)
        {
            return new Vector3(Vector3.Dot(world, frame.Tangent),
                               Vector3.Dot(world, bitangent),
                               Vector3.Dot(world, frame.Normal));
        }

        private bool EnsureSurface()
        {
            if (_surface != null && !_surface.IsDisposed)
            {
                return true;
            }

            ReleaseSurface();

            if (_owner.Scalp.Mode != HairScalpMode.Mesh)
            {
                return false;
            }

            if (!HairSurface.TryCreate(_owner.Scalp.Renderer, _owner.Scalp.Submesh, out _surface, out string _))
            {
                return false;
            }

            _raycaster.EnsureMesh(_owner.Scalp.Renderer, _owner.Scalp.Submesh);
            _mirrorAxis = _raycaster.Mesh != null ? _raycaster.Mesh.bounds.center.x : 0f;
            return true;
        }

        private void RecordEdit(HairGroup group, string name)
        {
            Undo.RecordObjects(new Object[] { group, this }, name);
        }

        private void CommitSelection(string name)
        {
            Undo.RecordObject(this, name);
            WriteSelection();
        }

        private void WriteSelection()
        {
            _committedGuides.Clear();
            _committedGuides.AddRange(_selectedGuides);
            _committedGuides.Sort();
            _committedPoints.Clear();
            _committedPoints.AddRange(_selectedPoints);
            _committedPoints.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        }

        private void RestoreSelection()
        {
            _selectedGuides.Clear();
            _selectedGuides.UnionWith(_committedGuides);
            _selectedPoints.Clear();
            _selectedPoints.UnionWith(_committedPoints);
            _activeGuide = FindAnySelected();
            SceneView.RepaintAll();
        }

        private void PruneSelection(HairGuideSet guides)
        {
            _selectedGuides.RemoveWhere(index => index < 0 || index >= guides.Count);
            _selectedPoints.RemoveWhere(point => !_selectedGuides.Contains(point.x) ||
                                                 point.y >= guides.Guides[point.x].PointCount);
            if (_activeGuide < 0 || !_selectedGuides.Contains(_activeGuide))
            {
                _activeGuide = FindAnySelected();
            }
        }

        private int FindAnySelected()
        {
            foreach (int index in _selectedGuides)
            {
                return index;
            }

            return -1;
        }

        private void Commit(HairGroup group)
        {
            MarkChanged(group);
            _owner?.Rebuild();
            SceneView.RepaintAll();
        }

        private static void MarkChanged(HairGroup group)
        {
            int guidePoints = group.Settings.Guides.MaxPointCount;
            if (guidePoints >= 2)
            {
                group.Settings.SegmentsPerStrand = guidePoints - 1;
            }
            EditorUtility.SetDirty(group);
        }

        private void ReleaseSurface()
        {
            _surface?.Dispose();
            _surface = null;
        }
    }
}
