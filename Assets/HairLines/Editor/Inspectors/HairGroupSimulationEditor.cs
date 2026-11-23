using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    [CustomEditor(typeof(HairGroupSimulation))]
    [CanEditMultipleObjects]
    public class HairGroupSimulationEditor : UnityEditor.Editor
    {
        private const string Prefix = "HairLines.GroupSimulation.";

        private SerializedProperty _mode;
        private SerializedProperty _preset;
        private SerializedProperty _substeps;
        private SerializedProperty _iterations;
        private SerializedProperty _simulationStep;
        private SerializedProperty _maxDeltaTime;
        private SerializedProperty _simulationVertexSkip;
        private SerializedProperty _simulationInfluence;
        private SerializedProperty _strainLimit;
        private SerializedProperty _pinnedRootVertices;
        private SerializedProperty _strandModel;
        private SerializedProperty _bendingSprings;
        private SerializedProperty _volumeVelocity;
        private SerializedProperty _volumePressure;
        private SerializedProperty _stretchCompliance;
        private SerializedProperty _bendCompliance;
        private SerializedProperty _gravity;
        private SerializedProperty _damping;
        private SerializedProperty _useUnityWind;
        private SerializedProperty _collisionOffset;
        private SerializedProperty _collideWithSceneGeometry;
        private SerializedProperty _collidersAsSceneFallback;
        private SerializedProperty _colliders;
        private SerializedProperty _boneCapsules;
        private string _fitReport;
        private bool _fitSucceeded;

        private void OnEnable()
        {
            _mode = serializedObject.FindProperty("_mode");
            _preset = serializedObject.FindProperty("_preset");

            SerializedProperty settings = serializedObject.FindProperty("_settings");
            _substeps = settings.FindPropertyRelative("_substeps");
            _iterations = settings.FindPropertyRelative("_iterations");
            _simulationStep = settings.FindPropertyRelative("_simulationStep");
            _maxDeltaTime = settings.FindPropertyRelative("_maxDeltaTime");
            _simulationVertexSkip = settings.FindPropertyRelative("_simulationVertexSkip");
            _simulationInfluence = settings.FindPropertyRelative("_simulationInfluence");
            _strainLimit = settings.FindPropertyRelative("_strainLimit");
            _pinnedRootVertices = settings.FindPropertyRelative("_pinnedRootVertices");
            _strandModel = settings.FindPropertyRelative("_strandModel");
            _bendingSprings = settings.FindPropertyRelative("_bendingSprings");
            _volumeVelocity = settings.FindPropertyRelative("_volumeVelocity");
            _volumePressure = settings.FindPropertyRelative("_volumePressure");
            _stretchCompliance = settings.FindPropertyRelative("_stretchCompliance");
            _bendCompliance = settings.FindPropertyRelative("_bendCompliance");
            _gravity = settings.FindPropertyRelative("_gravity");
            _damping = settings.FindPropertyRelative("_damping");
            _useUnityWind = settings.FindPropertyRelative("_useUnityWind");
            _collisionOffset = settings.FindPropertyRelative("_collisionOffset");
            _collideWithSceneGeometry = settings.FindPropertyRelative("_collideWithSceneGeometry");
            _collidersAsSceneFallback = settings.FindPropertyRelative("_collidersAsSceneFallback");
            _colliders = settings.FindPropertyRelative("_colliders");
            _boneCapsules = settings.FindPropertyRelative("_boneCapsules");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawOwnerWarning();
            DrawMode();

            if ((HairSimulationMode)_mode.enumValueIndex == HairSimulationMode.Preset)
            {
                HairInspectorGUI.Note("Parameters are read from the selected ScriptableObject preset.");
            }
            else
            {
                DrawSettings();
            }

            serializedObject.ApplyModifiedProperties();
            DrawSavePresetButton();
        }

        private void DrawOwnerWarning()
        {
            foreach (Object targetObject in targets)
            {
                HairGroupSimulation simulation = (HairGroupSimulation)targetObject;
                if (simulation.GetComponentInParent<LineHairSimulation>() == null)
                {
                    EditorGUILayout.HelpBox("Add Line Hair Simulation to the parent hair object to run this group.",
                                            MessageType.Warning);
                    return;
                }
            }
        }

        private void DrawMode()
        {
            if (HairInspectorGUI.BeginSection(Prefix + "Mode", "Physics Mode"))
            {
                HairInspectorGUI.EnumToolbar(_mode, "Profile");
                bool preset = !_mode.hasMultipleDifferentValues &&
                              (HairSimulationMode)_mode.enumValueIndex == HairSimulationMode.Preset;
                if (preset)
                {
                    EditorGUILayout.PropertyField(_preset, new GUIContent("Preset"));
                    if (_preset.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox("Assign a preset or switch to Custom.", MessageType.Warning);
                    }
                }
            }
            HairInspectorGUI.EndSection();
        }

        private void DrawSettings()
        {
            if (HairInspectorGUI.BeginSection(Prefix + "Solver", "Solver", SolverSummary()))
            {
                DrawSolver();
            }
            HairInspectorGUI.EndSection();

            if (HairInspectorGUI.BeginSection(Prefix + "Constraints", "Constraints", ConstraintSummary()))
            {
                DrawConstraints();
            }
            HairInspectorGUI.EndSection();

            if (HairInspectorGUI.BeginSection(Prefix + "Forces", "Forces"))
            {
                DrawForces();
            }
            HairInspectorGUI.EndSection();

            if (HairInspectorGUI.BeginSection(Prefix + "Collision", "Collision", CollisionSummary()))
            {
                DrawCollision();
            }
            HairInspectorGUI.EndSection();
        }

        private void DrawSolver()
        {
            EditorGUILayout.PropertyField(_simulationStep,
                new GUIContent("Delta Time", "Fixed duration of one solver step, in seconds."));
            EditorGUILayout.PropertyField(_maxDeltaTime);
            if (!_simulationStep.hasMultipleDifferentValues &&
                !_maxDeltaTime.hasMultipleDifferentValues &&
                _simulationStep.floatValue > _maxDeltaTime.floatValue)
            {
                EditorGUILayout.HelpBox(
                    "Delta Time is larger than Max Delta Time. Runtime uses Delta Time as the effective limit; " +
                    "Max Delta Time is not changed.", MessageType.Warning);
            }

            HairInspectorGUI.DrawSeparator();
            EditorGUILayout.PropertyField(_substeps);
            EditorGUILayout.PropertyField(_iterations);
            EditorGUILayout.PropertyField(_simulationVertexSkip,
                new GUIContent("Simulation Vertex Skip",
                               "Number of vertices skipped between simulated controls (0-4)."));
            if (!_substeps.hasMultipleDifferentValues && !_iterations.hasMultipleDifferentValues)
            {
                HairInspectorGUI.Note($"{_substeps.intValue * _iterations.intValue} Jacobi dispatches per frame.");
            }

            EditorGUILayout.PropertyField(_simulationInfluence,
                new GUIContent("Simulation Influence", "Multiplier along each strand from root to tip."));

            HairInspectorGUI.SubHeader("Strain Limiting");
            EditorGUILayout.PropertyField(_strainLimit);
            if (!_strainLimit.hasMultipleDifferentValues)
            {
                HairInspectorGUI.Note(_strainLimit.floatValue <= 0f
                    ? "Off - segments keep whatever stretch the solve leaves them."
                    : $"Segments are clamped to {(1f + _strainLimit.floatValue) * 100f:0}% of rest length.");
            }
        }

        private void DrawConstraints()
        {
            EditorGUILayout.PropertyField(_pinnedRootVertices);
            EditorGUILayout.PropertyField(_strandModel);
            EditorGUILayout.PropertyField(_bendingSprings);

            HairInspectorGUI.SubHeader("Hair Volume");
            EditorGUILayout.PropertyField(_volumeVelocity, new GUIContent("Shared Velocity",
                "Pull toward the average velocity of the hair around each vertex, per 1/60 s. " +
                "Makes strands move as one mass."));
            EditorGUILayout.PropertyField(_volumePressure, new GUIContent("Volume Pressure",
                "Push apart where hair packs tighter than the densest part of its own groom."));
            bool volume = _volumeVelocity.hasMultipleDifferentValues || _volumePressure.hasMultipleDifferentValues ||
                          _volumeVelocity.floatValue > 0f || _volumePressure.floatValue > 0f;
            HairInspectorGUI.Note(volume
                ? "Strands meet through a voxel grid of hair velocity and density, not through springs."
                : "Volume off - strands move independently of each other.");

            HairInspectorGUI.SubHeader("Compliance");
            HairInspectorGUI.ComplianceSlider(_stretchCompliance, "Stretch",
                "Softness, logarithmic range 1e-8..1e-2 m/N.");
            if (_bendingSprings.hasMultipleDifferentValues || _bendingSprings.boolValue)
            {
                HairInspectorGUI.ComplianceSlider(_bendCompliance, "Bend",
                    "Bend softness, logarithmic range 1e-8..1e-2 m/N.");
            }
            HairInspectorGUI.Note("Zero compliance is a rigid row; larger values let the segment give.");
        }

        private void DrawForces()
        {
            EditorGUILayout.PropertyField(_gravity);
            EditorGUILayout.PropertyField(_damping);
            EditorGUILayout.PropertyField(_useUnityWind, new GUIContent("Use Unity Wind",
                "Read wind from Unity Wind Zones instead of the manual vector."));
            if (_useUnityWind == null || _useUnityWind.hasMultipleDifferentValues)
            {
                HairInspectorGUI.Note("Unity Wind mode is mixed across the selected groups.");
            }
            else if (_useUnityWind.boolValue)
            {
                HairInspectorGUI.Note("Wind is driven by active Unity Wind Zones. Manual Wind and Turbulence are hidden.");
            }
            else
            {
                HairInspectorGUI.Note("Unity Wind is disabled; no wind force is applied.");
            }
        }

        private void DrawCollision()
        {
            EditorGUILayout.PropertyField(_collisionOffset);

            EditorGUILayout.PropertyField(_collideWithSceneGeometry,
                new GUIContent("Collide With Scene Geometry"));
            if (_collideWithSceneGeometry.hasMultipleDifferentValues ||
                _collideWithSceneGeometry.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_collidersAsSceneFallback,
                    new GUIContent("Colliders As Fallback"));
                EditorGUI.indentLevel--;

                LineHairSimulation owner = ((HairGroupSimulation)target)
                    .GetComponentInParent<LineHairSimulation>();
                if (owner == null)
                {
                    HairInspectorGUI.Note("Scene field settings are stored on the parent simulation.");
                }
                else if (!owner.SceneCollisionSupported)
                {
                    EditorGUILayout.HelpBox(owner.SceneFieldBuild == HairSceneFieldBuild.RayTracing
                        ? "Inline ray tracing and 3D render textures are unavailable on the current GPU or " +
                          "graphics API. Only the colliders below collide."
                        : "Compute shaders or writable 3D half-float textures are unavailable on the current " +
                          "GPU or graphics API. Only the colliders below collide.", MessageType.Warning);
                }
                else
                {
                    HairInspectorGUI.Note("Uses the shared scene field configured on the parent simulation.");
                }
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_colliders, true);
            EditorGUILayout.PropertyField(_boneCapsules, new GUIContent("Skeleton Capsules"), true);
            EditorGUI.indentLevel--;
            DrawSkeletonFit();

            int shapes = ShapeCount();
            if (shapes > LineHairSimulation.MaxShapesPerGroup)
            {
                EditorGUILayout.HelpBox(
                    $"{shapes} shapes are listed but the solver uploads at most " +
                    $"{LineHairSimulation.MaxShapesPerGroup} per group. The rest are ignored.", MessageType.Warning);
            }
        }

        private void DrawSkeletonFit()
        {
            if (targets.Length != 1)
            {
                return;
            }

            if (HairInspectorGUI.Button("Fit Capsules To Skeleton",
                    "Replace the skeleton capsules with the bodies Unreal's Physics Asset generator would make " +
                    "for the character's enabled skinned meshes, keeping those the strands can reach, nearest " +
                    "to the roots first, up to the group's free collision slots."))
            {
                var simulation = (HairGroupSimulation)target;
                serializedObject.ApplyModifiedProperties();
                _fitSucceeded = HairSkeletonColliders.Fit(simulation, out LineHairSimulation.BoneCapsule[] capsules,
                                                          out _fitReport);
                if (_fitSucceeded)
                {
                    Undo.RecordObject(simulation, "Fit Hair Capsules To Skeleton");
                    simulation.Settings.BoneCapsules = capsules;
                    EditorUtility.SetDirty(simulation);
                    serializedObject.Update();
                    simulation.GetComponentInParent<LineHairSimulation>()?.RefreshGroups();
                    SceneView.RepaintAll();
                }
            }

            if (!string.IsNullOrEmpty(_fitReport))
            {
                if (_fitSucceeded)
                {
                    HairInspectorGUI.Note("Fitted " + _fitReport + ".");
                }
                else
                {
                    EditorGUILayout.HelpBox("Nothing was fitted: " + _fitReport + ". The list is unchanged.",
                                            MessageType.Warning);
                }
            }
        }

        private void DrawSavePresetButton()
        {
            if (targets.Length != 1 || (HairSimulationMode)_mode.enumValueIndex != HairSimulationMode.Custom)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            if (!HairInspectorGUI.Button(
                    "Save to Preset",
                    "Create a reusable ScriptableObject from this group's custom physics settings."))
            {
                return;
            }

            HairGroupSimulation simulation = (HairGroupSimulation)target;
            string path = EditorUtility.SaveFilePanelInProject("Save Hair Simulation Preset",
                simulation.name + " Simulation", "asset", "Choose where to save the preset.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            HairSimulationPreset preset = ScriptableObject.CreateInstance<HairSimulationPreset>();
            preset.CopyFrom(simulation.Settings);
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            _preset.objectReferenceValue = preset;
            _mode.enumValueIndex = (int)HairSimulationMode.Preset;
            serializedObject.ApplyModifiedProperties();
            EditorGUIUtility.PingObject(preset);
        }

        private int ShapeCount()
        {
            int most = 0;
            foreach (Object targetObject in targets)
            {
                HairGroupSimulation simulation = (HairGroupSimulation)targetObject;
                int count = (simulation.Settings.Colliders != null ? simulation.Settings.Colliders.Length : 0) +
                            (simulation.Settings.BoneCapsules != null ? simulation.Settings.BoneCapsules.Length : 0);

                most = Mathf.Max(most, count);
            }

            return most;
        }

        private string SolverSummary()
        {
            if (_substeps.hasMultipleDifferentValues || _iterations.hasMultipleDifferentValues)
            {
                return "mixed";
            }

            return $"{_substeps.intValue} x {_iterations.intValue}";
        }

        private string ConstraintSummary()
        {
            if (_volumeVelocity.hasMultipleDifferentValues || _volumePressure.hasMultipleDifferentValues)
            {
                return "mixed";
            }

            return _volumeVelocity.floatValue > 0f || _volumePressure.floatValue > 0f ? "volume" : "no volume";
        }

        private string CollisionSummary()
        {
            int shapes = ShapeCount();
            string shapeSummary = shapes == 1 ? "1 shape" : $"{shapes} shapes";
            bool scene = !_collideWithSceneGeometry.hasMultipleDifferentValues &&
                         _collideWithSceneGeometry.boolValue;
            if (!scene)
            {
                return shapeSummary;
            }

            bool fallback = !_collidersAsSceneFallback.hasMultipleDifferentValues &&
                            _collidersAsSceneFallback.boolValue;
            return fallback ? "scene, " + shapeSummary + " fallback" : shapeSummary + " + scene";
        }
    }
}
