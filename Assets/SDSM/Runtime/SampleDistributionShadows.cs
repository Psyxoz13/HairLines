using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SDSM
{
    internal static class SampleDistributionShadows
    {
        private const int TileSize = 32;
        private const int TileStride = sizeof(float) * 4;

        private const int MaxAgeFrames = 4;
        private const int PendingTimeoutFrames = 30;
        private const int ForgetAfterFrames = 300;

        private static readonly float Step = Mathf.Pow(2f, 0.25f);
        private const int ShrinkAfterFrames = 30;

        private const float CutTranslation = 1f;
        private const float CutAngleOfFieldOfView = 0.5f;

        private static readonly ProfilingSampler _sampler = new ProfilingSampler("SDSM.DepthRange");
        private static readonly int _depthId = Shader.PropertyToID("_SDSMDepth");
        private static readonly int _invViewProjId = Shader.PropertyToID("_SDSMInvViewProj");
        private static readonly int _cameraId = Shader.PropertyToID("_SDSMCamera");
        private static readonly int _forwardId = Shader.PropertyToID("_SDSMForward");
        private static readonly int _sizeId = Shader.PropertyToID("_SDSMSize");
        private static readonly int _tilesId = Shader.PropertyToID("_SDSMTiles");

        private sealed class CameraRange
        {
            internal GraphicsBuffer Buffer;
            internal int BufferTiles;

            internal bool Valid;
            internal bool AnyReceiver;
            internal float Nearest;
            internal float FarthestDistance;
            internal int Frame;

            internal bool Pending;
            internal int PendingTiles;
            internal int PendingFrame;

            internal bool HasApplied;
            internal int DistanceSteps;
            internal int NearSteps;
            internal int DistanceShrinkFrames;
            internal int NearShrinkFrames;

            internal bool HasPose;
            internal Vector3 PosePosition;
            internal Vector3 PoseForward;
            internal int CutFrame = int.MinValue;

            internal int LastRecordedFrame;
            internal readonly Action<AsyncGPUReadbackRequest> OnReadback;

            internal CameraRange()
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
                if (data.Length < PendingTiles * 4)
                {
                    Valid = false;
                    return;
                }

                float nearest = float.MaxValue;
                float farthest = 0f;
                bool any = false;
                for (int i = 0; i < PendingTiles * 4; i += 4)
                {
                    if (data[i + 3] <= 0f)
                    {
                        continue;
                    }

                    any = true;
                    nearest = Mathf.Min(nearest, data[i]);
                    farthest = Mathf.Max(farthest, data[i + 2]);
                }

                AnyReceiver = any;
                Nearest = nearest;
                FarthestDistance = farthest;
                Frame = PendingFrame;
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
            internal CameraRange range;
            internal Matrix4x4 invViewProjection;
            internal Vector4 camera;
            internal Vector4 forward;
            internal Vector4 size;
            internal int tilesX;
            internal int tilesY;
        }

        private static readonly Dictionary<int, CameraRange> _cameras = new Dictionary<int, CameraRange>();
        private static readonly List<int> _forget = new List<int>();

        private static UniversalRenderPipelineAsset _appliedAsset;
        private static int _appliedCamera;
        private static float _savedDistance;
        private static float _savedSplit2;
        private static Vector2 _savedSplit3;
        private static Vector3 _savedSplit4;

        private static bool _subscribed;

        internal static void EnsureSubscribed()
        {
            if (_subscribed)
            {
                return;
            }

            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            _subscribed = true;
        }

        internal static void ReleaseAll()
        {
            Restore();
            foreach (CameraRange range in _cameras.Values)
            {
                range.Release();
            }
            _cameras.Clear();
        }

        internal static void Record(RenderGraph renderGraph, ContextContainer frameData, ComputeShader shader)
        {
            Forget();

            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            Camera camera = cameraData.camera;
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (shader == null || camera == null || asset == null || cameraData.xr.enabled)
            {
                return;
            }

            float reach = Mathf.Min(SavedDistance(asset), camera.farClipPlane);
            TextureHandle depth = resourceData.cameraDepthTexture;
            if (reach <= camera.nearClipPlane || !depth.IsValid())
            {
                return;
            }

            int id = camera.GetInstanceID();
            if (!_cameras.TryGetValue(id, out CameraRange range))
            {
                range = new CameraRange();
                _cameras.Add(id, range);
            }

            range.LastRecordedFrame = Time.frameCount;
            if (range.Pending && Time.frameCount - range.PendingFrame <= PendingTimeoutFrames)
            {
                return;
            }

            int width = Mathf.Max(1, cameraData.cameraTargetDescriptor.width);
            int height = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
            int tilesX = (width + TileSize - 1) / TileSize;
            int tilesY = (height + TileSize - 1) / TileSize;
            int tileCount = tilesX * tilesY;

            if (range.Buffer == null || range.BufferTiles != tileCount)
            {
                range.Buffer?.Release();
                range.Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, tileCount, TileStride)
                {
                    name = "SDSM Depth Range",
                };
                range.BufferTiles = tileCount;
            }

            Transform cameraTransform = camera.transform;
            range.Pending = true;
            range.PendingTiles = tileCount;
            range.PendingFrame = Time.frameCount;

            using var builder = renderGraph.AddUnsafePass<PassData>("SDSM.DepthRange", out var passData);
            passData.shader = shader;
            passData.kernel = shader.FindKernel("TileRange");
            passData.depth = depth;
            passData.range = range;
            passData.invViewProjection = (GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true) *
                                          cameraData.GetViewMatrix()).inverse;
            Vector3 position = cameraTransform.position;
            Vector3 forward = cameraTransform.forward;
            passData.camera = new Vector4(position.x, position.y, position.z, reach);
            passData.forward = new Vector4(forward.x, forward.y, forward.z, 0f);
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
                    ctx.cmd.SetComputeBufferParam(data.shader, data.kernel, _tilesId, data.range.Buffer);
                    ctx.cmd.SetComputeMatrixParam(data.shader, _invViewProjId, data.invViewProjection);
                    ctx.cmd.SetComputeVectorParam(data.shader, _cameraId, data.camera);
                    ctx.cmd.SetComputeVectorParam(data.shader, _forwardId, data.forward);
                    ctx.cmd.SetComputeVectorParam(data.shader, _sizeId, data.size);
                    ctx.cmd.DispatchCompute(data.shader, data.kernel, (data.tilesX + 7) / 8, (data.tilesY + 7) / 8, 1);
                    ctx.cmd.RequestAsyncReadback(data.range.Buffer, data.tilesX * data.tilesY * TileStride, 0,
                                                 data.range.OnReadback);
                }
            });
        }

        private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            Restore();

            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null || camera == null ||
                !_cameras.TryGetValue(camera.GetInstanceID(), out CameraRange range) ||
                !TryPartition(range, camera, asset, out float distance, out Vector3 splits))
            {
                return;
            }

            _appliedAsset = asset;
            _appliedCamera = camera.GetInstanceID();
            _savedDistance = asset.shadowDistance;
            _savedSplit2 = asset.cascade2Split;
            _savedSplit3 = asset.cascade3Split;
            _savedSplit4 = asset.cascade4Split;

            asset.shadowDistance = distance;
            asset.cascade2Split = splits.x;
            asset.cascade3Split = new Vector2(splits.x, splits.y);
            asset.cascade4Split = splits;
        }

        private static void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != null && _appliedAsset != null && _appliedCamera == camera.GetInstanceID())
            {
                Restore();
            }
        }

        private static void Restore()
        {
            if (_appliedAsset == null)
            {
                return;
            }

            _appliedAsset.shadowDistance = _savedDistance;
            _appliedAsset.cascade2Split = _savedSplit2;
            _appliedAsset.cascade3Split = _savedSplit3;
            _appliedAsset.cascade4Split = _savedSplit4;
            _appliedAsset = null;
        }

        private static float SavedDistance(UniversalRenderPipelineAsset asset)
        {
            return _appliedAsset == asset ? _savedDistance : asset.shadowDistance;
        }

        private static bool TryPartition(CameraRange range, Camera camera, UniversalRenderPipelineAsset asset,
                                         out float distance, out Vector3 splits)
        {
            distance = 0f;
            splits = Vector3.zero;

            float assetDistance = Mathf.Min(asset.shadowDistance, camera.farClipPlane);
            float near = Mathf.Max(camera.nearClipPlane, 1e-3f);

            Transform cameraTransform = camera.transform;
            float cutAngle = camera.orthographic ? 30f : camera.fieldOfView * CutAngleOfFieldOfView;
            if (range.HasPose &&
                (Vector3.Distance(cameraTransform.position, range.PosePosition) > CutTranslation ||
                 Vector3.Angle(cameraTransform.forward, range.PoseForward) > cutAngle))
            {
                range.CutFrame = Time.frameCount;
            }
            range.PosePosition = cameraTransform.position;
            range.PoseForward = cameraTransform.forward;
            range.HasPose = true;

            bool recording = Time.frameCount - range.LastRecordedFrame <= MaxAgeFrames;
            bool measuredSinceCut = range.Valid && range.Frame >= range.CutFrame;
            if (!recording || !measuredSinceCut || assetDistance <= near * Step)
            {
                range.HasApplied = false;
                return false;
            }

            if (!range.AnyReceiver)
            {
                if (!range.HasApplied)
                {
                    return false;
                }

                return Partition(range, asset, assetDistance, near, out distance, out splits);
            }

            float border = Mathf.Clamp(asset.cascadeBorder, 0f, 0.9f);
            float wantedDistance = Mathf.Clamp(range.FarthestDistance / (1f - border), near * Step, assetDistance);
            int distanceSteps = Mathf.Max(0, Mathf.FloorToInt(Mathf.Log(assetDistance / wantedDistance, Step) + 1e-4f));

            float wantedNear = Mathf.Clamp(range.Nearest, near, assetDistance);
            int nearSteps = Mathf.Max(0, Mathf.FloorToInt(Mathf.Log(wantedNear / near, Step) + 1e-4f));

            if (!range.HasApplied)
            {
                range.DistanceSteps = distanceSteps;
                range.NearSteps = nearSteps;
                range.DistanceShrinkFrames = 0;
                range.NearShrinkFrames = 0;
                range.HasApplied = true;
            }
            else
            {
                range.DistanceSteps = Hysteresis(range.DistanceSteps, distanceSteps, ref range.DistanceShrinkFrames);
                range.NearSteps = Hysteresis(range.NearSteps, nearSteps, ref range.NearShrinkFrames);
            }

            return Partition(range, asset, assetDistance, near, out distance, out splits);
        }

        private static bool Partition(CameraRange range, UniversalRenderPipelineAsset asset, float assetDistance,
                                      float near, out float distance, out Vector3 splits)
        {
            splits = Vector3.zero;
            float border = Mathf.Clamp(asset.cascadeBorder, 0f, 0.9f);
            distance = assetDistance / Mathf.Pow(Step, range.DistanceSteps);

            float end = distance * (1f - border);
            float start = Mathf.Min(near * Mathf.Pow(Step, range.NearSteps), end * 0.5f);
            int cascades = Mathf.Clamp(asset.shadowCascadeCount, 1, 4);
            float previous = 0f;
            for (int i = 1; i < 4; i++)
            {
                float split = 1f;
                if (i < cascades)
                {
                    split = start * Mathf.Pow(end / start, i / (float)cascades) / distance;
                    split = Mathf.Clamp(split, previous + 1e-3f, 1f - 1e-3f);
                }
                splits[i - 1] = split;
                previous = split;
            }

            return true;
        }

        private static int Hysteresis(int applied, int wanted, ref int frames)
        {
            if (wanted < applied)
            {
                frames = 0;
                return wanted;
            }

            if (wanted > applied + 1)
            {
                if (++frames >= ShrinkAfterFrames)
                {
                    frames = 0;
                    return wanted - 1;
                }
                return applied;
            }

            frames = 0;
            return applied;
        }

        private static void Forget()
        {
            int frame = Time.frameCount;
            _forget.Clear();
            foreach (KeyValuePair<int, CameraRange> entry in _cameras)
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
    }
}
