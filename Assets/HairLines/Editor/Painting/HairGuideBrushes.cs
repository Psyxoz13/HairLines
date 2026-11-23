using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    internal static class HairGuideBrushes
    {
        public const float TwistDegreesPerStep = 30f;
        public const float PuffRate = 0.1f;
        public const float StepPixels = 10f;
        public const float CurlRampSpan = 3f;

        public static void Grab(Vector3[] points, float[] weights, Vector3 delta)
        {
            for (int k = 1; k < points.Length; k++)
            {
                points[k] += delta * weights[k];
            }
        }

        public static void Comb(Vector3[] points, float[] weights, Vector3 direction)
        {
            var before = (Vector3[])points.Clone();
            for (int k = 1; k < points.Length; k++)
            {
                float length = Vector3.Distance(before[k - 1], before[k]);
                Vector3 target = points[k - 1] + direction * length;
                points[k] = Vector3.Lerp(before[k], target, Mathf.Clamp01(weights[k]));
            }
        }

        public static void Smooth(Vector3[] points, float[] weights)
        {
            var before = (Vector3[])points.Clone();
            for (int k = 1; k < points.Length - 1; k++)
            {
                Vector3 target = (before[k - 1] + before[k + 1]) * 0.5f;
                points[k] = Vector3.Lerp(before[k], target, Mathf.Clamp01(weights[k]));
            }

            int tip = points.Length - 1;
            if (points.Length > 2 && weights[tip] > 0f)
            {
                Vector3 tangent = before[tip - 1] - before[tip - 2];
                if (tangent.sqrMagnitude > 1e-12f)
                {
                    float length = Vector3.Distance(before[tip - 1], before[tip]);
                    Vector3 target = before[tip - 1] + tangent.normalized * length;
                    points[tip] = Vector3.Lerp(before[tip], target, Mathf.Clamp01(weights[tip]));
                }
            }
        }

        public static void ScaleFromRoot(Vector3[] points, float factor)
        {
            for (int k = 1; k < points.Length; k++)
            {
                points[k] = points[0] + (points[k] - points[0]) * factor;
            }
        }

        public static void Puff(Vector3[] points, float[] weights, Vector3 normal)
        {
            Vector3 root = points[0];
            float travelled = 0f;
            Vector3 previous = root;
            for (int k = 1; k < points.Length; k++)
            {
                Vector3 old = points[k];
                travelled += Vector3.Distance(previous, old);
                previous = old;

                Vector3 moved = Vector3.Lerp(old, root + normal * travelled, Mathf.Clamp01(weights[k]));
                float oldDistance = Vector3.Distance(old, root);
                float newDistance = Vector3.Distance(moved, root);
                if (newDistance < oldDistance && newDistance > 1e-8f)
                {
                    moved += (moved - root) / newDistance * (oldDistance - newDistance);
                }

                points[k] = moved;
            }
        }

        public static void Clump(Vector3[] points, float[] weights, Vector2 center, Vector3 viewNormal)
        {
            for (int k = 1; k < points.Length; k++)
            {
                if (weights[k] == 0f)
                {
                    continue;
                }

                Vector2 screen = HandleUtility.WorldToGUIPoint(points[k]);
                Ray ray = HandleUtility.GUIPointToWorldRay(Vector2.LerpUnclamped(screen, center, weights[k]));
                if (new Plane(viewNormal, points[k]).Raycast(ray, out float distance))
                {
                    points[k] = ray.GetPoint(distance);
                }
            }
        }

        public static void Twist(Vector3[] points, float[] degrees, Vector3 center, Vector3 axis)
        {
            for (int k = 1; k < points.Length; k++)
            {
                points[k] = center + Quaternion.AngleAxis(degrees[k], axis) * (points[k] - center);
            }
        }

        public static void Curl(Vector3[] points, float[] weights, Vector3 phaseReference, float radius, float turns)
        {
            int count = points.Length;
            if (count < 3 || radius < 0f || turns <= 0f)
            {
                return;
            }

            float[] travelled = ArcLengths(points);
            float total = travelled[count - 1];
            if (total < 1e-6f)
            {
                return;
            }

            Vector3[] stem = MovingAverage(points, travelled, total / turns);
            Vector3 normal = phaseReference;
            var target = new Vector3[count];
            target[0] = points[0];

            for (int k = 1; k < count; k++)
            {
                Vector3 tangent = stem[Mathf.Min(k + 1, count - 1)] - stem[k - 1];
                if (tangent.sqrMagnitude < 1e-12f)
                {
                    target[k] = points[k];
                    continue;
                }

                tangent.Normalize();
                normal -= tangent * Vector3.Dot(normal, tangent);
                normal = normal.sqrMagnitude > 1e-8f ? normal.normalized : HairRootFrame.PerpendicularTo(tangent);
                Vector3 bitangent = Vector3.Cross(tangent, normal);
                float along = travelled[k] / total;
                float angle = along * turns * Mathf.PI * 2f;
                float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, along * CurlRampSpan));
                target[k] = stem[k] + (normal * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * (radius * ramp);
            }

            for (int k = 1; k < count; k++)
            {
                points[k] = Vector3.Lerp(points[k], target[k], Mathf.Clamp01(weights[k]));
            }
        }

        private static float[] ArcLengths(Vector3[] points)
        {
            var travelled = new float[points.Length];
            for (int k = 1; k < points.Length; k++)
            {
                travelled[k] = travelled[k - 1] + Vector3.Distance(points[k - 1], points[k]);
            }

            return travelled;
        }

        private static Vector3[] MovingAverage(Vector3[] points, float[] travelled, float window)
        {
            int count = points.Length;
            var average = new Vector3[count];
            average[0] = points[0];
            float half = window * 0.5f;

            for (int k = 1; k < count; k++)
            {
                Vector3 sum = Vector3.zero;
                float weight = 0f;
                for (int j = 0; j < count; j++)
                {
                    if (Mathf.Abs(travelled[j] - travelled[k]) > half)
                    {
                        continue;
                    }

                    float spacing = (travelled[Mathf.Min(j + 1, count - 1)] - travelled[Mathf.Max(j - 1, 0)]) * 0.5f;
                    sum += points[j] * spacing;
                    weight += spacing;
                }

                average[k] = weight > 1e-8f ? sum / weight : points[k];
            }

            return average;
        }
    }
}
