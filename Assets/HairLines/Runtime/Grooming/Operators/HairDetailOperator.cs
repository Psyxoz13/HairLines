using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairDetailOperator : HairGroomOperator
    {
        public override string DisplayName => "Detail";

        public override string Description => "Re-samples every strand to a new number of segments and can " +
            "smooth it first. Everything below works at this resolution.";

        public int Segments { get => _segments; set => _segments = Mathf.Clamp(value, 1, MaxSegments); }
        public float Smoothing { get => _smoothing; set => _smoothing = Mathf.Clamp01(value); }
        public int Iterations { get => _iterations; set => _iterations = Mathf.Clamp(value, 0, 32); }
        internal int SmoothingChannel => _smoothingChannel;

        public const int MaxSegments = 128;

        [Tooltip("Segments every strand has after this operator. The strand is re-sampled along a " +
                 "centripetal Catmull-Rom curve through its points, spaced by the group's segment " +
                 "length distribution. Every operator below works at this resolution.")]
        [SerializeField, Range(1, MaxSegments)]
        private int _segments = 24;

        [Tooltip("How far each point moves towards the middle of its neighbours in one smoothing step, " +
                 "before re-sampling. The root stays put and the strand keeps its length.")]
        [SerializeField, Range(0f, 1f)]
        private float _smoothing;

        [SerializeField, HideInInspector]
        private int _smoothingChannel;

        [Tooltip("Smoothing steps. More steps smooth over a longer stretch of the strand.")]
        [SerializeField, Range(0, 32)]
        private int _iterations = 4;
    }
}
