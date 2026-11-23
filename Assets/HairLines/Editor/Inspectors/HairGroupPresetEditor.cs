using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    [CustomEditor(typeof(HairGroupPreset))]
    public class HairGroupPresetEditor : UnityEditor.Editor
    {
        private HairGrowSettingsGUI _settingsGui;
        private SerializedProperty _settings;
        private SerializedProperty _seed;

        private int _builtHash;

        private void OnEnable()
        {
            _settings = serializedObject.FindProperty("_settings");
            _seed = _settings.FindPropertyRelative("_seed");
            _settingsGui = new HairGrowSettingsGUI(_settings);
            _builtHash = HairInspectorGUI.HashProperties(_settings);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            _settingsGui.Draw(true, false);
            bool changed = serializedObject.hasModifiedProperties;
            serializedObject.ApplyModifiedProperties();

            bool explicitRandomize = RandomizeSeedIfRequested() ||
                                     _settingsGui.RandomizeSeedForOperator >= 0;

            int hash = HairInspectorGUI.HashProperties(_settings);
            if (!changed && !explicitRandomize && hash == _builtHash)
            {
                return;
            }

            _builtHash = hash;
            RebuildPresetUsers((HairGroupPreset)target);
        }

        public override bool HasPreviewGUI()
        {
            return targets.Length == 1;
        }

        public override void OnPreviewGUI(Rect rect, GUIStyle background)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Texture2D preview = HairPresetPreview.Get((HairGroupPreset)target);

            if (preview != null)
            {
                GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
            }
        }

        public override Texture2D RenderStaticPreview(string path, Object[] subAssets, int width, int height)
        {
            Texture2D preview = HairPresetPreview.Get((HairGroupPreset)target);
            return preview != null ? HairPresetPreview.Resized(preview, width, height) : null;
        }

        private bool RandomizeSeedIfRequested()
        {
            if (!_settingsGui.RandomizeSeedRequested || _seed == null)
            {
                return false;
            }

            _seed.intValue = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            return true;
        }

        private static void RebuildPresetUsers(HairGroupPreset preset)
        {
            foreach (HairGroup group in Object.FindObjectsByType<HairGroup>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (group.Mode != HairGroupMode.Preset || group.Preset != preset)
                {
                    continue;
                }

                LinePrimitiveRenderer renderer = group.GetComponentInParent<LinePrimitiveRenderer>();
                if (renderer != null)
                {
                    renderer.Rebuild();
                }
            }

            SceneView.RepaintAll();
        }
    }
}
