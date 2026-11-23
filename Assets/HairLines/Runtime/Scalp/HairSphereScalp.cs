using Unity.Profiling;
using UnityEngine;

namespace HairLines
{
    public static class HairSphereScalp
    {
        private const float GoldenAngle = 2.399963f;
        private const int PolarRadix = 2;

        private static readonly ProfilerMarker _sampleMarker =
            new ProfilerMarker("HairLines.Scalp.SampleSphereRoots");

        public static HairRootFrame[] SampleRoots(Vector3 center, float radius, float coverage, int count, int seed)
        {
            using ProfilerMarker.AutoScope scope = _sampleMarker.Auto();
            var frames = new HairRootFrame[Mathf.Max(0, count)];
            float rotation = HairRandom.HashToUnitFloat(seed) * Mathf.PI * 2f;

            for (int i = 0; i < frames.Length; i++)
            {
                float polar = HairRandom.RadicalInverse(PolarRadix, i + 1);
                float cosTheta = 1f - 2f * coverage * polar;
                float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - cosTheta * cosTheta));
                float azimuth = i * GoldenAngle + rotation;

                Vector3 normal = new Vector3(sinTheta * Mathf.Cos(azimuth), cosTheta, sinTheta * Mathf.Sin(azimuth));
                Vector2 uv = new Vector2(Mathf.Repeat(azimuth / (Mathf.PI * 2f), 1f), polar);

                frames[i] = new HairRootFrame(center + normal * radius, normal,
                                              HairRootFrame.PerpendicularTo(normal), uv);
            }

            return frames;
        }
    }
}
