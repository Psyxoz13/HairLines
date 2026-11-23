using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace HairLines.EditorTools
{
    [CustomEditor(typeof(HairGroup))]
    [CanEditMultipleObjects]
    public class HairGroupEditor : UnityEditor.Editor
    {
        private SerializedProperty _mode;
        private SerializedProperty _preset;
        private SerializedProperty _settings;
        private HairGrowSettingsGUI _settingsGui;
        private bool _generateGuides;
        private bool _clearGuides;
        private bool _rebuildPending;

        private const string Prefix = "HairLines.Group.";
        private const float RowGap = 6f;

        private void OnEnable()
        {
            ToolManager.activeToolChanged += Repaint;
            Bind();
        }

        private void OnDisable()
        {
            ToolManager.activeToolChanged -= Repaint;
        }

        private void Bind()
        {
            _mode = serializedObject.FindProperty("_mode");
            _preset = serializedObject.FindProperty("_preset");
            _settings = serializedObject.FindProperty("_settings");
            _settingsGui = new HairGrowSettingsGUI(_settings);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawOwnerWarning();
            bool modeWasMixed = _mode.hasMultipleDifferentValues;
            bool presetWasMixed = _preset.hasMultipleDifferentValues;
            int previousMode = _mode.enumValueIndex;
            Object previousPreset = _preset.objectReferenceValue;
            DrawMode();
            bool sourceChanged = modeWasMixed != _mode.hasMultipleDifferentValues ||
                                 presetWasMixed != _preset.hasMultipleDifferentValues ||
                                 previousMode != _mode.enumValueIndex ||
                                 previousPreset != _preset.objectReferenceValue;

            bool presetDrivesGroom = !_mode.hasMultipleDifferentValues &&
                                     (HairGroupMode)_mode.enumValueIndex == HairGroupMode.Preset;

            int lookBefore = 0;

            if (HairInspectorGUI.BeginSection(Prefix + "Look", "Look", MaterialSummary()))
            {
                if (presetDrivesGroom)
                {
                    HairInspectorGUI.Note("Material, tint and the rest of the look are read from " +
                                          "the preset. Switch to Custom to change them here.");
                }
                else
                {
                    lookBefore = HairInspectorGUI.HashProperties(_settings);
                    _settingsGui.DrawMaterial();
                    _settingsGui.DrawAppearance();
                }
            }
            HairInspectorGUI.EndSection();

            bool appearanceChanged = !presetDrivesGroom &&
                                     lookBefore != HairInspectorGUI.HashProperties(_settings);

            bool usesPreset = presetDrivesGroom;
            if (usesPreset)
            {
                HairInspectorGUI.Note("Grooming parameters are read from the selected " +
                                      "ScriptableObject preset.");
            }
            else
            {
                if (HairInspectorGUI.BeginSection(Prefix + "Guides", "Guides", GuideSummary()))
                {
                    DrawGuides();
                }
                HairInspectorGUI.EndSection();

                if (HairInspectorGUI.BeginSection(Prefix + "Groom", "Groom", StrandSummary()))
                {
                    int groomBefore = HairInspectorGUI.HashProperties(_settings);
                    _settingsGui.Draw(true, true, false);
                    _rebuildPending |= groomBefore != HairInspectorGUI.HashProperties(_settings);
                }
                HairInspectorGUI.EndSection();
            }

            DrawSimulationLink();

            serializedObject.ApplyModifiedProperties();

            if (appearanceChanged)
            {
                RefreshOwnerAppearance();
            }

            if (sourceChanged || (!presetDrivesGroom && _settingsGui.TakeColorMapsChanged()))
            {
                RebuildOwner();
            }

            if (!usesPreset && _settingsGui.RandomizeSeedRequested)
            {
                RandomizeSeed();
            }

            if (!usesPreset && _generateGuides)
            {
                _generateGuides = false;
                GenerateGuides();
            }

            if (!usesPreset && _clearGuides)
            {
                _clearGuides = false;
                ClearGuides();
            }

            if (!usesPreset && _settingsGui.RandomizeSeedForOperator >= 0)
            {
                RebuildOwner();
            }

            if (!usesPreset)
            {
                DrawSavePresetButton();
            }

            if (_rebuildPending && GUIUtility.hotControl == 0 && Event.current.type == EventType.Repaint)
            {
                _rebuildPending = false;
                RebuildOwner();
            }
        }

        private void DrawMode()
        {
            if (HairInspectorGUI.BeginSection(Prefix + "Mode", "Groom Mode", ModeSummary()))
            {
                EditorGUILayout.PropertyField(_mode, new GUIContent("Mode"));
                if (!_mode.hasMultipleDifferentValues &&
                    (HairGroupMode)_mode.enumValueIndex == HairGroupMode.Preset)
                {
                    EditorGUILayout.PropertyField(_preset, new GUIContent("Preset"));
                    if (_preset.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox("Assign a preset or switch to Custom.", MessageType.Warning);
                    }
                }
            }
            HairInspectorGUI.EndSection();
        }

        private void DrawGuides()
        {
            var group = (HairGroup)target;
            HairGuideSet guides = group.Settings.Guides;
            LinePrimitiveRenderer owner = group.GetComponentInParent<LinePrimitiveRenderer>();
            bool meshScalp = owner != null && owner.Scalp.Mode == HairScalpMode.Mesh;

            if (!meshScalp)
            {
                EditorGUILayout.HelpBox("Guides need the Mesh scalp mode on the Hair Lines renderer above.",
                                        MessageType.Warning);
            }

            _settingsGui.DrawGuideCount();

            if (guides.IsEmpty)
            {
                HairInspectorGUI.Note("No guides - strands are grown one by one and clumped towards " +
                                      "procedural ones. Generate guides to shape the whole groom by hand.");
            }
            else
            {
                HairInspectorGUI.Stat("Curves", HairInspectorGUI.Count(guides.Count));
                int fewestPoints = int.MaxValue;
                foreach (HairGuide guide in guides.Guides)
                {
                    fewestPoints = Mathf.Min(fewestPoints, guide != null ? guide.PointCount : 0);
                }
                int mostPoints = guides.MaxPointCount;
                HairInspectorGUI.Stat("Points per curve", fewestPoints == mostPoints
                    ? $"{mostPoints}"
                    : $"{fewestPoints}–{mostPoints}");
                HairInspectorGUI.Note("Every strand is blended from the nearest guides over its " +
                                      "whole length. Clumping sharpens the blend towards the " +
                                      "nearest one.");
            }

            Rect row = EditorGUILayout.GetControlRect(false, 20f);
            var left = new Rect(row.x, row.y, row.width * 0.5f - RowGap * 0.5f, row.height);
            var right = new Rect(left.xMax + RowGap, row.y, row.width - left.width - RowGap, row.height);

            using (new EditorGUI.DisabledScope(!meshScalp))
            {
                if (GUI.Button(left, new GUIContent(guides.IsEmpty ? "Generate Guides" : "Regenerate",
                                                    "Grow guide curves from the current settings.")))
                {
                    _generateGuides = true;
                }
            }

            using (new EditorGUI.DisabledScope(guides.IsEmpty))
            {
                if (GUI.Button(right, new GUIContent("Clear",
                                                     "Drop the guides and go back to procedural strands.")))
                {
                    _clearGuides = true;
                }
            }

            if (guides.IsEmpty)
            {
                return;
            }

            bool editing = ToolManager.activeToolType == typeof(HairGuideSculptSession);
            bool canEdit = meshScalp && owner != null && group.Mode == HairGroupMode.Custom;
            using (new EditorGUI.DisabledScope(!canEdit))
            {
                if (HairInspectorGUI.Button(editing ? "Stop Editing" : "Edit Guides",
                                             "Select, sculpt, add and freeze guide curves in the Scene view.",
                                             editing, 22f))
                {
                    if (editing)
                    {
                        ToolManager.RestorePreviousPersistentTool();
                    }
                    else
                    {
                        HairGuideSculptSession.Configure(group);
                        ToolManager.SetActiveTool<HairGuideSculptSession>();
                    }
                }
            }
        }

        private void DrawSimulationLink()
        {
            HairGroup group = (HairGroup)target;
            HairGroupSimulation simulation = group.GetComponent<HairGroupSimulation>();
            if (simulation != null)
            {
                return;
            }

            HairInspectorGUI.SubHeader("Simulation");
            if (targets.Length == 1 && HairInspectorGUI.Button("Add Group Simulation",
                "Add an independent physics component to this group."))
            {
                HairGroupSimulation added = Undo.AddComponent<HairGroupSimulation>(group.gameObject);
                if (added != null)
                {
                    EditorUtility.SetDirty(group);
                    Repaint();
                }
            }
        }

        private void GenerateGuides()
        {
            var group = (HairGroup)target;
            LinePrimitiveRenderer owner = group.GetComponentInParent<LinePrimitiveRenderer>();

            Undo.RecordObject(group, "Generate Hair Guides");

            if (!HairGuideBuilder.TryGenerate(group, owner, group.Settings.GuideCount, out string error))
            {
                Debug.LogError($"[HairLines] {error}", group);
                return;
            }

            EditorUtility.SetDirty(group);
            RebuildOwner();
        }

        private void ClearGuides()
        {
            var group = (HairGroup)target;
            Undo.RecordObject(group, "Clear Hair Guides");
            group.Settings.Guides.Clear();
            EditorUtility.SetDirty(group);
            RebuildOwner();
        }

        private void DrawOwnerWarning()
        {
            foreach (Object t in targets)
            {
                var group = (HairGroup)t;

                if (group.GetComponentInParent<LinePrimitiveRenderer>() == null)
                {
                    EditorGUILayout.HelpBox($"'{group.name}' has no Hair Lines renderer above it, " +
                                            "so nothing grows from this group.", MessageType.Warning);
                    return;
                }
            }
        }

        private void RandomizeSeed()
        {
            foreach (Object t in targets)
            {
                ((HairGroup)t).RandomizeSeed();
            }

            serializedObject.Update();
            RebuildOwner();
        }

        private void DrawSavePresetButton()
        {
            if (targets.Length != 1 || (HairGroupMode)_mode.enumValueIndex != HairGroupMode.Custom)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            if (!HairInspectorGUI.Button(
                    "Save to Preset",
                    "Create a reusable ScriptableObject from this group's custom grooming settings."))
            {
                return;
            }

            HairGroup group = (HairGroup)target;
            string path = EditorUtility.SaveFilePanelInProject("Save Hair Group Preset",
                group.name + " Groom", "asset", "Choose where to save the preset.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            HairGroupPreset preset = ScriptableObject.CreateInstance<HairGroupPreset>();
            preset.CopyFrom(group.Settings);
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            _preset.objectReferenceValue = preset;
            _mode.enumValueIndex = (int)HairGroupMode.Preset;
            serializedObject.ApplyModifiedProperties();
            RebuildOwner();
            EditorGUIUtility.PingObject(preset);
        }

        private void RefreshOwnerAppearance()
        {
            foreach (Object t in targets)
            {
                LinePrimitiveRenderer owner = ((HairGroup)t).GetComponentInParent<LinePrimitiveRenderer>();

                if (owner != null)
                {
                    owner.RefreshGroups();
                }
            }

            SceneView.RepaintAll();
        }

        private void RebuildOwner()
        {
            foreach (Object t in targets)
            {
                LinePrimitiveRenderer owner = ((HairGroup)t).GetComponentInParent<LinePrimitiveRenderer>();

                if (owner != null)
                {
                    owner.Rebuild();
                }
            }

            SceneView.RepaintAll();
        }

        private string GuideSummary()
        {
            HairGuideSet guides = ((HairGroup)target).ResolvedSettings.Guides;
            return guides.IsEmpty ? "procedural" : $"{guides.Count} curves";
        }

        private string MaterialSummary()
        {
            if (targets.Length > 1)
            {
                return "mixed";
            }

            HairGrowSettings settings = ((HairGroup)target).ResolvedSettings;
            string materialName = settings.Material != null ? settings.Material.name : "inherited";
            return settings.Tint == Color.white ? materialName : $"{materialName}, tinted";
        }

        private string ModeSummary()
        {
            if (_mode.hasMultipleDifferentValues)
            {
                return "mixed";
            }

            if ((HairGroupMode)_mode.enumValueIndex == HairGroupMode.Custom)
            {
                return "custom";
            }

            return _preset.objectReferenceValue != null ? _preset.objectReferenceValue.name : "no preset";
        }

        private string StrandSummary()
        {
            var group = (HairGroup)target;
            return $"{HairInspectorGUI.Count(group.ResolvedSettings.StrandCount)} strands";
        }
    }
}
