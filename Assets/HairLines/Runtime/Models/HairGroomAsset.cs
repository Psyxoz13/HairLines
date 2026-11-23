using UnityEngine;

namespace HairLines
{
    public class HairGroomAsset : ScriptableObject
    {
        public int StrandCount => _strandCount;
        public int VertexCount => _vertexCount;
        public Bounds LocalBounds => _localBounds;
        public long ByteCount => _blob != null ? _blob.LongLength : 0;

        [SerializeField, HideInInspector]
        private byte[] _blob;

        [SerializeField]
        private int _strandCount;

        [SerializeField]
        private int _vertexCount;

        [SerializeField]
        private Bounds _localBounds;

        public void Fill(byte[] blob, int strandCount, int vertexCount, Bounds localBounds)
        {
            _blob = blob;
            _strandCount = strandCount;
            _vertexCount = vertexCount;
            _localBounds = localBounds;
        }

        public LinePrimitiveModel CreateModel()
        {
            return _blob == null || _blob.Length == 0 ? null : HairGroomBlob.Read(_blob);
        }
    }
}
