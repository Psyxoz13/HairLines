using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairCurlOperator : HairGroomOperator
    {
        public override string DisplayName => "Curl";

        public override string Description => "Winds every strand into a helix around the path it already " +
            "has.";

        public float Radius { get => _radius; set => _radius = Mathf.Max(0f, value); }
        public float Frequency { get => _frequency; set => _frequency = Mathf.Max(0f, value); }
        internal int RadiusChannel => _radiusChannel;
        internal int FrequencyChannel => _frequencyChannel;

        [Tooltip("Radius of the helical curl, in metres.")]
        [SerializeField, Min(0f)]
        private float _radius = 0.004f;

        [SerializeField, HideInInspector]
        private int _radiusChannel;

        [Tooltip("Full curl turns over the length of a strand.")]
        [SerializeField, Min(0f)]
        private float _frequency = 2.5f;

        [SerializeField, HideInInspector]
        private int _frequencyChannel;

        private const float RampSpan = 3f;
    }
}
