using System;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;

namespace HairLines.EditorTools
{
    [InitializeOnLoad]
    [Overlay(typeof(SceneView), OverlayId, "Hair Brush")]
    public class HairMaskBrushOverlay : IMGUIOverlay, ITransientOverlay
    {
        static HairMaskBrushOverlay()
        {
            Selection.selectionChanged -= HandleSelectionChanged;
            Selection.selectionChanged += HandleSelectionChanged;
            ToolManager.activeToolChanged -= HandleToolChanged;
            ToolManager.activeToolChanged += HandleToolChanged;
        }

        bool ITransientOverlay.visible => IsBrushTool(ToolManager.activeToolType);

        internal static LinePrimitiveRenderer CurrentOwner => _owner;

        internal static bool ShowHair
        {
            get => EditorPrefs.GetBool(ShowHairKey, false);
            set
            {
                if (EditorPrefs.GetBool(ShowHairKey, false) == value)
                {
                    return;
                }

                EditorPrefs.SetBool(ShowHairKey, value);
                ApplyOwnerVisibility();
            }
        }

        internal static bool Hovering { get; set; }

        internal static string Target { get; set; }

        internal static int Strokes { get; set; }

        public static string Blocker
        {
            get => _blocker;
            set
            {
                if (_blocker == value)
                {
                    return;
                }

                _blocker = value;
                SceneView.RepaintAll();
            }
        }

        private static GUIContent[] _modes;
        private static string _blocker;
        private static LinePrimitiveRenderer _owner;

        private const string OverlayId = "hairlines-brush";
        private const string ShowHairKey = "HairLines.MaskEditor.ShowHair";
        private const float LabelWidth = 68f;
        private const float PanelWidth = 244f;
        private const float ModeHeight = 22f;

        public override void OnGUI()
        {
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = LabelWidth;

            GUILayout.BeginVertical(GUILayout.Width(PanelWidth));

            if (string.IsNullOrEmpty(_blocker))
            {
                DrawBrush();
            }
            else
            {
                EditorGUILayout.HelpBox(_blocker, MessageType.Warning);
            }

            GUILayout.EndVertical();

            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        internal static bool IsBrushTool(Type tool)
        {
            return tool == typeof(HairMaskPaintTool) || tool == typeof(HairCombPaintTool);
        }

        private static void DrawBrush()
        {
            DrawModes();
            GUILayout.Space(4f);

            HairMaskBrush.Diameter = EditorGUILayout.FloatField("Diameter", HairMaskBrush.Diameter);
            HairMaskBrush.Strength = EditorGUILayout.Slider("Strength", HairMaskBrush.Strength, 0f, 1f);
            HairMaskBrush.Hardness = EditorGUILayout.Slider("Hardness", HairMaskBrush.Hardness, 0f, 1f);
            HairMaskBrush.MirrorX = EditorGUILayout.Toggle("Mirror X", HairMaskBrush.MirrorX);

            GUILayout.Space(4f);
            bool showHair = EditorGUILayout.Toggle("Show Hair", ShowHair);
            if (showHair != ShowHair)
            {
                ShowHair = showHair;
            }

            HairMaskPreview.Enabled = EditorGUILayout.Toggle("Show Map", HairMaskPreview.Enabled);

            using (new EditorGUI.DisabledScope(!HairMaskPreview.Enabled))
            {
                HairMaskPreview.Opacity = EditorGUILayout.Slider("Opacity", HairMaskPreview.Opacity, 0f, 1f);
                HairMaskPreview.ShowCoverage = EditorGUILayout.Toggle(
                    new GUIContent("Show Coverage",
                                   "Colour the scalp by the group that grew there. Red is painted " +
                                   "but grown by nobody."),
                    HairMaskPreview.ShowCoverage);
            }

            EditorGUILayout.LabelField("Hold shift to flip the mode, shift+wheel for size",
                                       EditorStyles.miniLabel);

            DrawStatus();
        }

        private static void DrawStatus()
        {
            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Painting into", string.IsNullOrEmpty(Target) ? "nothing" : Target,
                                       EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Cursor on scalp", Hovering ? "yes" : "no", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Strokes this session", Strokes.ToString(), EditorStyles.miniLabel);
        }

        private static void DrawModes()
        {
            _modes ??= new[]
            {
                new GUIContent("  Paint", EditorGUIUtility.IconContent("Grid.PaintTool").image),
                new GUIContent("  Erase", EditorGUIUtility.IconContent("Grid.EraserTool").image),
            };

            int current = HairMaskBrush.Erase ? 1 : 0;
            int picked = GUILayout.Toolbar(current, _modes, GUILayout.Height(ModeHeight));

            if (picked != current)
            {
                HairMaskBrush.Erase = picked == 1;
                SceneView.RepaintAll();
            }
        }

        internal static void SetOwner(LinePrimitiveRenderer owner)
        {
            if (_owner != owner)
            {
                if (_owner != null)
                {
                    LinePrimitiveRenderer.SetEditorVisibility(_owner, true);
                }

                _owner = owner;
                ApplyOwnerVisibility();
            }
        }

        internal static void SyncOwner()
        {
            SetOwner(ResolveSelectedOwner());
        }

        internal static void ClearOwner()
        {
            if (_owner == null)
            {
                return;
            }

            if (_owner != null)
            {
                LinePrimitiveRenderer.SetEditorVisibility(_owner, true);
            }

            _owner = null;
            SceneView.RepaintAll();
        }

        private static void ApplyOwnerVisibility()
        {
            if (_owner != null)
            {
                LinePrimitiveRenderer.SetEditorVisibility(_owner, ShowHair);
            }

            SceneView.RepaintAll();
        }

        private static LinePrimitiveRenderer ResolveSelectedOwner()
        {
            GameObject selected = Selection.activeGameObject;
            return selected != null
                ? selected.GetComponentInParent<LinePrimitiveRenderer>()
                : null;
        }

        private static void HandleSelectionChanged()
        {
            if (!IsBrushTool(ToolManager.activeToolType))
            {
                ClearOwner();
            }
        }

        private static void HandleToolChanged()
        {
            if (!IsBrushTool(ToolManager.activeToolType))
            {
                ClearOwner();
            }
        }
    }
}
