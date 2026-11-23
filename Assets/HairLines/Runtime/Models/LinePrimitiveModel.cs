using System;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace HairLines
{
    public class LinePrimitiveModel : IDisposable
    {
        public Vector3[] Positions { get; }
        public Vector4[] Colors => _colorData ??= SynthesizeColors();
        public int[] Indices => _indexData ??= SynthesizeIndices();
        public float[] EdgeOpacity => _edgeOpacityData ??= SynthesizeEdgeOpacity();
        public Vector2Int[] Strands { get; }
        public uint[] VertexGroups { get; }
        public Vector4[] StrandDebug { get; private set; }
        public float LongestStrand { get; private set; }

        public Bounds LocalBounds { get; set; }

        public int StrandCount => Strands?.Length ?? 0;
        public int VertexCount => Positions?.Length ?? 0;
        public int EdgeCount => _strandEdgePrefix.Length > 0 ? _strandEdgePrefix[_strandEdgePrefix.Length - 1] : 0;
        internal int PatchedRanges { get; private set; } = -1;
        public int DrawRangeCount => _drawRanges.Length;
        public bool IsEmpty => VertexCount == 0 || EdgeCount == 0;

        public int EdgeCountForStrands(int strandCount)
        {
            if (Strands == null || strandCount <= 0)
            {
                return 0;
            }

            int count = Mathf.Min(strandCount, Strands.Length);
            int edges = _strandEdgePrefix[count];

            return Mathf.Min(edges, EdgeCount);
        }

        public int GroupForDrawRange(int drawRange)
        {
            return _drawRanges[Mathf.Clamp(drawRange, 0, _drawRanges.Length - 1)].Group;
        }

        public int EdgeOffsetForDrawRange(int drawRange)
        {
            return _drawRanges[Mathf.Clamp(drawRange, 0, _drawRanges.Length - 1)].EdgeOffset;
        }

        public int InstanceCountForDrawRange(int drawRange)
        {
            return 1 + CopySetForDrawRange(drawRange).Copies;
        }

        public int DrawnStrandCount
        {
            get
            {
                if (_copySets == null)
                {
                    return StrandCount;
                }

                int drawn = 0;
                for (int range = 0; range < _drawRanges.Length; range++)
                {
                    drawn += _drawRanges[range].StrandCount * InstanceCountForDrawRange(range);
                }

                return drawn;
            }
        }

        public long GeometryBytes
        {
            get
            {
                long bytes = (long)VertexCount * (PositionStride + sizeof(uint) + OpacityStride) +
                             (long)EdgeCount * (IndexStride * 2) +
                             (long)(CopyOffsets?.Length ?? 0) * PositionStride;

                if (HasVertexColor)
                {
                    bytes += (long)VertexCount * ColorStride;
                }

                if (_restPositions != null && _restPositions.IsValid())
                {
                    bytes += (long)VertexCount * PositionStride;
                }

                return bytes;
            }
        }

        public Vector4 CopyParamsForDrawRange(int drawRange)
        {
            if (_drawRanges.Length == 0)
            {
                return Vector4.zero;
            }

            DrawRange range = _drawRanges[Mathf.Clamp(drawRange, 0, _drawRanges.Length - 1)];
            Vector2Int first = Strands[range.StrandOffset];
            return new Vector4(range.CopyOffsetStart, first.x, first.y,
                               CopySetForDrawRange(drawRange).Copies);
        }

        public Vector4 CopySpreadForDrawRange(int drawRange)
        {
            CopySet set = CopySetForDrawRange(drawRange);
            return new Vector4(set.SpreadRoot, set.SpreadTip, set.ColorVariance, 0f);
        }

        public Vector2Int VertexRangeForDrawRange(int drawRange, float strandFraction)
        {
            if (_drawRanges.Length == 0)
            {
                return Vector2Int.zero;
            }

            DrawRange range = _drawRanges[Mathf.Clamp(drawRange, 0, _drawRanges.Length - 1)];
            int strandCount = Mathf.Clamp(Mathf.CeilToInt(range.StrandCount * strandFraction),
                                          1, range.StrandCount);

            Vector2Int first = Strands[range.StrandOffset];
            Vector2Int last = Strands[range.StrandOffset + strandCount - 1];
            return new Vector2Int(first.x, last.x + last.y - first.x);
        }

        public int EdgeCountForDrawRange(int drawRange, float strandFraction)
        {
            if (_drawRanges.Length == 0)
            {
                return 0;
            }

            DrawRange range = _drawRanges[Mathf.Clamp(drawRange, 0, _drawRanges.Length - 1)];
            int strandCount = Mathf.Clamp(Mathf.CeilToInt(range.StrandCount * strandFraction),
                                          1, range.StrandCount);
            int first = range.StrandOffset;
            return _strandEdgePrefix[first + strandCount] - _strandEdgePrefix[first];
        }

        public GraphicsBuffer PositionBuffer
        {
            get
            {
                EnsureGpuResources();
                return _positions;
            }
        }

        public GraphicsBuffer ColorBuffer
        {
            get
            {
                EnsureGpuResources();
                return _colors;
            }
        }

        public GraphicsBuffer DrawRangeIndexBuffer(int drawRange)
        {
            EnsureGpuResources();
            return _drawRangeIndices != null &&
                   drawRange >= 0 && drawRange < _drawRangeIndices.Length
                ? _drawRangeIndices[drawRange]
                : null;
        }

        public GraphicsBuffer GroupBuffer
        {
            get
            {
                EnsureGpuResources();
                return _groups;
            }
        }

        public GraphicsBuffer StrandInfoBuffer
        {
            get
            {
                EnsureGpuResources();
                return _strandInfo;
            }
        }

        public GraphicsBuffer StrandDebugBuffer
        {
            get
            {
                if (_strandDebug == null || !_strandDebug.IsValid())
                {
                    int count = Mathf.Max(1, StrandDebug?.Length ?? 0);
                    _strandDebug = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, ColorStride);

                    if (StrandDebug != null && StrandDebug.Length > 0)
                    {
                        _strandDebug.SetData(StrandDebug);
                    }
                }

                return _strandDebug;
            }
        }

        public Vector4 StrandRangeForDrawRange(int drawRange, float strandFraction)
        {
            if (_drawRanges.Length == 0)
            {
                return Vector4.zero;
            }

            DrawRange range = _drawRanges[Mathf.Clamp(drawRange, 0, _drawRanges.Length - 1)];
            Vector2Int first = Strands[range.StrandOffset];
            return new Vector4(range.StrandOffset, first.x, Mathf.Max(1, first.y), strandFraction);
        }

        public void StrandSpanForDrawRange(int drawRange, out int start, out int count)
        {
            if (_drawRanges.Length == 0)
            {
                start = 0;
                count = 0;
                return;
            }

            DrawRange range = _drawRanges[Mathf.Clamp(drawRange, 0, _drawRanges.Length - 1)];
            start = range.StrandOffset;
            count = range.StrandCount;
        }

        public Vector4 DebugRangeForDrawRange(int drawRange)
        {
            return new Vector4(GroupForDrawRange(drawRange), Mathf.Max(LongestStrand, 1e-4f), 0f, 0f);
        }

        internal void SetStrandDebug(Vector4[] strandDebug)
        {
            StrandDebug = strandDebug;
            LongestStrand = 0f;

            for (int i = 0; strandDebug != null && i < strandDebug.Length; i++)
            {
                LongestStrand = Mathf.Max(LongestStrand, strandDebug[i].w);
            }

            _strandDebug?.Dispose();
            _strandDebug = null;
        }

        public bool HasStrandAttributes { get; private set; }

        public GraphicsBuffer StrandAttributeBuffer
        {
            get
            {
                if (_strandAttributes == null || !_strandAttributes.IsValid())
                {
                    _strandAttributes = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, AttributeStride);
                    HasStrandAttributes = false;
                }

                return _strandAttributes;
            }
        }

        internal void SetStrandAttributes(NativeArray<uint3> attributes)
        {
            int count = Mathf.Max(1, attributes.Length);

            if (_strandAttributes == null || !_strandAttributes.IsValid() || _strandAttributes.count != count)
            {
                _strandAttributes?.Dispose();
                _strandAttributes = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, AttributeStride);
            }

            if (attributes.Length > 0)
            {
                _strandAttributes.SetData(attributes);
            }

            HasStrandAttributes = attributes.Length > 0;
        }

        public bool HasVertexColor
        {
            get
            {
                EnsureGpuResources();
                return _hasVertexColor;
            }
        }

        public GraphicsBuffer VertexOpacityBuffer
        {
            get
            {
                EnsureGpuResources();
                return _vertexOpacity;
            }
        }

        public GraphicsBuffer PreviousPositionBuffer =>
            _previousPositions != null && _previousPositions.IsValid() ? _previousPositions : null;

        public int PreviousPositionsFrame { get; private set; } = int.MinValue;

        public void CapturePreviousPositions()
        {
            if (IsEmpty)
            {
                return;
            }

            EnsureGpuResources();
            if (_previousPositions == null || !_previousPositions.IsValid())
            {
                _previousPositions = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopyDestination,
                    Positions.Length, PositionStride)
                {
                    name = "HairLines Previous Positions",
                };
            }

            Graphics.CopyBuffer(_positions, _previousPositions);
            PreviousPositionsFrame = Time.frameCount;
        }

        public GraphicsBuffer RestPositionBuffer
        {
            get
            {
                if (IsEmpty)
                {
                    return null;
                }

                if (_restPositions == null || !_restPositions.IsValid())
                {
                    _restPositions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Positions.Length, PositionStride);
                    _restPositions.SetData(Positions);
                }

                return _restPositions;
            }
        }

        public Vector3[] CopyOffsets { get; private set; }

        public GraphicsBuffer CopyOffsetBuffer
        {
            get
            {
                if (_copyOffsets == null || !_copyOffsets.IsValid())
                {
                    int count = Mathf.Max(1, CopyOffsets?.Length ?? 0);
                    _copyOffsets = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                                                      count, PositionStride);

                    if (CopyOffsets != null && CopyOffsets.Length > 0)
                    {
                        _copyOffsets.SetData(CopyOffsets);
                    }
                }

                return _copyOffsets;
            }
        }

        public readonly struct CopySet
        {
            public int Copies { get; }
            public float SpreadRoot { get; }
            public float SpreadTip { get; }
            public float ColorVariance { get; }

            public CopySet(int copies, float spreadRoot, float spreadTip, float colorVariance)
            {
                Copies = copies;
                SpreadRoot = spreadRoot;
                SpreadTip = spreadTip;
                ColorVariance = colorVariance;
            }
        }

        internal void SetCopySets(Vector3[] offsets, CopySet[] setsByGroup)
        {
            CopyOffsets = offsets;
            _copySets = setsByGroup;

            if (_copyOffsets != null && _copyOffsets.IsValid())
            {
                _copyOffsets.Release();
                _copyOffsets = null;
            }

            int cursor = 0;
            for (int range = 0; range < _drawRanges.Length; range++)
            {
                _drawRanges[range].CopyOffsetStart = cursor;
                cursor += _drawRanges[range].StrandCount * CopySetForDrawRange(range).Copies;
            }
        }

        private CopySet CopySetForDrawRange(int drawRange)
        {
            if (_copySets == null || _drawRanges.Length == 0)
            {
                return default;
            }

            int group = _drawRanges[Mathf.Clamp(drawRange, 0, _drawRanges.Length - 1)].Group;
            return group >= 0 && group < _copySets.Length ? _copySets[group] : default;
        }

        private GraphicsBuffer _positions;
        private GraphicsBuffer _colors;
        private GraphicsBuffer _copyOffsets;
        private CopySet[] _copySets;
        private GraphicsBuffer[] _drawRangeIndices;
        private GraphicsBuffer _vertexOpacity;
        private GraphicsBuffer _strandInfo;
        private GraphicsBuffer _strandDebug;
        private GraphicsBuffer _strandAttributes;
        private bool _hasVertexColor;
        private float[] _vertexOpacityScratch;
        private GraphicsBuffer _groups;
        private GraphicsBuffer _restPositions;
        private GraphicsBuffer _previousPositions;
        private readonly int[] _strandEdgePrefix;
        private readonly DrawRange[] _drawRanges;
        private Vector4[] _colorData;
        private int[] _indexData;
        private float[] _edgeOpacityData;
        private float[] _strandShade;
        private float[] _groupTipOpacity;
        private ulong[] _strandHash;

        private const int PositionStride = sizeof(float) * 3;
        private const int ColorStride = sizeof(float) * 4;
        private const int IndexStride = sizeof(uint);
        private const int OpacityStride = sizeof(float);
        private const int GroupStride = sizeof(uint);
        private const int AttributeStride = sizeof(uint) * 3;

        private static readonly ProfilerMarker _uploadGeometryMarker =
            new ProfilerMarker("HairLines.Model.UploadGeometry");
        private static readonly ProfilerMarker _uploadOpacityMarker =
            new ProfilerMarker("HairLines.Model.UploadOpacity");
        private static readonly ProfilerMarker _patchMarker =
            new ProfilerMarker("HairLines.Model.Patch");

        public LinePrimitiveModel(Vector3[] positions, Vector4[] colors, int[] indices,
                                  float[] edgeOpacity, Vector2Int[] strands, uint[] vertexGroups)
        {
            Positions = positions;
            _colorData = colors;
            _indexData = indices;
            _edgeOpacityData = edgeOpacity;
            Strands = strands;
            VertexGroups = vertexGroups;
            _strandEdgePrefix = BuildStrandEdgePrefix(strands);
            _drawRanges = BuildDrawRanges(strands, vertexGroups, _strandEdgePrefix);
            LocalBounds = BoundsOf(positions);
        }

        internal LinePrimitiveModel(Vector3[] positions, Vector2Int[] strands, uint[] vertexGroups,
                                    ulong[] strandHash, NativeArray<Vector3> gpuPositions,
                                    NativeArray<uint> strandInfo, NativeArray<float> vertexOpacity,
                                    NativeArray<uint> indices)
        {
            Positions = positions;
            Strands = strands;
            VertexGroups = vertexGroups;
            _strandHash = strandHash;
            _strandEdgePrefix = BuildStrandEdgePrefix(strands);
            _drawRanges = BuildDrawRanges(strands, vertexGroups, _strandEdgePrefix);

            if (IsEmpty)
            {
                return;
            }

            const GraphicsBuffer.Target structured = GraphicsBuffer.Target.Structured;
            _positions = new GraphicsBuffer(structured | GraphicsBuffer.Target.CopySource,
                                            positions.Length, PositionStride);
            _colors = new GraphicsBuffer(structured, 1, ColorStride);
            _vertexOpacity = new GraphicsBuffer(structured, positions.Length, OpacityStride);
            _strandInfo = new GraphicsBuffer(structured, positions.Length, GroupStride);
            _groups = new GraphicsBuffer(structured, vertexGroups.Length, GroupStride);
            _hasVertexColor = false;

            _positions.SetData(gpuPositions);
            _vertexOpacity.SetData(vertexOpacity);
            _strandInfo.SetData(strandInfo);
            _groups.SetData(vertexGroups);

            _drawRangeIndices = new GraphicsBuffer[_drawRanges.Length];
            for (int range = 0; range < _drawRanges.Length; range++)
            {
                int start = _drawRanges[range].EdgeOffset * 2;
                int end = range + 1 < _drawRanges.Length ? _drawRanges[range + 1].EdgeOffset * 2 : indices.Length;
                int count = Mathf.Max(end - start, 1);
                _drawRangeIndices[range] = new GraphicsBuffer(GraphicsBuffer.Target.Index, count, IndexStride);
                _drawRangeIndices[range].SetData(indices, start, 0, Mathf.Min(count, indices.Length - start));
            }
        }

        private LinePrimitiveModel(LinePrimitiveModel previous)
        {
            Positions = previous.Positions;
            Strands = previous.Strands;
            VertexGroups = previous.VertexGroups;
            _strandEdgePrefix = previous._strandEdgePrefix;
            _drawRanges = (DrawRange[])previous._drawRanges.Clone();
            _hasVertexColor = previous._hasVertexColor;

            _positions = previous._positions;
            _colors = previous._colors;
            _vertexOpacity = previous._vertexOpacity;
            _strandInfo = previous._strandInfo;
            _groups = previous._groups;
            _drawRangeIndices = previous._drawRangeIndices;
            _strandAttributes = previous._strandAttributes;

            previous._positions = null;
            previous._strandAttributes = null;
            previous._colors = null;
            previous._vertexOpacity = null;
            previous._strandInfo = null;
            previous._groups = null;
            previous._drawRangeIndices = null;
        }

        internal bool CanPatch(Vector2Int[] strands, NativeArray<uint> vertexGroups)
        {
            if (_strandHash == null || _positions == null || !_positions.IsValid() ||
                strands == null || Strands == null || strands.Length != Strands.Length ||
                vertexGroups.Length != VertexGroups.Length)
            {
                return false;
            }

            for (int i = 0; i < strands.Length; i++)
            {
                if (strands[i] != Strands[i] || vertexGroups[strands[i].x] != VertexGroups[strands[i].x])
                {
                    return false;
                }
            }

            return true;
        }

        internal static LinePrimitiveModel Patch(LinePrimitiveModel previous, ulong[] strandHash,
                                                 NativeArray<Vector3> positions, NativeArray<uint> strandInfo,
                                                 NativeArray<float> vertexOpacity)
        {
            using ProfilerMarker.AutoScope scope = _patchMarker.Auto();
            ulong[] previousHash = previous._strandHash;
            var model = new LinePrimitiveModel(previous) { _strandHash = strandHash, PatchedRanges = 0 };

            for (int range = 0; range < model._drawRanges.Length; range++)
            {
                DrawRange drawRange = model._drawRanges[range];
                int firstStrand = drawRange.StrandOffset;
                int lastStrand = firstStrand + drawRange.StrandCount;
                bool dirty = false;

                for (int strand = firstStrand; strand < lastStrand && !dirty; strand++)
                {
                    dirty = strandHash[strand] != previousHash[strand];
                }

                if (!dirty)
                {
                    continue;
                }

                Vector2Int first = model.Strands[firstStrand];
                Vector2Int last = model.Strands[lastStrand - 1];
                int start = first.x;
                int count = last.x + last.y - start;

                NativeArray<Vector3>.Copy(positions, start, model.Positions, start, count);
                model._positions.SetData(positions, start, start, count);
                model._strandInfo.SetData(strandInfo, start, start, count);
                model._vertexOpacity.SetData(vertexOpacity, start, start, count);
                model.PatchedRanges++;
            }

            return model;
        }

        internal void SetStrandShade(float[] strandShade, float[] groupTipOpacity)
        {
            _strandShade = strandShade;
            _groupTipOpacity = groupTipOpacity;
        }

        private Vector4[] SynthesizeColors()
        {
            var colors = new Vector4[Positions != null ? Positions.Length : 0];

            for (int strand = 0; Strands != null && strand < Strands.Length; strand++)
            {
                float shade = _strandShade != null && strand < _strandShade.Length ? _strandShade[strand] : 1f;
                int count = Strands[strand].y;

                for (int k = 0; k < count; k++)
                {
                    float along = count > 1 ? k / (float)(count - 1) : 0f;
                    colors[Strands[strand].x + k] = new Vector4(shade, shade, shade, along);
                }
            }

            return colors;
        }

        private int[] SynthesizeIndices()
        {
            var indices = new int[EdgeCount * 2];
            int cursor = 0;

            for (int strand = 0; Strands != null && strand < Strands.Length; strand++)
            {
                for (int edge = 0; edge < Strands[strand].y - 1; edge++)
                {
                    indices[cursor++] = Strands[strand].x + edge;
                    indices[cursor++] = Strands[strand].x + edge + 1;
                }
            }

            return indices;
        }

        private float[] SynthesizeEdgeOpacity()
        {
            var opacity = new float[EdgeCount];
            int cursor = 0;

            for (int strand = 0; Strands != null && strand < Strands.Length; strand++)
            {
                int group = GroupOfStrand(Strands[strand], VertexGroups);
                float tip = _groupTipOpacity != null && group < _groupTipOpacity.Length
                    ? _groupTipOpacity[group]
                    : 1f;
                int edges = Strands[strand].y - 1;

                for (int edge = 0; edge < edges; edge++)
                {
                    opacity[cursor++] = Mathf.Lerp(1f, tip, edges > 1 ? edge / (float)(edges - 1) : 0f);
                }
            }

            return opacity;
        }

        public void UploadGeometry()
        {
            using ProfilerMarker.AutoScope scope = _uploadGeometryMarker.Auto();
            if (IsEmpty)
            {
                return;
            }

            EnsureGpuResources();
            _positions.SetData(Positions);
        }

        public void UploadOpacity()
        {
            using ProfilerMarker.AutoScope scope = _uploadOpacityMarker.Auto();
            if (IsEmpty)
            {
                return;
            }

            EnsureGpuResources();
            UploadVertexOpacity();
        }

        public void UploadRestPositions()
        {
            if (IsEmpty)
            {
                return;
            }

            RestPositionBuffer.SetData(Positions);
        }

        public void Dispose()
        {
            _positions?.Dispose();
            _positions = null;

            _colors?.Dispose();
            _colors = null;

            if (_drawRangeIndices != null)
            {
                for (int i = 0; i < _drawRangeIndices.Length; i++)
                {
                    _drawRangeIndices[i]?.Dispose();
                }

                _drawRangeIndices = null;
            }

            _vertexOpacity?.Dispose();
            _vertexOpacity = null;

            _strandInfo?.Dispose();
            _strandDebug?.Dispose();
            _strandDebug = null;
            _strandInfo = null;

            _strandAttributes?.Dispose();
            _strandAttributes = null;
            HasStrandAttributes = false;

            _groups?.Dispose();
            _groups = null;

            _restPositions?.Dispose();
            _restPositions = null;

            _previousPositions?.Dispose();
            _previousPositions = null;
            PreviousPositionsFrame = int.MinValue;

            _copyOffsets?.Dispose();
            _copyOffsets = null;
        }

        public static Bounds BoundsOf(Vector3[] points)
        {
            if (points == null || points.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            float minX = points[0].x;
            float minY = points[0].y;
            float minZ = points[0].z;
            float maxX = minX;
            float maxY = minY;
            float maxZ = minZ;

            for (int i = 1; i < points.Length; i++)
            {
                Vector3 point = points[i];

                minX = point.x < minX ? point.x : minX;
                minY = point.y < minY ? point.y : minY;
                minZ = point.z < minZ ? point.z : minZ;
                maxX = point.x > maxX ? point.x : maxX;
                maxY = point.y > maxY ? point.y : maxY;
                maxZ = point.z > maxZ ? point.z : maxZ;
            }

            return BoundsFrom(minX, minY, minZ, maxX, maxY, maxZ);
        }

        private static Bounds BoundsFrom(float minX, float minY, float minZ,
                                        float maxX, float maxY, float maxZ)
        {
            var center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
            var size = new Vector3(maxX - minX, maxY - minY, maxZ - minZ);
            return new Bounds(center, size);
        }

        private static int[] BuildStrandEdgePrefix(Vector2Int[] strands)
        {
            if (strands == null)
            {
                return Array.Empty<int>();
            }

            var prefix = new int[strands.Length + 1];
            for (int i = 0; i < strands.Length; i++)
            {
                prefix[i + 1] = prefix[i] + Mathf.Max(0, strands[i].y - 1);
            }

            return prefix;
        }

        private static DrawRange[] BuildDrawRanges(Vector2Int[] strands, uint[] vertexGroups,
                                                   int[] strandEdgePrefix)
        {
            if (strands == null || strands.Length == 0)
            {
                return Array.Empty<DrawRange>();
            }

            var ranges = new System.Collections.Generic.List<DrawRange>();
            int firstStrand = 0;
            int group = GroupOfStrand(strands[0], vertexGroups);

            for (int strand = 1; strand <= strands.Length; strand++)
            {
                int nextGroup = strand < strands.Length ? GroupOfStrand(strands[strand], vertexGroups) : -1;
                if (strand < strands.Length && nextGroup == group)
                {
                    continue;
                }

                ranges.Add(new DrawRange(group, firstStrand, strand - firstStrand,
                                         strandEdgePrefix[firstStrand]));
                firstStrand = strand;
                group = nextGroup;
            }

            return ranges.ToArray();
        }

        private static int GroupOfStrand(Vector2Int strand, uint[] vertexGroups)
        {
            if (vertexGroups == null || strand.x < 0 || strand.x >= vertexGroups.Length)
            {
                return 0;
            }

            return Mathf.Clamp((int)vertexGroups[strand.x], 0, 15);
        }

        private void EnsureGpuResources()
        {
            if (IsEmpty)
            {
                return;
            }

            if (_positions != null && _positions.IsValid())
            {
                return;
            }

            const GraphicsBuffer.Target structured = GraphicsBuffer.Target.Structured;
            _positions = new GraphicsBuffer(structured | GraphicsBuffer.Target.CopySource,
                                            Positions.Length, PositionStride);
            _colors = new GraphicsBuffer(structured, Colors.Length, ColorStride);
            _vertexOpacity = new GraphicsBuffer(structured, Positions.Length, OpacityStride);
            _strandInfo = new GraphicsBuffer(structured, Positions.Length, GroupStride);
            CreateDrawRangeIndices();
            CreateStrandInfo();
            _groups = new GraphicsBuffer(structured, VertexGroups.Length, GroupStride);

            _positions.SetData(Positions);
            _colors.SetData(Colors);
            _groups.SetData(VertexGroups);
            UploadVertexOpacity();
        }

        private void CreateStrandInfo()
        {
            var packed = new uint[Positions.Length];
            _hasVertexColor = false;

            for (int s = 0; s < Strands.Length; s++)
            {
                int first = Strands[s].x;
                int count = Mathf.Clamp(Strands[s].y, 1, 4095);
                for (int k = 0; k < Strands[s].y; k++)
                {
                    int vertex = first + k;
                    if ((uint)vertex >= (uint)packed.Length)
                    {
                        break;
                    }

                    Vector4 color = Colors[vertex];
                    if (!_hasVertexColor &&
                        (Mathf.Abs(color.x - color.y) > 1e-4f || Mathf.Abs(color.y - color.z) > 1e-4f ||
                         color.x > 1.0001f))
                    {
                        _hasVertexColor = true;
                    }

                    uint local = (uint)Mathf.Min(k, 4095);
                    uint shade = (uint)Mathf.Clamp(Mathf.RoundToInt(color.x * 127f), 0, 255);
                    packed[vertex] = local | ((uint)count << 12) | (shade << 24);
                }
            }

            _strandInfo.SetData(packed);
        }

        private void CreateDrawRangeIndices()
        {
            _drawRangeIndices = new GraphicsBuffer[_drawRanges.Length];
            for (int range = 0; range < _drawRanges.Length; range++)
            {
                int start = _drawRanges[range].EdgeOffset * 2;
                int end = range + 1 < _drawRanges.Length
                    ? _drawRanges[range + 1].EdgeOffset * 2
                    : Indices.Length;
                int count = Mathf.Max(end - start, 1);

                var slice = new uint[count];
                for (int i = 0; i < count && start + i < Indices.Length; i++)
                {
                    slice[i] = (uint)Indices[start + i];
                }

                _drawRangeIndices[range] = new GraphicsBuffer(GraphicsBuffer.Target.Index,
                                                              count, IndexStride);
                _drawRangeIndices[range].SetData(slice);
            }
        }

        private void UploadVertexOpacity()
        {
            if (_vertexOpacity == null)
            {
                return;
            }

            if (_vertexOpacityScratch == null || _vertexOpacityScratch.Length != Positions.Length)
            {
                _vertexOpacityScratch = new float[Positions.Length];
            }

            int edges = EdgeCount;
            for (int edge = 0; edge < edges; edge++)
            {
                int first = Indices[edge * 2];
                int second = Indices[edge * 2 + 1];
                float value = EdgeOpacity[edge];
                if ((uint)first < (uint)_vertexOpacityScratch.Length)
                {
                    _vertexOpacityScratch[first] = value;
                }

                if ((uint)second < (uint)_vertexOpacityScratch.Length)
                {
                    _vertexOpacityScratch[second] = value;
                }
            }

            _vertexOpacity.SetData(_vertexOpacityScratch);
        }

        private struct DrawRange
        {
            public int Group { get; }
            public int StrandOffset { get; }
            public int StrandCount { get; }
            public int EdgeOffset { get; }

            public int CopyOffsetStart { get; set; }

            public DrawRange(int group, int strandOffset, int strandCount, int edgeOffset)
            {
                Group = group;
                StrandOffset = strandOffset;
                StrandCount = strandCount;
                EdgeOffset = edgeOffset;
                CopyOffsetStart = 0;
            }
        }
    }
}
