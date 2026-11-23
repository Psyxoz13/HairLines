using UnityEngine;

namespace HairLines.EditorTools
{
    public class HairSurfaceRasterizer
    {
        public delegate void TexelWriter(int texelIndex, float falloff, Vector3 tangent, Vector3 bitangent);
        public bool IsValid => _valid;

        private readonly Vector3[] _positions;
        private readonly Vector3[] _normals;
        private readonly Vector2[] _uvs;
        private readonly int[] _triangles;

        private readonly int _width;
        private readonly int _height;
        private readonly bool _valid;
        private readonly float _texelReach;
        private readonly int[] _claimedBySplat;

        private int _splat;

        private const float MinimumDenominator = 1e-12f;
        private const float BarycentricSlack = 1e-4f;

        public HairSurfaceRasterizer(Mesh mesh, int width, int height, Matrix4x4 toWorld, int submesh = -1)
        {
            Vector3[] positions = mesh != null ? mesh.vertices : null;
            Vector3[] normals = mesh != null ? mesh.normals : null;
            Vector2[] uvs = mesh != null ? mesh.uv : null;
            int[] triangles = ResolveTriangles(mesh, submesh);

            _positions = positions ?? new Vector3[0];
            _normals = normals != null && normals.Length == _positions.Length
                ? normals
                : new Vector3[0];
            _uvs = uvs ?? new Vector2[0];
            _triangles = triangles ?? new int[0];
            _width = width;
            _height = height;
            _valid = width > 0 && height > 0 && _positions.Length > 0 &&
                     _uvs.Length == _positions.Length && _triangles.Length >= 3;

            if (!_valid)
            {
                return;
            }

            _texelReach = 0.5f * Mathf.Sqrt(1f / (width * (float)width) + 1f / (height * (float)height));
            _claimedBySplat = new int[width * height];

            for (int i = 0; i < _positions.Length; i++)
            {
                _positions[i] = toWorld.MultiplyPoint3x4(_positions[i]);
            }

            for (int i = 0; i < _normals.Length; i++)
            {
                _normals[i] = toWorld.MultiplyVector(_normals[i]).normalized;
            }
        }

        public bool Splat(Vector3 center, float radius, float hardness, TexelWriter write)
        {
            if (!_valid || write == null || radius <= 0f)
            {
                return false;
            }

            float squaredRadius = radius * radius;
            float softness = Mathf.Max(1e-3f, 1f - Mathf.Clamp01(hardness));
            bool touched = false;
            _splat++;

            for (int pass = 0; pass < 2; pass++)
            {
                bool seamPass = pass == 1;

                for (int t = 0; t + 2 < _triangles.Length; t += 3)
                {
                    int i0 = _triangles[t];
                    int i1 = _triangles[t + 1];
                    int i2 = _triangles[t + 2];

                    if (!ValidIndex(i0) || !ValidIndex(i1) || !ValidIndex(i2))
                    {
                        continue;
                    }

                    if (!SphereTouchesBounds(center, radius, i0, i1, i2))
                    {
                        continue;
                    }

                    touched |= SplatTriangle(i0, i1, i2, center, radius, squaredRadius, softness,
                                             seamPass, write);
                }
            }

            return touched;
        }

