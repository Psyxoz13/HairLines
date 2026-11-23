using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HairLines
{
    internal static class HairShadowReceivers
    {
        private const int TileSize = 32;
        private const int TileStride = sizeof(float) * 6;

        private const int MaxAgeFrames = 4;

        private const float CutTranslation = 1f;
        private const float CutAngleOfFieldOfView = 0.5f;

        private const int PendingTimeoutFrames = 30;

        private const int ForgetAfterFrames = 300;

        private static readonly ProfilingSampler _sampler =
            new ProfilingSampler("HairLines.Rendering.GPU.ShadowReceivers");

        private static readonly int _depthId = Shader.PropertyToID("_HairReceiverDepth");
        private static readonly int _invViewProjId = Shader.PropertyToID("_HairReceiverInvViewProj");
        private static readonly int _cameraId = Shader.PropertyToID("_HairReceiverCamera");
        private static readonly int _sizeId = Shader.PropertyToID("_HairReceiverSize");
        private static readonly int _tilesId = Shader.PropertyToID("_HairReceiverTiles");

        internal sealed class CameraReceivers
        {
            internal GraphicsBuffer Buffer;
            internal int BufferTiles;

            internal float[] Tiles = Array.Empty<float>();
            internal int TileCount;
            internal bool Valid;
            internal int Frame;
            internal Vector3 Position;
            internal Vector3 Forward;

            internal bool Pending;
            internal int PendingTiles;
            internal int PendingFrame;
            internal Vector3 PendingPosition;
            internal Vector3 PendingForward;

            internal int LastRecordedFrame;
            internal readonly Action<AsyncGPUReadbackRequest> OnReadback;

            internal CameraReceivers()
            {
                OnReadback = Complete;
            }

            private void Complete(AsyncGPUReadbackRequest request)
            {
                Pending = false;
                if (request.hasError)
                {
                    Valid = false;
                    return;
                }

                NativeArray<float> data = request.GetData<float>();
                int count = PendingTiles * 6;
                if (data.Length < count)
                {
                    Valid = false;
                    return;
                }

                if (Tiles.Length < count)
                {
                    Tiles = new float[count];
                }

                NativeArray<float>.Copy(data, 0, Tiles, 0, count);
                TileCount = PendingTiles;
                Frame = PendingFrame;
                Position = PendingPosition;
                Forward = PendingForward;
                Valid = true;
            }

            internal void Release()
            {
                Buffer?.Release();
                Buffer = null;
                BufferTiles = 0;
                Valid = false;
                Pending = false;
            }
        }

        private class PassData
        {
            internal ComputeShader shader;
            internal int kernel;
            internal TextureHandle depth;
            internal CameraReceivers receivers;
            internal Matrix4x4 invViewProjection;
            internal Vector4 camera;
            internal Vector4 size;
            internal int tilesX;
            internal int tilesY;
        }

        private static readonly Dictionary<int, CameraReceivers> _cameras = new Dictionary<int, CameraReceivers>();
        private static readonly List<int> _forget = new List<int>();

        internal static void Record(RenderGraph renderGraph, UniversalCameraData cameraData,
                                    UniversalResourceData resourceData, ComputeShader shader)
        {
            Forget();

            Camera camera = cameraData.camera;
            if (shader == null || camera == null || cameraData.xr.enabled ||
                !SystemInfo.supportsAsyncGPUReadback || cameraData.maxShadowDistance <= 0f)
            {
                return;
            }

            TextureHandle depth = resourceData.cameraDepthTexture;
            if (!depth.IsValid())
            {
                return;
            }

            int kernel = shader.FindKernel("TileBounds");
            int width = Mathf.Max(1, cameraData.cameraTargetDescriptor.width);
            int height = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
            int tilesX = (width + TileSize - 1) / TileSize;
            int tilesY = (height + TileSize - 1) / TileSize;
            int tileCount = tilesX * tilesY;

            int id = camera.GetInstanceID();
            if (!_cameras.TryGetValue(id, out CameraReceivers receivers))
            {
                receivers = new CameraReceivers();
                _cameras.Add(id, receivers);
            }

            receivers.LastRecordedFrame = Time.frameCount;
            if (receivers.Pending && Time.frameCount - receivers.PendingFrame <= PendingTimeoutFrames)
            {
                return;
            }

            if (receivers.Buffer == null || receivers.BufferTiles != tileCount)
            {
                receivers.Buffer?.Release();
                receivers.Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, tileCount, TileStride)
                {
                    name = "HairLines Shadow Receivers",
                };
                receivers.BufferTiles = tileCount;
            }

            Transform cameraTransform = camera.transform;
            receivers.Pending = true;
            receivers.PendingTiles = tileCount;
            receivers.PendingFrame = Time.frameCount;
            receivers.PendingPosition = cameraTransform.position;
            receivers.PendingForward = cameraTransform.forward;

            using var builder = renderGraph.AddUnsafePass<PassData>("HairLines.ShadowReceivers", out var passData);
            passData.shader = shader;
            passData.kernel = kernel;
            passData.depth = depth;
            passData.receivers = receivers;
            Matrix4x4 viewProjection = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true) *
                                       cameraData.GetViewMatrix();
            passData.invViewProjection = viewProjection.inverse;
            Vector3 position = cameraTransform.position;
            passData.camera = new Vector4(position.x, position.y, position.z, cameraData.maxShadowDistance);
            passData.size = new Vector4(width, height, TileSize, tilesX);
            passData.tilesX = tilesX;
            passData.tilesY = tilesY;

            builder.UseTexture(depth, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((PassData data, UnsafeGraphContext ctx) =>
            {
                using (new ProfilingScope(ctx.cmd, _sampler))
                {
                    ctx.cmd.SetComputeTextureParam(data.shader, data.kernel, _depthId, data.depth);
                    ctx.cmd.SetComputeBufferParam(data.shader, data.kernel, _tilesId, data.receivers.Buffer);
                    ctx.cmd.SetComputeMatrixParam(data.shader, _invViewProjId, data.invViewProjection);
                    ctx.cmd.SetComputeVectorParam(data.shader, _cameraId, data.camera);
                    ctx.cmd.SetComputeVectorParam(data.shader, _sizeId, data.size);
                    ctx.cmd.DispatchCompute(data.shader, data.kernel,
                                            (data.tilesX + 7) / 8, (data.tilesY + 7) / 8, 1);
                    ctx.cmd.RequestAsyncReadback(data.receivers.Buffer,
                                                 data.tilesX * data.tilesY * TileStride, 0,
                                                 data.receivers.OnReadback);
                }
            });
        }

        internal static bool TryGet(Camera camera, out CameraReceivers receivers)
        {
            receivers = null;
            if (camera == null || !_cameras.TryGetValue(camera.GetInstanceID(), out CameraReceivers state) ||
                !state.Valid || Time.frameCount - state.Frame > MaxAgeFrames)
            {
                return false;
            }

            Transform cameraTransform = camera.transform;
            float cutAngle = camera.orthographic ? 30f : camera.fieldOfView * CutAngleOfFieldOfView;
            if (Vector3.Distance(cameraTransform.position, state.Position) > CutTranslation ||
                Vector3.Angle(cameraTransform.forward, state.Forward) > cutAngle)
            {
                return false;
            }

            receivers = state;
            return true;
        }

        internal static bool ReceivesShadow(CameraReceivers receivers, Bounds casterBounds, Vector3 lightDir,
                                            int shadowResolution)
        {
            CameraReceivers state = receivers;

            Vector3 up = Mathf.Abs(Vector3.Dot(lightDir, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(lightDir, up);
            Vector3 axisX = rotation * Vector3.right;
            Vector3 axisY = rotation * Vector3.up;
            Vector3 axisZ = rotation * Vector3.forward;

            Vector3 center = casterBounds.center;
            Vector3 extents = casterBounds.extents;
            float halfSize = Mathf.Max(Mathf.Max(Project(extents, axisX), Project(extents, axisY)), 1e-3f) * 1.05f;
            halfSize += 2f * halfSize / Mathf.Max(1, shadowResolution);

            float casterX = Vector3.Dot(center, axisX);
            float casterY = Vector3.Dot(center, axisY);
            float casterFront = Vector3.Dot(center, axisZ) - Project(extents, axisZ);

            float[] tiles = state.Tiles;
            for (int t = 0, i = 0; t < state.TileCount; t++, i += 6)
            {
                if (tiles[i] > tiles[i + 3])
                {
                    continue;
                }

                var tileCenter = new Vector3((tiles[i] + tiles[i + 3]) * 0.5f,
                                             (tiles[i + 1] + tiles[i + 4]) * 0.5f,
                                             (tiles[i + 2] + tiles[i + 5]) * 0.5f);
                var tileExtents = new Vector3((tiles[i + 3] - tiles[i]) * 0.5f,
                                              (tiles[i + 4] - tiles[i + 1]) * 0.5f,
                                              (tiles[i + 5] - tiles[i + 2]) * 0.5f);

                if (Mathf.Abs(Vector3.Dot(tileCenter, axisX) - casterX) > halfSize + Project(tileExtents, axisX) ||
                    Mathf.Abs(Vector3.Dot(tileCenter, axisY) - casterY) > halfSize + Project(tileExtents, axisY) ||
                    Vector3.Dot(tileCenter, axisZ) + Project(tileExtents, axisZ) <= casterFront)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        internal static void ReleaseAll()
        {
            foreach (CameraReceivers receivers in _cameras.Values)
            {
                receivers.Release();
            }
            _cameras.Clear();
        }

        private static void Forget()
        {
            int frame = Time.frameCount;
            _forget.Clear();
            foreach (KeyValuePair<int, CameraReceivers> entry in _cameras)
            {
                if (frame - entry.Value.LastRecordedFrame > ForgetAfterFrames)
                {
                    entry.Value.Release();
                    _forget.Add(entry.Key);
                }
            }
            for (int i = 0; i < _forget.Count; i++)
            {
                _cameras.Remove(_forget[i]);
            }
        }

        private static float Project(Vector3 extents, Vector3 axis)
        {
            return Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y + Mathf.Abs(axis.z) * extents.z;
        }
    }
}
