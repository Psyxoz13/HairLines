using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairCutOperator : HairGroomOperator
    {
        public override string DisplayName => "Cut";

        public override string Description => "Shortens strands along their own curve, with a random spread " +
            "between them, so the tips end at different heights.";

        public float Keep { get => _keep; set => _keep = Mathf.Clamp(value, 0.02f, 1f); }
        public float Ragged { get => _ragged; set => _ragged = Mathf.Clamp01(value); }
        internal int KeepChannel => _keepChannel;
        internal int RaggedChannel => _raggedChannel;

        [Tooltip("Fraction of the grown length that survives the cut.")]
        [SerializeField, Range(0.02f, 1f)]
        private float _keep = 0.6f;

        [SerializeField, HideInInspector]
        private int _keepChannel;

        [Tooltip("Random spread of the cut between strands, as a fraction of the kept length.")]
        [SerializeField, Range(0f, 1f)]
        private float _ragged = 0.1f;

        [SerializeField, HideInInspector]
        private int _raggedChannel;
    }
}
