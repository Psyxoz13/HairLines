using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairGroundOperator : HairGroomOperator
    {
        public override string DisplayName => "Ground";

        public override string Description => "Moves the strand's root onto the scalp mesh. Strands grown on " +
            "the scalp are already there, so this matters for the ones Propagation grew on other strands - " +
            "brows and lashes.";

        public AnimationCurve Blend
        {
            get => _blend = _blend != null && _blend.length > 0 ? _blend : DefaultBlend();
            set => _blend = value != null && value.length > 0
                ? new AnimationCurve(value.keys) { preWrapMode = value.preWrapMode, postWrapMode = value.postWrapMode }
                : DefaultBlend();
        }

        [Tooltip("How much of the move onto the scalp each point takes, from root (0) to tip (1). " +
                 "1 everywhere carries the whole strand along with its root; falling to 0 at the tip " +
                 "pins the tip where it was and bends the strand instead.")]
        [SerializeField]
        private AnimationCurve _blend = DefaultBlend();

        internal float[] BuildBlend(int vertexCount)
        {
            AnimationCurve blend = Blend;
            var values = new float[Mathf.Max(1, vertexCount)];
            float last = Mathf.Max(1, vertexCount - 1);

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Mathf.Clamp01(blend.Evaluate(i / last));
            }

            return values;
        }

        private static AnimationCurve DefaultBlend()
        {
            return AnimationCurve.Constant(0f, 1f, 1f);
        }
    }
}
