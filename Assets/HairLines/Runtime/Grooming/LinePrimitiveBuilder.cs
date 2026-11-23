using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace HairLines
{
    public class LinePrimitiveBuilder
    {
        public int VertexCount => _positions.Count;
        public int StrandCount => _strands.Count;

        private readonly List<Vector3> _positions = new List<Vector3>();
        private readonly List<Vector4> _colors = new List<Vector4>();
        private readonly List<int> _indices = new List<int>();
        private readonly List<float> _opacity = new List<float>();
        private readonly List<Vector2Int> _strands = new List<Vector2Int>();
        private readonly List<uint> _vertexGroups = new List<uint>();

        private int _strandStart = -1;
        private uint _group;

        private static readonly ProfilerMarker _buildMarker =
            new ProfilerMarker("HairLines.Grooming.BuildModel");

        public void Reserve(int vertices, int strands)
        {
            _positions.Capacity = Mathf.Max(_positions.Capacity, vertices);
            _colors.Capacity = Mathf.Max(_colors.Capacity, vertices);
            _indices.Capacity = Mathf.Max(_indices.Capacity, vertices * 2);
            _opacity.Capacity = Mathf.Max(_opacity.Capacity, vertices);
            _strands.Capacity = Mathf.Max(_strands.Capacity, strands);
            _vertexGroups.Capacity = Mathf.Max(_vertexGroups.Capacity, vertices);
        }

        public void BeginStrand(int group = 0)
        {
            _strandStart = _positions.Count;
            _group = (uint)Mathf.Max(0, group);
        }

        public void AddVertex(Vector3 position, Vector4 color)
        {
            _positions.Add(position);
            _colors.Add(color);
            _vertexGroups.Add(_group);
        }

        public void EndStrand(float rootOpacity = 1f, float tipOpacity = 1f)
        {
            int count = _positions.Count - _strandStart;

            if (count < 2)
            {
                if (count > 0)
                {
                    _positions.RemoveRange(_strandStart, count);
                    _colors.RemoveRange(_strandStart, count);
                    _vertexGroups.RemoveRange(_strandStart, count);
                }

                return;
            }

            _strands.Add(new Vector2Int(_strandStart, count));
            int edges = count - 1;

            for (int i = 0; i < edges; i++)
            {
                _indices.Add(_strandStart + i);
                _indices.Add(_strandStart + i + 1);

                float alongStrand = edges > 1 ? i / (float)(edges - 1) : 0f;
                _opacity.Add(Mathf.Lerp(rootOpacity, tipOpacity, alongStrand));
            }
        }

        public LinePrimitiveModel Build()
        {
            using ProfilerMarker.AutoScope scope = _buildMarker.Auto();
            return new LinePrimitiveModel(_positions.ToArray(), _colors.ToArray(), _indices.ToArray(),
                                          _opacity.ToArray(), _strands.ToArray(), _vertexGroups.ToArray());
        }
    }
}
