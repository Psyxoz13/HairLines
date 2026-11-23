using System;
using System.Collections.Generic;
using UnityEngine;

namespace HairLines
{
    public static class HairSimulationGuideSelectionBuilder
    {
        private const int MaxGroups = 16;
        private const float MetricPositionWeight = 1f;
        private const float MetricTangentWeight = 0.35f;
        private const float MetricCurvatureWeight = 0.15f;

        public static bool TryBuild(LinePrimitiveModel source, float guideFraction,
                                    out HairSimulationGuideBuildData data, out string error)
        {
            data = null;
            if (source == null || source.IsEmpty || source.Strands == null ||
                source.Positions == null)
            {
                error = "The source groom is empty.";
                return false;
            }

            guideFraction = Mathf.Clamp01(guideFraction);
            if (!(guideFraction > 0f))
            {
                error = "Guide fraction must be greater than zero.";
                return false;
            }

            int strandCount = source.StrandCount;
            int vertexCount = source.VertexCount;
            var signatures = new StrandSignature[strandCount];
            var strandsByGroup = new List<int>[MaxGroups];
            var allGroupStrands = new List<int>[MaxGroups];

            for (int strand = 0; strand < strandCount; strand++)
            {
                Vector2Int range = source.Strands[strand];
                if (range.y <= 0 || range.x < 0 || range.x > vertexCount ||
                    range.y > vertexCount - range.x)
                {
                    error = $"Source strand {strand} has an invalid vertex range.";
                    return false;
                }

                uint group = GroupOfStrand(source, range);
                if (!RangeHasGroup(source, range, group))
                {
                    error = $"Source strand {strand} crosses a group/material boundary.";
                    return false;
                }

                int groupIndex = (int)group;
                allGroupStrands[groupIndex] ??= new List<int>();
                allGroupStrands[groupIndex].Add(strand);
                signatures[strand] = BuildSignature(source.Positions, range,
                                                     source.LocalBounds.size.magnitude,
                                                     (int)group);
            }

            var selectedSourceStrands = new List<int>(Mathf.Max(1,
                                                                  Mathf.CeilToInt(strandCount * guideFraction)));
            for (int group = 0; group < MaxGroups; group++)
            {
                List<int> candidates = allGroupStrands[group];
                if (candidates == null || candidates.Count == 0)
                {
                    continue;
                }

                int wanted = guideFraction >= 0.999999f
                    ? candidates.Count
                    : Mathf.Clamp(Mathf.CeilToInt(candidates.Count * guideFraction), 1,
                                  candidates.Count);
                List<int> selected = SelectStratified(candidates, signatures, wanted);
                strandsByGroup[group] = selected;
                selectedSourceStrands.AddRange(selected);
            }

            if (selectedSourceStrands.Count == 0)
            {
                error = "Guide fraction produced no guide strands.";
                return false;
            }

            int guideCount = selectedSourceStrands.Count;
            var guideSourceStrands = selectedSourceStrands.ToArray();
            var guideRanges = new Vector2Int[guideCount];
            var guideGroups = new uint[guideCount];
            var guideReach = new float[guideCount];
            var strandGuideIds = new Vector2Int[strandCount];
            var strandGuideWeights = new Vector2[strandCount];
            var restRoots = new Vector3[strandCount];
            var restFrames = new Quaternion[strandCount];
            var arcParameters = new float[vertexCount];
            var guideIndexOfSource = new int[strandCount];
            for (int strand = 0; strand < guideIndexOfSource.Length; strand++)
            {
                guideIndexOfSource[strand] = -1;
            }

            for (int guide = 0; guide < guideCount; guide++)
            {
                int sourceStrand = guideSourceStrands[guide];
                guideIndexOfSource[sourceStrand] = guide;
                Vector2Int range = source.Strands[sourceStrand];
                guideRanges[guide] = range;
                guideGroups[guide] = GroupOfStrand(source, range);
                restRoots[sourceStrand] = source.Positions[range.x];
                restFrames[sourceStrand] = BuildRestFrame(source.Positions, range);
                HairSimulationGuideAsset.FillArcParameters(source.Positions, range,
                                                           arcParameters);
            }

            for (int strand = 0; strand < strandCount; strand++)
            {
                Vector2Int range = source.Strands[strand];
                restRoots[strand] = source.Positions[range.x];
                restFrames[strand] = BuildRestFrame(source.Positions, range);
                HairSimulationGuideAsset.FillArcParameters(source.Positions, range,
                                                           arcParameters);
            }

            var grid = BuildGuideGrid(source, signatures, strandsByGroup, guideIndexOfSource,
                                      source.LocalBounds);
            for (int strand = 0; strand < strandCount; strand++)
            {
                int ownGuide = guideIndexOfSource[strand];
                if (ownGuide >= 0)
                {
                    strandGuideIds[strand] = new Vector2Int(ownGuide, ownGuide);
                    strandGuideWeights[strand] = new Vector2(1f, 0f);
                    continue;
                }

                uint group = GroupOfStrand(source, source.Strands[strand]);
                List<int> sameGroupGuides = strandsByGroup[(int)group];
                FindNearestTwo(strand, signatures, sameGroupGuides, guideIndexOfSource,
                               grid, out int first, out int second,
                               out float firstDistance, out float secondDistance);
                if (first < 0)
                {
                    error = $"Unable to assign a same-group guide to strand {strand}.";
                    return false;
                }

                if (second < 0 || sameGroupGuides.Count < 2)
                {
                    second = first;
                    firstDistance = 0f;
                    secondDistance = 0f;
                    strandGuideWeights[strand] = new Vector2(1f, 0f);
                }
                else
                {
                    float firstWeight = 1f / Mathf.Max(firstDistance * firstDistance, 1e-8f);
                    float secondWeight = 1f / Mathf.Max(secondDistance * secondDistance, 1e-8f);
                    float total = firstWeight + secondWeight;
                    strandGuideWeights[strand] = new Vector2(firstWeight / total,
                                                              secondWeight / total);
                }

                strandGuideIds[strand] = new Vector2Int(first, second);
                Vector3 root = restRoots[strand];
                Vector3 firstRoot = restRoots[guideSourceStrands[first]];
                guideReach[first] = Mathf.Max(guideReach[first],
                                              Vector3.Distance(root, firstRoot));
                if (second != first)
                {
                    Vector3 secondRoot = restRoots[guideSourceStrands[second]];
                    guideReach[second] = Mathf.Max(guideReach[second],
                                                   Vector3.Distance(root, secondRoot));
                }
            }

            data = new HairSimulationGuideBuildData(
                guideSourceStrands, guideRanges, guideGroups, guideReach,
                strandGuideIds, strandGuideWeights, restRoots, restFrames, arcParameters);
            error = null;
            return true;
        }

