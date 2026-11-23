using UnityEngine;

namespace HairLines.EditorTools
{
    public class HairScalpRaycaster
    {
        public Mesh Mesh => _mesh;

        private Renderer _renderer;
        private Mesh _mesh;

        private Vector3[] _positions;
        private int[] _triangles;
        private int _submesh;

        private bool _ownsMesh;

        public void EnsureMesh(Renderer scalp, int submesh = -1)
        {
            if (_renderer == scalp && _positions != null && _submesh == submesh &&
                !(scalp is SkinnedMeshRenderer))
            {
                return;
            }

            Release();
            Mesh mesh = HairMaskFactory.ResolvePaintMesh(scalp);

            if (mesh == null)
            {
                return;
            }

            _renderer = scalp;
            _mesh = mesh;
            _submesh = submesh;
            _ownsMesh = scalp is SkinnedMeshRenderer;
            _positions = mesh.vertices;
            _triangles = ResolveTriangles(mesh, submesh);
        }

        public bool Raycast(Renderer scalp, Ray worldRay, out Vector3 point, out Vector3 normal)
        {
            return Raycast(scalp, worldRay, out point, out normal, out _);
        }

        public bool Raycast(Renderer scalp, Ray worldRay, out Vector3 point, out Vector3 normal,
                            out HairRootBinding binding)
        {
            point = Vector3.zero;
            normal = Vector3.up;
            binding = default;

            if (_positions == null || _triangles == null || _positions.Length == 0 || _triangles.Length < 3)
            {
                return false;
            }

            Matrix4x4 toLocal = scalp.transform.worldToLocalMatrix;
            Vector3 origin = toLocal.MultiplyPoint3x4(worldRay.origin);
            Vector3 direction = toLocal.MultiplyVector(worldRay.direction);
            float nearest = float.MaxValue;
            bool found = false;

            for (int t = 0; t + 2 < _triangles.Length; t += 3)
            {
                int index0 = _triangles[t];
                int index1 = _triangles[t + 1];
                int index2 = _triangles[t + 2];
                if (!ValidIndex(index0) || !ValidIndex(index1) || !ValidIndex(index2))
                {
                    continue;
                }

                Vector3 corner0 = _positions[index0];
                Vector3 corner1 = _positions[index1];
                Vector3 corner2 = _positions[index2];

                if (!IntersectTriangle(origin, direction, corner0, corner1, corner2, out float distance,
                                       out Vector2 barycentric))
                {
                    continue;
                }

                if (distance >= nearest)
                {
                    continue;
                }

                nearest = distance;
                normal = Vector3.Cross(corner1 - corner0, corner2 - corner0);
                binding = new HairRootBinding(t / 3, barycentric);
                found = true;
            }

            if (!found)
            {
                return false;
            }

            Matrix4x4 toWorld = scalp.transform.localToWorldMatrix;
            point = toWorld.MultiplyPoint3x4(origin + direction * nearest);
            normal = toWorld.MultiplyVector(normal).normalized;
            return true;
        }

        public void Release()
        {
            if (_ownsMesh && _mesh != null)
            {
                Object.DestroyImmediate(_mesh);
            }

            _mesh = null;
            _renderer = null;
            _positions = null;
            _triangles = null;
            _submesh = -1;
            _ownsMesh = false;
        }

        private bool ValidIndex(int index)
        {
            return index >= 0 && index < _positions.Length;
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

        private static bool IntersectTriangle(Vector3 origin, Vector3 direction, Vector3 corner0, Vector3 corner1,
                                              Vector3 corner2, out float distance, out Vector2 barycentric)
        {
            distance = 0f;
            barycentric = default;

            Vector3 edge1 = corner1 - corner0;
            Vector3 edge2 = corner2 - corner0;
            Vector3 across = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, across);

            if (Mathf.Abs(determinant) < 1e-10f)
            {
                return false;
            }

            float inverse = 1f / determinant;
            Vector3 toOrigin = origin - corner0;
            float weight1 = Vector3.Dot(toOrigin, across) * inverse;

            if (weight1 < 0f || weight1 > 1f)
            {
                return false;
            }

            Vector3 alongEdge = Vector3.Cross(toOrigin, edge1);
            float weight2 = Vector3.Dot(direction, alongEdge) * inverse;

            if (weight2 < 0f || weight1 + weight2 > 1f)
            {
                return false;
            }

            distance = Vector3.Dot(edge2, alongEdge) * inverse;
            barycentric = new Vector2(weight1, weight2);
            return distance > 1e-6f;
        }
    }
}
