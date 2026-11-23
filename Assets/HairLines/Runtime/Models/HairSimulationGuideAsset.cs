using System;
using System.Collections.Generic;
using UnityEngine;

namespace HairLines
{
    [CreateAssetMenu(fileName = "HairSimulationGuides",
                     menuName = "Hair Lines/Simulation Guide Map")]
    public sealed class HairSimulationGuideAsset : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;
        public const int InfluenceCount = 2;

        private const float ArcTolerance = 1e-3f;

        public int SchemaVersion => _schemaVersion;
        public int SourceVertexCount => _sourceVertexCount;
        public int SourceStrandCount => _sourceStrandCount;
        public int GuideCount => _guideSourceStrands?.Length ?? 0;
        public float GuideFraction => SourceStrandCount > 0
            ? GuideCount / (float)SourceStrandCount
            : 0f;
        public ulong TopologyHash => unchecked((ulong)_topologyHash);
        public ulong RestPoseHash => unchecked((ulong)_restPoseHash);
        public ulong MapHash => unchecked((ulong)_mapHash);

        [SerializeField, HideInInspector]
        private int _schemaVersion = CurrentSchemaVersion;

        [SerializeField, HideInInspector]
        private int _sourceVertexCount;

        [SerializeField, HideInInspector]
        private int _sourceStrandCount;

        [SerializeField, HideInInspector]
        private long _topologyHash;

        [SerializeField, HideInInspector]
        private long _restPoseHash;

        [SerializeField, HideInInspector]
        private long _mapHash;

        [SerializeField, HideInInspector]
        private int[] _guideSourceStrands = Array.Empty<int>();

        [SerializeField, HideInInspector]
        private Vector2Int[] _guideSourceRanges = Array.Empty<Vector2Int>();

        [SerializeField, HideInInspector]
        private uint[] _guideGroups = Array.Empty<uint>();

        [SerializeField, HideInInspector]
        private float[] _guideReach = Array.Empty<float>();

        [SerializeField, HideInInspector]
        private Vector2Int[] _strandGuideIds2 = Array.Empty<Vector2Int>();

        [SerializeField, HideInInspector]
        private Vector2[] _strandGuideWeights2 = Array.Empty<Vector2>();

        [SerializeField, HideInInspector]
        private Vector3[] _strandRestRoots = Array.Empty<Vector3>();

        [SerializeField, HideInInspector]
        private Quaternion[] _strandRestFrames = Array.Empty<Quaternion>();

        [SerializeField, HideInInspector]
        private float[] _vertexArcParameters = Array.Empty<float>();

        public bool TryFill(LinePrimitiveModel source, HairSimulationGuideBuildData data,
                            out string error)
        {
            if (!Validate(source, data, true, false, out error))
            {
                return false;
            }

            _schemaVersion = CurrentSchemaVersion;
            _sourceVertexCount = source.VertexCount;
            _sourceStrandCount = source.StrandCount;
            _topologyHash = unchecked((long)ComputeTopologyHash(source));
            _restPoseHash = unchecked((long)ComputeRestPoseHash(source));
            _mapHash = unchecked((long)ComputeMapHash(data));

            _guideSourceStrands = Clone(data.GuideSourceStrands);
            _guideSourceRanges = Clone(data.GuideSourceRanges);
            _guideGroups = Clone(data.GuideGroups);
            _guideReach = Clone(data.GuideReach);
            _strandGuideIds2 = Clone(data.StrandGuideIds2);
            _strandGuideWeights2 = Clone(data.StrandGuideWeights2);
            _strandRestRoots = Clone(data.StrandRestRoots);
            _strandRestFrames = Clone(data.StrandRestFrames);
            _vertexArcParameters = Clone(data.VertexArcParameters);
            return true;
        }