        private static List<int> SelectStratified(List<int> candidates,
                                                   StrandSignature[] signatures, int wanted)
        {
            var selected = new List<int>(wanted);
            if (wanted >= candidates.Count)
            {
                selected.AddRange(candidates);
                return selected;
            }

            for (int slot = 0; slot < wanted; slot++)
            {
                int from = (int)((long)slot * candidates.Count / wanted);
                int to = (int)((long)(slot + 1) * candidates.Count / wanted);
                to = Mathf.Max(to, from + 1);
                to = Mathf.Min(to, candidates.Count);

                int best = candidates[from];
                float bestScore = signatures[best].Curvature;
                for (int i = from + 1; i < to; i++)
                {
                    int candidate = candidates[i];
                    float score = signatures[candidate].Curvature;
                    if (score > bestScore + 1e-8f ||
                        (Mathf.Abs(score - bestScore) <= 1e-8f && candidate < best))
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }
                selected.Add(best);
            }
            return selected;
        }

        private static GuideGrid BuildGuideGrid(LinePrimitiveModel source,
                                                StrandSignature[] signatures,
                                                List<int>[] strandsByGroup,
                                                int[] guideIndexOfSource,
                                                Bounds bounds)
        {
            int totalGuides = 0;
            for (int group = 0; group < strandsByGroup.Length; group++)
            {
                totalGuides += strandsByGroup[group]?.Count ?? 0;
            }

            float diagonal = Mathf.Max(bounds.size.magnitude, 1e-4f);
            float cellSize = Mathf.Max(diagonal / Mathf.Pow(Mathf.Max(1, totalGuides), 1f / 3f),
                                       1e-5f);
            var grid = new GuideGrid(cellSize);
            for (int group = 0; group < strandsByGroup.Length; group++)
            {
                List<int> selected = strandsByGroup[group];
                if (selected == null)
                {
                    continue;
                }
                for (int i = 0; i < selected.Count; i++)
                {
                    int sourceStrand = selected[i];
                    Vector3 point = signatures[sourceStrand].Root;
                    grid.Add(group, point, guideIndexOfSource[sourceStrand], sourceStrand);
                }
            }
            return grid;
        }

