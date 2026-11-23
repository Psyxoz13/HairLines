using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairPropagationOperator : HairGroomOperator
    {
        public override string DisplayName => "Propagation";

        public override string Description => "Grows new strands on existing strands: fuzz on hair, brow and " +
            "lash hairs on a guide lying along the skin.";

        public override bool IsGlobal => true;

        public int Count { get => _count; set => _count = Mathf.Clamp(value, 0, MaxCount); }
        public float RangeStart { get => _rangeStart; set => _rangeStart = Mathf.Clamp01(value); }
        public float RangeEnd { get => _rangeEnd; set => _rangeEnd = Mathf.Clamp01(value); }

        public AnimationCurve Length
        {
            get => _length = _length != null && _length.length > 0 ? _length : DefaultLength();
            set => _length = value != null && value.length > 0
                ? new AnimationCurve(value.keys) { preWrapMode = value.preWrapMode, postWrapMode = value.postWrapMode }
                : DefaultLength();
        }

        public float Angle { get => _angle; set => _angle = Mathf.Clamp(value, 0f, 180f); }
        public int Sides { get => _sides; set => _sides = Mathf.Clamp(value, 1, 16); }
        public float Randomness { get => _randomness; set => _randomness = Mathf.Clamp01(value); }
        internal int CountChannel => _countChannel;
        internal int LengthChannel => _lengthChannel;
        internal int AngleChannel => _angleChannel;
        internal int RandomnessChannel => _randomnessChannel;

        public const int MaxCount = 64;

        [Tooltip("New strands grown on every strand this operator reaches. The mask scales the count, " +
                 "so a grey mask grows fewer, not shorter.")]
        [SerializeField, Range(0, MaxCount)]
        private int _count = 8;

        [SerializeField, HideInInspector]
        private int _countChannel;

        [Tooltip("Where along the parent the new strands start, as a fraction of it from the root.")]
        [SerializeField, Range(0f, 1f)]
        private float _rangeStart = 0.1f;

        [Tooltip("Where along the parent the new strands stop, as a fraction of it from the root.")]
        [SerializeField, Range(0f, 1f)]
        private float _rangeEnd = 0.9f;

        [Tooltip("Length of a new strand in metres, by where it sits on the parent: x is the position " +
                 "along the parent (0 root, 1 tip).")]
        [SerializeField]
        private AnimationCurve _length = DefaultLength();

        [SerializeField, HideInInspector]
        private int _lengthChannel;

        [Tooltip("Angle between a new strand and its parent, in degrees. 0 runs along the parent, " +
                 "90 stands straight out of it.")]
        [SerializeField, Range(0f, 180f)]
        private float _angle = 35f;

        [SerializeField, HideInInspector]
        private int _angleChannel;

        [Tooltip("Directions around the parent the new strands take in turn. 2 alternates left and " +
                 "right, flat against the scalp the parent grows from - the layout of a brow or a feather.")]
        [SerializeField, Range(1, 16)]
        private int _sides = 2;

        [Tooltip("Random spread of placement, direction and length, 0 to 1.")]
        [SerializeField, Range(0f, 1f)]
        private float _randomness = 0.3f;

        [SerializeField, HideInInspector]
        private int _randomnessChannel;

        internal float[] BuildLength(int samples)
        {
            AnimationCurve length = Length;
            var values = new float[Mathf.Max(2, samples)];
            float last = values.Length - 1;

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Mathf.Max(0f, length.Evaluate(i / last));
            }

            return values;
        }

        private static AnimationCurve DefaultLength()
        {
            return AnimationCurve.Constant(0f, 1f, 0.01f);
        }
    }
}
