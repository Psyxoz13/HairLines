using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace HairLines
{
    public class HairConstraints
    {
        public uint[] StrandLocalIndex { get; private set; }
        public Vector2Int[] StrandNeighbours { get; private set; }
        public float[] EdgeRestLength { get; private set; }

        public uint[] ControlIndices { get; private set; }
        public Vector2Int[] InterpolationAnchors { get; private set; }
        public float[] InterpolationT { get; private set; }
        public Vector2Int[] ControlStrands { get; private set; }
        public int ControlCount => ControlIndices?.Length ?? 0;

        public const int InvalidControlSlot = -1;

        public const int ReconstructedAnchor = InvalidControlSlot;
        public int[] ControlSlotOfVertex { get; private set; }
        public Vector2Int[] InterpolationSlotAnchors { get; private set; }
        public uint[] XpbdSlotEdgeVertices { get; private set; }
        public float[] XpbdSlotEdgeRest { get; private set; }
        public uint[] XpbdSlotEdgeType { get; private set; }
        public int[] XpbdSlotIncidentStart { get; private set; }
        public uint[] XpbdSlotIncidentEdges { get; private set; }
        public int XpbdSlotLocalEdgeCount { get; private set; }
        public int XpbdSlotEdgeCount => XpbdSlotEdgeType?.Length ?? 0;
        public int[] XpbdSlotLocalIncidentStart { get; private set; }
        public uint[] XpbdSlotLocalIncidentEdges { get; private set; }
        public int CompactSlotCount => ControlCount;
        public bool CompactSlotMapValid { get; private set; }
        public bool HasCompactSlotGraph => CompactSlotMapValid;

        public int[] SimulationVertexSkipByGroup { get; private set; }

        public bool[] SimulatedStrands { get; private set; }
        public int SimulatedStrandCount { get; private set; }
        public bool HasReconstructedStrands => SimulatedStrands != null &&
                                                SimulatedStrandCount < SimulatedStrands.Length;

        public const int MaxFusedStrandSlots = 64;
        public const uint InvalidFusedStrand = 0xffffffffu;
        public uint[] FusedStrandBase { get; private set; }
        public float[] FusedLocalRest { get; private set; }
        public int FusedMaxStrandSlots { get; private set; }
        public bool HasFusedLocalGraph { get; private set; }
        private bool _fusedLocalGraphBuilt;
        private int _fusedStrandCount;

        public uint[] XpbdEdgeVertices { get; private set; }
        public float[] XpbdEdgeRest { get; private set; }
        public uint[] XpbdEdgeType { get; private set; }
        public int[] XpbdIncidentStart { get; private set; }
        public uint[] XpbdIncidentEdges { get; private set; }
        public int XpbdEdgeCount { get; private set; }

        public int XpbdLocalEdgeCount => XpbdEdgeCount;
        public int[] XpbdLocalIncidentStart => XpbdIncidentStart;
        public uint[] XpbdLocalIncidentEdges => XpbdIncidentEdges;

        public int StretchCount { get; private set; }
        public int BendCount { get; private set; }

        public const int TypeStretch = 0;
        public const int TypeBend = 1;

        private static readonly ProfilerMarker _buildMarker =
            new ProfilerMarker("HairLines.Constraints.Build");
        private static readonly ProfilerMarker _topologyMarker =
            new ProfilerMarker("HairLines.Constraints.ReadStrandTopology");

        public static HairConstraints Build(LinePrimitiveModel model, int pinnedRootVertices,
                                            bool bending)
        {
            return Build(model, pinnedRootVertices, bending, Matrix4x4.identity, true, 0, null);
        }

        public static HairConstraints Build(LinePrimitiveModel model, int pinnedRootVertices,
                                             bool bending,
                                             Matrix4x4 localToWorld, bool buildXpbd,
                                             int simulationVertexSkip,
                                             int[] simulationVertexSkipByGroup,
                                             bool[] simulatedStrands = null,
                                             bool[] pinnedVertices = null)
        {
            using ProfilerMarker.AutoScope scope = _buildMarker.Auto();
            if (model == null || model.Strands == null)
            {
                throw new ArgumentException("Model has no strand information");
            }

            int vertexCount = model.VertexCount;
            Vector3[] restPositions = TransformPositions(model.Positions, localToWorld);
            var result = new HairConstraints
            {
                StrandLocalIndex = new uint[vertexCount],
                StrandNeighbours = new Vector2Int[vertexCount],
                EdgeRestLength = new float[vertexCount],
                SimulationVertexSkipByGroup = simulationVertexSkipByGroup != null
                    ? (int[])simulationVertexSkipByGroup.Clone()
                    : null,
            };

            if (simulatedStrands != null && simulatedStrands.Length == model.Strands.Length)
            {
                result.SimulatedStrands = (bool[])simulatedStrands.Clone();
                int simulated = 0;
                for (int strand = 0; strand < simulatedStrands.Length; strand++)
                {
                    if (simulatedStrands[strand])
                    {
                        simulated++;
                    }
                }
                result.SimulatedStrandCount = simulated;
            }
            else
            {
                simulatedStrands = null;
                result.SimulatedStrands = null;
                result.SimulatedStrandCount = model.Strands.Length;
            }

            var strandOfVertex = new int[vertexCount];
            var restArcLength = new float[vertexCount];
            using (ProfilerMarker.AutoScope topologyScope = _topologyMarker.Auto())
            {
                result.ReadStrandTopology(model, restPositions, pinnedRootVertices,
                                           strandOfVertex, restArcLength);
            }

            result.BuildControlMapping(model, restPositions, pinnedRootVertices,
                                       simulationVertexSkip, simulationVertexSkipByGroup,
                                       simulatedStrands, pinnedVertices);

            if (buildXpbd)
            {
                result.BuildXpbdEdges(model, restPositions, bending, result.ControlStrands,
                                      result.ControlIndices, restArcLength,
                                      strandOfVertex);
            }
            else
            {
                result.XpbdEdgeVertices = Array.Empty<uint>();
                result.XpbdEdgeRest = Array.Empty<float>();
                result.XpbdEdgeType = Array.Empty<uint>();
                result.XpbdIncidentStart = new int[vertexCount + 1];
                result.XpbdIncidentEdges = Array.Empty<uint>();
            }

            result.BuildCompactSlotGraph(vertexCount, model);
            result.ReleaseRenderVertexGraph();
            result._fusedStrandCount = model.Strands.Length;
            return result;
        }

        private void BuildXpbdEdges(LinePrimitiveModel model, Vector3[] restPositions,
                                    bool bending,
                                    Vector2Int[] controlStrands, uint[] controlIndices,
                                    float[] restArcLength,
                                    int[] strandOfVertex)
        {
            var vertices = new List<uint>();
            var rest = new List<float>();
            var types = new List<uint>();
            var edgeByKey = new Dictionary<ulong, int>();

            ForEachSpring(model, bending, controlStrands, controlIndices,
                          (a, b, type) =>
            {
                if (a == b)
                {
                    return;
                }

                int first = Mathf.Min(a, b);
                int second = Mathf.Max(a, b);
                ulong key = ((ulong)(uint)type << 58) |
                            ((ulong)(uint)first << 29) |
                            (uint)second;
                if (edgeByKey.ContainsKey(key))
                {
                    return;
                }

                edgeByKey.Add(key, types.Count);
                vertices.Add((uint)first);
                vertices.Add((uint)second);
                rest.Add(ComputeRestLength(first, second, type, restPositions,
                                           restArcLength, strandOfVertex));
                types.Add((uint)type);
            });

            XpbdEdgeVertices = vertices.ToArray();
            XpbdEdgeRest = rest.ToArray();
            XpbdEdgeType = types.ToArray();
            XpbdEdgeCount = XpbdEdgeType.Length;

            StretchCount = 0;
            BendCount = 0;
            for (int edge = 0; edge < XpbdEdgeType.Length; edge++)
            {
                switch (XpbdEdgeType[edge])
                {
                    case TypeStretch:
                        StretchCount++;
                        break;

                    default:
                        BendCount++;
                        break;
                }
            }

            int vertexCount = model.VertexCount;
            int[] allStarts;
            uint[] allIncident;
            BuildIncidentGraph(XpbdEdgeVertices, vertexCount, 0, XpbdEdgeCount,
                               out allStarts, out allIncident);
            XpbdIncidentStart = allStarts;
            XpbdIncidentEdges = allIncident;
        }

        private void BuildCompactSlotGraph(int vertexCount, LinePrimitiveModel model)
        {
            int safeVertexCount = Mathf.Max(0, vertexCount);
            int controlCount = ControlCount;
            ControlSlotOfVertex = new int[safeVertexCount];
            for (int vertex = 0; vertex < ControlSlotOfVertex.Length; vertex++)
            {
                ControlSlotOfVertex[vertex] = InvalidControlSlot;
            }

            bool valid = true;
            for (int slot = 0; slot < controlCount; slot++)
            {
                uint fullVertex = ControlIndices[slot];
                if (fullVertex >= (uint)safeVertexCount ||
                    ControlSlotOfVertex[fullVertex] != InvalidControlSlot)
                {
                    valid = false;
                    continue;
                }

                ControlSlotOfVertex[fullVertex] = slot;
            }

            InterpolationSlotAnchors = new Vector2Int[safeVertexCount];
            for (int vertex = 0; vertex < safeVertexCount; vertex++)
            {
                Vector2Int fullAnchors = InterpolationAnchors != null &&
                                         vertex < InterpolationAnchors.Length
                    ? InterpolationAnchors[vertex]
                    : new Vector2Int(vertex, vertex);

                if (fullAnchors.x == ReconstructedAnchor &&
                    fullAnchors.y == ReconstructedAnchor)
                {
                    InterpolationSlotAnchors[vertex] = new Vector2Int(ReconstructedAnchor,
                                                                       ReconstructedAnchor);
                    continue;
                }

                int first = MapVertexToSlot(fullAnchors.x);
                int second = MapVertexToSlot(fullAnchors.y);
                InterpolationSlotAnchors[vertex] = new Vector2Int(first, second);

                if (first == InvalidControlSlot || second == InvalidControlSlot)
                {
                    valid = false;
                }
            }

            valid &= ReconstructedAnchorsMatchMask(model);
            BuildSlotXpbdGraph(controlCount, ref valid);
            CompactSlotMapValid = valid;
        }

        private void ReleaseRenderVertexGraph()
        {
            XpbdEdgeVertices = Array.Empty<uint>();
            XpbdEdgeRest = Array.Empty<float>();
            XpbdEdgeType = Array.Empty<uint>();
            XpbdIncidentStart = Array.Empty<int>();
            XpbdIncidentEdges = Array.Empty<uint>();
            InterpolationAnchors = Array.Empty<Vector2Int>();
        }

        public bool EnsureFusedLocalGraph()
        {
            if (_fusedLocalGraphBuilt)
            {
                return HasFusedLocalGraph;
            }

            _fusedLocalGraphBuilt = true;
            HasFusedLocalGraph = false;
            FusedMaxStrandSlots = 0;
            FusedLocalRest = Array.Empty<float>();
            FusedStrandBase = new uint[Mathf.Max(0, _fusedStrandCount)];
            for (int strand = 0; strand < FusedStrandBase.Length; strand++)
            {
                FusedStrandBase[strand] = InvalidFusedStrand;
            }

            if (!CompactSlotMapValid || ControlStrands == null ||
                XpbdSlotEdgeVertices == null || XpbdSlotLocalEdgeCount <= 0 ||
                FusedStrandBase.Length == 0)
            {
                return false;
            }

            long canonicalRows = 0;
            for (int strand = 0; strand < FusedStrandBase.Length; strand++)
            {
                int slots = ControlStrands[strand].y;
                if (slots <= 0)
                {
                    continue;
                }

                if (slots > MaxFusedStrandSlots)
                {
                    return FailFusedLocalGraph();
                }

                canonicalRows += (slots - 1) + Mathf.Max(0, slots - 2);
            }

            if (canonicalRows <= 0 || canonicalRows > int.MaxValue / 4)
            {
                return FailFusedLocalGraph();
            }

            var rest = new float[canonicalRows];
            int cursor = 0;
            int written = 0;
            int maxSlots = 0;

            for (int strand = 0; strand < FusedStrandBase.Length; strand++)
            {
                Vector2Int range = ControlStrands[strand];
                int slotStart = range.x;
                int slotCount = range.y;
                if (slotCount <= 0)
                {
                    continue;
                }

                if (slotCount > MaxFusedStrandSlots)
                {
                    return FailFusedLocalGraph();
                }

                int stretchRows = slotCount - 1;
                int bendRows = Mathf.Max(0, slotCount - 2);
                if (written + stretchRows + bendRows > rest.Length)
                {
                    return FailFusedLocalGraph();
                }

                maxSlots = Mathf.Max(maxSlots, slotCount);
                FusedStrandBase[strand] = (uint)written;

                for (int j = 0; j < stretchRows; j++)
                {
                    rest[written++] = TakeFusedRow(ref cursor, slotStart + j, slotStart + j + 1,
                                                    TypeStretch, out bool failed);
                    if (failed)
                    {
                        return FailFusedLocalGraph();
                    }
                }

                for (int j = 0; j < bendRows; j++)
                {
                    rest[written++] = TakeFusedRow(ref cursor, slotStart + j, slotStart + j + 2,
                                                    TypeBend, out bool failed);
                    if (failed)
                    {
                        return FailFusedLocalGraph();
                    }
                }
            }

            if (cursor != XpbdSlotLocalEdgeCount)
            {
                return FailFusedLocalGraph();
            }

            if (written < rest.Length)
            {
                Array.Resize(ref rest, written);
            }

            FusedLocalRest = rest;
            FusedMaxStrandSlots = maxSlots;
            HasFusedLocalGraph = written > 0 && maxSlots > 0;
            return HasFusedLocalGraph;
        }

        private float TakeFusedRow(ref int cursor, int slotA, int slotB, int expectedType,
                                   out bool failed)
        {
            failed = false;
            if (cursor >= XpbdSlotLocalEdgeCount)
            {
                return -1f;
            }

            int first = (int)XpbdSlotEdgeVertices[cursor * 2];
            int second = (int)XpbdSlotEdgeVertices[cursor * 2 + 1];
            if (Mathf.Min(first, second) != Mathf.Min(slotA, slotB) ||
                Mathf.Max(first, second) != Mathf.Max(slotA, slotB))
            {
                return -1f;
            }

            if (XpbdSlotEdgeType[cursor] != (uint)expectedType)
            {
                failed = true;
                return -1f;
            }

            return XpbdSlotEdgeRest[cursor++];
        }

        private bool FailFusedLocalGraph()
        {
            FusedLocalRest = Array.Empty<float>();
            FusedMaxStrandSlots = 0;
            HasFusedLocalGraph = false;
            for (int strand = 0; strand < FusedStrandBase.Length; strand++)
            {
                FusedStrandBase[strand] = InvalidFusedStrand;
            }

            return false;
        }

        private bool ReconstructedAnchorsMatchMask(LinePrimitiveModel model)
        {
            if (model?.Strands == null || InterpolationSlotAnchors == null)
            {
                return true;
            }

            int vertexCount = InterpolationSlotAnchors.Length;
            for (int strand = 0; strand < model.Strands.Length; strand++)
            {
                bool simulated = SimulatedStrands == null ||
                                 (strand < SimulatedStrands.Length && SimulatedStrands[strand]);
                Vector2Int range = model.Strands[strand];
                if (range.x < 0 || range.y < 0 || range.x > vertexCount ||
                    range.y > vertexCount - range.x)
                {
                    return false;
                }

                for (int local = 0; local < range.y; local++)
                {
                    bool reconstructed =
                        InterpolationSlotAnchors[range.x + local].x == ReconstructedAnchor;
                    if (reconstructed == simulated)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private int MapVertexToSlot(int vertex)
        {
            return vertex >= 0 && ControlSlotOfVertex != null &&
                   vertex < ControlSlotOfVertex.Length
                ? ControlSlotOfVertex[vertex]
                : InvalidControlSlot;
        }

        private void BuildSlotXpbdGraph(int controlCount, ref bool valid)
        {
            var edgeVertices = new List<uint>();
            var edgeRest = new List<float>();
            var edgeTypes = new List<uint>();
            int localEdgeCount = 0;

            int sourceEdgeCount = XpbdEdgeCount;
            for (int edge = 0; edge < sourceEdgeCount; edge++)
            {
                int first = edge * 2 < XpbdEdgeVertices.Length
                    ? MapVertexToSlot((int)XpbdEdgeVertices[edge * 2])
                    : InvalidControlSlot;
                int second = edge * 2 + 1 < XpbdEdgeVertices.Length
                    ? MapVertexToSlot((int)XpbdEdgeVertices[edge * 2 + 1])
                    : InvalidControlSlot;
                if (first == InvalidControlSlot || second == InvalidControlSlot)
                {
                    valid = false;
                    continue;
                }

                edgeVertices.Add((uint)first);
                edgeVertices.Add((uint)second);
                edgeRest.Add(edge < XpbdEdgeRest.Length ? XpbdEdgeRest[edge] : 0f);
                edgeTypes.Add(edge < XpbdEdgeType.Length ? XpbdEdgeType[edge] : 0u);
                localEdgeCount++;
            }

            XpbdSlotEdgeVertices = edgeVertices.ToArray();
            XpbdSlotEdgeRest = edgeRest.ToArray();
            XpbdSlotEdgeType = edgeTypes.ToArray();
            XpbdSlotLocalEdgeCount = localEdgeCount;

            BuildIncidentGraph(XpbdSlotEdgeVertices, controlCount, 0,
                               XpbdSlotEdgeCount, out int[] allStarts,
                               out uint[] allIncident);
            XpbdSlotIncidentStart = allStarts;
            XpbdSlotIncidentEdges = allIncident;

            BuildIncidentGraph(XpbdSlotEdgeVertices, controlCount, 0,
                               XpbdSlotLocalEdgeCount, out int[] localStarts,
                               out uint[] localIncident);
            XpbdSlotLocalIncidentStart = localStarts;
            XpbdSlotLocalIncidentEdges = localIncident;
        }

        private static int[] PrefixSum(int[] degree)
        {
            var starts = new int[degree.Length + 1];
            for (int i = 0; i < degree.Length; i++)
            {
                starts[i + 1] = starts[i] + Mathf.Max(0, degree[i]);
            }

            return starts;
        }

        public bool ValidateCompactSlotMap(out string error)
        {
            error = null;
            if (!CompactSlotMapValid)
            {
                error = "compact slot map was marked invalid during build";
                return false;
            }

            int controlCount = ControlCount;
            if (ControlSlotOfVertex == null ||
                InterpolationSlotAnchors == null ||
                ControlSlotOfVertex.Length != InterpolationSlotAnchors.Length)
            {
                error = "compact vertex-to-slot maps have inconsistent lengths";
                return false;
            }

            for (int slot = 0; slot < controlCount; slot++)
            {
                uint fullVertex = ControlIndices[slot];
                if (fullVertex >= (uint)ControlSlotOfVertex.Length ||
                    ControlSlotOfVertex[fullVertex] != slot)
                {
                    error = "control slot is not a bijection with ControlIndices";
                    return false;
                }
            }

            if (!ValidateCsr(XpbdSlotIncidentStart, XpbdSlotIncidentEdges.Length,
                             controlCount, out error) ||
                !ValidateCsr(XpbdSlotLocalIncidentStart,
                             XpbdSlotLocalIncidentEdges.Length,
                             controlCount, out error))
            {
                return false;
            }

            for (int vertex = 0; vertex < InterpolationSlotAnchors.Length; vertex++)
            {
                Vector2Int anchors = InterpolationSlotAnchors[vertex];
                if (anchors.x == ReconstructedAnchor && anchors.y == ReconstructedAnchor)
                {
                    continue;
                }

                if (anchors.x < 0 || anchors.x >= controlCount ||
                    anchors.y < 0 || anchors.y >= controlCount)
                {
                    error = "interpolation anchor points outside compact slot domain";
                    return false;
                }
            }

            if (XpbdSlotLocalEdgeCount < 0 ||
                XpbdSlotLocalEdgeCount > XpbdSlotEdgeCount ||
                XpbdSlotLocalEdgeCount != XpbdSlotEdgeCount ||
                XpbdSlotEdgeVertices.Length != XpbdSlotEdgeCount * 2)
            {
                error = "XPBD compact edge ranges are inconsistent";
                return false;
            }

            for (int endpoint = 0; endpoint < XpbdSlotEdgeVertices.Length; endpoint++)
            {
                if (XpbdSlotEdgeVertices[endpoint] >= (uint)controlCount)
                {
                    error = "XPBD compact edge endpoint outside slot domain";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateCsr(int[] starts, int entryCount,
                                       int vertexCount, out string error)
        {
            error = null;
            if (starts == null || starts.Length != vertexCount + 1 ||
                starts[0] != 0 || starts[vertexCount] != entryCount)
            {
                error = "compact CSR start array has invalid bounds";
                return false;
            }

            for (int i = 0; i < vertexCount; i++)
            {
                if (starts[i] > starts[i + 1])
                {
                    error = "compact CSR starts are not monotonic";
                    return false;
                }
            }

            return true;
        }

        private static void BuildIncidentGraph(uint[] edgeVertices, int vertexCount,
                                               int edgeStart, int edgeCount,
                                               out int[] starts, out uint[] incident)
        {
            var degree = new int[vertexCount];
            int edgeEnd = edgeStart + edgeCount;
            for (int edge = edgeStart; edge < edgeEnd; edge++)
            {
                degree[edgeVertices[edge * 2]]++;
                degree[edgeVertices[edge * 2 + 1]]++;
            }

            starts = new int[vertexCount + 1];
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                starts[vertex + 1] = starts[vertex] + degree[vertex];
            }

            incident = new uint[starts[vertexCount]];
            var cursor = new int[vertexCount];
            Array.Copy(starts, cursor, vertexCount);
            for (int edge = edgeStart; edge < edgeEnd; edge++)
            {
                int first = (int)edgeVertices[edge * 2];
                int second = (int)edgeVertices[edge * 2 + 1];
                incident[cursor[first]++] = (uint)edge;
                incident[cursor[second]++] = (uint)edge | 0x80000000u;
            }
        }

        private void ReadStrandTopology(LinePrimitiveModel model, Vector3[] restPositions,
                                        int pinnedRootVertices, int[] strandOfVertex,
                                        float[] restArcLength)
        {
            for (int s = 0; s < model.Strands.Length; s++)
            {
                int start = model.Strands[s].x;
                int count = model.Strands[s].y;
                int pins = count > 1 ? Mathf.Clamp(pinnedRootVertices, 1, count - 1) : 1;
                if (count > 0)
                {
                    restArcLength[start] = 0f;
                }

                for (int i = 0; i < count; i++)
                {
                    int vertex = start + i;
                    strandOfVertex[vertex] = s;
                    StrandLocalIndex[vertex] = (uint)i;
                    StrandNeighbours[vertex] = new Vector2Int(
                        start + Mathf.Max(i - 1, 0),
                        start + Mathf.Min(i + 1, count - 1));

                    if (i + 1 < count)
                    {
                        float segmentLength = Vector3.Distance(restPositions[vertex],
                                                               restPositions[vertex + 1]);
                        EdgeRestLength[vertex] = segmentLength;
                        restArcLength[vertex + 1] = restArcLength[vertex] + segmentLength;
                    }
                    else
                    {
                        EdgeRestLength[vertex] = 0f;
                    }
                }
            }
        }

        private void BuildControlMapping(LinePrimitiveModel model, Vector3[] restPositions,
                                         int pinnedRootVertices, int simulationVertexSkip,
                                         int[] simulationVertexSkipByGroup,
                                         bool[] simulatedStrands, bool[] pinnedVertices)
        {
            int vertexCount = model.VertexCount;
            var controls = new List<uint>();
            var ranges = new Vector2Int[model.Strands.Length];
            var anchors = new Vector2Int[vertexCount];
            var interpolation = new float[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                anchors[i] = new Vector2Int(i, i);
            }

            for (int strand = 0; strand < model.Strands.Length; strand++)
            {
                int start = model.Strands[strand].x;
                int count = Mathf.Max(0, model.Strands[strand].y);
                int offset = controls.Count;

                if (simulatedStrands != null && !simulatedStrands[strand])
                {
                    ranges[strand] = new Vector2Int(offset, 0);
                    for (int local = 0; local < count; local++)
                    {
                        anchors[start + local] = new Vector2Int(ReconstructedAnchor,
                                                                 ReconstructedAnchor);
                        interpolation[start + local] = 0f;
                    }
                    continue;
                }

                int pins = count > 1 ? Mathf.Clamp(pinnedRootVertices, 1, count - 1) : count;
                int fallbackStride = Mathf.Clamp(simulationVertexSkip, 0, 4) + 1;
                var localControls = new List<int>(Mathf.Max(1, count / fallbackStride + 2));
                int groupPhase = 0;

                for (int local = 0; local < count; local++)
                {
                    uint group = model.VertexGroups != null &&
                                 start + local < model.VertexGroups.Length
                        ? model.VertexGroups[start + local]
                        : 0u;
                    uint previousGroup = local > 0 && model.VertexGroups != null &&
                                         start + local - 1 < model.VertexGroups.Length
                        ? model.VertexGroups[start + local - 1]
                        : group;
                    bool groupBoundary = local > 0 && group != previousGroup;
                    uint nextGroup = local + 1 < count && model.VertexGroups != null &&
                                     start + local + 1 < model.VertexGroups.Length
                        ? model.VertexGroups[start + local + 1]
                        : group;
                    bool nextGroupBoundary = local + 1 < count && nextGroup != group;
                    if (local == 0 || groupBoundary)
                    {
                        groupPhase = 0;
                    }

                    int groupSkip = simulationVertexSkipByGroup != null &&
                                    group < (uint)simulationVertexSkipByGroup.Length
                        ? Mathf.Clamp(simulationVertexSkipByGroup[group], 0, 4)
                        : Mathf.Clamp(simulationVertexSkip, 0, 4);
                    int stride = groupSkip + 1;
                    bool control = groupSkip <= 0 || local < pins ||
                                   local == count - 1 || groupBoundary || nextGroupBoundary ||
                                   (local >= pins && (groupPhase % stride) == 0) ||
                                   (pinnedVertices != null && pinnedVertices[start + local]);
                    if (!control)
                    {
                        if (local >= pins)
                        {
                            groupPhase++;
                        }
                        continue;
                    }

                    localControls.Add(local);
                    controls.Add((uint)(start + local));
                    if (local >= pins)
                    {
                        groupPhase++;
                    }
                }

                if (count > 0 && localControls.Count == 0)
                {
                    localControls.Add(0);
                    controls.Add((uint)start);
                }

                ranges[strand] = new Vector2Int(offset, localControls.Count);

                if (localControls.Count == 0)
                {
                    continue;
                }

                var arc = new float[count];
                for (int local = 1; local < count; local++)
                {
                    arc[local] = arc[local - 1] +
                                 Vector3.Distance(restPositions[start + local - 1],
                                                  restPositions[start + local]);
                }

                int previousControl = 0;
                for (int cursor = 0; cursor < localControls.Count; cursor++)
                {
                    int controlLocal = localControls[cursor];
                    for (int local = previousControl; local <= controlLocal; local++)
                    {
                        int vertex = start + local;
                        anchors[vertex] = new Vector2Int(start + controlLocal,
                                                         start + controlLocal);
                        interpolation[vertex] = 0f;
                    }
                    previousControl = controlLocal + 1;
                }

                for (int cursor = 0; cursor + 1 < localControls.Count; cursor++)
                {
                    int first = localControls[cursor];
                    int second = localControls[cursor + 1];
                    float denominator = arc[second] - arc[first];
                    for (int local = first + 1; local < second; local++)
                    {
                        float t = denominator > 1e-8f
                            ? (arc[local] - arc[first]) / denominator
                            : (local - first) / (float)Mathf.Max(1, second - first);
                        int vertex = start + local;
                        anchors[vertex] = new Vector2Int(start + first, start + second);
                        interpolation[vertex] = Mathf.Clamp01(t);
                    }
                }
            }

            ControlIndices = controls.ToArray();
            InterpolationAnchors = anchors;
            InterpolationT = interpolation;
            ControlStrands = ranges;
        }

        private static void ForEachSpring(LinePrimitiveModel model, bool bending,
                                  Vector2Int[] controlStrands, uint[] controlIndices,
                                  Action<int, int, int> visit)
        {
            for (int s = 0; s < model.Strands.Length; s++)
            {
                Vector2Int controls = controlStrands != null && s < controlStrands.Length
                    ? controlStrands[s]
                    : new Vector2Int(0, 0);
                int start = controls.x;
                int count = controls.y;

                for (int i = 0; i + 1 < count; i++)
                {
                    visit((int)controlIndices[start + i],
                          (int)controlIndices[start + i + 1], TypeStretch);
                }

                if (!bending)
                {
                    continue;
                }

                for (int i = 0; i + 2 < count; i++)
                {
                    visit((int)controlIndices[start + i],
                          (int)controlIndices[start + i + 2], TypeBend);
                }
            }
        }

        private static float ComputeRestLength(int a, int b, int type,
                                               Vector3[] restPositions,
                                               float[] restArcLength,
                                               int[] strandOfVertex)
        {
            if (type == TypeStretch && strandOfVertex != null &&
                a >= 0 && b >= 0 && a < strandOfVertex.Length && b < strandOfVertex.Length &&
                strandOfVertex[a] == strandOfVertex[b])
            {
                return Mathf.Abs(restArcLength[b] - restArcLength[a]);
            }

            return Vector3.Distance(restPositions[a], restPositions[b]);
        }

        private static Vector3[] TransformPositions(Vector3[] positions, Matrix4x4 localToWorld)
        {
            if (positions == null || localToWorld == Matrix4x4.identity)
            {
                return positions;
            }

            Vector3[] transformed = new Vector3[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                transformed[i] = localToWorld.MultiplyPoint3x4(positions[i]);
            }

            return transformed;
        }
    }
}
