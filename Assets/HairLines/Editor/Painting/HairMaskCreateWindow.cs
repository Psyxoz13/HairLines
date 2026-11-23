using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    public class HairMaskCreateWindow : EditorWindow
    {
        private MaskKind _kind;
        private Action<Texture2D> _created;
        private UnityEngine.Object _callbackOwner;
        private string _callbackPropertyPath;
        private string _savePath;
        private int _width = 1024;
        private int _height = 1024;
        private FillMode _fill = FillMode.Full;

        private const int MinimumSize = 1;
        private const int MaximumSize = 8192;

        private const float RowGap = 6f;
        private const float SizeLabelWidth = 34f;
        private const float SizeFieldWidth = 58f;
        private const float TimesWidth = 14f;
        private const float FillWidth = 84f;
        private const float PathLabelWidth = 66f;
        private const float BrowseWidth = 72f;
        private const float MinimumPathWidth = 120f;

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Create mask", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Type", KindLabel(_kind));

            DrawCreateRow();

            EditorGUILayout.HelpBox(FillDescription(_kind, _fill), MessageType.Info);

            if (CanCreate())
            {
                EditorGUILayout.LabelField("Project path", NormalizeProjectPath(_savePath),
                                           EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("Choose where the mask asset will be saved, inside this project.",
                                        MessageType.Warning);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel", GUILayout.Height(24f)))
            {
                Close();
            }

            using (new EditorGUI.DisabledScope(!CanCreate()))
            {
                if (GUILayout.Button("Create", GUILayout.Height(24f)))
                {
                    CreateMask();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        public static void Open(string defaultName, MaskKind kind, Action<Texture2D> created = null)
        {
            HairMaskCreateWindow window = GetWindow<HairMaskCreateWindow>(true, "Create Hair Mask", true);
            string name = string.IsNullOrWhiteSpace(defaultName) ? "Hair Mask" : defaultName;
            window._kind = kind;
            window._created = created;
            window._callbackOwner = null;
            window._callbackPropertyPath = string.Empty;
            window._savePath = $"{HairMaskFactory.DefaultFolder}/{SanitizeFileName(name)}.asset";
            window._width = 1024;
            window._height = 1024;
            window._fill = FillMode.Full;
            window.minSize = new Vector2(780f, 190f);
            window.ShowUtility();
            window.Focus();
        }

        public static void Open(string defaultName, MaskKind kind, SerializedProperty property)
        {
            Open(defaultName, kind);
            HairMaskCreateWindow window = GetWindow<HairMaskCreateWindow>();
            window._callbackOwner = property != null ? property.serializedObject.targetObject : null;
            window._callbackPropertyPath = property != null ? property.propertyPath : string.Empty;
        }

        private string AssetName => Path.GetFileNameWithoutExtension(NormalizeProjectPath(_savePath));

        private void DrawCreateRow()
        {
            Rect row = EditorGUILayout.GetControlRect();

            var sizeLabel = new Rect(row.x, row.y, SizeLabelWidth, row.height);
            var width = new Rect(sizeLabel.xMax, row.y, SizeFieldWidth, row.height);
            var times = new Rect(width.xMax, row.y, TimesWidth, row.height);
            var height = new Rect(times.xMax, row.y, SizeFieldWidth, row.height);
            var fill = new Rect(height.xMax + RowGap, row.y, FillWidth, row.height);
            var browse = new Rect(row.xMax - BrowseWidth, row.y, BrowseWidth, row.height);
            var pathLabel = new Rect(fill.xMax + RowGap, row.y, PathLabelWidth, row.height);
            float pathWidth = Mathf.Max(MinimumPathWidth, browse.x - RowGap - pathLabel.xMax);
            var path = new Rect(pathLabel.xMax, row.y, pathWidth, row.height);

            EditorGUI.LabelField(sizeLabel, new GUIContent("Size", "Mask resolution in pixels."));
            _width = Mathf.Clamp(EditorGUI.IntField(width, _width), MinimumSize, MaximumSize);
            EditorGUI.LabelField(times, "x", EditorStyles.centeredGreyMiniLabel);
            _height = Mathf.Clamp(EditorGUI.IntField(height, _height), MinimumSize, MaximumSize);
            _fill = (FillMode)EditorGUI.EnumPopup(fill, _fill);
            EditorGUI.LabelField(pathLabel, new GUIContent("Save path",
                                                           "Asset path inside this project."));
            _savePath = EditorGUI.TextField(path, _savePath);

            if (GUI.Button(browse, new GUIContent("Browse", "Choose an asset path inside this project."),
                           EditorStyles.miniButton))
            {
                PickSavePath();
            }
        }

        private bool CanCreate()
        {
            string path = NormalizeProjectPath(_savePath);
            return !string.IsNullOrWhiteSpace(_savePath) &&
                   IsProjectPath(path) &&
                   !string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(path));
        }

        private void PickSavePath()
        {
            string current = AssetName;
            string defaultName = string.IsNullOrWhiteSpace(current) ? "Hair Mask" : current;
            string path = EditorUtility.SaveFilePanelInProject("Save Hair Mask", defaultName, "asset",
                                                                "Choose where to save the mask asset.",
                                                                HairMaskFactory.DefaultFolder);
            if (!string.IsNullOrEmpty(path))
            {
                _savePath = path;
            }
        }

        private void CreateMask()
        {
            string path = NormalizeProjectPath(_savePath);
            if (!CanCreate() || string.IsNullOrEmpty(path))
            {
                return;
            }

            Texture2D texture = HairMaskFactory.Create(AssetName.Trim(), _width, _height,
                                                        _kind, _fill == FillMode.Full, path);
            if (texture == null)
            {
                return;
            }

            Action<Texture2D> callback = _created;
            _created = null;
            callback?.Invoke(texture);
            AssignCreatedTexture(texture);
            _callbackOwner = null;
            _callbackPropertyPath = string.Empty;
            Close();
        }

        private void AssignCreatedTexture(Texture2D texture)
        {
            if (_callbackOwner == null || string.IsNullOrEmpty(_callbackPropertyPath))
            {
                return;
            }

            var serializedObject = new SerializedObject(_callbackOwner);
            SerializedProperty property = serializedObject.FindProperty(_callbackPropertyPath);
            if (property == null)
            {
                return;
            }

            Undo.RecordObject(_callbackOwner, "Assign Hair Mask");
            property.objectReferenceValue = texture;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(_callbackOwner);
            RefreshOwner(_callbackOwner);
        }

        private static void RefreshOwner(UnityEngine.Object owner)
        {
            if (owner is HairGroup group)
            {
                InvalidateSettings(group.Settings);
                LinePrimitiveRenderer renderer = group.GetComponentInParent<LinePrimitiveRenderer>();
                if (renderer != null)
                {
                    renderer.Rebuild();
                }
            }
            else if (owner is HairGroupPreset preset)
            {
                InvalidateSettings(preset.Settings);
                RebuildPresetUsers(preset);
            }

            SceneView.RepaintAll();
        }

        private static void RebuildPresetUsers(HairGroupPreset preset)
        {
            foreach (HairGroup group in UnityEngine.Object.FindObjectsByType<HairGroup>(
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
        }

        private static void InvalidateSettings(HairGrowSettings settings)
        {
            settings.DensityMask.Invalidate();
            settings.CombMap.Invalidate();
            foreach (HairGroomOperator groomOperator in settings.Operators)
            {
                groomOperator?.InvalidateMask();
            }
        }

        private static string KindLabel(MaskKind kind)
        {
            switch (kind)
            {
                case MaskKind.Density:
                    return "Density";
                case MaskKind.Comb:
                    return "Comb";
                case MaskKind.Operator:
                    return "Operator";
                default:
                    return kind.ToString();
            }
        }

        private static string FillDescription(MaskKind kind, FillMode fill)
        {
            if (kind == MaskKind.Comb)
            {
                return "Comb maps use a neutral direction (0.5, 0.5) for both fill choices.";
            }

            if (fill == FillMode.Full)
            {
                return "Full sets every mask texel to 1.0.";
            }

            return "Empty sets every mask texel to 0.0.";
        }

        private static string NormalizeProjectPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalized = path.Replace('\\', '/').Trim();
            int assetsIndex = normalized.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            if (assetsIndex >= 0)
            {
                normalized = normalized.Substring(assetsIndex);
            }

            if (!normalized.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                normalized += ".asset";
            }

            return normalized;
        }

        private static bool IsProjectPath(string path)
        {
            return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                   !path.Contains("..", StringComparison.Ordinal);
        }

        private static string SanitizeFileName(string value)
        {
            string name = string.IsNullOrWhiteSpace(value) ? "Hair Mask" : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            return name.Replace('/', '_').Replace('\\', '_');
        }

        public enum MaskKind
        {
            Density,
            Comb,
            Operator
        }

        private enum FillMode
        {
            Full,
            Empty
        }
    }
}
