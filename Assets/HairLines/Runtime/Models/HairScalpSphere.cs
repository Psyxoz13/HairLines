using UnityEngine;

namespace HairLines
{
    public readonly struct HairScalpSphere
    {
        public Vector3 Center { get; }
        public float Radius { get; }
        public bool IsValid => Radius > 0f;

        public HairScalpSphere(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public static HairScalpSphere None => new HairScalpSphere(Vector3.zero, 0f);

        public Vector3 PushOut(Vector3 point)
        {
            Vector3 offset = point - Center;
            float distance = offset.magnitude;

            if (distance >= Radius)
            {
                return point;
            }

            if (distance < 1e-6f)
            {
                return Center + Vector3.up * Radius;
            }

            return Center + offset * (Radius / distance);
        }
    }
}
