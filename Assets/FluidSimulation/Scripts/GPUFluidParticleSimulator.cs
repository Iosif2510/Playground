using System;
using System.Runtime.InteropServices;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

namespace Playground.FluidSimulation
{
    public sealed class GPUFluidParticleSimulator : MonoBehaviour
    {
        private const int ThreadsPerGroup = 256;

        // HLSL:
        // struct Particle { float2 position; float2 velocity; };
        private const int ParticleStride = sizeof(float) * 4;

        // HLSL:
        // struct SpatialHashEntry
        // {
        //     uint hash;
        //     uint particleIndex;
        //     int2 cell;
        // };
        private const int HashEntryStride = sizeof(uint) * 2 + sizeof(int) * 2;

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

        // Particle indirect render shader properties
        private static readonly int ParticleRenderSizeId =
            Shader.PropertyToID("_ParticleRenderSize");

        private static readonly int ParticleColorId =
            Shader.PropertyToID("_ParticleColor");
        
        private static readonly int GrabRadiusId =
            Shader.PropertyToID("_GrabRadius");

        [StructLayout(LayoutKind.Sequential)]
        private struct ParticleGpu
        {
            public Vector2 position;
            public Vector2 velocity;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SpatialHashEntryGpu
        {
            public uint hash;
            public uint particleIndex;
            public int cellX;
            public int cellY;
        }

        [Title("Compute")]
        [SerializeField, Required]
        private ComputeShader fluidSimulation;

        [Title("Simulation")]
        [SerializeField]
        private Bounds bounds = new Bounds(
            Vector3.zero,
            new Vector3(8f, 4f, 0f));

        [SerializeField, Min(1)]
        private int numParticles = 127;

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
        private float gravity = -1f;

        [SerializeField, Range(0f, 1f)]
        private float boundaryDamping = 0.35f;

        [SerializeField, Min(0f)]
        private float lookAheadFactor = 1f / 120f;

        [Title("Initial Layout")]
        [SerializeField, Min(0.0001f)]
        private float particleSpacing = 0.25f;

        [SerializeField, Min(1)]
        private int columns = 13;

        [Title("Rendering")]
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

        private ComputeBuffer particleBuffer;
        private ComputeBuffer predictedPositionBuffer;
        private ComputeBuffer densityBuffer;
        private ComputeBuffer nearDensityBuffer;
        private ComputeBuffer spatialHashBuffer;
        private ComputeBuffer cellStartBuffer;

        private GraphicsBuffer indirectArgsBuffer;
        private GraphicsBuffer.IndirectDrawIndexedArgs[] indirectArgs;

        private MaterialPropertyBlock renderPropertyBlock;

        private int sortCount;
        private int cellTableSize;

        private int predictPositionsKernel;
        private int buildSpatialHashKernel;
        private int bitonicSortKernel;
        private int clearCellStartKernel;
        private int buildCellStartKernel;
        private int calculateDensitiesKernel;
        private int pressureAndIntegrateKernel;

        private float TargetDensity =>
            sampleDensity * densityMultiplier;

        private int ParticleGroupCount =>
            Mathf.CeilToInt(
                numParticles / (float)ThreadsPerGroup);

        private int SortGroupCount =>
            Mathf.CeilToInt(
                sortCount / (float)ThreadsPerGroup);

        private int CellGroupCount =>
            Mathf.CeilToInt(
                cellTableSize / (float)ThreadsPerGroup);

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

            sampleDensity = CalculateInitialDensity();

            BindComputeBuffers();
            SetSimulationParameters();
            UpdateIndirectArgs();
        }
        
        [SerializeField, Range(1, 16)]
        private int substeps = 4;

        private void FixedUpdate()
        {
            Simulate(Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            RenderParticles();
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
        }

        private void AllocateBuffers()
        {
            sortCount = Mathf.NextPowerOfTwo(numParticles);

            // Compute shader HashCell()의:
            // hash & (_CellTableSize - 1)
            // 연산을 위해 2의 거듭제곱으로 고정.
            cellTableSize = Mathf.NextPowerOfTwo(
                Mathf.Max(numParticles * 2, 1));

            particleBuffer = new ComputeBuffer(
                numParticles,
                ParticleStride,
                ComputeBufferType.Structured);

            predictedPositionBuffer = new ComputeBuffer(
                numParticles,
                sizeof(float) * 2,
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

            indirectArgsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.IndirectArguments,
                1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);

            indirectArgs =
                new GraphicsBuffer.IndirectDrawIndexedArgs[1];

            renderPropertyBlock = new MaterialPropertyBlock();
        }

        private void UploadInitialParticles()
        {
            var initialParticles = new ParticleGpu[numParticles];

            float spacing = particleSpacing;
            int index = 0;

            for (float y = bounds.min.y + spacing;
                 y < bounds.max.y - spacing && index < numParticles;
                 y += spacing)
            {
                for (float x = bounds.min.x + spacing;
                     x < bounds.max.x - spacing && index < numParticles;
                     x += spacing)
                {
                    initialParticles[index++] = new ParticleGpu
                    {
                        position = new Vector2(x, y),
                        velocity = Vector2.zero
                    };
                }
            }

            if (index != numParticles)
            {
                Debug.LogError(
                    $"초기 배치 공간 부족: {index}/{numParticles}. " +
                    $"bounds 또는 particleSpacing을 조정하세요.",
                    this);

                enabled = false;
                return;
            }

            particleBuffer.SetData(initialParticles);
        }
        
        private float CalculateInitialDensity() 
        {
            return numParticles * particleMass / (bounds.size.x * bounds.size.y);
        }

        private float CalculateInitialSampleDensity()
        {
            // initialization 직후 한 번만 readback.
            // simulation/runtime rendering path에서는 readback하지 않는다.
            var particles = new ParticleGpu[numParticles];
            particleBuffer.GetData(particles);

            var densitySamples = new float[numParticles];

            for (int i = 0; i < numParticles; i++)
            {
                float density = 0f;
                Vector2 p = particles[i].position;

                for (int j = 0; j < numParticles; j++)
                {
                    float distance = Vector2.Distance(
                        p,
                        particles[j].position);

                    density += particleMass *
                        SpikyKernel(smoothingRadius, distance);
                }

                densitySamples[i] = density;
            }

            Array.Sort(densitySamples);

            // 자유 표면을 제외하기 위해 상위 60% 평균.
            int begin = Mathf.FloorToInt(numParticles * 0.4f);
            float sum = 0f;

            for (int i = begin; i < numParticles; i++)
                sum += densitySamples[i];

            return sum / Mathf.Max(numParticles - begin, 1);
        }

        private static float SpikyKernel(
            float radius,
            float distance)
        {
            if (distance >= radius)
                return 0f;

            float volume = Mathf.PI * Mathf.Pow(radius, 4) / 6f;
            float value = radius - distance;

            return value * value / volume;
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

            // Cell size = h이면 3 x 3 cell만 탐색하면 된다.
            fluidSimulation.SetFloat(
                CellSizeId,
                smoothingRadius);

            fluidSimulation.SetFloat(
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

            // bounds 바깥으로 나가는 particle이 없다는 전제.
            // 외부 force를 추가할 계획이면 Expand로 여유를 준다.
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

            if (indirectArgsBuffer != null)
            {
                indirectArgsBuffer.Release();
                indirectArgsBuffer = null;
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