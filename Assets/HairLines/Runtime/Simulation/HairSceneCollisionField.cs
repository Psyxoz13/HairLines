using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace HairLines
{
    internal class HairSceneCollisionField : IDisposable
    {
        public bool IsValid => _isValid;
        public RenderTexture DistanceTexture => _distance;
        public Bounds Bounds => _bounds;
        public Vector3 CellSize => _bounds.size / Mathf.Max(1, _resolution);
        public int Resolution => _resolution;
        public int InstanceCount => _instanceCount;
        public long GpuBytes => _gpuBytes;

        private HairSceneFieldBuild _build;
        private RayTracingAccelerationStructure _accelerationStructure;
        private ComputeShader _shader;
        private RenderTexture _seedA;
        private RenderTexture _seedB;
        private RenderTexture _inside;
        private RenderTexture _distance;

        private GraphicsBuffer _splatA;
        private GraphicsBuffer _splatB;
        private GraphicsBuffer _jumpA;
        private GraphicsBuffer _jumpB;
        private readonly List<GraphicsBuffer> _borrowedBuffers = new List<GraphicsBuffer>();

        private Bounds _bounds;
        private float _padding = -1f;
        private int _buildSignature = int.MinValue;
        private int _resolution;
        private int _instanceCount;
        private int _updatedFrame = -1;
        private int _sceneSignature;
        private int _markSurfaceKernel;
        private int _jumpFloodKernel;
        private int _finalizeDistanceKernel;
        private int _initializeKernel;
        private int _splatTrianglesKernel;
        private int _unflipKernel;
        private int _linearFloodKernel;
        private int _linearFloodDiagonalKernel;
        private int _jumpFloodInitializeKernel;
        private int _jumpFloodDiagonalKernel;
        private int _jumpFloodFinalizeKernel;
        private int _writeTextureKernel;
        private long _gpuBytes;
        private bool _isValid;
        private CacheKey _cacheKey;
        private int _sharedReferences;
        private bool _isShared;

        private const int SafetyRefreshInterval = 30;
        private static Renderer[] _rendererScratch;
        private static RendererStamp[] _rendererStamps;
        private static int _rendererStampCount;
        private static int _rendererStampFrame = -1;

        private static readonly Dictionary<CacheKey, HairSceneCollisionField> _sharedFields =
            new Dictionary<CacheKey, HairSceneCollisionField>();

        private static readonly ProfilerMarker _updateMarker =
            new ProfilerMarker("HairLines.SceneCollision.Update");
        private static readonly ProfilerMarker _populateAccelerationMarker =
            new ProfilerMarker("HairLines.SceneCollision.BuildAccelerationStructure");
        private static readonly ProfilerMarker _distanceFieldMarker =
            new ProfilerMarker("HairLines.SceneCollision.BuildDistanceField");
        private static readonly ProfilingSampler _rtasBuildSampler =
            new ProfilingSampler("HairLines.SceneCollision.RTASBuild");
        private static readonly ProfilingSampler _markSurfaceSampler =
            new ProfilingSampler("HairLines.SceneCollision.MarkSurface");
        private static readonly ProfilingSampler _jumpFloodSampler =
            new ProfilingSampler("HairLines.SceneCollision.JumpFlood");
        private static readonly ProfilingSampler _finalizeDistanceSampler =
            new ProfilingSampler("HairLines.SceneCollision.FinalizeDistance");
        private static readonly ProfilingSampler _splatSampler =
            new ProfilingSampler("HairLines.SceneCollision.Splat");
        private static readonly ProfilingSampler _splatFloodSampler =
            new ProfilingSampler("HairLines.SceneCollision.SplatFlood");

        private const uint CollisionInstanceMask = 1u;
        private const int SplatThreads = 64;
        private const int MaxTrianglesPerDispatch = 65535 * SplatThreads;

        private static readonly int[] _jumpAxisOffset = new int[3];
        private static readonly HashSet<int> _splatWarnings = new HashSet<int>();

        internal static HairSceneCollisionField Acquire(HairSceneCollisionField current,
            ComputeShader shaderSource, HairSceneFieldBuild build, LayerMask layerMask, int resolution,
            float padding, int ownerId)
        {
            resolution = Mathf.Clamp(resolution, 16, 128);
            padding = Mathf.Max(0f, padding);
            CacheKey key = new CacheKey(shaderSource, build, layerMask, resolution, padding, ownerId);
            if (current != null && current._isShared && current._cacheKey.Equals(key))
            {
                return current;
            }

            current?.ReleaseShared();
            if (_sharedFields.TryGetValue(key, out HairSceneCollisionField shared) && shared != null)
            {
                shared._sharedReferences++;
                return shared;
            }

            shared = new HairSceneCollisionField
            {
                _build = build,
                _cacheKey = key,
                _sharedReferences = 1,
                _isShared = true,
            };
            _sharedFields[key] = shared;
            return shared;
        }

        public static bool IsSupported(HairSceneFieldBuild build)
        {
            if (build == HairSceneFieldBuild.TriangleSplat)
            {
                return SystemInfo.supportsComputeShaders &&
                       SystemInfo.supports3DRenderTextures &&
                       SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RHalf);
            }

            return SystemInfo.supportsInlineRayTracing &&
                   SystemInfo.supports3DRenderTextures &&
                   SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBFloat) &&
                   SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat);
        }

        public bool Update(ComputeShader shader, Bounds simulationBounds, LayerMask layerMask,
            int resolution, float padding, HairSceneFieldFlood flood, int floodIterations, bool diagonalFlood)
        {
            using ProfilerMarker.AutoScope scope = _updateMarker.Auto();
            if (!IsSupported(_build) || shader == null || simulationBounds.size.sqrMagnitude <= 1e-10f)
            {
                return false;
            }

            _isValid = false;

            resolution = Mathf.Clamp(resolution, 16, 128);
            padding = Mathf.Max(0f, padding);
            if (!Mathf.Approximately(_padding, padding))
            {
                _bounds = default;
                _updatedFrame = -1;
                _padding = padding;
            }

            floodIterations = Mathf.Clamp(floodIterations, 0, 64);
            int buildSignature = _build == HairSceneFieldBuild.TriangleSplat
                ? ((int)flood * 131 + floodIterations) * 2 + (diagonalFlood ? 1 : 0)
                : 0;
            if (buildSignature != _buildSignature)
            {
                _buildSignature = buildSignature;
                _updatedFrame = -1;
            }

            Bounds requestedBounds = ExpandBounds(simulationBounds, padding);
            if (_updatedFrame == Time.frameCount && _bounds.size.sqrMagnitude > 1e-10f &&
                Contains(_bounds, requestedBounds))
            {
                _isValid = true;
                return true;
            }
            if (_sharedReferences <= 1 && _bounds.size.sqrMagnitude > 1e-10f &&
                !Contains(_bounds, requestedBounds))
            {
                _bounds = default;
                _updatedFrame = -1;
            }

            bool containsRequestedBounds = _bounds.size.sqrMagnitude > 1e-10f &&
                                           Contains(_bounds, requestedBounds);
            Bounds fieldBounds = containsRequestedBounds
                ? _bounds
                : StabilizeBounds(UnionBounds(_bounds, requestedBounds), resolution);
            EnsureShader(shader);
            if (_build == HairSceneFieldBuild.TriangleSplat)
            {
                EnsureSplatResources(resolution, flood);
            }
            else
            {
                EnsureAccelerationStructure();
                EnsureTextures(resolution);
            }

            int sceneSignature = ComputeSceneSignature(fieldBounds, layerMask);
            bool sceneChanged = _updatedFrame < 0 || sceneSignature != _sceneSignature;
            bool safetyRefresh = _updatedFrame < 0 ||
                                 Time.frameCount < _updatedFrame ||
                                 Time.frameCount - _updatedFrame >= SafetyRefreshInterval;
            if (sceneChanged || safetyRefresh || !containsRequestedBounds)
            {
                CommandBuffer cmd = CommandBufferPool.Get();
                try
                {
                    if (_build == HairSceneFieldBuild.TriangleSplat)
                    {
                        _instanceCount = BuildSplatField(fieldBounds, layerMask, flood, floodIterations,
                            diagonalFlood, cmd);
                        Graphics.ExecuteCommandBuffer(cmd);
                        _gpuBytes = SplatBytes(_resolution, flood);
                    }
                    else
                    {
                        PopulateAccelerationStructure(fieldBounds, layerMask, cmd);
                        BuildDistanceField(fieldBounds, cmd);
                        Graphics.ExecuteCommandBuffer(cmd);
                        _instanceCount = (int)_accelerationStructure.GetInstanceCount();
                        _gpuBytes = (long)_accelerationStructure.GetSize() + TextureBytes(_resolution);
                    }
                }
                finally
                {
                    CommandBufferPool.Release(cmd);
                    ReleaseBorrowedBuffers();
                }
                _updatedFrame = Time.frameCount;
                _sceneSignature = sceneSignature;
            }

            _bounds = fieldBounds;
            _isValid = true;
            return true;
        }

        public void Dispose()
        {
            ReleaseShared();
        }

        private void ReleaseShared()
        {
            if (_isShared)
            {
                _sharedReferences = Mathf.Max(0, _sharedReferences - 1);
                if (_sharedReferences > 0)
                {
                    return;
                }

                _sharedFields.Remove(_cacheKey);
                _isShared = false;
            }

            DisposeResources();
        }

        private void DisposeResources()
        {
            _isValid = false;
            _updatedFrame = -1;
            _sceneSignature = 0;
            _padding = -1f;
            _buildSignature = int.MinValue;
            _instanceCount = 0;
            _gpuBytes = 0;

            _accelerationStructure?.Dispose();
            _accelerationStructure = null;

            DestroyShader(ref _shader);

            ReleaseTexture(ref _seedA);
            ReleaseTexture(ref _seedB);
            ReleaseTexture(ref _inside);
            ReleaseTexture(ref _distance);
            ReleaseBuffer(ref _splatA);
            ReleaseBuffer(ref _splatB);
            ReleaseBuffer(ref _jumpA);
            ReleaseBuffer(ref _jumpB);
            ReleaseBorrowedBuffers();
        }

        private void EnsureAccelerationStructure()
        {
            if (_accelerationStructure != null)
            {
                return;
            }

            RayTracingAccelerationStructure.Settings settings = new RayTracingAccelerationStructure.Settings(
                RayTracingAccelerationStructure.ManagementMode.Manual,
                RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                ~0)
            {
                buildFlagsStaticGeometries = RayTracingAccelerationStructureBuildFlags.PreferFastTrace,
                buildFlagsDynamicGeometries = RayTracingAccelerationStructureBuildFlags.PreferFastBuild,
                enableCompaction = false,
            };
            _accelerationStructure = new RayTracingAccelerationStructure(settings);
        }

        private void EnsureShader(ComputeShader shader)
        {
            if (_shader != null)
            {
                return;
            }

            DestroyShader(ref _shader);
            _shader = UnityEngine.Object.Instantiate(shader);
            _shader.name = $"{shader.name} (Scene Collision Shared Instance)";
            _shader.hideFlags = HideFlags.HideAndDontSave;
            _jumpFloodKernel = _shader.FindKernel("JumpFlood");
            if (_build == HairSceneFieldBuild.TriangleSplat)
            {
                _initializeKernel = _shader.FindKernel("Initialize");
                _splatTrianglesKernel = _shader.FindKernel("SplatTriangles");
                _unflipKernel = _shader.FindKernel("Unflip");
                _linearFloodKernel = _shader.FindKernel("LinearFlood");
                _linearFloodDiagonalKernel = _shader.FindKernel("LinearFloodDiagonal");
                _jumpFloodInitializeKernel = _shader.FindKernel("JumpFloodInitialize");
                _jumpFloodDiagonalKernel = _shader.FindKernel("JumpFloodDiagonal");
                _jumpFloodFinalizeKernel = _shader.FindKernel("JumpFloodFinalize");
                _writeTextureKernel = _shader.FindKernel("WriteTexture");
            }
            else
            {
                _markSurfaceKernel = _shader.FindKernel("MarkSurface");
                _finalizeDistanceKernel = _shader.FindKernel("FinalizeDistance");
            }
        }

        private void EnsureTextures(int resolution)
        {
            if (_resolution == resolution && _seedA != null && _seedB != null &&
                _inside != null && _distance != null)
            {
                return;
            }

            ReleaseTexture(ref _seedA);
            ReleaseTexture(ref _seedB);
            ReleaseTexture(ref _inside);
            ReleaseTexture(ref _distance);

            _resolution = resolution;
            _seedA = CreateTexture("Hair Scene Collision Seeds A", resolution, RenderTextureFormat.ARGBFloat,
                FilterMode.Point);
            _seedB = CreateTexture("Hair Scene Collision Seeds B", resolution, RenderTextureFormat.ARGBFloat,
                FilterMode.Point);
            _inside = CreateTexture("Hair Scene Collision Inside", resolution, RenderTextureFormat.RFloat,
                FilterMode.Point);
            _distance = CreateTexture("Hair Scene Collision Distance", resolution, RenderTextureFormat.RFloat,
                FilterMode.Bilinear);
            _updatedFrame = -1;
        }

        private void EnsureSplatResources(int resolution, HairSceneFieldFlood flood)
        {
            bool jump = flood == HairSceneFieldFlood.Jump;
            if (_resolution == resolution && _splatA != null && _splatB != null && _distance != null &&
                (!jump || (_jumpA != null && _jumpB != null)))
            {
                if (!jump)
                {
                    ReleaseBuffer(ref _jumpA);
                    ReleaseBuffer(ref _jumpB);
                }
                return;
            }

            ReleaseBuffer(ref _splatA);
            ReleaseBuffer(ref _splatB);
            ReleaseBuffer(ref _jumpA);
            ReleaseBuffer(ref _jumpB);
            ReleaseTexture(ref _distance);

            _resolution = resolution;
            int voxels = resolution * resolution * resolution;
            _splatA = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxels, sizeof(float));
            _splatB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxels, sizeof(float));
            if (jump)
            {
                _jumpA = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxels, sizeof(int));
                _jumpB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxels, sizeof(int));
            }

            _distance = CreateTexture("Hair Scene Collision Distance", resolution, RenderTextureFormat.RHalf,
                FilterMode.Bilinear);
            _updatedFrame = -1;
        }

        private int BuildSplatField(Bounds fieldBounds, LayerMask layerMask, HairSceneFieldFlood flood,
            int floodIterations, bool diagonalFlood, CommandBuffer cmd)
        {
            using ProfilerMarker.AutoScope scope = _distanceFieldMarker.Auto();
            int voxels = _resolution * _resolution * _resolution;
            int voxelGroups = CeilDiv(voxels, SplatThreads);
            Vector3 cellSize = fieldBounds.size / _resolution;
            float maximumDistance = fieldBounds.size.magnitude;

            cmd.SetComputeIntParam(_shader, Ids.Resolution, _resolution);
            cmd.SetComputeIntParam(_shader, Ids.VoxelCount, voxels);
            cmd.SetComputeVectorParam(_shader, Ids.FieldMinimum, fieldBounds.min);
            cmd.SetComputeVectorParam(_shader, Ids.CellSize, cellSize);
            cmd.SetComputeFloatParam(_shader, Ids.MaxDistance, maximumDistance);
            cmd.SetComputeFloatParam(_shader, Ids.InitialDistance, maximumDistance * 1.01f);

            GraphicsBuffer field = _splatA;
            GraphicsBuffer spare = _splatB;
            int splatted = 0;
            using (new ProfilingScope(cmd, _splatSampler))
            {
                cmd.SetComputeBufferParam(_shader, _initializeKernel, Ids.SplatField, field);
                cmd.DispatchCompute(_shader, _initializeKernel, voxelGroups, 1, 1);

                for (int i = 0; i < _rendererStampCount; i++)
                {
                    RendererStamp stamp = _rendererStamps[i];
                    if ((layerMask.value & (1 << stamp.layer)) == 0 || !stamp.bounds.Intersects(fieldBounds))
                    {
                        continue;
                    }

                    if (SplatRenderer(stamp.renderer, field, cmd))
                    {
                        splatted++;
                    }
                }

                cmd.SetComputeBufferParam(_shader, _unflipKernel, Ids.SplatField, field);
                cmd.DispatchCompute(_shader, _unflipKernel, voxelGroups, 1, 1);
            }

            using (new ProfilingScope(cmd, _splatFloodSampler))
            {
                if (flood == HairSceneFieldFlood.Linear)
                {
                    int kernel = diagonalFlood ? _linearFloodDiagonalKernel : _linearFloodKernel;
                    for (int i = 0; i < floodIterations; i++)
                    {
                        cmd.SetComputeBufferParam(_shader, kernel, Ids.FieldRead, field);
                        cmd.SetComputeBufferParam(_shader, kernel, Ids.FieldWrite, spare);
                        cmd.DispatchCompute(_shader, kernel, voxelGroups, 1, 1);
                        (field, spare) = (spare, field);
                    }
                }
                else
                {
                    cmd.SetComputeBufferParam(_shader, _jumpFloodInitializeKernel, Ids.FieldRead, field);
                    cmd.SetComputeBufferParam(_shader, _jumpFloodInitializeKernel, Ids.JumpWrite, _jumpA);
                    cmd.DispatchCompute(_shader, _jumpFloodInitializeKernel, voxelGroups, 1, 1);

                    GraphicsBuffer read = _jumpA;
                    GraphicsBuffer write = _jumpB;
                    for (int jump = HighestPowerOfTwoAtMost(_resolution / 2); jump >= 1; jump >>= 1)
                    {
                        if (diagonalFlood)
                        {
                            cmd.SetComputeIntParam(_shader, Ids.JumpOffset, jump);
                            DispatchJump(cmd, _jumpFloodDiagonalKernel, voxelGroups, read, write);
                            (read, write) = (write, read);
                            continue;
                        }

                        for (int axis = 0; axis < 3; axis++)
                        {
                            _jumpAxisOffset[0] = axis == 0 ? jump : 0;
                            _jumpAxisOffset[1] = axis == 1 ? jump : 0;
                            _jumpAxisOffset[2] = axis == 2 ? jump : 0;
                            cmd.SetComputeIntParams(_shader, Ids.JumpAxisOffset, _jumpAxisOffset);
                            DispatchJump(cmd, _jumpFloodKernel, voxelGroups, read, write);
                            (read, write) = (write, read);
                        }
                    }

                    cmd.SetComputeBufferParam(_shader, _jumpFloodFinalizeKernel, Ids.JumpRead, read);
                    cmd.SetComputeBufferParam(_shader, _jumpFloodFinalizeKernel, Ids.FieldRead, field);
                    cmd.SetComputeBufferParam(_shader, _jumpFloodFinalizeKernel, Ids.FieldWrite, spare);
                    cmd.DispatchCompute(_shader, _jumpFloodFinalizeKernel, voxelGroups, 1, 1);
                    (field, spare) = (spare, field);
                }

                float minimumCell = Mathf.Min(cellSize.x, Mathf.Min(cellSize.y, cellSize.z));
                float reach = (floodIterations + 1) * minimumCell * (diagonalFlood ? 1f : 1f / Mathf.Sqrt(3f));
                cmd.SetComputeFloatParam(_shader, Ids.UnreachedDistance,
                    Mathf.Max(minimumCell, reach - cellSize.magnitude));
                cmd.SetComputeBufferParam(_shader, _writeTextureKernel, Ids.FieldRead, field);
                cmd.SetComputeTextureParam(_shader, _writeTextureKernel, Ids.Distance, _distance);
                cmd.DispatchCompute(_shader, _writeTextureKernel, voxelGroups, 1, 1);
            }

            return splatted;
        }

        private void DispatchJump(CommandBuffer cmd, int kernel, int groups, GraphicsBuffer read,
            GraphicsBuffer write)
        {
            cmd.SetComputeBufferParam(_shader, kernel, Ids.JumpRead, read);
            cmd.SetComputeBufferParam(_shader, kernel, Ids.JumpWrite, write);
            cmd.DispatchCompute(_shader, kernel, groups, 1, 1);
        }

        private bool SplatRenderer(Renderer renderer, GraphicsBuffer field, CommandBuffer cmd)
        {
            if (renderer == null)
            {
                return false;
            }

            Mesh mesh;
            Matrix4x4 meshToWorld;
            int firstSubMesh = 0;
            int subMeshCount;
            GraphicsBuffer vertices;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                mesh = skinned.sharedMesh;
                if (!CanSplat(mesh) || !RequestRawIndices(mesh, renderer))
                {
                    return false;
                }

                if ((skinned.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
                {
                    skinned.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                }

                vertices = skinned.GetVertexBuffer();
                Transform root = skinned.rootBone != null ? skinned.rootBone : skinned.transform;
                meshToWorld = root.localToWorldMatrix * Matrix4x4.Scale(root.lossyScale).inverse;
                subMeshCount = mesh.subMeshCount;
            }
            else if (renderer is MeshRenderer meshRenderer && renderer.TryGetComponent(out MeshFilter filter))
            {
                mesh = filter.sharedMesh;
                if (!CanSplat(mesh) || !RequestRawIndices(mesh, renderer))
                {
                    return false;
                }

                if ((mesh.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
                {
                    if (!mesh.isReadable)
                    {
                        WarnSplat(mesh, renderer);
                        return false;
                    }

                    mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                }

                vertices = mesh.GetVertexBuffer(mesh.GetVertexAttributeStream(VertexAttribute.Position));
                if (renderer.isPartOfStaticBatch)
                {
                    meshToWorld = Matrix4x4.identity;
                    firstSubMesh = meshRenderer.subMeshStartIndex;
                    subMeshCount = renderer.sharedMaterials.Length;
                }
                else
                {
                    meshToWorld = renderer.localToWorldMatrix;
                    subMeshCount = mesh.subMeshCount;
                }
            }
            else
            {
                return false;
            }

            if (vertices == null)
            {
                return false;
            }

            _borrowedBuffers.Add(vertices);
            GraphicsBuffer indices = mesh.GetIndexBuffer();
            if (indices == null)
            {
                return false;
            }

            _borrowedBuffers.Add(indices);

            int kernel = _splatTrianglesKernel;
            cmd.SetComputeBufferParam(_shader, kernel, Ids.SplatField, field);
            cmd.SetComputeBufferParam(_shader, kernel, Ids.VertexBuffer, vertices);
            cmd.SetComputeBufferParam(_shader, kernel, Ids.IndexBuffer, indices);
            cmd.SetComputeIntParam(_shader, Ids.IndexFormat16, mesh.indexFormat == IndexFormat.UInt16 ? 1 : 0);
            cmd.SetComputeIntParam(_shader, Ids.VertexStride,
                mesh.GetVertexBufferStride(mesh.GetVertexAttributeStream(VertexAttribute.Position)));
            cmd.SetComputeIntParam(_shader, Ids.PositionOffset, mesh.GetVertexAttributeOffset(VertexAttribute.Position));
            cmd.SetComputeMatrixParam(_shader, Ids.MeshToWorld, meshToWorld);

            Material[] materials = renderer.sharedMaterials;
            bool splatted = false;
            for (int i = 0; i < subMeshCount; i++)
            {
                int subMesh = firstSubMesh + i;
                if (subMesh >= mesh.subMeshCount)
                {
                    break;
                }

                Material material = materials.Length > 0 ? materials[Mathf.Min(i, materials.Length - 1)] : null;
                SubMeshDescriptor descriptor = mesh.GetSubMesh(subMesh);
                if (material == null || material.renderQueue >= (int)RenderQueue.AlphaTest ||
                    descriptor.topology != MeshTopology.Triangles)
                {
                    continue;
                }

                cmd.SetComputeIntParam(_shader, Ids.IndexStart, descriptor.indexStart);
                cmd.SetComputeIntParam(_shader, Ids.BaseVertex, descriptor.baseVertex);
                int triangles = descriptor.indexCount / 3;
                for (int offset = 0; offset < triangles; offset += MaxTrianglesPerDispatch)
                {
                    int count = Mathf.Min(MaxTrianglesPerDispatch, triangles - offset);
                    cmd.SetComputeIntParam(_shader, Ids.TriangleOffset, offset);
                    cmd.SetComputeIntParam(_shader, Ids.TriangleCount, count);
                    cmd.DispatchCompute(_shader, kernel, CeilDiv(count, SplatThreads), 1, 1);
                    splatted = true;
                }
            }

            return splatted;
        }

        private static bool CanSplat(Mesh mesh)
        {
            return mesh != null &&
                   mesh.HasVertexAttribute(VertexAttribute.Position) &&
                   mesh.GetVertexAttributeFormat(VertexAttribute.Position) == VertexAttributeFormat.Float32 &&
                   mesh.GetVertexAttributeDimension(VertexAttribute.Position) >= 3;
        }

        private static bool RequestRawIndices(Mesh mesh, Renderer renderer)
        {
            if ((mesh.indexBufferTarget & GraphicsBuffer.Target.Raw) != 0)
            {
                return true;
            }

            if (!mesh.isReadable)
            {
                WarnSplat(mesh, renderer);
                return false;
            }

            mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            return true;
        }

        private static void WarnSplat(Mesh mesh, Renderer renderer)
        {
            if (_splatWarnings.Add(mesh.GetInstanceID()))
            {
                Debug.LogWarning($"[HairLines] '{mesh.name}' on '{renderer.name}' is not readable, so the " +
                                 "triangle splat cannot read its buffers from a compute shader. Enable " +
                                 "Read/Write in its import settings to collide with it.", renderer);
            }
        }

        private void ReleaseBorrowedBuffers()
        {
            foreach (GraphicsBuffer buffer in _borrowedBuffers)
            {
                buffer?.Dispose();
            }

            _borrowedBuffers.Clear();
        }

        private void PopulateAccelerationStructure(Bounds fieldBounds, LayerMask layerMask, CommandBuffer cmd)
        {
            using ProfilerMarker.AutoScope scope = _populateAccelerationMarker.Auto();
            _accelerationStructure.ClearInstances();
            RayTracingInstanceCullingTest instanceTest = new RayTracingInstanceCullingTest
            {
                instanceMask = CollisionInstanceMask,
                layerMask = layerMask.value,
                shadowCastingModeMask = ~0,
                allowOpaqueMaterials = true,
                allowTransparentMaterials = false,
                allowAlphaTestedMaterials = false,
                allowVisualEffects = false,
            };
            RayTracingInstanceCullingConfig cullingConfig = new RayTracingInstanceCullingConfig
            {
                flags = RayTracingInstanceCullingFlags.EnableSphereCulling |
                        RayTracingInstanceCullingFlags.IgnoreReflectionProbes,
                sphereCenter = fieldBounds.center,
                sphereRadius = fieldBounds.extents.magnitude,
                instanceTests = new[] { instanceTest },
            };
            cullingConfig.subMeshFlagsConfig.opaqueMaterials =
                RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
            cullingConfig.subMeshFlagsConfig.transparentMaterials = RayTracingSubMeshFlags.Disabled;
            cullingConfig.subMeshFlagsConfig.alphaTestedMaterials = RayTracingSubMeshFlags.Disabled;
            cullingConfig.triangleCullingConfig.forceDoubleSided = true;

            _accelerationStructure.CullInstances(ref cullingConfig);

            using (new ProfilingScope(cmd, _rtasBuildSampler))
            {
                cmd.BuildRayTracingAccelerationStructure(_accelerationStructure);
            }
        }

        private void BuildDistanceField(Bounds fieldBounds, CommandBuffer cmd)
        {
            using ProfilerMarker.AutoScope scope = _distanceFieldMarker.Auto();
            Vector3 cellSize = fieldBounds.size / _resolution;
            float maximumTraceDistance = fieldBounds.size.magnitude * 2f;
            int groups = Mathf.CeilToInt(_resolution / 4f);

            cmd.SetComputeIntParam(_shader, Ids.Resolution, _resolution);
            cmd.SetComputeVectorParam(_shader, Ids.FieldMinimum, fieldBounds.min);
            cmd.SetComputeVectorParam(_shader, Ids.FieldSize, fieldBounds.size);
            cmd.SetComputeVectorParam(_shader, Ids.CellSize, cellSize);
            cmd.SetComputeFloatParam(_shader, Ids.MaximumTraceDistance, maximumTraceDistance);
            cmd.SetRayTracingAccelerationStructure(_shader, _markSurfaceKernel, Ids.AccelerationStructure,
                _accelerationStructure);
            cmd.SetComputeTextureParam(_shader, _markSurfaceKernel, Ids.SeedWrite, _seedA);
            cmd.SetComputeTextureParam(_shader, _markSurfaceKernel, Ids.Inside, _inside);
            using (new ProfilingScope(cmd, _markSurfaceSampler))
            {
                cmd.DispatchCompute(_shader, _markSurfaceKernel, groups, groups, groups);
            }

            RenderTexture read = _seedA;
            RenderTexture write = _seedB;
            int jump = HighestPowerOfTwoAtMost(_resolution / 2);
            using (new ProfilingScope(cmd, _jumpFloodSampler))
            {
                while (jump >= 1)
                {
                    cmd.SetComputeIntParam(_shader, Ids.Jump, jump);
                    cmd.SetComputeTextureParam(_shader, _jumpFloodKernel, Ids.SeedRead, read);
                    cmd.SetComputeTextureParam(_shader, _jumpFloodKernel, Ids.SeedWrite, write);
                    cmd.DispatchCompute(_shader, _jumpFloodKernel, groups, groups, groups);
                    (read, write) = (write, read);
                    jump >>= 1;
                }
            }

            cmd.SetComputeTextureParam(_shader, _finalizeDistanceKernel, Ids.SeedRead, read);
            cmd.SetComputeTextureParam(_shader, _finalizeDistanceKernel, Ids.Inside, _inside);
            cmd.SetComputeTextureParam(_shader, _finalizeDistanceKernel, Ids.Distance, _distance);
            using (new ProfilingScope(cmd, _finalizeDistanceSampler))
            {
                cmd.DispatchCompute(_shader, _finalizeDistanceKernel, groups, groups, groups);
            }
        }

        private static int ComputeSceneSignature(Bounds fieldBounds, LayerMask layerMask)
        {
            RefreshRendererStamps();

            unchecked
            {
                int hash = 17;
                int count = 0;
                for (int i = 0; i < _rendererStampCount; i++)
                {
                    RendererStamp stamp = _rendererStamps[i];
                    if ((layerMask.value & (1 << stamp.layer)) == 0 ||
                        !stamp.bounds.Intersects(fieldBounds))
                    {
                        continue;
                    }

                    hash ^= stamp.hash;
                    hash += stamp.hash * 397;
                    count++;
                }
                return hash * 31 + count;
            }
        }

        private static void RefreshRendererStamps()
        {
            int frame = Time.frameCount;
            if (_rendererStampFrame == frame)
            {
                return;
            }

            _rendererStampFrame = frame;
            try
            {
                _rendererScratch = UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            }
            catch
            {
                _rendererScratch = Array.Empty<Renderer>();
            }

            int required = _rendererScratch != null ? _rendererScratch.Length : 0;
            if (_rendererStamps == null || _rendererStamps.Length < required)
            {
                _rendererStamps = new RendererStamp[Mathf.Max(required, 16)];
            }

            _rendererStampCount = 0;
            for (int i = 0; i < required; i++)
            {
                Renderer renderer = _rendererScratch[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (renderer.GetComponent<LinePrimitiveRenderer>() != null)
                {
                    continue;
                }

                try
                {
                    Bounds bounds = renderer.bounds;
                    int layer = renderer.gameObject.layer;
                    int hash = HashRenderer(renderer, bounds);
                    _rendererStamps[_rendererStampCount++] = new RendererStamp(renderer, layer, bounds, hash);
                }
                catch (MissingReferenceException)
                {
                }
            }
        }

        private static int HashRenderer(Renderer renderer, Bounds bounds)
        {
            unchecked
            {
                int hash = renderer.GetInstanceID();
                hash = hash * 31 + renderer.GetType().GetHashCode();
                hash = hash * 31 + BitConverter.SingleToInt32Bits(bounds.center.x);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(bounds.center.y);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(bounds.center.z);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(bounds.size.x);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(bounds.size.y);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(bounds.size.z);

                Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
                hash = hash * 31 + BitConverter.SingleToInt32Bits(matrix.m00);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(matrix.m01);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(matrix.m02);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(matrix.m03);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(matrix.m10);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(matrix.m11);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(matrix.m12);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(matrix.m13);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(matrix.m20);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(matrix.m21);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(matrix.m22);
                hash = hash * 31 + BitConverter.SingleToInt32Bits(matrix.m23);

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh :
                    (renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : null);
                hash = hash * 31 + (mesh != null ? mesh.GetInstanceID() : 0);
                return hash;
            }
        }

        private Bounds StabilizeBounds(Bounds requestedBounds, int resolution)
        {
            if (_bounds.size.sqrMagnitude > 1e-10f && Contains(_bounds, requestedBounds))
            {
                return _bounds;
            }

            Vector3 size = requestedBounds.size;
            size.x = Mathf.Max(size.x, 0.01f);
            size.y = Mathf.Max(size.y, 0.01f);
            size.z = Mathf.Max(size.z, 0.01f);
            Vector3 cellSize = size / Mathf.Max(1, resolution - 2);
            Vector3 center = requestedBounds.center;
            center.x = Mathf.Round(center.x / cellSize.x) * cellSize.x;
            center.y = Mathf.Round(center.y / cellSize.y) * cellSize.y;
            center.z = Mathf.Round(center.z / cellSize.z) * cellSize.z;
            return new Bounds(center, size + cellSize * 2f);
        }

        private static Bounds ExpandBounds(Bounds bounds, float padding)
        {
            bounds.Expand(Mathf.Max(0f, padding) * 2f);
            return bounds;
        }

        private static Bounds UnionBounds(Bounds current, Bounds requested)
        {
            if (current.size.sqrMagnitude <= 1e-10f)
            {
                return requested;
            }

            current.Encapsulate(requested.min);
            current.Encapsulate(requested.max);
            return current;
        }

        private static bool Contains(Bounds outer, Bounds inner)
        {
            return outer.Contains(inner.min) && outer.Contains(inner.max);
        }

        private static int HighestPowerOfTwoAtMost(int value)
        {
            int result = 1;
            while (result <= value / 2)
            {
                result <<= 1;
            }
            return result;
        }

        private static RenderTexture CreateTexture(string name, int resolution, RenderTextureFormat format,
            FilterMode filterMode)
        {
            RenderTexture texture = new RenderTexture(resolution, resolution, 0, format)
            {
                name = name,
                dimension = TextureDimension.Tex3D,
                volumeDepth = resolution,
                enableRandomWrite = true,
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.Create();
            return texture;
        }

        private static long TextureBytes(int resolution)
        {
            long voxels = (long)resolution * resolution * resolution;
            return voxels * (sizeof(float) * 4 * 2 + sizeof(float) * 2);
        }

        private static long SplatBytes(int resolution, HairSceneFieldFlood flood)
        {
            long voxels = (long)resolution * resolution * resolution;
            int seeds = flood == HairSceneFieldFlood.Jump ? sizeof(int) * 2 : 0;
            return voxels * (sizeof(float) * 2 + seeds + sizeof(ushort));
        }

        private static int CeilDiv(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }

        private static void ReleaseBuffer(ref GraphicsBuffer buffer)
        {
            buffer?.Dispose();
            buffer = null;
        }

        private static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
            texture = null;
        }

        private static void DestroyShader(ref ComputeShader shader)
        {
            if (shader == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(shader);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(shader);
            }
            shader = null;
        }

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            private readonly int _shaderId;
            private readonly HairSceneFieldBuild _build;
            private readonly int _layerMask;
            private readonly int _resolution;
            private readonly int _paddingBits;
            private readonly int _ownerId;

            internal CacheKey(ComputeShader shader, HairSceneFieldBuild build, LayerMask layerMask,
                int resolution, float padding, int ownerId)
            {
                _shaderId = shader != null ? shader.GetInstanceID() : 0;
                _build = build;
                _layerMask = layerMask.value;
                _resolution = resolution;
                _paddingBits = BitConverter.SingleToInt32Bits(padding);
                _ownerId = ownerId;
            }

            public bool Equals(CacheKey other)
            {
                return _shaderId == other._shaderId &&
                       _build == other._build &&
                       _layerMask == other._layerMask &&
                       _resolution == other._resolution &&
                       _paddingBits == other._paddingBits &&
                       _ownerId == other._ownerId;
            }

            public override bool Equals(object obj)
            {
                return obj is CacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _shaderId;
                    hash = hash * 397 ^ (int)_build;
                    hash = hash * 397 ^ _layerMask;
                    hash = hash * 397 ^ _resolution;
                    hash = hash * 397 ^ _paddingBits;
                    hash = hash * 397 ^ _ownerId;
                    return hash;
                }
            }
        }

        private readonly struct RendererStamp
        {
            internal readonly Renderer renderer;
            internal readonly int layer;
            internal readonly Bounds bounds;
            internal readonly int hash;

            internal RendererStamp(Renderer renderer, int layer, Bounds bounds, int hash)
            {
                this.renderer = renderer;
                this.layer = layer;
                this.bounds = bounds;
                this.hash = hash;
            }
        }

        private static class Ids
        {
            internal static readonly int AccelerationStructure = Shader.PropertyToID("_SceneAccelerationStructure");
            internal static readonly int Resolution = Shader.PropertyToID("_Resolution");
            internal static readonly int FieldMinimum = Shader.PropertyToID("_FieldMinimum");
            internal static readonly int FieldSize = Shader.PropertyToID("_FieldSize");
            internal static readonly int CellSize = Shader.PropertyToID("_CellSize");
            internal static readonly int MaximumTraceDistance = Shader.PropertyToID("_MaximumTraceDistance");
            internal static readonly int Jump = Shader.PropertyToID("_Jump");
            internal static readonly int SeedRead = Shader.PropertyToID("_SeedRead");
            internal static readonly int SeedWrite = Shader.PropertyToID("_SeedWrite");
            internal static readonly int Inside = Shader.PropertyToID("_Inside");
            internal static readonly int Distance = Shader.PropertyToID("_Distance");

            internal static readonly int VoxelCount = Shader.PropertyToID("_VoxelCount");
            internal static readonly int MaxDistance = Shader.PropertyToID("_MaxDistance");
            internal static readonly int InitialDistance = Shader.PropertyToID("_InitialDistance");
            internal static readonly int UnreachedDistance = Shader.PropertyToID("_UnreachedDistance");
            internal static readonly int VertexBuffer = Shader.PropertyToID("_VertexBuffer");
            internal static readonly int IndexBuffer = Shader.PropertyToID("_IndexBuffer");
            internal static readonly int IndexFormat16 = Shader.PropertyToID("_IndexFormat16");
            internal static readonly int VertexStride = Shader.PropertyToID("_VertexStride");
            internal static readonly int PositionOffset = Shader.PropertyToID("_PositionOffset");
            internal static readonly int IndexStart = Shader.PropertyToID("_IndexStart");
            internal static readonly int BaseVertex = Shader.PropertyToID("_BaseVertex");
            internal static readonly int TriangleOffset = Shader.PropertyToID("_TriangleOffset");
            internal static readonly int TriangleCount = Shader.PropertyToID("_TriangleCount");
            internal static readonly int MeshToWorld = Shader.PropertyToID("_MeshToWorld");
            internal static readonly int SplatField = Shader.PropertyToID("_SplatField");
            internal static readonly int FieldRead = Shader.PropertyToID("_FieldRead");
            internal static readonly int FieldWrite = Shader.PropertyToID("_FieldWrite");
            internal static readonly int JumpRead = Shader.PropertyToID("_JumpRead");
            internal static readonly int JumpWrite = Shader.PropertyToID("_JumpWrite");
            internal static readonly int JumpOffset = Shader.PropertyToID("_JumpOffset");
            internal static readonly int JumpAxisOffset = Shader.PropertyToID("_JumpAxisOffset");
        }
    }
}
