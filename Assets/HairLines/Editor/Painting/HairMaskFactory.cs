using System.IO;
using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    public static class HairMaskFactory
    {
        public const string DefaultFolder = "Assets/HairLines/Masks";

        public static Texture2D Create(string name, int resolution, byte fill)
        {
            return CreateTexture(name, resolution, resolution, TextureFormat.R8,
                                 new Color32(fill, fill, fill, 255),
                                 AssetDatabase.GenerateUniqueAssetPath($"{DefaultFolder}/{name}.asset"));
        }

        public static Texture2D Create(string name, int width, int height,
                                       HairMaskCreateWindow.MaskKind kind, bool full, string path)
        {
            TextureFormat format = kind == HairMaskCreateWindow.MaskKind.Comb
                ? TextureFormat.RG16
                : TextureFormat.R8;
            byte value = kind == HairMaskCreateWindow.MaskKind.Comb
                ? HairCombMap.Neutral
                : (full ? byte.MaxValue : (byte)0);
            Color32 pixel = kind == HairMaskCreateWindow.MaskKind.Comb
                ? new Color32(value, value, 0, 255)
                : new Color32(value, value, value, 255);
            string assetPath = NormalizeAssetPath(path, name);
            return CreateTexture(name, width, height, format, pixel, assetPath);
        }

        public static Texture2D CreateComb(string name, int resolution)
        {
            return CreateTexture(name, resolution, resolution, TextureFormat.RG16,
                                 new Color32(HairCombMap.Neutral, HairCombMap.Neutral, 0, 255),
                                 AssetDatabase.GenerateUniqueAssetPath($"{DefaultFolder}/{name}.asset"));
        }

        public static Texture2D CreateComb(string name, int width, int height, string path)
        {
            string assetPath = NormalizeAssetPath(path, name);
            return CreateTexture(name, width, height, TextureFormat.RG16,
                                 new Color32(HairCombMap.Neutral, HairCombMap.Neutral, 0, 255), assetPath);
        }

        public static void Save(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            EditorUtility.SetDirty(texture);
            AssetDatabase.SaveAssetIfDirty(texture);
        }

        public static Mesh ResolvePaintMesh(Renderer renderer)
        {
            if (renderer == null)
            {
                return null;
            }

            if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null)
                {
                    return null;
                }

                var baked = new Mesh { name = "HairLines Paint Target", hideFlags = HideFlags.HideAndDontSave };
                skinned.BakeMesh(baked, true);
                return baked;
            }

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        public static bool UvsOverlap(Mesh mesh)
        {
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;

            if (uvs.Length == 0 || triangles.Length == 0)
            {
                return false;
            }

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            float chartArea = 0f;

            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector2 uv0 = uvs[triangles[t]];
                Vector2 uv1 = uvs[triangles[t + 1]];
                Vector2 uv2 = uvs[triangles[t + 2]];

                min = Vector2.Min(min, Vector2.Min(uv0, Vector2.Min(uv1, uv2)));
                max = Vector2.Max(max, Vector2.Max(uv0, Vector2.Max(uv1, uv2)));
                chartArea += Mathf.Abs((uv1.x - uv0.x) * (uv2.y - uv0.y) - (uv2.x - uv0.x) * (uv1.y - uv0.y)) * 0.5f;
            }

            Vector2 size = max - min;
            float boxArea = size.x * size.y;
            return boxArea > 0f && chartArea > boxArea;
        }

        private static Texture2D CreateTexture(string name, int width, int height,
                                               TextureFormat format, Color32 value, string path)
        {
            Directory.CreateDirectory(DefaultFolder);

            width = Mathf.Clamp(width, 1, 8192);
            height = Mathf.Clamp(height, 1, 8192);
            string assetPath = NormalizeAssetPath(path, name);
            string directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) != null)
            {
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            }

            Texture2D texture = new Texture2D(width, height, format, false, true)
            {
                name = string.IsNullOrEmpty(name) ? "Hair Mask" : name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            Color32[] pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = value;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
            AssetDatabase.CreateAsset(texture, assetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static string NormalizeAssetPath(string path, string name)
        {
            string assetPath = string.IsNullOrWhiteSpace(path)
                ? $"{DefaultFolder}/{name}.asset"
                : path.Replace('\\', '/').Trim();
            int assetsIndex = assetPath.IndexOf("Assets/", System.StringComparison.OrdinalIgnoreCase);
            if (assetsIndex >= 0)
            {
                assetPath = assetPath.Substring(assetsIndex);
            }

            if (!assetPath.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
            {
                assetPath += ".asset";
            }

            if (!assetPath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
            {
                assetPath = $"{DefaultFolder}/{Path.GetFileName(assetPath)}";
            }

            return assetPath;
        }
    }
}
