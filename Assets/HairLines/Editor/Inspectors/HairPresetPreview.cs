using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    public static class HairPresetPreview
    {
        private const int MaxPreviewStrands = 50000;
        private const int RenderSize = 512;
        private const float FieldOfView = 30f;
        private const float ViewYaw = 35f;

        private static readonly Dictionary<HairGroupPreset, Entry> _cache =
            new Dictionary<HairGroupPreset, Entry>();

        public static Texture2D Get(HairGroupPreset preset)
        {
            if (preset == null)
            {
                return null;
            }

            LinePrimitiveRenderer owner = FindOwner(preset);
            int stamp = Stamp(preset, owner);

            if (_cache.TryGetValue(preset, out Entry entry) && entry.Stamp == stamp)
            {
                return entry.Texture;
            }

            if (entry.Texture != null)
            {
                Object.DestroyImmediate(entry.Texture);
            }

            Texture2D rendered = Render(preset, owner, RenderSize);
            _cache[preset] = new Entry { Stamp = stamp, Texture = rendered };
            return rendered;
        }

        public static Texture2D Resized(Texture2D source, int width, int height)
        {
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, target);
            RenderTexture.active = target;

            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            result.Apply(false);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return result;
        }

        private static int Stamp(HairGroupPreset preset, LinePrimitiveRenderer owner)
        {
            HairGrowSettings settings = preset.Settings;
            Material material = settings.Material != null ? settings.Material
                : owner != null && owner.Material != null ? owner.Material
                : LinePrimitiveRenderer.FallbackMaterial;
            Texture2D mask = settings.DensityMask.Texture;

            int stamp = EditorUtility.GetDirtyCount(preset);
            stamp = stamp * 397 ^ (owner != null ? owner.GetInstanceID() : 0);
            stamp = stamp * 397 ^ (material != null ? material.GetInstanceID() : 0);
            stamp = stamp * 397 ^ (material != null ? EditorUtility.GetDirtyCount(material) : 0);
            stamp = stamp * 397 ^ (mask != null ? mask.imageContentsHash.GetHashCode() : 0);
            return stamp;
        }

        private static Texture2D Render(HairGroupPreset preset, LinePrimitiveRenderer owner, int size)
        {
            var utility = new PreviewRenderUtility();
            GameObject host = null;
            Mesh baked = null;

            try
            {
                host = BuildHair(preset, owner, out LinePrimitiveRenderer renderer);
                utility.AddSingleGO(host);
                renderer.Rebuild();
                renderer.CompletePendingBuild();

                if (renderer.Model == null || renderer.Model.IsEmpty)
                {
                    return null;
                }

                Transform reference = owner != null ? owner.transform : null;
                Frame(utility.camera, renderer.WorldBounds, reference);
                Light(utility, reference);

                utility.BeginStaticPreview(new Rect(0f, 0f, size, size));
                baked = DrawScalp(utility, renderer.Scalp.Mode == HairScalpMode.Mesh ? renderer.Scalp.Renderer : null);
                utility.Render(true);
                Texture2D texture = utility.EndStaticPreview();
                texture.hideFlags = HideFlags.HideAndDontSave;
                return texture;
            }
            finally
            {
                if (host != null)
                {
                    Object.DestroyImmediate(host);
                }

                if (baked != null)
                {
                    Object.DestroyImmediate(baked);
                }

                utility.Cleanup();
            }
        }

        private static GameObject BuildHair(HairGroupPreset preset, LinePrimitiveRenderer owner,
                                            out LinePrimitiveRenderer renderer)
        {
            var host = new GameObject("HairLinesPresetPreview") { hideFlags = HideFlags.HideAndDontSave };
            renderer = host.AddComponent<LinePrimitiveRenderer>();
            renderer.LodEnabled = false;

            bool meshScalp = owner != null && owner.Scalp.Mode == HairScalpMode.Mesh &&
                             owner.Scalp.Renderer != null;

            if (meshScalp)
            {
                renderer.Scalp.Mode = HairScalpMode.Mesh;
                renderer.Scalp.Renderer = owner.Scalp.Renderer;
                renderer.Scalp.Submesh = owner.Scalp.Submesh;
                renderer.Material = owner.Material;
            }
            else
            {
                renderer.Scalp.Mode = HairScalpMode.Sphere;
                renderer.Scalp.SphereRadius = 0.1f;
                renderer.Scalp.SphereCoverage = 0.55f;
            }

            var child = new GameObject("Group") { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(host.transform, false);
            HairGroup group = child.AddComponent<HairGroup>();
            group.Settings.CopyFrom(preset.Settings);
            group.Settings.StrandCount = Mathf.Min(group.Settings.StrandCount, MaxPreviewStrands);

            if (!meshScalp)
            {
                group.Settings.DensityMask.Texture = null;
            }

            return host;
        }

        private static Mesh DrawScalp(PreviewRenderUtility utility, Renderer scalp)
        {
            if (scalp == null)
            {
                return null;
            }

            Mesh mesh = null;
            Mesh baked = null;

            if (scalp is SkinnedMeshRenderer skinned)
            {
                baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                skinned.BakeMesh(baked, true);
                mesh = baked;
            }
            else if (scalp.TryGetComponent(out MeshFilter filter))
            {
                mesh = filter.sharedMesh;
            }

            if (mesh == null)
            {
                return baked;
            }

            Material[] materials = scalp.sharedMaterials;
            Matrix4x4 matrix = baked != null
                ? Matrix4x4.TRS(scalp.transform.position, scalp.transform.rotation, Vector3.one)
                : scalp.transform.localToWorldMatrix;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                Material material = materials.Length > 0 ? materials[Mathf.Min(submesh, materials.Length - 1)] : null;

                if (material != null)
                {
                    utility.DrawMesh(mesh, matrix, material, submesh);
                }
            }

            return baked;
        }

        private static LinePrimitiveRenderer FindOwner(HairGroupPreset preset)
        {
            foreach (LinePrimitiveRenderer renderer in LinePrimitiveRenderer.Instances)
            {
                if (renderer == null ||
                    UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(renderer.gameObject.scene))
                {
                    continue;
                }

                foreach (HairGroup group in renderer.Groups)
                {
                    if (group != null && group.Mode == HairGroupMode.Preset && group.Preset == preset)
                    {
                        return renderer;
                    }
                }
            }

            return null;
        }

        private static void Frame(Camera camera, Bounds bounds, Transform reference)
        {
            Vector3 up = reference != null ? reference.up : Vector3.up;
            Vector3 forward = reference != null ? reference.forward : Vector3.forward;
            Vector3 view = Quaternion.AngleAxis(ViewYaw, up) * forward;

            float radius = Mathf.Max(bounds.extents.magnitude, 1e-3f);
            float distance = radius / Mathf.Sin(FieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;

            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = distance * 0.02f;
            camera.farClipPlane = distance * 4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.16f, 0.18f, 1f);
            camera.transform.position = bounds.center + view * distance;
            camera.transform.LookAt(bounds.center, up);
        }

        private static void Light(PreviewRenderUtility utility, Transform reference)
        {
            Vector3 up = reference != null ? reference.up : Vector3.up;
            Vector3 forward = reference != null ? reference.forward : Vector3.forward;
            Vector3 right = Vector3.Cross(up, forward);

            Light key = utility.lights[0];
            key.type = LightType.Directional;
            key.intensity = 1.4f;
            key.color = Color.white;
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.LookRotation(-(forward + up + right * 0.6f).normalized, up);

            Light rim = utility.lights[1];
            rim.type = LightType.Directional;
            rim.intensity = 0.6f;
            rim.color = new Color(0.85f, 0.9f, 1f);
            rim.shadows = LightShadows.None;
            rim.transform.rotation = Quaternion.LookRotation((forward - up * 0.3f - right * 0.5f).normalized, up);

            utility.ambientColor = new Color(0.22f, 0.22f, 0.24f);
        }

        private struct Entry
        {
            public int Stamp;
            public Texture2D Texture;
        }
    }
}
