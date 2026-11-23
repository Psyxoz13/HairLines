using Unity.Mathematics;

namespace HairLines
{
    public static class HairCatmullRom
    {
        public static float3 Centripetal(float3 p0, float3 p1, float3 p2, float3 p3, float u)
        {
            float t0 = 0f;
            float t1 = t0 + math.max(math.sqrt(math.distance(p0, p1)), 1e-5f);
            float t2 = t1 + math.max(math.sqrt(math.distance(p1, p2)), 1e-5f);
            float t3 = t2 + math.max(math.sqrt(math.distance(p2, p3)), 1e-5f);
            float t = math.lerp(t1, t2, u);

            float3 a1 = ((t1 - t) * p0 + (t - t0) * p1) / (t1 - t0);
            float3 a2 = ((t2 - t) * p1 + (t - t1) * p2) / (t2 - t1);
            float3 a3 = ((t3 - t) * p2 + (t - t2) * p3) / (t3 - t2);
            float3 b1 = ((t2 - t) * a1 + (t - t0) * a2) / (t2 - t0);
            float3 b2 = ((t3 - t) * a2 + (t - t1) * a3) / (t3 - t1);
            return ((t2 - t) * b1 + (t - t1) * b2) / (t2 - t1);
        }
    }
}
