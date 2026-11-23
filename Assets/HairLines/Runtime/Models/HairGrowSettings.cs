using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace HairLines
{
    [Serializable]
    public class HairGrowSettings
    {
        public int Seed { get => _seed; set => _seed = value; }
        public int StrandCount { get => _strandCount; set => _strandCount = Mathf.Max(1, value); }
        public int SegmentsPerStrand { get => _segmentsPerStrand; set => _segmentsPerStrand = Mathf.Clamp(value, 1, 128); }

        public Material Material { get => _material; set => _material = value; }
        public Color Tint { get => _tint; set => _tint = value; }

        public HairDensityMask DensityMask => _densityMask;
        public HairCombMap CombMap => _combMap;
        public HairGuideSet Guides => _guides;
        public HairColorMap RootColorMap => _rootColorMap ??= new HairColorMap();
        public HairColorMap TipColorMap => _tipColorMap ??= new HairColorMap();
        public HairColorMap TintMap => _tintMap ??= new HairColorMap();

        public List<HairGroomOperator> Operators
        {
            get
            {
                MigrateLegacyOperators();
                SortOperatorsByPriority();
                return _operators;
            }
        }

        private void SortOperatorsByPriority()
        {
            if (_operators == null || _operators.Count < 2)
            {
                return;
            }

            for (int i = 1; i < _operators.Count; i++)
            {
                if (PriorityOf(_operators[i - 1]) > PriorityOf(_operators[i]))
                {
                    var ordered = _operators
                        .Select((op, index) => (op, index))
                        .OrderBy(entry => PriorityOf(entry.op))
                        .ThenBy(entry => entry.index)
                        .Select(entry => entry.op)
                        .ToList();
                    _operators.Clear();
                    _operators.AddRange(ordered);
                    return;
                }
            }
        }

        private static int PriorityOf(HairGroomOperator groomOperator)
        {
            return groomOperator?.Priority ?? 0;
        }

        public float StrandLength { get => _strandLength; set => _strandLength = Mathf.Max(0.001f, value); }
        public float LengthVariance { get => _lengthVariance; set => _lengthVariance = Mathf.Clamp01(value); }
        public AnimationCurve SegmentLengthDistribution
        {
            get
            {
                EnsureSegmentLengthDistribution();
                return _segmentLengthDistribution;
            }
            set => _segmentLengthDistribution = CloneCurve(value);
        }
        public float ScalpFlow { get => _scalpFlow; set => _scalpFlow = Mathf.Clamp01(value); }
        public float Gravity { get => _gravity; set => _gravity = Mathf.Clamp(value, 0f, 4f); }
        public float Stiffness { get => _stiffness; set => _stiffness = Mathf.Clamp01(value); }
        public float Frizz { get => _frizz; set => _frizz = Mathf.Clamp01(value); }

        public float CurlRadius { get => _curlRadius; set => _curlRadius = Mathf.Max(0f, value); }
        public float CurlFrequency { get => _curlFrequency; set => _curlFrequency = Mathf.Max(0f, value); }

        public int GuideCount { get => _guideCount; set => _guideCount = Mathf.Max(0, value); }

        public float LodPriority { get => _lodPriority; set => _lodPriority = Mathf.Clamp01(value); }

        public float ColorVariance { get => _colorVariance; set => _colorVariance = Mathf.Clamp01(value); }
        public float TipOpacity { get => _tipOpacity; set => _tipOpacity = Mathf.Clamp01(value); }

        [SerializeField, FormerlySerializedAs("seed")]
        private int _seed = 12345;

        [SerializeField, FormerlySerializedAs("strandCount"), Min(1)]
        private int _strandCount = 15000;

        [SerializeField, FormerlySerializedAs("segmentsPerStrand"), Range(1, 128)]
        private int _segmentsPerStrand = 12;

        [SerializeField]
        private HairDensityMask _densityMask = new HairDensityMask();

        [SerializeField]
        private HairCombMap _combMap = new HairCombMap();

        [SerializeField]
        private HairGuideSet _guides = new HairGuideSet();

        [SerializeReference]
        private List<HairGroomOperator> _operators = new List<HairGroomOperator>();

        [SerializeField, HideInInspector]
        private bool _operatorsMigrated;

        [Tooltip("Length of a strand, in metres.")]
        [SerializeField, FormerlySerializedAs("strandLength"), Min(0.001f)]
        private float _strandLength = 0.25f;

        [Tooltip("Random spread of the strand length, as a fraction of it.")]
        [SerializeField, FormerlySerializedAs("lengthVariance"), Range(0f, 1f)]
        private float _lengthVariance = 0.15f;

        [Tooltip("Relative segment length from root (0) to tip (1). Values are normalized, so the full strand length does not change.")]
        [SerializeField]
        private AnimationCurve _segmentLengthDistribution = DefaultSegmentLengthDistribution();

        [Tooltip("How much the strand leaves the scalp along the surface (1) instead of straight out of it (0).")]
        [SerializeField, FormerlySerializedAs("scalpFlow"), Range(0f, 1f)]
        private float _scalpFlow = 0.85f;

        [Tooltip("How strongly the strand bends towards world down along its length.")]
        [SerializeField, FormerlySerializedAs("gravity"), Range(0f, 4f)]
        private float _gravity = 1.1f;

        [Tooltip("How much of the previous direction each segment keeps.")]
        [SerializeField, FormerlySerializedAs("stiffness"), Range(0f, 1f)]
        private float _stiffness = 0.75f;

        [Tooltip("Per-strand noise added to the growth direction.")]
        [SerializeField, FormerlySerializedAs("frizz"), Range(0f, 1f)]
        private float _frizz = 0.18f;

        [Tooltip("Radius of the helical curl, in metres.")]
        [SerializeField, FormerlySerializedAs("curlRadius"), Min(0f)]
        private float _curlRadius = 0.004f;

        [Tooltip("Full curl turns over the length of a strand.")]
        [SerializeField, FormerlySerializedAs("curlFrequency"), Min(0f)]
        private float _curlFrequency = 2.5f;

        [Tooltip("How many guide strands Generate Guides lays out over the group, in strands.")]
        [SerializeField, FormerlySerializedAs("clumpCount"), FormerlySerializedAs("_clumpCount"), Min(0)]
        private int _guideCount = 200;

        [Tooltip("Relative importance of this group when strand LOD reduces density. Higher-priority groups survive longer.")]
        [SerializeField, Range(0f, 1f)]
        private float _lodPriority = 0.5f;

        [Tooltip("Per-strand brightness spread baked into the vertex attribute. The hair colour itself lives on the material.")]
        [SerializeField, FormerlySerializedAs("colorVariance"), Range(0f, 1f)]
        private float _colorVariance = 0.18f;

        [Tooltip("Per-edge existence probability at the tip. Below 1 the tips only survive the stochastic opacity test part of the time.")]
        [SerializeField, FormerlySerializedAs("tipOpacity"), Range(0f, 1f)]
        private float _tipOpacity = 1f;

        [Tooltip("Material used to draw this group. When empty, the parent hair material is used.")]
        [SerializeField]
        private Material _material;

        [Tooltip("Multiplies the material colour for this group alone. White leaves the material as it is.")]
        [SerializeField]
        private Color _tint = Color.white;

        [Tooltip("Root colour of every strand, read from this texture at the strand's scalp UV. It " +
                 "replaces the material's Root colour for this group. Empty keeps the material's.")]
        [SerializeField]
        private HairColorMap _rootColorMap = new HairColorMap();

        [Tooltip("Tip colour of every strand, read from this texture at the strand's scalp UV. It " +
                 "replaces the material's Tip colour for this group. Empty keeps the material's.")]
        [SerializeField]
        private HairColorMap _tipColorMap = new HairColorMap();

        [Tooltip("Multiplies the whole strand, read at its scalp UV. Works with RGB and melanin colour " +
                 "alike, on top of Tint. Empty is white.")]
        [SerializeField]
        private HairColorMap _tintMap = new HairColorMap();

        private const float LegacyFrizzToMetres = 0.02f;

        public HairGrowSettings Clone()
        {
            HairGrowSettings clone = new HairGrowSettings();
            clone.CopyFrom(this);
            return clone;
        }

        public void CopyFrom(HairGrowSettings source)
        {
            if (source == null)
            {
                return;
            }

            _seed = source._seed;
            _strandCount = source._strandCount;
            _segmentsPerStrand = source._segmentsPerStrand;
            _densityMask = new HairDensityMask
            {
                Texture = source._densityMask != null ? source._densityMask.Texture : null,
            };
            _combMap = new HairCombMap
            {
                Texture = source._combMap != null ? source._combMap.Texture : null,
            };
            _guides = new HairGuideSet();
            if (source._guides != null)
            {
                foreach (HairGuide guide in source._guides.Guides)
                {
                    if (guide != null)
                    {
                        _guides.Guides.Add(guide.Clone());
                    }
                }
            }

            source.MigrateLegacyOperators();
            _operators = new List<HairGroomOperator>(source._operators.Count);
            foreach (HairGroomOperator groomOperator in source._operators)
            {
                _operators.Add(groomOperator?.Clone());
            }
            _operatorsMigrated = true;

            _strandLength = source._strandLength;
            _lengthVariance = source._lengthVariance;
            _segmentLengthDistribution = CloneCurve(source._segmentLengthDistribution);
            _scalpFlow = source._scalpFlow;
            _gravity = source._gravity;
            _stiffness = source._stiffness;
            _frizz = source._frizz;
            _curlRadius = source._curlRadius;
            _curlFrequency = source._curlFrequency;
            _guideCount = source._guideCount;
            _lodPriority = source._lodPriority;
            _colorVariance = source._colorVariance;
            _tipOpacity = source._tipOpacity;
            _material = source._material;
            _tint = source._tint;
            _rootColorMap = new HairColorMap { Texture = source.RootColorMap.Texture };
            _tipColorMap = new HairColorMap { Texture = source.TipColorMap.Texture };
            _tintMap = new HairColorMap { Texture = source.TintMap.Texture };
        }

        public float[] BuildSegmentLengthFractions(int segmentCount)
        {
            segmentCount = Mathf.Max(1, segmentCount);
            EnsureSegmentLengthDistribution();

            var fractions = new float[segmentCount];
            float sum = 0f;

            for (int segment = 0; segment < segmentCount; segment++)
            {
                float position = (segment + 0.5f) / segmentCount;
                float weight = _segmentLengthDistribution.Evaluate(position);
                if (float.IsNaN(weight) || float.IsInfinity(weight))
                {
                    weight = 0f;
                }

                weight = Mathf.Max(0f, weight);
                fractions[segment] = weight;
                sum += weight;
            }

            if (!(sum > 1e-6f) || float.IsNaN(sum) || float.IsInfinity(sum))
            {
                float uniform = 1f / segmentCount;
                for (int segment = 0; segment < segmentCount; segment++)
                {
                    fractions[segment] = uniform;
                }

                return fractions;
            }

            float inverseSum = 1f / sum;
            for (int segment = 0; segment < segmentCount; segment++)
            {
                fractions[segment] *= inverseSum;
            }

            return fractions;
        }

        private void EnsureSegmentLengthDistribution()
        {
            if (_segmentLengthDistribution == null || _segmentLengthDistribution.length == 0)
            {
                _segmentLengthDistribution = DefaultSegmentLengthDistribution();
            }
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null || source.length == 0)
            {
                return DefaultSegmentLengthDistribution();
            }

            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }

        private static AnimationCurve CloneCurve(AnimationCurve source, Func<AnimationCurve> fallback)
        {
            if (source == null || source.length == 0)
            {
                return fallback();
            }

            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
        }

        private static float[] SampleUnitCurve(AnimationCurve curve, int sampleCount)
        {
            sampleCount = Mathf.Max(1, sampleCount);
            var weights = new float[sampleCount];
            float denominator = Mathf.Max(1, sampleCount - 1);

            for (int i = 0; i < sampleCount; i++)
            {
                float value = curve.Evaluate(i / denominator);
                weights[i] = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
            }

            return weights;
        }

        private static AnimationCurve DefaultSegmentLengthDistribution() =>
            AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f);

        private void MigrateLegacyOperators()
        {
            if (_operatorsMigrated)
            {
                return;
            }

            _operatorsMigrated = true;
            _operators ??= new List<HairGroomOperator>();

            if (_curlRadius > 0f && _curlFrequency > 0f)
            {
                _operators.Add(new HairCurlOperator { Radius = _curlRadius, Frequency = _curlFrequency });
            }

            if (_frizz > 0f)
            {
                _operators.Add(new HairFrizzOperator { Amount = _frizz * LegacyFrizzToMetres });
            }
        }
    }
}
