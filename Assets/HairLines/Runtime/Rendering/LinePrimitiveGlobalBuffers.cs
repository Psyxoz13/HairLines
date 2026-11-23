using System.Runtime.InteropServices;
using UnityEngine;

namespace HairLines
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct HairShadowCasterData
    {
        public Matrix4x4 WorldToLight;
        public Vector4 LightDir;
        public Vector4 Layers;
        public Vector4 Filter;
        public Vector4 Density;

        public const int Stride = sizeof(float) * (16 + 4 * 4);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HairVolumeCasterData
    {
        public Vector4 Origin;
        public Vector4 Scale;
        public Vector4 SlabU;
        public Vector4 SlabW;

        public const int Stride = sizeof(float) * 4 * 4;
    }

    internal static class LinePrimitiveGlobalBuffers
    {
        private static GraphicsBuffer _positions;
        private static GraphicsBuffer _strandInfo;
        private static GraphicsBuffer _colors;
        private static GraphicsBuffer _indices;
        private static GraphicsBuffer _opacity;
        private static GraphicsBuffer _copyOffsets;
        private static GraphicsBuffer _shadowCasters;
        private static GraphicsBuffer _volumeCasters;

        private const int PositionStride = sizeof(float) * 3;
        private const int ColorStride = sizeof(float) * 4;
        private const int IndexStride = sizeof(uint);
        private const int OpacityStride = sizeof(float);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void BindOnLoad()
        {
            Bind();
            Application.quitting -= Release;
            Application.quitting += Release;
        }

        public static void Bind()
        {
            if (_positions != null && _positions.IsValid())
            {
                return;
            }

            Release();

            const GraphicsBuffer.Target structured = GraphicsBuffer.Target.Structured;
            _positions = new GraphicsBuffer(structured, 1, PositionStride) { name = "HairLines Dummy Positions" };
            _colors = new GraphicsBuffer(structured, 1, ColorStride) { name = "HairLines Dummy Colors" };
            _strandInfo = new GraphicsBuffer(structured, 1, sizeof(uint)) { name = "HairLines Dummy Strand Info" };
            _strandInfo.SetData(new uint[] { 0 });
            _indices = new GraphicsBuffer(structured, 1, IndexStride) { name = "HairLines Dummy Indices" };
            _opacity = new GraphicsBuffer(structured, 1, OpacityStride) { name = "HairLines Dummy Opacity" };
            _copyOffsets = new GraphicsBuffer(structured, 1, PositionStride) { name = "HairLines Dummy Copy Offsets" };
            _copyOffsets.SetData(new[] { Vector3.zero });

            _positions.SetData(new[] { Vector3.zero });
            _colors.SetData(new[] { Vector4.zero });
            _indices.SetData(new uint[] { 0 });
            _opacity.SetData(new[] { 0f });

            Shader.SetGlobalBuffer(LineShaderIds.Positions, _positions);
            Shader.SetGlobalBuffer(LineShaderIds.PrevPositions, _positions);
            Shader.SetGlobalMatrix(LineShaderIds.PrevObjectToWorld, Matrix4x4.identity);
            Shader.SetGlobalBuffer(LineShaderIds.StrandInfo, _strandInfo);
            Shader.SetGlobalFloat(LineShaderIds.HasVertexColor, 0f);
            Shader.SetGlobalBuffer(LineShaderIds.Colors, _colors);
            Shader.SetGlobalBuffer(LineShaderIds.Indices, _indices);
            Shader.SetGlobalBuffer(LineShaderIds.Opacity, _opacity);
            Shader.SetGlobalBuffer(LineShaderIds.CopyOffsets, _copyOffsets);
            Shader.SetGlobalVector(LineShaderIds.CopyParams, Vector4.zero);
            Shader.SetGlobalVector(LineShaderIds.CopySpread, Vector4.zero);
            Shader.SetGlobalVectorArray(LineShaderIds.HairLightSources,
                new Vector4[UnityEngine.Rendering.Universal.UniversalRenderPipeline.maxVisibleAdditionalLights]);
            Shader.SetGlobalFloatArray(LineShaderIds.HairLightShadowStrengths,
                new float[UnityEngine.Rendering.Universal.UniversalRenderPipeline.maxVisibleAdditionalLights]);
            Shader.SetGlobalInt(LineShaderIds.DrawVertexOffset, 0);
            Shader.SetGlobalVector(LineShaderIds.GroupTint, Vector4.one);

            Shader.SetGlobalTexture(LineShaderIds.HairShadowDepthTex, Texture2D.whiteTexture);
            Shader.SetGlobalTexture(LineShaderIds.HairShadowLayerTex, Texture2D.blackTexture);
            Shader.SetGlobalVector(LineShaderIds.HairShadowParams, Vector4.zero);

            Shader.SetGlobalInt(LineShaderIds.HairShadowCount, 0);
            PublishShadowCasters(new[] { new HairShadowCasterData
            {
                WorldToLight = Matrix4x4.identity,
                LightDir = new Vector4(0f, -1f, 0f, 0f),
                Layers = Vector4.one,
                Filter = new Vector4(1f, 1f, 0f, 0f),
            } }, 1);
            Shader.SetGlobalInt(LineShaderIds.HairVolumeCount, 0);
            PublishVolumeCasters(new[] { new HairVolumeCasterData() }, 1);
        }

        public static GraphicsBuffer VolumeCasters => _volumeCasters;

        public static void PublishShadowCasters(HairShadowCasterData[] casters, int count)
        {
            Publish(ref _shadowCasters, casters, count, HairShadowCasterData.Stride,
                    LineShaderIds.HairShadowCasters, "HairLines Shadow Casters");
        }

        public static void PublishVolumeCasters(HairVolumeCasterData[] casters, int count)
        {
            Publish(ref _volumeCasters, casters, count, HairVolumeCasterData.Stride,
                    LineShaderIds.HairVolumeCasters, "HairLines Volume Casters");
        }

        private static void Publish<T>(ref GraphicsBuffer buffer, T[] data, int count, int stride,
                                       int id, string name) where T : struct
        {
            if (buffer == null || !buffer.IsValid() || buffer.count < count)
            {
                buffer?.Dispose();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                                            Mathf.NextPowerOfTwo(Mathf.Max(1, count)), stride)
                {
                    name = name,
                };
            }

            if (count > 0)
            {
                buffer.SetData(data, 0, 0, count);
            }

            Shader.SetGlobalBuffer(id, buffer);
        }

        private static void Release()
        {
            _positions?.Dispose();
            _positions = null;

            _strandInfo?.Dispose();
            _strandInfo = null;

            _colors?.Dispose();
            _colors = null;

            _indices?.Dispose();
            _indices = null;

            _opacity?.Dispose();
            _opacity = null;

            _copyOffsets?.Dispose();
            _copyOffsets = null;

            _shadowCasters?.Dispose();
            _shadowCasters = null;

            _volumeCasters?.Dispose();
            _volumeCasters = null;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void BindInEditor()
        {
            Bind();
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Release;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Release;
            UnityEditor.EditorApplication.quitting -= Release;
            UnityEditor.EditorApplication.quitting += Release;
        }
#endif
    }
}
