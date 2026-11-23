using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace HairLines
{
    internal static class HairSurfaceDraw
    {
        private const int TriangleRadix = 2;
        private const int FirstBarycentricRadix = 3;
        private const int SecondBarycentricRadix = 5;
        private const int AcceptanceRadix = 7;
        private const int BatchSize = 64;

        private static readonly ProfilerMarker _drawMarker =
            new ProfilerMarker("HairLines.Scalp.DrawCandidates");

        public static int Draw(HairSurfaceDistribution distribution, int seed, int limit,
                               HairRootBinding[] candidates, NativeArray<float3> positions,
                               NativeArray<float> density)
        {
            using ProfilerMarker.AutoScope scope = _drawMarker.Auto();
            HairSurface surface = distribution.Surface;
            int wanted = candidates.Length;

            var vertexPositions = ToNative(surface.VertexPositions);
            var vertexUvs = ToNative(surface.VertexUvs);
            var triangles = new NativeArray<int>(surface.Triangles, Allocator.TempJob);
            var cumulativeWeight = new NativeArray<float>(distribution.CumulativeWeight, Allocator.TempJob);
            var triangleMasks = new NativeArray<float>(distribution.TriangleMasks, Allocator.TempJob);
            HairMaskLayers mask = HairMaskLayers.Build(distribution.Mask);

            int chunk = math.clamp(wanted, 4096, 1 << 20);
            var triangleOut = new NativeArray<int>(chunk, Allocator.TempJob,
                                                   NativeArrayOptions.UninitializedMemory);
            var baryOut = new NativeArray<float2>(chunk, Allocator.TempJob,
                                                  NativeArrayOptions.UninitializedMemory);
            var positionOut = new NativeArray<float3>(chunk, Allocator.TempJob,
                                                      NativeArrayOptions.UninitializedMemory);
            var densityOut = new NativeArray<float>(chunk, Allocator.TempJob,
                                                    NativeArrayOptions.UninitializedMemory);
            var acceptedOut = new NativeArray<byte>(chunk, Allocator.TempJob,
                                                    NativeArrayOptions.UninitializedMemory);

            Matrix4x4 toWorld = surface.LocalToWorld;
            int accepted = 0;
            int candidate = 1;

            try
            {
                while (accepted < wanted && candidate <= limit)
                {
                    int count = math.min(chunk, limit - candidate + 1);

                    new DrawJob
                    {
                        vertexPositions = vertexPositions,
                        vertexUvs = vertexUvs,
                        triangles = triangles,
                        cumulativeWeight = cumulativeWeight,
                        triangleMasks = triangleMasks,
                        maskPixels = mask.Pixels,
                        maskLayers = mask.Layers,
                        maskLayerCount = mask.Count,
                        totalWeight = distribution.TotalWeight,
                        hasUvs = surface.HasUvs ? 1 : 0,
                        toWorld = toWorld,
                        rotations = new float4(HairRandom.HashToUnitFloat(seed),
                                               HairRandom.HashToUnitFloat(seed + 1),
                                               HairRandom.HashToUnitFloat(seed + 2),
                                               HairRandom.HashToUnitFloat(seed + 3)),
                        candidateBase = candidate,
                        triangleOut = triangleOut,
                        baryOut = baryOut,
                        positionOut = positionOut,
                        densityOut = densityOut,
                        acceptedOut = acceptedOut,
                    }.Schedule(count, BatchSize).Complete();

                    for (int i = 0; i < count && accepted < wanted; i++)
                    {
                        if (acceptedOut[i] == 0)
                        {
                            continue;
                        }

                        candidates[accepted] = new HairRootBinding(triangleOut[i], baryOut[i]);
                        positions[accepted] = positionOut[i];
                        density[accepted] = densityOut[i];
                        accepted++;
                    }

                    candidate += count;
                }
            }
            finally
            {
                vertexPositions.Dispose();
                vertexUvs.Dispose();
                triangles.Dispose();
                cumulativeWeight.Dispose();
                triangleMasks.Dispose();
                mask.Dispose();
                triangleOut.Dispose();
                baryOut.Dispose();
                positionOut.Dispose();
                densityOut.Dispose();
                acceptedOut.Dispose();
            }

            return accepted;
        }

        private static NativeArray<float3> ToNative(Vector3[] source)
        {
            int length = source != null ? source.Length : 0;
            var result = new NativeArray<float3>(math.max(1, length), Allocator.TempJob,
                                                 NativeArrayOptions.ClearMemory);

            for (int i = 0; i < length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        private static NativeArray<float2> ToNative(Vector2[] source)
        {
            int length = source != null ? source.Length : 0;
            var result = new NativeArray<float2>(math.max(1, length), Allocator.TempJob,
                                                 NativeArrayOptions.ClearMemory);

            for (int i = 0; i < length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
        private struct DrawJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> vertexPositions;
            [ReadOnly] public NativeArray<float2> vertexUvs;
            [ReadOnly] public NativeArray<int> triangles;
            [ReadOnly] public NativeArray<float> cumulativeWeight;
            [ReadOnly] public NativeArray<float> triangleMasks;
            [ReadOnly] public NativeArray<byte> maskPixels;
            [ReadOnly] public NativeArray<int4> maskLayers;

            public int maskLayerCount;
            public float totalWeight;
            public int hasUvs;
            public float4x4 toWorld;
            public float4 rotations;
            public int candidateBase;

            [WriteOnly] public NativeArray<int> triangleOut;
            [WriteOnly] public NativeArray<float2> baryOut;
            [WriteOnly] public NativeArray<float3> positionOut;
            [WriteOnly] public NativeArray<float> densityOut;
            [WriteOnly] public NativeArray<byte> acceptedOut;

            public void Execute(int index)
            {
                int candidate = candidateBase + index;

                float triangleSample = Wrap(RadicalInverse(TriangleRadix, candidate) + rotations.x);
                float first = Wrap(RadicalInverse(FirstBarycentricRadix, candidate) + rotations.y);
                float second = Wrap(RadicalInverse(SecondBarycentricRadix, candidate) + rotations.z);

                int triangle = PickTriangle(triangleSample);
                float edge = math.sqrt(first);
                float2 bary = new float2(edge * (1f - second), edge * second);

                acceptedOut[index] = 0;
                triangleOut[index] = triangle;
                baryOut[index] = bary;

                float triangleMask = triangleMasks[triangle];

                if (triangleMask <= 0f)
                {
                    positionOut[index] = float3.zero;
                    densityOut[index] = 0f;
                    return;
                }

                float3 weights = new float3(1f - bary.x - bary.y, bary.x, bary.y);
                int offset = triangle * 3;
                int i0 = triangles[offset];
                int i1 = triangles[offset + 1];
                int i2 = triangles[offset + 2];

                float2 uv = hasUvs != 0
                    ? vertexUvs[i0] * weights.x + vertexUvs[i1] * weights.y + vertexUvs[i2] * weights.z
                    : float2.zero;

                float here = maskLayerCount > 0
                    ? math.saturate(HairMaskLayers.Evaluate(maskPixels, maskLayers, maskLayerCount, uv))
                    : 1f;

                float acceptance = math.saturate(here / triangleMask);
                float sample = Wrap(RadicalInverse(AcceptanceRadix, candidate) + rotations.w);

                if (sample >= acceptance)
                {
                    positionOut[index] = float3.zero;
                    densityOut[index] = 0f;
                    return;
                }

                float3 local = vertexPositions[i0] * weights.x + vertexPositions[i1] * weights.y +
                               vertexPositions[i2] * weights.z;

                positionOut[index] = math.transform(toWorld, local);
                densityOut[index] = here;
                acceptedOut[index] = 1;
            }

            private int PickTriangle(float unitRandom)
            {
                float target = math.saturate(unitRandom) * totalWeight;

                int low = 0;
                int high = cumulativeWeight.Length - 1;

                while (low < high)
                {
                    int middle = (low + high) >> 1;

                    if (cumulativeWeight[middle] < target)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                return low;
            }

            private static float RadicalInverse(int radix, int index)
            {
                float inverseRadix = 1f / radix;
                float scale = inverseRadix;
                float result = 0f;

                while (index > 0)
                {
                    result += (index % radix) * scale;
                    index /= radix;
                    scale *= inverseRadix;
                }

                return result;
            }

            private static float Wrap(float value)
            {
                return value - math.floor(value);
            }
        }
    }
}
