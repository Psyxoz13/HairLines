using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairStrandDataOperator : HairGroomOperator
    {
        public override string DisplayName => "Strand Data";

        public override string Description => "Writes its own mask into a channel. Any value below can pick " +
            "that channel from the menu next to it and is then scaled by this mask: white keeps the value, " +
            "black takes it to zero.";

        public int Channel { get => _channel; set => _channel = Mathf.Clamp(value, 1, ChannelCount); }

        public const int ChannelCount = 8;

        [Tooltip("Channel this operator's mask is written to. Any parameter below can pick the same " +
                 "channel from the menu next to it and is then scaled by this mask.")]
        [SerializeField, Range(1, ChannelCount)]
        private int _channel = 1;
    }
}
