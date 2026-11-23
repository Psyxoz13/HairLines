using System;
using UnityEngine;

namespace HairLines
{
    [Serializable]
    public class HairSimulationSettings
    {
        public int Substeps { get => _substeps; set => _substeps = Mathf.Clamp(value, 1, 8); }
        public int Iterations { get => _iterations; set => _iterations = Mathf.Clamp(value, 1, 64); }
        public float MaxDeltaTime { get => _maxDeltaTime; set => _maxDeltaTime = Mathf.Max(0.005f, value); }
        public float SimulationStep { get => _simulationStep; set => _simulationStep = Mathf.Clamp(value, 1f / 240f, 1f); }
        public int SimulationVertexSkip { get => _simulationVertexSkip; set => _simulationVertexSkip = Mathf.Clamp(value, 0, 4); }
        public float StrainLimit { get => _strainLimit; set => _strainLimit = Mathf.Clamp(value, 0f, 0.2f); }
        public AnimationCurve SimulationInfluence { get => _simulationInfluence; set => _simulationInfluence = value ?? AnimationCurve.Linear(0f, 1f, 1f, 1f); }

        public int PinnedRootVertices { get => _pinnedRootVertices; set => _pinnedRootVertices = Mathf.Clamp(value, 1, 6); }
        public HairStrandModel StrandModel { get => _strandModel; set => _strandModel = value; }
        public bool BendingSprings { get => _bendingSprings; set => _bendingSprings = value; }
        public float VolumeVelocity { get => _volumeVelocity; set => _volumeVelocity = Mathf.Clamp01(value); }
        public float VolumePressure { get => _volumePressure; set => _volumePressure = Mathf.Clamp01(value); }

        public float StretchCompliance { get => _stretchCompliance; set => _stretchCompliance = Mathf.Max(0f, value); }
        public float BendCompliance { get => _bendCompliance; set => _bendCompliance = Mathf.Max(0f, value); }

        public Vector3 Gravity { get => _gravity; set => _gravity = value; }
        public float Damping { get => _damping; set => _damping = Mathf.Clamp01(value); }
        public bool UseUnityWind { get => _useUnityWind; set => _useUnityWind = value; }
        public Vector3 Wind { get => _wind; set => _wind = value; }
        public float WindTurbulence { get => _windTurbulence; set => _windTurbulence = Mathf.Clamp(value, 0f, 3f); }

        public float CollisionOffset { get => _collisionOffset; set => _collisionOffset = Mathf.Max(0f, value); }
        public float CollisionRootHold { get => _collisionRootHold; set => _collisionRootHold = Mathf.Max(0f, value); }
        public bool CollideWithSceneGeometry
        {
            get => _collideWithSceneGeometry;
            set => _collideWithSceneGeometry = value;
        }
        public bool CollidersAsSceneFallback
        {
            get => _collidersAsSceneFallback;
            set => _collidersAsSceneFallback = value;
        }
        public LineHairSimulation.CollisionShape[] Colliders { get => _colliders; set => _colliders = value ?? Array.Empty<LineHairSimulation.CollisionShape>(); }
        public LineHairSimulation.BoneCapsule[] BoneCapsules { get => _boneCapsules; set => _boneCapsules = value ?? Array.Empty<LineHairSimulation.BoneCapsule>(); }

        [Tooltip("Solver sub-steps per frame.")]
        [SerializeField, Range(1, 8)]
        private int _substeps = 2;

        [Tooltip("Jacobi iterations per sub-step.")]
        [SerializeField, Range(1, 64)]
        private int _iterations = 12;

        [Tooltip("Maximum accumulated frame time in seconds.")]
        [SerializeField, Min(0.005f)]
        private float _maxDeltaTime = 1f / 30f;

        [Tooltip("Fixed simulation step in seconds.")]
        [SerializeField, Min(1f / 240f)]
        private float _simulationStep = 1f / 60f;

        [Tooltip("Number of strand vertices skipped between simulated control vertices. 0 simulates every vertex; 1 simulates every second vertex.")]
        [SerializeField, Range(0, 4)]
        private int _simulationVertexSkip = 1;

        [Tooltip("Maximum segment strain above rest length, as a fraction.")]
        [SerializeField, Range(0f, 0.2f)]
        private float _strainLimit = 0.1f;

        [Tooltip("Simulation multiplier from root (0) to tip (1).")]
        [SerializeField]
        private AnimationCurve _simulationInfluence = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        [Tooltip("Pinned vertices at each strand root.")]
        [SerializeField, Range(1, 6)]
        private int _pinnedRootVertices = 2;

        [Tooltip("Cosserat Rods: segments carry orientations and bend against the rest shape " +
                 "(Stable Cosserat Rods, SIGGRAPH 2025). Distance Springs: XPBD rows between i, i+1 and i, i+2.")]
        [SerializeField]
        private HairStrandModel _strandModel = HairStrandModel.CosseratRods;

        [Tooltip("Enable bending: bend-twist against the rest shape for Cosserat Rods, springs between every second vertex for Distance Springs.")]
        [SerializeField]
        private bool _bendingSprings = true;

        [Tooltip("How strongly a vertex takes on the average velocity of the hair around it, as the " +
                 "fraction of the difference removed per 1/60 s. This is what makes strands move as " +
                 "one mass. 0 lets every strand move on its own.")]
        [SerializeField, Range(0f, 1f)]
        private float _volumeVelocity = 0.05f;