        private static void FindNearestTwo(int strand, StrandSignature[] signatures,
                                           List<int> sameGroupSourceStrands,
                                           int[] guideIndexOfSource, GuideGrid grid,
                                           out int firstGuide, out int secondGuide,
                                           out float firstDistance, out float secondDistance)
        {
            firstGuide = -1;
            secondGuide = -1;
            firstDistance = float.MaxValue;
            secondDistance = float.MaxValue;
            if (sameGroupSourceStrands == null || sameGroupSourceStrands.Count == 0)
            {
                return;
            }

            int group = signatures[strand].Group;
            var candidates = new List<int>(32);
            grid.Collect(group, signatures[strand].Root, 1, candidates);
            if (candidates.Count < 2)
            {
                grid.Collect(group, signatures[strand].Root, 2, candidates);
            }

            if (candidates.Count < 2 && sameGroupSourceStrands.Count > 1)
            {
                candidates.Clear();
                for (int i = 0; i < sameGroupSourceStrands.Count; i++)
                {
                    candidates.Add(guideIndexOfSource[sameGroupSourceStrands[i]]);
                }
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                int guide = candidates[i];
                if (guide < 0)
                {
                    continue;
                }

                int guideSource = grid.SourceStrandOfGuide(guide);
                if (guideSource < 0)
                {
                    continue;
                }
                float distance = Metric(signatures[strand], signatures[guideSource]);
                if (distance < firstDistance - 1e-8f ||
                    (Mathf.Abs(distance - firstDistance) <= 1e-8f && guide < firstGuide))
                {
                    secondGuide = firstGuide;
                    secondDistance = firstDistance;
                    firstGuide = guide;
                    firstDistance = distance;
                }
                else if (guide != firstGuide &&
                         (distance < secondDistance - 1e-8f ||
                          (Mathf.Abs(distance - secondDistance) <= 1e-8f && guide < secondGuide)))
                {
                    secondGuide = guide;
                    secondDistance = distance;
                }
            }
        }

        private static float Metric(in StrandSignature a, in StrandSignature b)
        {
            float scale = Mathf.Max(Mathf.Max(a.BoundsScale, b.BoundsScale), 1e-4f);
            float position = (a.Root - b.Root).magnitude / scale * MetricPositionWeight;
            float tangent = 1f - Mathf.Clamp(Vector3.Dot(a.Tangent, b.Tangent), -1f, 1f);
            float curvature = Mathf.Abs(a.Curvature - b.Curvature);
            return position + MetricTangentWeight * tangent + MetricCurvatureWeight * curvature;
        }

        private static StrandSignature BuildSignature(Vector3[] positions, Vector2Int range,
                                                       float boundsScale, int group)
        {
            Vector3 root = positions[range.x];
            Vector3 tangent = Vector3.forward;
            float curvature = 0f;
            Vector3 previousDirection = Vector3.zero;
            for (int local = 1; local < range.y; local++)
            {
                Vector3 direction = positions[range.x + local] - positions[range.x + local - 1];
                if (direction.sqrMagnitude <= 1e-12f)
                {
                    continue;
                }
                direction.Normalize();
                if (local == 1)
                {
                    tangent = direction;
                }
                if (previousDirection.sqrMagnitude > 0f)
                {
                    curvature += 1f - Mathf.Clamp(Vector3.Dot(previousDirection, direction), -1f, 1f);
                }
                previousDirection = direction;
            }

            curvature /= Mathf.Max(1, range.y - 2);
            return new StrandSignature(root, tangent, curvature,
                                       Mathf.Max(boundsScale, 1e-4f), group);
        }

