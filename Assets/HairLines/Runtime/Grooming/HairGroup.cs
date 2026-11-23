using UnityEngine;

namespace HairLines
{
    [ExecuteAlways]
    [AddComponentMenu("Hair Lines/Hair Group")]
    [DisallowMultipleComponent]
    public class HairGroup : MonoBehaviour
    {
        public HairGroupMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value)
                {
                    return;
                }

                _mode = value;
                NotifyOwner(true);
            }
        }
        public HairGroupPreset Preset
        {
            get => _preset;
            set
            {
                if (_preset == value)
                {
                    return;
                }

                _preset = value;
                NotifyOwner(true);
            }
        }
        public HairGrowSettings Settings => _settings ??= new HairGrowSettings();
        public HairGrowSettings ResolvedSettings
        {
            get
            {
                return _mode == HairGroupMode.Preset && _preset != null && _preset.Settings != null
                    ? _preset.Settings
                    : Settings;
            }
        }
        public Material Material
        {
            get => ResolvedSettings.Material;
            set
            {
                ResolvedSettings.Material = value;
                NotifyOwner(false);
            }
        }
        public Color Tint
        {
            get => ResolvedSettings.Tint;
            set
            {
                ResolvedSettings.Tint = value;
                NotifyOwner(false);
            }
        }
        public bool ClaimsWholeScalp => !ResolvedSettings.DensityMask.IsUsable;

        [Tooltip("Choose local Custom grooming values or a reusable Preset asset.")]
        [SerializeField]
        private HairGroupMode _mode = HairGroupMode.Custom;

        [Tooltip("Reusable grooming preset used when Mode is Preset.")]
        [SerializeField]
        private HairGroupPreset _preset;

        [Tooltip("Per-group grooming values used when Mode is Custom.")]
        [SerializeField]
        private HairGrowSettings _settings = new HairGrowSettings();

        public IHairDensityMask ResolveMask()
        {
            HairDensityMask mask = ResolvedSettings.DensityMask;
            return mask.IsUsable ? mask : null;
        }

        public void CopyResolvedSettingsTo(HairGrowSettings destination)
        {
            if (destination == null)
            {
                return;
            }

            destination.CopyFrom(ResolvedSettings);
        }

        public void RandomizeSeed()
        {
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "Randomize Seed");
#endif
            _settings.Seed = Random.Range(int.MinValue, int.MaxValue);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _settings ??= new HairGrowSettings();
            NotifyOwner(true);
        }
#endif

        private void NotifyOwner(bool rebuild)
        {
            LinePrimitiveRenderer renderer = GetComponentInParent<LinePrimitiveRenderer>();
            if (renderer == null)
            {
                return;
            }

            if (rebuild)
            {
                renderer.Rebuild();
            }
            else
            {
                renderer.RefreshGroups();
            }
        }
    }
}
