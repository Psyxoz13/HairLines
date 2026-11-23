using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace HairLines.EditorTools
{
    [Overlay(typeof(SceneView), OverlayId, "Hair Guides")]
    public class HairGuideSculptOverlay : IMGUIOverlay, ITransientOverlay
    {
        bool ITransientOverlay.visible => ToolManager.activeToolType == typeof(HairGuideSculptSession);

        private const string OverlayId = "hairlines-guide-sculpt";
        private const float PanelWidth = 286f;
        private const float LabelWidth = 86f;
        private const float CentimetresPerMetre = 100f;
        private const float MillimetresPerMetre = 1000f;
        private const float SectionGap = 4f;

        private static GUIStyle _shortcutKeyStyle;

        private static readonly (string Keys, string Action)[] SelectionShortcuts =
        {
            ("Tab", "Switch to Edit"),
            ("1 – 4", "Click, Brush, Circle, Area"),
            ("Shift+Click", "Add to selection"),
            ("Ctrl/Cmd+Click", "Toggle selection"),
            ("Shift+Brush", "Add to selection"),
            ("Ctrl/Cmd+Brush", "Remove from selection"),
            ("Shift+Wheel", "Selection brush radius"),
            ("Delete", "Delete selected guides"),
            ("F", "Frame selection"),
        };

        private static readonly (string Keys, string Action)[] EditShortcuts =
        {
            ("Tab", "Switch to Selection"),
            ("1 – 0", "Points, Grab, Comb, Smooth, Length, Puff, Twist, Curl, Clump, Add"),
            ("Shift+Click", "Add a point to the selection"),
            ("Ctrl/Cmd+Click", "Toggle a point; on a segment, split it"),
            ("Ctrl/Cmd+Brush", "Twist back, uncurl, unclump, unfreeze, flip density"),
            ("Shift+Drag tip", "Add a segment"),
            ("Alt+S", "Subdivide segments between selected points"),
            ("Delete", "Delete selected points, or guides when no point is selected"),
            ("Shift+Wheel", "Brush diameter"),
            ("F", "Frame selection"),
        };

        public override void OnGUI()
        {
            displayName = HairGuideSculptSettings.ToolMode == HairGuideToolMode.Selection
                ? "Selection"
                : "Edit";

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = LabelWidth;

            GUILayout.BeginVertical(GUILayout.Width(PanelWidth));

            int selectionCount = HairGuideSculptSession.SelectionCount;
            EditorGUILayout.LabelField($"{selectionCount} guide(s) selected", EditorStyles.miniBoldLabel);
            if (HairGuideSculptSettings.ToolMode == HairGuideToolMode.Selection)
            {
                DrawSelectionTools();
            }
            else
            {
                DrawEditTools(selectionCount);
            }

            DrawSoftSelection();
            GUILayout.Space(SectionGap);
            DrawShortcuts();
            GUILayout.EndVertical();

            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        private static void Section(string title)
        {
            GUILayout.Space(SectionGap);
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
        }

        private static void DrawSelectionTools()
        {
            EditorGUILayout.LabelField("Choose a selection tool in the Scene View toolbar.",
                                       EditorStyles.wordWrappedMiniLabel);

            if (HairGuideSculptSettings.SelectionMode == HairGuideSelectionMode.Brush)
            {
                Section("Selection Brush");
                HairGuideSculptSettings.SelectionBrushRadius = EditorGUILayout.FloatField(
                    new GUIContent("Radius", "Screen radius of the selection brush in pixels."),
                    HairGuideSculptSettings.SelectionBrushRadius);
            }
        }

        private static void DrawEditTools(int selectionCount)
        {
            HairGuideEditMode mode = HairGuideSculptSettings.Mode;
            if (NeedsSelection(mode) && selectionCount == 0)
            {
                EditorGUILayout.HelpBox("No guides selected. Switch to Selection mode first.", MessageType.Info);
                if (GUILayout.Button("Go to Selection"))
                {
                    HairGuideSculptSettings.ToolMode = HairGuideToolMode.Selection;
                    SceneView.RepaintAll();
                }
                return;
            }

            switch (mode)
            {
                case HairGuideEditMode.Points:
                    DrawPointTools();
                    break;
                case HairGuideEditMode.Add:
                    break;
                default:
                    DrawBrushTools(mode);
                    break;
            }

            switch (mode)
            {
                case HairGuideEditMode.Curl:
                    DrawCurlTools();
                    break;
                case HairGuideEditMode.Density:
                    DrawDensityTools();
                    break;
            }

            GUILayout.Space(SectionGap);
            EditorGUILayout.LabelField(HelpFor(mode), EditorStyles.wordWrappedMiniLabel);
        }

        private static bool NeedsSelection(HairGuideEditMode mode)
        {
            return mode is not (HairGuideEditMode.Add or HairGuideEditMode.Density or HairGuideEditMode.Freeze);
        }

        private static void DrawBrushTools(HairGuideEditMode mode)
        {
            Section("Brush");
            HairGuideSculptSettings.Diameter = EditorGUILayout.FloatField(
                new GUIContent("Diameter", "World-space brush diameter in centimetres."),
                HairGuideSculptSettings.Diameter * CentimetresPerMetre) / CentimetresPerMetre;
            HairGuideSculptSettings.Strength = EditorGUILayout.Slider(
                new GUIContent("Strength", "Amount applied by every part of the stroke."),
                HairGuideSculptSettings.Strength, 0f, 1f);
            HairGuideSculptSettings.Hardness = EditorGUILayout.Slider(
                new GUIContent("Hardness", "Size of the full-strength core inside the brush."),
                HairGuideSculptSettings.Hardness, 0f, 1f);

            if (NeedsSelection(mode))
            {
                DrawPreserveLength(mode);
            }
        }

        private static void DrawPreserveLength(HairGuideEditMode mode)
        {
            using (new EditorGUI.DisabledScope(mode == HairGuideEditMode.Length))
            {
                HairGuideSculptSettings.PreserveLength = RightToggle(
                    new GUIContent("Preserve Length", "Keep every guide segment at its current length."),
                    HairGuideSculptSettings.PreserveLength);
            }
        }

        private static bool RightToggle(GUIContent label, bool value)
        {
            Rect row = EditorGUILayout.GetControlRect();
            float toggleWidth = EditorStyles.toggle.CalcSize(GUIContent.none).x;
            EditorGUI.LabelField(row, label);
            return EditorGUI.Toggle(new Rect(row.xMax - toggleWidth, row.y, toggleWidth, row.height), value);
        }

        private static void DrawCurlTools()
        {
            Section("Curl");
            HairGuideSculptSettings.CurlRadius = EditorGUILayout.FloatField(
                new GUIContent("Radius", "Radius of the helix the brush winds guides into, in millimetres."),
                HairGuideSculptSettings.CurlRadius * MillimetresPerMetre) / MillimetresPerMetre;
            HairGuideSculptSettings.CurlTurns = EditorGUILayout.Slider(
                new GUIContent("Turns", "Full curl turns over the length of a guide."),
                HairGuideSculptSettings.CurlTurns, 0.25f, 20f);
        }

        private static void DrawDensityTools()
        {
            Section("Density");
            HairGuideSculptSettings.DensityMode = (HairGuideDensityMode)EditorGUILayout.EnumPopup(
                new GUIContent("Mode", "Auto adds where roots are further apart than Distance and removes " +
                                       "where they are closer. Ctrl/Cmd flips Add and Remove."),
                HairGuideSculptSettings.DensityMode);
            HairGuideSculptSettings.DensityDistance = EditorGUILayout.FloatField(
                new GUIContent("Distance", "Goal distance between guide roots, in centimetres."),
                HairGuideSculptSettings.DensityDistance * CentimetresPerMetre) / CentimetresPerMetre;
            HairGuideSculptSettings.DensityAttempts = EditorGUILayout.IntSlider(
                new GUIContent("Count Max", "Candidate roots the brush tries per step."),
                HairGuideSculptSettings.DensityAttempts, 1, 1000);
        }

        private static void DrawSoftSelection()
        {
            if (!HairGuideSculptSettings.SoftSelection)
            {
                return;
            }

            Section("Soft Selection");
            EditorGUI.BeginChangeCheck();
            HairGuideSculptSettings.SoftRadius = EditorGUILayout.FloatField(
                new GUIContent("Radius", "Distance from the selection over which the effect fades out, in centimetres."),
                HairGuideSculptSettings.SoftRadius * CentimetresPerMetre) / CentimetresPerMetre;
            HairGuideSculptSettings.SoftFalloff = (HairGuideFalloff)EditorGUILayout.EnumPopup(
                new GUIContent("Falloff", "Shape of the fade from the selection to the radius."),
                HairGuideSculptSettings.SoftFalloff);
            HairGuideSculptSettings.SoftConnected = RightToggle(
                new GUIContent("Along Curve Only", "Measure the distance along each curve from its selected " +
                                                   "points instead of through space."),
                HairGuideSculptSettings.SoftConnected);
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }
        }

        private static void DrawShortcuts()
        {
            _shortcutKeyStyle ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontStyle = FontStyle.Bold,
                wordWrap = false,
            };

            HairGuideSculptSettings.ShortcutsExpanded = EditorGUILayout.Foldout(
                HairGuideSculptSettings.ShortcutsExpanded, "Shortcuts", true);
            if (!HairGuideSculptSettings.ShortcutsExpanded)
            {
                return;
            }

            var shortcuts = HairGuideSculptSettings.ToolMode == HairGuideToolMode.Selection
                ? SelectionShortcuts
                : EditShortcuts;
            foreach ((string keys, string action) in shortcuts)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(keys, _shortcutKeyStyle, GUILayout.Width(LabelWidth));
                GUILayout.Label(action, EditorStyles.wordWrappedMiniLabel);
                GUILayout.EndHorizontal();
            }
        }

        private static void DrawPointTools()
        {
            Section("Points");
            int pointCount = HairGuideSculptSession.SelectedPointCount;
            EditorGUILayout.LabelField($"{pointCount} point(s) selected", EditorStyles.miniLabel);
            DrawPreserveLength(HairGuideEditMode.Points);

            using (new EditorGUI.DisabledScope(pointCount == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Subdivide", "Split every segment between two selected points (Alt+S).")))
                {
                    HairGuideSculptSession.SubdivideSelectedSegments();
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button(new GUIContent("Delete Points", "Remove the selected points (Delete).")))
                {
                    HairGuideSculptSession.DeleteSelectedGuidePoints();
                    SceneView.RepaintAll();
                }
            }
        }

        private static string HelpFor(HairGuideEditMode mode)
        {
            return mode switch
            {
                HairGuideEditMode.Points => "Click or drag a box to select points, drag a point to move it. Strands take the segment count of the guide with the most points.",
                HairGuideEditMode.Grab => "Drag through the groom to pull a region without editing points one by one.",
                HairGuideEditMode.Comb => "The mouse stroke becomes the desired flow direction.",
                HairGuideEditMode.Smooth => "Stroke over guides to remove sharp changes of direction.",
                HairGuideEditMode.Length => "Vertical strokes change the full length of guides under the brush.",
                HairGuideEditMode.Puff => "Stroke over guides to stand them up from the scalp. Points never move closer to the root.",
                HairGuideEditMode.Twist => "Stroke to wind guides around the brush centre, like twisting a lock. Ctrl/Cmd twists back.",
                HairGuideEditMode.Curl => "Stroke to wind guides into a helix around their own path. Ctrl/Cmd straightens them.",
                HairGuideEditMode.Clump => "Stroke to pull guides towards the brush centre. Ctrl/Cmd pushes them apart.",
                HairGuideEditMode.Add => "Click the scalp to add a guide shaped like its neighbours. Shift+Click keeps the selection.",
                HairGuideEditMode.Density => "Paint the scalp to add guides between existing ones or thin them out to Distance. New guides take the shape of their neighbours.",
                HairGuideEditMode.Freeze => "Paint over guides to lock them against every brush. Ctrl/Cmd unfreezes.",
                _ => string.Empty,
            };
        }
    }

    [Overlay(typeof(SceneView), OverlayId, "Hair Guide Tools")]
    [Icon("Assets/HairLines/Editor/Icons/HairGuideTools/comb.png")]
    public class HairGuideSculptToolOverlay : ToolbarOverlay, ITransientOverlay
    {
        private const string OverlayId = "hairlines-guide-sculpt-tools";

        public HairGuideSculptToolOverlay() : base(
            HairGuideSelectionToolbarGroup.Id,
            HairGuideSelectionActionsToolbarGroup.Id,
            HairGuideOperationsToolbarSeparator.Id,
            HairGuideOperationsToolbarGroup.Id,
            HairGuideEditToolbarSeparator.Id,
            HairGuidePointsToolbarGroup.Id,
            HairGuideShapeToolbarGroup.Id,
            HairGuideStyleToolbarGroup.Id,
            HairGuidePopulationToolbarGroup.Id,
            HairGuideFreezeToolbarGroup.Id,
            HairGuideOptionsToolbarSeparator.Id,
            HairGuideOptionsToolbarGroup.Id,
            HairGuideDisplayToolbarSeparator.Id,
            HairGuideDisplayToolbarGroup.Id)
        {
        }

        bool ITransientOverlay.visible => ToolManager.activeToolType == typeof(HairGuideSculptSession);
    }

    internal static class HairGuideToolbarIcons
    {
        public static Texture2D Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/HairLines/Editor/Icons/HairGuideTools/{name}.png");
        }
    }

    internal static class HairGuideToolbarLayout
    {
        public static void FollowOrientation(VisualElement element, System.Action<bool> apply)
        {
            VisualElement host = null;
            EventCallback<GeometryChangedEvent> update = _ =>
                apply(host != null && host.resolvedStyle.flexDirection == FlexDirection.Column);

            apply(false);
            element.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                host = element.parent;
                host?.RegisterCallback(update);
            });
            element.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                host?.UnregisterCallback(update);
                host = null;
            });
        }
    }

    internal abstract class HairGuideToolbarGroup : OverlayToolbar
    {
        private const float Gap = 8f;

        protected HairGuideToolbarGroup(bool spaced, params VisualElement[] items)
        {
            style.alignSelf = Align.Center;
            foreach (VisualElement item in items)
            {
                Add(item);
            }
            SetupChildrenAsButtonStrip();

            if (spaced)
            {
                HairGuideToolbarLayout.FollowOrientation(this, vertical =>
                {
                    style.marginLeft = vertical ? 0f : Gap;
                    style.marginTop = vertical ? Gap : 0f;
                });
            }
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideSelectionToolbarGroup : HairGuideToolbarGroup
    {
        public const string Id = "HairLines/GuideTools/SelectionGroup";

        public HairGuideSelectionToolbarGroup() : base(false,
            new HairGuideClickSelectionToolbarToggle(),
            new HairGuideBrushSelectionToolbarToggle(),
            new HairGuideCircleSelectionToolbarToggle(),
            new HairGuideAreaSelectionToolbarToggle())
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideSelectionActionsToolbarGroup : HairGuideToolbarGroup
    {
        public const string Id = "HairLines/GuideTools/SelectionActionsGroup";

        public HairGuideSelectionActionsToolbarGroup() : base(true,
            new HairGuideSelectAllToolbarButton(),
            new HairGuideClearSelectionToolbarButton(),
            new HairGuideInvertSelectionToolbarButton())
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideOperationsToolbarGroup : HairGuideToolbarGroup
    {
        public const string Id = "HairLines/GuideTools/OperationsGroup";

        public HairGuideOperationsToolbarGroup() : base(false,
            new HairGuideResampleToolbarDropdown(),
            new HairGuideMoveToGroupToolbarDropdown(),
            new HairGuideFreezeSelectionToolbarDropdown())
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuidePointsToolbarGroup : HairGuideToolbarGroup
    {
        public const string Id = "HairLines/GuideTools/PointsGroup";

        public HairGuidePointsToolbarGroup() : base(false,
            HairGuideSculptModeToolbarToggle.For(HairGuideEditMode.Points))
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideShapeToolbarGroup : HairGuideToolbarGroup
    {
        public const string Id = "HairLines/GuideTools/ShapeGroup";

        public HairGuideShapeToolbarGroup() : base(true,
            HairGuideSculptModeToolbarToggle.For(HairGuideEditMode.Grab),
            HairGuideSculptModeToolbarToggle.For(HairGuideEditMode.Comb),
            HairGuideSculptModeToolbarToggle.For(HairGuideEditMode.Smooth),
            HairGuideSculptModeToolbarToggle.For(HairGuideEditMode.Length))
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideStyleToolbarGroup : HairGuideToolbarGroup
    {
        public const string Id = "HairLines/GuideTools/StyleGroup";

        public HairGuideStyleToolbarGroup() : base(true,
            HairGuideSculptModeToolbarToggle.For(HairGuideEditMode.Puff),
            HairGuideSculptModeToolbarToggle.For(HairGuideEditMode.Twist),
            HairGuideSculptModeToolbarToggle.For(HairGuideEditMode.Curl),
            HairGuideSculptModeToolbarToggle.For(HairGuideEditMode.Clump))
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuidePopulationToolbarGroup : HairGuideToolbarGroup
    {
        public const string Id = "HairLines/GuideTools/PopulationGroup";

        public HairGuidePopulationToolbarGroup() : base(true,
            HairGuideSculptModeToolbarToggle.For(HairGuideEditMode.Add),
            HairGuideSculptModeToolbarToggle.For(HairGuideEditMode.Density))
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideFreezeToolbarGroup : HairGuideToolbarGroup
    {
        public const string Id = "HairLines/GuideTools/FreezeGroup";

        public HairGuideFreezeToolbarGroup() : base(true,
            HairGuideSculptModeToolbarToggle.For(HairGuideEditMode.Freeze))
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideOptionsToolbarGroup : HairGuideToolbarGroup
    {
        public const string Id = "HairLines/GuideTools/OptionsGroup";

        public HairGuideOptionsToolbarGroup() : base(false,
            new HairGuideSoftSelectionToolbarToggle(),
            new HairGuideMirrorToolbarToggle())
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideDisplayToolbarGroup : HairGuideToolbarGroup
    {
        public const string Id = "HairLines/GuideTools/DisplayGroup";

        public HairGuideDisplayToolbarGroup() : base(false,
            new HairGuideXRayToolbarToggle(),
            new HairGuideShowHairToolbarToggle(),
            new HairGuideSimulationToolbarToggle())
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideOperationsToolbarSeparator : HairGuideToolbarSeparatorElement
    {
        public const string Id = "HairLines/GuideTools/OperationsSeparator";
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideEditToolbarSeparator : HairGuideToolbarSeparatorElement
    {
        public const string Id = "HairLines/GuideTools/EditSeparator";
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideOptionsToolbarSeparator : HairGuideToolbarSeparatorElement
    {
        public const string Id = "HairLines/GuideTools/OptionsSeparator";
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideDisplayToolbarSeparator : HairGuideToolbarSeparatorElement
    {
        public const string Id = "HairLines/GuideTools/DisplaySeparator";
    }

    internal abstract class HairGuideSettingToolbarToggle : EditorToolbarToggle
    {
        protected abstract bool Setting { get; set; }

        protected HairGuideSettingToolbarToggle(string iconName, string tooltip)
        {
            icon = HairGuideToolbarIcons.Load(iconName);
            this.tooltip = tooltip;

            RegisterCallback<ChangeEvent<bool>>(HandleValueChanged);
            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
            SyncState();
        }

        protected virtual void Apply()
        {
            SceneView.RepaintAll();
        }

        private void OnAttachedToPanel(AttachToPanelEvent _)
        {
            EditorApplication.update += SyncState;
            SyncState();
        }

        private void OnDetachedFromPanel(DetachFromPanelEvent _)
        {
            EditorApplication.update -= SyncState;
        }

        private void HandleValueChanged(ChangeEvent<bool> change)
        {
            Setting = change.newValue;
            Apply();
        }

        private void SyncState()
        {
            SetValueWithoutNotify(Setting);
        }
    }

    internal abstract class HairGuideDisplayToolbarToggle : HairGuideSettingToolbarToggle
    {
        protected HairGuideDisplayToolbarToggle(string iconName, string tooltip) : base(iconName, tooltip)
        {
        }

        protected override void Apply()
        {
            HairGuideSculptSession.ApplyDisplay();
        }
    }

    internal class HairGuideSoftSelectionToolbarToggle : HairGuideSettingToolbarToggle
    {
        protected override bool Setting
        {
            get => HairGuideSculptSettings.SoftSelection;
            set => HairGuideSculptSettings.SoftSelection = value;
        }

        public HairGuideSoftSelectionToolbarToggle() : base("select-soft",
            "Soft Selection\nGuides near the selection follow every edit with a falloff over the radius set in " +
            "the Hair Guides panel.")
        {
        }
    }

    internal class HairGuideMirrorToolbarToggle : HairGuideSettingToolbarToggle
    {
        protected override bool Setting
        {
            get => HairGuideSculptSettings.MirrorX;
            set => HairGuideSculptSettings.MirrorX = value;
        }

        public HairGuideMirrorToolbarToggle() : base("mirror-x",
            "Mirror X\nRepeat selection and brush strokes on the other side of the scalp, across the centre of " +
            "its mesh.")
        {
        }
    }

    internal class HairGuideXRayToolbarToggle : HairGuideDisplayToolbarToggle
    {
        protected override bool Setting
        {
            get => HairGuideSculptSettings.XRay;
            set => HairGuideSculptSettings.XRay = value;
        }

        public HairGuideXRayToolbarToggle() : base("view-xray",
            "X-Ray\nShow guides through objects. When off, objects hide the guides behind them; the hair never does.")
        {
        }
    }

    internal class HairGuideShowHairToolbarToggle : HairGuideDisplayToolbarToggle
    {
        protected override bool Setting
        {
            get => HairGuideSculptSettings.ShowHair;
            set => HairGuideSculptSettings.ShowHair = value;
        }

        public HairGuideShowHairToolbarToggle() : base("view-hair", "Hair\nShow the hair while editing guides.")
        {
        }
    }

    internal class HairGuideSimulationToolbarToggle : HairGuideDisplayToolbarToggle
    {
        protected override bool Setting
        {
            get => HairGuideSculptSettings.Simulate;
            set => HairGuideSculptSettings.Simulate = value;
        }

        public HairGuideSimulationToolbarToggle() : base("view-simulation",
            "Simulation\nRun the hair simulation while editing guides. Off shows the rest pose.")
        {
        }
    }

    internal abstract class HairGuideToolbarSeparatorElement : VisualElement
    {
        private const float Gap = 6f;

        protected HairGuideToolbarSeparatorElement()
        {
            style.flexShrink = 0f;
            style.alignSelf = Align.Stretch;
            style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            HairGuideToolbarLayout.FollowOrientation(this, vertical =>
            {
                style.width = vertical ? StyleKeyword.Auto : 1f;
                style.height = vertical ? 1f : StyleKeyword.Auto;
                style.marginLeft = vertical ? 0f : Gap;
                style.marginRight = vertical ? 0f : Gap;
                style.marginTop = vertical ? Gap : 0f;
                style.marginBottom = vertical ? Gap : 0f;
            });
        }
    }

    internal class HairGuideSculptModeToolbarToggle : EditorToolbarToggle
    {
        private readonly HairGuideEditMode _mode;

        public static HairGuideSculptModeToolbarToggle For(HairGuideEditMode mode)
        {
            return mode switch
            {
                HairGuideEditMode.Points => new(mode, "points", "Points (1)\nEdit individual control points."),
                HairGuideEditMode.Grab => new(mode, "grab", "Grab (2)\nPull guide points under the brush."),
                HairGuideEditMode.Comb => new(mode, "comb", "Comb (3)\nAlign guide segments to the stroke direction."),
                HairGuideEditMode.Smooth => new(mode, "smooth", "Smooth (4)\nRelax bends and kinks."),
                HairGuideEditMode.Length => new(mode, "length",
                    "Length (5)\nDrag up to grow guides and down to shorten them."),
                HairGuideEditMode.Puff => new(mode, "puff", "Puff (6)\nStand guides up from the scalp."),
                HairGuideEditMode.Twist => new(mode, "twist",
                    "Twist (7)\nWind guides around the brush centre. Ctrl/Cmd twists back."),
                HairGuideEditMode.Curl => new(mode, "curl",
                    "Curl (8)\nWind guides into a helix. Ctrl/Cmd straightens them."),
                HairGuideEditMode.Clump => new(mode, "clump",
                    "Clump (9)\nPull guides together. Ctrl/Cmd pushes them apart."),
                HairGuideEditMode.Add => new(mode, "add", "Add (0)\nClick the scalp to add a guide."),
                HairGuideEditMode.Density => new(mode, "density",
                    "Density\nPaint the scalp to add or remove guides down to a set spacing."),
                _ => new(mode, "freeze",
                    "Freeze\nPaint over guides to lock them against every brush. Ctrl/Cmd unfreezes."),
            };
        }

        private HairGuideSculptModeToolbarToggle(HairGuideEditMode mode, string iconName, string tooltip)
        {
            _mode = mode;
            icon = HairGuideToolbarIcons.Load(iconName);
            this.tooltip = tooltip;

            RegisterCallback<ChangeEvent<bool>>(HandleValueChanged);
            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
            SyncState();
        }

        private void OnAttachedToPanel(AttachToPanelEvent _)
        {
            EditorApplication.update += SyncState;
            ToolManager.activeToolChanged += SyncState;
            SyncState();
        }

        private void OnDetachedFromPanel(DetachFromPanelEvent _)
        {
            EditorApplication.update -= SyncState;
            ToolManager.activeToolChanged -= SyncState;
        }

        private void HandleValueChanged(ChangeEvent<bool> change)
        {
            if (!change.newValue)
            {
                SetValueWithoutNotify(true);
                return;
            }

            HairGuideSculptSettings.ToolMode = HairGuideToolMode.Edit;
            HairGuideSculptSettings.Mode = _mode;
            SceneView.RepaintAll();
        }

        private void SyncState()
        {
            SetValueWithoutNotify(HairGuideSculptSettings.ToolMode == HairGuideToolMode.Edit &&
                                  HairGuideSculptSettings.Mode == _mode);
        }
    }

    internal abstract class HairGuideSelectionModeToolbarToggle : EditorToolbarToggle
    {
        private readonly HairGuideSelectionMode _mode;

        protected HairGuideSelectionModeToolbarToggle(HairGuideSelectionMode mode, string iconName, string tooltip)
        {
            _mode = mode;
            icon = HairGuideToolbarIcons.Load(iconName);
            this.tooltip = tooltip;

            RegisterCallback<ChangeEvent<bool>>(HandleValueChanged);
            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
            SyncState();
        }

        private void OnAttachedToPanel(AttachToPanelEvent _)
        {
            EditorApplication.update += SyncState;
            SyncState();
        }

        private void OnDetachedFromPanel(DetachFromPanelEvent _)
        {
            EditorApplication.update -= SyncState;
        }

        private void HandleValueChanged(ChangeEvent<bool> change)
        {
            if (!change.newValue)
            {
                SetValueWithoutNotify(true);
                return;
            }

            HairGuideSculptSettings.ToolMode = HairGuideToolMode.Selection;
            HairGuideSculptSettings.SelectionMode = _mode;
            SceneView.RepaintAll();
        }

        private void SyncState()
        {
            SetValueWithoutNotify(HairGuideSculptSettings.ToolMode == HairGuideToolMode.Selection &&
                                  HairGuideSculptSettings.SelectionMode == _mode);
        }
    }

    internal abstract class HairGuideSelectionToolbarButton : EditorToolbarButton
    {
        protected HairGuideSelectionToolbarButton(string iconName, string tooltip, System.Action action)
        {
            icon = HairGuideToolbarIcons.Load(iconName);
            this.tooltip = tooltip;
            clicked += () =>
            {
                HairGuideSculptSettings.ToolMode = HairGuideToolMode.Selection;
                action();
                SceneView.RepaintAll();
            };
        }
    }

    internal abstract class HairGuideOperationToolbarDropdown : EditorToolbarDropdown
    {
        protected HairGuideOperationToolbarDropdown(string iconName, string tooltip)
        {
            icon = HairGuideToolbarIcons.Load(iconName);
            this.tooltip = tooltip;
            clicked += Open;
        }

        protected abstract void Open();
    }

    internal class HairGuideResampleToolbarDropdown : HairGuideOperationToolbarDropdown
    {
        public HairGuideResampleToolbarDropdown() : base("resample",
            "Resample\nRebuild the selected guides with a chosen number of segments, spaced by the group's " +
            "Segment Length Distribution.")
        {
        }

        protected override void Open()
        {
            UnityEditor.PopupWindow.Show(worldBound, new HairGuideResamplePopup());
        }
    }

    internal class HairGuideResamplePopup : PopupWindowContent
    {
        private const float Width = 240f;

        public override Vector2 GetWindowSize()
        {
            return new Vector2(Width, EditorGUIUtility.singleLineHeight * 3f + 16f);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUIUtility.labelWidth = 70f;
            EditorGUILayout.LabelField($"Resample {HairGuideSculptSession.SelectionCount} selected guide(s)",
                                       EditorStyles.miniBoldLabel);
            HairGuideSculptSettings.ResampleSegments = EditorGUILayout.IntSlider(
                "Segments", HairGuideSculptSettings.ResampleSegments, 1, 128);
            using (new EditorGUI.DisabledScope(!HairGuideSculptSession.HasSelection))
            {
                if (GUILayout.Button("Resample"))
                {
                    HairGuideSculptSession.ResampleSelectedGuides(HairGuideSculptSettings.ResampleSegments);
                    editorWindow.Close();
                }
            }
        }
    }

    internal class HairGuideMoveToGroupToolbarDropdown : HairGuideOperationToolbarDropdown
    {
        public HairGuideMoveToGroupToolbarDropdown() : base("move-group",
            "Move to Group\nMove or copy the selected guides to another group of this Hair Lines renderer.")
        {
        }

        protected override void Open()
        {
            var menu = new GenericMenu();
            List<HairGroup> targets = HairGuideSculptSession.TransferTargets();
            if (targets.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No other Custom group on this renderer"));
            }
            else if (!HairGuideSculptSession.HasSelection)
            {
                menu.AddDisabledItem(new GUIContent("Select guides first"));
            }
            else
            {
                foreach (HairGroup target in targets)
                {
                    menu.AddItem(new GUIContent($"Move to/{target.name}"), false,
                                 () => HairGuideSculptSession.TransferSelectedGuides(target, false));
                }

                foreach (HairGroup target in targets)
                {
                    menu.AddItem(new GUIContent($"Copy to/{target.name}"), false,
                                 () => HairGuideSculptSession.TransferSelectedGuides(target, true));
                }
            }

            menu.ShowAsContext();
        }
    }

    internal class HairGuideFreezeSelectionToolbarDropdown : HairGuideOperationToolbarDropdown
    {
        public HairGuideFreezeSelectionToolbarDropdown() : base("freeze-selection",
            "Freeze Selection\nLock the selected guides against every brush, or release them. With Soft Selection the " +
            "lock fades out over its radius.")
        {
        }

        protected override void Open()
        {
            var menu = new GenericMenu();
            bool selection = HairGuideSculptSession.HasSelection;
            AddItem(menu, "Freeze Selected", selection, () => HairGuideSculptSession.FreezeSelectedGuides(true));
            AddItem(menu, "Unfreeze Selected", selection, () => HairGuideSculptSession.FreezeSelectedGuides(false));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Unfreeze All"), false, HairGuideSculptSession.UnfreezeAllGuides);
            menu.ShowAsContext();
        }

        private static void AddItem(GenericMenu menu, string label, bool enabled, GenericMenu.MenuFunction action)
        {
            if (enabled)
            {
                menu.AddItem(new GUIContent(label), false, action);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(label));
            }
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideClickSelectionToolbarToggle : HairGuideSelectionModeToolbarToggle
    {
        public const string Id = "HairLines/GuideSelection/Click";

        public HairGuideClickSelectionToolbarToggle() : base(HairGuideSelectionMode.Click, "select-click",
            "Click (1)\nSelect one curve. Shift adds and Ctrl/Cmd toggles.")
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideBrushSelectionToolbarToggle : HairGuideSelectionModeToolbarToggle
    {
        public const string Id = "HairLines/GuideSelection/Brush";

        public HairGuideBrushSelectionToolbarToggle() : base(HairGuideSelectionMode.Brush, "select-brush",
            "Brush (2)\nPaint over guides to select them. Ctrl/Cmd removes them, Shift+Wheel changes the radius.")
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideCircleSelectionToolbarToggle : HairGuideSelectionModeToolbarToggle
    {
        public const string Id = "HairLines/GuideSelection/Circle";

        public HairGuideCircleSelectionToolbarToggle() : base(HairGuideSelectionMode.Circle, "select-circle",
            "Circle (3)\nDrag a circular selection around guides.")
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideAreaSelectionToolbarToggle : HairGuideSelectionModeToolbarToggle
    {
        public const string Id = "HairLines/GuideSelection/Area";

        public HairGuideAreaSelectionToolbarToggle() : base(HairGuideSelectionMode.Area, "select-area",
            "Area (4)\nDrag a rectangular selection around guides.")
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideSelectAllToolbarButton : HairGuideSelectionToolbarButton
    {
        public const string Id = "HairLines/GuideSelection/All";

        public HairGuideSelectAllToolbarButton() : base("select-all", "Select All\nSelect every guide.",
            HairGuideSculptSession.SelectAllGuides)
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideClearSelectionToolbarButton : HairGuideSelectionToolbarButton
    {
        public const string Id = "HairLines/GuideSelection/Clear";

        public HairGuideClearSelectionToolbarButton() : base("select-clear", "Clear Selection\nClear the current guide selection.",
            HairGuideSculptSession.ClearGuideSelection)
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class HairGuideInvertSelectionToolbarButton : HairGuideSelectionToolbarButton
    {
        public const string Id = "HairLines/GuideSelection/Invert";

        public HairGuideInvertSelectionToolbarButton() : base("select-invert", "Invert Selection\nInvert the current guide selection.",
            HairGuideSculptSession.InvertGuideSelection)
        {
        }
    }
}
