using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairBraidOperator : HairGroomOperator
    {
        public enum StemCount
        {
            Three = 3,
            Five = 5,
            Seven = 7,
        }

        public override string DisplayName => "Braid";

        public override string Description => "Plaits the strands of one lock into a braid of three, five or " +
            "seven stems. Put a Detail above it: a braid needs points to plait with.";

        public override bool IsGlobal => true;

        public StemCount Stems { get => _stems; set => _stems = value; }

        public AnimationCurve Width
        {
            get => _width = ValidCurve(_width, DefaultWidth);
            set => _width = CloneCurve(value, DefaultWidth);
        }

        public AnimationCurve Radius
        {
            get => _radius = ValidCurve(_radius, DefaultRadius);
            set => _radius = CloneCurve(value, DefaultRadius);
        }

        public float Frequency { get => _frequency; set => _frequency = Mathf.Max(0f, value); }
        public float Flatness { get => _flatness; set => _flatness = Mathf.Clamp(value, 0.05f, 2f); }
        internal int WidthChannel => _widthChannel;
        internal int RadiusChannel => _radiusChannel;
        internal int FrequencyChannel => _frequencyChannel;
        internal int FlatnessChannel => _flatnessChannel;

        [Tooltip("How many stems the braid is plaited from. Three is a pigtail.")]
        [SerializeField]
        private StemCount _stems = StemCount.Three;

        [Tooltip("Radius of one stem along the strand: x is the position from root (0) to tip (1), " +
                 "y is the radius in metres. The value at x = 0 is also the catchment radius, exactly " +
                 "as in Clump: strands rooted further than that from the braid centre stay loose.")]
        [SerializeField]
        private AnimationCurve _width = DefaultWidth();

        [SerializeField, HideInInspector]
        private int _widthChannel;

        [Tooltip("How far the stems swing from the braid centre, in metres, along the strand. " +
                 "0 is no braid, so the curve also sets where the braid starts and where it loosens.")]
        [SerializeField]
        private AnimationCurve _radius = DefaultRadius();

        [SerializeField, HideInInspector]
        private int _radiusChannel;

        [Tooltip("Full plait periods over the length of a strand. Each period every stem crosses " +
                 "over its neighbours once on each side.")]
        [SerializeField, Min(0f)]
        private float _frequency = 6f;

        [SerializeField, HideInInspector]
        private int _frequencyChannel;

        [Tooltip("Depth of the braid against its width. 1 keeps the lobes round, lower flattens the braid.")]
        [SerializeField, Range(0.05f, 2f)]
        private float _flatness = 1f;

        [SerializeField, HideInInspector]
        private int _flatnessChannel;

        internal float[] BuildWidths(int vertexCount)
        {
            return Sample(Width, vertexCount);
        }

        internal float[] BuildRadii(int vertexCount)
        {
            return Sample(Radius, vertexCount);
        }

        private static float[] Sample(AnimationCurve curve, int vertexCount)
        {
            var values = new float[Mathf.Max(1, vertexCount)];
            float last = Mathf.Max(1, vertexCount - 1);

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Mathf.Max(0f, curve.Evaluate(i / last));
            }

            return values;
        }

        private static AnimationCurve DefaultWidth()
        {
            return new AnimationCurve(new Keyframe(0f, 0.06f), new Keyframe(0.25f, 0.007f),
                                      new Keyframe(1f, 0.005f));
        }

        private static AnimationCurve DefaultRadius()
        {
            return new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 0.012f),
                                      new Keyframe(0.9f, 0.01f), new Keyframe(1f, 0.004f));
        }

        private static AnimationCurve ValidCurve(AnimationCurve curve, Func<AnimationCurve> fallback)
        {
            return curve != null && curve.length > 0 ? curve : fallback();
        }

        private static AnimationCurve CloneCurve(AnimationCurve source, Func<AnimationCurve> fallback)
        {
            if (source == null || source.length == 0)
            {
                return fallback();
            }

            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }
    }
}
