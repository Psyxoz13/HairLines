using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairGravityOperator : HairGroomOperator
    {
        public override string DisplayName => "Gravity";

        public override string Description => "Turns segments towards straight down while the strand keeps " +
            "its length.";

        public float Amount { get => _amount; set => _amount = Mathf.Clamp01(value); }
        public float RootHold { get => _rootHold; set => _rootHold = Mathf.Clamp01(value); }
        internal int AmountChannel => _amountChannel;
        internal int RootHoldChannel => _rootHoldChannel;

        [Tooltip("How far each segment turns towards straight down, 0 to 1. Past about 0.2 the strand " +
                 "goes vertical and loses whatever shape the clump gave it.")]
        [SerializeField, Range(0f, 1f)]
        private float _amount = 0.1f;

        [SerializeField, HideInInspector]
        private int _amountChannel;

        [Tooltip("Share of the strand measured from the root that keeps its direction, 0 to 1.")]
        [SerializeField, Range(0f, 1f)]
        private float _rootHold = 0.2f;

        [SerializeField, HideInInspector]
        private int _rootHoldChannel;
    }
}