        public bool TryOpen(LinePrimitiveModel source, out HairSimulationGuideMap map,
                            out string error)
        {
            map = default;
            if (_schemaVersion != CurrentSchemaVersion)
            {
                error = $"Guide-map schema {_schemaVersion} is not supported; expected " +
                        $"{CurrentSchemaVersion}.";
                return false;
            }

            var data = new HairSimulationGuideBuildData(
                _guideSourceStrands, _guideSourceRanges, _guideGroups, _guideReach,
                _strandGuideIds2, _strandGuideWeights2, _strandRestRoots,
                _strandRestFrames, _vertexArcParameters);

            if (_sourceVertexCount != source.VertexCount ||
                _sourceStrandCount != source.StrandCount)
            {
                error = $"Guide map was baked for {_sourceStrandCount:N0} strands and " +
                        $"{_sourceVertexCount:N0} vertices; this groom has " +
                        $"{source.StrandCount:N0} and {source.VertexCount:N0}. Rebake it.";
                return false;
            }

            if (TopologyHash != ComputeTopologyHash(source))
            {
                error = "The strand layout changed since the map was baked (ranges or groups). " +
                        "Rebake it for this groom.";
                return false;
            }

            bool sameBits = RestPoseHash == ComputeRestPoseHash(source);
            if (!Validate(source, data, true, !sameBits, out error))
            {
                return false;
            }

            if (MapHash != ComputeMapHash(data))
            {
                error = "The guide-map data fingerprint is stale or corrupt; rebake it.";
                return false;
            }

            map = new HairSimulationGuideMap(data, MapHash);
            error = null;
            return true;
        }

        public void Clear()
        {
            _schemaVersion = CurrentSchemaVersion;
            _sourceVertexCount = 0;
            _sourceStrandCount = 0;
            _topologyHash = 0;
            _restPoseHash = 0;
            _mapHash = 0;
            _guideSourceStrands = Array.Empty<int>();
            _guideSourceRanges = Array.Empty<Vector2Int>();
            _guideGroups = Array.Empty<uint>();
            _guideReach = Array.Empty<float>();
            _strandGuideIds2 = Array.Empty<Vector2Int>();
            _strandGuideWeights2 = Array.Empty<Vector2>();
            _strandRestRoots = Array.Empty<Vector3>();
            _strandRestFrames = Array.Empty<Quaternion>();
            _vertexArcParameters = Array.Empty<float>();
        }

        public static ulong ComputeTopologyHash(LinePrimitiveModel source)
        {
            if (source == null)
            {
                return 0;
            }

            ulong hash = FnvOffset;
            Add(ref hash, source.VertexCount);
            Add(ref hash, source.StrandCount);
            Vector2Int[] strands = source.Strands;
            if (strands == null)
            {
                return hash;
            }
            for (int i = 0; i < strands.Length; i++)
            {
                Add(ref hash, strands[i].x);
                Add(ref hash, strands[i].y);
            }

            uint[] groups = source.VertexGroups;
            Add(ref hash, groups?.Length ?? 0);
            if (groups != null)
            {
                for (int i = 0; i < groups.Length; i++)
                {
                    Add(ref hash, groups[i]);
                }
            }

            return hash;
        }

