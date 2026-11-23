using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace HairLines
{
    public class HairPins
    {
        public const uint NoPin = 0xffffffffu;

        public int Count => _vertices.Length;
        public int CapturedHash { get; private set; }
        public bool HasCapture { get; private set; }
        public bool[] PinnedVertices { get; private set; }

        public long GpuBytes =>
            (_vertexPin != null ? (long)_vertexPin.count * sizeof(uint) : 0L) +
            (_records != null ? (long)_records.count * Marshal.SizeOf<GpuRecord>() : 0L) +
            (_targets != null ? (long)_targets.count * sizeof(float) * 4L : 0L) +
            (_lambda != null ? (long)_lambda.count * sizeof(float) : 0L) +
            (_matrixBuffer != null ? (long)_matrixBuffer.count * sizeof(float) * 16L : 0L) +
            (_complianceBuffer != null ? (long)_complianceBuffer.count * sizeof(float) : 0L);

        private const int Threads = 256;
        private const float Inactive = -1f;

        private int[] _vertices = Array.Empty<int>();
        private int[] _pinOfRecord = Array.Empty<int>();
        private Vector4[] _weights = Array.Empty<Vector4>();
        private int[] _slots = Array.Empty<int>();
        private HairPin[] _sources = Array.Empty<HairPin>();
        private bool[] _bound = Array.Empty<bool>();
        private bool _bindAtRest;
        private readonly List<Transform> _slotTransforms = new List<Transform>();
        private readonly List<Matrix4x4> _slotBindposes = new List<Matrix4x4>();
        private Matrix4x4[] _matrices = Array.Empty<Matrix4x4>();
        private float[] _compliance = Array.Empty<float>();
        private GpuRecord[] _recordData = Array.Empty<GpuRecord>();
        private Vector3[] _positions = Array.Empty<Vector3>();

        private GraphicsBuffer _vertexPin, _records, _targets, _lambda, _matrixBuffer, _complianceBuffer;

        [StructLayout(LayoutKind.Sequential)]
        private struct GpuRecord
        {
            internal uint Slot0, Slot1, Slot2, Slot3;
            internal Vector4 Weights;
            internal Vector3 BindPoint;
            internal uint Pin;
        }

        public static int ComputeHash(IReadOnlyList<HairPin> pins)
        {
            int hash = 17;
            if (pins == null)
            {
                return hash;
            }

            hash = hash * 31 + pins.Count;
            for (int i = 0; i < pins.Count; i++)
            {
                HairPin pin = pins[i];
                if (pin == null)
                {
                    hash *= 31;
                    continue;
                }

                hash = hash * 31 + pin.GetInstanceID();
                hash = hash * 31 + pin.Radius.GetHashCode();
                hash = hash * 31 + (pin.Surface != null ? pin.Surface.GetInstanceID() : 0);
            }

            return hash;
        }

        public void ClearCapture()
        {
            HasCapture = false;
        }

        public void Capture(LinePrimitiveModel model, Matrix4x4 hairToWorld, IReadOnlyList<HairPin> pins, int hash)
        {
            CapturedHash = hash;
            HasCapture = true;
            _slotTransforms.Clear();
            _slotBindposes.Clear();
            var sources = new List<HairPin>();
            if (pins != null)
            {
                for (int i = 0; i < pins.Count; i++)
                {
                    if (pins[i] != null)
                    {
                        sources.Add(pins[i]);
                    }
                }
            }

            _sources = sources.ToArray();
            _compliance = new float[Mathf.Max(1, _sources.Length)];
            _bound = new bool[_sources.Length];
            var vertices = new List<int>();
            var owners = new List<int>();
            int vertexCount = model.VertexCount;
            PinnedVertices = null;
            if (_sources.Length > 0)
            {
                var centres = new Vector3[_sources.Length];
                var radii = new float[_sources.Length];
                for (int pin = 0; pin < _sources.Length; pin++)
                {
                    centres[pin] = _sources[pin].transform.position;
                    radii[pin] = _sources[pin].Radius * _sources[pin].Radius;
                }

                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    Vector3 rest = hairToWorld.MultiplyPoint3x4(model.Positions[vertex]);
                    for (int pin = 0; pin < _sources.Length; pin++)
                    {
                        if ((rest - centres[pin]).sqrMagnitude <= radii[pin])
                        {
                            vertices.Add(vertex);
                            owners.Add(pin);
                            break;
                        }
                    }
                }
            }

            _vertices = vertices.ToArray();
            _pinOfRecord = owners.ToArray();
            _weights = new Vector4[_vertices.Length];
            _slots = new int[_vertices.Length * 4];
            if (_vertices.Length > 0)
            {
                PinnedVertices = new bool[vertexCount];
                for (int record = 0; record < _vertices.Length; record++)
                {
                    PinnedVertices[_vertices[record]] = true;
                }
            }

            for (int pin = 0; pin < _sources.Length; pin++)
            {
                if (!WeightToSkin(pin, model, hairToWorld))
                {
                    WeightToTransform(pin);
                }
            }

            _matrices = new Matrix4x4[Mathf.Max(1, _slotTransforms.Count)];
            _recordData = new GpuRecord[_vertices.Length];
            for (int record = 0; record < _recordData.Length; record++)
            {
                _recordData[record].Pin = (uint)_pinOfRecord[record];
            }
        }

        public void Allocate(int vertexCount)
        {
            ReleaseBuffers();
            int records = Mathf.Max(1, Count);
            _vertexPin = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Count > 0 ? vertexCount : 1,
                                            sizeof(uint));
            _records = new GraphicsBuffer(GraphicsBuffer.Target.Structured, records,
                                          Marshal.SizeOf<GpuRecord>());
            _targets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, records, sizeof(float) * 4);
            _lambda = new GraphicsBuffer(GraphicsBuffer.Target.Structured, records, sizeof(float));
            _matrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _matrices.Length,
                                               sizeof(float) * 16);
            _complianceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _compliance.Length,
                                                   sizeof(float));

            var vertexPin = new uint[_vertexPin.count];
            for (int i = 0; i < vertexPin.Length; i++)
            {
                vertexPin[i] = NoPin;
            }

            for (int record = 0; record < Count; record++)
            {
                vertexPin[_vertices[record]] = (uint)record;
            }

            var targets = new Vector4[records];
            for (int record = 0; record < records; record++)
            {
                targets[record] = new Vector4(0f, 0f, 0f, Inactive);
            }

            _vertexPin.SetData(vertexPin);
            _records.SetData(Count > 0 ? _recordData : new GpuRecord[records]);
            _targets.SetData(targets);
            _lambda.SetData(new float[records]);
            _matrixBuffer.SetData(_matrices);
            _complianceBuffer.SetData(_compliance);
            for (int pin = 0; pin < _bound.Length; pin++)
            {
                _bound[pin] = false;
            }
        }

        public void BindPendingAtRest()
        {
            _bindAtRest = true;
        }

        public void ReadTargets(Vector4[] destination)
        {
            if (Count > 0 && _targets != null)
            {
                _targets.GetData(destination, 0, 0, Count);
            }
        }

        public void WriteTargets(Vector4[] source)
        {
            if (Count > 0 && _targets != null)
            {
                _targets.SetData(source, 0, 0, Count);
            }
        }

        public void UpdateTargets(ComputeShader shader, int kernel, LinePrimitiveModel model, Matrix4x4 hairToWorld)
        {
            if (Count == 0 || _records == null)
            {
                return;
            }

            ReadPins();
            BindActivatedPins(model, hairToWorld);
            _matrixBuffer.SetData(_matrices);
            _complianceBuffer.SetData(_compliance);
            SetBuffers(shader, kernel);
            shader.Dispatch(kernel, Groups, 1, 1);
        }

        public void UpdateTargets(CommandBuffer commands, ComputeShader shader, int kernel, LinePrimitiveModel model,
                                  Matrix4x4 hairToWorld)
        {
            if (Count == 0 || _records == null)
            {
                return;
            }

            ReadPins();
            BindActivatedPins(model, hairToWorld);
            commands.SetBufferData(_matrixBuffer, _matrices);
            commands.SetBufferData(_complianceBuffer, _compliance);
            SetBuffers(commands, shader, kernel);
            commands.DispatchCompute(shader, kernel, Groups, 1, 1);
        }

        public void SetBuffers(ComputeShader shader, int kernel)
        {
            shader.SetBuffer(kernel, Ids.VertexPin, _vertexPin);
            shader.SetBuffer(kernel, Ids.PinRecords, _records);
            shader.SetBuffer(kernel, Ids.PinTargets, _targets);
            shader.SetBuffer(kernel, Ids.PinTargetsRead, _targets);
            shader.SetBuffer(kernel, Ids.PinLambda, _lambda);
            shader.SetBuffer(kernel, Ids.PinMatrices, _matrixBuffer);
            shader.SetBuffer(kernel, Ids.PinCompliance, _complianceBuffer);
            shader.SetInt(Ids.PinCount, Count);
        }

        public void SetBuffers(CommandBuffer commands, ComputeShader shader, int kernel)
        {
            commands.SetComputeBufferParam(shader, kernel, Ids.VertexPin, _vertexPin);
            commands.SetComputeBufferParam(shader, kernel, Ids.PinRecords, _records);
            commands.SetComputeBufferParam(shader, kernel, Ids.PinTargets, _targets);
            commands.SetComputeBufferParam(shader, kernel, Ids.PinTargetsRead, _targets);
            commands.SetComputeBufferParam(shader, kernel, Ids.PinLambda, _lambda);
            commands.SetComputeBufferParam(shader, kernel, Ids.PinMatrices, _matrixBuffer);
            commands.SetComputeBufferParam(shader, kernel, Ids.PinCompliance, _complianceBuffer);
            commands.SetComputeIntParam(shader, Ids.PinCount, Count);
        }

        public void ReleaseBuffers()
        {
            _vertexPin?.Dispose(); _vertexPin = null;
            _records?.Dispose(); _records = null;
            _targets?.Dispose(); _targets = null;
            _lambda?.Dispose(); _lambda = null;
            _matrixBuffer?.Dispose(); _matrixBuffer = null;
            _complianceBuffer?.Dispose(); _complianceBuffer = null;
        }

        private int Groups => Mathf.CeilToInt(Count / (float)Threads);

        private void ReadPins()
        {
            for (int slot = 0; slot < _slotTransforms.Count; slot++)
            {
                Transform transform = _slotTransforms[slot];
                _matrices[slot] = transform != null
                    ? transform.localToWorldMatrix * _slotBindposes[slot]
                    : _matrices[slot];
            }

            for (int pin = 0; pin < _sources.Length; pin++)
            {
                bool active = _sources[pin] != null && _sources[pin].isActiveAndEnabled;
                _compliance[pin] = active ? _sources[pin].Compliance : Inactive;
                if (!active)
                {
                    _bound[pin] = false;
                }
            }
        }

        private void BindActivatedPins(LinePrimitiveModel model, Matrix4x4 hairToWorld)
        {
            bool atRest = _bindAtRest;
            _bindAtRest = false;
            bool changed = false;
            bool positionsRead = false;
            for (int pin = 0; pin < _sources.Length; pin++)
            {
                if (_bound[pin] || _compliance[pin] < 0f)
                {
                    continue;
                }

                if (!atRest && !positionsRead)
                {
                    if (_positions.Length != model.VertexCount)
                    {
                        _positions = new Vector3[model.VertexCount];
                    }

                    model.PositionBuffer.GetData(_positions);
                    positionsRead = true;
                }

                for (int record = 0; record < Count; record++)
                {
                    if (_pinOfRecord[record] != pin)
                    {
                        continue;
                    }

                    int vertex = _vertices[record];
                    Vector3 held = atRest ? hairToWorld.MultiplyPoint3x4(model.Positions[vertex]) : _positions[vertex];
                    BindRecord(record, held);
                }

                _bound[pin] = true;
                changed = true;
            }

            if (changed)
            {
                _records.SetData(_recordData);
            }
        }

        private void BindRecord(int record, Vector3 held)
        {
            int slot = record * 4;
            Vector4 weight = _weights[record];
            Matrix4x4 blended = Blend(_matrices, _slots, slot, weight);
            _recordData[record] = new GpuRecord
            {
                Slot0 = (uint)_slots[slot],
                Slot1 = (uint)_slots[slot + 1],
                Slot2 = (uint)_slots[slot + 2],
                Slot3 = (uint)_slots[slot + 3],
                Weights = weight,
                BindPoint = blended.inverse.MultiplyPoint3x4(held),
                Pin = (uint)_pinOfRecord[record],
            };
        }

        private static Matrix4x4 Blend(Matrix4x4[] matrices, int[] slots, int first, Vector4 weight)
        {
            Matrix4x4 blended = default;
            for (int k = 0; k < 4; k++)
            {
                float w = weight[k];
                if (w <= 0f)
                {
                    continue;
                }

                Matrix4x4 m = matrices[slots[first + k]];
                for (int e = 0; e < 16; e++)
                {
                    blended[e] += w * m[e];
                }
            }

            return blended;
        }

        private void WeightToTransform(int pin)
        {
            int slot = AddSlot(_sources[pin].transform, Matrix4x4.identity);
            for (int record = 0; record < Count; record++)
            {
                if (_pinOfRecord[record] != pin)
                {
                    continue;
                }

                _weights[record] = new Vector4(1f, 0f, 0f, 0f);
                for (int k = 0; k < 4; k++)
                {
                    _slots[record * 4 + k] = slot;
                }
            }
        }

        private bool WeightToSkin(int pin, LinePrimitiveModel model, Matrix4x4 hairToWorld)
        {
            SkinnedMeshRenderer surface = _sources[pin].Surface;
            if (surface == null)
            {
                return false;
            }

            Mesh mesh = surface.sharedMesh;
            Transform[] bones = surface.bones;
            if (mesh == null || bones == null || bones.Length == 0 || !mesh.isReadable)
            {
                Debug.LogWarning($"[HairLines] Hair Pin '{_sources[pin].name}' cannot read the bone weights of " +
                                 $"'{surface.name}' (the mesh needs Read/Write and a skeleton); its vertices follow " +
                                 "the pin transform instead.", _sources[pin]);
                return false;
            }

            Vector3[] local = mesh.vertices;
            int[] triangles = mesh.triangles;
            BoneWeight[] boneWeights = mesh.boneWeights;
            Matrix4x4[] bindposes = mesh.bindposes;
            if (triangles.Length < 3 || boneWeights.Length != local.Length || bindposes.Length != bones.Length)
            {
                Debug.LogWarning($"[HairLines] Hair Pin '{_sources[pin].name}': '{surface.name}' has no usable " +
                                 "triangles, bone weights or bind poses; its vertices follow the pin transform instead.",
                                 _sources[pin]);
                return false;
            }

            var skinning = new Matrix4x4[bones.Length];
            for (int bone = 0; bone < bones.Length; bone++)
            {
                skinning[bone] = bones[bone] != null ? bones[bone].localToWorldMatrix * bindposes[bone] : Matrix4x4.zero;
            }

            var world = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++)
            {
                world[i] = Skin(skinning, boneWeights[i], local[i]);
            }

            Vector3 centre = _sources[pin].transform.position;
            float radius = _sources[pin].Radius;
            float surfaceDistance = Mathf.Sqrt(ClosestTriangle(centre, world, triangles, null, out _, out _));
            float reach = surfaceDistance + radius * 2f;
            var nearby = new List<int>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 point = ClosestPointOnTriangle(centre, world[triangles[t]], world[triangles[t + 1]],
                                                       world[triangles[t + 2]], out _);
                if ((point - centre).sqrMagnitude <= reach * reach)
                {
                    nearby.Add(t);
                }
            }

            var slotTransforms = new List<Transform>();
            var slotBindposes = new List<Matrix4x4>();
            var boneSlots = new Dictionary<int, int>();
            var influence = new Dictionary<int, float>();
            var weights = new Dictionary<int, Vector4>();
            var slots = new Dictionary<int, int[]>();
            for (int record = 0; record < Count; record++)
            {
                if (_pinOfRecord[record] != pin)
                {
                    continue;
                }

                Vector3 rest = hairToWorld.MultiplyPoint3x4(model.Positions[_vertices[record]]);
                ClosestTriangle(rest, world, triangles, nearby, out int triangle, out Vector3 barycentric);
                influence.Clear();
                for (int corner = 0; corner < 3; corner++)
                {
                    BoneWeight bw = boneWeights[triangles[triangle + corner]];
                    float scale = barycentric[corner];
                    Accumulate(influence, bones, bw.boneIndex0, bw.weight0 * scale);
                    Accumulate(influence, bones, bw.boneIndex1, bw.weight1 * scale);
                    Accumulate(influence, bones, bw.boneIndex2, bw.weight2 * scale);
                    Accumulate(influence, bones, bw.boneIndex3, bw.weight3 * scale);
                }

                var ranked = new List<KeyValuePair<int, float>>(influence);
                ranked.Sort((a, b) => b.Value.CompareTo(a.Value));
                int kept = Mathf.Min(4, ranked.Count);
                if (kept == 0)
                {
                    Debug.LogWarning($"[HairLines] Hair Pin '{_sources[pin].name}': the surface of '{surface.name}' " +
                                     "under it has no weights to existing bones; its vertices follow the pin " +
                                     "transform instead.", _sources[pin]);
                    return false;
                }

                float total = 0f;
                for (int k = 0; k < kept; k++)
                {
                    total += ranked[k].Value;
                }

                var weight = Vector4.zero;
                var recordSlots = new int[4];
                for (int k = 0; k < 4; k++)
                {
                    int bone = ranked[Mathf.Min(k, kept - 1)].Key;
                    if (!boneSlots.TryGetValue(bone, out int slot))
                    {
                        slot = _slotTransforms.Count + slotTransforms.Count;
                        slotTransforms.Add(bones[bone]);
                        slotBindposes.Add(bindposes[bone]);
                        boneSlots.Add(bone, slot);
                    }

                    weight[k] = k < kept ? ranked[k].Value / total : 0f;
                    recordSlots[k] = slot;
                }

                weights[record] = weight;
                slots[record] = recordSlots;
            }

            _slotTransforms.AddRange(slotTransforms);
            _slotBindposes.AddRange(slotBindposes);
            foreach (KeyValuePair<int, Vector4> entry in weights)
            {
                _weights[entry.Key] = entry.Value;
                Array.Copy(slots[entry.Key], 0, _slots, entry.Key * 4, 4);
            }

            return true;
        }

        private static Vector3 Skin(Matrix4x4[] skinning, BoneWeight weight, Vector3 position)
        {
            Vector3 result = Vector3.zero;
            result += weight.weight0 * SkinWith(skinning, weight.boneIndex0, position);
            result += weight.weight1 * SkinWith(skinning, weight.boneIndex1, position);
            result += weight.weight2 * SkinWith(skinning, weight.boneIndex2, position);
            result += weight.weight3 * SkinWith(skinning, weight.boneIndex3, position);
            return result;
        }

        private static Vector3 SkinWith(Matrix4x4[] skinning, int bone, Vector3 position)
        {
            return bone >= 0 && bone < skinning.Length ? skinning[bone].MultiplyPoint3x4(position) : Vector3.zero;
        }

        private int AddSlot(Transform transform, Matrix4x4 bindpose)
        {
            _slotTransforms.Add(transform);
            _slotBindposes.Add(bindpose);
            return _slotTransforms.Count - 1;
        }

        private static void Accumulate(Dictionary<int, float> influence, Transform[] bones, int bone, float weight)
        {
            if (weight <= 0f || bone < 0 || bone >= bones.Length || bones[bone] == null)
            {
                return;
            }

            influence.TryGetValue(bone, out float current);
            influence[bone] = current + weight;
        }

        private static float ClosestTriangle(Vector3 point, Vector3[] vertices, int[] triangles, List<int> subset,
                                             out int closest, out Vector3 barycentric)
        {
            closest = 0;
            barycentric = new Vector3(1f, 0f, 0f);
            float best = float.PositiveInfinity;
            int count = subset != null ? subset.Count : triangles.Length / 3;
            for (int i = 0; i < count; i++)
            {
                int t = subset != null ? subset[i] : i * 3;
                Vector3 candidate = ClosestPointOnTriangle(point, vertices[triangles[t]], vertices[triangles[t + 1]],
                                                           vertices[triangles[t + 2]], out Vector3 weights);
                float distance = (candidate - point).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    closest = t;
                    barycentric = weights;
                }
            }

            return best;
        }

        private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 weights)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
            {
                weights = new Vector3(1f, 0f, 0f);
                return a;
            }

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
            {
                weights = new Vector3(0f, 1f, 0f);
                return b;
            }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                weights = new Vector3(1f - v, v, 0f);
                return a + v * ab;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
            {
                weights = new Vector3(0f, 0f, 1f);
                return c;
            }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                weights = new Vector3(1f - w, 0f, w);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                weights = new Vector3(0f, 1f - w, w);
                return b + w * (c - b);
            }

            float denominator = 1f / (va + vb + vc);
            float vv = vb * denominator;
            float ww = vc * denominator;
            weights = new Vector3(1f - vv - ww, vv, ww);
            return a + ab * vv + ac * ww;
        }

        private static class Ids
        {
            public static readonly int VertexPin = Shader.PropertyToID("_VertexPin");
            public static readonly int PinRecords = Shader.PropertyToID("_PinRecords");
            public static readonly int PinTargets = Shader.PropertyToID("_PinTargets");
            public static readonly int PinTargetsRead = Shader.PropertyToID("_PinTargetsRead");
            public static readonly int PinLambda = Shader.PropertyToID("_PinLambda");
            public static readonly int PinMatrices = Shader.PropertyToID("_PinMatrices");
            public static readonly int PinCompliance = Shader.PropertyToID("_PinCompliance");
            public static readonly int PinCount = Shader.PropertyToID("_PinCount");
        }
    }
}
