using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairFrizzOperator : HairGroomOperator
    {
        public override string DisplayName => "Frizz";

        public override string Description => "Pushes every strand sideways on its own, so neighbours go " +
            "different ways: fly-aways and fuzz.";

        public float Amount { get => _amount; set => _amount = Mathf.Max(0f, value); }
        public float Scale { get => _scale; set => _scale = Mathf.Clamp(value, 0.05f, 1f); }
        public float RootHold { get => _rootHold; set => _rootHold = Mathf.Clamp01(value); }
        internal int AmountChannel => _amountChannel;
        internal int ScaleChannel => _scaleChannel;
        internal int RootHoldChannel => _rootHoldChannel;

        [Tooltip("Sideways displacement at the tip, in metres.")]
        [SerializeField, Min(0f)]
        private float _amount = 0.01f;

        [SerializeField, HideInInspector]
        private int _amountChannel;

        [Tooltip("Fraction of the strand one wobble spans. Small values make it fine and busy.")]
        [SerializeField, Range(0.05f, 1f)]
        private float _scale = 0.35f;

        [SerializeField, HideInInspector]
        private int _scaleChannel;

        [Tooltip("How much of the strand near the root stays undisturbed.")]
        [SerializeField, Range(0f, 1f)]
        private float _rootHold = 0.15f;

        [SerializeField, HideInInspector]
        private int _rootHoldChannel;
    }
}
