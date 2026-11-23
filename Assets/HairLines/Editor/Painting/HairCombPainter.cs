using UnityEngine;

namespace HairLines.EditorTools
{
    public class HairCombPainter
    {
        public int Width => _texels.Width;
        public int Height => _texels.Height;
        public bool IsValid => _texels.IsValid && _rasterizer.IsValid;

        private readonly HairSurfaceRasterizer _rasterizer;
        private readonly HairTexelBuffer _texels;

        public HairCombPainter(Mesh mesh, Texture2D texture, Matrix4x4 toWorld, int submesh = -1)
        {
            _texels = new HairTexelBuffer(texture);
            _rasterizer = new HairSurfaceRasterizer(mesh, _texels.Width, _texels.Height, toWorld, submesh);
        }

        public bool Stamp(Vector3 center, Vector3 stroke, float radius, float strength, float hardness)
        {
            if (!IsValid || stroke.sqrMagnitude < 1e-12f)
            {
                return false;
            }

            bool touched = false;

            _rasterizer.Splat(center, radius, hardness, (index, falloff, tangent, bitangent) =>
            {
                var wanted = new Vector2(Vector3.Dot(stroke, tangent), Vector3.Dot(stroke, bitangent));

                if (wanted.sqrMagnitude < 1e-12f)
                {
                    return;
                }

                Vector2 current = HairCombMap.Decode(_texels.Read(index));
                Vector2 blended = Vector2.Lerp(current, wanted.normalized, strength * falloff);

                touched |= _texels.Write(index, HairCombMap.Encode(blended));
            });

            return touched;
        }

        public bool Clear(Vector3 center, float radius, float strength, float hardness)
        {
            if (!IsValid)
            {
                return false;
            }

            bool touched = false;

            _rasterizer.Splat(center, radius, hardness, (index, falloff, tangent, bitangent) =>
            {
                Vector2 current = HairCombMap.Decode(_texels.Read(index));
                Vector2 blended = Vector2.Lerp(current, Vector2.zero, strength * falloff);

                touched |= _texels.Write(index, HairCombMap.Encode(blended));
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

        public float MeanStrength()
        {
            float sum = 0f;
            int count = Width * Height;

            if (!IsValid || count == 0)
            {
                return 0f;
            }

            for (int i = 0; i < count; i++)
            {
                sum += HairCombMap.Decode(_texels.Read(i)).magnitude;
            }

            return sum / count;
        }
    }
}