        public static ulong ComputeRestPoseHash(LinePrimitiveModel source)
        {
            if (source == null)
            {
                return 0;
            }

            ulong hash = FnvOffset;
            Vector3[] positions = source.Positions;
            Add(ref hash, positions?.Length ?? 0);
            if (positions != null)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    Add(ref hash, positions[i].x);
                    Add(ref hash, positions[i].y);
                    Add(ref hash, positions[i].z);
                }
            }
            return hash;
        }

        public static ulong ComputeMapHash(HairSimulationGuideBuildData data)
        {
            if (data == null)
            {
                return 0;
            }

            ulong hash = FnvOffset;
            Add(ref hash, CurrentSchemaVersion);
            Add(ref hash, InfluenceCount);
            Add(ref hash, data.GuideSourceStrands?.Length ?? 0);
            Add(ref hash, data.StrandGuideIds2?.Length ?? 0);
            Add(ref hash, data.VertexArcParameters?.Length ?? 0);
            Add(ref hash, data.GuideSourceStrands);
            Add(ref hash, data.GuideSourceRanges);
            Add(ref hash, data.GuideGroups);
            Add(ref hash, data.GuideReach);
            Add(ref hash, data.StrandGuideIds2);
            Add(ref hash, data.StrandGuideWeights2);
            Add(ref hash, data.StrandRestRoots);
            Add(ref hash, data.StrandRestFrames);
            Add(ref hash, data.VertexArcParameters);
            return hash;
        }

        private static bool Validate(LinePrimitiveModel source,
                                     HairSimulationGuideBuildData data,
                                     bool verifyRestRoots,
                                     bool verifyRestShape,
                                     out string error)
        {
            if (source == null || source.IsEmpty || source.Strands == null)
            {
                error = "The source groom is empty.";
                return false;
            }
            if (data == null)
            {
                error = "Guide-map data is null.";
                return false;
            }

            int guideCount = data.GuideSourceStrands?.Length ?? 0;
            int strandCount = source.StrandCount;
            int vertexCount = source.VertexCount;
            if (guideCount == 0)
            {
                error = "A guide map must contain at least one guide.";
                return false;
            }

            if (!LengthIs(data.GuideSourceRanges, guideCount) ||
                !LengthIs(data.GuideGroups, guideCount) ||
                !LengthIs(data.GuideReach, guideCount) ||
                !LengthIs(data.StrandGuideIds2, strandCount) ||
                !LengthIs(data.StrandGuideWeights2, strandCount) ||
                !LengthIs(data.StrandRestRoots, strandCount) ||
                !LengthIs(data.StrandRestFrames, strandCount) ||
                !LengthIs(data.VertexArcParameters, vertexCount))
            {
                error = "Guide-map arrays do not match guide, strand, or vertex counts.";
                return false;
            }

            var seenGuideStrands = new HashSet<int>();
            for (int guide = 0; guide < guideCount; guide++)
            {
                int sourceStrand = data.GuideSourceStrands[guide];
                if ((uint)sourceStrand >= (uint)strandCount)
                {
                    error = $"Guide {guide} references invalid strand {sourceStrand}.";
                    return false;
                }
                if (!seenGuideStrands.Add(sourceStrand))
                {
                    error = $"Guide map selects source strand {sourceStrand} more than once.";
                    return false;
                }

                Vector2Int expected = source.Strands[sourceStrand];
                if (data.GuideSourceRanges[guide] != expected || expected.y <= 0)
                {
                    error = $"Guide {guide} has a stale or empty source range.";
                    return false;
                }

                uint group = GroupOfStrand(source, sourceStrand);
                if (data.GuideGroups[guide] != group ||
                    !RangeHasGroup(source, expected, group))
                {
                    error = $"Guide {guide} crosses its source group boundary.";
                    return false;
                }

                float reach = data.GuideReach[guide];
                if (!Finite(reach) || reach < 0f)
                {
                    error = $"Guide {guide} has invalid reach metadata.";
                    return false;
                }
            }

            for (int strand = 0; strand < strandCount; strand++)
            {
                Vector2Int ids = data.StrandGuideIds2[strand];
                Vector2 weights = data.StrandGuideWeights2[strand];
                if ((uint)ids.x >= (uint)guideCount || (uint)ids.y >= (uint)guideCount)
                {
                    error = $"Strand {strand} references an invalid guide slot.";
                    return false;
                }
                if (!Finite(weights.x) || !Finite(weights.y) ||
                    weights.x < 0f || weights.y < 0f ||
                    Mathf.Abs(weights.x + weights.y - 1f) > 1e-4f)
                {
                    error = $"Strand {strand} has non-normalized guide weights.";
                    return false;
                }

                uint group = GroupOfStrand(source, strand);
                if (!RangeHasGroup(source, source.Strands[strand], group) ||
                    data.GuideGroups[ids.x] != group || data.GuideGroups[ids.y] != group)
                {
                    error = $"Strand {strand} has an influence across a group/material boundary.";
                    return false;
                }

                Quaternion frame = data.StrandRestFrames[strand];
                float norm = frame.x * frame.x + frame.y * frame.y +
                             frame.z * frame.z + frame.w * frame.w;
                if (!Finite(data.StrandRestRoots[strand]) || !Finite(frame) ||
                    Mathf.Abs(norm - 1f) > 1e-3f)
                {
                    error = $"Strand {strand} has an invalid rest root frame.";
                    return false;
                }

                Vector2Int range = source.Strands[strand];
                if (range.y <= 0 || range.x < 0 || range.x > vertexCount ||
                    range.y > vertexCount - range.x)
                {
                    error = $"Source strand {strand} has an invalid vertex range.";
                    return false;
                }
                if (verifyRestRoots &&
                    (data.StrandRestRoots[strand] - source.Positions[range.x]).sqrMagnitude > 1e-10f)
                {
                    error = $"Strand {strand} rest root does not match the final groom.";
                    return false;
                }

                float previous = -1f;
                for (int local = 0; local < range.y; local++)
                {
                    float arc = data.VertexArcParameters[range.x + local];
                    if (!Finite(arc) || arc < -1e-5f || arc > 1.00001f || arc + 1e-6f < previous)
                    {
                        error = $"Strand {strand} has invalid arc-length samples.";
                        return false;
                    }
                    previous = arc;
                }

                if (verifyRestShape &&
                    ArcDrift(source.Positions, range, data.VertexArcParameters) > ArcTolerance)
                {
                    error = $"Strand {strand} no longer follows the rest pose the map was baked " +
                            "against; rebake the guide map for this groom.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        public static void FillArcParameters(Vector3[] positions, Vector2Int range,
                                             float[] destination)
        {
            int start = range.x;
            int count = range.y;
            destination[start] = 0f;
            float length = 0f;
            for (int local = 1; local < count; local++)
            {
                length += Vector3.Distance(positions[start + local - 1],
                                           positions[start + local]);
                destination[start + local] = length;
            }

            if (IsDegenerateLength(length))
            {
                float denominator = Mathf.Max(1, count - 1);
                for (int local = 0; local < count; local++)
                {
                    destination[start + local] = local / denominator;
                }
                return;
            }

            float inverseLength = 1f / length;
            for (int local = 1; local < count; local++)
            {
                destination[start + local] *= inverseLength;
            }
        }

        private static float ArcDrift(Vector3[] positions, Vector2Int range, float[] stored)
        {
            int start = range.x;
            int count = range.y;
            float length = 0f;
            for (int local = 1; local < count; local++)
            {
                length += Vector3.Distance(positions[start + local - 1],
                                           positions[start + local]);
            }

            bool degenerate = IsDegenerateLength(length);
            float inverseLength = degenerate ? 0f : 1f / length;
            float denominator = Mathf.Max(1, count - 1);
            float travelled = 0f;
            float drift = 0f;
            for (int local = 0; local < count; local++)
            {
                if (local > 0)
                {
                    travelled += Vector3.Distance(positions[start + local - 1],
                                                  positions[start + local]);
                }

                float expected = degenerate ? local / denominator : travelled * inverseLength;
                drift = Mathf.Max(drift, Mathf.Abs(stored[start + local] - expected));
            }

            return drift;
        }

        private static bool IsDegenerateLength(float length)
        {
            return !(length > 1e-8f) || float.IsNaN(length) || float.IsInfinity(length);
        }

        private static uint GroupOfStrand(LinePrimitiveModel source, int strand)
        {
            Vector2Int range = source.Strands[strand];
            if (source.VertexGroups == null || range.x < 0 || range.x >= source.VertexGroups.Length)
            {
                return 0u;
            }

            return source.VertexGroups[range.x] > 15u ? 15u : source.VertexGroups[range.x];
        }

        private static bool RangeHasGroup(LinePrimitiveModel source, Vector2Int range, uint group)
        {
            if (source.VertexGroups == null)
            {
                return group == 0u;
            }

            int length = source.VertexGroups.Length;
            if (range.x < 0 || range.y < 0 || range.x > length || range.y > length - range.x)
            {
                return false;
            }

            int end = range.x + range.y;
            for (int vertex = range.x; vertex < end; vertex++)
            {
                uint value = source.VertexGroups[vertex] > 15u ? 15u : source.VertexGroups[vertex];
                if (value != group)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool LengthIs(Array values, int length)
        {
            return values != null && values.Length == length;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }

        private static bool Finite(Quaternion value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
        }

        private static T[] Clone<T>(T[] source)
        {
            return source == null ? Array.Empty<T>() : (T[])source.Clone();
        }

        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private static void Add(ref ulong hash, int value)
        {
            Add(ref hash, unchecked((uint)value));
        }

        private static void Add(ref ulong hash, uint value)
        {
            hash ^= value & 0xffu;
            hash *= FnvPrime;
            hash ^= (value >> 8) & 0xffu;
            hash *= FnvPrime;
            hash ^= (value >> 16) & 0xffu;
            hash *= FnvPrime;
            hash ^= value >> 24;
            hash *= FnvPrime;
        }

        private static void Add(ref ulong hash, float value)
        {
            Add(ref hash, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
        }

        private static void Add(ref ulong hash, int[] values)
        {
            Add(ref hash, values?.Length ?? 0);
            if (values == null) return;
            for (int i = 0; i < values.Length; i++) Add(ref hash, values[i]);
        }

        private static void Add(ref ulong hash, uint[] values)
        {
            Add(ref hash, values?.Length ?? 0);
            if (values == null) return;
            for (int i = 0; i < values.Length; i++) Add(ref hash, values[i]);
        }

        private static void Add(ref ulong hash, float[] values)
        {
            Add(ref hash, values?.Length ?? 0);
            if (values == null) return;
            for (int i = 0; i < values.Length; i++) Add(ref hash, values[i]);
        }

        private static void Add(ref ulong hash, Vector2Int[] values)
        {
            Add(ref hash, values?.Length ?? 0);
            if (values == null) return;
            for (int i = 0; i < values.Length; i++)
            {
                Add(ref hash, values[i].x);
                Add(ref hash, values[i].y);
            }
        }

        private static void Add(ref ulong hash, Vector2[] values)
        {
            Add(ref hash, values?.Length ?? 0);
            if (values == null) return;
            for (int i = 0; i < values.Length; i++)
            {
                Add(ref hash, values[i].x);
                Add(ref hash, values[i].y);
            }
        }

        private static void Add(ref ulong hash, Vector3[] values)
        {
            Add(ref hash, values?.Length ?? 0);
            if (values == null) return;
            for (int i = 0; i < values.Length; i++)
            {
                Add(ref hash, values[i].x);
                Add(ref hash, values[i].y);
                Add(ref hash, values[i].z);
            }
        }

        private static void Add(ref ulong hash, Quaternion[] values)
        {
            Add(ref hash, values?.Length ?? 0);
            if (values == null) return;
            for (int i = 0; i < values.Length; i++)
            {
                Add(ref hash, values[i].x);
                Add(ref hash, values[i].y);
                Add(ref hash, values[i].z);
                Add(ref hash, values[i].w);
            }
        }
    }

    public sealed class HairSimulationGuideBuildData
    {
        public int[] GuideSourceStrands { get; }
        public Vector2Int[] GuideSourceRanges { get; }
        public uint[] GuideGroups { get; }
        public float[] GuideReach { get; }
        public Vector2Int[] StrandGuideIds2 { get; }
        public Vector2[] StrandGuideWeights2 { get; }
        public Vector3[] StrandRestRoots { get; }
        public Quaternion[] StrandRestFrames { get; }
        public float[] VertexArcParameters { get; }

        public HairSimulationGuideBuildData(
            int[] guideSourceStrands,
            Vector2Int[] guideSourceRanges,
            uint[] guideGroups,
            float[] guideReach,
            Vector2Int[] strandGuideIds2,
            Vector2[] strandGuideWeights2,
            Vector3[] strandRestRoots,
            Quaternion[] strandRestFrames,
            float[] vertexArcParameters)
        {
            GuideSourceStrands = guideSourceStrands;
            GuideSourceRanges = guideSourceRanges;
            GuideGroups = guideGroups;
            GuideReach = guideReach;
            StrandGuideIds2 = strandGuideIds2;
            StrandGuideWeights2 = strandGuideWeights2;
            StrandRestRoots = strandRestRoots;
            StrandRestFrames = strandRestFrames;
            VertexArcParameters = vertexArcParameters;
        }
    }

    public readonly struct HairSimulationGuideMap
    {
        public ulong MapHash => _mapHash;
        public int GuideCount => _data?.GuideSourceStrands?.Length ?? 0;
        public int StrandCount => _data?.StrandGuideIds2?.Length ?? 0;
        public int VertexCount => _data?.VertexArcParameters?.Length ?? 0;

        public IReadOnlyList<int> GuideSourceStrands => _data?.GuideSourceStrands;
        public IReadOnlyList<Vector2Int> GuideSourceRanges => _data?.GuideSourceRanges;
        public IReadOnlyList<uint> GuideGroups => _data?.GuideGroups;
        public IReadOnlyList<float> GuideReach => _data?.GuideReach;
        public IReadOnlyList<Vector2Int> StrandGuideIds2 => _data?.StrandGuideIds2;
        public IReadOnlyList<Vector2> StrandGuideWeights2 => _data?.StrandGuideWeights2;
        public IReadOnlyList<Vector3> StrandRestRoots => _data?.StrandRestRoots;
        public IReadOnlyList<Quaternion> StrandRestFrames => _data?.StrandRestFrames;
        public IReadOnlyList<float> VertexArcParameters => _data?.VertexArcParameters;

        internal HairSimulationGuideBuildData Data => _data;
        private readonly HairSimulationGuideBuildData _data;
        private readonly ulong _mapHash;

        internal HairSimulationGuideMap(HairSimulationGuideBuildData data, ulong mapHash)
        {
            _data = data;
            _mapHash = mapHash;
        }
    }
}
