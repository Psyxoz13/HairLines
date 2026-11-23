using UnityEditor;
using UnityEngine;

namespace HairLines.EditorTools
{
    public static class HairMaskBrush
    {
        public static float Diameter
        {
            get => EditorPrefs.GetFloat(DiameterKey, DefaultDiameter);
            set => EditorPrefs.SetFloat(DiameterKey, Mathf.Max(MinDiameter, value));
        }

        public static float Radius
        {
            get => Diameter * 0.5f;
            set => Diameter = value * 2f;
        }

        public static float Strength
        {
            get => EditorPrefs.GetFloat(StrengthKey, 0.5f);
            set => EditorPrefs.SetFloat(StrengthKey, Mathf.Clamp01(value));
        }

        public static float Hardness
        {
            get => EditorPrefs.GetFloat(HardnessKey, 0.3f);
            set => EditorPrefs.SetFloat(HardnessKey, Mathf.Clamp01(value));
        }

        public static bool MirrorX
        {
            get => EditorPrefs.GetBool(MirrorKey, true);
            set => EditorPrefs.SetBool(MirrorKey, value);
        }

        public static bool Erase
        {
            get => EditorPrefs.GetBool(EraseKey, false);
            set => EditorPrefs.SetBool(EraseKey, value);
        }

        public static bool Subtracts(bool shiftHeld)
        {
            return Erase ^ shiftHeld;
        }

        public const float MinDiameter = 0.002f;
        public const float DefaultDiameter = 0.03f;

        private const string Prefix = "HairLines.Brush.";
        private const string DiameterKey = Prefix + "Diameter";
        private const string StrengthKey = Prefix + "Strength";
        private const string HardnessKey = Prefix + "Hardness";
        private const string MirrorKey = Prefix + "MirrorX";
        private const string EraseKey = Prefix + "Erase";
    }
}
