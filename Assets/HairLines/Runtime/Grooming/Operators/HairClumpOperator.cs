using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairClumpOperator : HairGroomOperator
    {
        public override string DisplayName => "Clump";

        public override string Description => "Gathers strands into locks around a centre strand. The Width " +
            "curve is the lock radius in metres along the strand, and its value at the root is also the " +
            "catchment radius: strands rooted further away stay loose. A second Clump below splits every " +
            "lock into smaller ones.";

        public override bool IsGlobal => true;

        public AnimationCurve Width
        {
            get
            {
                EnsureWidth();
                return _width;
            }
            set => _width = CloneWidth(value);
        }

        public float WidthScale { get => _widthScale; set => _widthScale = Mathf.Max(0f, value); }
        public float Stray { get => _stray; set => _stray = Mathf.Clamp01(value); }
        internal int WidthScaleChannel => _widthScaleChannel;
        internal int StrayChannel => _strayChannel;

        [Tooltip("Clump radius along the strand: x is the position from root (0) to tip (1), " +
                 "y is the radius in metres before Width Scale. The value at x = 0 is also the " +
                 "catchment radius - strands rooted further than that from a clump centre stay loose, " +
                 "and a second Clump in the stack splits each clump with its own smaller radius.")]
        [SerializeField]
        private AnimationCurve _width = DefaultWidth();

        [Tooltip("Multiplies the whole width curve. 1 keeps the curve in metres.")]
        [SerializeField, Min(0f)]
        private float _widthScale = 1f;

        [SerializeField, HideInInspector]
        private int _widthScaleChannel;

        [Tooltip("Share of strands that keep their own shape instead of joining a clump, 0 to 1.")]
        [SerializeField, Range(0f, 1f)]
        private float _stray;

        [SerializeField, HideInInspector]
        private int _strayChannel;

        internal float[] BuildWidths(int vertexCount)
        {
            EnsureWidth();
            var widths = new float[Mathf.Max(1, vertexCount)];
            float last = Mathf.Max(1, vertexCount - 1);

            for (int i = 0; i < widths.Length; i++)
            {
                widths[i] = Mathf.Max(0f, _width.Evaluate(i / last)) * _widthScale;
            }

            return widths;
        }

        private void EnsureWidth()
        {
            if (_width == null || _width.length == 0)
            {
                _width = DefaultWidth();
            }
        }

        private static AnimationCurve DefaultWidth()
        {
            return new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.5f, 0.025f),
                                      new Keyframe(1f, 0.006f));
        }

        private static AnimationCurve CloneWidth(AnimationCurve source)
        {
            if (source == null || source.length == 0)
            {
                return DefaultWidth();
            }

            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }
    }
}
