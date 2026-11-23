using System.IO;
using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    [CustomEditor(typeof(LinePrimitiveRenderer))]
    [CanEditMultipleObjects]
    public class LinePrimitiveRendererEditor : UnityEditor.Editor
    {
        private SerializedProperty _source, _scalpSettings, _groomAsset, _material;

        private const float SelectButtonWidth = 56f;
        private SerializedProperty _displayFraction;
        private SerializedProperty _lodEnabled, _lodFullDetailCoverage, _lodMinimumDetailCoverage,
            _lodMinimumStrandFraction, _lodCurve;
        private SerializedProperty _cullingBoundsPadding;

        private bool _addGroupRequested;
        private bool _bakeRequested;
        private SerializedProperty _scalpMode, _scalpRenderer, _scalpSubmesh;
        private SerializedProperty _scalpRadius, _scalpCoverage, _scalpCenter;

        private int _builtHash;
        private bool _rebuildPending;

        private const string Prefix = "HairLines.Renderer.";
        private const float RowGap = 6f;
        private const float SquareCentimetresPerSquareMetre = 10000f;

        private void OnEnable()
        {
            _source = serializedObject.FindProperty("_source");
            _scalpSettings = serializedObject.FindProperty("_scalp");
            _groomAsset = serializedObject.FindProperty("_groomAsset");
            _material = serializedObject.FindProperty("_material");
            _displayFraction = serializedObject.FindProperty("_displayFraction");
            _lodEnabled = serializedObject.FindProperty("_lodEnabled");
            _lodFullDetailCoverage = serializedObject.FindProperty("_lodFullDetailCoverage");
            _lodMinimumDetailCoverage = serializedObject.FindProperty("_lodMinimumDetailCoverage");
            _lodMinimumStrandFraction = serializedObject.FindProperty("_lodMinimumStrandFraction");
            _lodCurve = serializedObject.FindProperty("_lodCurve");
            _cullingBoundsPadding = serializedObject.FindProperty("_cullingBoundsPadding");

            _scalpMode = _scalpSettings.FindPropertyRelative("_mode");
            _scalpRenderer = _scalpSettings.FindPropertyRelative("_renderer");
            _scalpSubmesh = _scalpSettings.FindPropertyRelative("_submesh");
            _scalpRadius = _scalpSettings.FindPropertyRelative("_sphereRadius");
            _scalpCoverage = _scalpSettings.FindPropertyRelative("_sphereCoverage");
            _scalpCenter = _scalpSettings.FindPropertyRelative("_sphereCenter");

            _builtHash = SourceHash();
        }

        public override bool RequiresConstantRepaint()
        {
            return _rebuildPending;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawFeatureWarning();

            if (HairInspectorGUI.BeginSection(Prefix + "Source", "Source", SourceSummary()))
            {
                DrawSource();
            }
            HairInspectorGUI.EndSection();

            if (HairInspectorGUI.BeginSection(Prefix + "LOD", "LOD", LodSummary()))
            {
                DrawLod();
            }
            HairInspectorGUI.EndSection();

            if (HairInspectorGUI.BeginSection(Prefix + "Culling", "Culling", CullingSummary()))
            {
                DrawCulling();
            }
            HairInspectorGUI.EndSection();

            bool assignDefaultMaterial = false;
            if (HairInspectorGUI.BeginSection(Prefix + "Appearance", "Appearance", MaterialSummary()))
            {
                assignDefaultMaterial = DrawAppearance();
            }
            HairInspectorGUI.EndSection();

            if (targets.Length == 1)
            {
                if (HairInspectorGUI.BeginSection(Prefix + "Model", "Model", ModelSummary()))
                {
                    DrawModelInfo();
                }
                HairInspectorGUI.EndSection();
            }

            bool changed = serializedObject.hasModifiedProperties;
            serializedObject.ApplyModifiedProperties();

            if (assignDefaultMaterial)
            {
                AssignDefaultMaterial();
            }

            if (_addGroupRequested)
            {
                AddGroup();
                _addGroupRequested = false;
            }

            if (_bakeRequested)
            {
                BakeGroom();
                _bakeRequested = false;
            }

            if (changed)
            {
                _rebuildPending = true;
            }

            DrawActions();

            if (_rebuildPending && GUIUtility.hotControl == 0 && Event.current.type == EventType.Repaint)
            {
                _rebuildPending = false;
                RebuildAll();
            }
        }

        private void DrawSource()
        {
            HairInspectorGUI.EnumToolbar(_source);

            if (_source.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("The selected objects use different source modes.", MessageType.Info);
                return;
            }

            if ((LinePrimitiveRenderer.SourceMode)_source.enumValueIndex == LinePrimitiveRenderer.SourceMode.Procedural)
            {
                DrawProcedural();
            }
            else
            {
                DrawAsset();
            }
        }

        private void DrawProcedural()
        {
            DrawScalp();
            DrawGroups();
            DrawWorkingDetail();
        }

        private void DrawWorkingDetail()
        {
            HairInspectorGUI.SubHeader("Working Detail");

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_displayFraction,
                new GUIContent("Display", "Share of the strands grown while you work."));

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                RebuildAll();
            }

            if (_displayFraction.hasMultipleDifferentValues || _displayFraction.floatValue >= 1f)
            {
                HairInspectorGUI.Note("Every strand is grown. Lower this while editing operators.");
                return;
            }

            int full = 0;
            int shown = 0;

            foreach (var t in targets)
            {
                var renderer = (LinePrimitiveRenderer)t;

                foreach (HairGroup group in renderer.GetComponentsInChildren<HairGroup>(false))
                {
                    int count = group.ResolvedSettings.StrandCount;
                    full += count;
                    shown += Mathf.Clamp(Mathf.CeilToInt(count * _displayFraction.floatValue), 1, count);
                }
            }

            HairInspectorGUI.Note($"Growing {HairInspectorGUI.Count(shown)} of " +
                                  $"{HairInspectorGUI.Count(full)} strands. The root order is " +
                                  "progressive, so these are the first strands of the full groom, " +
                                  "vertex for vertex.");
        }

        private void DrawGroups()
        {
            HairInspectorGUI.SubHeader("Groups");

            var renderer = (LinePrimitiveRenderer)target;
            HairGroup[] children = renderer.GetComponentsInChildren<HairGroup>(false);

            if (children.Length == 0)
            {
                EditorGUILayout.HelpBox("No groups, so nothing is grown, drawn or simulated. " +
                                        "Every strand belongs to a group.", MessageType.Warning);
            }
            else
            {
                foreach (HairGroup group in children)
                {
                    DrawGroupRow(group);
                }

                HairInspectorGUI.Note("Each group uses its own mask. " +
                                      "Hierarchy order does not change group coverage.");
            }

            if (HairInspectorGUI.Button("Add Group", "Create a child object with its own groom settings.",
                                        false, 20f))
            {
                _addGroupRequested = true;
            }
        }

        private void DrawGroupRow(HairGroup group)
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var select = new Rect(row.xMax - SelectButtonWidth, row.y, SelectButtonWidth, row.height);
            var label = new Rect(row.x, row.y, select.x - row.x - 4f, row.height);

            string material = group.Material != null ? group.Material.name : "inherited";
            string summary = $"{HairInspectorGUI.Count(group.ResolvedSettings.StrandCount)} strands" +
                             (group.ResolvedSettings.DensityMask.IsUsable ? ", masked" : ", whole scalp") +
                             $" · {material}";

            EditorGUI.LabelField(label,
                HairInspectorGUI.TreePrefix(((LinePrimitiveRenderer)target).transform, group.transform) +
                group.name, summary);

            if (GUI.Button(select, new GUIContent("Select", "Ping and select this group."),
                           EditorStyles.miniButton))
            {
                EditorGUIUtility.PingObject(group.gameObject);
                Selection.activeGameObject = group.gameObject;
            }
        }

        private bool HasGroupChildren()
        {
            return ((LinePrimitiveRenderer)target).GetComponentsInChildren<HairGroup>(false).Length > 0;
        }

        private void BakeGroom()
        {
            var renderer = (LinePrimitiveRenderer)target;
            HairGroomAsset asset = HairGroomBaker.Bake(renderer.Model, renderer.name);

            if (asset == null)
            {
                Debug.LogWarning("[HairLines] Nothing is built, so there is nothing to bake.", renderer);
                return;
            }

            _groomAsset.objectReferenceValue = asset;
            _source.enumValueIndex = (int)LinePrimitiveRenderer.SourceMode.Asset;
            serializedObject.ApplyModifiedProperties();

            RebuildAll();
            EditorGUIUtility.PingObject(asset);
        }

        private void AddGroup()
        {
            var renderer = (LinePrimitiveRenderer)target;
            int existing = renderer.GetComponentsInChildren<HairGroup>(true).Length;

            var child = new GameObject($"Group {existing + 1}");
            Undo.RegisterCreatedObjectUndo(child, "Add Hair Group");
            child.transform.SetParent(renderer.transform, false);

            HairGroup group = Undo.AddComponent<HairGroup>(child);
            group.Material = renderer.Material;
            group.Settings.Seed = group.Settings.Seed + existing;

            RebuildAll();
            Selection.activeGameObject = child;
        }

        private void DrawScalp()
        {
            HairInspectorGUI.SubHeader("Scalp");
            EditorGUILayout.PropertyField(_scalpMode);

            if (_scalpMode.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("The selected objects use different scalp modes.", MessageType.Info);
                return;
            }

            if ((HairScalpMode)_scalpMode.enumValueIndex == HairScalpMode.Sphere)
            {
                EditorGUILayout.PropertyField(_scalpRadius);
                EditorGUILayout.PropertyField(_scalpCoverage);
                EditorGUILayout.PropertyField(_scalpCenter);
                return;
            }

            EditorGUILayout.PropertyField(_scalpRenderer);
            EditorGUILayout.PropertyField(_scalpSubmesh);
            DrawScalpStatus();
        }

        private void DrawScalpStatus()
        {
            if (_scalpRenderer.hasMultipleDifferentValues)
            {
                return;
            }

            if (_scalpRenderer.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No scalp renderer assigned - nothing will be grown.", MessageType.Warning);
                return;
            }

            var mesh = MeshOf(_scalpRenderer.objectReferenceValue as Renderer);

            if (mesh == null)
            {
                EditorGUILayout.HelpBox("The renderer has no mesh. Pick a MeshRenderer or a SkinnedMeshRenderer.",
                                        MessageType.Error);
                return;
            }

            if (!mesh.isReadable)
            {
                EditorGUILayout.HelpBox($"'{mesh.name}' is not readable. Enable Read/Write in its import settings.",
                                        MessageType.Error);
                return;
            }

            if (mesh.uv == null || mesh.uv.Length == 0)
            {
                EditorGUILayout.HelpBox($"'{mesh.name}' has no UVs. Roots still bind, but masks and comb maps " +
                                        "will have nowhere to live.", MessageType.Warning);
                return;
            }

            if (HairMaskFactory.UvsOverlap(mesh))
            {
                EditorGUILayout.HelpBox($"The UVs of '{mesh.name}' overlap - the two halves of the head most " +
                                        "likely share one layout. Painting one side will paint the other.",
                                        MessageType.Warning);
            }
        }

        private void DrawAsset()
        {
            EditorGUILayout.PropertyField(_groomAsset, new GUIContent("Groom"));

            if (_groomAsset.hasMultipleDifferentValues)
            {
                return;
            }

            var asset = _groomAsset.objectReferenceValue as HairGroomAsset;

            if (asset == null)
            {
                EditorGUILayout.HelpBox("No groom assigned. Bake a procedural groom, or drop in a " +
                                        ".hair or .hairgroom file - both import into a groom asset.",
                                        MessageType.Warning);
                return;
            }

            HairInspectorGUI.Stat("Strands", HairInspectorGUI.Count(asset.StrandCount));
            HairInspectorGUI.Stat("Vertices", HairInspectorGUI.Count(asset.VertexCount));
            HairInspectorGUI.Stat("On disk", HairInspectorGUI.Memory(asset.ByteCount));
            HairInspectorGUI.Note("Loaded straight from the asset - no groom is generated on enable.");
        }

        private bool DrawAppearance()
        {
            EditorGUILayout.PropertyField(_material);
            HairInspectorGUI.Note("Groups can override this material in their own inspectors.");

            if (_material.hasMultipleDifferentValues)
            {
                return false;
            }

            var material = _material.objectReferenceValue as Material;

            if (material == null)
            {
                return HairInspectorGUI.HelpBoxWithButton(
                    "No material assigned - a built-in default is used, and nothing about the look " +
                    "can be edited on this renderer. Groups may still override it.", MessageType.Info, "Create");
            }

            if (material.shader != null && material.shader.name != LineShaderIds.LineShaderName)
            {
                return HairInspectorGUI.HelpBoxWithButton(
                    $"The material uses '{material.shader.name}'. Strands are drawn procedurally " +
                    $"and need '{LineShaderIds.LineShaderName}'.", MessageType.Error, "Fix");
            }

            return false;
        }

        private void DrawLod()
        {
            EditorGUILayout.PropertyField(_lodEnabled, new GUIContent("Enable LOD"));

            if (_lodEnabled.hasMultipleDifferentValues || !_lodEnabled.boolValue)
            {
                return;
            }

            EditorGUILayout.PropertyField(_lodFullDetailCoverage,
                new GUIContent("Full density coverage",
                    "Share of the frame height the groom must cover to keep every strand. " +
                    "Coverage rather than distance, so the same setting behaves identically " +
                    "at any resolution and field of view."));
            EditorGUILayout.PropertyField(_lodMinimumDetailCoverage,
                new GUIContent("Minimum density coverage"));
            EditorGUILayout.PropertyField(_lodMinimumStrandFraction,
                                          new GUIContent("Minimum strand fraction"));
            EditorGUILayout.PropertyField(_lodCurve, new GUIContent("Density falloff"));
            HairInspectorGUI.Note("LOD removes complete strands in a stable prefix inside every group, so " +
                                  "distant hair keeps each group's root distribution and does not shimmer.");
        }

        private void DrawCulling()
        {
            EditorGUILayout.PropertyField(_cullingBoundsPadding,
                new GUIContent("Bounds Padding",
                    "Extra margin around the culling bounds, in metres."));
            HairInspectorGUI.Note("Culling and shadow-caster tests use this box. Simulated bounds " +
                                  "arrive a few frames late through an async readback, so the margin " +
                                  "keeps swinging strands from popping at the edge of the frame. The " +
                                  "simulation keeps its own padding for the scene collision field.");
        }

        private string CullingSummary()
        {
            if (_cullingBoundsPadding == null || _cullingBoundsPadding.hasMultipleDifferentValues)
            {
                return "mixed";
            }

            float padding = _cullingBoundsPadding.floatValue;
            return padding > 0f ? $"+{padding:0.###} m" : "exact bounds";
        }

        private string LodSummary()
        {
            if (_lodEnabled == null || _lodEnabled.hasMultipleDifferentValues)
            {
                return "mixed";
            }

            if (!_lodEnabled.boolValue)
            {
                return "off";
            }

            return $"{_lodMinimumDetailCoverage.floatValue:P0}–{_lodFullDetailCoverage.floatValue:P0} of frame, " +
                   $"min {_lodMinimumStrandFraction.floatValue:P0}";
        }

        private void DrawModelInfo()
        {
            var renderer = (LinePrimitiveRenderer)target;
            LinePrimitiveModel model = renderer.Model;

            if (model == null)
            {
                EditorGUILayout.HelpBox("Nothing is built yet, so nothing is drawn.",
                                        MessageType.Warning);
                return;
            }

            long vertices = model.VertexCount;
            long edges = model.EdgeCount;
            int drawn = model.DrawnStrandCount;

            HairInspectorGUI.Stat("Strands",
                drawn != model.StrandCount
                    ? $"{HairInspectorGUI.Count(model.StrandCount)} stored, " +
                      $"{HairInspectorGUI.Count(drawn)} drawn"
                    : HairInspectorGUI.Count(model.StrandCount));
            HairInspectorGUI.Stat("Vertices", HairInspectorGUI.Count(vertices));
            HairInspectorGUI.Stat("Edges", HairInspectorGUI.Count(edges));
            HairInspectorGUI.Stat("Geometry buffers", HairInspectorGUI.Memory(model.GeometryBytes));

            Vector3 size = model.LocalBounds.size;
            HairInspectorGUI.Stat("Local bounds", $"{size.x:0.###} x {size.y:0.###} x {size.z:0.###} m");

            Bounds worldBounds = renderer.WorldBounds;
            HairInspectorGUI.Stat("World bounds", $"{worldBounds.size.x:0.###} x {worldBounds.size.y:0.###} x {worldBounds.size.z:0.###} m");

            if (renderer.Source == LinePrimitiveRenderer.SourceMode.Procedural &&
                HairInspectorGUI.Button("Bake to Groom Asset",
                                        "Write the built strands to a .hairgroom file and draw that instead, " +
                                        "so entering Play mode does not grow the groom again.", false, 20f))
            {
                _bakeRequested = true;
            }
        }

        private void DrawActions()
        {
            if (!HasModel())
            {
                EditorGUILayout.HelpBox("No model is built for this object yet.", MessageType.Info);
            }
        }

        private void DrawFeatureWarning()
        {
            if (HairLinesSetup.IsRendererFeatureInstalled())
            {
                return;
            }

            if (HairInspectorGUI.HelpBoxWithButton(
                    "The Hair Lines renderer feature is not installed on any URP renderer, " +
                    "so these strands are not drawn at all.", MessageType.Warning, "Install"))
            {
                HairLinesSetup.InstallRendererFeature();
            }
        }

        private void RebuildAll()
        {
            foreach (var t in targets)
            {
                ((LinePrimitiveRenderer)t).Rebuild();
            }

            _builtHash = SourceHash();
            SceneView.RepaintAll();
        }

        private void AssignDefaultMaterial()
        {
            var material = HairLinesSetup.GetOrCreateDefaultMaterial();

            if (material == null)
            {
                return;
            }

            foreach (var t in targets)
            {
                Undo.RecordObject(t, "Assign Hair Material");
                ((LinePrimitiveRenderer)t).Material = material;
                EditorUtility.SetDirty(t);
            }

            serializedObject.Update();
            EditorGUIUtility.PingObject(material);
        }

        private int SourceHash()
        {
            return HairInspectorGUI.HashProperties(_source, _scalpSettings, _groomAsset);
        }

        private bool HasModel()
        {
            foreach (var t in targets)
            {
                if (((LinePrimitiveRenderer)t).Model == null)
                {
                    return false;
                }
            }

            return true;
        }

        private string SourceSummary()
        {
            if (_source.hasMultipleDifferentValues)
            {
                return "mixed";
            }

            if ((LinePrimitiveRenderer.SourceMode)_source.enumValueIndex != LinePrimitiveRenderer.SourceMode.Procedural)
            {
                return _groomAsset.objectReferenceValue != null ? _groomAsset.objectReferenceValue.name : "no groom";
            }

            return _scalpMode.hasMultipleDifferentValues || (HairScalpMode)_scalpMode.enumValueIndex == HairScalpMode.Sphere
                ? "procedural, sphere"
                : "procedural, mesh";
        }

        private string MaterialSummary()
        {
            if (_material.hasMultipleDifferentValues)
            {
                return "mixed";
            }

            var material = _material.objectReferenceValue as Material;
            return material != null ? material.name : "default";
        }

        private string ModelSummary()
        {
            LinePrimitiveModel model = ((LinePrimitiveRenderer)target).Model;
            return model == null ? "not built" : $"{HairInspectorGUI.Count(model.StrandCount)} strands";
        }

        private static Mesh MeshOf(Renderer renderer)
        {
            if (renderer == null)
            {
                return null;
            }

            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }

        private static string MakeProjectRelative(string absolute)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');

            if (!root.EndsWith("/"))
            {
                root += "/";
            }

            string full = Path.GetFullPath(absolute).Replace('\\', '/');
            return full.StartsWith(root) ? full.Substring(root.Length) : full;
        }
    }
}
