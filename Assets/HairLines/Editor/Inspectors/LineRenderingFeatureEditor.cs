using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HairLines.EditorTools
{
    [CustomEditor(typeof(LineRenderingFeature))]
    public class LineRenderingFeatureEditor : UnityEditor.Editor
    {
        private const string Prefix = "HairLines.Feature.";

        private SerializedProperty _supersampling, _filter, _gaussianSigma;
        private SerializedProperty _injectionPoint, _renderInSceneView, _motionVectors, _debugMode;
        private SerializedProperty _castShadows, _castShadowsOnScene, _shadowResolution;
        private SerializedProperty _fibreDiameter, _densityScale;
        private SerializedProperty _layerDistribution, _shadowDepthBias, _softness;
        private SerializedProperty _volumeShadows, _volumeResolution, _skyOcclusion;
        private SerializedProperty _skyRayMarch, _skyConeTrace, _skyGrid;

        private void OnEnable()
        {
            SerializedProperty settings = serializedObject.FindProperty("_settings");

            _supersampling = settings.FindPropertyRelative("_supersampling");
            _filter = settings.FindPropertyRelative("_filter");
            _gaussianSigma = settings.FindPropertyRelative("_gaussianSigmaPixels");

            _injectionPoint = settings.FindPropertyRelative("_injectionPoint");
            _renderInSceneView = settings.FindPropertyRelative("_renderInSceneView");
            _debugMode = settings.FindPropertyRelative("_debugMode");
            _motionVectors = settings.FindPropertyRelative("_motionVectors");

            _castShadows = settings.FindPropertyRelative("_castShadows");
            _castShadowsOnScene = settings.FindPropertyRelative("_castShadowsOnScene");
            _shadowResolution = settings.FindPropertyRelative("_shadowResolution");
            _fibreDiameter = settings.FindPropertyRelative("_fibreDiameter");
            _densityScale = settings.FindPropertyRelative("_densityScale");
            _layerDistribution = settings.FindPropertyRelative("_layerDistribution");
            _shadowDepthBias = settings.FindPropertyRelative("_shadowDepthBias");
            _softness = settings.FindPropertyRelative("_softness");
            _volumeShadows = settings.FindPropertyRelative("_volumeShadows");
            _volumeResolution = settings.FindPropertyRelative("_volumeResolution");
            _skyOcclusion = settings.FindPropertyRelative("_skyOcclusion");
            _skyRayMarch = settings.FindPropertyRelative("_skyRayMarch");
            _skyConeTrace = settings.FindPropertyRelative("_skyConeTrace");
            _skyGrid = settings.FindPropertyRelative("_skyGrid");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (HairInspectorGUI.BeginSection(Prefix + "Reconstruction", "Reconstruction",
                                             ReconstructionSummary()))
                DrawReconstruction();
            HairInspectorGUI.EndSection();

            if (HairInspectorGUI.BeginSection(Prefix + "Rasterization", "Rasterization"))
            {
                DrawRasterization();
            }
            HairInspectorGUI.EndSection();

            if (HairInspectorGUI.BeginSection(Prefix + "Shadows", "Shadows", ShadowSummary()))
            {
                DrawShadows();            }
            HairInspectorGUI.EndSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawReconstruction()
        {
            HairInspectorGUI.EnumToolbar(_filter, "Filter");

            HairReconstruction filter = (HairReconstruction)_filter.enumValueIndex;
            if (filter == HairReconstruction.None)
            {
                HairInspectorGUI.Note("Rasterized at native resolution; the subpixel grid is unused.");
                return;
            }

            EditorGUILayout.PropertyField(_supersampling);
            int s = Mathf.Clamp(_supersampling.intValue, 1, 4);
            HairInspectorGUI.Note($"Subpixel target: {s * s}x the camera pixel count.");

            if (filter == HairReconstruction.Gaussian)
            {
                EditorGUILayout.PropertyField(_gaussianSigma);
                HairInspectorGUI.Note($"{3 * s} taps per axis at the 3-sigma cutoff.");
            }
        }

        private void DrawRasterization()
        {
            EditorGUILayout.PropertyField(_injectionPoint);
            EditorGUILayout.PropertyField(_renderInSceneView);

            EditorGUILayout.PropertyField(_motionVectors);
            if ((HairMotionVectors)_motionVectors.enumValueIndex != HairMotionVectors.Off)
            {
                HairInspectorGUI.Note("Written only on cameras where URP builds motion vectors: " +
                                      "TAA, STP or object motion blur.");
                if ((RenderPassEvent)_injectionPoint.intValue > RenderPassEvent.BeforeRenderingPostProcessing)
                {
                    HairInspectorGUI.Note("The strands are drawn after post-processing, so there " +
                                          "is nothing left to write them into.");
                }
            }

            EditorGUILayout.PropertyField(_debugMode, new GUIContent("Debug View"));

            if ((HairDebugMode)_debugMode.enumValueIndex != HairDebugMode.Off)
            {
                HairInspectorGUI.Note(DebugNote((HairDebugMode)_debugMode.enumValueIndex));
            }
        }

        private static string DebugNote(HairDebugMode mode)
        {
            switch (mode)
            {
                case HairDebugMode.Group:
                    return "One hue per group. Strands the wrong colour are grown by the wrong group.";
                case HairDebugMode.RootUv:
                    return "Root UV in red and green. Seams and mirrored islands show up as hard edges.";
                case HairDebugMode.Seed:
                    return "One hue per strand. Repeating patches mean the seed is shared.";
                case HairDebugMode.ClumpId:
                    return "One hue per clump. Grey strands belong to no clump.";
                case HairDebugMode.Lod:
                    return "Green where every strand is drawn, red where the fraction is smallest.";
                case HairDebugMode.Coverage:
                    return "Orange where LOD compensation is widening strands to keep coverage.";
                case HairDebugMode.Length:
                    return "Pale where the strand is as long as the longest in the groom, blue where it is short.";
                default:
                    return string.Empty;
            }
        }

        private void DrawShadows()
        {
            EditorGUILayout.PropertyField(_castShadows);
            if (!_castShadows.boolValue)
            {
                HairInspectorGUI.Note("No opacity map is built - the main light lights the " +
                                      "strands flat.");
                DrawVolume();
                return;
            }

            EditorGUILayout.PropertyField(_castShadowsOnScene);
            if (!_castShadowsOnScene.boolValue)
            {
                HairInspectorGUI.Note("Strand-on-strand only; the scene sees no hair shadow.");
            }

            EditorGUILayout.PropertyField(_shadowResolution);
            int resolution = _shadowResolution.intValue;
            HairInspectorGUI.Note("Opacity map: " +
                HairInspectorGUI.Memory((long)resolution * resolution * 12L) + " for depth + 4 layers.");
            if (resolution >= 4096)
            {
                HairInspectorGUI.Note("Only the silhouette gets sharper at this size - softness " +
                                      "and darkness are physical lengths and do not scale with " +
                                      "resolution.");
            }

            HairInspectorGUI.SubHeader("Density");
            EditorGUILayout.PropertyField(_fibreDiameter);
            EditorGUILayout.PropertyField(_densityScale);

            HairInspectorGUI.SubHeader("Look");
            EditorGUILayout.PropertyField(_layerDistribution);
            EditorGUILayout.PropertyField(_softness);
            if (_softness.floatValue <= 0f)
            {
                HairInspectorGUI.Note("Hard edges - no blur pass is recorded.");
            }

            HairInspectorGUI.SubHeader("Bias");
            EditorGUILayout.PropertyField(_shadowDepthBias);

            DrawVolume();
        }

        private void DrawVolume()
        {
            HairInspectorGUI.SubHeader("Other lights");
            EditorGUILayout.PropertyField(_volumeShadows);

            if (!_volumeShadows.boolValue)
            {
                HairInspectorGUI.Note("Lights other than the main one, and the sky, see the hair " +
                                      "as unoccluded by itself.");
                return;
            }

            EditorGUILayout.PropertyField(_volumeResolution);

            int resolution = _volumeResolution.intValue;
            HairInspectorGUI.Note("Density volume: " +
                HairInspectorGUI.Memory((long)resolution * resolution * resolution * 2L) +
                " per casting groom, rebuilt every frame.");

            EditorGUILayout.PropertyField(_skyOcclusion);
            switch ((HairSkyOcclusion)_skyOcclusion.enumValueIndex)
            {
                case HairSkyOcclusion.RayMarch:
                    DrawChildren(_skyRayMarch);
                    break;
                case HairSkyOcclusion.ConeTrace:
                    DrawChildren(_skyConeTrace);
                    break;
                case HairSkyOcclusion.PrecomputedGrid:
                    DrawChildren(_skyGrid);
                    break;
            }

            WarnAboutAdditionalLights();
        }

        private static void DrawChildren(SerializedProperty parent)
        {
            SerializedProperty child = parent.Copy();
            SerializedProperty end = parent.GetEndProperty();

            EditorGUI.indentLevel++;
            for (bool more = child.NextVisible(true);
                 more && !SerializedProperty.EqualContents(child, end);
                 more = child.NextVisible(false))
            {
                EditorGUILayout.PropertyField(child, true);
            }
            EditorGUI.indentLevel--;
        }

        private static void WarnAboutAdditionalLights()
        {
            var pipeline = UniversalRenderPipeline.asset;
            if (pipeline == null || pipeline.additionalLightsRenderingMode == LightRenderingMode.PerPixel)
            {
                return;
            }

            string message = $"Additional Lights is set to {pipeline.additionalLightsRenderingMode} " +
                             $"on '{pipeline.name}'. Strands take additional lights per pixel only, " +
                             "so the volume is built every frame and nothing reads it.";

            if (!HairInspectorGUI.HelpBoxWithButton(message, MessageType.Warning, "Per Pixel"))
            {
                return;
            }

            var asset = new SerializedObject(pipeline);
            SerializedProperty mode = asset.FindProperty("m_AdditionalLightsRenderingMode");

            if (mode == null)
            {
                Debug.LogError("[HairLines] Could not find Additional Lights on the render " +
                               "pipeline asset; set it to Per Pixel by hand.", pipeline);
                return;
            }

            mode.intValue = (int)LightRenderingMode.PerPixel;
            asset.ApplyModifiedProperties();
        }

        private string ReconstructionSummary()
        {
            HairReconstruction filter = (HairReconstruction)_filter.enumValueIndex;
            if (filter == HairReconstruction.None)
            {
                return "off";
            }
            return $"{filter} {Mathf.Clamp(_supersampling.intValue, 1, 4)}x";
        }

        private string ShadowSummary()
        {
            if (!_castShadows.boolValue)
            {
                return "off";
            }
            return _castShadowsOnScene.boolValue
                ? $"{_shadowResolution.intValue} px, on scene"
                : $"{_shadowResolution.intValue} px, hair only";
        }
    }
}
