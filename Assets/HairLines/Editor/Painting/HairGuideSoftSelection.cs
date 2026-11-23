using System.Collections.Generic;
using UnityEngine;

namespace HairLines.EditorTools
{
    internal enum HairGuideFalloff
    {
        Smooth,
        Sphere,
        Root,
        InverseSquare,
        Sharp,
        Linear,
        Constant,
    }

    internal static class HairGuideSoftSelection
    {
        public static float Falloff(HairGuideFalloff falloff, float distance, float radius)
        {
            if (distance > radius || radius <= 0f)
            {
                return 0f;
            }

            float x = Mathf.Max(0f, (radius - distance) / radius);
            return falloff switch
            {
                HairGuideFalloff.Smooth => Mathf.Min(1f, 3f * x * x - 2f * x * x * x),
                HairGuideFalloff.Sphere => Mathf.Sqrt(2f * x - x * x),
                HairGuideFalloff.Root => Mathf.Sqrt(x),
                HairGuideFalloff.InverseSquare => x * (2f - x),
                HairGuideFalloff.Sharp => x * x,
                HairGuideFalloff.Linear => x,
                _ => 1f,
            };
        }

        public static float[][] Weights(IReadOnlyList<Vector3[]> points, IReadOnlyList<bool[]> seeds,
                                        float radius, HairGuideFalloff falloff, bool connected)
        {
            var weights = new float[points.Count][];
            if (connected)
            {
                for (int g = 0; g < points.Count; g++)
                {
                    weights[g] = AlongCurve(points[g], seeds[g], radius, falloff);
                }

                return weights;
            }

            Dictionary<Vector3Int, List<Vector3>> grid = SeedGrid(points, seeds, radius);
            for (int g = 0; g < points.Count; g++)
            {
                weights[g] = AcrossSpace(points[g], seeds[g], grid, radius, falloff);
            }

            return weights;
        }

        private static float[] AlongCurve(Vector3[] points, bool[] seeds, float radius, HairGuideFalloff falloff)
        {
            if (seeds == null)
            {
                return null;
            }

            var distance = new float[points.Length];
            float travelled = float.PositiveInfinity;
            for (int k = 0; k < points.Length; k++)
            {
                travelled = seeds[k] ? 0f
                    : k > 0 ? travelled + Vector3.Distance(points[k - 1], points[k])
                    : float.PositiveInfinity;
                distance[k] = travelled;
            }

            travelled = float.PositiveInfinity;
            for (int k = points.Length - 1; k >= 0; k--)
            {
                travelled = seeds[k] ? 0f
                    : k < points.Length - 1 ? travelled + Vector3.Distance(points[k + 1], points[k])
                    : float.PositiveInfinity;
                distance[k] = Mathf.Min(distance[k], travelled);
            }

            var weights = new float[points.Length];
            for (int k = 0; k < points.Length; k++)
            {
                weights[k] = seeds[k] ? 1f : Falloff(falloff, distance[k], radius);
            }

            return weights;
        }

        private static float[] AcrossSpace(Vector3[] points, bool[] seeds,
                                           Dictionary<Vector3Int, List<Vector3>> grid, float radius,
                                           HairGuideFalloff falloff)
        {
            float[] weights = null;
            float radiusSquared = radius * radius;
            for (int k = 0; k < points.Length; k++)
            {
                float weight;
                if (seeds != null && seeds[k])
                {
                    weight = 1f;
                }
                else
                {
                    float nearest = NearestSeedSquared(grid, points[k], radius);
                    weight = nearest <= radiusSquared ? Falloff(falloff, Mathf.Sqrt(nearest), radius) : 0f;
                }

                if (weight <= 0f)
                {
                    continue;
                }

                weights ??= new float[points.Length];
                weights[k] = weight;
            }

            return weights;
        }

        private static Dictionary<Vector3Int, List<Vector3>> SeedGrid(IReadOnlyList<Vector3[]> points,
                                                                      IReadOnlyList<bool[]> seeds, float radius)
        {
            var grid = new Dictionary<Vector3Int, List<Vector3>>();
            for (int g = 0; g < points.Count; g++)
            {
                if (seeds[g] == null)
                {
                    continue;
                }

                for (int k = 0; k < points[g].Length; k++)
                {
                    if (!seeds[g][k])
                    {
                        continue;
                    }

                    Vector3Int cell = Cell(points[g][k], radius);
                    if (!grid.TryGetValue(cell, out List<Vector3> bucket))
                    {
                        bucket = new List<Vector3>();
                        grid.Add(cell, bucket);
                    }

                    bucket.Add(points[g][k]);
                }
            }

            return grid;
        }

        private static float NearestSeedSquared(Dictionary<Vector3Int, List<Vector3>> grid, Vector3 point,
                                                float radius)
        {
            Vector3Int center = Cell(point, radius);
            float nearest = float.PositiveInfinity;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                if (!grid.TryGetValue(center + new Vector3Int(x, y, z), out List<Vector3> bucket))
                {
                    continue;
                }

                foreach (Vector3 seed in bucket)
                {
                    nearest = Mathf.Min(nearest, (seed - point).sqrMagnitude);
                }
            }

            return nearest;
        }

        private static Vector3Int Cell(Vector3 point, float radius)
        {
            return Vector3Int.FloorToInt(point / Mathf.Max(radius, 1e-5f));
        }
    }
}
