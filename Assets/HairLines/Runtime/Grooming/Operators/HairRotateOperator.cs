using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairRotateOperator : HairGroomOperator
    {
        public override string DisplayName => "Rotate";

        public override string Description => "Turns the strand's frame about its own stem and twists it " +
            "towards the tip. On its own it changes nothing - Curl, Braid and Propagation below build on " +
            "that frame.";

        public float Angle { get => _angle; set => _angle = value; }
        public float Chaos { get => _chaos; set => _chaos = Mathf.Clamp(value, 0f, 180f); }
        public float Twist { get => _twist; set => _twist = value; }
        internal int AngleChannel => _angleChannel;
        internal int ChaosChannel => _chaosChannel;
        internal int TwistChannel => _twistChannel;

        [Tooltip("Turn of the strand frame about its stem at the root, in degrees, measured from the " +
                 "scalp tangent. Operators that work around the stem - Curl, Braid, Propagation - " +
                 "start from this frame.")]
        [SerializeField]
        private float _angle;

        [SerializeField, HideInInspector]
        private int _angleChannel;

        [Tooltip("Random spread of the root angle between strands, in degrees either way.")]
        [SerializeField, Range(0f, 180f)]
        private float _chaos;

        [SerializeField, HideInInspector]
        private int _chaosChannel;

        [Tooltip("Extra turn of the frame at the tip relative to the root, in degrees. The frame turns " +
                 "evenly in between, so a Curl or Braid below twists along the strand.")]
        [SerializeField]
        private float _twist;

        [SerializeField, HideInInspector]
        private int _twistChannel;
    }
}
