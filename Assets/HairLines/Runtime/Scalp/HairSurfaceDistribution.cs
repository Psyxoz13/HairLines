using Unity.Profiling;
using UnityEngine;

namespace HairLines
{
    public class HairSurfaceDistribution
    {
        public HairSurface Surface => _surface;
        public float TotalArea => _surface != null ? _surface.TotalArea : 0f;
        public float TotalWeight => _cumulativeWeight.Length > 0 ? _cumulativeWeight[_cumulativeWeight.Length - 1] : 0f;
        public float CoveredArea { get; private set; }
        public float Coverage => TotalArea > 1e-8f ? Mathf.Clamp01(CoveredArea / TotalArea) : 0f;

        internal float[] CumulativeWeight => _cumulativeWeight;
        internal float[] TriangleMasks => _triangleMask;
        internal IHairDensityMask Mask => _mask;

        private readonly HairSurface _surface;
        private readonly IHairDensityMask _mask;

        private readonly float[] _cumulativeWeight;
        private readonly float[] _triangleMask;

        private static readonly ProfilerMarker _buildMarker =
            new ProfilerMarker("HairLines.Scalp.BuildDistribution");

        public HairSurfaceDistribution(HairSurface surface, IHairDensityMask mask)
        {
            using ProfilerMarker.AutoScope scope = _buildMarker.Auto();
            _surface = surface;
            _mask = mask;

            int count = surface.TriangleCount;
            _cumulativeWeight = new float[count];
            _triangleMask = new float[count];

            float running = 0f;
            CoveredArea = 0f;

            for (int i = 0; i < count; i++)
            {
                _triangleMask[i] = CornerAverage(i);
                float area = surface.TriangleArea(i);

                CoveredArea += area * _triangleMask[i];
                running += area * _triangleMask[i];
                _cumulativeWeight[i] = running;
            }
        }

        public int PickTriangle(float unitRandom)
        {
            float target = Mathf.Clamp01(unitRandom) * TotalWeight;

            int low = 0;
            int high = _cumulativeWeight.Length - 1;

            while (low < high)
            {
                int middle = (low + high) >> 1;

                if (_cumulativeWeight[middle] < target)
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

        public float TriangleMask(int triangleIndex)
        {
            return _triangleMask[triangleIndex];
        }

        public float MaskAt(Vector2 uv)
        {
            return _mask == null ? 1f : Mathf.Clamp01(_mask.Evaluate(uv));
        }

        private float CornerAverage(int triangleIndex)
        {
            if (_mask == null || !_surface.HasUvs)
            {
                return 1f;
            }

            _surface.TriangleUvs(triangleIndex, out Vector2 corner0, out Vector2 corner1, out Vector2 corner2);

            float sum = Mathf.Clamp01(_mask.Evaluate(corner0))
                        + Mathf.Clamp01(_mask.Evaluate(corner1))
                        + Mathf.Clamp01(_mask.Evaluate(corner2));

            return sum / 3f;
        }
    }
}
