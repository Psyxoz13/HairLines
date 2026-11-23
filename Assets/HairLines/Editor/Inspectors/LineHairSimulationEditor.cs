using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    [CustomEditor(typeof(LineHairSimulation))]
    [CanEditMultipleObjects]
    public class LineHairSimulationEditor : UnityEditor.Editor
    {
        private const float GroupButtonWidth = 66f;

        private const string Prefix = "HairLines.Simulation.";

        private SerializedProperty _sceneCollisionMask, _sceneCollisionResolution, _sceneCollisionBoundsPadding;
        private SerializedProperty _sceneFieldBuild, _sceneFieldFlood, _sceneFieldFloodIterations,
            _sceneFieldDiagonalFlood;
        private SerializedProperty _simulateInEditMode, _volumeResolution, _pins, _prerollTime;
        private SerializedProperty _cacheMode, _cachePath;

        private SerializedProperty _simulationControlMode, _guideMapAsset, _guideLengthSweep;
        private SerializedProperty _simulationLodEnabled;
        private SerializedProperty _simulationLodMinimumBudgetFraction, _simulationLodMaxFrameInterval;
        private SerializedProperty _computeShader, _sceneCollisionShader, _sceneSplatShader;

        private int _builtHash;

        private void OnEnable()
        {
            _sceneCollisionMask = serializedObject.FindProperty("_sceneCollisionMask");
            _sceneCollisionResolution = serializedObject.FindProperty("_sceneCollisionResolution");
            _sceneCollisionBoundsPadding = serializedObject.FindProperty("_sceneCollisionBoundsPadding");
            _sceneFieldBuild = serializedObject.FindProperty("_sceneFieldBuild");
            _sceneFieldFlood = serializedObject.FindProperty("_sceneFieldFlood");
            _sceneFieldFloodIterations = serializedObject.FindProperty("_sceneFieldFloodIterations");
            _sceneFieldDiagonalFlood = serializedObject.FindProperty("_sceneFieldDiagonalFlood");

            _simulateInEditMode = serializedObject.FindProperty("_simulateInEditMode");
            _volumeResolution = serializedObject.FindProperty("_volumeResolution");
            _pins = serializedObject.FindProperty("_pins");
            _prerollTime = serializedObject.FindProperty("_prerollTime");
            _cacheMode = serializedObject.FindProperty("_cacheMode");
            _cachePath = serializedObject.FindProperty("_cachePath");
            _simulationControlMode = serializedObject.FindProperty("_simulationControlMode");
            _guideMapAsset = serializedObject.FindProperty("_guideMapAsset");
            _guideLengthSweep = serializedObject.FindProperty("_guideLengthSweep");
            _simulationLodEnabled = serializedObject.FindProperty("_simulationLodEnabled");
            _simulationLodMinimumBudgetFraction =
                serializedObject.FindProperty("_simulationLodMinimumBudgetFraction");
            _simulationLodMaxFrameInterval = serializedObject.FindProperty("_simulationLodMaxFrameInterval");
            _computeShader = serializedObject.FindProperty("_computeShader");
            _sceneCollisionShader = serializedObject.FindProperty("_sceneCollisionShader");
            _sceneSplatShader = serializedObject.FindProperty("_sceneSplatShader");
            _builtHash = GraphHash();
        }

        public override bool RequiresConstantRepaint() => IsRunning();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawGroupSection();
            DrawComputeShaderWarning();

            if (HairInspectorGUI.BeginSection(Prefix + "Playback", "Playback", PlaybackSummary()))
            {
                DrawPlayback();
            }
            HairInspectorGUI.EndSection();

            if (HairInspectorGUI.BeginSection(Prefix + "Pins", "Pins", PinSummary()))
            {
                DrawPins();
            }
            HairInspectorGUI.EndSection();

            if (HairInspectorGUI.BeginSection(Prefix + "Cache", "Cache", CacheSummary()))
            {
                DrawCache();
            }
            HairInspectorGUI.EndSection();

            if (HairInspectorGUI.BeginSection(Prefix + "SimulationLod", "Simulation LOD",
                    SimulationLodSummary()))
            {
                DrawSimulationLod();
            }
            HairInspectorGUI.EndSection();

            if (HairInspectorGUI.BeginSection(Prefix + "SceneCollisionField", "Scene Collision Field",
                    SceneCollisionFieldSummary()))
            {
                DrawSceneCollisionConfiguration();
            }
            HairInspectorGUI.EndSection();

            if (targets.Length == 1)
            {
                if (HairInspectorGUI.BeginSection(Prefix + "Stats", "Solver State", StateSummary()))
                {
                    DrawStats();
                }
                HairInspectorGUI.EndSection();
            }

            if (HairInspectorGUI.BeginSection(Prefix + "Assets", "Assets", AssetSummary(), false))
            {
                EditorGUILayout.PropertyField(_computeShader);
                EditorGUILayout.PropertyField(_sceneCollisionShader);
                EditorGUILayout.PropertyField(_sceneSplatShader);
            }
            HairInspectorGUI.EndSection();

            serializedObject.ApplyModifiedProperties();
            DrawActions();
        }

        private void DrawGroupSection()
        {
            if (HairInspectorGUI.BeginSection(Prefix + "Groups", "Groups", GroupSummary()))
            {
                DrawGroups();
            }
            HairInspectorGUI.EndSection();
        }

        private void DrawGroups()
        {
            if (targets.Length != 1)
            {
                HairInspectorGUI.Note("Select one hair object to inspect its groups.");
                return;
            }

            LineHairSimulation simulation = (LineHairSimulation)target;
            LinePrimitiveRenderer renderer = simulation.GetComponent<LinePrimitiveRenderer>();
            if (renderer == null)
            {
                HairInspectorGUI.Note("No Hair Lines renderer is attached to this object.");
                return;
            }

            HairGroup[] children = renderer.GetComponentsInChildren<HairGroup>(false);
            if (children.Length == 0)
            {
                EditorGUILayout.HelpBox("No groups, so nothing is simulated. The settings here are only " +
                                        "the defaults a group without its own Hair Group Simulation picks up.",
                                        MessageType.Warning);
                return;
            }

            foreach (HairGroup group in children)
            {
                DrawGroupRow(group);
            }

            HairInspectorGUI.Note("A group is simulated only with a Hair Group Simulation component on it. " +
                                  "Without one it keeps its rest pose.");
        }

        private void DrawGroupRow(HairGroup group)
        {
            HairGroupSimulation groupSimulation = group.GetComponent<HairGroupSimulation>();
            string state = groupSimulation == null
                ? "not simulated"
                : groupSimulation.isActiveAndEnabled ? "simulated" : "disabled";

            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var button = new Rect(row.xMax - GroupButtonWidth, row.y, GroupButtonWidth, row.height);
            var label = new Rect(row.x, row.y, button.x - row.x - 4f, row.height);

            EditorGUI.LabelField(label,
                HairInspectorGUI.TreePrefix(((LineHairSimulation)target).transform, group.transform) +
                group.name,
                $"{HairInspectorGUI.Count(group.ResolvedSettings.StrandCount)} strands · {state}");

            if (groupSimulation != null)
            {
                if (GUI.Button(button, new GUIContent("Select", "Ping and select this group."),
                               EditorStyles.miniButton))
                {
                    EditorGUIUtility.PingObject(group.gameObject);
                    Selection.activeGameObject = group.gameObject;
                }

                return;
            }

            if (GUI.Button(button, new GUIContent("Simulate", "Add Hair Group Simulation to this group."),
                           EditorStyles.miniButton))
            {
                Undo.AddComponent<HairGroupSimulation>(group.gameObject);
                EditorGUIUtility.PingObject(group.gameObject);
            }
        }

        private bool HasHairGroups()
        {
            foreach (Object targetObject in targets)
            {
                LinePrimitiveRenderer renderer = ((LineHairSimulation)targetObject)
                    .GetComponent<LinePrimitiveRenderer>();
                if (renderer != null && renderer.GetComponentsInChildren<HairGroup>(false).Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawPlayback()
        {
            EditorGUILayout.PropertyField(_simulateInEditMode);
            EditorGUILayout.PropertyField(_prerollTime, new GUIContent("Preroll"));
            EditorGUILayout.PropertyField(_volumeResolution, new GUIContent("Volume Resolution",
                "Cells per axis of the voxel grid strands interact through."));
            EditorGUILayout.PropertyField(_simulationControlMode,
                new GUIContent("Control Mode",
                    "Full control simulates every strand. Guide control simulates only the strands " +
                    "named by a baked guide map and reconstructs the rest."));
            bool guideRequested = !_simulationControlMode.hasMultipleDifferentValues &&
                                  _simulationControlMode.enumValueIndex ==
                                      (int)HairSimulationControlMode.GuideControl;
            if (guideRequested)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_guideMapAsset, new GUIContent("Guide Map"));
                EditorGUILayout.PropertyField(_guideLengthSweep, new GUIContent("Length Sweep"));

                string reason = targets.Length == 1
                    ? ((LineHairSimulation)target).InspectGuideMap() ??
                      ((LineHairSimulation)target).GuideFallbackReason
                    : null;
                bool hasMap = _guideMapAsset.objectReferenceValue != null;
                bool rejected = hasMap && !string.IsNullOrEmpty(reason);
                if (rejected)
                {
                    EditorGUILayout.HelpBox("The map is not driving this groom, so every strand " +
                                            "is simulated: " + reason, MessageType.Error);
                }

                if (!hasMap || rejected)
                {
                    DrawGuideMapBakeRow(rejected);
                }

                EditorGUI.indentLevel--;

                if (!hasMap)
                {
                    HairInspectorGUI.Note("Without a baked map the solver keeps simulating every " +
                                          "strand. 25% is the share that holds up in motion; bake " +
                                          "100% when something looks wrong, because there every " +
                                          "strand is its own guide and the result has to match " +
                                          "full control exactly.");
                }
            }

            bool stepping = Application.isPlaying ||
                            _simulateInEditMode.hasMultipleDifferentValues ||
                            _simulateInEditMode.boolValue;
            if (!stepping)
                HairInspectorGUI.Note("The strands stay in their rest pose outside Play Mode.");
        }

        private void DrawPins()
        {
            EditorGUILayout.PropertyField(_pins, new GUIContent("Pins"), true);
            if (targets.Length == 1 && HairInspectorGUI.Button("Add Pin",
                    "Create a Hair Pin under this hair object and add it to the list. Move it onto the " +
                    "strands it should hold, or parent it to the bone or clip that carries them."))
            {
                var simulation = (LineHairSimulation)target;
                var pinObject = new GameObject("Hair Pin");
                Undo.RegisterCreatedObjectUndo(pinObject, "Add Hair Pin");
                pinObject.transform.SetParent(simulation.transform, false);
                HairPin pin = pinObject.AddComponent<HairPin>();
                _pins.arraySize++;
                _pins.GetArrayElementAtIndex(_pins.arraySize - 1).objectReferenceValue = pin;
                serializedObject.ApplyModifiedProperties();
                Selection.activeGameObject = pinObject;
                GUIUtility.ExitGUI();
            }

            HairInspectorGUI.Note("A pin owns the strand vertices resting inside its sphere, chosen when the " +
                                  "simulation starts or on Reset Simulation. Enabled, it carries them wherever " +
                                  "it moves: exactly at Compliance 0, on a spring above it. Disabling releases " +
                                  "them; enabling grabs them where they hang at that moment. It follows its own " +
                                  "transform, or the skin of its Surface mesh. Under guide control the " +
                                  "reconstructed strands keep exact pins too.");
        }

        private void DrawCache()
        {
            HairInspectorGUI.EnumToolbar(_cacheMode, "Mode");
            EditorGUILayout.DelayedTextField(_cachePath, new GUIContent("File",
                "Cache file. A relative path is placed under StreamingAssets. Applied on Enter."));

            if (targets.Length != 1 || _cacheMode.hasMultipleDifferentValues)
            {
                return;
            }

            var simulation = (LineHairSimulation)target;
            if (!string.IsNullOrEmpty(simulation.CacheError))
            {
                EditorGUILayout.HelpBox("The cache is not used: " + simulation.CacheError + ".", MessageType.Error);
            }

            switch ((HairSimulationCacheMode)_cacheMode.enumValueIndex)
            {
                case HairSimulationCacheMode.Record:
                    HairInspectorGUI.Note("Every shown frame that solved a step is written to the file, starting " +
                                          "with the first frame after Preroll. An existing file is replaced. " +
                                          "Switch to Play afterwards to replay it without simulating.");
                    break;
                case HairSimulationCacheMode.Play:
                    HairInspectorGUI.Note("The solver is replaced by the file; the strands it does not own are " +
                                          "reconstructed as usual, and the cache follows the object's transform. " +
                                          "A script can set Cache Time to jump within the file.");
                    break;
            }
        }

        private string CacheSummary()
        {
            if (_cacheMode.hasMultipleDifferentValues)
            {
                return "mixed";
            }

            var mode = (HairSimulationCacheMode)_cacheMode.enumValueIndex;
            if (targets.Length != 1 || mode == HairSimulationCacheMode.Off)
            {
                return mode.ToString().ToLowerInvariant();
            }

            var simulation = (LineHairSimulation)target;
            if (mode == HairSimulationCacheMode.Record)
            {
                return simulation.CacheFrameCount > 0
                    ? $"recording · {simulation.CacheFrameCount} frames · " +
                      HairInspectorGUI.Memory(simulation.CacheBytes)
                    : "record";
            }

            return simulation.PlayingCache
                ? $"play · {simulation.CacheTime:0.00} / {simulation.CacheDuration:0.00} s"
                : "play";
        }

        private string PinSummary()
        {
            if (_pins.hasMultipleDifferentValues)
            {
                return "mixed";
            }

            if (_pins.arraySize == 0)
            {
                return "none";
            }

            var simulation = (LineHairSimulation)target;
            return simulation.IsReady
                ? $"{_pins.arraySize} · {HairInspectorGUI.Count(simulation.PinnedVertexCount)} vertices held"
                : $"{_pins.arraySize}";
        }

        private const string GuideFractionKey = Prefix + "GuideFraction";

        private const float DefaultGuideFraction = 0.25f;
        private const float BakeButtonWidth = 64f;

        private float _bakeFraction = -1f;

        private void DrawGuideMapBakeRow(bool rebake)
        {
            if (targets.Length != 1)
            {
                return;
            }

            var simulation = (LineHairSimulation)target;
            LinePrimitiveRenderer renderer = simulation.GetComponent<LinePrimitiveRenderer>();
            bool ready = renderer != null && renderer.Model != null && !renderer.Model.IsEmpty;
            var replaced = _guideMapAsset.objectReferenceValue as HairSimulationGuideAsset;

            if (_bakeFraction < 0f)
            {
                _bakeFraction = rebake && replaced != null && replaced.GuideFraction > 0f
                    ? Mathf.Clamp01(replaced.GuideFraction)
                    : EditorPrefs.GetFloat(GuideFractionKey, DefaultGuideFraction);
            }

            Rect row = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            var sliderRect = new Rect(row.x, row.y, row.width - BakeButtonWidth - 4f, row.height);
            var buttonRect = new Rect(row.xMax - BakeButtonWidth, row.y, BakeButtonWidth, row.height);

            EditorGUI.BeginChangeCheck();
            float percentage = EditorGUI.Slider(sliderRect,
                new GUIContent(rebake ? "Rebake Map" : "Bake New Map",
                    "Share of strands that keep solver state. 100% makes every strand its own " +
                    "guide and matches full control; smaller fractions are what actually reduces " +
                    "the physics budget."),
                _bakeFraction * 100f, 1f, 100f);
            if (EditorGUI.EndChangeCheck())
            {
                _bakeFraction = Mathf.Clamp01(percentage / 100f);
                EditorPrefs.SetFloat(GuideFractionKey, _bakeFraction);
            }

            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUI.Button(buttonRect, rebake ? "Rebake" : "Bake", EditorStyles.miniButton))
                {
                    float baked = Mathf.Clamp01(percentage / 100f);
                    HairSimulationGuideAsset asset = HairSimulationGuideAssetBuilder.Bake(
                        renderer, baked, baked >= 0.999f, rebake ? replaced : null);
                    if (asset != null)
                    {
                        _guideMapAsset.objectReferenceValue = asset;
                        serializedObject.ApplyModifiedProperties();
                        simulation.GuideMapAsset = asset;
                        simulation.RebuildConstraints();
                    }

                    GUIUtility.ExitGUI();
                }
            }

            if (!ready)
            {
                HairInspectorGUI.Note("The groom is still building; baking needs its final strands.");
            }
        }

        private void DrawSimulationLod()
        {
            EditorGUILayout.PropertyField(_simulationLodEnabled, new GUIContent("Enabled"));
            if (!_simulationLodEnabled.boolValue && !_simulationLodEnabled.hasMultipleDifferentValues)
            {
                HairInspectorGUI.Note("Disabled by default; the renderer LOD and solver budget remain unchanged.");
                return;
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_simulationLodMinimumBudgetFraction,
                new GUIContent("Minimum Iteration Fraction"));
            EditorGUILayout.PropertyField(_simulationLodMaxFrameInterval,
                new GUIContent("Maximum Frame Interval (legacy)"));
            EditorGUI.indentLevel--;

            DrawSimulationLodRendererWarning();
            HairInspectorGUI.Note("Uses the renderer's automatic camera LOD. At distance, solver iterations " +
                                  "are reduced with a smoothed, hysteretic fraction; substeps and the physical " +
                                  "fixed step stay constant to prevent a jerk at LOD boundaries. The renderer " +
                                  "LOD must be enabled. The frame-interval field is retained for scene compatibility.");
        }

        private void DrawSimulationLodRendererWarning()
        {
            bool missingRenderer = false;
            bool rendererLodDisabled = false;
            foreach (Object targetObject in targets)
            {
                LineHairSimulation simulation = targetObject as LineHairSimulation;
                LinePrimitiveRenderer renderer = simulation != null
                    ? simulation.GetComponent<LinePrimitiveRenderer>()
                    : null;
                if (renderer == null)
                {
                    missingRenderer = true;
                }
                else if (!renderer.LodEnabled)
                {
                    rendererLodDisabled = true;
                }
            }

            if (missingRenderer)
            {
                EditorGUILayout.HelpBox(
                    "Simulation LOD cannot use the renderer LOD because a Hair Lines renderer is missing. " +
                    "Add or restore the Line Primitive Renderer component.", MessageType.Error);
            }
            else if (rendererLodDisabled)
            {
                EditorGUILayout.HelpBox(
                    "Simulation LOD is enabled, but renderer LOD is disabled. Enable 'Enable LOD' on the " +
                    "Line Primitive Renderer so the distance-based simulation budget can take effect.",
                    MessageType.Error);
            }
        }

        private string SimulationLodSummary()
        {
            if (_simulationLodEnabled == null || _simulationLodEnabled.hasMultipleDifferentValues)
            {
                return "Mixed";
            }

            return _simulationLodEnabled.boolValue ? "Enabled" : "Disabled";
        }

        private void DrawSceneCollisionConfiguration()
        {
            EditorGUI.indentLevel++;
            HairInspectorGUI.EnumToolbar(_sceneFieldBuild, "Build");
            EditorGUILayout.PropertyField(_sceneCollisionMask, new GUIContent("Layer Mask"));
            EditorGUILayout.PropertyField(_sceneCollisionResolution, new GUIContent("Field Resolution"));
            EditorGUILayout.PropertyField(_sceneCollisionBoundsPadding,
                new GUIContent("Field Bounds Padding",
                    "Extra clearance around the simulated bounds when the scene distance field "
                    + "is built. The culling margin lives on the renderer."));

            bool splat = _sceneFieldBuild.hasMultipleDifferentValues ||
                         _sceneFieldBuild.enumValueIndex == (int)HairSceneFieldBuild.TriangleSplat;
            if (splat)
            {
                HairInspectorGUI.EnumToolbar(_sceneFieldFlood, "Flood");
                bool linear = _sceneFieldFlood.hasMultipleDifferentValues ||
                              _sceneFieldFlood.enumValueIndex == (int)HairSceneFieldFlood.Linear;
                if (linear)
                {
                    EditorGUILayout.PropertyField(_sceneFieldFloodIterations, new GUIContent("Flood Iterations"));
                }

                EditorGUILayout.PropertyField(_sceneFieldDiagonalFlood, new GUIContent("Diagonal Neighbours"));
            }
            EditorGUI.indentLevel--;

            bool rayTracing = _sceneFieldBuild.hasMultipleDifferentValues ||
                              _sceneFieldBuild.enumValueIndex == (int)HairSceneFieldBuild.RayTracing;
            if (!SceneCollisionSupported())
            {
                EditorGUILayout.HelpBox(rayTracing
                    ? "Inline ray tracing and 3D render textures are unavailable on the current GPU or " +
                      "graphics API. Only the group colliders collide."
                    : "Compute shaders or writable 3D half-float textures are unavailable on the current " +
                      "GPU or graphics API. Only the group colliders collide.", MessageType.Warning);
            }
            else if (splat)
            {
                HairInspectorGUI.Note("Opaque triangles of Mesh Renderers and Skinned Mesh Renderers inside the " +
                                      "bounds are splatted into a shared distance field. Static meshes need " +
                                      "Read/Write enabled.");
            }
            else
            {
                HairInspectorGUI.Note("Opaque Mesh Renderers and Skinned Mesh Renderers inside the bounds are " +
                                      "converted to a shared ray-traced distance field.");
            }
        }

        private void DrawStats()
        {
            var sim = (LineHairSimulation)target;

            if (!sim.IsReady)
            {
                EditorGUILayout.HelpBox(
                    "The solver is not initialised. It allocates on the first step, so enter Play " +
                    "Mode or turn on Simulate In Edit Mode.", MessageType.Info);
                return;
            }

            HairInspectorGUI.Stat("Stretch springs", HairInspectorGUI.Count(sim.StretchSpringCount));
            HairInspectorGUI.Stat("Bend springs", HairInspectorGUI.Count(sim.BendSpringCount));
            HairInspectorGUI.Stat("Hair volume", sim.VolumeActive ? $"{sim.VolumeResolution}³" : "off");
            HairInspectorGUI.Stat("Solver state", $"{HairInspectorGUI.Count(sim.SimulationStateCount)} / " +
                                  $"{HairInspectorGUI.Count(sim.VertexCount)} slots");
            HairInspectorGUI.Stat("Local backend", sim.FusedLocalSolverActive
                ? $"fused · {sim.FusedLocalSolverStatus}"
                : sim.FusedLocalSolver
                    ? $"reference · {sim.FusedLocalSolverStatus}"
                    : "reference");
            HairInspectorGUI.Stat("Control mode", sim.GuideControlActive
                ? $"guides · {HairInspectorGUI.Count(sim.SimulationGuideCount)} " +
                  $"({sim.SimulationGuideFraction:P1})"
                : sim.SimulationControlMode == HairSimulationControlMode.GuideControl
                    ? "full control (guide map rejected)"
                    : "full control");
            HairInspectorGUI.Stat("Solver buffers", HairInspectorGUI.Memory(sim.GpuBytes));
            if (sim.SceneCollisionReady)
            {
                HairInspectorGUI.Stat("Scene field", $"{sim.ActiveSceneCollisionResolution}³ · " +
                    $"{HairInspectorGUI.Count(sim.SceneCollisionInstanceCount)} renderers");
            }
        }

        private void DrawActions()
        {
            if (GraphHash() != _builtHash && GUIUtility.hotControl == 0 &&
                Event.current.type == EventType.Repaint)
            {
                foreach (var t in targets)
                    ((LineHairSimulation)t).RebuildConstraints();
                _builtHash = GraphHash();
                SceneView.RepaintAll();
            }

            if (HairInspectorGUI.Button("Reset Simulation",
                    "Put every strand back on its rest pose and let every pin grab the strands inside it again."))
            {
                foreach (var t in targets)
                    ((LineHairSimulation)t).RebuildConstraints();
                SceneView.RepaintAll();
            }
        }

        private void DrawComputeShaderWarning()
        {
            if (_computeShader.hasMultipleDifferentValues || _computeShader.objectReferenceValue != null)
            {
                return;
            }

            if (!HairInspectorGUI.HelpBoxWithButton(
                    "LineHairSimulation.compute is not assigned, so nothing can be solved.",
                    MessageType.Error, "Find"))
                return;

            var found = FindComputeShader();
            if (found == null)
            {
                Debug.LogError("[HairLines] LineHairSimulation.compute was not found in the project.");
                return;
            }

            _computeShader.objectReferenceValue = found;
            serializedObject.ApplyModifiedProperties();
        }

        private static ComputeShader FindComputeShader()
        {
            foreach (var guid in AssetDatabase.FindAssets("LineHairSimulation t:ComputeShader"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                if (shader != null)
                {
                    return shader;
                }
            }
            return null;
        }

        private int GraphHash() => HairInspectorGUI.HashProperties(_sceneCollisionResolution);

        private bool IsRunning()
        {
            foreach (var t in targets)
            {
                var sim = (LineHairSimulation)t;
                if (sim.isActiveAndEnabled && (Application.isPlaying || sim.SimulateInEditMode))
                {
                    return true;
                }
            }
            return false;
        }

        private string PlaybackSummary()
        {
            if (Application.isPlaying)
            {
                return "playing";
            }
            if (_simulateInEditMode.hasMultipleDifferentValues)
            {
                return "mixed";
            }
            return _simulateInEditMode.boolValue ? "live in edit mode" : "paused";
        }

        private string SceneCollisionFieldSummary()
        {
            if (_sceneCollisionResolution.hasMultipleDifferentValues || _sceneFieldBuild.hasMultipleDifferentValues)
            {
                return "mixed";
            }

            string build = _sceneFieldBuild.enumValueIndex == (int)HairSceneFieldBuild.TriangleSplat
                ? "splat"
                : "ray traced";
            return $"{_sceneCollisionResolution.intValue}³ {build}";
        }

        private bool SceneCollisionSupported()
        {
            foreach (Object targetObject in targets)
            {
                if (!((LineHairSimulation)targetObject).SceneCollisionSupported)
                {
                    return false;
                }
            }
            return true;
        }

        private string StateSummary()
        {
            return ((LineHairSimulation)target).IsReady ? "running" : "idle";
        }

        private string AssetSummary()
        {
            if (_computeShader.hasMultipleDifferentValues)
            {
                return "mixed";
            }
            var shader = _computeShader.objectReferenceValue;
            return shader != null ? shader.name : "missing";
        }

        private string GroupSummary()
        {
            if (targets.Length != 1)
            {
                return "multiple objects";
            }

            LinePrimitiveRenderer renderer = ((LineHairSimulation)target)
                .GetComponent<LinePrimitiveRenderer>();
            if (renderer == null)
            {
                return "no renderer";
            }

            HairGroup[] groups = renderer.GetComponentsInChildren<HairGroup>(false);
            int simulated = 0;
            foreach (HairGroup group in groups)
            {
                HairGroupSimulation simulation = group.GetComponent<HairGroupSimulation>();
                if (simulation != null && simulation.isActiveAndEnabled)
                {
                    simulated++;
                }
            }

            return $"{groups.Length} groups, {simulated} simulated";
        }
    }
}
