using System;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace HairLines.EditorTools
{
    public class HairGrowSettingsGUI
    {
        public bool RandomizeSeedRequested { get; private set; }
        private bool _colorMapsChanged;

        public bool TakeColorMapsChanged()
        {
            bool changed = _colorMapsChanged;
            _colorMapsChanged = false;
            return changed;
        }
        public int RandomizeSeedForOperator => _operatorStack.RandomizeSeedForOperator;

        private readonly SerializedProperty _seed;
        private readonly SerializedProperty _strandCount;
        private readonly SerializedProperty _segmentsPerStrand;
        private readonly SerializedProperty _maskTexture;
        private readonly SerializedProperty _combTexture;
        private readonly HairOperatorStackGUI _operatorStack;
        private readonly SerializedProperty _strandLength;
        private readonly SerializedProperty _lengthVariance;
        private readonly SerializedProperty _segmentLengthDistribution;
        private readonly SerializedProperty _scalpFlow;
        private readonly SerializedProperty _gravity;
        private readonly SerializedProperty _stiffness;
        private readonly SerializedProperty _guideCount;
        private readonly SerializedProperty _material;
        private readonly SerializedProperty _tint;
        private readonly SerializedProperty _rootColorMap;
        private readonly SerializedProperty _tipColorMap;
        private readonly SerializedProperty _tintMap;
        private readonly SerializedProperty _lodPriority;
        private readonly SerializedProperty _colorVariance;
        private readonly SerializedProperty _tipOpacity;
        private readonly SerializedProperty _guideList;

        private static readonly GUIContent _densityMaskLabel = new GUIContent("Density Mask");
        private static readonly GUIContent _combMapLabel = new GUIContent("Comb Map");

        private const float SeedButtonWidth = 80f;

        public HairGrowSettingsGUI(SerializedProperty settings)
        {
            _seed = settings.FindPropertyRelative("_seed");
            _strandCount = settings.FindPropertyRelative("_strandCount");
            _segmentsPerStrand = settings.FindPropertyRelative("_segmentsPerStrand");
            _maskTexture = settings.FindPropertyRelative("_densityMask").FindPropertyRelative("_texture");
            _combTexture = settings.FindPropertyRelative("_combMap").FindPropertyRelative("_texture");
            _operatorStack = new HairOperatorStackGUI(settings);
            _strandLength = settings.FindPropertyRelative("_strandLength");
            _lengthVariance = settings.FindPropertyRelative("_lengthVariance");
            _segmentLengthDistribution = settings.FindPropertyRelative("_segmentLengthDistribution");
            _scalpFlow = settings.FindPropertyRelative("_scalpFlow");
            _gravity = settings.FindPropertyRelative("_gravity");
            _stiffness = settings.FindPropertyRelative("_stiffness");
            _guideCount = settings.FindPropertyRelative("_guideCount");
            _guideList = settings.FindPropertyRelative("_guides")?.FindPropertyRelative("_guides");
            _lodPriority = settings.FindPropertyRelative("_lodPriority");
            _colorVariance = settings.FindPropertyRelative("_colorVariance");
            _material = settings.FindPropertyRelative("_material");
            _tint = settings.FindPropertyRelative("_tint");
            _rootColorMap = settings.FindPropertyRelative("_rootColorMap").FindPropertyRelative("_texture");
            _tipColorMap = settings.FindPropertyRelative("_tipColorMap").FindPropertyRelative("_texture");
            _tintMap = settings.FindPropertyRelative("_tintMap").FindPropertyRelative("_texture");
            _tipOpacity = settings.FindPropertyRelative("_tipOpacity");
        }

        private int MaxGuidePointCount()
        {
            if (_guideList == null || _guideList.hasMultipleDifferentValues)
            {
                return 0;
            }

            int max = 0;
            for (int i = 0; i < _guideList.arraySize; i++)
            {
                SerializedProperty points = _guideList.GetArrayElementAtIndex(i).FindPropertyRelative("_points");
                if (points != null)
                {
                    max = Mathf.Max(max, points.arraySize);
                }
            }
            return Mathf.Min(max, 129);
        }

        public void DrawGuideCount()
        {
            EditorGUILayout.PropertyField(_guideCount, new GUIContent("Guide Strands", _guideCount.tooltip));
        }

        public void DrawMaterial()
        {
            EditorGUILayout.PropertyField(_material);
            EditorGUILayout.PropertyField(_tint);
            HairInspectorGUI.Note("An empty material inherits the parent hair material. " +
                                  "Tint multiplies this group's material colour. No rebuild needed.");

            EditorGUI.BeginChangeCheck();
            DrawColorMap(_rootColorMap, "Root Colour Map");
            DrawColorMap(_tipColorMap, "Tip Colour Map");
            DrawColorMap(_tintMap, "Tint Map");
            _colorMapsChanged |= EditorGUI.EndChangeCheck();

            var material = _material.objectReferenceValue as Material;

            if (!_material.hasMultipleDifferentValues && material != null &&
                material.shader != null && material.shader.name != LineShaderIds.LineShaderName)
            {
                EditorGUILayout.HelpBox($"This material uses '{material.shader.name}'. Strands are " +
                                        $"drawn by '{LineShaderIds.LineShaderName}'.", MessageType.Warning);
            }
        }

        private static void DrawColorMap(SerializedProperty texture, string label)
        {
            EditorGUILayout.PropertyField(texture, new GUIContent(label, texture.tooltip));

            if (texture.objectReferenceValue is Texture2D map && !map.isReadable)
            {
                EditorGUILayout.HelpBox($"'{map.name}' is not readable, so it is ignored. " +
                                        "Enable Read/Write in its import settings.", MessageType.Error);
            }
        }

        public void DrawAppearance()
        {
            EditorGUILayout.PropertyField(_lodPriority, new GUIContent("LOD Priority"));
            EditorGUILayout.PropertyField(_colorVariance);
            EditorGUILayout.PropertyField(_tipOpacity);

            if (!_tipOpacity.hasMultipleDifferentValues && _tipOpacity.floatValue < 1f)
            {
                HairInspectorGUI.Note("Tips thin out only with Stochastic Opacity on the material.");
            }
        }

        public void Draw(bool showDensityMask, bool allowEditing = false, bool inlineSections = true)
        {
            RandomizeSeedRequested = HairInspectorGUI.PropertyWithButton(_seed, "Randomize", SeedButtonWidth,
                "Pick a new seed and rebuild.");

            HairInspectorGUI.SubHeader("Density");
            EditorGUILayout.PropertyField(_strandCount);
            int guideSegments = MaxGuidePointCount() - 1;
            if (guideSegments >= 1)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.IntField(new GUIContent(_segmentsPerStrand.displayName,
                        "Set by the guides: strands get the segment count of the guide with the most points."),
                        guideSegments);
                }
            }
            else
            {
                EditorGUILayout.PropertyField(_segmentsPerStrand);
            }

            if (!_strandCount.hasMultipleDifferentValues && !_segmentsPerStrand.hasMultipleDifferentValues)
            {
                int segments = guideSegments >= 1 ? guideSegments : _segmentsPerStrand.intValue;
                long vertices = (long)_strandCount.intValue * (segments + 1);
                long edges = (long)_strandCount.intValue * segments;
                HairInspectorGUI.Note($"{HairInspectorGUI.Count(vertices)} vertices, " +
                                      $"{HairInspectorGUI.Count(edges)} edges after a rebuild");
            }

            if (showDensityMask)
            {
                DrawDensityMask(allowEditing);
            }

            DrawCombMap(allowEditing);

            HairInspectorGUI.SubHeader("Shape");
            EditorGUILayout.PropertyField(_strandLength);
            EditorGUILayout.PropertyField(_lengthVariance);
            EditorGUILayout.PropertyField(_segmentLengthDistribution,
                new GUIContent("Segment Length Distribution", _segmentLengthDistribution.tooltip));
            EditorGUILayout.PropertyField(_scalpFlow);
            EditorGUILayout.PropertyField(_gravity);
            EditorGUILayout.PropertyField(_stiffness);

            _operatorStack.Draw(allowEditing);

            if (!inlineSections)
            {
                return;
            }

            HairInspectorGUI.SubHeader("Guides");
            DrawGuideCount();

            HairInspectorGUI.SubHeader("Appearance");
            DrawMaterial();
            DrawAppearance();
        }

        private void DrawDensityMask(bool allowEditing)
        {
            HairInspectorGUI.SubHeader("Mask");

            if (_maskTexture.hasMultipleDifferentValues)
            {
                HairInspectorGUI.PropertyWithMaskButtons(_maskTexture, _densityMaskLabel,
                                                         false, false,
                                                         out bool ignoredCreate, out bool ignoredEdit);
                return;
            }

            Texture2D texture = _maskTexture.objectReferenceValue as Texture2D;
            HairInspectorGUI.PropertyWithMaskButtons(
                _maskTexture, _densityMaskLabel,
                allowEditing && texture != null && texture.isReadable,
                IsScalarEditActive(texture),
                out bool createClicked, out bool editClicked);

            if (createClicked)
            {
                string defaultName = _maskTexture.serializedObject.targetObject != null
                    ? $"{_maskTexture.serializedObject.targetObject.name} Density Mask"
                    : "Density Mask";
                HairMaskCreateWindow.Open(defaultName, HairMaskCreateWindow.MaskKind.Density,
                                          _maskTexture);
            }

            if (editClicked)
            {
                ToggleScalarEdit(_maskTexture, texture, "Paint Hair Density", InvalidateDensityMasks);
            }

            if (texture == null)
            {
                HairInspectorGUI.Note("No mask - this group grows over the whole scalp " +
                                      "independently of other groups.");
                return;
            }

            if (!texture.isReadable)
            {
                EditorGUILayout.HelpBox($"'{texture.name}' is not readable, so it is ignored. " +
                                        "Enable Read/Write in its import settings.", MessageType.Error);
                return;
            }

            HairInspectorGUI.Stat("Resolution", $"{texture.width} x {texture.height}");
        }

        private void DrawCombMap(bool allowEditing)
        {
            HairInspectorGUI.SubHeader("Comb");

            if (_combTexture.hasMultipleDifferentValues)
            {
                HairInspectorGUI.PropertyWithMaskButtons(_combTexture, _combMapLabel,
                                                         false, false,
                                                         out bool ignoredCreate, out bool ignoredEdit);
                return;
            }

            Texture2D texture = _combTexture.objectReferenceValue as Texture2D;
            HairInspectorGUI.PropertyWithMaskButtons(
                _combTexture, _combMapLabel,
                allowEditing && texture != null && texture.isReadable,
                IsCombEditActive(texture),
                out bool createClicked, out bool editClicked);

            if (createClicked)
            {
                string defaultName = _combTexture.serializedObject.targetObject != null
                    ? $"{_combTexture.serializedObject.targetObject.name} Comb Map"
                    : "Comb Map";
                HairMaskCreateWindow.Open(defaultName, HairMaskCreateWindow.MaskKind.Comb,
                                          _combTexture);
            }

            if (editClicked)
            {
                ToggleCombEdit(_combTexture, texture, InvalidateCombMaps);
            }

            if (texture == null)
            {
                HairInspectorGUI.Note("No comb map - strands leave the scalp along the root normal.");
                return;
            }

            if (!texture.isReadable)
            {
                EditorGUILayout.HelpBox($"'{texture.name}' is not readable, so it is ignored. " +
                                        "Enable Read/Write in its import settings.", MessageType.Error);
                return;
            }

            HairInspectorGUI.Stat("Resolution", $"{texture.width} x {texture.height}");

            if (_guideList != null && _guideList.arraySize > 0)
            {
                HairInspectorGUI.Note("Guides hold the shape, so the comb map reaches the hair through " +
                                      "Generate Guides - regenerate them after painting.");
            }
        }

        private static bool IsScalarEditActive(Texture2D texture)
        {
            return texture != null && ToolManager.activeToolType == typeof(HairMaskPaintTool) &&
                   HairMaskPaintTool.ConfiguredTexture == texture;
        }

        private static void ToggleScalarEdit(SerializedProperty property, Texture2D texture,
                                             string undoName, Action invalidate)
        {
            if (IsScalarEditActive(texture))
            {
                ToolManager.RestorePreviousPersistentTool();
                return;
            }

            if (!HairInspectorGUI.TryResolvePaintTarget(property, out LinePrimitiveRenderer hair,
                                                        out HairGroup group))
            {
                return;
            }

            HairMaskPaintTool.Configure(hair, group, texture, invalidate, undoName);
            ToolManager.SetActiveTool<HairMaskPaintTool>();
        }

        private static void ToggleCombEdit(SerializedProperty property, Texture2D texture, Action invalidate)
        {
            if (IsCombEditActive(texture))
            {
                ToolManager.RestorePreviousPersistentTool();
            }
            else
            {
                if (!HairInspectorGUI.TryResolvePaintTarget(property, out LinePrimitiveRenderer hair,
                                                            out HairGroup group))
                {
                    return;
                }

                HairCombPaintTool.Configure(hair, group, texture, invalidate, "Comb Hair");
                ToolManager.SetActiveTool<HairCombPaintTool>();
            }
        }

        private static bool IsCombEditActive(Texture2D texture)
        {
            return texture != null && ToolManager.activeToolType == typeof(HairCombPaintTool) &&
                   HairCombPaintTool.ConfiguredTexture == texture;
        }

        private void InvalidateDensityMasks()
        {
            foreach (UnityEngine.Object target in _maskTexture.serializedObject.targetObjects)
            {
                if (target is HairGroup group)
                {
                    group.Settings.DensityMask.Invalidate();
                }
            }
        }

        private void InvalidateCombMaps()
        {
            foreach (UnityEngine.Object target in _combTexture.serializedObject.targetObjects)
            {
                if (target is HairGroup group)
                {
                    group.Settings.CombMap.Invalidate();
                }
            }
        }
    }
}
