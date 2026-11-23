using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairNoiseOperator : HairGroomOperator
    {
        public override string DisplayName => "Noise";

        public override string Description => "Bends strands with one wave sampled in world space, so " +
            "neighbours inside a wave move together.";

        public float Amount { get => _amount; set => _amount = Mathf.Max(0f, value); }
        public float Scale { get => _scale; set => _scale = Mathf.Max(0.001f, value); }
        public float RootHold { get => _rootHold; set => _rootHold = Mathf.Clamp01(value); }
        internal int AmountChannel => _amountChannel;
        internal int ScaleChannel => _scaleChannel;
        internal int RootHoldChannel => _rootHoldChannel;

        [Tooltip("Size of the wave, in metres.")]
        [SerializeField, Min(0f)]
        private float _amount = 0.02f;

        [SerializeField, HideInInspector]
        private int _amountChannel;

        [Tooltip("Distance between wave crests, in metres. Neighbouring strands inside one wave move " +
                 "together, which is what separates this from Frizz.")]
        [SerializeField, Min(0.001f)]
        private float _scale = 0.12f;

        [SerializeField, HideInInspector]
        private int _scaleChannel;

        [Tooltip("Share of the strand measured from the root that stays put, 0 to 1.")]
        [SerializeField, Range(0f, 1f)]
        private float _rootHold = 0.15f;

        [SerializeField, HideInInspector]
        private int _rootHoldChannel;
    }
}
