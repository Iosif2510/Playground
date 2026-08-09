using System;
using System.Runtime.InteropServices;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

namespace Playground.FluidSimulation
{
    public sealed class GPUFluidParticleSimulator3D : MonoBehaviour
    {
        private const int ThreadsPerGroup = 256;

        // HLSL: struct Particle { float3 position; float3 velocity; };
        private const int ParticleStride = sizeof(float) * 6;

        // HLSL:
        // struct SpatialHashEntry
        // {
        //     uint hash;
        //     uint particleIndex;
        //     int3 cell;
        // };
        private const int HashEntryStride =
            sizeof(uint) * 2 + sizeof(int) * 3;

        private static readonly int ParticlesId =
            Shader.PropertyToID("_Particles");

        private static readonly int PredictedPositionsId =
            Shader.PropertyToID("_PredictedPositions");

        private static readonly int DensitiesId =
            Shader.PropertyToID("_Densities");

        private static readonly int NearDensitiesId =
            Shader.PropertyToID("_NearDensities");

        private static readonly int SpatialHashEntriesId =
            Shader.PropertyToID("_SpatialHashEntries");

        private static readonly int CellStartIndicesId =
            Shader.PropertyToID("_CellStartIndices");

        private static readonly int ParticleCountId =
            Shader.PropertyToID("_ParticleCount");

        private static readonly int SortCountId =
            Shader.PropertyToID("_SortCount");

        private static readonly int CellTableSizeId =
            Shader.PropertyToID("_CellTableSize");

        private static readonly int BitonicKId =
            Shader.PropertyToID("_BitonicK");

        private static readonly int BitonicJId =
            Shader.PropertyToID("_BitonicJ");

        private static readonly int DeltaTimeId =
            Shader.PropertyToID("_DeltaTime");

        private static readonly int PredictionTimeId =
            Shader.PropertyToID("_PredictionTime");

        private static readonly int GravityId =
            Shader.PropertyToID("_Gravity");

        private static readonly int BoundaryDampingId =
            Shader.PropertyToID("_BoundaryDamping");

        private static readonly int ParticleMassId =
            Shader.PropertyToID("_ParticleMass");

        private static readonly int TargetDensityId =
            Shader.PropertyToID("_TargetDensity");

        private static readonly int PressureMultiplierId =
            Shader.PropertyToID("_PressureMultiplier");

        private static readonly int SmoothingRadiusId =
            Shader.PropertyToID("_SmoothingRadius");

        private static readonly int CellSizeId =
            Shader.PropertyToID("_CellSize");

        private static readonly int BoundsMinId =
            Shader.PropertyToID("_BoundsMin");

        private static readonly int BoundsMaxId =
            Shader.PropertyToID("_BoundsMax");

        private static readonly int ParticleRenderSizeId =
            Shader.PropertyToID("_ParticleRenderSize");

        private static readonly int ParticleColorId =
            Shader.PropertyToID("_ParticleColor");

        private static readonly int DensityVolumeId =
            Shader.PropertyToID("_DensityVolume");

        private static readonly int DensityVolumeSizeId =
            Shader.PropertyToID("_DensityVolumeSize");

        private static readonly int VolumeBoundsMinId =
            Shader.PropertyToID("_VolumeBoundsMin");

        private static readonly int VolumeBoundsMaxId =
            Shader.PropertyToID("_VolumeBoundsMax");

        private static readonly int VolumeParticleRadiusId =
            Shader.PropertyToID("_VolumeParticleRadius");

        private static readonly int VolumeDensityScaleId =
            Shader.PropertyToID("_VolumeDensityScale");

        private static readonly int DensityVolumeTexelSizeId =
            Shader.PropertyToID("_DensityVolumeTexelSize");

        [StructLayout(LayoutKind.Sequential)]
        private struct ParticleGpu
        {
            public Vector3 position;
            public Vector3 velocity;
        }

        [Title("Compute")]
        [SerializeField, Required]
        private ComputeShader fluidSimulation;

        [Title("Simulation")]
        [SerializeField]
        private Bounds bounds = new Bounds(
            Vector3.zero,
            new Vector3(8f, 4f, 8f));

