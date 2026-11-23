using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace HairLines
{
    [Serializable]
    public class HairImportSettings
    {
        public float Scale { get => _scale; set => _scale = Mathf.Max(0.0001f, value); }
        public bool FlipZ { get => _flipZ; set => _flipZ = value; }
        public Vector3 Offset { get => _offset; set => _offset = value; }
        public int StrandStride { get => _strandStride; set => _strandStride = Mathf.Max(1, value); }
        public Color DefaultColor { get => _defaultColor; set => _defaultColor = value; }
        public float TipOpacity { get => _tipOpacity; set => _tipOpacity = Mathf.Clamp01(value); }

        [Tooltip("Uniform scale applied to the imported points, so a dataset in centimetres lands in metres.")]
        [SerializeField, FormerlySerializedAs("scale"), Min(0.0001f)]
        private float _scale = 1f;

        [Tooltip("Mirror Z on import. Most strand datasets are right-handed, Unity is left-handed.")]
        [SerializeField, FormerlySerializedAs("flipZ")]
        private bool _flipZ = true;

        [Tooltip("Offset applied after scaling, in metres.")]
        [SerializeField, FormerlySerializedAs("offset")]
        private Vector3 _offset = Vector3.zero;

        [Tooltip("Keep only every Nth strand. Useful to preview huge files.")]
        [SerializeField, FormerlySerializedAs("strandStride"), Min(1)]
        private int _strandStride = 1;

        [Tooltip("Per-vertex modulation used when the file carries no colour. White lets the material decide.")]
        [SerializeField, FormerlySerializedAs("defaultColor")]
        private Color _defaultColor = Color.white;

        [Tooltip("Per-edge existence probability at the tip.")]
        [SerializeField, FormerlySerializedAs("tipOpacity"), Range(0f, 1f)]
        private float _tipOpacity = 1f;

        public Vector3 Apply(Vector3 point)
        {
            if (_flipZ)
            {
                point.z = -point.z;
            }

            return point * _scale + _offset;
        }
    }
}
