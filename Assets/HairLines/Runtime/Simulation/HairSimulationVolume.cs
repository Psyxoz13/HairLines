using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace HairLines
{
    internal sealed class HairSimulationVolume : IDisposable
    {
        internal const float FixedPoint = 1 << 12;

        internal const int PressureIterations = 5;

        private const int InactiveResolution = 2;

        public bool Active { get; private set; }
        public int Resolution { get; private set; }
        public int CellCount => Resolution * Resolution * Resolution;
        public Vector3 Origin { get; private set; }
        public float CellSize { get; private set; }
        public float RestWeight { get; private set; } = 1f;

        public long GpuBytes
        {
            get
            {
                long bytes = 0L;
                bytes += Count(_accumulator) * sizeof(int);
                bytes += Count(_cells) * sizeof(float) * 4L;
                bytes += Count(_divergence) * sizeof(float);
                bytes += Count(_pressure[0]) * sizeof(float);
                bytes += Count(_pressure[1]) * sizeof(float);
                bytes += Count(_gradient) * sizeof(float) * 3L;
                bytes += Count(_splatWeights) * sizeof(float);
                return bytes;
            }
        }

        private GraphicsBuffer _accumulator;
        private GraphicsBuffer _cells;
        private GraphicsBuffer _divergence;
        private GraphicsBuffer _gradient;
        private GraphicsBuffer _splatWeights;
        private readonly GraphicsBuffer[] _pressure = new GraphicsBuffer[2];

        internal struct Kernels
        {
            internal int Clear;
            internal int Splat;
            internal int Resolve;
            internal int Divergence;
            internal int Pressure;
            internal int Gradient;
        }

        public void Allocate(bool active, int resolution, LinePrimitiveModel model,
                             HairConstraints constraints, int[] pinnedRootVerticesByGroup)
        {
            Release();

            int controlCount = Mathf.Max(1, constraints?.ControlCount ?? 0);
            active &= model?.Positions != null && model.Strands != null && constraints != null &&
                      constraints.ControlCount > 0;
            Active = active;
            Resolution = active ? Mathf.Max(InactiveResolution, resolution) : InactiveResolution;

            float[] weights = new float[controlCount];
            if (active)
            {
                FitGrid(model);
                ComputeSplatWeights(model, constraints, pinnedRootVerticesByGroup, weights);
                RestWeight = MeasureRestWeight(model, constraints, weights);
            }
            else
            {
                Origin = Vector3.zero;
                CellSize = 1f;
                RestWeight = 1f;
            }

            const GraphicsBuffer.Target Structured = GraphicsBuffer.Target.Structured;
            int cells = CellCount;
            _accumulator = new GraphicsBuffer(Structured, cells * 4, sizeof(int));
            _cells = new GraphicsBuffer(Structured, cells, sizeof(float) * 4);
            _divergence = new GraphicsBuffer(Structured, cells, sizeof(float));
            _pressure[0] = new GraphicsBuffer(Structured, cells, sizeof(float));
            _pressure[1] = new GraphicsBuffer(Structured, cells, sizeof(float));
            _gradient = new GraphicsBuffer(Structured, cells, sizeof(float) * 3);
            _splatWeights = new GraphicsBuffer(Structured, controlCount, sizeof(float));

            _accumulator.SetData(new int[cells * 4]);
            _cells.SetData(new Vector4[cells]);
            _divergence.SetData(new float[cells]);
            _pressure[0].SetData(new float[cells]);
            _pressure[1].SetData(new float[cells]);
            _gradient.SetData(new Vector3[cells]);
            _splatWeights.SetData(weights);
        }

        public void SetUniforms(CommandBuffer commands, ComputeShader shader, Transform transform)
        {
            Matrix4x4 worldToObject = transform.worldToLocalMatrix;
            float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), 1e-6f);
            commands.SetComputeIntParam(shader, Ids.VolumeActive, Active ? 1 : 0);
            commands.SetComputeIntParam(shader, Ids.VolumeCellCount, CellCount);
            commands.SetComputeMatrixParam(shader, Ids.WorldToObject, worldToObject);
            commands.SetComputeVectorParam(shader, Ids.VolumeGrid,
                new Vector4(Origin.x, Origin.y, Origin.z, CellSize));
            commands.SetComputeVectorParam(shader, Ids.VolumeParameters,
                new Vector4(Resolution, 1f / Mathf.Max(RestWeight, 1e-6f), CellSize * scale, scale));
        }

        public void Record(CommandBuffer commands, ComputeShader shader, in Kernels kernels,
                           int controlGroups)
        {
            if (!Active)
            {
                return;
            }

            int cells = CellCount;
            int cellGroups = Mathf.CeilToInt(cells / 256f);

            commands.SetComputeBufferParam(shader, kernels.Clear, Ids.VolumeAccumulator, _accumulator);
            commands.DispatchCompute(shader, kernels.Clear, Mathf.CeilToInt(cells * 4 / 256f), 1, 1);

            commands.SetComputeBufferParam(shader, kernels.Splat, Ids.VolumeAccumulator, _accumulator);
            commands.SetComputeBufferParam(shader, kernels.Splat, Ids.VolumeSplatWeight, _splatWeights);
            commands.DispatchCompute(shader, kernels.Splat, controlGroups, 1, 1);

            commands.SetComputeBufferParam(shader, kernels.Resolve, Ids.VolumeAccumulator, _accumulator);
            commands.SetComputeBufferParam(shader, kernels.Resolve, Ids.VolumeCells, _cells);
            commands.DispatchCompute(shader, kernels.Resolve, cellGroups, 1, 1);

            commands.SetComputeBufferParam(shader, kernels.Divergence, Ids.VolumeCellsRead, _cells);
            commands.SetComputeBufferParam(shader, kernels.Divergence, Ids.VolumeDivergence, _divergence);
            commands.DispatchCompute(shader, kernels.Divergence, cellGroups, 1, 1);

            int current = 0;
            commands.SetComputeBufferParam(shader, kernels.Pressure, Ids.VolumeCellsRead, _cells);
            commands.SetComputeBufferParam(shader, kernels.Pressure, Ids.VolumeDivergenceRead, _divergence);
            for (int iteration = 0; iteration <= PressureIterations; iteration++)
            {
                commands.SetComputeIntParam(shader, Ids.VolumePressureInit, iteration == 0 ? 1 : 0);
                commands.SetComputeBufferParam(shader, kernels.Pressure, Ids.VolumePressureRead,
                                               _pressure[current]);
                commands.SetComputeBufferParam(shader, kernels.Pressure, Ids.VolumePressureNext,
                                               _pressure[1 - current]);
                commands.DispatchCompute(shader, kernels.Pressure, cellGroups, 1, 1);
                current = 1 - current;
            }

            commands.SetComputeBufferParam(shader, kernels.Gradient, Ids.VolumePressureRead,
                                           _pressure[current]);
            commands.SetComputeBufferParam(shader, kernels.Gradient, Ids.VolumeGradient, _gradient);
            commands.DispatchCompute(shader, kernels.Gradient, cellGroups, 1, 1);
        }

        public void BindSampling(CommandBuffer commands, ComputeShader shader, int kernel)
        {
            commands.SetComputeBufferParam(shader, kernel, Ids.VolumeCellsRead, _cells);
            commands.SetComputeBufferParam(shader, kernel, Ids.VolumeGradientRead, _gradient);
        }

        public void BindSampling(ComputeShader shader, int kernel)
        {
            shader.SetBuffer(kernel, Ids.VolumeCellsRead, _cells);
            shader.SetBuffer(kernel, Ids.VolumeGradientRead, _gradient);
        }

        public void Dispose()
        {
            Release();
        }

        private void Release()
        {
            _accumulator?.Dispose(); _accumulator = null;
            _cells?.Dispose(); _cells = null;
            _divergence?.Dispose(); _divergence = null;
            _pressure[0]?.Dispose(); _pressure[0] = null;
            _pressure[1]?.Dispose(); _pressure[1] = null;
            _gradient?.Dispose(); _gradient = null;
            _splatWeights?.Dispose(); _splatWeights = null;
        }

        private void FitGrid(LinePrimitiveModel model)
        {
            Vector3[] positions = model.Positions;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = -min;
            for (int i = 0; i < positions.Length; i++)
            {
                min = Vector3.Min(min, positions[i]);
                max = Vector3.Max(max, positions[i]);
            }

            float longest = 0f;
            for (int strand = 0; strand < model.Strands.Length; strand++)
            {
                Vector2Int range = model.Strands[strand];
                float length = 0f;
                for (int local = 1; local < range.y; local++)
                {
                    int vertex = range.x + local;
                    if (vertex < positions.Length)
                    {
                        length += Vector3.Distance(positions[vertex - 1], positions[vertex]);
                    }
                }

                longest = Mathf.Max(longest, length);
            }

            Vector3 size = max - min;
            float side = Mathf.Max(Mathf.Max(size.x, size.y), Mathf.Max(size.z, 1e-3f)) + longest;
            Vector3 centre = (min + max) * 0.5f;
            CellSize = side / Resolution;
            Origin = centre - Vector3.one * (side * 0.5f);
        }

        private static void ComputeSplatWeights(LinePrimitiveModel model, HairConstraints constraints,
                                                int[] pinnedRootVerticesByGroup, float[] weights)
        {
            Vector3[] positions = model.Positions;
            uint[] controls = constraints.ControlIndices;
            Vector2Int[] strands = constraints.ControlStrands;
            double sum = 0.0;
            int counted = 0;

            for (int strand = 0; strand < strands.Length; strand++)
            {
                int start = strands[strand].x;
                int count = strands[strand].y;
                for (int j = 0; j < count; j++)
                {
                    int slot = start + j;
                    int vertex = (int)controls[slot];
                    if (IsPinned(model, constraints, pinnedRootVerticesByGroup, vertex))
                    {
                        weights[slot] = 0f;
                        continue;
                    }

                    float span = 0f;
                    if (j > 0)
                    {
                        span += 0.5f * Vector3.Distance(positions[(int)controls[slot - 1]], positions[vertex]);
                    }

                    if (j + 1 < count)
                    {
                        span += 0.5f * Vector3.Distance(positions[vertex], positions[(int)controls[slot + 1]]);
                    }

                    weights[slot] = span;
                    if (span > 0f)
                    {
                        sum += span;
                        counted++;
                    }
                }
            }

            float mean = counted > 0 ? (float)(sum / counted) : 1f;
            for (int slot = 0; slot < weights.Length; slot++)
            {
                weights[slot] /= Mathf.Max(mean, 1e-8f);
            }
        }

        private static bool IsPinned(LinePrimitiveModel model, HairConstraints constraints,
                                     int[] pinnedRootVerticesByGroup, int vertex)
        {
            uint group = model.VertexGroups != null && vertex < model.VertexGroups.Length
                ? model.VertexGroups[vertex]
                : 0u;
            int pinned = pinnedRootVerticesByGroup != null && group < pinnedRootVerticesByGroup.Length
                ? pinnedRootVerticesByGroup[group]
                : 1;
            return vertex < constraints.StrandLocalIndex.Length &&
                   constraints.StrandLocalIndex[vertex] < pinned;
        }

        private float MeasureRestWeight(LinePrimitiveModel model, HairConstraints constraints,
                                        float[] weights)
        {
            int resolution = Resolution;
            var grid = new float[CellCount];
            Vector3[] positions = model.Positions;
            uint[] controls = constraints.ControlIndices;
            float densest = 0f;

            for (int slot = 0; slot < controls.Length; slot++)
            {
                float weight = weights[slot];
                if (!(weight > 0f))
                {
                    continue;
                }

                Vector3 cell = (positions[(int)controls[slot]] - Origin) / CellSize -
                               Vector3.one * 0.5f;
                Vector3 floor = new Vector3(
                    Mathf.Clamp(Mathf.Floor(cell.x), 0f, resolution - 2),
                    Mathf.Clamp(Mathf.Floor(cell.y), 0f, resolution - 2),
                    Mathf.Clamp(Mathf.Floor(cell.z), 0f, resolution - 2));
                Vector3 fraction = new Vector3(
                    Mathf.Clamp01(cell.x - floor.x),
                    Mathf.Clamp01(cell.y - floor.y),
                    Mathf.Clamp01(cell.z - floor.z));

                for (int corner = 0; corner < 8; corner++)
                {
                    int ox = corner & 1;
                    int oy = (corner >> 1) & 1;
                    int oz = (corner >> 2) & 1;
                    float t = (ox == 1 ? fraction.x : 1f - fraction.x) *
                              (oy == 1 ? fraction.y : 1f - fraction.y) *
                              (oz == 1 ? fraction.z : 1f - fraction.z);
                    int index = (((int)floor.z + oz) * resolution + (int)floor.y + oy) * resolution +
                                (int)floor.x + ox;
                    grid[index] += weight * t;
                    densest = Mathf.Max(densest, grid[index]);
                }
            }

            return Mathf.Max(densest, 1e-3f);
        }

        private static long Count(GraphicsBuffer buffer)
        {
            return buffer != null ? buffer.count : 0L;
        }

        private static class Ids
        {
            public static readonly int VolumeActive = Shader.PropertyToID("_VolumeActive");
            public static readonly int VolumeCellCount = Shader.PropertyToID("_VolumeCellCount");
            public static readonly int VolumeGrid = Shader.PropertyToID("_VolumeGrid");
            public static readonly int VolumeParameters = Shader.PropertyToID("_VolumeParameters");
            public static readonly int VolumePressureInit = Shader.PropertyToID("_VolumePressureInit");
            public static readonly int WorldToObject = Shader.PropertyToID("_WorldToObject");
            public static readonly int VolumeAccumulator = Shader.PropertyToID("_VolumeAccumulator");
            public static readonly int VolumeSplatWeight = Shader.PropertyToID("_VolumeSplatWeight");
            public static readonly int VolumeCells = Shader.PropertyToID("_VolumeCells");
            public static readonly int VolumeCellsRead = Shader.PropertyToID("_VolumeCellsRead");
            public static readonly int VolumeDivergence = Shader.PropertyToID("_VolumeDivergence");
            public static readonly int VolumeDivergenceRead = Shader.PropertyToID("_VolumeDivergenceRead");
            public static readonly int VolumePressureRead = Shader.PropertyToID("_VolumePressureRead");
            public static readonly int VolumePressureNext = Shader.PropertyToID("_VolumePressureNext");
            public static readonly int VolumeGradient = Shader.PropertyToID("_VolumeGradient");
            public static readonly int VolumeGradientRead = Shader.PropertyToID("_VolumeGradientRead");
        }
    }
}
