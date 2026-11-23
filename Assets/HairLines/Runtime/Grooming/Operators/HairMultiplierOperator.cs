using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairMultiplierOperator : HairGroomOperator
    {
        public override string DisplayName => "Multiplier";

        public override string Description => "Grows extra strands beside every strand, spread apart at root " +
            "and tip. Always the last operator in the stack.";

        public override int Priority => int.MaxValue;

        public HairMultiplierMode Mode { get => _mode; set => _mode = value; }
        public int Copies { get => _copies; set => _copies = Mathf.Clamp(value, 0, 8); }
        public float SpreadRoot { get => _spreadRoot; set => _spreadRoot = Mathf.Max(0f, value); }
        public float SpreadTip { get => _spreadTip; set => _spreadTip = Mathf.Max(0f, value); }
        internal int SpreadRootChannel => _spreadRootChannel;
        internal int SpreadTipChannel => _spreadTipChannel;

        [Tooltip("Unique grows every copy as its own strand - its own length, curl and frizz. " +
                 "Instanced draws copies as GPU instances of the original: one vector per copy " +
                 "instead of a whole strand, and the solver only sees the originals, but copies " +
                 "share the length and the curl of the strand they came from.")]
        [SerializeField]
        private HairMultiplierMode _mode = HairMultiplierMode.Unique;

        [Tooltip("Extra strands grown next to every original one. 2 means three strands in total.")]
        [SerializeField, Range(0, 8)]
        private int _copies = 2;

        [Tooltip("How far a copy sits from its original at the root, in metres. It slides along the " +
                 "scalp surface, so the root stays on the mesh.")]
        [SerializeField, Min(0f)]
        private float _spreadRoot = 0.002f;

        [SerializeField, HideInInspector]
        private int _spreadRootChannel;

        [Tooltip("How far a copy sits from its original at the tip, in metres.")]
        [SerializeField, Min(0f)]
        private float _spreadTip = 0.012f;

        [SerializeField, HideInInspector]
        private int _spreadTipChannel;
    }
}
