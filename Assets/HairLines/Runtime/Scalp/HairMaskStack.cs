using System.Collections.Generic;
using UnityEngine;

namespace HairLines
{
    public class HairMaskStack : IHairDensityMask
    {
        internal IHairDensityMask Own => _own;
        internal IReadOnlyList<IHairDensityMask> ClaimedAbove => _claimedAbove;

        private readonly IHairDensityMask _own;
        private readonly List<IHairDensityMask> _claimedAbove = new List<IHairDensityMask>();

        public HairMaskStack(IHairDensityMask own, IEnumerable<IHairDensityMask> claimedAbove)
        {
            _own = own;

            if (claimedAbove == null)
            {
                return;
            }

            foreach (IHairDensityMask mask in claimedAbove)
            {
                _claimedAbove.Add(mask);
            }
        }

        public float Evaluate(Vector2 uv)
        {
            float free = 1f;

            for (int i = 0; i < _claimedAbove.Count; i++)
            {
                float claimed = _claimedAbove[i] == null ? 1f : Mathf.Clamp01(_claimedAbove[i].Evaluate(uv));
                free *= 1f - claimed;

                if (free <= 0f)
                {
                    return 0f;
                }
            }

            float own = _own == null ? 1f : Mathf.Clamp01(_own.Evaluate(uv));
            return free * own;
        }
    }
}