        [SerializeField, Min(1)]
        private int numParticles = 512;

        [SerializeField, Min(0.0001f)]
        private float particleMass = 0.1f;

        [SerializeField, ReadOnly]
        private float sampleDensity = 1f;

        [SerializeField, Range(0.1f, 3f)]
        private float densityMultiplier = 1f;

        [SerializeField, Min(0f)]
        private float pressureMultiplier = 10f;

        [SerializeField, Min(0.0001f)]
        private float smoothingRadius = 0.5f;

        [SerializeField]
        private Vector3 gravity = new Vector3(0f, -9.81f, 0f);

        [SerializeField, Range(0f, 1f)]
        private float boundaryDamping = 0.35f;

        [SerializeField, Min(0f)]
        private float lookAheadFactor = 1f / 120f;

        [SerializeField, Range(1, 16)]
        private int substeps = 4;

        [Title("Initial Layout")]
        [SerializeField, Min(0.0001f)]
        private float particleSpacing = 0.25f;

        [Title("Rendering")]
        [SerializeField]
        private bool renderDebugParticles;

        [SerializeField, Required]
        private Mesh particleMesh;

        [SerializeField, Required]
        private Material particleMaterial;

        [SerializeField, Min(0.0001f)]
        private float particleRenderSize = 0.1f;

        [SerializeField]
        private Color particleColor = Color.white;

        [SerializeField]
        private ShadowCastingMode shadowCastingMode =
            ShadowCastingMode.Off;

        [SerializeField]
        private bool receiveShadows;

        [Title("Raymarching Water")]
        [SerializeField]
        private bool renderRaymarchedWater = true;

        [SerializeField, Required]
        private Material raymarchingWaterMaterial;

        [SerializeField, Range(16, 128)]
        private int densityVolumeResolution = 64;

        [SerializeField, Min(0.0001f)]
        private float volumeParticleRadius = 0.55f;

        [SerializeField, Min(0.0001f)]
        private float volumeDensityScale = 0.18f;

        [SerializeField, Min(0f)]
        private float volumeBoundsPadding = 0.85f;

        private ComputeBuffer particleBuffer;
        private ComputeBuffer predictedPositionBuffer;
        private ComputeBuffer densityBuffer;
        private ComputeBuffer nearDensityBuffer;
        private ComputeBuffer spatialHashBuffer;
        private ComputeBuffer cellStartBuffer;
        private RenderTexture densityVolume;

        private GraphicsBuffer indirectArgsBuffer;
        private GraphicsBuffer.IndirectDrawIndexedArgs[] indirectArgs;

        private MaterialPropertyBlock renderPropertyBlock;
        private MaterialPropertyBlock waterPropertyBlock;
        private Mesh fullscreenTriangleMesh;

        private int sortCount;
        private int cellTableSize;

        private int predictPositionsKernel;
        private int buildSpatialHashKernel;
        private int bitonicSortKernel;
        private int clearCellStartKernel;
        private int buildCellStartKernel;
        private int calculateDensitiesKernel;
        private int pressureAndIntegrateKernel;
        private int generateDensityVolumeKernel;

        private float TargetDensity =>
            sampleDensity * densityMultiplier;

        private int ParticleGroupCount =>
            Mathf.CeilToInt(numParticles / (float)ThreadsPerGroup);

        private int SortGroupCount =>
            Mathf.CeilToInt(sortCount / (float)ThreadsPerGroup);

        private int CellGroupCount =>
            Mathf.CeilToInt(cellTableSize / (float)ThreadsPerGroup);

        private Bounds DensityVolumeBounds
        {
            get
            {
                Bounds volumeBounds = bounds;
                float padding = Mathf.Max(
                    volumeBoundsPadding,
                    volumeParticleRadius * 1.5f);

                volumeBounds.Expand(padding * 2f);
                return volumeBounds;
            }
        }

        private void Start()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogError(
                    "Compute shaders are not supported on this platform.",
                    this);

                enabled = false;
                return;
            }

            CacheKernels();
            AllocateBuffers();
            UploadInitialParticles();

            if (!enabled)
                return;

