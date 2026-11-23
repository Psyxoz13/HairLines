using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace HairLines
{
    public static class LineHairGenerator
    {
        private const int GuideSeedSalt = 92821;
        private const int GuideSeedStride = 7919;
        private const int StrandSeedSalt = 6151;
        private const int StrandSeedStride = 31337;
        private const float CombThreshold = 1e-3f;

        private static readonly ProfilerMarker _growMarker =
            new ProfilerMarker("HairLines.Grooming.Grow");
        private static readonly ProfilerMarker _guideShapeMarker =
            new ProfilerMarker("HairLines.Grooming.GrowGuideShape");

        public static Vector3[] GrowGuideShape(in HairRootFrame root, HairGrowSettings settings,
                                               int seed, int vertexCount)
        {
            using ProfilerMarker.AutoScope scope = _guideShapeMarker.Auto();
            var points = new Vector3[vertexCount];
            var rng = new HairRandom(seed);
            float[] segmentLengthFractions = settings.BuildSegmentLengthFractions(vertexCount - 1);
            IntegrateStrand(root, settings, ref rng, points, 0, vertexCount,
                            segmentLengthFractions);

            Vector3 bitangent = root.Bitangent;

            for (int k = 0; k < vertexCount; k++)
            {
                Vector3 offset = points[k] - root.Position;
                points[k] = new Vector3(Vector3.Dot(offset, root.Tangent),
                                        Vector3.Dot(offset, bitangent),
                                        Vector3.Dot(offset, root.Normal));
            }

            return points;
        }

        private static void IntegrateStrand(in HairRootFrame root, HairGrowSettings settings, ref HairRandom rng,
                                            Vector3[] destination, int offset, int vertexCount,
                                            float[] segmentLengthFractions)
        {
            int segments = vertexCount - 1;
            float length = settings.StrandLength * (1f + rng.Range(-settings.LengthVariance, settings.LengthVariance));

            Vector3 lay = FlowDirection(root, settings);
            Vector3 direction = root.Normal;
            Vector3 point = root.Position;
            destination[offset] = point;

            for (int k = 1; k <= segments; k++)
            {
                float alongStrand = k / (float)segments;
                Vector3 pull = Vector3.down * (settings.Gravity * alongStrand);
                Vector3 settled = Vector3.Slerp(direction, lay, settings.ScalpFlow * alongStrand);

                if (settled.sqrMagnitude < 1e-8f)
                {
                    settled = direction;
                }

                direction = (settled.normalized * settings.Stiffness + pull).normalized;
                if (direction.sqrMagnitude < 1e-8f)
                {
                    direction = root.Normal;
                }

                point += direction * (length * segmentLengthFractions[k - 1]);
                destination[offset + k] = point;
            }
        }

        private static Vector3 SurfaceDownhill(Vector3 normal)
        {
            Vector3 downhill = Vector3.down - normal * Vector3.Dot(Vector3.down, normal);

            if (downhill.sqrMagnitude < 1e-6f)
            {
                downhill = new Vector3(normal.x, 0f, normal.z);

                if (downhill.sqrMagnitude < 1e-6f)
                {
                    downhill = Vector3.forward;
                }
            }

            return downhill.normalized;
        }

        private static Vector3 FlowDirection(in HairRootFrame root, HairGrowSettings settings)
        {
            Vector3 downhill = SurfaceDownhill(root.Normal);

            if (!settings.CombMap.IsUsable)
            {
                return downhill;
            }

            Vector2 combed = settings.CombMap.Evaluate(root.Uv);
            float strength = Mathf.Clamp01(combed.magnitude);

            if (strength < CombThreshold)
            {
                return downhill;
            }

            Vector3 alongSurface = root.Tangent * combed.x + root.Bitangent * combed.y;

            if (alongSurface.sqrMagnitude < 1e-10f)
            {
                return downhill;
            }

            return Vector3.Slerp(downhill, alongSurface.normalized, strength).normalized;
        }

        private static float WrappedUvDistance(Vector2 a, Vector2 b)
        {
            float horizontal = Mathf.Abs(a.x - b.x);
            horizontal = Mathf.Min(horizontal, 1f - horizontal);
            float vertical = a.y - b.y;
            return horizontal * horizontal + vertical * vertical;
        }
    }
}
