using UnityEngine;

namespace HairLines
{
    public readonly struct HairRootBinding
    {
        public int TriangleIndex { get; }
        public Vector2 Barycentric { get; }

        public HairRootBinding(int triangleIndex, Vector2 barycentric)
        {
            TriangleIndex = triangleIndex;
            Barycentric = barycentric;
        }

        public Vector3 Weights()
        {
            return new Vector3(1f - Barycentric.x - Barycentric.y, Barycentric.x, Barycentric.y);
        }
    }
}
