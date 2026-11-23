using System.Runtime.InteropServices;
using UnityEngine;

namespace HairLines
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct HairRootFrame
    {
        public Vector3 Position { get; }
        public Vector3 Normal { get; }
        public Vector3 Tangent { get; }
        public Vector2 Uv { get; }
        public Vector3 Bitangent => Vector3.Cross(Normal, Tangent);

        public HairRootFrame(Vector3 position, Vector3 normal, Vector3 tangent, Vector2 uv)
        {
            Position = position;
            Normal = normal;
            Tangent = tangent;
            Uv = uv;
        }

        public HairRootFrame TransformedBy(Matrix4x4 matrix)
        {
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            Vector3 normal = normalMatrix.MultiplyVector(Normal);
            Vector3 tangent = matrix.MultiplyVector(Tangent);

            normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up;
            tangent -= normal * Vector3.Dot(tangent, normal);
            tangent = tangent.sqrMagnitude > 1e-12f ? tangent.normalized : PerpendicularTo(normal);

            return new HairRootFrame(matrix.MultiplyPoint3x4(Position), normal, tangent, Uv);
        }

        public static Vector3 PerpendicularTo(Vector3 direction)
        {
            Vector3 reference = Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right;
            return Vector3.Cross(direction, reference).normalized;
        }
    }
}
