using System;
using System.Collections.Generic;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairGuideSet
    {
        public List<HairGuide> Guides => _guides;
        public int Count => _guides.Count;
        public bool IsEmpty => _guides.Count == 0;
        public int MaxPointCount
        {
            get
            {
                int max = 0;
                foreach (HairGuide guide in _guides)
                {
                    max = Mathf.Max(max, guide != null ? guide.PointCount : 0);
                }
                return max;
            }
        }

        [SerializeReference]
        private List<HairGuide> _guides = new List<HairGuide>();

        public const int MaxInfluences = 4;

        private const float DistanceFloor = 1e-4f;

        public void Replace(IEnumerable<HairGuide> guides)
        {
            _guides.Clear();
            _guides.AddRange(guides);
        }

        public void Clear()
        {
            _guides.Clear();
        }

        public bool HasUniformPointCount(int pointCount)
        {
            foreach (HairGuide guide in _guides)
            {
                if (guide == null || guide.PointCount != pointCount)
                {
                    return false;
                }
            }
            return _guides.Count > 0;
        }

        public int FindNearest(Vector2 uv, int[] indices, float[] weights, float sharpness)
        {
            int wanted = Mathf.Min(indices.Length, _guides.Count);

            if (wanted == 0)
            {
                return 0;
            }

            for (int i = 0; i < wanted; i++)
            {
                indices[i] = -1;
                weights[i] = float.MaxValue;
            }

            for (int g = 0; g < _guides.Count; g++)
            {
                HairGuide guide = _guides[g];
                if (guide == null)
                {
                    continue;
                }

                float distance = WrappedDistance(uv, guide.Uv);
                int slot = wanted;

                while (slot > 0 && weights[slot - 1] > distance)
                {
                    slot--;
                }

                if (slot >= wanted)
                {
                    continue;
                }

                for (int s = wanted - 1; s > slot; s--)
                {
                    indices[s] = indices[s - 1];
                    weights[s] = weights[s - 1];
                }

                indices[slot] = g;
                weights[slot] = distance;
            }

            float total = 0f;
            float nearest = weights[0] + DistanceFloor;

            for (int i = 0; i < wanted; i++)
            {
                if (indices[i] < 0)
                {
                    weights[i] = 0f;
                    continue;
                }

                float ratio = nearest / (weights[i] + DistanceFloor);
                weights[i] = ratio * ratio;
                total += weights[i];
            }

            if (total <= 0f)
            {
                return 0;
            }

            float snap = Mathf.Clamp01(sharpness);

            for (int i = 0; i < wanted; i++)
            {
                float smooth = weights[i] / total;
                weights[i] = Mathf.Lerp(smooth, i == 0 ? 1f : 0f, snap);
            }

            return wanted;
        }

        public static float WrappedDistance(Vector2 a, Vector2 b)
        {
            float horizontal = Mathf.Abs(a.x - b.x);
            horizontal = Mathf.Min(horizontal, 1f - horizontal);
            float vertical = a.y - b.y;
            return Mathf.Sqrt(horizontal * horizontal + vertical * vertical);
        }
    }
}
