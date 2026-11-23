using UnityEngine;

namespace HairLines.EditorTools
{
    public class HairMaskPainter
    {
        public int Width => _texels.Width;
        public int Height => _texels.Height;
        public bool HasPendingPixels => _texels.HasPendingTexels;
        public bool IsValid => _texels.IsValid && _rasterizer.IsValid;

        private readonly HairSurfaceRasterizer _rasterizer;
        private readonly HairTexelBuffer _texels;

        public HairMaskPainter(Mesh mesh, Texture2D texture, Matrix4x4 toWorld, int submesh = -1)
        {
            _texels = new HairTexelBuffer(texture);
            _rasterizer = new HairSurfaceRasterizer(mesh, _texels.Width, _texels.Height, toWorld, submesh);
        }

        public bool Stamp(Vector3 center, float radius, float strength, float hardness, bool subtract)
        {
            if (!IsValid)
            {
                return false;
            }

            bool touched = false;

            _rasterizer.Splat(center, radius, hardness, (index, falloff, tangent, bitangent) =>
            {
                float amount = strength * falloff;
                float value = _texels.Read(index).r / 255f;
                float after = Mathf.Clamp01(subtract ? value - amount : value + amount);
                var written = (byte)Mathf.RoundToInt(after * 255f);

                touched |= _texels.Write(index, new Color32(written, written, written, 255));
            });

            return touched;
        }

        public bool TryTakeStroke(out RectInt region, out Color32[] before, out Color32[] after)
        {
            return _texels.TryTakeStroke(out region, out before, out after);
        }

        public void Apply()
        {
            if (IsValid)
            {
                _texels.Apply();
            }
        }

        public float Coverage()
        {
            return _texels.MeanRed();
        }
    }
}
