using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace HairLines
{
    public class HairSurface : IDisposable
    {
        public bool IsDisposed => _triangles == null;
        public int TriangleCount => _triangles.Length / 3;
        public bool IsSkinned => _skinnedRenderer != null;
        public bool HasUvs { get; private set; }
        public float TotalArea { get; private set; }
        public Matrix4x4 LocalToWorld => _renderer.transform.localToWorldMatrix;
        public Bounds WorldBounds
        {
            get
            {
                ThrowIfDisposed();
                if (_skinnedRenderer != null)
                {
                    return _skinnedRenderer.bounds;
                }

                Bounds local = _sourceMesh != null ? _sourceMesh.bounds : default;
                Matrix4x4 matrix = _renderer.transform.localToWorldMatrix;
                Vector3 center = matrix.MultiplyPoint3x4(local.center);
                Vector3 extents = local.extents;
                Vector3 transformedExtents = new Vector3(
                    Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y + Mathf.Abs(matrix.m02) * extents.z,
                    Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z,
                    Mathf.Abs(matrix.m20) * extents.x + Mathf.Abs(matrix.m21) * extents.y + Mathf.Abs(matrix.m22) * extents.z);
                return new Bounds(center, transformedExtents * 2f);
            }
        }
        public int Fingerprint { get; private set; }
        public int[] Triangles => _triangles;
        internal Vector3[] VertexPositions => _positions;
        internal Vector2[] VertexUvs => _uvs;

        private Renderer _renderer;
        private SkinnedMeshRenderer _skinnedRenderer;
        private Mesh _sourceMesh;
        private Mesh _bakedMesh;

        private Vector3[] _positions;
        private Vector3[] _normals;
        private Vector2[] _uvs;
        private int[] _triangles;
        private int[] _usedVertices;
        private float[] _triangleArea;
        private Vector3[] _fitPoints;

        private readonly List<Vector3> _vertexScratch = new List<Vector3>();
        private readonly List<Vector3> _normalScratch = new List<Vector3>();

        private static readonly ProfilerMarker _tryCreateMarker =
            new ProfilerMarker("HairLines.Scalp.CreateSurface");
        private static readonly ProfilerMarker _fitSphereMarker =
            new ProfilerMarker("HairLines.Scalp.FitInteriorSphere");

        public static bool TryCreate(Renderer renderer, int submesh,
                                     out HairSurface surface, out string error)
        {
            using ProfilerMarker.AutoScope scope = _tryCreateMarker.Auto();
            surface = null;

            if (renderer == null)
            {
                error = "Scalp renderer is not assigned.";
                return false;
            }

            Mesh mesh = ResolveMesh(renderer, out error);
            if (mesh == null)
            {
                return false;
            }

            if (!mesh.isReadable)
            {
                error = $"Mesh '{mesh.name}' is not readable. Enable Read/Write in its import settings.";
                return false;
            }

            if (submesh >= mesh.subMeshCount)
            {
                error = $"Mesh '{mesh.name}' has {mesh.subMeshCount} submesh(es), index {submesh} is out of range.";
                return false;
            }

            var created = new HairSurface
            {
                _renderer = renderer,
                _skinnedRenderer = renderer as SkinnedMeshRenderer,
                _sourceMesh = mesh,
            };

            created.ReadTopology(mesh, submesh);
            created.ReadGeometry();
            created.BuildTriangleAreas();

            if (created.TriangleCount == 0)
            {
                created.Dispose();
                error = $"Mesh '{mesh.name}' has no triangles in the selected submesh.";
                return false;
            }

            surface = created;
            error = null;
            return true;
        }

        public HairRootFrame Evaluate(in HairRootBinding binding)
        {
            ThrowIfDisposed();

            int offset = binding.TriangleIndex * 3;
            int i0 = _triangles[offset];
            int i1 = _triangles[offset + 1];
            int i2 = _triangles[offset + 2];

            Vector3 weights = binding.Weights();
            Vector3 position = _positions[i0] * weights.x + _positions[i1] * weights.y + _positions[i2] * weights.z;

            Vector3 normal = _normals != null && _normals.Length > 0
                ? _normals[i0] * weights.x + _normals[i1] * weights.y + _normals[i2] * weights.z
                : Vector3.Cross(_positions[i1] - _positions[i0], _positions[i2] - _positions[i0]);

            normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up;

            Vector2 uv = HasUvs
                ? _uvs[i0] * weights.x + _uvs[i1] * weights.y + _uvs[i2] * weights.z
                : Vector2.zero;

            Vector3 tangent = TriangleTangent(i0, i1, i2, normal);
            return new HairRootFrame(position, normal, tangent, uv);
        }

        public Vector3 EvaluatePosition(in HairRootBinding binding)
        {
            ThrowIfDisposed();

            int offset = binding.TriangleIndex * 3;
            Vector3 weights = binding.Weights();

            return _positions[_triangles[offset]] * weights.x
                   + _positions[_triangles[offset + 1]] * weights.y
                   + _positions[_triangles[offset + 2]] * weights.z;
        }

        public Vector2 EvaluateUv(in HairRootBinding binding)
        {
            ThrowIfDisposed();

            if (!HasUvs)
            {
                return Vector2.zero;
            }

            int offset = binding.TriangleIndex * 3;
            Vector3 weights = binding.Weights();

            return _uvs[_triangles[offset]] * weights.x
                   + _uvs[_triangles[offset + 1]] * weights.y
                   + _uvs[_triangles[offset + 2]] * weights.z;
        }

        public HairScalpSphere FitInteriorSphere(Matrix4x4 surfaceToTarget)
        {
            return FitInteriorSphere(surfaceToTarget, out _);
        }

        public HairScalpSphere FitInteriorSphere(Matrix4x4 surfaceToTarget, out Bounds bounds)
        {
            using ProfilerMarker.AutoScope scope = _fitSphereMarker.Auto();
            ThrowIfDisposed();

            if (_usedVertices.Length == 0)
            {
                bounds = new Bounds(Vector3.zero, Vector3.zero);
                return HairScalpSphere.None;
            }

            if (_fitPoints == null || _fitPoints.Length != _usedVertices.Length)
            {
                _fitPoints = new Vector3[_usedVertices.Length];
            }

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float minZ = float.MaxValue;
            float maxX = -float.MaxValue;
            float maxY = -float.MaxValue;
            float maxZ = -float.MaxValue;

            for (int i = 0; i < _usedVertices.Length; i++)
            {
                Vector3 point = surfaceToTarget.MultiplyPoint3x4(_positions[_usedVertices[i]]);
                _fitPoints[i] = point;

                minX = point.x < minX ? point.x : minX;
                minY = point.y < minY ? point.y : minY;
                minZ = point.z < minZ ? point.z : minZ;
                maxX = point.x > maxX ? point.x : maxX;
                maxY = point.y > maxY ? point.y : maxY;
                maxZ = point.z > maxZ ? point.z : maxZ;
            }

            var center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
            bounds = new Bounds(center, new Vector3(maxX - minX, maxY - minY, maxZ - minZ));
            float squaredRadius = float.MaxValue;

            for (int i = 0; i < _fitPoints.Length; i++)
            {
                float dx = _fitPoints[i].x - center.x;
                float dy = _fitPoints[i].y - center.y;
                float dz = _fitPoints[i].z - center.z;
                float squared = dx * dx + dy * dy + dz * dz;
                squaredRadius = squared < squaredRadius ? squared : squaredRadius;
            }

            return new HairScalpSphere(center, Mathf.Sqrt(squaredRadius));
        }

        public float TriangleArea(int triangleIndex)
        {
            return _triangleArea[triangleIndex];
        }

        public void TriangleUvs(int triangleIndex, out Vector2 corner0, out Vector2 corner1, out Vector2 corner2)
        {
            if (!HasUvs)
            {
                corner0 = Vector2.zero;
                corner1 = Vector2.zero;
                corner2 = Vector2.zero;
                return;
            }

            int offset = triangleIndex * 3;
            corner0 = _uvs[_triangles[offset]];
            corner1 = _uvs[_triangles[offset + 1]];
            corner2 = _uvs[_triangles[offset + 2]];
        }

        public void Dispose()
        {
            DestroyBakedMesh();
            _positions = null;
            _normals = null;
            _uvs = null;
            _triangles = null;
            _usedVertices = null;
            _triangleArea = null;
            _fitPoints = null;
            _vertexScratch.Clear();
            _normalScratch.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(HairSurface),
                    "The scalp surface was released by a rebuild. Read LinePrimitiveRenderer.Surface again.");
            }
        }

        private static Mesh ResolveMesh(Renderer renderer, out string error)
        {
            error = null;

            if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null)
                {
                    error = $"'{renderer.name}' has no mesh assigned.";
                }
                return skinned.sharedMesh;
            }

            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                error = $"'{renderer.name}' has no MeshFilter with a mesh. " +
                        "Pick a MeshRenderer or a SkinnedMeshRenderer.";
                return null;
            }

            return filter.sharedMesh;
        }

        private void ReadTopology(Mesh mesh, int submesh)
        {
            _triangles = submesh < 0 ? mesh.triangles : mesh.GetTriangles(submesh);
            _uvs = mesh.uv;
            HasUvs = _uvs != null && _uvs.Length > 0;
            CollectUsedVertices(mesh.vertexCount);

            Fingerprint = mesh.vertexCount
                          ^ (_triangles.Length << 8)
                          ^ (mesh.subMeshCount << 24)
                          ^ mesh.name.GetHashCode();
        }

        private void CollectUsedVertices(int vertexCount)
        {
            var seen = new bool[vertexCount];
            int unique = 0;

            for (int i = 0; i < _triangles.Length; i++)
            {
                int index = _triangles[i];

                if (seen[index])
                {
                    continue;
                }

                seen[index] = true;
                unique++;
            }

            _usedVertices = new int[unique];
            int slot = 0;

            for (int index = 0; index < vertexCount; index++)
            {
                if (seen[index])
                {
                    _usedVertices[slot++] = index;
                }
            }
        }

        private void ReadGeometry()
        {
            Mesh mesh = _sourceMesh;

            if (_skinnedRenderer != null)
            {
                if (_bakedMesh == null)
                {
                    _bakedMesh = new Mesh { name = "HairLines Baked Scalp", hideFlags = HideFlags.HideAndDontSave };
                }

                _skinnedRenderer.BakeMesh(_bakedMesh, true);
                mesh = _bakedMesh;
            }

            mesh.GetVertices(_vertexScratch);
            _positions = CopyInto(_vertexScratch, _positions);

            mesh.GetNormals(_normalScratch);
            _normals = CopyInto(_normalScratch, _normals);
        }

        private static Vector3[] CopyInto(List<Vector3> source, Vector3[] destination)
        {
            if (destination == null || destination.Length != source.Count)
            {
                destination = new Vector3[source.Count];
            }

            source.CopyTo(destination);
            return destination;
        }

        private void BuildTriangleAreas()
        {
            int count = TriangleCount;
            _triangleArea = new float[count];

            Matrix4x4 toWorld = LocalToWorld;
            TotalArea = 0f;

            for (int i = 0; i < count; i++)
            {
                int offset = i * 3;
                Vector3 corner0 = toWorld.MultiplyPoint3x4(_positions[_triangles[offset]]);
                Vector3 corner1 = toWorld.MultiplyPoint3x4(_positions[_triangles[offset + 1]]);
                Vector3 corner2 = toWorld.MultiplyPoint3x4(_positions[_triangles[offset + 2]]);

                _triangleArea[i] = Vector3.Cross(corner1 - corner0, corner2 - corner0).magnitude * 0.5f;
                TotalArea += _triangleArea[i];
            }
        }

        private Vector3 TriangleTangent(int i0, int i1, int i2, Vector3 normal)
        {
            Vector3 edge1 = _positions[i1] - _positions[i0];
            Vector3 edge2 = _positions[i2] - _positions[i0];
            Vector3 tangent;

            if (HasUvs)
            {
                Vector2 deltaUv1 = _uvs[i1] - _uvs[i0];
                Vector2 deltaUv2 = _uvs[i2] - _uvs[i0];
                float determinant = deltaUv1.x * deltaUv2.y - deltaUv2.x * deltaUv1.y;

                tangent = Mathf.Abs(determinant) > 1e-12f
                    ? (edge1 * deltaUv2.y - edge2 * deltaUv1.y) / determinant
                    : edge1;
            }
            else
            {
                tangent = edge1;
            }

            tangent -= normal * Vector3.Dot(tangent, normal);
            return tangent.sqrMagnitude > 1e-12f ? tangent.normalized : HairRootFrame.PerpendicularTo(normal);
        }

        private void DestroyBakedMesh()
        {
            if (_bakedMesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_bakedMesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_bakedMesh);
            }

            _bakedMesh = null;
        }
    }
}
