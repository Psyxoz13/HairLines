using System.Collections.Generic;
using UnityEngine;

namespace HairLines.EditorTools
{
    public static class HairSkeletonColliders
    {
        private const float MinBoneSize = 0.2f;
        private const float RetryMinBoneSize = 0.01f;
        private const float MinWeldSize = 1e-6f;
        private const float MinPrimSize = 0.005f;
        private const float Margin = 1.01f;
        private const int PowerIterations = 32;
        private const int SegmentSamples = 9;

        private class Bone
        {
            internal Transform Transform;
            internal Bone Parent;
            internal int Depth;
            internal Matrix4x4 BindToCharacter;
            internal readonly List<Vector3> Own = new List<Vector3>();
            internal readonly List<Vector3> Merged = new List<Vector3>();
            internal float MergedSize;
        }

        public static bool Fit(HairGroupSimulation groupSimulation, out LineHairSimulation.BoneCapsule[] capsules,
                               out string report)
        {
            capsules = null;
            LinePrimitiveRenderer renderer = groupSimulation.GetComponentInParent<LinePrimitiveRenderer>();
            HairGroup group = groupSimulation.GetComponent<HairGroup>();
            LinePrimitiveModel model = renderer != null ? renderer.Model : null;
            int groupIndex = renderer != null ? IndexOf(renderer.Groups, group) : -1;
            if (model == null || model.IsEmpty || groupIndex < 0)
            {
                report = "the groom is not built yet";
                return false;
            }

            if (!Reach(model, renderer.transform.localToWorldMatrix, groupIndex, out Bounds roots, out Bounds reach))
            {
                report = "this group has no strands";
                return false;
            }

            Transform character = CharacterRoot(renderer.transform);
            var bones = new Dictionary<Transform, Bone>();
            int meshes = 0;
            int skipped = 0;
            foreach (SkinnedMeshRenderer skin in character.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!skin.enabled)
                {
                    continue;
                }

                if (CollectDominantVertices(skin, character, bones))
                {
                    meshes++;
                }
                else
                {
                    skipped++;
                }
            }

            if (bones.Count == 0)
            {
                report = $"no enabled skinned mesh with bone weights under '{character.name}'";
                return false;
            }

            LinkParents(bones, character);
            List<Bone> bodies = MergeSmallBones(bones, MinBoneSize);
            if (bodies.Count == 0)
            {
                bodies = MergeSmallBones(bones, RetryMinBoneSize);
            }

            int manual = groupSimulation.Settings.Colliders != null ? groupSimulation.Settings.Colliders.Length : 0;
            int room = Mathf.Max(0, LineHairSimulation.MaxShapesPerGroup - manual);
            if (room == 0)
            {
                report = $"the group's {manual} manual colliders already fill all " +
                         $"{LineHairSimulation.MaxShapesPerGroup} collision shapes";
                return false;
            }

            var candidates = new List<(LineHairSimulation.BoneCapsule capsule, float distance)>();
            foreach (Bone body in bodies)
            {
                LineHairSimulation.BoneCapsule capsule = CapsuleOf(body);
                LineHairSimulation.ResolvedShape shape = LineHairSimulation.Resolve(capsule);
                if (Distance(shape, reach) <= 0f)
                {
                    candidates.Add((capsule, Distance(shape, roots)));
                }
            }

            if (candidates.Count == 0)
            {
                report = $"none of the {bodies.Count} bodies under '{character.name}' is within the strands' reach";
                return false;
            }

            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
            var result = new List<LineHairSimulation.BoneCapsule>();
            for (int i = 0; i < candidates.Count && i < room; i++)
            {
                result.Add(candidates[i].capsule);
            }

            capsules = result.ToArray();
            report = $"{result.Count} of {bodies.Count} bodies under '{character.name}' " +
                     $"({meshes} meshes, {bones.Count} skinned bones)" +
                     (candidates.Count > room ? $", {candidates.Count - room} farther ones left out" : "") +
                     (bodies.Count > candidates.Count ? $", {bodies.Count - candidates.Count} out of the strands' reach" : "") +
                     (skipped > 0 ? $", {skipped} meshes without usable bone weights skipped" : "");
            return true;
        }