        private bool SplatTriangle(int i0, int i1, int i2, Vector3 center, float radius, float squaredRadius,
                                   float softness, bool seamPass, TexelWriter write)
        {
            if (!ValidIndex(i0) || !ValidIndex(i1) || !ValidIndex(i2))
            {
                return false;
            }

            Vector2 uv0 = _uvs[i0];
            Vector2 uv1 = _uvs[i1];
            Vector2 uv2 = _uvs[i2];

            Vector2 edge1 = uv1 - uv0;
            Vector2 edge2 = uv2 - uv0;

            float d00 = Vector2.Dot(edge1, edge1);
            float d01 = Vector2.Dot(edge1, edge2);
            float d11 = Vector2.Dot(edge2, edge2);
            float denominator = d00 * d11 - d01 * d01;

            if (Mathf.Abs(denominator) < MinimumDenominator)
            {
                return false;
            }

            Vector3 tangent = TriangleTangent(i0, i1, i2, out Vector3 normal);
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            Vector2 gradient1 = (d11 * edge1 - d01 * edge2) / denominator;
            Vector2 gradient2 = (d00 * edge2 - d01 * edge1) / denominator;
            float slack1 = seamPass ? _texelReach * gradient1.magnitude : BarycentricSlack;
            float slack2 = seamPass ? _texelReach * gradient2.magnitude : BarycentricSlack;
            float slack0 = seamPass ? _texelReach * (gradient1 + gradient2).magnitude : BarycentricSlack;

            int fromX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uv0.x, Mathf.Min(uv1.x, uv2.x)) * _width) - 1, 0, _width - 1);
            int toX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(uv0.x, Mathf.Max(uv1.x, uv2.x)) * _width) + 1, 0, _width - 1);
            int fromY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uv0.y, Mathf.Min(uv1.y, uv2.y)) * _height) - 1, 0, _height - 1);
            int toY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(uv0.y, Mathf.Max(uv1.y, uv2.y)) * _height) + 1, 0, _height - 1);

            bool touched = false;

            for (int y = fromY; y <= toY; y++)
            {
                for (int x = fromX; x <= toX; x++)
                {
                    var texel = new Vector2((x + 0.5f) / _width, (y + 0.5f) / _height);
                    Vector2 offset = texel - uv0;

                    float d20 = Vector2.Dot(offset, edge1);
                    float d21 = Vector2.Dot(offset, edge2);
                    float weight1 = (d11 * d20 - d01 * d21) / denominator;
                    float weight2 = (d00 * d21 - d01 * d20) / denominator;
                    float weight0 = 1f - weight1 - weight2;

                    if (weight0 < -slack0 || weight1 < -slack1 || weight2 < -slack2)
                    {
                        continue;
                    }

                    int index = y * _width + x;

                    if (_claimedBySplat[index] == _splat)
                    {
                        continue;
                    }

                    float inside0 = Mathf.Max(0f, weight0);
                    float inside1 = Mathf.Max(0f, weight1);
                    float inside2 = Mathf.Max(0f, weight2);
                    float total = inside0 + inside1 + inside2;
                    inside0 /= total;
                    inside1 /= total;
                    inside2 /= total;

                    Vector3 surfacePoint = _positions[i0] * inside0 + _positions[i1] * inside1 +
                                           _positions[i2] * inside2;
                    float squaredDistance = (surfacePoint - center).sqrMagnitude;

                    if (squaredDistance > squaredRadius)
                    {
                        continue;
                    }

                    float falloff = Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01((1f - Mathf.Sqrt(squaredDistance) / radius) / softness));

                    if (falloff <= 0f)
                    {
                        continue;
                    }

                    _claimedBySplat[index] = _splat;
                    write(index, falloff, tangent, bitangent);
                    touched = true;
                }
            }

            return touched;
        }

        private Vector3 TriangleTangent(int i0, int i1, int i2, out Vector3 normal)
        {
            Vector3 edge1 = _positions[i1] - _positions[i0];
            Vector3 edge2 = _positions[i2] - _positions[i0];

            normal = _normals.Length == _positions.Length
                ? (_normals[i0] + _normals[i1] + _normals[i2]).normalized
                : Vector3.Cross(edge1, edge2).normalized;

            Vector2 deltaUv1 = _uvs[i1] - _uvs[i0];
            Vector2 deltaUv2 = _uvs[i2] - _uvs[i0];
            float determinant = deltaUv1.x * deltaUv2.y - deltaUv2.x * deltaUv1.y;

            Vector3 tangent = Mathf.Abs(determinant) > MinimumDenominator
                ? (edge1 * deltaUv2.y - edge2 * deltaUv1.y) / determinant
                : edge1;

            tangent -= normal * Vector3.Dot(tangent, normal);
            return tangent.sqrMagnitude > 1e-12f ? tangent.normalized : HairRootFrame.PerpendicularTo(normal);
        }

        private bool SphereTouchesBounds(Vector3 center, float radius, int i0, int i1, int i2)
        {
            Vector3 min = Vector3.Min(_positions[i0], Vector3.Min(_positions[i1], _positions[i2]));
            Vector3 max = Vector3.Max(_positions[i0], Vector3.Max(_positions[i1], _positions[i2]));
            Vector3 nearest = Vector3.Max(min, Vector3.Min(center, max));
            return (nearest - center).sqrMagnitude <= radius * radius;
        }

        private bool ValidIndex(int index)
        {
            return index >= 0 && index < _positions.Length && index < _uvs.Length;
        }

        private static int[] ResolveTriangles(Mesh mesh, int submesh)
        {
            if (mesh == null)
            {
                return null;
            }

            if (submesh < 0)
            {
                return mesh.triangles;
            }

            if (submesh >= mesh.subMeshCount)
            {
                return null;
            }

            return mesh.GetTriangles(submesh);
        }
    }
}
