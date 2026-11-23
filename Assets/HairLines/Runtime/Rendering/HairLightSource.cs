using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HairLines
{
    [DisallowMultipleComponent, RequireComponent(typeof(Light))]
    [AddComponentMenu("Hair Lines/Hair Light Source")]
    public class HairLightSource : MonoBehaviour
    {
        public const float DefaultRadius = 0.025f;
        public const float DefaultAngularDiameter = 0.5f;

        [Tooltip("Radius of a point or spot light's emitter, in metres. Widens the strand " +
                 "highlights by the angle the emitter subtends from each strand.")]
        [SerializeField, Min(0f)]
        private float _radius = DefaultRadius;

        [Tooltip("Angular diameter of a directional light's disc, in degrees. The sun is 0.5.")]
        [SerializeField, Range(0f, 90f)]
        private float _angularDiameter = DefaultAngularDiameter;

        public float Radius { get => _radius; set => _radius = Mathf.Max(0f, value); }
        public float AngularDiameter { get => _angularDiameter; set => _angularDiameter = Mathf.Clamp(value, 0f, 90f); }

        private static Vector4[] _sources;
        private static float[] _shadowStrengths;

        internal static float CollectSources(UniversalLightData lightData, out Vector4[] sources,
                                             out float[] shadowStrengths)
        {
            int capacity = UniversalRenderPipeline.maxVisibleAdditionalLights;
            if (_sources == null || _sources.Length != capacity)
                _sources = new Vector4[capacity];
            if (_shadowStrengths == null || _shadowStrengths.Length != capacity)
                _shadowStrengths = new float[capacity];
            sources = _sources;
            shadowStrengths = _shadowStrengths;

            float mainAngle = 0f;
            if (lightData == null)
                return mainAngle;

            var lights = lightData.visibleLights;
            int mainIndex = lightData.mainLightIndex;
            for (int i = 0, slot = 0; i < lights.Length && slot < capacity; i++)
            {
                VisibleLight visible = lights[i];
                Light light = visible.light;
                HairLightSource source = null;
                if (light != null)
                    light.TryGetComponent(out source);

                if (visible.lightType == LightType.Directional)
                {
                    float diameter = source != null ? source._angularDiameter : DefaultAngularDiameter;
                    float halfAngle = 0.5f * diameter * Mathf.Deg2Rad;
                    if (i == mainIndex)
                    {
                        mainAngle = halfAngle;
                        continue;
                    }
                    _shadowStrengths[slot] = ShadowStrength(light);
                    _sources[slot++] = new Vector4(0f, 0f, 0f, -Mathf.Max(halfAngle, 1e-6f));
                }
                else
                {
                    if (i == mainIndex)
                        continue;
                    Vector3 position = visible.localToWorldMatrix.GetColumn(3);
                    float radius = source != null ? source._radius : DefaultRadius;
                    _shadowStrengths[slot] = ShadowStrength(light);
                    _sources[slot++] = new Vector4(position.x, position.y, position.z, radius);
                }
            }

            return mainAngle;
        }

        private static float ShadowStrength(Light light)
        {
            return light != null && light.shadows != LightShadows.None ? light.shadowStrength : 0f;
        }
    }
}