        private static int IndexOf(IReadOnlyList<HairGroup> groups, HairGroup group)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] == group)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool Reach(LinePrimitiveModel model, Matrix4x4 hairToWorld, int groupIndex,
                                  out Bounds roots, out Bounds reach)
        {
            roots = default;
            reach = default;
            bool found = false;
            float longest = 0f;
            for (int strand = 0; strand < model.StrandCount; strand++)
            {
                Vector2Int range = model.Strands[strand];
                if (range.y <= 0 || model.VertexGroups == null || model.VertexGroups[range.x] != groupIndex)
                {
                    continue;
                }

                Vector3 root = hairToWorld.MultiplyPoint3x4(model.Positions[range.x]);
                float length = 0f;
                for (int local = 1; local < range.y; local++)
                {
                    length += Vector3.Distance(hairToWorld.MultiplyPoint3x4(model.Positions[range.x + local - 1]),
                                               hairToWorld.MultiplyPoint3x4(model.Positions[range.x + local]));
                }

                longest = Mathf.Max(longest, length);
                if (!found)
                {
                    roots = new Bounds(root, Vector3.zero);
                    found = true;
                }
                else
                {
                    roots.Encapsulate(root);
                }
            }

            reach = roots;
            reach.Expand(longest * 2f);
            return found;
        }

        private static Transform CharacterRoot(Transform hair)
        {
            Animator animator = hair.GetComponentInParent<Animator>();
            return animator != null ? animator.transform : hair.root;
        }

        private static bool CollectDominantVertices(SkinnedMeshRenderer skin, Transform character,
                                                    Dictionary<Transform, Bone> bones)
        {
            Mesh mesh = skin.sharedMesh;
            Transform[] skinBones = skin.bones;
            if (mesh == null || skinBones == null || skinBones.Length == 0)
            {
                return false;
            }

            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bindposes = mesh.bindposes;
            if (weights.Length != vertices.Length || bindposes.Length != skinBones.Length)
            {
                return false;
            }

            Matrix4x4 meshToCharacter = character.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            for (int i = 0; i < skinBones.Length; i++)
            {
                if (skinBones[i] != null && !bones.ContainsKey(skinBones[i]))
                {
                    bones.Add(skinBones[i], new Bone
                    {
                        Transform = skinBones[i],
                        BindToCharacter = meshToCharacter * bindposes[i].inverse,
                    });
                }
            }

            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                int dominant = Dominant(weights[vertex]);
                if (dominant < 0 || dominant >= skinBones.Length || skinBones[dominant] == null)
                {
                    continue;
                }

                bones[skinBones[dominant]].Own.Add(bindposes[dominant].MultiplyPoint3x4(vertices[vertex]));
            }

            return true;
        }

        private static int Dominant(BoneWeight weight)
        {
            int bone = weight.boneIndex0;
            float best = weight.weight0;
            if (weight.weight1 > best)
            {
                bone = weight.boneIndex1;
                best = weight.weight1;
            }

            if (weight.weight2 > best)
            {
                bone = weight.boneIndex2;
                best = weight.weight2;
            }

            if (weight.weight3 > best)
            {
                bone = weight.boneIndex3;
                best = weight.weight3;
            }

            return best > 0f ? bone : -1;
        }

        private static void LinkParents(Dictionary<Transform, Bone> bones, Transform character)
        {
            foreach (Bone bone in bones.Values)
            {
                Transform ancestor = bone.Transform.parent;
                while (ancestor != null && !bones.ContainsKey(ancestor) && ancestor != character)
                {
                    ancestor = ancestor.parent;
                }

                bone.Parent = ancestor != null && bones.TryGetValue(ancestor, out Bone parent) ? parent : null;
            }

            foreach (Bone bone in bones.Values)
            {
                for (Bone parent = bone.Parent; parent != null; parent = parent.Parent)
                {
                    bone.Depth++;
                }
            }
        }

        private static List<Bone> MergeSmallBones(Dictionary<Transform, Bone> bones, float minBoneSize)
        {
            foreach (Bone bone in bones.Values)
            {
                bone.MergedSize = 0f;
                bone.Merged.Clear();
            }

            var leavesFirst = new List<Bone>(bones.Values);
            leavesFirst.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            foreach (Bone bone in leavesFirst)
            {
                bone.MergedSize += HalfDiagonal(bone, bone.Own);
                if (bone.MergedSize >= minBoneSize || bone.MergedSize < MinWeldSize || bone.Parent == null)
                {
                    continue;
                }

                Bone parent = bone.Parent;
                parent.MergedSize += bone.MergedSize;
                Matrix4x4 toParent = ChildToParent(bone, parent);
                foreach (Vector3 position in bone.Own)
                {
                    parent.Merged.Add(toParent.MultiplyPoint3x4(position));
                }

                foreach (Vector3 position in bone.Merged)
                {
                    parent.Merged.Add(toParent.MultiplyPoint3x4(position));
                }

                bone.Merged.Clear();
            }

            var parentsFirst = new List<Bone>(bones.Values);
            parentsFirst.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            Bone forcedRoot = null;
            Bone firstParent = null;
            foreach (Bone bone in parentsFirst)
            {
                if (bone.MergedSize <= minBoneSize)
                {
                    continue;
                }

                if (bone.Parent == null)
                {
                    break;
                }

                if (firstParent == null)
                {
                    firstParent = bone.Parent;
                }
                else if (bone.Parent == firstParent)
                {
                    forcedRoot = firstParent;
                    break;
                }
            }

            var bodies = new List<Bone>();
            foreach (Bone bone in parentsFirst)
            {
                if ((bone.MergedSize > minBoneSize || bone == forcedRoot) && bone.Own.Count + bone.Merged.Count > 0)
                {
                    bodies.Add(bone);
                }
            }

            return bodies;
        }

        private static Matrix4x4 ChildToParent(Bone child, Bone parent)
        {
            return parent.BindToCharacter.inverse * child.BindToCharacter;
        }

        private static Vector3 BindScale(Bone bone)
        {
            Vector3 scale = bone.BindToCharacter.lossyScale;
            return new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }

        private static float HalfDiagonal(Bone bone, List<Vector3> positions)
        {
            if (positions.Count == 0)
            {
                return 0f;
            }

            var box = new Bounds(positions[0], Vector3.zero);
            for (int i = 1; i < positions.Count; i++)
            {
                box.Encapsulate(positions[i]);
            }

            return Vector3.Scale(box.extents, BindScale(bone)).magnitude;
        }

        private static float SmallestComponentExtent(Bone bone, Vector3 min, Vector3 max)
        {
            var box = new Bounds(bone.BindToCharacter.MultiplyPoint3x4(min), Vector3.zero);
            for (int corner = 1; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) != 0 ? max.x : min.x, (corner & 2) != 0 ? max.y : min.y,
                                        (corner & 4) != 0 ? max.z : min.z);
                box.Encapsulate(bone.BindToCharacter.MultiplyPoint3x4(point));
            }

            return Mathf.Min(box.extents.x, Mathf.Min(box.extents.y, box.extents.z));
        }

        private static LineHairSimulation.BoneCapsule CapsuleOf(Bone bone)
        {
            var positions = new List<Vector3>(bone.Own.Count + bone.Merged.Count);
            positions.AddRange(bone.Own);
            positions.AddRange(bone.Merged);

            Vector3 zAxis = DominantAxis(positions);
            Vector3 yAxis = Mathf.Abs(zAxis.z) > Mathf.Abs(zAxis.x) && Mathf.Abs(zAxis.z) > Mathf.Abs(zAxis.y)
                ? Vector3.right
                : Vector3.forward;
            yAxis = (yAxis - zAxis * Vector3.Dot(yAxis, zAxis)).normalized;
            Vector3 xAxis = Vector3.Cross(yAxis, zAxis);

            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            foreach (Vector3 position in positions)
            {
                var local = new Vector3(Vector3.Dot(position, xAxis), Vector3.Dot(position, yAxis),
                                        Vector3.Dot(position, zAxis));
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }

            Vector3 centre = (min + max) * 0.5f;
            Vector3 extent = (max - min) * 0.5f;
            if (SmallestComponentExtent(bone, min, max) < MinPrimSize)
            {
                Vector3 scale = BindScale(bone);
                extent = Vector3.one * (MinPrimSize / Mathf.Max(1e-6f, Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z))));
            }

            Vector3 origin = xAxis * centre.x + yAxis * centre.y + zAxis * centre.z;
            Vector3 axis;
            float radius;
            float length;
            if (extent.x > extent.z && extent.x > extent.y)
            {
                axis = xAxis;
                radius = Mathf.Max(extent.y, extent.z);
                length = extent.x;
            }
            else if (extent.y > extent.z && extent.y > extent.x)
            {
                axis = yAxis;
                radius = Mathf.Max(extent.x, extent.z);
                length = extent.y;
            }
            else
            {
                axis = zAxis;
                radius = Mathf.Max(extent.x, extent.y);
                length = extent.z;
            }

            Vector3 half = axis * (length * Margin * 0.5f);
            return new LineHairSimulation.BoneCapsule
            {
                bone = bone.Transform,
                start = origin - half,
                end = origin + half,
                radius = radius * Margin,
            };
        }

        private static Vector3 DominantAxis(List<Vector3> positions)
        {
            Vector3 mean = Vector3.zero;
            foreach (Vector3 position in positions)
            {
                mean += position;
            }

            mean /= positions.Count;
            float xx = 0f, xy = 0f, xz = 0f, yy = 0f, yz = 0f, zz = 0f;
            foreach (Vector3 position in positions)
            {
                Vector3 error = position - mean;
                xx += error.x * error.x;
                xy += error.x * error.y;
                xz += error.x * error.z;
                yy += error.y * error.y;
                yz += error.y * error.z;
                zz += error.z * error.z;
            }

            float count = positions.Count;
            xx /= count; xy /= count; xz /= count; yy /= count; yz /= count; zz /= count;
            Vector3 axis = Vector3.forward;
            for (int i = 0; i < PowerIterations; i++)
            {
                float length = axis.magnitude;
                if (length > 0f)
                {
                    axis = new Vector3(xx * axis.x + xy * axis.y + xz * axis.z,
                                       xy * axis.x + yy * axis.y + yz * axis.z,
                                       xz * axis.x + yz * axis.y + zz * axis.z) / length;
                }
            }

            return axis.sqrMagnitude > 1e-20f ? axis.normalized : Vector3.forward;
        }

        private static float Distance(LineHairSimulation.ResolvedShape capsule, Bounds bounds)
        {
            float closest = float.PositiveInfinity;
            for (int i = 0; i < SegmentSamples; i++)
            {
                Vector3 point = Vector3.Lerp(capsule.a, capsule.b, i / (float)(SegmentSamples - 1));
                closest = Mathf.Min(closest, Mathf.Sqrt(bounds.SqrDistance(point)));
            }

            return closest - capsule.radius;
        }
    }
}
