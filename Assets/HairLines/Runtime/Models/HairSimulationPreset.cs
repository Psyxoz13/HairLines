using UnityEngine;

namespace HairLines
{
    [CreateAssetMenu(fileName = "HairSimulationPreset", menuName = "Hair Lines/Simulation Preset")]
    public class HairSimulationPreset : ScriptableObject
    {
        public HairSimulationSettings Settings => _settings;

        [SerializeField]
        private HairSimulationSettings _settings = new HairSimulationSettings();

        public HairSimulationSettings CreateSettings()
        {
            return _settings == null ? new HairSimulationSettings() : _settings.Clone();
        }

        public void CopyFrom(HairSimulationSettings source)
        {
            _settings ??= new HairSimulationSettings();
            _settings.CopyFrom(source);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _settings ??= new HairSimulationSettings();
            _settings.Sanitize();
        }
#endif
    }
}
