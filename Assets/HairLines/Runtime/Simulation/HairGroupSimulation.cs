using UnityEngine;

namespace HairLines
{
    [ExecuteAlways]
    [RequireComponent(typeof(HairGroup))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Hair Lines/Hair Group Simulation")]
    public class HairGroupSimulation : MonoBehaviour
    {
        public HairSimulationMode Mode { get => _mode; set => _mode = value; }
        public HairSimulationPreset Preset { get => _preset; set => _preset = value; }
        public HairSimulationSettings Settings => _settings;

        [Tooltip("Choose local Custom physics values or a reusable Preset asset.")]
        [SerializeField]
        private HairSimulationMode _mode = HairSimulationMode.Custom;

        [Tooltip("Reusable physics preset used when Mode is Preset.")]
        [SerializeField]
        private HairSimulationPreset _preset;

        [Tooltip("Per-group physics values used when Mode is Custom.")]
        [SerializeField]
        private HairSimulationSettings _settings = new HairSimulationSettings();

        public void CopyResolvedSettingsTo(HairSimulationSettings destination)
        {
            if (destination == null)
            {
                return;
            }

            if (_mode == HairSimulationMode.Preset && _preset != null && _preset.Settings != null)
            {
                destination.CopyValuesFrom(_preset.Settings);
                destination.Sanitize();
            }
            else
            {
                destination.CopyValuesFrom(_settings ?? new HairSimulationSettings());
                destination.Sanitize();
            }
        }

        private void OnEnable()
        {
            NotifyOwner();
        }

        private void OnDisable()
        {
            NotifyOwner();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _settings ??= new HairSimulationSettings();
            _settings.Sanitize();
            NotifyOwner();
        }
#endif

        private void NotifyOwner()
        {
            LinePrimitiveRenderer renderer = GetComponentInParent<LinePrimitiveRenderer>();
            renderer?.RefreshGroups();
            LineHairSimulation simulation = GetComponentInParent<LineHairSimulation>();
            if (simulation != null)
            {
                simulation.RefreshGroups();
            }
        }
    }
}
