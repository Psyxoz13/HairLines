using UnityEngine;

namespace HairLines
{
    public struct HairRandom
    {
        public float Value => (NextUInt() & 0xFFFFFF) / 16777216f;

        private uint _state;

        public HairRandom(int seed)
        {
            _state = (uint)seed | 1u;
        }

        public uint NextUInt()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state;
        }

        public float Range(float min, float max)
        {
            return min + (max - min) * Value;
        }

        public Vector3 OnUnitSphere()
        {
            float z = Range(-1f, 1f);
            float angle = Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(radius * Mathf.Cos(angle), z, radius * Mathf.Sin(angle));
        }

        public static float HashToUnitFloat(int value)
        {
            uint hash = (uint)value * 2654435761u;
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            return (hash & 0xFFFFFF) / 16777216f;
        }

        public static float RadicalInverse(int radix, int index)
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
    }
}