        [Tooltip("How strongly hair is pushed apart where it packs tighter than the densest part of " +
                 "its own groom. 0 lets strands pile through each other.")]
        [SerializeField, Range(0f, 1f)]
        private float _volumePressure = 1f;

        [Tooltip("Stretch compliance in metres/Newton. Zero is a rigid row.")]
        [SerializeField, Min(0f)]
        private float _stretchCompliance = 0.000001f;

        [Tooltip("Bend compliance in metres/Newton. Zero is a rigid distance constraint.")]
        [SerializeField, Min(0f)]
        private float _bendCompliance = 0.00001f;

        [Tooltip("Acceleration in metres per second squared.")]
        [SerializeField]
        private Vector3 _gravity = new Vector3(0f, -9.81f, 0f);

        [Tooltip("Velocity removed per simulation step as a fraction.")]
        [SerializeField, Range(0f, 1f)]
        private float _damping = 0.05f;

        [Tooltip("Use active Unity Wind Zone components instead of a manually entered wind vector.")]
        [SerializeField]
        private bool _useUnityWind;

        [HideInInspector]
        private Vector3 _wind = Vector3.zero;

        [HideInInspector]
        private float _windTurbulence = 0.6f;

        [Tooltip("Length of strand measured from the root, in metres, over which collision fades in. " +
                 "0 collides the whole strand and straightens it where it leaves the scalp.")]
        [SerializeField, Min(0f)]
        private float _collisionRootHold = 0.02f;

        [Tooltip("Collision clearance in metres.")]
        [SerializeField, Min(0f)]
        private float _collisionOffset = 0.004f;

        [Tooltip("Collide with renderer geometry inside the simulation bounds through a ray-traced distance field.")]
        [SerializeField]
        private bool _collideWithSceneGeometry;

        [Tooltip("Use the collider list only where the scene distance field cannot be built, such as " +
                 "GPUs without inline ray tracing. Off: the colliders and the scene both collide.")]
        [SerializeField]
        private bool _collidersAsSceneFallback;

        [Tooltip("Additional colliders used by this group.")]
        [SerializeField]
        private LineHairSimulation.CollisionShape[] _colliders = Array.Empty<LineHairSimulation.CollisionShape>();

        [Tooltip("Capsules fitted to the character's bones by their skin weights. They collide like the " +
                 "colliders above and add no Collider to the physics scene.")]
        [SerializeField]
        private LineHairSimulation.BoneCapsule[] _boneCapsules = Array.Empty<LineHairSimulation.BoneCapsule>();

        public HairSimulationSettings Clone()
        {
            HairSimulationSettings clone = new HairSimulationSettings();
            clone.CopyFrom(this);
            return clone;
        }

        public void CopyFrom(HairSimulationSettings source)
        {
            if (source == null)
            {
                return;
            }

            CopyValuesFrom(source);
            _simulationInfluence = source._simulationInfluence == null
                ? AnimationCurve.Linear(0f, 1f, 1f, 1f)
                : new AnimationCurve(source._simulationInfluence.keys);
            _colliders = source._colliders == null
                ? Array.Empty<LineHairSimulation.CollisionShape>()
                : (LineHairSimulation.CollisionShape[])source._colliders.Clone();
            _boneCapsules = source._boneCapsules == null
                ? Array.Empty<LineHairSimulation.BoneCapsule>()
                : (LineHairSimulation.BoneCapsule[])source._boneCapsules.Clone();
            Sanitize();
        }

        public void CopyValuesFrom(HairSimulationSettings source)
        {
            if (source == null)
            {
                return;
            }

            _substeps = source._substeps;
            _iterations = source._iterations;
            _maxDeltaTime = source._maxDeltaTime;
            _simulationStep = source._simulationStep;
            _simulationVertexSkip = source._simulationVertexSkip;
            _strainLimit = source._strainLimit;
            _simulationInfluence = source._simulationInfluence;
            _pinnedRootVertices = source._pinnedRootVertices;
            _strandModel = source._strandModel;
            _bendingSprings = source._bendingSprings;
            _volumeVelocity = source._volumeVelocity;
            _volumePressure = source._volumePressure;
            _stretchCompliance = source._stretchCompliance;
            _bendCompliance = source._bendCompliance;
            _gravity = source._gravity;
            _damping = source._damping;
            _useUnityWind = source._useUnityWind;
            _wind = source._wind;
            _windTurbulence = source._windTurbulence;
            _collisionOffset = source._collisionOffset;
            _collideWithSceneGeometry = source._collideWithSceneGeometry;
            _collidersAsSceneFallback = source._collidersAsSceneFallback;
            _colliders = source._colliders;
            _boneCapsules = source._boneCapsules;
        }

        public void Sanitize()
        {
            Iterations = _iterations;
            MaxDeltaTime = _maxDeltaTime;
            SimulationStep = _simulationStep;
            SimulationVertexSkip = _simulationVertexSkip;
            StrainLimit = _strainLimit;
            SimulationInfluence = _simulationInfluence;
            PinnedRootVertices = _pinnedRootVertices;
            VolumeVelocity = _volumeVelocity;
            VolumePressure = _volumePressure;
            StretchCompliance = _stretchCompliance;
            BendCompliance = _bendCompliance;
            Damping = _damping;
            WindTurbulence = _windTurbulence;
            CollisionOffset = _collisionOffset;
            _colliders ??= Array.Empty<LineHairSimulation.CollisionShape>();
            _boneCapsules ??= Array.Empty<LineHairSimulation.BoneCapsule>();
        }
    }
}
