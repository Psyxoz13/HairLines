using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    public class HairMaskPreview
    {
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, true);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        public static float Opacity
        {
            get => EditorPrefs.GetFloat(OpacityKey, 0.55f);
            set => EditorPrefs.SetFloat(OpacityKey, Mathf.Clamp01(value));
        }

        private Material _material;

        private const string ShaderName = "Hidden/HairLines/MaskPreview";
        private const string EnabledKey = "HairLines.Preview.Enabled";
        private const string OpacityKey = "HairLines.Preview.Opacity";
        private const string CoverageKey = "HairLines.Preview.Coverage";

        private static readonly int MaskTex = Shader.PropertyToID("_MaskTex");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int Mode = Shader.PropertyToID("_Mode");

        public static bool ShowCoverage
        {
            get => EditorPrefs.GetBool(CoverageKey, false);
            set => EditorPrefs.SetBool(CoverageKey, value);
        }

        public void Draw(Renderer scalp, Mesh mesh, Texture2D map, bool comb, int submesh = -1)
        {
            Draw(scalp, mesh, map, comb ? 1f : 0f, submesh);
        }

        public void DrawCoverage(Renderer scalp, Mesh mesh, Texture2D map, int submesh = -1)
        {
            Draw(scalp, mesh, map, 2f, submesh);
        }

        private void Draw(Renderer scalp, Mesh mesh, Texture2D map, float mode, int submesh)
        {
            if (!Enabled || scalp == null || mesh == null || map == null || !EnsureMaterial())
            {
                return;
            }

            _material.SetTexture(MaskTex, map);
            _material.SetFloat(OpacityId, Opacity);
            _material.SetFloat(Mode, mode);

            if (!_material.SetPass(0))
            {
                return;
            }

            if (submesh >= 0 && submesh < mesh.subMeshCount)
            {
                Graphics.DrawMeshNow(mesh, scalp.transform.localToWorldMatrix, submesh);
            }
            else if (submesh < 0)
            {
                Graphics.DrawMeshNow(mesh, scalp.transform.localToWorldMatrix);
            }
        }

        public void Release()
        {
            if (_material == null)
            {
                return;
            }

            Object.DestroyImmediate(_material);
            _material = null;
        }

        private bool EnsureMaterial()
        {
            if (_material != null)
            {
                return true;
            }

            var shader = Shader.Find(ShaderName);

            if (shader == null)
            {
                Debug.LogError($"[HairLines] Shader '{ShaderName}' not found, so the mask cannot be shown.");
                return false;
            }

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }
    }
}
