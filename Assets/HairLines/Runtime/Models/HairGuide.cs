using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairGuide
    {
        public Vector2 Uv => _uv;
        public int PointCount => _points != null ? _points.Length : 0;
        public HairRootBinding Binding => new HairRootBinding(_triangleIndex, _barycentric);
        public bool HasFreeze => _freeze != null && _freeze.Length > 0;

        [SerializeField]
        private Vector3[] _points = Array.Empty<Vector3>();

        [SerializeField]
        private float[] _freeze = Array.Empty<float>();

        [SerializeField]
        private Vector2 _barycentric;

        [SerializeField]
        private Vector2 _uv;

        [SerializeField]
        private int _triangleIndex;

        public HairGuide()
        {
        }

        public HairGuide(in HairRootBinding binding, Vector2 uv, Vector3[] localPoints)
        {
            _triangleIndex = binding.TriangleIndex;
            _barycentric = binding.Barycentric;
            _uv = uv;
            _points = localPoints;
        }

        public Vector3 PointAt(int index)
        {
            return _points[Mathf.Clamp(index, 0, _points.Length - 1)];
        }

        public void SetPoint(int index, Vector3 local)
        {
            if (index > 0 && index < _points.Length)
            {
                _points[index] = local;
            }
        }

        public void SetPoints(Vector3[] localPoints)
        {
            if (localPoints == null || localPoints.Length < 2)
            {
                return;
            }

            if (HasFreeze && localPoints.Length != _freeze.Length)
            {
                _freeze = StretchFreeze(_freeze, localPoints.Length);
            }

            _points = localPoints;
        }

        public float FreezeAt(int index)
        {
            return HasFreeze && index >= 0 && index < _freeze.Length ? _freeze[index] : 0f;
        }

        public void SetFreeze(int index, float amount)
        {
            if (index < 0 || index >= PointCount)
            {
                return;
            }

            if (!HasFreeze || _freeze.Length != PointCount)
            {
                _freeze = new float[PointCount];
            }

            _freeze[index] = Mathf.Clamp01(amount);
        }

        public void ClearFreeze()
        {
            _freeze = Array.Empty<float>();
        }

        public void Resample(float[] segmentFractions)
        {
            int count = PointCount;
            int target = segmentFractions != null ? segmentFractions.Length + 1 : 0;
            if (count < 2 || target < 2)
            {
                return;
            }

            var travelled = new float[count];
            for (int k = 1; k < count; k++)
            {
                travelled[k] = travelled[k - 1] + Vector3.Distance(_points[k - 1], _points[k]);
            }

            float total = travelled[count - 1];
            if (total < 1e-8f)
            {
                return;
            }

            var points = new Vector3[target];
            var freeze = HasFreeze ? new float[target] : null;
            float wanted = 0f;
            int segment = 1;

            for (int k = 0; k < target; k++)
            {
                if (k > 0)
                {
                    wanted = k == target - 1 ? total : wanted + segmentFractions[k - 1] * total;
                }

                while (segment < count - 1 && travelled[segment] < wanted)
                {
                    segment++;
                }

                float start = travelled[segment - 1];
                float span = travelled[segment] - start;
                float u = span > 1e-8f ? Mathf.Clamp01((wanted - start) / span) : 0f;
                points[k] = CatmullRom(segment - 1, u);

                if (freeze != null)
                {
                    freeze[k] = Mathf.Lerp(FreezeAt(segment - 1), FreezeAt(segment), u);
                }
            }

            points[0] = _points[0];
            _points = points;
            if (freeze != null)
            {
                _freeze = freeze;
            }
        }

        public Vector3 SampleAt(float alongStrand)
        {
            if (_points.Length == 0)
            {
                return Vector3.zero;
            }

            if (_points.Length == 1)
            {
                return _points[0];
            }

            float scaled = Mathf.Clamp01(alongStrand) * (_points.Length - 1);
            int lower = Mathf.Min((int)scaled, _points.Length - 2);
            return Vector3.Lerp(_points[lower], _points[lower + 1], scaled - lower);
        }

        public HairGuide Clone()
        {
            return new HairGuide(Binding, _uv,
                _points != null ? (Vector3[])_points.Clone() : Array.Empty<Vector3>())
            {
                _freeze = HasFreeze ? (float[])_freeze.Clone() : Array.Empty<float>(),
            };
        }

        private Vector3 CatmullRom(int segment, float u)
        {
            int count = _points.Length;
            Vector3 p1 = _points[segment];
            Vector3 p2 = _points[segment + 1];
            Vector3 p0 = segment > 0 ? _points[segment - 1] : 2f * p1 - p2;
            Vector3 p3 = segment + 2 < count ? _points[segment + 2] : 2f * p2 - p1;
            return HairCatmullRom.Centripetal(p0, p1, p2, p3, u);
        }

        private static float[] StretchFreeze(float[] freeze, int count)
        {
            var stretched = new float[count];
            for (int k = 0; k < count; k++)
            {
                float scaled = count > 1 ? k / (float)(count - 1) * (freeze.Length - 1) : 0f;
                int lower = Mathf.Min((int)scaled, freeze.Length - 1);
                int upper = Mathf.Min(lower + 1, freeze.Length - 1);
                stretched[k] = Mathf.Lerp(freeze[lower], freeze[upper], scaled - lower);
            }

            return stretched;
        }
    }
}
