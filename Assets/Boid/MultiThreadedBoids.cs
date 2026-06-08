using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Boids
{
    public class MultiThreadedBoids : MonoBehaviour
    {
        [Header("Spawn")]
        [SerializeField] private GameObject boidPrefab;
        [SerializeField] private int boidsCount = 100;
        [SerializeField] private float cellSize = 1f;
        [SerializeField] private float initialSphereRadius = 1f;
        [SerializeField] private float3 initialDirection = new float3(0.1f, 0.1f, 0.1f);

        [Header("Movement")]
        [SerializeField] private float maxSpeed = 5f;
        [SerializeField] private float maxSteeringForce = 8f;

        [Header("Neighborhood Radius")]
        [SerializeField] private float separationRadius = 0.15f;
        [SerializeField] private float alignmentRadius = 0.5f;
        [SerializeField] private float cohesionRadius = 0.5f;

        [Header("Behavior Weight")]
        [SerializeField] private float separationStrength = 1.5f;
        [SerializeField] private float alignmentStrength = 0.8f;
        [SerializeField] private float cohesionStrength = 0.7f;

        [Header("Collision")]
        [SerializeField] private float collisionCheckHalfExtent = 0.5f;
        [SerializeField] private float collisionResolveStrength = 8f;
        [SerializeField] private float maxCollisionResolveStrength = 30f;
        [SerializeField] private LayerMask collisionLayerMask = 1 << 3;

        [Header("Target")]
        [SerializeField] private Transform targetTransform;
        [SerializeField] private float targetFollowStrength = 1f;

        private Transform[] boidTransforms;
        
        private NativeArray<float3> positions;
        private NativeArray<float3> velocities;
        private NativeArray<float3> nextPositions;
        private NativeArray<float3> nextVelocities;
        private NativeParallelMultiHashMap<int, int> grid;
        
        private void Start()
        {
            boidTransforms = new Transform[boidsCount];

            positions = new NativeArray<float3>(boidsCount, Allocator.Persistent);
            velocities = new NativeArray<float3>(boidsCount, Allocator.Persistent);
            nextPositions = new NativeArray<float3>(boidsCount, Allocator.Persistent);
            nextVelocities = new NativeArray<float3>(boidsCount, Allocator.Persistent);
            grid = new NativeParallelMultiHashMap<int, int>(boidsCount * 2, Allocator.Persistent);

            for (int i = 0; i < boidsCount; i++)
            {
                var go = Instantiate(boidPrefab, transform);
                boidTransforms[i] = go.transform;

                positions[i] = (float3)transform.position + (float3)UnityEngine.Random.insideUnitSphere;
                velocities[i] = math.normalizesafe(new float3(0.1f, 0.1f, 0.1f), new float3(0, 0, 1)) * maxSpeed;
            }
        }

        private void FixedUpdate()
        {
            grid.Clear();

            var buildGridJob = new BuildGridJob
            {
                Positions = positions,
                CellSize = cellSize,
                GridWriter = grid.AsParallelWriter()
            };

            var simulateJob = new SimulateBoidsJob
            {
                Positions = positions,
                Velocities = velocities,
                NextPositions = nextPositions,
                NextVelocities = nextVelocities,
                Grid = grid,
                DeltaTime = Time.fixedDeltaTime,
                CellSize = cellSize,
                MaxSpeed = maxSpeed,
                MaxSteeringForce = maxSteeringForce,
                SeparationRadius = separationRadius,
                AlignmentRadius = alignmentRadius,
                CohesionRadius = cohesionRadius,
                SeparationStrength = separationStrength,
                AlignmentStrength = alignmentStrength,
                CohesionStrength = cohesionStrength
            };

            JobHandle buildHandle = buildGridJob.Schedule(boidsCount, 64);
            JobHandle simulateHandle = simulateJob.Schedule(boidsCount, 64, buildHandle);
            simulateHandle.Complete();

            (positions, nextPositions) = (nextPositions, positions);
            (velocities, nextVelocities) = (nextVelocities, velocities);

            for (int i = 0; i < boidsCount; i++)
            {
                boidTransforms[i].position = positions[i];
                boidTransforms[i].forward = math.normalizesafe(velocities[i], new float3(0, 0, 1));
            }
        }

        private void OnDestroy()
        {
            if (positions.IsCreated) positions.Dispose();
            if (velocities.IsCreated) velocities.Dispose();
            if (nextPositions.IsCreated) nextPositions.Dispose();
            if (nextVelocities.IsCreated) nextVelocities.Dispose();
            if (grid.IsCreated) grid.Dispose();
        }
            
        [BurstCompile]
        private struct BuildGridJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> Positions;
            public float CellSize;
            public NativeParallelMultiHashMap<int, int>.ParallelWriter GridWriter;

            public void Execute(int index)
            {
                int3 cell = WorldToCell(Positions[index], CellSize);
                int hash = Hash(cell);
                GridWriter.Add(hash, index);
            }
        }

        [BurstCompile]
        private struct SimulateBoidsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> Positions;
            [ReadOnly] public NativeArray<float3> Velocities;
            [ReadOnly] public NativeParallelMultiHashMap<int, int> Grid;

            [WriteOnly] public NativeArray<float3> NextPositions;
            [WriteOnly] public NativeArray<float3> NextVelocities;

            public float DeltaTime;
            public float CellSize;
            public float MaxSpeed;
            public float MaxSteeringForce;

            public float SeparationRadius;
            public float AlignmentRadius;
            public float CohesionRadius;

            public float SeparationStrength;
            public float AlignmentStrength;
            public float CohesionStrength;

            public void Execute(int index)
            {
                float3 myPos = Positions[index];
                float3 myVel = Velocities[index];

                float sepR2 = SeparationRadius * SeparationRadius;
                float alignR2 = AlignmentRadius * AlignmentRadius;
                float cohR2 = CohesionRadius * CohesionRadius;

                float3 separationSum = 0;
                float3 alignmentSum = 0;
                float3 cohesionSum = 0;

                int separationCount = 0;
                int alignmentCount = 0;
                int cohesionCount = 0;

                int3 baseCell = WorldToCell(myPos, CellSize);

                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    int3 cell = baseCell + new int3(x, y, z);
                    int hash = Hash(cell);

                    NativeParallelMultiHashMapIterator<int> it;
                    int otherIndex;

                    if (!Grid.TryGetFirstValue(hash, out otherIndex, out it))
                        continue;

                    do
                    {
                        if (otherIndex == index)
                            continue;

                        float3 toOther = Positions[otherIndex] - myPos;
                        float distSq = math.lengthsq(toOther);
                        if (distSq < 1e-6f)
                            continue;

                        if (distSq < sepR2)
                        {
                            float dist = math.sqrt(distSq);
                            float weight = 1f - math.saturate(dist / SeparationRadius);
                            separationSum -= (toOther / dist) * weight;
                            separationCount++;
                        }

                        if (distSq < alignR2)
                        {
                            alignmentSum += Velocities[otherIndex];
                            alignmentCount++;
                        }

                        if (distSq < cohR2)
                        {
                            cohesionSum += Positions[otherIndex];
                            cohesionCount++;
                        }
                    } while (Grid.TryGetNextValue(out otherIndex, ref it));
                }

                float3 accel = 0;

                if (separationCount > 0)
                {
                    float3 desired = math.normalizesafe(separationSum, float3.zero) * MaxSpeed;
                    accel += ClampMagnitude(desired - myVel, MaxSteeringForce) * SeparationStrength;
                }

                if (alignmentCount > 0)
                {
                    float3 avgVel = alignmentSum / alignmentCount;
                    float3 desired = math.normalizesafe(avgVel, float3.zero) * MaxSpeed;
                    accel += ClampMagnitude(desired - myVel, MaxSteeringForce) * AlignmentStrength;
                }

                if (cohesionCount > 0)
                {
                    float3 center = cohesionSum / cohesionCount;
                    float3 desired = math.normalizesafe(center - myPos, float3.zero) * MaxSpeed;
                    accel += ClampMagnitude(desired - myVel, MaxSteeringForce) * CohesionStrength;
                }

                float3 newVel = ClampMagnitude(myVel + accel * DeltaTime, MaxSpeed);
                float3 newPos = myPos + newVel * DeltaTime;

                NextVelocities[index] = newVel;
                NextPositions[index] = newPos;
            }
        }


        private static int3 WorldToCell(float3 pos, float cellSize)
        {
            return (int3)math.floor(pos / cellSize);
        }

        private static int Hash(int3 cell)
        {
            unchecked
            {
                return cell.x * 73856093 ^ cell.y * 19349663 ^ cell.z * 83492791;
            }
        }

        private static float3 ClampMagnitude(float3 v, float max)
        {
            float lenSq = math.lengthsq(v);
            float maxSq = max * max;
            if (lenSq <= maxSq || lenSq < 1e-6f)
                return v;
            return v * math.rsqrt(lenSq) * max;
        }
        
    }
    
    
}