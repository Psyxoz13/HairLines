using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HairLines.EditorTools
{
    public static class HairLinesSetup
    {
        private static double _featureCacheTime = -1;
        private static bool _featureCached;

        private const string DefaultMaterialPath = "Assets/HairLines/Materials/Hair Default.mat";
        private const string HairObjectName = "Hair Lines";
        private const string ScalpObjectName = "Hair Scalp";
        private const string FeatureAssetName = "Hair Lines";

        private const string RendererFeaturesField = "m_RendererFeatures";
        private const string RendererFeatureMapField = "m_RendererFeatureMap";
        private const string ResolveShaderField = "_resolveShader";
        private const string ShadowShaderField = "_shadowShader";
        private const string DepthShaderField = "_depthShader";
        private const string DensityShaderField = "_densityShader";
        private const string DensityShaderName = "HairDensityVolume";

        private const double FeatureCacheSeconds = 2.0;
        private const float ScalpSphereClearance = 0.004f;
        private const float MinimumScalpSphereRadius = 0.001f;

        [MenuItem("Hair Lines/Install Renderer Feature", priority = 0)]
        public static void InstallRendererFeature()
        {
            List<ScriptableRendererData> targets = CollectRendererData();
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("Hair Lines",
                    "No Universal Renderer assets were found. Make sure the project uses URP and that a " +
                    "Universal Render Pipeline Asset is assigned in Project Settings > Graphics.", "OK");
                return;
            }

            int installed = 0;
            int removed = 0;
            foreach (ScriptableRendererData data in targets)
            {
                removed += RemoveOrphanFeatures(data);

                if (AddFeature(data))
                {
                    installed++;
                }
            }

            AssetDatabase.SaveAssets();
            _featureCacheTime = -1;

            string cleanup = removed > 0 ? $" Removed {removed} unreferenced copies." : string.Empty;
            Debug.Log(installed > 0
                ? $"[HairLines] Renderer feature installed on {installed} renderer asset(s).{cleanup}"
                : $"[HairLines] Renderer feature was already installed on every renderer asset.{cleanup}");
        }

        public static bool IsRendererFeatureInstalled()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_featureCacheTime >= 0 && now - _featureCacheTime < FeatureCacheSeconds)
            {
                return _featureCached;
            }

            _featureCached = false;
            foreach (ScriptableRendererData data in CollectRendererData())
            {
                if (HasFeature(data))
                {
                    _featureCached = true;
                    break;
                }
            }

            _featureCacheTime = now;
            return _featureCached;
        }

        public static Material GetOrCreateDefaultMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find(LineShaderIds.LineShaderName);
            if (shader == null)
            {
                Debug.LogError($"[HairLines] Shader '{LineShaderIds.LineShaderName}' not found.");
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(DefaultMaterialPath)));
            var material = new Material(shader) { name = "Hair Default" };
            AssetDatabase.CreateAsset(material, DefaultMaterialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(DefaultMaterialPath);
            return AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
        }

        [MenuItem("Hair Lines/Create Hair", priority = 10)]
        public static void CreateHairObject(MenuCommand command)
        {
            Renderer scalpRenderer = ResolveScalpRenderer(command.context);
            Material material = GetOrCreateDefaultMaterial();

            var root = new GameObject(HairObjectName);
            if (scalpRenderer != null)
            {
                GameObjectUtility.SetParentAndAlign(root, scalpRenderer.gameObject);
                FollowScalpBone(root.transform, scalpRenderer);
            }
            else
            {
                StageUtility.PlaceGameObjectInCurrentStage(root);
            }

            GameObjectUtility.EnsureUniqueNameForSibling(root);

            LinePrimitiveRenderer renderer = root.AddComponent<LinePrimitiveRenderer>();
            renderer.Material = material;

            Transform scalpCollider = null;

            if (scalpRenderer != null)
            {
                BindMeshScalp(renderer, scalpRenderer);
            }
            else
            {
                scalpCollider = BuildSphereScalp(root, renderer);
            }

            AddDefaultGroup(renderer, scalpCollider);
            renderer.RefreshGroups();

            Undo.RegisterCreatedObjectUndo(root, "Create Hair");
            EditorUtility.SetDirty(root);

            renderer.Rebuild();
            Selection.activeGameObject = root;

            PromptForRendererFeature();
        }

        [MenuItem("Hair Lines/Create Material", priority = 20)]
        private static void CreateMaterialAsset()
        {
            Shader shader = Shader.Find(LineShaderIds.LineShaderName);
            if (shader == null)
            {
                Debug.LogError($"[HairLines] Shader '{LineShaderIds.LineShaderName}' not found.");
                return;
            }

            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                ScriptableObject.CreateInstance<CreateHairMaterialAction>(),
                "New Hair Material.mat",
                AssetPreview.GetMiniTypeThumbnail(typeof(Material)),
                null);
        }

        private static Renderer ResolveScalpRenderer(Object context)
        {
            GameObject selected = context as GameObject;

            if (selected == null && context is Component component)
            {
                selected = component.gameObject;
            }

            selected ??= Selection.activeGameObject;

            if (selected == null || !selected.scene.IsValid())
            {
                return null;
            }

            Renderer own = ScalpRendererOn(selected);
            return own != null ? own : ScalpRendererInChildren(selected);
        }

        private static Renderer ScalpRendererOn(GameObject candidate)
        {
            if (candidate.TryGetComponent(out SkinnedMeshRenderer skinned) && skinned.sharedMesh != null)
            {
                return skinned;
            }

            if (!candidate.TryGetComponent(out MeshRenderer meshRenderer))
            {
                return null;
            }

            return candidate.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null
                ? meshRenderer
                : null;
        }

        private static Renderer ScalpRendererInChildren(GameObject selected)
        {
            SkinnedMeshRenderer[] skinnedRenderers =
                selected.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            foreach (SkinnedMeshRenderer skinned in skinnedRenderers)
            {
                if (skinned.sharedMesh != null)
                {
                    return skinned;
                }
            }

            foreach (MeshRenderer meshRenderer in selected.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (meshRenderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                {
                    return meshRenderer;
                }
            }

            return null;
        }

        private static void FollowScalpBone(Transform hair, Renderer scalpRenderer)
        {
            if (!(scalpRenderer is SkinnedMeshRenderer skinned))
            {
                return;
            }

            Transform bone = DominantBone(skinned);
            if (bone == null)
            {
                return;
            }

            hair.position = bone.position;

            ParentConstraint constraint = hair.gameObject.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = bone, weight = 1f });
            constraint.SetTranslationOffset(0, Vector3.zero);
            constraint.SetRotationOffset(0, (Quaternion.Inverse(bone.rotation) * hair.rotation).eulerAngles);
            constraint.weight = 1f;
            constraint.locked = true;
            constraint.constraintActive = true;
        }

        private static Transform DominantBone(SkinnedMeshRenderer skinned)
        {
            Transform[] bones = skinned.bones;
            Mesh mesh = skinned.sharedMesh;

            if (bones.Length == 0 || !mesh.isReadable)
            {
                return skinned.rootBone;
            }

            var total = new float[bones.Length];
            NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();

            for (int i = 0; i < weights.Length; i++)
            {
                int index = weights[i].boneIndex;
                if (index >= 0 && index < total.Length)
                {
                    total[index] += weights[i].weight;
                }
            }

            int best = -1;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && total[i] > 0f && (best < 0 || total[i] > total[best]))
                {
                    best = i;
                }
            }

            return best >= 0 ? bones[best] : skinned.rootBone;
        }

        private static void BindMeshScalp(LinePrimitiveRenderer renderer, Renderer scalpRenderer)
        {
            renderer.Scalp.Mode = HairScalpMode.Mesh;
            renderer.Scalp.Renderer = scalpRenderer;
            renderer.Scalp.Submesh = -1;
        }

        private static HairGroup AddDefaultGroup(LinePrimitiveRenderer renderer, Transform scalpCollider)
        {
            var child = new GameObject("Group 1");
            GameObjectUtility.SetParentAndAlign(child, renderer.gameObject);
            HairGroup group = child.AddComponent<HairGroup>();
            group.Material = renderer.Material;

            if (scalpCollider == null)
            {
                return group;
            }

            HairGroupSimulation simulation = child.AddComponent<HairGroupSimulation>();
            simulation.Settings.Colliders = new[]
            {
                new LineHairSimulation.CollisionShape
                {
                    transform = scalpCollider,
                    padding = 0f,
                },
            };

            return group;
        }

        private static Transform BuildSphereScalp(GameObject root, LinePrimitiveRenderer renderer)
        {
            renderer.Scalp.Mode = HairScalpMode.Sphere;

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = ScalpObjectName;
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localPosition = renderer.Scalp.SphereCenter;
            sphere.transform.localRotation = Quaternion.identity;

            root.AddComponent<LineHairSimulation>();
            var clearance = new HairSimulationSettings();
            float radius = Mathf.Max(MinimumScalpSphereRadius,
                renderer.Scalp.SphereRadius - clearance.CollisionOffset - ScalpSphereClearance);
            sphere.transform.localScale = Vector3.one * (radius * 2f);

            return sphere.transform;
        }

        private static void PromptForRendererFeature()
        {
            if (IsRendererFeatureInstalled())
            {
                return;
            }

            if (EditorUtility.DisplayDialog("Hair Lines",
                    "The Hair Lines renderer feature is not installed on any URP renderer, " +
                    "so the strands will not be drawn. Install it now?", "Install", "Later"))
            {
                InstallRendererFeature();
            }
        }

        private static List<ScriptableRendererData> CollectRendererData()
        {
            var result = new List<ScriptableRendererData>();

            void AddFrom(UniversalRenderPipelineAsset asset)
            {
                if (asset == null)
                {
                    return;
                }

                foreach (ScriptableRendererData data in asset.rendererDataList)
                {
                    if (data != null && !result.Contains(data))
                    {
                        result.Add(data);
                    }
                }
            }

            AddFrom(GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset);
            AddFrom(QualitySettings.renderPipeline as UniversalRenderPipelineAsset);

            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AddFrom(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path));
            }

            return result;
        }

        private static bool HasFeature(ScriptableRendererData data)
        {
            foreach (ScriptableRendererFeature feature in data.rendererFeatures)
            {
                if (feature is LineRenderingFeature)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AddFeature(ScriptableRendererData data)
        {
            if (HasFeature(data))
            {
                return false;
            }

            var feature = ScriptableObject.CreateInstance<LineRenderingFeature>();
            feature.name = FeatureAssetName;
            AssetDatabase.AddObjectToAsset(feature, data);
            AssignShaders(feature);

            if (AttachFeature(data, feature))
            {
                return true;
            }

            RemoveUnattachedFeature(feature);
            return false;
        }

        private static void AssignShaders(LineRenderingFeature feature)
        {
            var featureObject = new SerializedObject(feature);
            var missing = new List<string>();

            AssignShader(featureObject, ResolveShaderField, LineShaderIds.ResolveShaderName, missing);
            AssignShader(featureObject, ShadowShaderField, LineShaderIds.ShadowShaderName, missing);
            AssignShader(featureObject, DepthShaderField, LineShaderIds.DepthShaderName, missing);
            AssignComputeShader(featureObject, DensityShaderField, DensityShaderName, missing);

            featureObject.ApplyModifiedPropertiesWithoutUndo();

            if (missing.Count == 0)
            {
                return;
            }

            Debug.LogWarning("[HairLines] The renderer feature was installed without " +
                             string.Join(", ", missing) + ". Strands fall back to Shader.Find in the " +
                             "editor but are stripped from player builds. Reimport " +
                             "Assets/HairLines/Shaders and install the feature again.");
        }

        private static void AssignShader(SerializedObject feature, string field, string shaderName,
                                         List<string> missing)
        {
            SerializedProperty property = feature.FindProperty(field);
            if (property == null)
            {
                missing.Add($"field '{field}'");
                return;
            }

            Shader shader = Shader.Find(shaderName);
            property.objectReferenceValue = shader;

            if (shader == null)
            {
                missing.Add($"shader '{shaderName}'");
            }
        }

        private static void AssignComputeShader(SerializedObject feature, string field, string shaderName,
                                                List<string> missing)
        {
            SerializedProperty property = feature.FindProperty(field);
            if (property == null)
            {
                missing.Add($"field '{field}'");
                return;
            }

            ComputeShader shader = null;

            foreach (string guid in AssetDatabase.FindAssets($"{shaderName} t:ComputeShader"))
            {
                shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(AssetDatabase.GUIDToAssetPath(guid));
                if (shader != null)
                {
                    break;
                }
            }

            property.objectReferenceValue = shader;

            if (shader == null)
            {
                missing.Add($"compute shader '{shaderName}'");
            }
        }

        private static bool AttachFeature(ScriptableRendererData data, ScriptableRendererFeature feature)
        {
            var dataObject = new SerializedObject(data);
            dataObject.Update();
            SerializedProperty features = dataObject.FindProperty(RendererFeaturesField);
            SerializedProperty map = dataObject.FindProperty(RendererFeatureMapField);

            if (features == null || !features.isArray || map == null || !map.isArray)
            {
                Debug.LogError("[HairLines] The active URP version does not expose renderer feature lists.");
                return false;
            }

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId))
            {
                Debug.LogError("[HairLines] Could not resolve the renderer feature asset identifier.");
                return false;
            }

            int index = features.arraySize;
            while (map.arraySize < index)
            {
                map.InsertArrayElementAtIndex(map.arraySize);
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = 0;
            }

            while (map.arraySize > index)
            {
                map.DeleteArrayElementAtIndex(map.arraySize - 1);
            }

            features.InsertArrayElementAtIndex(index);
            features.GetArrayElementAtIndex(index).objectReferenceValue = feature;

            map.InsertArrayElementAtIndex(index);
            map.GetArrayElementAtIndex(index).longValue = localId;

            dataObject.ApplyModifiedPropertiesWithoutUndo();
            data.SetDirty();
            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(feature);
            AssetDatabase.SaveAssetIfDirty(data);
            return true;
        }

        private static int RemoveOrphanFeatures(ScriptableRendererData data)
        {
            string path = AssetDatabase.GetAssetPath(data);
            if (string.IsNullOrEmpty(path))
            {
                return 0;
            }

            HashSet<long> linked = LinkedFeatureIds(data);
            var orphans = new List<Object>();

            foreach (Object candidate in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var feature = candidate as LineRenderingFeature;
                if (feature == null || data.rendererFeatures.Contains(feature))
                {
                    continue;
                }

                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId) &&
                    linked.Contains(localId))
                {
                    continue;
                }

                orphans.Add(feature);
            }

            foreach (Object orphan in orphans)
            {
                AssetDatabase.RemoveObjectFromAsset(orphan);
                Object.DestroyImmediate(orphan, true);
            }

            if (orphans.Count > 0)
            {
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
            }

            return orphans.Count;
        }

        private static HashSet<long> LinkedFeatureIds(ScriptableRendererData data)
        {
            var ids = new HashSet<long>();
            var dataObject = new SerializedObject(data);
            SerializedProperty map = dataObject.FindProperty(RendererFeatureMapField);

            if (map == null || !map.isArray)
            {
                return ids;
            }

            for (int i = 0; i < map.arraySize; i++)
            {
                long localId = map.GetArrayElementAtIndex(i).longValue;
                if (localId != 0)
                {
                    ids.Add(localId);
                }
            }

            return ids;
        }

        private static void RemoveUnattachedFeature(ScriptableRendererFeature feature)
        {
            if (feature == null)
            {
                return;
            }

            AssetDatabase.RemoveObjectFromAsset(feature);
            Object.DestroyImmediate(feature, true);
            AssetDatabase.SaveAssets();
        }

        private class CreateHairMaterialAction : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                var material = new Material(Shader.Find(LineShaderIds.LineShaderName))
                {
                    name = Path.GetFileNameWithoutExtension(pathName),
                };
                AssetDatabase.CreateAsset(material, pathName);
                ProjectWindowUtil.ShowCreatedAsset(material);
            }
        }
    }
}