            sampleDensity = CalculateInitialSampleDensity();

            BindComputeBuffers();
            SetSimulationParameters();
            UpdateIndirectArgs();
            GenerateDensityVolume();
        }

        private void FixedUpdate()
        {
            int stepCount = Mathf.Max(substeps, 1);
            float substepDeltaTime =
                Time.fixedDeltaTime / stepCount;

            for (int i = 0; i < stepCount; i++)
                Simulate(substepDeltaTime);

            GenerateDensityVolume();
        }

        private void LateUpdate()
        {
            if (renderDebugParticles)
                RenderParticles();

            RenderRaymarchedWater();
        }

        private void OnDrawGizmos()
        {
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }

        private void CacheKernels()
        {
            predictPositionsKernel =
                fluidSimulation.FindKernel("PredictPositions");

            buildSpatialHashKernel =
                fluidSimulation.FindKernel("BuildSpatialHash");

            bitonicSortKernel =
                fluidSimulation.FindKernel("BitonicSort");

            clearCellStartKernel =
                fluidSimulation.FindKernel("ClearCellStartIndices");

            buildCellStartKernel =
                fluidSimulation.FindKernel("BuildCellStartIndices");

            calculateDensitiesKernel =
                fluidSimulation.FindKernel("CalculateDensities");

            pressureAndIntegrateKernel =
                fluidSimulation.FindKernel(
                    "CalculatePressureAndIntegrate");

            generateDensityVolumeKernel =
                fluidSimulation.FindKernel("GenerateDensityVolume");
        }

        private void AllocateBuffers()
        {
            sortCount = Mathf.NextPowerOfTwo(numParticles);

            cellTableSize = Mathf.NextPowerOfTwo(
                Mathf.Max(numParticles * 4, 1));

            particleBuffer = new ComputeBuffer(
                numParticles,
                ParticleStride,
                ComputeBufferType.Structured);

            predictedPositionBuffer = new ComputeBuffer(
                numParticles,
                sizeof(float) * 3,
                ComputeBufferType.Structured);

            densityBuffer = new ComputeBuffer(
                numParticles,
                sizeof(float),
                ComputeBufferType.Structured);

            nearDensityBuffer = new ComputeBuffer(
                numParticles,
                sizeof(float),
                ComputeBufferType.Structured);

            spatialHashBuffer = new ComputeBuffer(
                sortCount,
                HashEntryStride,
                ComputeBufferType.Structured);

            cellStartBuffer = new ComputeBuffer(
                cellTableSize,
                sizeof(uint),
                ComputeBufferType.Structured);

            AllocateDensityVolume();

            indirectArgsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments,
                1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);

            indirectArgs =
                new GraphicsBuffer.IndirectDrawIndexedArgs[1];

            renderPropertyBlock = new MaterialPropertyBlock();
            waterPropertyBlock = new MaterialPropertyBlock();
        }

        private void AllocateDensityVolume()
        {
            int resolution = Mathf.Max(densityVolumeResolution, 1);

            var descriptor = new RenderTextureDescriptor(
                resolution,
                resolution,
                RenderTextureFormat.RHalf,
                depthBufferBits: 0)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = resolution,
                enableRandomWrite = true,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false
            };

            densityVolume = new RenderTexture(descriptor)
            {
                name = "Fluid Density Volume 3D",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            densityVolume.Create();
        }

        private void UploadInitialParticles()
        {
            var initialParticles = new ParticleGpu[numParticles];

            float spacing = particleSpacing;
            int index = 0;
            Vector3 min = bounds.min + Vector3.one * spacing;
            Vector3 max = bounds.max - Vector3.one * spacing;

            for (float y = min.y;
                 y <= max.y && index < numParticles;
                 y += spacing)
            {
                for (float z = min.z;
                     z <= max.z && index < numParticles;
                     z += spacing)
                {
                    for (float x = min.x;
                         x <= max.x && index < numParticles;
                         x += spacing)
                    {
                        initialParticles[index++] = new ParticleGpu
                        {
                            position = new Vector3(x, y, z),
                            velocity = Vector3.zero
                        };
                    }
                }
            }

            if (index != numParticles)
            {
                Debug.LogError(
                    $"Initial particle layout has insufficient space: " +
                    $"{index}/{numParticles}. Adjust bounds or spacing.",
                    this);

                enabled = false;
                return;
            }

            particleBuffer.SetData(initialParticles);
        }

        private float CalculateInitialSampleDensity()
        {
            var particles = new ParticleGpu[numParticles];
            particleBuffer.GetData(particles);

            int sampleCount = Mathf.Min(numParticles, 512);
            var densitySamples = new float[sampleCount];

            for (int sample = 0; sample < sampleCount; sample++)
            {
                int i = sampleCount == 1
                    ? 0
                    : Mathf.RoundToInt(
                        sample *
                        (numParticles - 1) /
                        (float)(sampleCount - 1));

                float density = 0f;
                Vector3 position = particles[i].position;

                for (int j = 0; j < numParticles; j++)
                {
                    float distance =
                        Vector3.Distance(
                            position,
                            particles[j].position);

                    density +=
                        particleMass *
                        SpikyKernel(smoothingRadius, distance);
                }

                densitySamples[sample] = density;
            }

            Array.Sort(densitySamples);

            int begin = Mathf.FloorToInt(sampleCount * 0.4f);
            float sum = 0f;

            for (int i = begin; i < sampleCount; i++)
                sum += densitySamples[i];

            return sum / Mathf.Max(sampleCount - begin, 1);
        }

        private static float SpikyKernel(
            float radius,
            float distance)
        {
            if (distance >= radius)
                return 0f;

            float volume =
                Mathf.PI * Mathf.Pow(radius, 6) / 15f;

            float value = radius - distance;
            return value * value * value / volume;
        }

        private void BindComputeBuffers()
        {
            BindParticleBuffers(predictPositionsKernel);
            BindParticleBuffers(buildSpatialHashKernel);
            BindParticleBuffers(calculateDensitiesKernel);
            BindParticleBuffers(pressureAndIntegrateKernel);

            fluidSimulation.SetBuffer(
                buildSpatialHashKernel,
                SpatialHashEntriesId,
                spatialHashBuffer);

            fluidSimulation.SetBuffer(
                bitonicSortKernel,
                SpatialHashEntriesId,
                spatialHashBuffer);

            fluidSimulation.SetBuffer(
                clearCellStartKernel,
                CellStartIndicesId,
                cellStartBuffer);

            fluidSimulation.SetBuffer(
                buildCellStartKernel,
                SpatialHashEntriesId,
                spatialHashBuffer);

            fluidSimulation.SetBuffer(
                buildCellStartKernel,
                CellStartIndicesId,
                cellStartBuffer);

            BindNeighborBuffers(calculateDensitiesKernel);
            BindNeighborBuffers(pressureAndIntegrateKernel);

            fluidSimulation.SetBuffer(
                generateDensityVolumeKernel,
                ParticlesId,
                particleBuffer);

            fluidSimulation.SetTexture(
                generateDensityVolumeKernel,
                DensityVolumeId,
                densityVolume);
        }

        private void BindParticleBuffers(int kernel)
        {
            fluidSimulation.SetBuffer(
                kernel,
                ParticlesId,
                particleBuffer);

            fluidSimulation.SetBuffer(
                kernel,
                PredictedPositionsId,
                predictedPositionBuffer);
        }

        private void BindNeighborBuffers(int kernel)
        {
            fluidSimulation.SetBuffer(
                kernel,
                DensitiesId,
                densityBuffer);

            fluidSimulation.SetBuffer(
                kernel,
                NearDensitiesId,
                nearDensityBuffer);

            fluidSimulation.SetBuffer(
                kernel,
                SpatialHashEntriesId,
                spatialHashBuffer);

            fluidSimulation.SetBuffer(
                kernel,
                CellStartIndicesId,
                cellStartBuffer);
        }

        private void SetSimulationParameters()
        {
            fluidSimulation.SetInt(
                ParticleCountId,
                numParticles);

            fluidSimulation.SetInt(
                SortCountId,
                sortCount);

            fluidSimulation.SetInt(
                CellTableSizeId,
                cellTableSize);

            fluidSimulation.SetFloat(
                ParticleMassId,
                particleMass);

            fluidSimulation.SetFloat(
                TargetDensityId,
                TargetDensity);

            fluidSimulation.SetFloat(
                PressureMultiplierId,
                pressureMultiplier);

            fluidSimulation.SetFloat(
                SmoothingRadiusId,
                smoothingRadius);

            fluidSimulation.SetFloat(
                CellSizeId,
                smoothingRadius);

            fluidSimulation.SetVector(
                GravityId,
                gravity);

            fluidSimulation.SetFloat(
                BoundaryDampingId,
                boundaryDamping);

            fluidSimulation.SetVector(
                BoundsMinId,
                bounds.min);

            fluidSimulation.SetVector(
                BoundsMaxId,
                bounds.max);

            fluidSimulation.SetInts(
                DensityVolumeSizeId,
                densityVolumeResolution,
                densityVolumeResolution,
                densityVolumeResolution);

            Bounds volumeBounds = DensityVolumeBounds;

            fluidSimulation.SetVector(
                VolumeBoundsMinId,
                volumeBounds.min);

            fluidSimulation.SetVector(
                VolumeBoundsMaxId,
                volumeBounds.max);

            fluidSimulation.SetFloat(
                VolumeParticleRadiusId,
                volumeParticleRadius);

            fluidSimulation.SetFloat(
                VolumeDensityScaleId,
                volumeDensityScale);
        }

        private void Simulate(float deltaTime)
        {
            SetSimulationParameters();

            fluidSimulation.SetFloat(
                PredictionTimeId,
                lookAheadFactor);

            fluidSimulation.SetFloat(
                DeltaTimeId,
                deltaTime);

            fluidSimulation.Dispatch(
                predictPositionsKernel,
                ParticleGroupCount,
                1,
                1);

            fluidSimulation.Dispatch(
                buildSpatialHashKernel,
                SortGroupCount,
                1,
                1);

            SortSpatialHashEntries();

            fluidSimulation.Dispatch(
                clearCellStartKernel,
                CellGroupCount,
                1,
                1);

            fluidSimulation.Dispatch(
                buildCellStartKernel,
                ParticleGroupCount,
                1,
                1);

            fluidSimulation.Dispatch(
                calculateDensitiesKernel,
                ParticleGroupCount,
                1,
                1);

            fluidSimulation.Dispatch(
                pressureAndIntegrateKernel,
                ParticleGroupCount,
                1,
                1);
        }

        private void GenerateDensityVolume()
        {
            if (densityVolume == null)
                return;

            SetSimulationParameters();

            int groupCount =
                Mathf.CeilToInt(densityVolumeResolution / 8f);

            fluidSimulation.Dispatch(
                generateDensityVolumeKernel,
                groupCount,
                groupCount,
                groupCount);
        }

        private void SortSpatialHashEntries()
        {
            for (int k = 2; k <= sortCount; k <<= 1)
            {
                for (int j = k >> 1; j > 0; j >>= 1)
                {
                    fluidSimulation.SetInt(BitonicKId, k);
                    fluidSimulation.SetInt(BitonicJId, j);

                    fluidSimulation.Dispatch(
                        bitonicSortKernel,
                        SortGroupCount,
                        1,
                        1);
                }
            }
        }

        private void UpdateIndirectArgs()
        {
            if (particleMesh == null || indirectArgsBuffer == null)
                return;

            indirectArgs[0] =
                new GraphicsBuffer.IndirectDrawIndexedArgs
                {
                    indexCountPerInstance =
                        particleMesh.GetIndexCount(0),

                    instanceCount = (uint)numParticles,

                    startIndex =
                        particleMesh.GetIndexStart(0),

                    baseVertexIndex =
                        particleMesh.GetBaseVertex(0),

                    startInstance = 0
                };

            indirectArgsBuffer.SetData(indirectArgs);
        }

        private void RenderParticles()
        {
            if (particleMesh == null ||
                particleMaterial == null ||
                particleBuffer == null ||
                indirectArgsBuffer == null)
            {
                return;
            }

            renderPropertyBlock.SetBuffer(
                ParticlesId,
                particleBuffer);

            renderPropertyBlock.SetFloat(
                ParticleRenderSizeId,
                particleRenderSize);

            renderPropertyBlock.SetColor(
                ParticleColorId,
                particleColor);

            Bounds renderBounds = bounds;
            renderBounds.Expand(particleRenderSize * 2f);

            var renderParams = new RenderParams(particleMaterial)
            {
                worldBounds = renderBounds,
                matProps = renderPropertyBlock,
                shadowCastingMode = shadowCastingMode,
                receiveShadows = receiveShadows
            };

            Graphics.RenderMeshIndirect(
                renderParams,
                particleMesh,
                indirectArgsBuffer,
                commandCount: 1);
        }

        private void RenderRaymarchedWater()
        {
            if (!renderRaymarchedWater ||
                raymarchingWaterMaterial == null ||
                densityVolume == null)
            {
                return;
            }

            int resolution = Mathf.Max(densityVolumeResolution, 1);

            waterPropertyBlock.SetTexture(
                DensityVolumeId,
                densityVolume);

            waterPropertyBlock.SetVector(
                BoundsMinId,
                bounds.min);

            waterPropertyBlock.SetVector(
                BoundsMaxId,
                bounds.max);

            Bounds volumeBounds = DensityVolumeBounds;

            waterPropertyBlock.SetVector(
                VolumeBoundsMinId,
                volumeBounds.min);

            waterPropertyBlock.SetVector(
                VolumeBoundsMaxId,
                volumeBounds.max);

            waterPropertyBlock.SetVector(
                DensityVolumeSizeId,
                new Vector4(
                    resolution,
                    resolution,
                    resolution,
                    0f));

            waterPropertyBlock.SetVector(
                DensityVolumeTexelSizeId,
                new Vector4(
                    1f / resolution,
                    1f / resolution,
                    1f / resolution,
                    resolution));

            var renderParams = new RenderParams(raymarchingWaterMaterial)
            {
                worldBounds = volumeBounds,
                matProps = waterPropertyBlock,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };

            Matrix4x4 matrix =
                Matrix4x4.identity;

            Graphics.RenderMesh(
                renderParams,
                GetFullscreenTriangleMesh(),
                0,
                matrix);
        }

        private Mesh GetFullscreenTriangleMesh()
        {
            if (fullscreenTriangleMesh != null)
                return fullscreenTriangleMesh;

            fullscreenTriangleMesh = new Mesh
            {
                name = "Generated Raymarch Fullscreen Triangle"
            };

            fullscreenTriangleMesh.vertices = new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(3f, -1f, 0f),
                new Vector3(-1f, 3f, 0f)
            };

            fullscreenTriangleMesh.triangles = new[]
            {
                0, 1, 2
            };

            fullscreenTriangleMesh.bounds =
                new Bounds(Vector3.zero, Vector3.one * 2f);

            return fullscreenTriangleMesh;
        }

        private void OnDisable()
        {
            ReleaseBuffers();
        }

        private void OnDestroy()
        {
            ReleaseBuffers();
        }

        private void ReleaseBuffers()
        {
            ReleaseComputeBuffer(ref particleBuffer);
            ReleaseComputeBuffer(ref predictedPositionBuffer);
            ReleaseComputeBuffer(ref densityBuffer);
            ReleaseComputeBuffer(ref nearDensityBuffer);
            ReleaseComputeBuffer(ref spatialHashBuffer);
            ReleaseComputeBuffer(ref cellStartBuffer);

            if (densityVolume != null)
            {
                densityVolume.Release();
                densityVolume = null;
            }

            if (indirectArgsBuffer != null)
            {
                indirectArgsBuffer.Release();
                indirectArgsBuffer = null;
            }

            if (fullscreenTriangleMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(fullscreenTriangleMesh);
                else
                    DestroyImmediate(fullscreenTriangleMesh);

                fullscreenTriangleMesh = null;
            }
        }

        private static void ReleaseComputeBuffer(
            ref ComputeBuffer buffer)
        {
            if (buffer == null)
                return;

            buffer.Release();
            buffer = null;
        }
    }
}
