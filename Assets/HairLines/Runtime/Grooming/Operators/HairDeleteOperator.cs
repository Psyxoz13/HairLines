using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairDeleteOperator : HairGroomOperator
    {
        public override string DisplayName => "Delete";

        public override string Description => "Removes whole strands where the mask is white, together with " +
            "anything Propagation grew on them. It never shortens a strand.";

        public float Fraction { get => _fraction; set => _fraction = Mathf.Clamp01(value); }
        internal int FractionChannel => _fractionChannel;

        [Tooltip("Share of strands removed where the mask is white. The mask scales it, so painting " +
                 "grey thins the hair instead of shortening it. Whole strands go, with whatever " +
                 "grew on them. Strands rooted on the scalp are removed in the reverse of their " +
                 "blue-noise order, so what stays is still evenly spread.")]
        [SerializeField, Range(0f, 1f)]
        private float _fraction = 1f;

        [SerializeField, HideInInspector]
        private int _fractionChannel;
    }
}