        private static Quaternion BuildRestFrame(Vector3[] positions, Vector2Int range)
        {
            Vector3 tangent = Vector3.forward;
            for (int local = 1; local < range.y; local++)
            {
                Vector3 candidate = positions[range.x + local] - positions[range.x];
                if (candidate.sqrMagnitude > 1e-12f)
                {
                    tangent = candidate.normalized;
                    break;
                }
            }

            Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) < 0.95f
                ? Vector3.up
                : Vector3.right;
            Vector3 right = Vector3.Cross(reference, tangent).normalized;
            Vector3 normal = Vector3.Cross(tangent, right).normalized;
            return Quaternion.LookRotation(tangent, normal);
        }

        private static uint GroupOfStrand(LinePrimitiveModel source, Vector2Int range)
        {
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
            for (int vertex = range.x; vertex < range.x + range.y; vertex++)
            {
                uint value = source.VertexGroups[vertex] > 15u ? 15u : source.VertexGroups[vertex];
                if (value != group)
                {
                    return false;
                }
            }
            return true;
        }

        private readonly struct StrandSignature
        {
            public readonly Vector3 Root;
            public readonly Vector3 Tangent;
            public readonly float Curvature;
            public readonly float BoundsScale;
            public readonly int Group;

            public StrandSignature(Vector3 root, Vector3 tangent, float curvature,
                                   float boundsScale, int group)
            {
                Root = root;
                Tangent = tangent;
                Curvature = curvature;
                BoundsScale = boundsScale;
                Group = group;
            }
        }

        private readonly struct GridKey : IEquatable<GridKey>
        {
            public readonly int Group;
            public readonly int X;
            public readonly int Y;
            public readonly int Z;

            public GridKey(int group, int x, int y, int z)
            {
                Group = group;
                X = x;
                Y = y;
                Z = z;
            }

            public bool Equals(GridKey other)
            {
                return Group == other.Group && X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is GridKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Group;
                    hash = hash * 397 ^ X;
                    hash = hash * 397 ^ Y;
                    hash = hash * 397 ^ Z;
                    return hash;
                }
            }
        }

        private sealed class GuideGrid
        {
            private readonly float _cellSize;
            private readonly Dictionary<GridKey, List<int>> _cells =
                new Dictionary<GridKey, List<int>>();
            private readonly Dictionary<int, int> _sourceByGuide =
                new Dictionary<int, int>();

            public GuideGrid(float cellSize)
            {
                _cellSize = Mathf.Max(cellSize, 1e-5f);
            }

            public void Add(int group, Vector3 position, int guideIndex, int sourceStrand = -1)
            {
                GridKey key = Key(group, position);
                if (!_cells.TryGetValue(key, out List<int> values))
                {
                    values = new List<int>();
                    _cells.Add(key, values);
                }
                values.Add(guideIndex);
                if (sourceStrand >= 0)
                {
                    _sourceByGuide[guideIndex] = sourceStrand;
                }
            }

            public void Collect(int group, Vector3 position, int radius, List<int> destination)
            {
                destination.Clear();
                GridKey centre = Key(group, position);
                for (int z = -radius; z <= radius; z++)
                for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    var key = new GridKey(group, centre.X + x, centre.Y + y, centre.Z + z);
                    if (_cells.TryGetValue(key, out List<int> values))
                    {
                        destination.AddRange(values);
                    }
                }
            }

            public int SourceStrandOfGuide(int guideIndex)
            {
                return _sourceByGuide.TryGetValue(guideIndex, out int source) ? source : -1;
            }

            private GridKey Key(int group, Vector3 position)
            {
                return new GridKey(group,
                                   Mathf.FloorToInt(position.x / _cellSize),
                                   Mathf.FloorToInt(position.y / _cellSize),
                                   Mathf.FloorToInt(position.z / _cellSize));
            }
        }
    }
}
