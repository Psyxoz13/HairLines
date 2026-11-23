using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace HairLines
{
    internal class LineHairBuildJob : IDisposable
    {
        internal bool IsCompleted => _handle.IsCompleted;

        private NativeArray<GroupData> _groups;
        private NativeArray<float> _segmentLengthFractions;
        private NativeArray<float> _segmentLengthParameters;
        private NativeArray<float> _passCurves;
        private NativeArray<PassData> _passes;
        private NativeArray<float> _curves;
        private NativeArray<RootData> _roots;
        private NativeArray<float2> _comb;
        private NativeArray<uint3> _rootColors;
        private NativeArray<OperatorData> _operators;
        private NativeArray<float> _operatorWeights;
        private NativeArray<GuideData> _guides;
        private NativeArray<float3> _guidePoints;
        private NativeArray<StrandWork> _strands;
        private NativeArray<float3> _rotations;
        private NativeArray<int> _clumpAxes;
        private NativeArray<int> _clumpIds;
        private NativeArray<float> _clumpDistances;
        private NativeArray<float> _clumpSeats;
        private NativeArray<long> _clumpKeys;
        private NativeArray<float3> _groundPositions;
        private NativeArray<int3> _groundTriangles;
        private NativeArray<int> _groundCells;
        private NativeArray<int> _groundCellTriangles;

        private NativeArray<Vector3> _positions;
        private NativeArray<uint> _strandInfo;
        private NativeArray<float> _vertexOpacity;
        private NativeArray<uint> _indices;
        private NativeArray<float> _strandShade;
        private NativeArray<ulong> _strandHash;
        private NativeArray<uint3> _strandAttributes;
        private float[] _tipOpacityByGroup;
        private NativeArray<Vector2Int> _strandRanges;
        private NativeArray<Vector4> _strandDebug;
        private NativeArray<uint> _vertexGroups;
        private NativeArray<float3> _scratch;
        private NativeArray<float3> _operatorScratch;
        private NativeArray<float> _distances;
        private NativeArray<StrandBounds> _bounds;
        private NativeArray<float3> _copyOffsets;

        private LinePrimitiveModel.CopySet[] _copySets;
        private float _copyReach;

        private JobHandle _handle;
        private bool _disposed;

        private const int LengthSamples = 64;
        private const int NoChannel = -1;

        private static readonly ProfilerMarker _scheduleMarker =
            new ProfilerMarker("HairLines.Grooming.ScheduleBuild");
        private static readonly ProfilerMarker _prepareMarker =
            new ProfilerMarker("HairLines.Grooming.PrepareBuild");
        private static readonly ProfilerMarker _topologyMarker =
            new ProfilerMarker("HairLines.Grooming.StrandTopology");
        private static readonly ProfilerMarker _completeMarker =
            new ProfilerMarker("HairLines.Grooming.CompleteBuild");

        internal static LineHairBuildJob Schedule(IReadOnlyList<HairRootFrame[]> rootsByGroup,
                                                  IReadOnlyList<HairGrowSettings> settingsByGroup,
                                                  IReadOnlyList<HairRootFrame[]> guideFramesByGroup,
                                                  HairScalpSphere scalp,
                                                  IReadOnlyList<Vector2[]> combByGroup,
                                                  IReadOnlyList<float[]> weightsByGroup,
                                                  IReadOnlyList<uint[]> colorsByGroup = null,
                                                  Vector3[] groundPositions = null,
                                                  int[] groundTriangles = null)
        {
            using ProfilerMarker.AutoScope scope = _scheduleMarker.Auto();
            if (rootsByGroup == null || settingsByGroup == null ||
                rootsByGroup.Count != settingsByGroup.Count)
            {
                throw new ArgumentException("Root and settings groups must have the same length.");
            }

            var build = new LineHairBuildJob();

            try
            {
                build.PrepareAndSchedule(rootsByGroup, settingsByGroup, guideFramesByGroup, scalp,
                                         combByGroup, weightsByGroup, colorsByGroup,
                                         groundPositions, groundTriangles);
                return build;
            }
            catch
            {
                build.Dispose();
                throw;
            }
        }

        internal LinePrimitiveModel Complete(LinePrimitiveModel previous)
        {
            using ProfilerMarker.AutoScope scope = _completeMarker.Auto();
            ThrowIfDisposed();
            _handle.Complete();

            Vector2Int[] strands = _strandRanges.ToArray();
            ulong[] strandHash = _strandHash.ToArray();
            Bounds localBounds = CombineBounds();

            if (_copyReach > 0f)
            {
                localBounds.Expand(_copyReach * 2f);
            }

            LinePrimitiveModel model = previous != null && previous.CanPatch(strands, _vertexGroups)
                ? LinePrimitiveModel.Patch(previous, strandHash, _positions, _strandInfo, _vertexOpacity)
                : new LinePrimitiveModel(_positions.ToArray(), strands, _vertexGroups.ToArray(), strandHash,
                                         _positions, _strandInfo, _vertexOpacity, _indices);

            model.LocalBounds = localBounds;
            model.SetStrandDebug(_strandDebug.ToArray());
            model.SetStrandShade(_strandShade.ToArray(), _tipOpacityByGroup);
            model.SetStrandAttributes(_strandAttributes);

            if (_copyOffsets.Length > 0)
            {
                model.SetCopySets(ToVectorArray(_copyOffsets), _copySets);
            }

            return model;
        }

        private static Vector3[] ToVectorArray(NativeArray<float3> source)
        {
            var result = new Vector3[source.Length];

            for (int i = 0; i < source.Length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        internal void CompleteWithoutResult()
        {
            if (!_disposed)
            {
                _handle.Complete();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _handle.Complete();
            _disposed = true;

            Dispose(ref _groups);
            Dispose(ref _segmentLengthFractions);
            Dispose(ref _segmentLengthParameters);
            Dispose(ref _passCurves);
            Dispose(ref _passes);
            Dispose(ref _curves);
            Dispose(ref _roots);
            Dispose(ref _comb);
            Dispose(ref _rootColors);
            Dispose(ref _operators);
            Dispose(ref _operatorWeights);
            Dispose(ref _guides);
            Dispose(ref _guidePoints);
            Dispose(ref _strands);
            Dispose(ref _rotations);
            Dispose(ref _clumpAxes);
            Dispose(ref _clumpIds);
            Dispose(ref _clumpDistances);
            Dispose(ref _clumpSeats);
            Dispose(ref _clumpKeys);
            Dispose(ref _groundPositions);
            Dispose(ref _groundTriangles);
            Dispose(ref _groundCells);
            Dispose(ref _groundCellTriangles);
            Dispose(ref _positions);
            Dispose(ref _strandInfo);
            Dispose(ref _vertexOpacity);
            Dispose(ref _indices);
            Dispose(ref _strandShade);
            Dispose(ref _strandHash);
            Dispose(ref _strandAttributes);
            Dispose(ref _strandRanges);
            Dispose(ref _strandDebug);
            Dispose(ref _vertexGroups);
            Dispose(ref _scratch);
            Dispose(ref _operatorScratch);
            Dispose(ref _distances);
            Dispose(ref _bounds);
            Dispose(ref _copyOffsets);
        }

        private static float LargestCopySpread(LinePrimitiveModel.CopySet[] copySets)
        {
            float largest = 0f;

            for (int i = 0; copySets != null && i < copySets.Length; i++)
            {
                if (copySets[i].Copies > 0)
                {
                    largest = math.max(largest,
                                       math.max(copySets[i].SpreadRoot, copySets[i].SpreadTip));
                }
            }

            return largest;
        }

        private sealed class GroupPlan
        {
            public readonly List<HairGroomOperator> Active = new List<HairGroomOperator>();
            public readonly List<OperatorData> Operators = new List<OperatorData>();
            public readonly List<PassData> Passes = new List<PassData>();
            public readonly List<int> Writers = new List<int>();
            public readonly List<StrandWork> Strands = new List<StrandWork>();
            public int GrowPoints;
            public int ScratchPoints;
            public int FinalPoints;
            public int CopiesPerRoot = 1;
            public int BakedCopiesPerRoot = 1;
            public bool Structural;
        }

        private void PrepareAndSchedule(IReadOnlyList<HairRootFrame[]> rootsByGroup,
                                        IReadOnlyList<HairGrowSettings> settingsByGroup,
                                        IReadOnlyList<HairRootFrame[]> guideFramesByGroup,
                                        HairScalpSphere scalp,
                                        IReadOnlyList<Vector2[]> combByGroup,
                                        IReadOnlyList<float[]> weightsByGroup,
                                        IReadOnlyList<uint[]> colorsByGroup,
                                        Vector3[] groundPositions,
                                        int[] groundTriangles)
        {
            using ProfilerMarker.AutoScope scope = _prepareMarker.Auto();
            int groupCount = rootsByGroup.Count;
            var managedGroups = new GroupData[groupCount];
            var plans = new GroupPlan[groupCount];
            var managedCurves = new List<float>();
            var managedPassCurves = new List<float>();
            var copySets = new LinePrimitiveModel.CopySet[groupCount];

            int rootCount = 0;
            int strandCount = 0;
            int vertexCount = 0;
            int scratchCount = 0;
            int edgeCount = 0;
            int copyOffsetCount = 0;
            int operatorCount = 0;
            int operatorWeightCount = 0;
            int passCount = 0;
            int guideCount = 0;
            int guidePointCount = 0;
            int segmentLengthFractionCount = 0;
            int segmentLengthParameterCount = 0;
            bool needsGround = false;

            for (int group = 0; group < groupCount; group++)
            {
                HairGrowSettings settings = settingsByGroup[group] ??
                                            throw new ArgumentNullException(nameof(settingsByGroup));
                HairRootFrame[] roots = rootsByGroup[group] ?? Array.Empty<HairRootFrame>();
                float[] weights = weightsByGroup != null && group < weightsByGroup.Count
                    ? weightsByGroup[group]
                    : null;
                HairGuideSet guideSet = settings.Guides;
                int groupGuideCount = guideSet != null ? guideSet.Count : 0;
                int guideVertices = guideSet != null ? guideSet.MaxPointCount : 0;

                var plan = new GroupPlan();
                plans[group] = plan;
                plan.GrowPoints = guideVertices >= 2
                    ? math.min(guideVertices, 129)
                    : math.clamp(settings.SegmentsPerStrand, 1, 128) + 1;

                List<HairGroomOperator> operators = settings.Operators;
                for (int i = 0; i < operators.Count; i++)
                {
                    if (operators[i] != null && operators[i].Enabled)
                    {
                        plan.Active.Add(operators[i]);
                    }
                }

                PlanOperators(plan, settings, managedCurves, managedPassCurves, ref needsGround);
                int instancedCopies = PlanMultiplier(plan, out HairMultiplierOperator instanced);
                copySets[group] = instancedCopies > 0
                    ? new LinePrimitiveModel.CopySet(instancedCopies, instanced.SpreadRoot,
                                                     instanced.SpreadTip, settings.ColorVariance)
                    : default;

                using (_topologyMarker.Auto())
                {
                    BuildTopology(plan, group, roots.Length, weights, settings.Seed);
                }

                int groupStrands = plan.Strands.Count;
                int firstBarrier = plan.Passes.Count > 0 ? plan.Passes[0].StackIndex : plan.Active.Count;

                managedGroups[group] = new GroupData
                {
                    RootOffset = rootCount,
                    RootCount = roots.Length,
                    StrandOffset = strandCount,
                    StrandCount = groupStrands,
                    GrowPoints = plan.GrowPoints,
                    ScratchPoints = plan.ScratchPoints,
                    VerticesPerStrand = plan.FinalPoints,
                    Seed = settings.Seed,
                    StrandLength = settings.StrandLength,
                    LengthVariance = settings.LengthVariance,
                    ScalpFlow = settings.ScalpFlow,
                    Gravity = settings.Gravity,
                    Stiffness = settings.Stiffness,
                    CopiesPerRoot = plan.CopiesPerRoot,
                    InstancedCopies = instancedCopies,
                    CopyOffsetStart = copyOffsetCount,
                    MultiplierStackIndex = MultiplierIndex(plan),
                    FirstBarrier = firstBarrier,
                    PassStart = passCount,
                    PassCount = plan.Passes.Count,
                    ColorVariance = settings.ColorVariance,
                    TipOpacity = settings.TipOpacity,
                    SegmentLengthOffset = segmentLengthFractionCount,
                    SegmentParameterOffset = segmentLengthParameterCount,
                    OperatorStart = operatorCount,
                    OperatorCount = plan.Active.Count,
                    OperatorWeightOffset = operatorWeightCount,
                    GuideStart = guideCount,
                    GuideCount = groupGuideCount,
                    GuideFrames = GuidesAreFramed(guideFramesByGroup, group, groupGuideCount) ? 1 : 0,
                    GuidesDirect = guideSet != null && guideSet.HasUniformPointCount(plan.GrowPoints) ? 1 : 0,
                };

                rootCount += roots.Length;
                strandCount += groupStrands;
                vertexCount += groupStrands * plan.FinalPoints;
                scratchCount += groupStrands * plan.ScratchPoints;
                edgeCount += groupStrands * (plan.FinalPoints - 1);
                copyOffsetCount += groupStrands * instancedCopies;
                operatorCount += plan.Active.Count;
                operatorWeightCount += roots.Length * plan.Active.Count;
                passCount += plan.Passes.Count;
                guideCount += groupGuideCount;
                segmentLengthFractionCount += plan.GrowPoints - 1;
                segmentLengthParameterCount += plan.GrowPoints;

                if (guideSet != null)
                {
                    for (int i = 0; i < guideSet.Guides.Count; i++)
                    {
                        HairGuide guide = guideSet.Guides[i];
                        guidePointCount += guide != null ? guide.PointCount : 0;
                    }
                }
            }

            _groups = NewArray<GroupData>(groupCount);
            _segmentLengthFractions = NewArray<float>(segmentLengthFractionCount);
            _segmentLengthParameters = NewArray<float>(segmentLengthParameterCount);
            _passCurves = ToNative(managedPassCurves);
            _passes = NewArray<PassData>(passCount);
            _curves = ToNative(managedCurves);
            _roots = NewArray<RootData>(rootCount);
            _comb = NewArray<float2>(rootCount);
            _rootColors = NewArray<uint3>(rootCount);
            _operators = NewArray<OperatorData>(operatorCount);
            _operatorWeights = NewArray<float>(operatorWeightCount);
            _guides = NewArray<GuideData>(guideCount);
            _guidePoints = NewArray<float3>(guidePointCount);
            _strands = NewArray<StrandWork>(strandCount);
            _rotations = NewArray<float3>(strandCount);
            _clumpAxes = NewArray<int>(strandCount);
            _clumpIds = NewArray<int>(strandCount);
            _clumpDistances = NewArray<float>(strandCount);
            _clumpSeats = NewArray<float>(strandCount);
            _clumpKeys = NewArray<long>(strandCount);

            _positions = NewArray<Vector3>(vertexCount);
            _strandInfo = NewArray<uint>(vertexCount);
            _vertexOpacity = NewArray<float>(vertexCount);
            _indices = NewArray<uint>(edgeCount * 2);
            _strandShade = NewArray<float>(strandCount);
            _strandHash = NewArray<ulong>(strandCount);
            _strandAttributes = NewArray<uint3>(strandCount);
            _strandRanges = NewArray<Vector2Int>(strandCount);
            _strandDebug = NewArray<Vector4>(strandCount);
            _vertexGroups = NewArray<uint>(vertexCount);
            _scratch = NewArray<float3>(scratchCount);
            _operatorScratch = NewArray<float3>(scratchCount);
            _distances = NewArray<float>(scratchCount);
            _bounds = NewArray<StrandBounds>(strandCount);
            _copyOffsets = NewArray<float3>(copyOffsetCount);
            _copySets = copySets;
            _copyReach = LargestCopySpread(copySets);

            _tipOpacityByGroup = new float[managedGroups.Length];

            int passCursor = 0;
            int operatorCursor = 0;

            for (int i = 0; i < managedGroups.Length; i++)
            {
                GroupPlan plan = plans[i];
                _tipOpacityByGroup[i] = managedGroups[i].TipOpacity;
                _groups[i] = managedGroups[i];
                int segmentCount = managedGroups[i].GrowPoints - 1;
                float[] fractions = settingsByGroup[i].BuildSegmentLengthFractions(segmentCount);
                for (int segment = 0; segment < segmentCount; segment++)
                {
                    _segmentLengthFractions[managedGroups[i].SegmentLengthOffset + segment] = fractions[segment];
                    _segmentLengthParameters[managedGroups[i].SegmentParameterOffset + segment] =
                        segment == 0
                            ? 0f
                            : _segmentLengthParameters[managedGroups[i].SegmentParameterOffset + segment - 1]
                              + fractions[segment - 1];
                }

                _segmentLengthParameters[managedGroups[i].SegmentParameterOffset + segmentCount] = 1f;

                for (int pass = 0; pass < plan.Passes.Count; pass++)
                {
                    _passes[passCursor++] = plan.Passes[pass];
                }

                for (int op = 0; op < plan.Operators.Count; op++)
                {
                    _operators[operatorCursor++] = plan.Operators[op];
                }

            }

            FillRootsAndStrands(rootsByGroup, settingsByGroup, guideFramesByGroup, combByGroup, weightsByGroup,
                                colorsByGroup, managedGroups, plans);

            var sphere = new SphereData { Center = scalp.Center, Radius = scalp.Radius };
            GroundData ground = PrepareGround(needsGround, sphere, groundPositions, groundTriangles,
                                              out JobHandle groundHandle);

            _handle = new GrowStrandsJob
            {
                groups = _groups,
                segmentLengthFractions = _segmentLengthFractions,
                segmentLengthParameters = _segmentLengthParameters,
                roots = _roots,
                comb = _comb,
                guides = _guides,
                guidePoints = _guidePoints,
                work = _strands,
                sphere = sphere,
                stack = BuildStackInputs(ground),
                scratch = _scratch,
                operatorScratch = _operatorScratch,
                distances = _distances,
                rotations = _rotations,
            }.Schedule(_strands.Length, 32, groundHandle);

            _handle = SchedulePasses(sphere, ground, _handle);

            if (_copyOffsets.Length > 0)
            {
                _handle = new CopyOffsetsJob
                {
                    groups = _groups,
                    work = _strands,
                    roots = _roots,
                    operators = _operators,
                    operatorWeights = _operatorWeights,
                    offsets = _copyOffsets,
                }.Schedule(_strands.Length, 64, _handle);
            }

            _handle = new FinalizeStrandsJob
            {
                groups = _groups,
                work = _strands,
                scratch = _scratch,
                roots = _roots,
                rootColors = _rootColors,
                clumpIds = _clumpIds,
                positions = _positions,
                strandInfo = _strandInfo,
                vertexOpacity = _vertexOpacity,
                indices = _indices,
                strandShade = _strandShade,
                strandHash = _strandHash,
                strandAttributes = _strandAttributes,
                strandRanges = _strandRanges,
                vertexGroups = _vertexGroups,
                bounds = _bounds,
                strandDebug = _strandDebug,
            }.Schedule(_strands.Length, 32, _handle);
            JobHandle.ScheduleBatchedJobs();
        }

        private void PlanOperators(GroupPlan plan, HairGrowSettings settings, List<float> curves,
                                   List<float> passCurves, ref bool needsGround)
        {
            int points = plan.GrowPoints;
            int scratchPoints = points;
            int clumpOrdinal = 0;

            for (int i = 0; i < plan.Active.Count; i++)
            {
                HairGroomOperator groomOperator = plan.Active[i];
                OperatorData data = ToJobOperator(groomOperator, plan, i);
                data.Points = points;

                switch (groomOperator)
                {
                    case HairClumpOperator clump:
                        plan.Passes.Add(new PassData
                        {
                            Kind = PassKind.Clump,
                            StackIndex = i,
                            Points = points,
                            WidthOffset = Append(passCurves, clump.BuildWidths(points)),
                            RadiusOffset = -1,
                            ClumpOrdinal = clumpOrdinal++,
                        });
                        break;
                    case HairBraidOperator braid:
                        plan.Passes.Add(new PassData
                        {
                            Kind = PassKind.Braid,
                            StackIndex = i,
                            Points = points,
                            WidthOffset = Append(passCurves, braid.BuildWidths(points)),
                            RadiusOffset = Append(passCurves, braid.BuildRadii(points)),
                            ClumpOrdinal = clumpOrdinal++,
                        });
                        break;
                    case HairPropagationOperator propagation:
                        data.CurveOffset = Append(curves, propagation.BuildLength(LengthSamples));
                        plan.Passes.Add(new PassData
                        {
                            Kind = PassKind.Propagate,
                            StackIndex = i,
                            Points = points,
                            WidthOffset = -1,
                            RadiusOffset = -1,
                        });
                        plan.Structural = true;
                        break;
                    case HairGroundOperator ground:
                        data.CurveOffset = Append(curves, ground.BuildBlend(points));
                        needsGround = true;
                        break;
                    case HairDetailOperator detail:
                        int detailPoints = detail.Segments + 1;
                        float[] fractions = settings.BuildSegmentLengthFractions(detail.Segments);
                        var parameters = new float[detailPoints];
                        for (int k = 1; k < detailPoints; k++)
                        {
                            parameters[k] = parameters[k - 1] + fractions[k - 1];
                        }

                        parameters[detailPoints - 1] = 1f;
                        data.CurveOffset = Append(curves, parameters);
                        data.Int1 = detailPoints;
                        points = detailPoints;
                        scratchPoints = math.max(scratchPoints, points);
                        break;
                    case HairDeleteOperator:
                        plan.Structural = true;
                        break;
                }

                plan.Operators.Add(data);
            }

            plan.FinalPoints = points;
            plan.ScratchPoints = scratchPoints;
        }

        private static int PlanMultiplier(GroupPlan plan, out HairMultiplierOperator instanced)
        {
            int copiesPerRoot = 1;
            int multiplierCount = 0;
            HairMultiplierOperator last = null;

            for (int i = plan.Active.Count - 1; i >= 0; i--)
            {
                if (!(plan.Active[i] is HairMultiplierOperator multiplier))
                {
                    continue;
                }

                multiplierCount++;
                last = multiplier;

                OperatorData data = plan.Operators[i];
                data.D = copiesPerRoot;
                plan.Operators[i] = data;
                copiesPerRoot *= 1 + multiplier.Copies;
            }

            plan.CopiesPerRoot = copiesPerRoot;
            bool isInstanced = multiplierCount == 1 && last.Mode == HairMultiplierMode.Instanced && last.Copies > 0;
            plan.BakedCopiesPerRoot = isInstanced ? 1 : copiesPerRoot;
            instanced = isInstanced ? last : null;
            return isInstanced ? last.Copies : 0;
        }

        private static int MultiplierIndex(GroupPlan plan)
        {
            for (int i = plan.Active.Count - 1; i >= 0; i--)
            {
                if (plan.Active[i] is HairMultiplierOperator)
                {
                    return i;
                }
            }

            return -1;
        }

        private static float WeightOf(float[] weights, int local, int active, int stackIndex)
        {
            int index = local * active + stackIndex;
            return stackIndex >= 0 && weights != null && index < weights.Length ? weights[index] : 1f;
        }

        private struct TopologyNode
        {
            public int Parent;
            public int SeedId;
            public int BirthPass;
            public int ChildIndex;
            public int ChildCount;
            public float BirthT;
            public float Anchor;
            public bool Alive;
        }

        private static readonly List<TopologyNode> _nodes = new List<TopologyNode>();
        private static readonly List<int> _emitted = new List<int>();

        private void BuildTopology(GroupPlan plan, int group, int rootCount, float[] weights, int seed)
        {
            int active = plan.Active.Count;
            plan.Strands.Capacity = rootCount * plan.BakedCopiesPerRoot;

            for (int local = 0; local < rootCount; local++)
            {
                for (int copy = 0; copy < plan.BakedCopiesPerRoot; copy++)
                {
                    int seedId = local * plan.CopiesPerRoot + copy;

                    if (!plan.Structural)
                    {
                        plan.Strands.Add(new StrandWork
                        {
                            Group = group,
                            LocalStrand = local,
                            Copy = copy,
                            SeedId = seedId,
                            LineageSeed = seedId,
                            Parent = -1,
                            BirthPass = -1,
                        });
                        continue;
                    }

                    _nodes.Clear();
                    _nodes.Add(new TopologyNode
                    {
                        Parent = -1,
                        SeedId = seedId,
                        BirthPass = -1,
                        Alive = true,
                    });

                    int pass = -1;

                    for (int i = 0; i < active; i++)
                    {
                        HairGroomOperator groomOperator = plan.Active[i];
                        OperatorData data = plan.Operators[i];
                        float weight = WeightOf(weights, local, active, i);

                        if (groomOperator.IsGlobal)
                        {
                            pass++;
                        }

                        switch (groomOperator)
                        {
                            case HairPropagationOperator:
                                Propagate(data, i, pass, weight, WeightOf(weights, local, active, data.BindE));
                                break;
                            case HairDeleteOperator:
                                Delete(data, data.A * weight * WeightOf(weights, local, active, data.BindA),
                                       local, rootCount, seed);
                                break;
                        }
                    }

                    int first = plan.Strands.Count;
                    _emitted.Clear();

                    for (int n = 0; n < _nodes.Count; n++)
                    {
                        TopologyNode node = _nodes[n];
                        _emitted.Add(node.Alive ? plan.Strands.Count - first : -1);

                        if (!node.Alive)
                        {
                            continue;
                        }

                        int parent = node.Parent >= 0 ? _emitted[node.Parent] : -1;

                        plan.Strands.Add(new StrandWork
                        {
                            Group = group,
                            LocalStrand = local,
                            Copy = copy,
                            SeedId = node.SeedId,
                            LineageSeed = seedId,
                            Parent = parent >= 0 ? first + parent : -1,
                            BirthPass = node.BirthPass,
                            ChildIndex = node.ChildIndex,
                            ChildCount = node.ChildCount,
                            BirthT = node.BirthT,
                            Anchor = node.Anchor,
                        });
                    }
                }
            }
        }

        private static void Propagate(OperatorData data, int stackIndex, int pass, float weight, float countFactor)
        {
            int count = (int)math.round(data.E * weight * math.max(0f, countFactor));

            if (count <= 0)
            {
                return;
            }

            int parents = _nodes.Count;

            for (int n = 0; n < parents; n++)
            {
                TopologyNode parent = _nodes[n];

                if (!parent.Alive)
                {
                    continue;
                }

                for (int child = 0; child < count; child++)
                {
                    int seedId = (int)math.hash(new int3(parent.SeedId, stackIndex * 977 + data.Seed, child));
                    float jitter = (HairRandom.HashToUnitFloat(seedId) - 0.5f) * data.D;
                    float t = math.lerp(data.A, data.B, math.saturate((child + 0.5f + jitter) / count));

                    _nodes.Add(new TopologyNode
                    {
                        Parent = n,
                        SeedId = seedId,
                        BirthPass = pass,
                        ChildIndex = child,
                        ChildCount = count,
                        BirthT = t,
                        Anchor = parent.BirthPass < 0 ? t : parent.Anchor,
                        Alive = true,
                    });
                }
            }
        }

        private static void Delete(OperatorData data, float fraction, int local, int rootCount, int seed)
        {
            if (fraction <= 0f)
            {
                return;
            }

            float rank = rootCount > 0 ? local / (float)rootCount : 0f;

            for (int n = 0; n < _nodes.Count; n++)
            {
                TopologyNode node = _nodes[n];

                if (!node.Alive || (node.Parent >= 0 && !_nodes[node.Parent].Alive))
                {
                    node.Alive = false;
                    _nodes[n] = node;
                    continue;
                }

                bool remove = node.BirthPass < 0
                    ? rank >= 1f - fraction
                    : HairRandom.HashToUnitFloat(unchecked(node.SeedId * 31337 + data.Seed * 6151 + seed)) < fraction;

                if (remove)
                {
                    node.Alive = false;
                    _nodes[n] = node;
                }
            }

            for (int n = 0; n < _nodes.Count; n++)
            {
                TopologyNode node = _nodes[n];

                if (node.Alive && node.Parent >= 0 && !_nodes[node.Parent].Alive)
                {
                    node.Alive = false;
                    _nodes[n] = node;
                }
            }
        }

        private void FillRootsAndStrands(IReadOnlyList<HairRootFrame[]> rootsByGroup,
                                         IReadOnlyList<HairGrowSettings> settingsByGroup,
                                         IReadOnlyList<HairRootFrame[]> guideFramesByGroup,
                                         IReadOnlyList<Vector2[]> combByGroup,
                                         IReadOnlyList<float[]> weightsByGroup,
                                         IReadOnlyList<uint[]> colorsByGroup,
                                         GroupData[] managedGroups, GroupPlan[] plans)
        {
            int rootCursor = 0;
            int strandCursor = 0;
            int vertexCursor = 0;
            int scratchCursor = 0;
            int edgeCursor = 0;
            int guideCursor = 0;
            int guidePointCursor = 0;
            var white = new uint3(0u, 0u, 0xFFFFFFFFu);

            for (int group = 0; group < managedGroups.Length; group++)
            {
                GroupData groupData = managedGroups[group];
                HairGrowSettings settings = settingsByGroup[group];
                HairRootFrame[] roots = rootsByGroup[group] ?? Array.Empty<HairRootFrame>();
                int active = plans[group].Active.Count;
                Vector2[] comb = combByGroup != null && group < combByGroup.Count ? combByGroup[group] : null;
                float[] weights = weightsByGroup != null && group < weightsByGroup.Count
                    ? weightsByGroup[group]
                    : null;
                uint[] colors = colorsByGroup != null && group < colorsByGroup.Count ? colorsByGroup[group] : null;

                for (int local = 0; local < roots.Length; local++)
                {
                    HairRootFrame root = roots[local];
                    _roots[rootCursor] = new RootData
                    {
                        Position = root.Position,
                        Normal = root.Normal,
                        Tangent = root.Tangent,
                        Uv = root.Uv,
                    };
                    _comb[rootCursor] = comb != null && local < comb.Length
                        ? new float2(comb[local].x, comb[local].y)
                        : float2.zero;
                    _rootColors[rootCursor] = colors != null && local * 3 + 2 < colors.Length
                        ? new uint3(colors[local * 3], colors[local * 3 + 1], colors[local * 3 + 2])
                        : white;

                    for (int op = 0; op < active; op++)
                    {
                        int source = local * active + op;
                        _operatorWeights[groupData.OperatorWeightOffset + source] =
                            weights != null && source < weights.Length ? weights[source] : 1f;
                    }

                    rootCursor++;
                }

                List<StrandWork> strands = plans[group].Strands;
                int groupStrandStart = strandCursor;

                for (int i = 0; i < strands.Count; i++)
                {
                    StrandWork item = strands[i];
                    item.Root = groupData.RootOffset + item.LocalStrand;
                    item.Parent = item.Parent >= 0 ? groupStrandStart + item.Parent : -1;
                    item.VertexStart = vertexCursor;
                    item.ScratchStart = scratchCursor;
                    item.EdgeStart = edgeCursor;
                    _clumpIds[strandCursor] = -1;
                    _strands[strandCursor++] = item;

                    vertexCursor += groupData.VerticesPerStrand;
                    scratchCursor += groupData.ScratchPoints;
                    edgeCursor += groupData.VerticesPerStrand - 1;
                }

                HairGuideSet guideSet = settings.Guides;
                if (guideSet == null)
                {
                    continue;
                }

                HairRootFrame[] guideFrames = guideFramesByGroup != null && group < guideFramesByGroup.Count
                    ? guideFramesByGroup[group] ?? Array.Empty<HairRootFrame>()
                    : Array.Empty<HairRootFrame>();

                for (int i = 0; i < guideSet.Guides.Count; i++)
                {
                    HairGuide guide = guideSet.Guides[i];
                    int points = guide != null ? guide.PointCount : 0;
                    bool framed = i < guideFrames.Length;
                    HairRootFrame frame = framed ? guideFrames[i] : default;
                    Vector3 guideBitangent = framed ? frame.Bitangent : Vector3.zero;

                    _guides[guideCursor++] = new GuideData
                    {
                        Uv = guide != null ? (float2)guide.Uv : float2.zero,
                        Position = framed ? (float3)frame.Position : float3.zero,
                        Normal = framed ? (float3)frame.Normal : float3.zero,
                        PointStart = guidePointCursor,
                        PointCount = points,
                    };

                    var shape = new float3[points];

                    for (int point = 0; point < points; point++)
                    {
                        Vector3 local = guide.PointAt(point);
                        shape[point] = framed
                            ? (float3)(frame.Tangent * local.x + guideBitangent * local.y +
                                       frame.Normal * local.z)
                            : (float3)local;
                    }

                    if (groupData.GuidesDirect != 0)
                    {
                        for (int point = 0; point < points; point++)
                        {
                            _guidePoints[guidePointCursor + point] = shape[point];
                        }
                    }
                    else
                    {
                        SpaceByArcLength(shape, _guidePoints, guidePointCursor);
                    }

                    guidePointCursor += points;
                }
            }
        }

        private GroundData PrepareGround(bool needsGround, SphereData sphere, Vector3[] positions, int[] triangles,
                                         out JobHandle handle)
        {
            handle = default;
            var ground = new GroundData { Mode = GroundMode.None, Sphere = sphere };
            bool mesh = needsGround && positions != null && triangles != null && triangles.Length >= 3;

            _groundPositions = NewArray<float3>(mesh ? positions.Length : 0);
            _groundTriangles = NewArray<int3>(mesh ? triangles.Length / 3 : 0);

            if (!needsGround)
            {
                _groundCells = NewArray<int>(0);
                _groundCellTriangles = NewArray<int>(0);
                return ground;
            }

            if (!mesh)
            {
                _groundCells = NewArray<int>(0);
                _groundCellTriangles = NewArray<int>(0);
                ground.Mode = sphere.Radius > 0f ? GroundMode.Sphere : GroundMode.None;
                return ground;
            }

            float3 minimum = positions[triangles[0]];
            float3 maximum = minimum;
            float edge = 0f;

            for (int i = 0; i < positions.Length; i++)
            {
                _groundPositions[i] = positions[i];
            }

            for (int t = 0; t < _groundTriangles.Length; t++)
            {
                var triangle = new int3(triangles[t * 3], triangles[t * 3 + 1], triangles[t * 3 + 2]);
                _groundTriangles[t] = triangle;
                float3 a = positions[triangle.x];
                float3 b = positions[triangle.y];
                float3 c = positions[triangle.z];
                minimum = math.min(minimum, math.min(a, math.min(b, c)));
                maximum = math.max(maximum, math.max(a, math.max(b, c)));
                edge += math.distance(a, b) + math.distance(b, c) + math.distance(c, a);
            }

            float3 extent = math.max(maximum - minimum, 1e-4f);
            float cellSize = math.max(2f * edge / math.max(1, _groundTriangles.Length * 3), 1e-4f);
            int3 dims = math.clamp((int3)math.ceil(extent / cellSize), 1, 64);
            cellSize = math.cmax(extent / dims);

            ground.Mode = GroundMode.Mesh;
            ground.Origin = minimum;
            ground.CellSize = cellSize;
            ground.Dims = dims;

            int cells = dims.x * dims.y * dims.z;
            _groundCells = NewArray<int>(cells + 1);
            var counts = new int[cells + 1];
            var ranges = new int4[_groundTriangles.Length];

            for (int t = 0; t < _groundTriangles.Length; t++)
            {
                int3 triangle = _groundTriangles[t];
                float3 low = math.min(_groundPositions[triangle.x], math.min(_groundPositions[triangle.y],
                                                                             _groundPositions[triangle.z]));
                float3 high = math.max(_groundPositions[triangle.x], math.max(_groundPositions[triangle.y],
                                                                              _groundPositions[triangle.z]));
                int3 from = math.clamp((int3)math.floor((low - minimum) / cellSize), 0, dims - 1);
                int3 to = math.clamp((int3)math.floor((high - minimum) / cellSize), 0, dims - 1);
                ranges[t] = new int4(Pack(from, dims), Pack(to, dims), 0, 0);

                for (int z = from.z; z <= to.z; z++)
                for (int y = from.y; y <= to.y; y++)
                for (int x = from.x; x <= to.x; x++)
                {
                    counts[x + dims.x * (y + dims.y * z) + 1]++;
                }
            }

            for (int cell = 0; cell < cells; cell++)
            {
                counts[cell + 1] += counts[cell];
            }

            _groundCellTriangles = NewArray<int>(counts[cells]);
            var cursor = new int[cells];
            for (int cell = 0; cell <= cells; cell++)
            {
                _groundCells[cell] = counts[cell];
            }

            for (int t = 0; t < _groundTriangles.Length; t++)
            {
                int3 from = Unpack(ranges[t].x, dims);
                int3 to = Unpack(ranges[t].y, dims);

                for (int z = from.z; z <= to.z; z++)
                for (int y = from.y; y <= to.y; y++)
                for (int x = from.x; x <= to.x; x++)
                {
                    int cell = x + dims.x * (y + dims.y * z);
                    _groundCellTriangles[counts[cell] + cursor[cell]++] = t;
                }
            }

            return ground;
        }

        private static int Pack(int3 cell, int3 dims)
        {
            return cell.x + dims.x * (cell.y + dims.y * cell.z);
        }

        private static int3 Unpack(int index, int3 dims)
        {
            int x = index % dims.x;
            int y = index / dims.x % dims.y;
            int z = index / (dims.x * dims.y);
            return new int3(x, y, z);
        }

        private StackInputs BuildStackInputs(GroundData ground)
        {
            return new StackInputs
            {
                operators = _operators,
                operatorWeights = _operatorWeights,
                curves = _curves,
                roots = _roots,
                ground = ground,
                groundPositions = _groundPositions,
                groundTriangles = _groundTriangles,
                groundCells = _groundCells,
                groundCellTriangles = _groundCellTriangles,
            };
        }

        private JobHandle SchedulePasses(SphereData sphere, GroundData ground, JobHandle input)
        {
            int passCount = 0;

            for (int i = 0; i < _groups.Length; i++)
            {
                passCount = math.max(passCount, _groups[i].PassCount);
            }

            JobHandle handle = input;

            for (int pass = 0; pass < passCount && _strands.Length > 0; pass++)
            {
                bool clumps = false;
                bool propagates = false;

                for (int i = 0; i < _groups.Length; i++)
                {
                    if (pass >= _groups[i].PassCount)
                    {
                        continue;
                    }

                    PassKind kind = _passes[_groups[i].PassStart + pass].Kind;
                    clumps |= kind != PassKind.Propagate;
                    propagates |= kind == PassKind.Propagate;
                }

                if (clumps)
                {
                    handle = new AssignClumpsJob
                    {
                        groups = _groups,
                        passes = _passes,
                        work = _strands,
                        roots = _roots,
                        guides = _guides,
                        operators = _operators,
                        operatorWeights = _operatorWeights,
                        axes = _clumpAxes,
                        pass = pass,
                        clumpIds = _clumpIds,
                        clumpDistances = _clumpDistances,
                    }.Schedule(_strands.Length, 32, handle);

                    handle = new SelectClumpAxesJob
                    {
                        groups = _groups,
                        passes = _passes,
                        passCurves = _passCurves,
                        work = _strands,
                        roots = _roots,
                        operators = _operators,
                        operatorWeights = _operatorWeights,
                        clumpIds = _clumpIds,
                        clumpDistances = _clumpDistances,
                        pass = pass,
                        keys = _clumpKeys,
                        axes = _clumpAxes,
                        seats = _clumpSeats,
                    }.Schedule(handle);

                    handle = new ClumpOperatorJob
                    {
                        groups = _groups,
                        passes = _passes,
                        passCurves = _passCurves,
                        work = _strands,
                        roots = _roots,
                        operators = _operators,
                        operatorWeights = _operatorWeights,
                        rotations = _rotations,
                        axes = _clumpAxes,
                        seats = _clumpSeats,
                        pass = pass,
                        sphere = sphere,
                        scratch = _scratch,
                        operatorScratch = _operatorScratch,
                        distances = _distances,
                    }.Schedule(_strands.Length, 32, handle);
                }

                if (propagates)
                {
                    handle = new PropagateJob
                    {
                        groups = _groups,
                        passes = _passes,
                        work = _strands,
                        roots = _roots,
                        operators = _operators,
                        operatorWeights = _operatorWeights,
                        curves = _curves,
                        pass = pass,
                        scratch = _scratch,
                        distances = _distances,
                        rotations = _rotations,
                    }.Schedule(_strands.Length, 32, handle);
                }

                handle = new OperatorRangeJob
                {
                    groups = _groups,
                    passes = _passes,
                    work = _strands,
                    stack = BuildStackInputs(ground),
                    afterPass = pass,
                    sphere = sphere,
                    scratch = _scratch,
                    operatorScratch = _operatorScratch,
                    distances = _distances,
                    rotations = _rotations,
                }.Schedule(_strands.Length, 32, handle);
            }

            return handle;
        }

        private static OperatorData ToJobOperator(HairGroomOperator groomOperator, GroupPlan plan, int stackIndex)
        {
            var data = new OperatorData
            {
                Seed = groomOperator.Seed,
                CurveOffset = -1,
                BindA = NoChannel,
                BindB = NoChannel,
                BindC = NoChannel,
                BindD = NoChannel,
                BindE = NoChannel,
                BindCurve = NoChannel,
            };

            switch (groomOperator)
            {
                case HairClumpOperator clump:
                    data.Type = OperatorType.Clump;
                    data.A = clump.WidthScale;
                    data.B = clump.Stray;
                    data.BindA = Bind(plan, stackIndex, clump.WidthScaleChannel);
                    data.BindB = Bind(plan, stackIndex, clump.StrayChannel);
                    break;
                case HairBraidOperator braid:
                    data.Type = OperatorType.Braid;
                    data.A = braid.Frequency;
                    data.B = braid.Flatness;
                    data.Int0 = (int)braid.Stems;
                    data.BindA = Bind(plan, stackIndex, braid.FrequencyChannel);
                    data.BindB = Bind(plan, stackIndex, braid.FlatnessChannel);
                    data.BindC = Bind(plan, stackIndex, braid.WidthChannel);
                    data.BindD = Bind(plan, stackIndex, braid.RadiusChannel);
                    break;
                case HairCurlOperator curl:
                    data.Type = OperatorType.Curl;
                    data.A = curl.Radius;
                    data.B = curl.Frequency;
                    data.BindA = Bind(plan, stackIndex, curl.RadiusChannel);
                    data.BindB = Bind(plan, stackIndex, curl.FrequencyChannel);
                    break;
                case HairCutOperator cut:
                    data.Type = OperatorType.Cut;
                    data.A = cut.Keep;
                    data.B = cut.Ragged;
                    data.BindA = Bind(plan, stackIndex, cut.KeepChannel);
                    data.BindB = Bind(plan, stackIndex, cut.RaggedChannel);
                    break;
                case HairMultiplierOperator multiplier:
                    data.Type = OperatorType.Multiplier;
                    data.A = multiplier.SpreadRoot;
                    data.B = multiplier.SpreadTip;
                    data.C = 1 + multiplier.Copies;
                    data.BindA = Bind(plan, stackIndex, multiplier.SpreadRootChannel);
                    data.BindB = Bind(plan, stackIndex, multiplier.SpreadTipChannel);
                    break;
                case HairGravityOperator gravity:
                    data.Type = OperatorType.Gravity;
                    data.A = gravity.Amount;
                    data.B = gravity.RootHold;
                    data.BindA = Bind(plan, stackIndex, gravity.AmountChannel);
                    data.BindB = Bind(plan, stackIndex, gravity.RootHoldChannel);
                    break;
                case HairNoiseOperator noise:
                    data.Type = OperatorType.Noise;
                    data.A = noise.Amount;
                    data.B = noise.Scale;
                    data.C = noise.RootHold;
                    data.BindA = Bind(plan, stackIndex, noise.AmountChannel);
                    data.BindB = Bind(plan, stackIndex, noise.ScaleChannel);
                    data.BindC = Bind(plan, stackIndex, noise.RootHoldChannel);
                    break;
                case HairFrizzOperator frizz:
                    data.Type = OperatorType.Frizz;
                    data.A = frizz.Amount;
                    data.B = frizz.Scale;
                    data.C = frizz.RootHold;
                    data.BindA = Bind(plan, stackIndex, frizz.AmountChannel);
                    data.BindB = Bind(plan, stackIndex, frizz.ScaleChannel);
                    data.BindC = Bind(plan, stackIndex, frizz.RootHoldChannel);
                    break;
                case HairRotateOperator rotate:
                    data.Type = OperatorType.Rotate;
                    data.A = math.radians(rotate.Angle);
                    data.B = math.radians(rotate.Chaos);
                    data.C = math.radians(rotate.Twist);
                    data.BindA = Bind(plan, stackIndex, rotate.AngleChannel);
                    data.BindB = Bind(plan, stackIndex, rotate.ChaosChannel);
                    data.BindC = Bind(plan, stackIndex, rotate.TwistChannel);
                    break;
                case HairDetailOperator detail:
                    data.Type = OperatorType.Detail;
                    data.A = detail.Smoothing;
                    data.Int0 = detail.Iterations;
                    data.BindA = Bind(plan, stackIndex, detail.SmoothingChannel);
                    break;
                case HairGroundOperator:
                    data.Type = OperatorType.Ground;
                    break;
                case HairPropagationOperator propagation:
                    data.Type = OperatorType.Propagation;
                    data.A = math.min(propagation.RangeStart, propagation.RangeEnd);
                    data.B = math.max(propagation.RangeStart, propagation.RangeEnd);
                    data.C = math.radians(propagation.Angle);
                    data.D = propagation.Randomness;
                    data.E = propagation.Count;
                    data.Int0 = propagation.Sides;
                    data.BindC = Bind(plan, stackIndex, propagation.AngleChannel);
                    data.BindD = Bind(plan, stackIndex, propagation.RandomnessChannel);
                    data.BindE = Bind(plan, stackIndex, propagation.CountChannel);
                    data.BindCurve = Bind(plan, stackIndex, propagation.LengthChannel);
                    break;
                case HairDeleteOperator delete:
                    data.Type = OperatorType.Delete;
                    data.A = delete.Fraction;
                    data.BindA = Bind(plan, stackIndex, delete.FractionChannel);
                    break;
                case HairStrandDataOperator:
                    data.Type = OperatorType.StrandChannel;
                    plan.Writers.Add(stackIndex);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Groom operator '{groomOperator.GetType().Name}' does not have a Job implementation.");
            }

            return data;
        }

        private static int Bind(GroupPlan plan, int stackIndex, int channel)
        {
            if (channel <= 0)
            {
                return NoChannel;
            }

            for (int w = plan.Writers.Count - 1; w >= 0; w--)
            {
                int writer = plan.Writers[w];

                if (writer < stackIndex && plan.Active[writer] is HairStrandDataOperator data &&
                    data.Channel == channel && data.Mask.IsUsable)
                {
                    return writer;
                }
            }

            return NoChannel;
        }

        private static int Append(List<float> destination, float[] values)
        {
            int offset = destination.Count;
            destination.AddRange(values);
            return offset;
        }

        private static NativeArray<float> ToNative(List<float> values)
        {
            NativeArray<float> result = NewArray<float>(values.Count);

            for (int i = 0; i < values.Count; i++)
            {
                result[i] = values[i];
            }

            return result;
        }

        private Bounds CombineBounds()
        {
            if (_bounds.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            float3 minimum = _bounds[0].Minimum;
            float3 maximum = _bounds[0].Maximum;

            for (int i = 1; i < _bounds.Length; i++)
            {
                minimum = math.min(minimum, _bounds[i].Minimum);
                maximum = math.max(maximum, _bounds[i].Maximum);
            }

            return new Bounds((Vector3)((minimum + maximum) * 0.5f),
                              (Vector3)(maximum - minimum));
        }

        private static bool GuidesAreFramed(IReadOnlyList<HairRootFrame[]> guideFramesByGroup, int group,
                                            int guideCount)
        {
            if (guideCount <= 0 || guideFramesByGroup == null || group >= guideFramesByGroup.Count)
            {
                return false;
            }

            HairRootFrame[] frames = guideFramesByGroup[group];
            return frames != null && frames.Length >= guideCount;
        }

        private static void SpaceByArcLength(float3[] shape, NativeArray<float3> destination, int offset)
        {
            if (shape.Length == 0)
            {
                return;
            }

            if (shape.Length < 3)
            {
                for (int i = 0; i < shape.Length; i++)
                {
                    destination[offset + i] = shape[i];
                }

                return;
            }

            var travelled = new float[shape.Length];

            for (int i = 1; i < shape.Length; i++)
            {
                travelled[i] = travelled[i - 1] + math.distance(shape[i], shape[i - 1]);
            }

            float total = travelled[shape.Length - 1];

            if (total <= 1e-6f)
            {
                for (int i = 0; i < shape.Length; i++)
                {
                    destination[offset + i] = shape[i];
                }

                return;
            }

            int last = shape.Length - 1;
            destination[offset] = shape[0];
            destination[offset + last] = shape[last];
            int cursor = 1;

            for (int i = 1; i < last; i++)
            {
                float wanted = total * i / last;

                while (cursor < last && travelled[cursor] < wanted)
                {
                    cursor++;
                }

                float span = travelled[cursor] - travelled[cursor - 1];
                float blend = span > 1e-8f ? (wanted - travelled[cursor - 1]) / span : 0f;
                destination[offset + i] = math.lerp(shape[cursor - 1], shape[cursor], blend);
            }
        }

        private static NativeArray<T> NewArray<T>(int length) where T : struct
        {
            return new NativeArray<T>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void Dispose<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LineHairBuildJob));
            }
        }

        private struct GroupData
        {
            public int RootOffset;
            public int RootCount;
            public int StrandOffset;
            public int StrandCount;
            public int GrowPoints;
            public int ScratchPoints;
            public int VerticesPerStrand;
            public int Seed;
            public float StrandLength;
            public float LengthVariance;
            public float ScalpFlow;
            public float Gravity;
            public float Stiffness;
            public int CopiesPerRoot;
            public int InstancedCopies;
            public int CopyOffsetStart;
            public int MultiplierStackIndex;
            public int FirstBarrier;
            public int PassStart;
            public int PassCount;
            public float ColorVariance;
            public float TipOpacity;
            public int SegmentLengthOffset;
            public int SegmentParameterOffset;
            public int OperatorStart;
            public int OperatorCount;
            public int OperatorWeightOffset;
            public int GuideStart;
            public int GuideCount;
            public int GuideFrames;
            public int GuidesDirect;
        }

        private struct RootData
        {
            public float3 Position;
            public float3 Normal;
            public float3 Tangent;
            public float2 Uv;
        }

        private enum OperatorType : byte
        {
            Curl,
            Cut,
            Frizz,
            Clump,
            Gravity,
            Noise,
            Multiplier,
            Braid,
            Rotate,
            Detail,
            Ground,
            Propagation,
            Delete,
            StrandChannel,
        }

        private enum PassKind : byte
        {
            Clump,
            Braid,
            Propagate,
        }

        private struct OperatorData
        {
            public OperatorType Type;
            public int Seed;
            public float A;
            public float B;
            public float C;
            public float D;
            public float E;
            public int Int0;
            public int Int1;
            public int Points;
            public int CurveOffset;
            public int BindA;
            public int BindB;
            public int BindC;
            public int BindD;
            public int BindE;
            public int BindCurve;
        }

        private struct PassData
        {
            public PassKind Kind;
            public int StackIndex;
            public int Points;
            public int WidthOffset;
            public int RadiusOffset;
            public int ClumpOrdinal;
        }

        private struct GuideData
        {
            public float2 Uv;
            public float3 Position;
            public float3 Normal;
            public int PointStart;
            public int PointCount;
        }

        private struct StrandWork
        {
            public int Group;
            public int LocalStrand;
            public int Copy;
            public int Root;
            public int VertexStart;
            public int ScratchStart;
            public int EdgeStart;
            public int SeedId;
            public int LineageSeed;
            public int Parent;
            public int BirthPass;
            public int ChildIndex;
            public int ChildCount;
            public float BirthT;
            public float Anchor;
        }

        private struct SphereData
        {
            public float3 Center;
            public float Radius;
        }

        private enum GroundMode : byte
        {
            None,
            Sphere,
            Mesh,
        }

        private struct GroundData
        {
            public GroundMode Mode;
            public SphereData Sphere;
            public float3 Origin;
            public float CellSize;
            public int3 Dims;
        }

        private struct StrandBounds
        {
            public float3 Minimum;
            public float3 Maximum;
        }

        private struct StackInputs
        {
            [ReadOnly] public NativeArray<OperatorData> operators;
            [ReadOnly] public NativeArray<float> operatorWeights;
            [ReadOnly] public NativeArray<float> curves;
            [ReadOnly] public NativeArray<RootData> roots;
            public GroundData ground;
            [ReadOnly] public NativeArray<float3> groundPositions;
            [ReadOnly] public NativeArray<int3> groundTriangles;
            [ReadOnly] public NativeArray<int> groundCells;
            [ReadOnly] public NativeArray<int> groundCellTriangles;
        }

        private struct StackBuffers
        {
            public NativeArray<float3> scratch;
            public NativeArray<float3> operatorScratch;
            public NativeArray<float> distances;
            public NativeArray<float3> rotations;
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct GrowStrandsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<GroupData> groups;
            [ReadOnly] public NativeArray<float> segmentLengthFractions;
            [ReadOnly] public NativeArray<float> segmentLengthParameters;
            [ReadOnly] public NativeArray<RootData> roots;
            [ReadOnly] public NativeArray<float2> comb;
            [ReadOnly] public NativeArray<GuideData> guides;
            [ReadOnly] public NativeArray<float3> guidePoints;
            [ReadOnly] public NativeArray<StrandWork> work;
            public SphereData sphere;
            public StackInputs stack;

            [NativeDisableParallelForRestriction] public NativeArray<float3> scratch;
            [NativeDisableParallelForRestriction] public NativeArray<float3> operatorScratch;
            [NativeDisableParallelForRestriction] public NativeArray<float> distances;
            [NativeDisableParallelForRestriction] public NativeArray<float3> rotations;

            public void Execute(int index)
            {
                StrandWork item = work[index];

                if (item.BirthPass >= 0)
                {
                    return;
                }

                GroupData group = groups[item.Group];
                RootData root = roots[item.Root];
                var random = new JobRandom(group.Seed * 6151 + item.SeedId * 31337 + 7);
                rotations[index] = float3.zero;

                Kernel.Integrate(root, group, comb[item.Root], segmentLengthFractions, ref random,
                                 scratch, item.ScratchStart, group.GrowPoints);

                if (group.GuideCount > 0)
                {
                    Kernel.ShapeFromGuides(root, group, ref random, guides, guidePoints,
                                           segmentLengthParameters, scratch, item.ScratchStart);
                }

                var buffers = new StackBuffers
                {
                    scratch = scratch,
                    operatorScratch = operatorScratch,
                    distances = distances,
                    rotations = rotations,
                };

                Kernel.ApplyOperators(ref stack, ref buffers, index, item, group, 0, group.FirstBarrier);
                Kernel.PushOut(scratch, item.ScratchStart, Kernel.PointsAt(stack.operators, group, group.FirstBarrier),
                               sphere);
            }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct OperatorRangeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<GroupData> groups;
            [ReadOnly] public NativeArray<PassData> passes;
            [ReadOnly] public NativeArray<StrandWork> work;
            public StackInputs stack;
            public int afterPass;
            public SphereData sphere;

            [NativeDisableParallelForRestriction] public NativeArray<float3> scratch;
            [NativeDisableParallelForRestriction] public NativeArray<float3> operatorScratch;
            [NativeDisableParallelForRestriction] public NativeArray<float> distances;
            [NativeDisableParallelForRestriction] public NativeArray<float3> rotations;

            public void Execute(int index)
            {
                StrandWork item = work[index];
                GroupData group = groups[item.Group];

                if (afterPass >= group.PassCount || item.BirthPass > afterPass)
                {
                    return;
                }

                int from = passes[group.PassStart + afterPass].StackIndex + 1;
                int to = afterPass + 1 < group.PassCount
                    ? passes[group.PassStart + afterPass + 1].StackIndex
                    : group.OperatorCount;

                if (from >= to)
                {
                    return;
                }

                var buffers = new StackBuffers
                {
                    scratch = scratch,
                    operatorScratch = operatorScratch,
                    distances = distances,
                    rotations = rotations,
                };

                Kernel.ApplyOperators(ref stack, ref buffers, index, item, group, from, to);
                Kernel.PushOut(scratch, item.ScratchStart, Kernel.PointsAt(stack.operators, group, to), sphere);
            }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct AssignClumpsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<GroupData> groups;
            [ReadOnly] public NativeArray<PassData> passes;
            [ReadOnly] public NativeArray<StrandWork> work;
            [ReadOnly] public NativeArray<RootData> roots;
            [ReadOnly] public NativeArray<GuideData> guides;
            [ReadOnly] public NativeArray<OperatorData> operators;
            [ReadOnly] public NativeArray<float> operatorWeights;
            [ReadOnly] public NativeArray<int> axes;
            public int pass;

            public NativeArray<int> clumpIds;
            public NativeArray<float> clumpDistances;

            public void Execute(int index)
            {
                StrandWork item = work[index];
                GroupData group = groups[item.Group];

                if (!Kernel.JoinsClumps(groups, passes, operators, operatorWeights, work, index, pass))
                {
                    clumpDistances[index] = float.MaxValue;
                    return;
                }

                PassData clumpPass = passes[group.PassStart + pass];

                if (clumpPass.ClumpOrdinal > 0)
                {
                    int parent = axes[index];
                    clumpIds[index] = parent;
                    clumpDistances[index] =
                        math.distance(roots[item.Root].Position, roots[work[parent].Root].Position);
                    return;
                }

                if (group.GuideCount <= 0)
                {
                    clumpIds[index] = item.Group;
                    clumpDistances[index] = 0f;
                    return;
                }

                RootData root = roots[item.Root];
                int nearest = -1;
                float best = float.MaxValue;

                for (int local = 0; local < group.GuideCount; local++)
                {
                    int guideIndex = group.GuideStart + local;

                    float distance = Kernel.GuideDistance(root, guides[guideIndex], group.GuideFrames);

                    if (distance < best)
                    {
                        best = distance;
                        nearest = guideIndex;
                    }
                }

                clumpIds[index] = nearest;
                clumpDistances[index] = best;
            }
        }

        [BurstCompile]
        private struct SelectClumpAxesJob : IJob
        {
            [ReadOnly] public NativeArray<GroupData> groups;
            [ReadOnly] public NativeArray<PassData> passes;
            [ReadOnly] public NativeArray<float> passCurves;
            [ReadOnly] public NativeArray<StrandWork> work;
            [ReadOnly] public NativeArray<RootData> roots;
            [ReadOnly] public NativeArray<OperatorData> operators;
            [ReadOnly] public NativeArray<float> operatorWeights;
            [ReadOnly] public NativeArray<int> clumpIds;
            [ReadOnly] public NativeArray<float> clumpDistances;
            public int pass;

            public NativeArray<long> keys;
            public NativeArray<int> axes;
            public NativeArray<float> seats;

            public void Execute()
            {
                for (int i = 0; i < work.Length; i++)
                {
                    bool joins = Kernel.JoinsClumps(groups, passes, operators, operatorWeights, work, i, pass);
                    keys[i] = joins ? ((long)(clumpIds[i] + 1) << 32) | (uint)i : (uint)i;

                    if (Kernel.IsClumpPass(groups, passes, work[i].Group, pass))
                    {
                        axes[i] = i;
                        seats[i] = 0f;
                    }
                }

                keys.Sort();

                int runStart = 0;

                for (int i = 1; i <= keys.Length; i++)
                {
                    bool end = i == keys.Length || (keys[i] >> 32) != (keys[runStart] >> 32);

                    if (!end)
                    {
                        continue;
                    }

                    if ((keys[runStart] >> 32) > 0)
                    {
                        SeatRun(runStart, i);
                    }

                    runStart = i;
                }
            }

            private void SeatRun(int from, int to)
            {
                GroupData group = groups[work[StrandAt(from)].Group];
                PassData clumpPass = passes[group.PassStart + pass];
                float influence = passCurves[clumpPass.WidthOffset];

                if (influence <= 1e-6f)
                {
                    return;
                }

                if (clumpPass.ClumpOrdinal == 0 && group.GuideCount > 0)
                {
                    SeatAroundGuide(from, to, influence);
                    return;
                }

                SeatBySpacing(from, to, influence);
            }

            private void SeatAroundGuide(int from, int to, float influence)
            {
                int axis = StrandAt(from);
                float nearest = clumpDistances[axis];

                for (int j = from + 1; j < to; j++)
                {
                    int strand = StrandAt(j);

                    if (clumpDistances[strand] < nearest)
                    {
                        nearest = clumpDistances[strand];
                        axis = strand;
                    }
                }

                float3 centre = roots[work[axis].Root].Position;

                for (int j = from; j < to; j++)
                {
                    int strand = StrandAt(j);
                    float radius = math.distance(roots[work[strand].Root].Position, centre);

                    if (radius > influence)
                    {
                        continue;
                    }

                    axes[strand] = axis;
                    seats[strand] = radius / influence;
                }
            }

            private void SeatBySpacing(int from, int to, float influence)
            {
                var centres = new NativeList<int>(16, Allocator.Temp);

                for (int j = from; j < to; j++)
                {
                    int strand = StrandAt(j);
                    float3 position = roots[work[strand].Root].Position;
                    bool isolated = true;

                    for (int c = 0; c < centres.Length; c++)
                    {
                        if (math.distance(position, roots[work[centres[c]].Root].Position) <= influence)
                        {
                            isolated = false;
                            break;
                        }
                    }

                    if (isolated)
                    {
                        centres.Add(strand);
                    }
                }

                for (int j = from; j < to; j++)
                {
                    int strand = StrandAt(j);
                    float3 position = roots[work[strand].Root].Position;
                    int axis = -1;
                    float nearest = influence;

                    for (int c = 0; c < centres.Length; c++)
                    {
                        float distance = math.distance(position, roots[work[centres[c]].Root].Position);

                        if (distance <= nearest)
                        {
                            nearest = distance;
                            axis = centres[c];
                        }
                    }

                    if (axis < 0)
                    {
                        continue;
                    }

                    axes[strand] = axis;
                    seats[strand] = nearest / influence;
                }

                centres.Dispose();
            }

            private int StrandAt(int keyIndex)
            {
                return (int)(keys[keyIndex] & 0xFFFFFFFFL);
            }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct ClumpOperatorJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<GroupData> groups;
            [ReadOnly] public NativeArray<PassData> passes;
            [ReadOnly] public NativeArray<float> passCurves;
            [ReadOnly] public NativeArray<StrandWork> work;
            [ReadOnly] public NativeArray<RootData> roots;
            [ReadOnly] public NativeArray<OperatorData> operators;
            [ReadOnly] public NativeArray<float> operatorWeights;
            [ReadOnly] public NativeArray<float3> rotations;
            [ReadOnly] public NativeArray<int> axes;
            [ReadOnly] public NativeArray<float> seats;
            public int pass;
            public SphereData sphere;

            [NativeDisableParallelForRestriction] public NativeArray<float3> scratch;
            [NativeDisableParallelForRestriction] public NativeArray<float3> operatorScratch;
            [NativeDisableParallelForRestriction] public NativeArray<float> distances;

            public void Execute(int index)
            {
                StrandWork item = work[index];
                GroupData group = groups[item.Group];
                int axis = axes[index];

                if (!Kernel.IsClumpPass(groups, passes, item.Group, pass) || axis == index)
                {
                    return;
                }

                StrandWork axisItem = work[axis];
                PassData clumpPass = passes[group.PassStart + pass];
                OperatorData groomOperator = operators[group.OperatorStart + clumpPass.StackIndex];
                float weight = operatorWeights[group.OperatorWeightOffset +
                                               item.LocalStrand * group.OperatorCount + clumpPass.StackIndex];

                if (axisItem.Group != item.Group || weight <= 0f)
                {
                    return;
                }

                int count = clumpPass.Points;
                float seat = seats[index];
                Kernel.CaptureSegmentLengths(scratch, item.ScratchStart, count, distances);

                if (clumpPass.Kind == PassKind.Braid)
                {
                    Braid(item, axisItem, axis, group, clumpPass, groomOperator, count, seat, weight);
                    Kernel.FollowPath(scratch, operatorScratch, item.ScratchStart, count, distances);
                }
                else
                {
                    Clump(item, axisItem, group, clumpPass, groomOperator, count, seat, weight);
                    Kernel.RestoreSegmentLengths(scratch, item.ScratchStart, count, distances);
                }

                Kernel.PushOut(scratch, item.ScratchStart, count, sphere);
            }

            private void Clump(StrandWork item, StrandWork axisItem, GroupData group, PassData clumpPass,
                               OperatorData groomOperator, int count, float seat, float weight)
            {
                float stray = groomOperator.B * Kernel.MapValue(operatorWeights, group, item, groomOperator.BindB);

                if (Strays(stray, group.Seed, groomOperator.Seed, item.SeedId, clumpPass.StackIndex))
                {
                    return;
                }

                float scale = Kernel.MapValue(operatorWeights, group, item, groomOperator.BindA);

                for (int k = 1; k < count; k++)
                {
                    int point = item.ScratchStart + k;
                    float3 centre = scratch[axisItem.ScratchStart + k];
                    float3 forward = AxisTangent(axisItem, k, count);
                    float3 offset = scratch[point] - centre;
                    float along = math.dot(offset, forward);
                    float3 radial = offset - forward * along;
                    float radius = math.length(radial);

                    float3 direction = radius > 1e-6f
                        ? radial / radius
                        : Kernel.Perpendicular(forward);

                    float3 target = centre + forward * along +
                                    direction * (passCurves[clumpPass.WidthOffset + k] * scale * seat);
                    scratch[point] = math.lerp(scratch[point], target, weight);
                }
            }

            private void Braid(StrandWork item, StrandWork axisItem, int axis, GroupData group, PassData braidPass,
                               OperatorData groomOperator, int count, float seat, float weight)
            {
                int stems = math.max(1, groomOperator.Int0);
                float frequency = groomOperator.A * Kernel.MapValue(operatorWeights, group, item, groomOperator.BindA);
                float flatness = groomOperator.B * Kernel.MapValue(operatorWeights, group, item, groomOperator.BindB);
                float widthScale = Kernel.MapValue(operatorWeights, group, item, groomOperator.BindC);
                float radiusScale = Kernel.MapValue(operatorWeights, group, item, groomOperator.BindD);
                float3 twist = rotations[axis];
                float rotation = twist.z > 0f ? twist.x : 0f;
                float twistAmount = twist.z > 0f ? twist.y : 0f;

                RootData axisRoot = roots[axisItem.Root];
                float3 tangent = AxisTangent(axisItem, 0, count);
                float3 normal = Kernel.Orthogonal(axisRoot.Tangent, tangent);
                float3 binormal = math.cross(tangent, normal);
                float3 fromAxis = roots[item.Root].Position - axisRoot.Position;
                float sector = math.atan2(math.dot(fromAxis, binormal), math.dot(fromAxis, normal)) - rotation;
                int stem = (int)math.floor(math.frac(sector / (2f * math.PI)) * stems) % stems;
                var phaseRandom = Kernel.RandomFor(group.Seed, axisItem.SeedId, groomOperator.Seed);
                float phase = phaseRandom.Range(0f, 2f * math.PI);
                operatorScratch[item.ScratchStart] = scratch[item.ScratchStart];

                for (int k = 1; k < count; k++)
                {
                    int point = item.ScratchStart + k;
                    float u = k / (float)(count - 1);
                    float3 forward = AxisTangent(axisItem, k, count);
                    normal = Kernel.Orthogonal(normal, forward);
                    float turn = rotation + twistAmount * u;
                    float3 side = normal * math.cos(turn) + math.cross(forward, normal) * math.sin(turn);
                    float3 up = math.cross(forward, side);

                    float theta = phase + 2f * math.PI * (frequency * u + stem / (float)stems);
                    float swing = passCurves[braidPass.RadiusOffset + k] * radiusScale;
                    float3 centre = scratch[axisItem.ScratchStart + k] +
                                    (side * math.cos(theta) + up * (flatness * 0.5f * math.sin(2f * theta))) * swing;

                    float3 offset = scratch[point] - centre;
                    float along = math.dot(offset, forward);
                    float3 radial = offset - forward * along;
                    float radius = math.length(radial);
                    float3 direction = radius > 1e-6f ? radial / radius : side;

                    float3 target = centre + forward * along +
                                    direction * (passCurves[braidPass.WidthOffset + k] * widthScale * seat);
                    operatorScratch[point] = math.lerp(scratch[point], target, weight);
                }
            }

            private float3 AxisTangent(StrandWork axisItem, int k, int count)
            {
                int head = axisItem.ScratchStart + math.min(k + 1, count - 1);
                int tail = axisItem.ScratchStart + math.max(k - 1, 0);
                float3 direction = scratch[head] - scratch[tail];

                return math.lengthsq(direction) > 1e-16f
                    ? math.normalize(direction)
                    : new float3(0f, 1f, 0f);
            }

            private static bool Strays(float stray, int seed, int operatorSeed, int strand, int stackIndex)
            {
                if (stray <= 0f)
                {
                    return false;
                }

                uint hash = math.hash(new int4(strand, seed, stackIndex, operatorSeed));
                return (hash & 0xFFFFFFu) / 16777216f < stray;
            }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct PropagateJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<GroupData> groups;
            [ReadOnly] public NativeArray<PassData> passes;
            [ReadOnly] public NativeArray<StrandWork> work;
            [ReadOnly] public NativeArray<RootData> roots;
            [ReadOnly] public NativeArray<OperatorData> operators;
            [ReadOnly] public NativeArray<float> operatorWeights;
            [ReadOnly] public NativeArray<float> curves;
            public int pass;

            [NativeDisableParallelForRestriction] public NativeArray<float3> scratch;
            [NativeDisableParallelForRestriction] public NativeArray<float> distances;
            [NativeDisableParallelForRestriction] public NativeArray<float3> rotations;

            public void Execute(int index)
            {
                StrandWork item = work[index];

                if (item.BirthPass != pass)
                {
                    return;
                }

                GroupData group = groups[item.Group];
                PassData propagation = passes[group.PassStart + pass];
                OperatorData groomOperator = operators[group.OperatorStart + propagation.StackIndex];
                StrandWork parent = work[item.Parent];
                int count = propagation.Points;
                var random = Kernel.RandomFor(group.Seed, item.SeedId, groomOperator.Seed);

                float randomness = math.saturate(groomOperator.D *
                                                 Kernel.MapValue(operatorWeights, group, item, groomOperator.BindD));
                float fan = groomOperator.C * Kernel.MapValue(operatorWeights, group, item, groomOperator.BindC);
                fan = math.clamp(fan * (1f + random.Range(-0.5f, 0.5f) * randomness), 0f, math.PI);

                distances[item.ScratchStart] = 0f;
                for (int k = 1; k < count; k++)
                {
                    distances[item.ScratchStart + k] = distances[item.ScratchStart + k - 1] +
                        math.distance(scratch[parent.ScratchStart + k], scratch[parent.ScratchStart + k - 1]);
                }

                float wanted = item.BirthT * distances[item.ScratchStart + count - 1];
                int segment = 1;
                while (segment < count - 1 && distances[item.ScratchStart + segment] < wanted)
                {
                    segment++;
                }

                float start = distances[item.ScratchStart + segment - 1];
                float span = distances[item.ScratchStart + segment] - start;
                float blend = span > 1e-8f ? math.saturate((wanted - start) / span) : 0f;
                float3 tail = scratch[parent.ScratchStart + segment - 1];
                float3 head = scratch[parent.ScratchStart + segment];
                float3 anchor = math.lerp(tail, head, blend);
                float3 tangent = math.lengthsq(head - tail) > 1e-16f
                    ? math.normalize(head - tail)
                    : roots[parent.Root].Normal;

                float3 up = Kernel.Orthogonal(roots[parent.Root].Normal, tangent);
                float3 across = math.cross(tangent, up);
                float3 parentTwist = rotations[item.Parent];
                float turn = parentTwist.z > 0f ? parentTwist.x + parentTwist.y * item.BirthT : 0f;
                int sides = math.max(1, groomOperator.Int0);
                float sector = 2f * math.PI / sides;
                float phi = turn + sector * (item.ChildIndex % sides) + random.Range(-0.5f, 0.5f) * sector * randomness;
                float3 outward = across * math.cos(phi) + up * math.sin(phi);
                float3 direction = tangent * math.cos(fan) + outward * math.sin(fan);

                float length = Kernel.SampleCurve(curves, groomOperator.CurveOffset, LengthSamples, item.BirthT) *
                               Kernel.MapValue(operatorWeights, group, item, groomOperator.BindCurve) *
                               (1f + random.Range(-0.5f, 0.5f) * randomness);

                for (int k = 0; k < count; k++)
                {
                    scratch[item.ScratchStart + k] = anchor + direction * (length * k / (count - 1f));
                }

                rotations[index] = parentTwist;
            }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct CopyOffsetsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<GroupData> groups;
            [ReadOnly] public NativeArray<StrandWork> work;
            [ReadOnly] public NativeArray<RootData> roots;
            [ReadOnly] public NativeArray<OperatorData> operators;
            [ReadOnly] public NativeArray<float> operatorWeights;

            [NativeDisableParallelForRestriction] public NativeArray<float3> offsets;

            public void Execute(int index)
            {
                StrandWork item = work[index];
                GroupData group = groups[item.Group];

                if (group.InstancedCopies <= 0 || group.MultiplierStackIndex < 0)
                {
                    return;
                }

                RootData root = roots[item.Root];
                OperatorData groomOperator = operators[group.OperatorStart + group.MultiplierStackIndex];
                float weight = operatorWeights[group.OperatorWeightOffset +
                                               item.LocalStrand * group.OperatorCount + group.MultiplierStackIndex];
                float3 bitangent = math.cross(root.Normal, root.Tangent);
                int ordinal = index - group.StrandOffset;

                for (int copy = 0; copy < group.InstancedCopies; copy++)
                {
                    int slot = group.CopyOffsetStart + ordinal * group.InstancedCopies + copy;

                    if (weight <= 0f)
                    {
                        offsets[slot] = float3.zero;
                        continue;
                    }

                    var random = Kernel.RandomFor(group.Seed, item.LineageSeed * group.InstancedCopies + copy,
                                                  groomOperator.Seed);
                    float angle = random.Range(0f, math.PI * 2f);
                    float3 direction = root.Tangent * math.cos(angle) + bitangent * math.sin(angle);
                    offsets[slot] = direction * (math.sqrt(random.Value) * weight);
                }
            }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct FinalizeStrandsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<GroupData> groups;
            [ReadOnly] public NativeArray<StrandWork> work;
            [ReadOnly] public NativeArray<float3> scratch;
            [ReadOnly] public NativeArray<RootData> roots;
            [ReadOnly] public NativeArray<uint3> rootColors;
            [ReadOnly] public NativeArray<int> clumpIds;

            [WriteOnly] public NativeArray<Vector4> strandDebug;
            [WriteOnly] public NativeArray<float> strandShade;
            [WriteOnly] public NativeArray<ulong> strandHash;
            [WriteOnly] public NativeArray<uint3> strandAttributes;

            [NativeDisableParallelForRestriction] public NativeArray<Vector3> positions;
            [NativeDisableParallelForRestriction] public NativeArray<uint> strandInfo;
            [NativeDisableParallelForRestriction] public NativeArray<float> vertexOpacity;
            [NativeDisableParallelForRestriction] public NativeArray<uint> indices;
            [NativeDisableParallelForRestriction] public NativeArray<Vector2Int> strandRanges;
            [NativeDisableParallelForRestriction] public NativeArray<uint> vertexGroups;
            [NativeDisableParallelForRestriction] public NativeArray<StrandBounds> bounds;

            public void Execute(int index)
            {
                StrandWork item = work[index];
                GroupData group = groups[item.Group];
                var random = new JobRandom(group.Seed * 7919 + item.SeedId * 104729 + 11);
                int count = group.VerticesPerStrand;
                int edges = count - 1;

                float shade = math.max(0f, 1f + random.Range(-group.ColorVariance, group.ColorVariance));
                uint packedShade = (uint)math.clamp((int)math.round(shade * 127f), 0, 255);
                uint countBits = (uint)math.clamp(count, 1, 4095) << 12;
                uint groupIndex = (uint)math.max(0, item.Group);

                float3 minimum = scratch[item.ScratchStart];
                float3 maximum = minimum;
                float length = 0f;
                ulong hash = 14695981039346656037UL;

                for (int k = 0; k < count; k++)
                {
                    int point = item.VertexStart + k;
                    float3 position = scratch[item.ScratchStart + k];
                    int edge = math.clamp(k, 0, math.max(edges - 1, 0));

                    positions[point] = position;
                    vertexGroups[point] = groupIndex;
                    strandInfo[point] = (uint)math.min(k, 4095) | countBits | (packedShade << 24);
                    vertexOpacity[point] = math.lerp(1f, group.TipOpacity,
                                                     edges > 1 ? edge / (float)(edges - 1) : 0f);

                    minimum = math.min(minimum, position);
                    maximum = math.max(maximum, position);

                    if (k > 0)
                    {
                        length += math.distance(position, scratch[item.ScratchStart + k - 1]);
                    }

                    hash = Mix(hash, math.asuint(position.x));
                    hash = Mix(hash, math.asuint(position.y));
                    hash = Mix(hash, math.asuint(position.z));
                }

                bounds[index] = new StrandBounds { Minimum = minimum, Maximum = maximum };

                for (int edge = 0; edge < edges; edge++)
                {
                    int output = item.EdgeStart + edge;
                    indices[output * 2] = (uint)(item.VertexStart + edge);
                    indices[output * 2 + 1] = (uint)(item.VertexStart + edge + 1);
                }

                strandRanges[index] = new Vector2Int(item.VertexStart, count);
                strandShade[index] = shade;

                float2 rootUv = roots[item.Root].Uv;
                strandDebug[index] = new Vector4(rootUv.x, rootUv.y, clumpIds[index], length);

                uint3 attributes = rootColors[item.Root];
                strandAttributes[index] = attributes;

                hash = Mix(hash, packedShade);
                hash = Mix(hash, math.asuint(group.TipOpacity));
                hash = Mix(hash, (uint)clumpIds[index]);
                hash = Mix(hash, math.asuint(length));
                hash = Mix(hash, attributes.x);
                hash = Mix(hash, attributes.y);
                hash = Mix(hash, attributes.z);
                strandHash[index] = hash;
            }

            private static ulong Mix(ulong hash, uint value)
            {
                return (hash ^ value) * 1099511628211UL;
            }
        }

        private static class Kernel
        {
            private const float CombThreshold = 1e-3f;
            private const float DistanceFloor = 1e-4f;

            public static int PointsAt(NativeArray<OperatorData> operators, GroupData group, int stackIndex)
            {
                return stackIndex < group.OperatorCount
                    ? operators[group.OperatorStart + stackIndex].Points
                    : group.VerticesPerStrand;
            }

            public static float MapValue(NativeArray<float> operatorWeights, GroupData group, StrandWork item,
                                        int slot)
            {
                return slot < 0
                    ? 1f
                    : operatorWeights[group.OperatorWeightOffset + item.LocalStrand * group.OperatorCount + slot];
            }

            public static bool IsClumpPass(NativeArray<GroupData> groups, NativeArray<PassData> passes, int group,
                                           int pass)
            {
                GroupData data = groups[group];
                return pass < data.PassCount && passes[data.PassStart + pass].Kind != PassKind.Propagate;
            }

            public static bool JoinsClumps(NativeArray<GroupData> groups, NativeArray<PassData> passes,
                                           NativeArray<OperatorData> operators, NativeArray<float> operatorWeights,
                                           NativeArray<StrandWork> work,
                                           int index, int pass)
            {
                StrandWork item = work[index];

                if (item.BirthPass >= 0 || !IsClumpPass(groups, passes, item.Group, pass))
                {
                    return false;
                }

                GroupData group = groups[item.Group];
                int stackIndex = passes[group.PassStart + pass].StackIndex;
                OperatorData groomOperator = operators[group.OperatorStart + stackIndex];

                return operatorWeights[group.OperatorWeightOffset + item.LocalStrand * group.OperatorCount +
                                       stackIndex] > 0f;
            }

            public static void Integrate(RootData root, GroupData group, float2 comb,
                                         NativeArray<float> segmentLengthFractions, ref JobRandom random,
                                         NativeArray<float3> points, int offset, int count)
            {
                int segments = count - 1;
                float length = group.StrandLength *
                               (1f + random.Range(-group.LengthVariance, group.LengthVariance));
                float3 lay = FlowDirection(root, comb);
                float3 direction = root.Normal;
                float3 point = root.Position;
                points[offset] = point;

                for (int k = 1; k <= segments; k++)
                {
                    float along = k / (float)segments;
                    float3 pull = new float3(0f, -group.Gravity * along, 0f);
                    float3 settled = NormalizeSafe(Slerp(direction, lay, group.ScalpFlow * along),
                                                   direction);
                    direction = NormalizeSafe(settled * group.Stiffness + pull, root.Normal);
                    point += direction * (length *
                        segmentLengthFractions[group.SegmentLengthOffset + k - 1]);
                    points[offset + k] = point;
                }
            }

            public static void ShapeFromGuides(RootData root, GroupData group, ref JobRandom random,
                                               NativeArray<GuideData> guides,
                                               NativeArray<float3> guidePoints,
                                               NativeArray<float> segmentLengthParameters,
                                               NativeArray<float3> destination, int offset)
            {
                var indices = new FixedList64Bytes<int>();
                var weights = new FixedList64Bytes<float>();
                int wanted = math.min(4, group.GuideCount);

                for (int i = 0; i < wanted; i++)
                {
                    indices.Add(-1);
                    weights.Add(float.MaxValue);
                }

                for (int local = 0; local < group.GuideCount; local++)
                {
                    int guideIndex = group.GuideStart + local;
                    float distance = GuideDistance(root, guides[guideIndex], group.GuideFrames);

                    if (distance == float.MaxValue)
                    {
                        continue;
                    }

                    int slot = wanted;

                    while (slot > 0 && weights[slot - 1] > distance)
                    {
                        slot--;
                    }

                    if (slot >= wanted)
                    {
                        continue;
                    }

                    for (int move = wanted - 1; move > slot; move--)
                    {
                        indices[move] = indices[move - 1];
                        weights[move] = weights[move - 1];
                    }

                    indices[slot] = guideIndex;
                    weights[slot] = distance;
                }

                if (indices[0] < 0)
                {
                    return;
                }

                float total = 0f;
                float nearest = weights[0] + DistanceFloor;
                for (int i = 0; i < wanted; i++)
                {
                    float ratio = nearest / (weights[i] + DistanceFloor);
                    weights[i] = ratio * ratio;
                    total += weights[i];
                }

                for (int i = 0; i < wanted; i++)
                {
                    weights[i] = total > 0f ? weights[i] / total : 0f;
                }

                float scale = 1f + random.Range(-group.LengthVariance, group.LengthVariance);
                int count = group.GrowPoints;

                for (int k = 0; k < count; k++)
                {
                    float along = segmentLengthParameters[group.SegmentParameterOffset + k];
                    float3 localPoint = float3.zero;

                    for (int i = 0; i < wanted; i++)
                    {
                        if (indices[i] >= 0)
                        {
                            GuideData guide = guides[indices[i]];
                            localPoint += (group.GuidesDirect != 0
                                ? guidePoints[guide.PointStart + k]
                                : SampleGuide(guide, guidePoints, along)) * weights[i];
                        }
                    }

                    destination[offset + k] = root.Position + localPoint * scale;
                }
            }

            public static void ApplyOperators(ref StackInputs stack, ref StackBuffers buffers, int index,
                                              StrandWork item, GroupData group, int from, int to)
            {
                int offset = item.ScratchStart;

                for (int local = from; local < to; local++)
                {
                    OperatorData groomOperator = stack.operators[group.OperatorStart + local];
                    float weight = stack.operatorWeights[group.OperatorWeightOffset +
                                                         item.LocalStrand * group.OperatorCount + local];
                    int count = groomOperator.Points;

                    if (groomOperator.Type == OperatorType.Detail)
                    {
                        float smoothing = groomOperator.A * weight *
                                          MapValue(stack.operatorWeights, group, item, groomOperator.BindA);
                        ApplyDetail(groomOperator, math.saturate(smoothing), stack.curves, buffers, offset, count);
                        continue;
                    }

                    if (weight <= 0f)
                    {
                        continue;
                    }

                    groomOperator.A *= MapValue(stack.operatorWeights, group, item, groomOperator.BindA);
                    groomOperator.B *= MapValue(stack.operatorWeights, group, item, groomOperator.BindB);
                    groomOperator.C *= MapValue(stack.operatorWeights, group, item, groomOperator.BindC);

                    switch (groomOperator.Type)
                    {
                        case OperatorType.Curl:
                            ApplyCurl(group, groomOperator, stack.roots[item.Root], item.SeedId, weight,
                                      buffers.rotations[index], buffers.scratch, offset, count);
                            break;
                        case OperatorType.Cut:
                            ApplyCut(group, groomOperator, item.SeedId, weight, buffers.scratch,
                                     buffers.operatorScratch, buffers.distances, offset, count);
                            break;
                        case OperatorType.Frizz:
                            ApplyFrizz(group, groomOperator, item.SeedId, weight, buffers.scratch, offset, count);
                            break;
                        case OperatorType.Gravity:
                            ApplyGravity(groomOperator, weight, buffers.scratch, buffers.distances, offset, count);
                            break;
                        case OperatorType.Noise:
                            ApplyNoise(groomOperator, weight, buffers.scratch, offset, count);
                            break;
                        case OperatorType.Multiplier:
                            if (group.InstancedCopies <= 0)
                            {
                                ApplyMultiplier(stack.roots[item.Root], group, groomOperator, item, weight,
                                                buffers.scratch, offset, count);
                            }

                            break;
                        case OperatorType.Rotate:
                        {
                            var random = RandomFor(group.Seed, item.SeedId, groomOperator.Seed);
                            float angle = (groomOperator.A + random.Range(-1f, 1f) * groomOperator.B) * weight;
                            buffers.rotations[index] = new float3(angle, groomOperator.C * weight, 1f);
                            break;
                        }
                        case OperatorType.Ground:
                            ApplyGround(ref stack, groomOperator, weight, buffers.scratch, offset, count);
                            break;
                    }
                }
            }

            public static void PushOut(NativeArray<float3> points, int offset, int count, SphereData sphere)
            {
                if (sphere.Radius <= 0f)
                {
                    return;
                }

                for (int i = 1; i < count; i++)
                {
                    int index = offset + i;
                    float3 fromCenter = points[index] - sphere.Center;
                    float distance = math.length(fromCenter);

                    if (distance < sphere.Radius && distance >= 1e-6f)
                    {
                        points[index] = sphere.Center + fromCenter * (sphere.Radius / distance);
                    }
                }
            }

            private static void ApplyCurl(GroupData group, OperatorData groomOperator, RootData root, int strand,
                                          float weight, float3 rotation, NativeArray<float3> points, int offset,
                                          int count)
            {
                if (groomOperator.A <= 0f || groomOperator.B <= 0f || count < 2)
                {
                    return;
                }

                var random = RandomFor(group.Seed, strand, groomOperator.Seed);
                int segments = count - 1;
                float3 firstTangent = NormalizeSafe(points[offset + 1] - points[offset], new float3(0f, 1f, 0f));
                bool framed = rotation.z > 0f;
                float3 frameNormal = framed ? Orthogonal(root.Tangent, firstTangent) : Perpendicular(firstTangent);
                float phase = random.Range(0f, math.PI * 2f);
                float radius = groomOperator.A * weight * (0.6f + 0.8f * random.Value);
                float3 previous = points[offset];

                if (framed)
                {
                    phase = rotation.x;
                }

                for (int k = 1; k <= segments; k++)
                {
                    int index = offset + k;
                    float3 uncurled = points[index];
                    float3 tangent = uncurled - previous;
                    previous = uncurled;

                    if (math.lengthsq(tangent) < 1e-12f)
                    {
                        continue;
                    }

                    tangent = math.normalize(tangent);
                    frameNormal -= tangent * math.dot(frameNormal, tangent);
                    if (math.lengthsq(frameNormal) < 1e-8f)
                    {
                        frameNormal = math.cross(tangent, new float3(0f, 1f, 0f));
                    }

                    frameNormal = NormalizeSafe(frameNormal, Perpendicular(tangent));
                    float3 frameBitangent = math.cross(tangent, frameNormal);
                    float along = k / (float)segments;
                    float angle = phase + along * groomOperator.B * math.PI * 2f + (framed ? rotation.y * along : 0f);
                    float ramp = SmoothStep(math.min(1f, along * 3f));
                    points[index] = uncurled
                                    + (frameNormal * math.cos(angle) + frameBitangent * math.sin(angle))
                                    * (radius * ramp);
                }
            }

            private static void ApplyCut(GroupData group, OperatorData groomOperator, int strand,
                                         float weight, NativeArray<float3> points,
                                         NativeArray<float3> scratch, NativeArray<float> distances, int offset,
                                         int count)
            {
                if (count < 2)
                {
                    return;
                }

                var random = RandomFor(group.Seed, strand, groomOperator.Seed);
                float keep = math.lerp(1f,
                    groomOperator.A * (1f + random.Range(-groomOperator.B, groomOperator.B)), weight);
                keep = math.clamp(keep, 0.02f, 1f);

                if (keep >= 0.999f)
                {
                    return;
                }

                scratch[offset] = points[offset];
                distances[offset] = 0f;
                for (int k = 1; k < count; k++)
                {
                    scratch[offset + k] = points[offset + k];
                    distances[offset + k] = distances[offset + k - 1]
                                            + math.distance(points[offset + k], points[offset + k - 1]);
                }

                float total = distances[offset + count - 1];
                if (total < 1e-6f)
                {
                    return;
                }

                int segment = 1;
                for (int k = 1; k < count; k++)
                {
                    float wanted = total * keep * k / (count - 1);
                    while (segment < count - 1 && distances[offset + segment] < wanted)
                    {
                        segment++;
                    }

                    float from = distances[offset + segment - 1];
                    float span = distances[offset + segment] - from;
                    float blend = span > 1e-8f ? math.saturate((wanted - from) / span) : 0f;
                    points[offset + k] = math.lerp(scratch[offset + segment - 1],
                                                   scratch[offset + segment], blend);
                }
            }

            private static void ApplyMultiplier(RootData root, GroupData group, OperatorData groomOperator,
                                                StrandWork item, float weight, NativeArray<float3> points,
                                                int offset, int count)
            {
                int copies = (int)groomOperator.C;
                int stride = math.max(1, (int)groomOperator.D);
                int band = copies > 1 ? (item.Copy / stride) % copies : 0;

                if (band == 0 || weight <= 0f)
                {
                    return;
                }

                var random = RandomFor(group.Seed, item.LineageSeed, groomOperator.Seed + band * 7919);
                float3 bitangent = math.cross(root.Normal, root.Tangent);
                float angle = random.Range(0f, math.PI * 2f);
                float3 direction = root.Tangent * math.cos(angle) + bitangent * math.sin(angle);
                float reach = math.sqrt(random.Value);
                int segments = count - 1;
                bool grown = item.BirthPass >= 0;

                for (int k = 0; k < count; k++)
                {
                    float along = grown ? item.Anchor : k / (float)segments;
                    float spread = math.lerp(groomOperator.A, groomOperator.B, along) * weight * reach;
                    points[offset + k] += direction * spread;
                }
            }

            private static void ApplyGravity(OperatorData groomOperator, float weight,
                                             NativeArray<float3> points, NativeArray<float> lengths,
                                             int offset, int count)
            {
                if (groomOperator.A <= 0f || count < 2)
                {
                    return;
                }

                CaptureSegmentLengths(points, offset, count, lengths);

                float3 down = new float3(0f, -1f, 0f);
                float3 point = points[offset];
                float3 previous = point;
                int segments = count - 1;

                for (int k = 1; k <= segments; k++)
                {
                    int index = offset + k;
                    float3 direction = points[index] - previous;
                    previous = points[index];

                    if (math.lengthsq(direction) < 1e-16f)
                    {
                        continue;
                    }

                    float along = k / (float)segments;
                    float denominator = 1f - groomOperator.B;
                    float ramp = math.abs(denominator) > 1e-8f
                        ? SmoothStep(math.saturate((along - groomOperator.B) / denominator))
                        : 0f;

                    direction = math.normalize(direction);
                    direction = math.normalize(math.lerp(direction, down,
                                                         groomOperator.A * weight * ramp));
                    point += direction * lengths[index];
                    points[index] = point;
                }
            }

            private static void ApplyNoise(OperatorData groomOperator, float weight,
                                           NativeArray<float3> points, int offset, int count)
            {
                if (groomOperator.A <= 0f || count < 2)
                {
                    return;
                }

                float frequency = 1f / math.max(0.001f, groomOperator.B);
                float amount = groomOperator.A * weight;
                var shift = new float3(37.13f, 17.71f, 91.37f);
                int segments = count - 1;

                for (int k = 1; k <= segments; k++)
                {
                    int index = offset + k;
                    float along = k / (float)segments;
                    float denominator = 1f - groomOperator.C;
                    float ramp = math.abs(denominator) > 1e-8f
                        ? SmoothStep(math.saturate((along - groomOperator.C) / denominator))
                        : 0f;

                    if (ramp <= 0f)
                    {
                        continue;
                    }

                    float3 sample = points[index] * frequency + groomOperator.Seed * 0.137f;
                    var wave = new float3(noise.snoise(sample),
                                          noise.snoise(sample + shift),
                                          noise.snoise(sample + shift * 2f));

                    points[index] += wave * (amount * ramp);
                }
            }

            private static void ApplyFrizz(GroupData group, OperatorData groomOperator, int strand,
                                           float weight, NativeArray<float3> points, int offset, int count)
            {
                if (groomOperator.A <= 0f || count < 2)
                {
                    return;
                }

                var random = RandomFor(group.Seed, strand, groomOperator.Seed);
                float3 first = random.OnUnitSphere();
                float3 second = random.OnUnitSphere();
                float phase = random.Range(0f, math.PI * 2f);
                float turns = math.PI * 2f / math.max(0.05f, groomOperator.B);
                int segments = count - 1;
                float amount = groomOperator.A * weight;

                for (int k = 1; k <= segments; k++)
                {
                    float along = k / (float)segments;
                    float denominator = 1f - groomOperator.C;
                    float inverse = math.abs(denominator) > 1e-8f
                        ? math.saturate((along - groomOperator.C) / denominator)
                        : 0f;
                    float ramp = SmoothStep(inverse);

                    if (ramp <= 0f)
                    {
                        continue;
                    }

                    float angle = phase + along * turns;
                    float3 wobble = first * math.sin(angle) + second * math.cos(angle * 0.61803f);
                    points[offset + k] += wobble * (amount * ramp);
                }
            }

            private static void ApplyDetail(OperatorData groomOperator, float smoothing, NativeArray<float> curves,
                                            StackBuffers buffers, int offset, int count)
            {
                NativeArray<float3> points = buffers.scratch;
                NativeArray<float3> source = buffers.operatorScratch;
                NativeArray<float> lengths = buffers.distances;
                int target = groomOperator.Int1;

                if (count < 2 || target < 2)
                {
                    return;
                }

                if (smoothing > 0f && groomOperator.Int0 > 0 && count > 2)
                {
                    CaptureSegmentLengths(points, offset, count, lengths);

                    for (int iteration = 0; iteration < groomOperator.Int0; iteration++)
                    {
                        for (int k = 0; k < count; k++)
                        {
                            source[offset + k] = points[offset + k];
                        }

                        for (int k = 1; k < count - 1; k++)
                        {
                            float3 middle = (source[offset + k - 1] + source[offset + k + 1]) * 0.5f;
                            points[offset + k] = math.lerp(source[offset + k], middle, smoothing);
                        }
                    }

                    RestoreSegmentLengths(points, offset, count, lengths);
                }

                for (int k = 0; k < count; k++)
                {
                    source[offset + k] = points[offset + k];
                }

                lengths[offset] = 0f;
                for (int k = 1; k < count; k++)
                {
                    lengths[offset + k] = lengths[offset + k - 1] +
                                          math.distance(source[offset + k], source[offset + k - 1]);
                }

                float total = lengths[offset + count - 1];

                if (total < 1e-8f)
                {
                    for (int k = count; k < target; k++)
                    {
                        points[offset + k] = source[offset];
                    }

                    return;
                }

                int segment = 1;

                for (int k = 0; k < target; k++)
                {
                    float wanted = total * curves[groomOperator.CurveOffset + k];

                    while (segment < count - 1 && lengths[offset + segment] < wanted)
                    {
                        segment++;
                    }

                    float start = lengths[offset + segment - 1];
                    float span = lengths[offset + segment] - start;
                    float u = span > 1e-8f ? math.saturate((wanted - start) / span) : 0f;
                    points[offset + k] = CatmullRom(source, offset, count, segment - 1, u);
                }

                points[offset] = source[offset];
            }

            private static float3 CatmullRom(NativeArray<float3> points, int offset, int count, int segment, float u)
            {
                float3 p1 = points[offset + segment];
                float3 p2 = points[offset + segment + 1];
                float3 p0 = segment > 0 ? points[offset + segment - 1] : 2f * p1 - p2;
                float3 p3 = segment + 2 < count ? points[offset + segment + 2] : 2f * p2 - p1;
                return HairCatmullRom.Centripetal(p0, p1, p2, p3, u);
            }

            private static void ApplyGround(ref StackInputs stack, OperatorData groomOperator, float weight,
                                            NativeArray<float3> points, int offset, int count)
            {
                if (stack.ground.Mode == GroundMode.None || count < 1)
                {
                    return;
                }

                float3 root = points[offset];
                float3 surface = stack.ground.Mode == GroundMode.Sphere
                    ? ClosestOnSphere(stack.ground.Sphere, root)
                    : ClosestOnMesh(ref stack, root);
                float3 shift = (surface - root) * weight;

                for (int k = 0; k < count; k++)
                {
                    points[offset + k] += shift * stack.curves[groomOperator.CurveOffset + k];
                }

                points[offset] = math.lerp(root, surface, weight);
            }

            private static float3 ClosestOnSphere(SphereData sphere, float3 point)
            {
                float3 fromCenter = point - sphere.Center;
                float length = math.length(fromCenter);
                return length > 1e-8f
                    ? sphere.Center + fromCenter * (sphere.Radius / length)
                    : sphere.Center + new float3(0f, sphere.Radius, 0f);
            }

            private static float3 ClosestOnMesh(ref StackInputs stack, float3 point)
            {
                GroundData ground = stack.ground;
                int3 cell = math.clamp((int3)math.floor((point - ground.Origin) / ground.CellSize), 0,
                                       ground.Dims - 1);
                float3 inside = math.clamp(point, ground.Origin, ground.Origin + (float3)ground.Dims * ground.CellSize);
                float outside = math.distance(point, inside);
                float bestSquared = float.MaxValue;
                float3 best = point;
                int maxRing = math.cmax(ground.Dims);

                for (int ring = 0; ring <= maxRing; ring++)
                {
                    float reach = outside + (ring - 1) * ground.CellSize;

                    if (ring > 0 && reach > 0f && reach * reach > bestSquared)
                    {
                        break;
                    }

                    int3 low = cell - ring;
                    int3 high = cell + ring;

                    for (int z = low.z; z <= high.z; z++)
                    for (int y = low.y; y <= high.y; y++)
                    for (int x = low.x; x <= high.x; x++)
                    {
                        bool shell = x == low.x || x == high.x || y == low.y || y == high.y ||
                                     z == low.z || z == high.z;

                        if (!shell || x < 0 || y < 0 || z < 0 ||
                            x >= ground.Dims.x || y >= ground.Dims.y || z >= ground.Dims.z)
                        {
                            continue;
                        }

                        int index = x + ground.Dims.x * (y + ground.Dims.y * z);

                        for (int i = stack.groundCells[index]; i < stack.groundCells[index + 1]; i++)
                        {
                            int3 triangle = stack.groundTriangles[stack.groundCellTriangles[i]];
                            float3 candidate = ClosestOnTriangle(point, stack.groundPositions[triangle.x],
                                                                 stack.groundPositions[triangle.y],
                                                                 stack.groundPositions[triangle.z]);
                            float squared = math.distancesq(point, candidate);

                            if (squared < bestSquared)
                            {
                                bestSquared = squared;
                                best = candidate;
                            }
                        }
                    }
                }

                return best;
            }

            private static float3 ClosestOnTriangle(float3 p, float3 a, float3 b, float3 c)
            {
                float3 ab = b - a;
                float3 ac = c - a;
                float3 ap = p - a;
                float d1 = math.dot(ab, ap);
                float d2 = math.dot(ac, ap);

                if (d1 <= 0f && d2 <= 0f)
                {
                    return a;
                }

                float3 bp = p - b;
                float d3 = math.dot(ab, bp);
                float d4 = math.dot(ac, bp);

                if (d3 >= 0f && d4 <= d3)
                {
                    return b;
                }

                float vc = d1 * d4 - d3 * d2;

                if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                {
                    return a + ab * (d1 / (d1 - d3));
                }

                float3 cp = p - c;
                float d5 = math.dot(ab, cp);
                float d6 = math.dot(ac, cp);

                if (d6 >= 0f && d5 <= d6)
                {
                    return c;
                }

                float vb = d5 * d2 - d1 * d6;

                if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                {
                    return a + ac * (d2 / (d2 - d6));
                }

                float va = d3 * d6 - d5 * d4;

                if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                {
                    return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
                }

                float denominator = 1f / (va + vb + vc);
                return a + ab * (vb * denominator) + ac * (vc * denominator);
            }

            public static float SampleCurve(NativeArray<float> curves, int offset, int samples, float x)
            {
                if (offset < 0)
                {
                    return 1f;
                }

                float scaled = math.saturate(x) * (samples - 1);
                int lower = math.min((int)scaled, samples - 2);
                return math.lerp(curves[offset + lower], curves[offset + lower + 1], scaled - lower);
            }

            private static float3 SurfaceDownhill(float3 normal)
            {
                float3 down = new float3(0f, -1f, 0f);
                float3 downhill = down - normal * math.dot(down, normal);

                if (math.lengthsq(downhill) < 1e-6f)
                {
                    downhill = new float3(normal.x, 0f, normal.z);

                    if (math.lengthsq(downhill) < 1e-6f)
                    {
                        downhill = new float3(0f, 0f, 1f);
                    }
                }

                return math.normalize(downhill);
            }

            private static float3 FlowDirection(RootData root, float2 comb)
            {
                float3 downhill = SurfaceDownhill(root.Normal);
                float strength = math.saturate(math.length(comb));

                if (strength < CombThreshold)
                {
                    return downhill;
                }

                float3 alongSurface = root.Tangent * comb.x + math.cross(root.Normal, root.Tangent) * comb.y;

                if (math.lengthsq(alongSurface) < 1e-10f)
                {
                    return downhill;
                }

                return NormalizeSafe(Slerp(downhill, math.normalize(alongSurface), strength), downhill);
            }

            public static void CaptureSegmentLengths(NativeArray<float3> points, int offset, int count,
                                                     NativeArray<float> lengths)
            {
                for (int k = 1; k < count; k++)
                {
                    lengths[offset + k] = math.length(points[offset + k] - points[offset + k - 1]);
                }
            }

            public static void FollowPath(NativeArray<float3> points, NativeArray<float3> path, int offset, int count,
                                          NativeArray<float> lengths)
            {
                float3 position = path[offset];
                points[offset] = position;
                int segment = 1;

                for (int k = 1; k < count; k++)
                {
                    float remaining = lengths[offset + k];

                    while (remaining > 0f && segment < count)
                    {
                        float3 end = path[offset + segment];
                        float available = math.distance(position, end);

                        if (available >= remaining)
                        {
                            position += (end - position) * (remaining / math.max(available, 1e-12f));
                            remaining = 0f;
                        }
                        else
                        {
                            remaining -= available;
                            position = end;
                            segment++;
                        }
                    }

                    if (remaining > 0f)
                    {
                        float3 tail = path[offset + count - 1] - path[offset + count - 2];
                        position += NormalizeSafe(tail, new float3(0f, -1f, 0f)) * remaining;
                    }

                    points[offset + k] = position;
                }
            }

            public static void RestoreSegmentLengths(NativeArray<float3> points, int offset, int count,
                                                     NativeArray<float> lengths)
            {
                float3 previous = points[offset];

                for (int k = 1; k < count; k++)
                {
                    float3 direction = points[offset + k] - previous;
                    float length = math.length(direction);
                    direction = length > 1e-8f ? direction / length : new float3(0f, 1f, 0f);
                    previous += direction * lengths[offset + k];
                    points[offset + k] = previous;
                }
            }

            private static float3 SampleGuide(GuideData guide, NativeArray<float3> points, float along)
            {
                if (guide.PointCount <= 0)
                {
                    return float3.zero;
                }

                if (guide.PointCount == 1)
                {
                    return points[guide.PointStart];
                }

                float scaled = math.saturate(along) * (guide.PointCount - 1);
                int lower = math.min((int)scaled, guide.PointCount - 2);
                return math.lerp(points[guide.PointStart + lower], points[guide.PointStart + lower + 1],
                                 scaled - lower);
            }

            public static float GuideDistance(RootData root, GuideData guide, int framed)
            {
                if (framed == 0)
                {
                    return WrappedDistance(root.Uv, guide.Uv);
                }

                if (math.dot(root.Normal, guide.Normal) <= 0f)
                {
                    return float.MaxValue;
                }

                return math.distance(root.Position, guide.Position);
            }

            public static float WrappedDistance(float2 a, float2 b)
            {
                float horizontal = math.abs(a.x - b.x);
                horizontal = math.min(horizontal, 1f - horizontal);
                float vertical = a.y - b.y;
                return math.sqrt(horizontal * horizontal + vertical * vertical);
            }

            public static JobRandom RandomFor(int seed, int strand, int operatorSeed)
            {
                return new JobRandom(seed + strand * 31337 + operatorSeed * 6151 + 17);
            }

            public static float3 Perpendicular(float3 direction)
            {
                float3 reference = math.abs(direction.y) < 0.9f
                    ? new float3(0f, 1f, 0f)
                    : new float3(1f, 0f, 0f);
                return NormalizeSafe(math.cross(direction, reference), new float3(1f, 0f, 0f));
            }

            public static float3 Orthogonal(float3 reference, float3 direction)
            {
                float3 projected = reference - direction * math.dot(reference, direction);
                return math.lengthsq(projected) > 1e-10f ? math.normalize(projected) : Perpendicular(direction);
            }

            private static float3 NormalizeSafe(float3 value, float3 fallback)
            {
                return math.lengthsq(value) > 1e-12f ? math.normalize(value) : fallback;
            }

            private static float3 Slerp(float3 from, float3 to, float amount)
            {
                float3 a = NormalizeSafe(from, new float3(0f, 1f, 0f));
                float3 b = NormalizeSafe(to, a);
                float dot = math.clamp(math.dot(a, b), -1f, 1f);

                if (dot > 0.9995f)
                {
                    return NormalizeSafe(math.lerp(a, b, amount), a);
                }

                if (dot < -0.9995f)
                {
                    float oppositeAngle = math.PI * amount;
                    return a * math.cos(oppositeAngle) + Perpendicular(a) * math.sin(oppositeAngle);
                }

                float angle = math.acos(dot);
                float sine = math.sin(angle);
                if (math.abs(sine) < 1e-6f)
                {
                    return NormalizeSafe(math.lerp(a, b, amount), a);
                }

                return a * (math.sin((1f - amount) * angle) / sine)
                       + b * (math.sin(amount * angle) / sine);
            }

            private static float SmoothStep(float value)
            {
                value = math.saturate(value);
                return value * value * (3f - 2f * value);
            }
        }

        private struct JobRandom
        {
            private uint _state;

            public float Value => (NextUInt() & 0xFFFFFF) / 16777216f;

            public JobRandom(int seed)
            {
                _state = (uint)seed | 1u;
            }

            public float Range(float min, float max)
            {
                return min + (max - min) * Value;
            }

            public float3 OnUnitSphere()
            {
                float z = Range(-1f, 1f);
                float angle = Range(0f, math.PI * 2f);
                float radius = math.sqrt(math.max(0f, 1f - z * z));
                return new float3(radius * math.cos(angle), z, radius * math.sin(angle));
            }

            private uint NextUInt()
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return _state;
            }
        }
    }
}
