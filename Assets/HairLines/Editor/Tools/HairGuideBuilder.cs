using System.Collections.Generic;
using UnityEngine;

namespace HairLines.EditorTools
{
    public static class HairGuideBuilder
    {
        private const int GuideSeedSalt = 92821;
        private const int GuideSeedStride = 7919;

        public static bool TryGenerate(HairGroup group, LinePrimitiveRenderer owner, int count, out string error)
        {
            if (owner == null)
            {
                error = "The group has no Hair Lines renderer above it.";
                return false;
            }

            if (owner.Scalp.Mode != HairScalpMode.Mesh)
            {
                error = "Guides need the Mesh scalp mode.";
                return false;
            }

            if (!HairSurface.TryCreate(owner.Scalp.Renderer, owner.Scalp.Submesh, out HairSurface surface, out error))
            {
                return false;
            }

            try
            {
                HairGrowSettings settings = group.ResolvedSettings;
                IHairDensityMask mask = settings.DensityMask.IsUsable ? settings.DensityMask : null;
                var distribution = new HairSurfaceDistribution(surface, mask);
                HairRootBinding[] bindings = HairSurfaceSampler.Sample(distribution, count, settings.Seed);

                if (bindings.Length == 0)
                {
                    error = "The density mask leaves no room for guides.";
                    return false;
                }

                int vertexCount = settings.SegmentsPerStrand + 1;
                var guides = new List<HairGuide>(bindings.Length);

                for (int g = 0; g < bindings.Length; g++)
                {
                    HairRootFrame frame = surface.Evaluate(bindings[g]);
                    int seed = settings.Seed * GuideSeedSalt + g * GuideSeedStride + 13;
                    Vector3[] local = LineHairGenerator.GrowGuideShape(frame, settings, seed, vertexCount);
                    var guide = new HairGuide(bindings[g], frame.Uv, local)
                    {
                    };
                    guides.Add(guide);
                }

                settings.Guides.Replace(guides);
                error = null;
                return true;
            }
            finally
            {
                surface.Dispose();
            }
        }
    }
}
