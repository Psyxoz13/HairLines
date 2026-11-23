using UnityEngine;

namespace HairLines
{
    [CreateAssetMenu(fileName = "HairGroupPreset", menuName = "Hair Lines/Group Preset")]
    public class HairGroupPreset : ScriptableObject
    {
        public HairGrowSettings Settings => _settings ??= new HairGrowSettings();

        [SerializeField]
        private HairGrowSettings _settings = new HairGrowSettings();

        public HairGrowSettings CreateSettings()
        {
            return _settings == null ? new HairGrowSettings() : _settings.Clone();
        }

        public void CopyFrom(HairGrowSettings source)
        {
            _settings ??= new HairGrowSettings();
            _settings.CopyFrom(source);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _settings ??= new HairGrowSettings();
        }
#endif
    }
}
