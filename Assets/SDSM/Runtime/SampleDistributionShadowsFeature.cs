using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SDSM
{
    [DisallowMultipleRendererFeature("Sample Distribution Shadows")]
    public sealed class SampleDistributionShadowsFeature : ScriptableRendererFeature
    {
        [SerializeField, HideInInspector]
        private ComputeShader _depthBounds;

        private DepthRangePass _pass;

        public override void Create()
        {
#if UNITY_EDITOR
            if (_depthBounds == null)
            {
                foreach (string guid in UnityEditor.AssetDatabase.FindAssets("SDSMDepthBounds t:ComputeShader"))
                {
                    var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                        UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (shader != null && shader.name == "SDSMDepthBounds")
                    {
                        _depthBounds = shader;
                        break;
                    }
                }
            }
#endif
            _pass ??= new DepthRangePass();
            _pass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            SampleDistributionShadows.EnsureSubscribed();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (_depthBounds == null || camera == null ||
                camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection ||
                !SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback)
            {
                return;
            }

            _pass.Shader = _depthBounds;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            SampleDistributionShadows.ReleaseAll();
        }

        private sealed class DepthRangePass : ScriptableRenderPass
        {
            internal ComputeShader Shader;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                SampleDistributionShadows.Record(renderGraph, frameData, Shader);
            }
        }
    }
}
