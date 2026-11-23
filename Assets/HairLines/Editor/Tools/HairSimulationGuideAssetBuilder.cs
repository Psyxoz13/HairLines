using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    public static class HairSimulationGuideAssetBuilder
    {
        [MenuItem("CONTEXT/LinePrimitiveRenderer/Bake 100% Simulation Guide Map")]
        private static void BakeIdentity(MenuCommand command)
        {
            Bake(command, 1f, true);
        }

        [MenuItem("CONTEXT/LinePrimitiveRenderer/Bake 100% Simulation Guide Map", true)]
        private static bool CanBakeIdentity(MenuCommand command)
        {
            var renderer = command.context as LinePrimitiveRenderer;
            return renderer != null && renderer.Model != null && !renderer.Model.IsEmpty;
        }

        [MenuItem("CONTEXT/LinePrimitiveRenderer/Bake 25% Simulation Guide Map")]
        private static void BakeQuarter(MenuCommand command) => Bake(command, 0.25f, false);

        [MenuItem("CONTEXT/LinePrimitiveRenderer/Bake 10% Simulation Guide Map")]
        private static void BakeTenth(MenuCommand command) => Bake(command, 0.10f, false);

        [MenuItem("CONTEXT/LinePrimitiveRenderer/Bake 5% Simulation Guide Map")]
        private static void BakeTwentieth(MenuCommand command) => Bake(command, 0.05f, false);

        [MenuItem("CONTEXT/LinePrimitiveRenderer/Bake 2% Simulation Guide Map")]
        private static void BakeFiftieth(MenuCommand command) => Bake(command, 0.02f, false);

        [MenuItem("CONTEXT/LinePrimitiveRenderer/Bake 25% Simulation Guide Map", true)]
        [MenuItem("CONTEXT/LinePrimitiveRenderer/Bake 10% Simulation Guide Map", true)]
        [MenuItem("CONTEXT/LinePrimitiveRenderer/Bake 5% Simulation Guide Map", true)]
        [MenuItem("CONTEXT/LinePrimitiveRenderer/Bake 2% Simulation Guide Map", true)]
        private static bool CanBakeReduced(MenuCommand command)
        {
            return CanBakeIdentity(command);
        }

        private static void Bake(MenuCommand command, float fraction, bool identity)
        {
            Bake(command.context as LinePrimitiveRenderer, fraction, identity);
        }

        public static HairSimulationGuideAsset Bake(LinePrimitiveRenderer renderer, float fraction,
                                                     bool identity,
                                                     HairSimulationGuideAsset replacing = null)
        {
            LinePrimitiveModel model = renderer != null ? renderer.Model : null;
            if (model == null || model.IsEmpty)
            {
                EditorUtility.DisplayDialog("Simulation Guide Map",
                    "The renderer has no completed model. Wait for its rebuild and try again.",
                    "OK");
                return null;
            }

            string percentage = Mathf.RoundToInt(fraction * 100f).ToString();
            string existingPath = replacing != null
                ? AssetDatabase.GetAssetPath(replacing)
                : null;
            int slash = existingPath != null ? existingPath.LastIndexOf('/') : -1;
            string directory = slash > 0 ? existingPath.Substring(0, slash) : null;
            string defaultName = !string.IsNullOrEmpty(existingPath)
                ? System.IO.Path.GetFileNameWithoutExtension(existingPath)
                : renderer.name + " Simulation Guides " + percentage;

            const string message = "Choose a sidecar path. The HairGroomAsset blob is not modified.";
            string title = $"Bake {percentage}% Simulation Guide Map";
            string path = string.IsNullOrEmpty(directory)
                ? EditorUtility.SaveFilePanelInProject(title, defaultName, "asset", message)
                : EditorUtility.SaveFilePanelInProject(title, defaultName, "asset", message,
                                                       directory);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            HairSimulationGuideBuildData data;
            string error;
            bool built;
            if (identity)
            {
                built = HairSimulationGuideMapBuilder.TryBuildIdentity(model, out data, out error);
            }
            else
            {
                built = HairSimulationGuideSelectionBuilder.TryBuild(model, fraction,
                                                                       out data, out error);
            }
            if (!built)
            {
                EditorUtility.DisplayDialog("Simulation Guide Map", error, "OK");
                return null;
            }

            var asset = ScriptableObject.CreateInstance<HairSimulationGuideAsset>();
            if (!asset.TryFill(model, data, out error))
            {
                Object.DestroyImmediate(asset);
                EditorUtility.DisplayDialog("Simulation Guide Map", error, "OK");
                return null;
            }

            HairSimulationGuideAsset existing =
                AssetDatabase.LoadAssetAtPath<HairSimulationGuideAsset>(path);
            HairSimulationGuideAsset saved;
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                Object.DestroyImmediate(asset);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                Selection.activeObject = existing;
                saved = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssetIfDirty(asset);
                Selection.activeObject = asset;
                saved = asset;
            }

            Debug.Log($"[HairLines] Baked {percentage}% simulation guide map: " +
                      $"{saved.GuideCount:N0}/{model.StrandCount:N0} guides, " +
                      $"{model.VertexCount:N0} vertices, map 0x{saved.MapHash:x16}.", renderer);
            return saved;
        }
    }

    [CustomEditor(typeof(HairSimulationGuideAsset))]
    internal sealed class HairSimulationGuideAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var asset = (HairSimulationGuideAsset)target;
            EditorGUILayout.LabelField("Schema", asset.SchemaVersion.ToString());
            EditorGUILayout.LabelField("Source strands", asset.SourceStrandCount.ToString("N0"));
            EditorGUILayout.LabelField("Source vertices", asset.SourceVertexCount.ToString("N0"));
            EditorGUILayout.LabelField("Simulation guides", asset.GuideCount.ToString("N0"));
            EditorGUILayout.LabelField("Guide fraction", asset.GuideFraction.ToString("P2"));
            EditorGUILayout.Space();
            EditorGUILayout.SelectableLabel($"Topology  0x{asset.TopologyHash:x16}",
                                             EditorStyles.textField,
                                             GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.SelectableLabel($"Rest pose 0x{asset.RestPoseHash:x16}",
                                             EditorStyles.textField,
                                             GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.SelectableLabel($"Map       0x{asset.MapHash:x16}",
                                             EditorStyles.textField,
                                             GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.HelpBox(
                "This is a simulation sidecar. It does not replace authored HairGuideSet data " +
                "and does not modify HairGroomBlob v1.", MessageType.Info);
        }
    }
}
