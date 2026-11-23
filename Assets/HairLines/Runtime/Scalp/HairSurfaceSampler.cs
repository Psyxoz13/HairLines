using System;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace HairLines
{
    public static class HairSurfaceSampler
    {
        private const int CandidatesPerStrand = 64;
        private const int CandidateFloor = 1024;

        private static readonly ProfilerMarker _sampleMarker =
            new ProfilerMarker("HairLines.Scalp.SampleRoots");
        private static readonly ProfilerMarker _evaluateMarker =
            new ProfilerMarker("HairLines.Scalp.EvaluateRoots");

        public static HairRootBinding[] Sample(HairSurfaceDistribution distribution, int count, int seed)
        {
            using ProfilerMarker.AutoScope scope = _sampleMarker.Auto();
            int requested = Mathf.Max(0, count);
            int wanted = EffectiveCount(distribution, requested);
            var bindings = new HairRootBinding[wanted];

            if (wanted == 0 || distribution == null || distribution.TotalWeight <= 0f)
            {
                return Array.Empty<HairRootBinding>();
            }

            int pool = wanted * HairSampleElimination.CandidateMultiple;
            var candidates = new HairRootBinding[pool];
            var positions = new NativeArray<float3>(pool, Allocator.TempJob);
            var density = new NativeArray<float>(pool, Allocator.TempJob);

            try
            {
                int accepted = HairSurfaceDraw.Draw(distribution, seed,
                                                   candidates.Length * CandidatesPerStrand + CandidateFloor,
                                                   candidates, positions, density);

                if (accepted <= wanted)
                {
                    Array.Resize(ref bindings, accepted);
                    Array.Copy(candidates, bindings, accepted);
                    return bindings;
                }

                var order = new NativeArray<int>(wanted, Allocator.TempJob);

                try
                {
                    HairSampleElimination.Order(positions.GetSubArray(0, accepted),
                                                density.GetSubArray(0, accepted),
                                                distribution.CoveredArea, order);

                    for (int i = 0; i < wanted; i++)
                    {
                        bindings[i] = candidates[order[i]];
                    }
                }
                finally
                {
                    order.Dispose();
                }
            }
            finally
            {
                positions.Dispose();
                density.Dispose();
            }

            return bindings;
        }

        private static int EffectiveCount(HairSurfaceDistribution distribution, int requested)
        {
            if (requested == 0 || distribution == null || distribution.TotalWeight <= 0f)
            {
                return 0;
            }

            if (distribution.Coverage <= 0f)
            {
                return 0;
            }

            return requested;
        }

        public static HairRootFrame[] Evaluate(HairSurface surface, HairRootBinding[] bindings, Matrix4x4 surfaceToTarget)
        {
            return Evaluate(surface, bindings, bindings.Length, surfaceToTarget);
        }

        public static HairRootFrame[] Evaluate(HairSurface surface, HairRootBinding[] bindings, int count,
                                               Matrix4x4 surfaceToTarget)
        {
            using ProfilerMarker.AutoScope scope = _evaluateMarker.Auto();
            count = Mathf.Clamp(count, 0, bindings.Length);
            var frames = new HairRootFrame[count];

            for (int i = 0; i < count; i++)
            {
                frames[i] = surface.Evaluate(bindings[i]).TransformedBy(surfaceToTarget);
            }

            return frames;
        }

        public static void EvaluateInto(HairSurface surface, HairRootBinding[] bindings,
                                        Matrix4x4 surfaceToTarget, HairRootFrame[] frames)
        {
            using ProfilerMarker.AutoScope scope = _evaluateMarker.Auto();
            for (int i = 0; i < bindings.Length; i++)
            {
                frames[i] = surface.Evaluate(bindings[i]).TransformedBy(surfaceToTarget);
            }
        }

    }
}
