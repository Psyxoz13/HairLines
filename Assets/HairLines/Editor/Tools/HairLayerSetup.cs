using System;
using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    public static class HairLayerSetup
    {
        private const int UnderlayerStrands = 20000;
        private const int MainStrands = 40000;
        private const int FringeStrands = 9000;
        private const int FlyawayStrands = 1500;

        private const float ClumpSize = 0.055f;
        private const float SubClumpRatio = 3f;

        [MenuItem("Hair Lines/Build Layered Groom", priority = 15)]
        public static void BuildLayers(MenuCommand command)
        {
            LinePrimitiveRenderer hair = ResolveHair(command.context);

            if (hair == null)
            {
                EditorUtility.DisplayDialog("Hair Lines",
                    "Select a Hair Lines object first, or run Hair Lines / Create Hair.", "OK");
                return;
            }

            Texture2D density = FirstDensityMask(hair);
            Texture2D comb = FirstCombMap(hair);

            AddLayer(hair, "Underlayer", density, comb, Underlayer);
            AddLayer(hair, "Main", density, comb, Main);
            AddLayer(hair, "Fringe", density, comb, Fringe);
            AddLayer(hair, "Flyaway", density, comb, Flyaway);

            hair.RefreshGroups();
            hair.Rebuild();
            Selection.activeGameObject = hair.gameObject;
        }

        [MenuItem("Hair Lines/Build Layered Groom", validate = true)]
        private static bool ValidateBuildLayers(MenuCommand command)
        {
            return ResolveHair(command.context) != null;
        }

        private static void Underlayer(HairGrowSettings settings)
        {
            settings.Seed = 11;
            settings.StrandCount = UnderlayerStrands;
            settings.SegmentsPerStrand = 16;
            settings.StrandLength = 0.13f;
            settings.LengthVariance = 0.4f;
            settings.Stiffness = 0.9f;
            settings.Operators.Add(Multiplier(3, 0.004f, 0.012f));
            settings.Operators.Add(Clump(ClumpSize, 0f));
            settings.Operators.Add(new HairFrizzOperator { Amount = 0.003f, Scale = 0.2f, RootHold = 0.1f });
        }

        private static void Main(HairGrowSettings settings)
        {
            settings.Seed = 23;
            settings.StrandCount = MainStrands;
            settings.SegmentsPerStrand = 32;
            settings.StrandLength = 0.34f;
            settings.LengthVariance = 0.2f;
            settings.Operators.Add(Multiplier(2, 0.003f, 0.014f));
            settings.Operators.Add(new HairNoiseOperator { Amount = 0.03f, Scale = 0.22f, RootHold = 0.06f });
            settings.Operators.Add(Clump(ClumpSize, 0f));
            settings.Operators.Add(new HairFrizzOperator { Amount = 0.004f, Scale = 0.25f, RootHold = 0.15f });
            settings.Operators.Add(Clump(ClumpSize / SubClumpRatio, 0.05f));
            settings.Operators.Add(new HairFrizzOperator { Amount = 0.002f, Scale = 0.15f, RootHold = 0.2f });
            settings.Operators.Add(Clump(ClumpSize / (SubClumpRatio * SubClumpRatio), 0.08f));
        }

        private static void Fringe(HairGrowSettings settings)
        {
            settings.Seed = 37;
            settings.StrandCount = FringeStrands;
            settings.SegmentsPerStrand = 24;
            settings.StrandLength = 0.17f;
            settings.LengthVariance = 0.35f;
            settings.Operators.Add(new HairNoiseOperator { Amount = 0.012f, Scale = 0.14f, RootHold = 0.08f });
            settings.Operators.Add(Clump(ClumpSize / SubClumpRatio, 0.05f));
            settings.Operators.Add(new HairFrizzOperator { Amount = 0.002f, Scale = 0.15f, RootHold = 0.2f });
            settings.Operators.Add(Clump(ClumpSize / (SubClumpRatio * SubClumpRatio), 0.1f));
        }

        private static void Flyaway(HairGrowSettings settings)
        {
            settings.Seed = 53;
            settings.StrandCount = FlyawayStrands;
            settings.SegmentsPerStrand = 24;
            settings.StrandLength = 0.38f;
            settings.LengthVariance = 0.5f;
            settings.Operators.Add(new HairNoiseOperator { Amount = 0.025f, Scale = 0.16f, RootHold = 0.08f });
            settings.Operators.Add(new HairFrizzOperator { Amount = 0.006f, Scale = 0.3f, RootHold = 0.2f });
        }

        private static HairMultiplierOperator Multiplier(int copies, float spreadRoot, float spreadTip)
        {
            return new HairMultiplierOperator
            {
                Copies = copies,
                SpreadRoot = spreadRoot,
                SpreadTip = spreadTip,
            };
        }

        private static HairClumpOperator Clump(float size, float stray)
        {
            return new HairClumpOperator
            {
                Width = new AnimationCurve(new Keyframe(0f, size), new Keyframe(0.45f, size * 0.82f),
                                           new Keyframe(1f, size * 0.16f)),
                Stray = stray,
            };
        }

        private static void AddLayer(LinePrimitiveRenderer hair, string name, Texture2D density,
                                     Texture2D comb, Action<HairGrowSettings> build)
        {
            var layer = new GameObject(name);
            GameObjectUtility.SetParentAndAlign(layer, hair.gameObject);
            GameObjectUtility.EnsureUniqueNameForSibling(layer);

            HairGroup group = layer.AddComponent<HairGroup>();
            group.Settings.DensityMask.Texture = density;
            group.Settings.CombMap.Texture = comb;
            group.Settings.Operators.Clear();
            build(group.Settings);

            Undo.RegisterCreatedObjectUndo(layer, "Build Layered Groom");
            EditorUtility.SetDirty(layer);
        }

        private static Texture2D FirstDensityMask(LinePrimitiveRenderer hair)
        {
            foreach (HairGroup group in hair.GetComponentsInChildren<HairGroup>(true))
            {
                if (group.Settings.DensityMask.HasTexture)
                {
                    return group.Settings.DensityMask.Texture;
                }
            }

            return null;
        }

        private static Texture2D FirstCombMap(LinePrimitiveRenderer hair)
        {
            foreach (HairGroup group in hair.GetComponentsInChildren<HairGroup>(true))
            {
                if (group.Settings.CombMap.HasTexture)
                {
                    return group.Settings.CombMap.Texture;
                }
            }

            return null;
        }

        private static LinePrimitiveRenderer ResolveHair(UnityEngine.Object context)
        {
            var target = context as GameObject ?? Selection.activeGameObject;

            if (target == null)
            {
                return null;
            }

            return target.GetComponentInParent<LinePrimitiveRenderer>() ??
                   target.GetComponentInChildren<LinePrimitiveRenderer>(true);
        }
    }
}
