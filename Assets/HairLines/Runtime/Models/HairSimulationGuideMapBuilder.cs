using System;
using UnityEngine;

namespace HairLines
{
    public static class HairSimulationGuideMapBuilder
    {
        public static bool TryBuildIdentity(LinePrimitiveModel source,
                                            out HairSimulationGuideBuildData data,
                                            out string error)
        {
            data = null;
            if (source == null || source.IsEmpty || source.Strands == null ||
                source.Positions == null)
            {
                error = "The source groom is empty.";
                return false;
            }

            int strandCount = source.StrandCount;
            int vertexCount = source.VertexCount;
            var guideSourceStrands = new int[strandCount];
            var guideRanges = new Vector2Int[strandCount];
            var guideGroups = new uint[strandCount];
            var guideReach = new float[strandCount];
            var guideIds = new Vector2Int[strandCount];
            var guideWeights = new Vector2[strandCount];
            var restRoots = new Vector3[strandCount];
            var restFrames = new Quaternion[strandCount];
            var arcParameters = new float[vertexCount];

            for (int strand = 0; strand < strandCount; strand++)
            {
                Vector2Int range = source.Strands[strand];
                if (range.y <= 0 || range.x < 0 || range.x > vertexCount ||
                    range.y > vertexCount - range.x)
                {
                    error = $"Source strand {strand} has an invalid vertex range.";
                    return false;
                }

                guideSourceStrands[strand] = strand;
                guideRanges[strand] = range;
                guideGroups[strand] = source.VertexGroups != null && range.x < source.VertexGroups.Length
                    ? (source.VertexGroups[range.x] > 15u ? 15u : source.VertexGroups[range.x])
                    : 0u;
                guideReach[strand] = 0f;
                guideIds[strand] = new Vector2Int(strand, strand);
                guideWeights[strand] = new Vector2(1f, 0f);
                restRoots[strand] = source.Positions[range.x];
                restFrames[strand] = BuildRestFrame(source.Positions, range);
                HairSimulationGuideAsset.FillArcParameters(source.Positions, range,
                                                           arcParameters);
            }

            data = new HairSimulationGuideBuildData(
                guideSourceStrands, guideRanges, guideGroups, guideReach,
                guideIds, guideWeights, restRoots, restFrames, arcParameters);
            error = null;
            return true;
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
    }
}
