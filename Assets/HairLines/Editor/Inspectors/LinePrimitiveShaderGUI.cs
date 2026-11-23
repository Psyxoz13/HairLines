using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    public class LinePrimitiveShaderGUI : ShaderGUI
    {
        private const string Prefix = "HairLines.Material.";

        private const int ShadingHair = 0;
        private const int ShadingRadiance = 1;
        private const int ShadingTangent = 2;

        private const int SourceRgb = 0;
        private const int SourceMelanin = 1;

        private static readonly string[] ShadingNames = { "Hair", "Radiance", "Tangent" };
        private static readonly string[] SourceNames = { "RGB", "Melanin" };

        private const int PreviewSteps = 24;

        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            editor.SetDefaultGUIWidths();

            MaterialProperty shading = FindProperty("_Shading", properties, false);
            int shadingMode = shading != null ? DrawShading(editor, shading) : ShadingHair;

            bool hair = shadingMode < 0 || shadingMode == ShadingHair;
            bool radiance = shadingMode < 0 || shadingMode == ShadingRadiance;

            if (hair || radiance)
            {
                DrawColour(editor, properties, hair);
            }

            if (hair)
            {
                DrawHighlights(editor, properties);
                DrawLightThroughHair(editor, properties);
                DrawOcclusion(editor, properties);
            }

            DrawTips(editor, properties);
            DrawRasterization(editor, properties);
        }

        private static int DrawShading(MaterialEditor editor, MaterialProperty shading)
        {
            int mode = shading.hasMixedValue ? -1 : Mathf.RoundToInt(shading.floatValue);
            string summary = mode >= 0 && mode < ShadingNames.Length ? ShadingNames[mode] : "—";

            if (HairInspectorGUI.BeginSection(Prefix + "Shading", "Shading", summary))
            {
                mode = Toolbar(editor, shading, "Model",
                               "Hair: Marschner strand reflectance. Radiance: colour straight from the " +
                               "vertex attribute, as reconstructed models store it. Tangent: debug view.",
                               ShadingNames);
                switch (mode)
                {
                    case ShadingRadiance:
                        HairInspectorGUI.Note("Colour is read from the vertex attribute; lighting is off.");
                        break;
                    case ShadingTangent:
                        HairInspectorGUI.Note("Debug view: the strand tangent drawn as colour.");
                        break;
                }
            }
            HairInspectorGUI.EndSection();
            return mode;
        }

        private static void DrawColour(MaterialEditor editor, MaterialProperty[] properties, bool hair)
        {
            MaterialProperty source = FindProperty("_ColorSource", properties, false);
            int sourceMode = source == null || source.hasMixedValue ? -1 : Mathf.RoundToInt(source.floatValue);
            string summary = !hair ? "Tint" : sourceMode >= 0 && sourceMode < SourceNames.Length
                ? SourceNames[sourceMode]
                : "—";

            if (HairInspectorGUI.BeginSection(Prefix + "Colour", "Colour", summary))
            {
                if (hair && source != null)
                {
                    sourceMode = Toolbar(editor, source, "Source",
                                         "RGB: root and tip colours. Melanin: pigment concentrations, " +
                                         "turned into absorption as pbrt-v4 does.",
                                         SourceNames);

                    if (sourceMode < 0 || sourceMode == SourceRgb)
                    {
                        Field(editor, properties, "_RootColor", "Root", "Colour at the scalp.");
                        Field(editor, properties, "_TipColor", "Tip", "Colour at the end of the strand.");
                    }

                    if (sourceMode < 0 || sourceMode == SourceMelanin)
                    {
                        HairInspectorGUI.SubHeader("Root");
                        Field(editor, properties, "_RootEumelanin", "Eumelanin",
                              "Brown-black pigment. About 8 is black, 1.3 brown, 0.3 blonde.");
                        Field(editor, properties, "_RootPheomelanin", "Pheomelanin",
                              "Red-yellow pigment. Adds the red of auburn and ginger hair.");
                        HairInspectorGUI.SubHeader("Tip");
                        Field(editor, properties, "_TipEumelanin", "Eumelanin",
                              "Brown-black pigment at the end of the strand.");
                        Field(editor, properties, "_TipPheomelanin", "Pheomelanin",
                              "Red-yellow pigment at the end of the strand.");
                        DrawMelaninPreview(properties);
                    }

                    HairInspectorGUI.SubHeader("Overall");
                }

                Field(editor, properties, "_ColorTint", "Tint", "Multiplies the colour in every shading model.");
                if (hair)
                {
                    HairInspectorGUI.Note("Per-strand brightness comes from the groom's Color Variance.");
                }
            }
            HairInspectorGUI.EndSection();
        }

        private static void DrawHighlights(MaterialEditor editor, MaterialProperty[] properties)
        {
            if (HairInspectorGUI.BeginSection(Prefix + "Highlights", "Highlights"))
            {
                Field(editor, properties, "_Specular", "Specular",
                      "Strength of the surface highlight (R) and the coloured secondary one (TRT).");
                Field(editor, properties, "_Roughness", "Roughness",
                      "Width of both highlights along the strand.");
                Field(editor, properties, "_CuticleShift", "Cuticle Tilt",
                      "Tilt of the cuticle scales, in radians. Pulls the two highlights apart.");

                HairInspectorGUI.SubHeader("Per strand");
                Field(editor, properties, "_RoughnessVariation", "Roughness Spread",
                      "How far roughness strays from strand to strand, as a fraction of Roughness.");
                Field(editor, properties, "_CuticleShiftVariation", "Tilt Spread",
                      "How far the cuticle tilt strays from strand to strand, in radians.");
                HairInspectorGUI.Note(BothZero(properties, "_RoughnessVariation", "_CuticleShiftVariation")
                    ? "Every strand shares one highlight band."
                    : "Breaks the highlight band into strands.");
            }
            HairInspectorGUI.EndSection();
        }

        private static void DrawLightThroughHair(MaterialEditor editor, MaterialProperty[] properties)
        {
            if (HairInspectorGUI.BeginSection(Prefix + "Scattering", "Light Through Hair"))
            {
                Field(editor, properties, "_Transmission", "Transmission",
                      "Light passing straight through a fibre (TT). Lights the hair from behind.");
                Field(editor, properties, "_RimLight", "Rim Light",
                      "Forward scattering through many fibres: the glow of backlit hair.");
                Field(editor, properties, "_Scatter", "Local Scattering",
                      "Broad glow of dense hair, strongest with the light on the camera's side.");

                if (IsZero(properties, "_Transmission") && IsZero(properties, "_RimLight") &&
                    IsZero(properties, "_Scatter"))
                {
                    HairInspectorGUI.Note("No light passes through the hair.");
                }
                else
                {
                    Field(editor, properties, "_ScatterDepth", "Scatter Depth",
                          "Fibres light crosses before it has taken on the hair's own colour.");
                }
            }
            HairInspectorGUI.EndSection();
        }

        private static void DrawOcclusion(MaterialEditor editor, MaterialProperty[] properties)
        {
            if (HairInspectorGUI.BeginSection(Prefix + "Occlusion", "Ambient"))
            {
                Field(editor, properties, "_Ambient", "Sky Light",
                      "Light probes and the sky on the strands. Occluded by the groom when Volume " +
                      "Shadows is on in the renderer feature.");
                Field(editor, properties, "_RootShadow", "Root Shadow",
                      "Brightness left at the scalp, for the occlusion the shadow map is too coarse to see.");
            }
            HairInspectorGUI.EndSection();
        }

        private static void DrawTips(MaterialEditor editor, MaterialProperty[] properties)
        {
            MaterialProperty length = FindProperty("_TipTaperLength", properties, false);
            MaterialProperty width = FindProperty("_TipWidth", properties, false);
            bool off = length != null && !length.hasMixedValue && length.floatValue <= 0f;
            string summary = off ? "off" : length != null && width != null &&
                                          !length.hasMixedValue && !width.hasMixedValue
                ? $"{length.floatValue * 100f:0}% → {width.floatValue * 100f:0}%"
                : null;

            if (HairInspectorGUI.BeginSection(Prefix + "Tips", "Tip Taper", summary))
            {
                Field(editor, properties, "_TipTaperLength", "Length",
                      "Share of the strand, counted from the tip, over which it narrows.");
                if (off)
                {
                    HairInspectorGUI.Note("Strands keep their full width to the tip.");
                }
                else
                {
                    Field(editor, properties, "_TipWidth", "Width at Tip",
                          "Width left at the very end, as a fraction of the full strand.");
                }
            }
            HairInspectorGUI.EndSection();
        }

        private static void DrawRasterization(MaterialEditor editor, MaterialProperty[] properties)
        {
            MaterialProperty stochastic = FindProperty("_StochasticOpacity", properties, false);
            bool random = stochastic != null && !stochastic.hasMixedValue && stochastic.floatValue > 0.5f;

            if (HairInspectorGUI.BeginSection(Prefix + "Rasterization", "Rasterization",
                                             random ? "stochastic" : null, defaultExpanded: false))
            {
                Field(editor, properties, "_StochasticOpacity", "Stochastic Opacity",
                      "Draw an edge when its opacity beats a random threshold instead of a fixed one.");
                if (random)
                {
                    HairInspectorGUI.Note("Each edge draws a random threshold; the fixed one is unused.");
                }
                else
                {
                    Field(editor, properties, "_OpacityThreshold", "Opacity Threshold",
                          "An edge is drawn when its opacity exceeds this (tau in the paper, 0.5).");
                }
            }
            HairInspectorGUI.EndSection();
        }

        private static void DrawMelaninPreview(MaterialProperty[] properties)
        {
            MaterialProperty rootEu = FindProperty("_RootEumelanin", properties, false);
            MaterialProperty tipEu = FindProperty("_TipEumelanin", properties, false);
            MaterialProperty rootPheo = FindProperty("_RootPheomelanin", properties, false);
            MaterialProperty tipPheo = FindProperty("_TipPheomelanin", properties, false);
            MaterialProperty tint = FindProperty("_ColorTint", properties, false);
            if (rootEu == null || tipEu == null || rootPheo == null || tipPheo == null)
            {
                return;
            }

            Color tintColor = tint != null ? tint.colorValue : Color.white;
            GUILayout.Space(4f);
            DrawMelaninBar(new GUIContent("Hair Colour",
                    "Colour of the hair as a mass, root to tip: sky light and light scattered " +
                    "between strands."),
                rootEu, tipEu, rootPheo, tipPheo, tintColor, diffuse: true);
            GUILayout.Space(2f);
            DrawMelaninBar(new GUIContent("Through a Fibre",
                    "Colour of light passing through one fibre, root to tip: the backlit glow and " +
                    "the secondary highlight."),
                rootEu, tipEu, rootPheo, tipPheo, tintColor, diffuse: false);
        }

        private static void DrawMelaninBar(GUIContent label, MaterialProperty rootEu, MaterialProperty tipEu,
                                           MaterialProperty rootPheo, MaterialProperty tipPheo,
                                           Color tint, bool diffuse)
        {
            Rect rect = PrefixedRow(14f, label);
            bool linear = PlayerSettings.colorSpace == ColorSpace.Linear;
            float step = rect.width / PreviewSteps;
            for (int i = 0; i < PreviewSteps; i++)
            {
                float along = (i + 0.5f) / PreviewSteps;
                MelaninColours(Mathf.Lerp(rootEu.floatValue, tipEu.floatValue, along),
                               Mathf.Lerp(rootPheo.floatValue, tipPheo.floatValue, along),
                               out Color absorption, out Color scattered);
                Color colour = (diffuse ? scattered : absorption) * tint;
                colour.a = 1f;
                EditorGUI.DrawRect(new Rect(rect.x + i * step, rect.y, Mathf.Ceil(step), rect.height),
                                   linear ? colour.gamma : colour);
            }
        }

        private static void MelaninColours(float eumelanin, float pheomelanin,
                                           out Color absorption, out Color scattered)
        {
            var eumelaninSigmaA = new Vector3(0.419f, 0.697f, 1.37f);
            var pheomelaninSigmaA = new Vector3(0.187f, 0.4f, 1.05f);
            Vector3 sigmaA = Mathf.Max(eumelanin, 0f) * eumelaninSigmaA +
                             Mathf.Max(pheomelanin, 0f) * pheomelaninSigmaA;

            const float betaN = 0.3f;
            float denominator = (((((0.245f * betaN) + 5.574f) * betaN - 10.73f) * betaN + 2.532f) * betaN -
                                 0.215f) * betaN + 5.969f;

            absorption = new Color(Mathf.Exp(-4f * sigmaA.x), Mathf.Exp(-4f * sigmaA.y),
                                   Mathf.Exp(-4f * sigmaA.z), 1f);
            scattered = new Color(Mathf.Exp(-Mathf.Sqrt(sigmaA.x) * denominator),
                                  Mathf.Exp(-Mathf.Sqrt(sigmaA.y) * denominator),
                                  Mathf.Exp(-Mathf.Sqrt(sigmaA.z) * denominator), 1f);
        }

        private static int Toolbar(MaterialEditor editor, MaterialProperty property, string label,
                                   string tooltip, string[] names)
        {
            int current = property.hasMixedValue ? -1 : Mathf.RoundToInt(property.floatValue);
            Rect rect = PrefixedRow(EditorGUIUtility.singleLineHeight, new GUIContent(label, tooltip));

            EditorGUI.BeginChangeCheck();
            int picked = GUI.Toolbar(rect, current, names);
            if (EditorGUI.EndChangeCheck() && picked != current)
            {
                editor.RegisterPropertyChangeUndo(label);
                property.floatValue = picked;
                MaterialEditor.ApplyMaterialPropertyDrawers(editor.targets);
                current = picked;
            }

            return current;
        }

        private static Rect PrefixedRow(float height, GUIContent label)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 0f;
            rect = EditorGUI.PrefixLabel(rect, label);
            EditorGUIUtility.labelWidth = labelWidth;
            return rect;
        }

        private static void Field(MaterialEditor editor, MaterialProperty[] properties, string name,
                                  string label, string tooltip)
        {
            MaterialProperty property = FindProperty(name, properties, false);
            if (property != null)
            {
                editor.ShaderProperty(property, new GUIContent(label, tooltip));
            }
        }

        private static bool IsZero(MaterialProperty[] properties, string name)
        {
            MaterialProperty property = FindProperty(name, properties, false);
            return property != null && !property.hasMixedValue && property.floatValue <= 0f;
        }

        private static bool BothZero(MaterialProperty[] properties, string a, string b)
        {
            return IsZero(properties, a) && IsZero(properties, b);
        }
    }
}
