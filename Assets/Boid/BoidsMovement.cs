using Unity.Mathematics;
using UnityEngine;

namespace Boids
{
    public struct Boid
    {
        public float3 Position;
        public float3 Velocity;
    }

    public class BoidsMovement : MonoBehaviour
    {
        [Header("Spawn")]
        [SerializeField] private GameObject boidPrefab;
        [SerializeField] private int boidsCount = 100;
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
        private Boid[] boids;
        private Boid[] nextBoids;
        private readonly Collider[] colliders = new Collider[16];

        private const float Epsilon = 1e-6f;

        private void Start()
        {
            Initialize();
            SyncTransforms();
        }

        private void FixedUpdate()
        {
            Simulate(Time.fixedDeltaTime);
            SwapBuffers();
            SyncTransforms();
        }

        public void SetTarget(Transform target)
        {
            targetTransform = target;
        }

        private void Initialize()
        {
            boidTransforms = new Transform[boidsCount];
            boids = new Boid[boidsCount];
            nextBoids = new Boid[boidsCount];

            float3 initialVelocity = GetInitialVelocity();

            for (int i = 0; i < boidsCount; i++)
            {
                var boidObject = Instantiate(boidPrefab, transform);
                boidTransforms[i] = boidObject.transform;

                var spawnPosition =
                    (float3)transform.position +
                    (float3)UnityEngine.Random.insideUnitSphere * initialSphereRadius;

                boids[i] = new Boid
                {
                    Position = spawnPosition,
                    Velocity = initialVelocity
                };

                nextBoids[i] = boids[i];
            }
        }

        private float3 GetInitialVelocity()
        {
            float3 velocity = initialDirection;

            if (math.lengthsq(velocity) < Epsilon)
                velocity = new float3(0f, 0f, 1f);

            return ClampMagnitude(velocity, maxSpeed);
        }

        private void Simulate(float dt)
        {
            float separationRadiusSq = separationRadius * separationRadius;
            float alignmentRadiusSq = alignmentRadius * alignmentRadius;
            float cohesionRadiusSq = cohesionRadius * cohesionRadius;

            for (int i = 0; i < boidsCount; i++)
            {
                Boid current = boids[i];

                float3 separationSum = float3.zero;
                float3 alignmentVelocitySum = float3.zero;
                float3 cohesionPositionSum = float3.zero;

                int separationCount = 0;
                int alignmentCount = 0;
                int cohesionCount = 0;

                for (int j = 0; j < boidsCount; j++)
                {
                    if (i == j)
                        continue;

                    Boid other = boids[j];

                    float3 toOther = other.Position - current.Position;
                    float3 absDelta = math.abs(toOther);
                    float distanceSq = math.lengthsq(toOther);

                    if (distanceSq < Epsilon)
                        continue;

                    if (IsInsideAABB(absDelta, separationRadius) && distanceSq < separationRadiusSq)
                    {
                        float distance = math.sqrt(distanceSq);
                        float weight = 1f - math.saturate(distance / separationRadius);

                        separationSum -= (toOther / distance) * weight;
                        separationCount++;
                    }

                    if (IsInsideAABB(absDelta, alignmentRadius) && distanceSq < alignmentRadiusSq)
                    {
                        alignmentVelocitySum += other.Velocity;
                        alignmentCount++;
                    }

                    if (IsInsideAABB(absDelta, cohesionRadius) && distanceSq < cohesionRadiusSq)
                    {
                        cohesionPositionSum += other.Position;
                        cohesionCount++;
                    }
                }

                float3 acceleration = float3.zero;

                if (separationCount > 0)
                {
                    float3 desiredVelocity = math.normalizesafe(separationSum, float3.zero) * maxSpeed;
                    acceleration += GetSteeringForce(current.Velocity, desiredVelocity, separationStrength);
                }

                if (alignmentCount > 0)
                {
                    float3 avgVelocity = alignmentVelocitySum / alignmentCount;
                    float3 desiredVelocity = math.normalizesafe(avgVelocity, float3.zero) * maxSpeed;
                    acceleration += GetSteeringForce(current.Velocity, desiredVelocity, alignmentStrength);
                }

                if (cohesionCount > 0)
                {
                    float3 center = cohesionPositionSum / cohesionCount;
                    float3 toCenter = center - current.Position;
                    float3 desiredVelocity = math.normalizesafe(toCenter, float3.zero) * maxSpeed;
                    acceleration += GetSteeringForce(current.Velocity, desiredVelocity, cohesionStrength);
                }

                acceleration += GetCollisionForce(current);

                if (targetTransform != null)
                {
                    acceleration += GetSeekForce(current, (float3)targetTransform.position, targetFollowStrength);
                }

                current.Velocity = ClampMagnitude(current.Velocity + acceleration * dt, maxSpeed);
                current.Position += current.Velocity * dt;

                nextBoids[i] = current;
            }
        }

        private static bool IsInsideAABB(float3 absDelta, float radius)
        {
            return absDelta.x <= radius
                && absDelta.y <= radius
                && absDelta.z <= radius;
        }

        private float3 GetSteeringForce(float3 currentVelocity, float3 desiredVelocity, float weight)
        {
            float3 steering = desiredVelocity - currentVelocity;
            steering = ClampMagnitude(steering, maxSteeringForce);
            return steering * weight;
        }

        private float3 GetSeekForce(Boid boid, float3 targetPosition, float weight)
        {
            float3 desiredVelocity =
                math.normalizesafe(targetPosition - boid.Position, float3.zero) * maxSpeed;

            return GetSteeringForce(boid.Velocity, desiredVelocity, weight);
        }

        private float3 GetCollisionForce(Boid boid)
        {
            int count = Physics.OverlapBoxNonAlloc(
                boid.Position,
                Vector3.one * collisionCheckHalfExtent,
                colliders,
                Quaternion.identity,
                collisionLayerMask);

            if (count == 0)
                return float3.zero;

            float3 totalForce = float3.zero;

            for (int i = 0; i < count; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled)
                    continue;

                float3 closestPoint = collider.ClosestPoint(boid.Position);
                float3 away = boid.Position - closestPoint;
                float awayDistanceSqr = math.lengthsq(away);

                if (awayDistanceSqr < Epsilon)
                {
                    away = boid.Position - (float3)collider.bounds.center;
                    awayDistanceSqr = maxCollisionResolveStrength / collisionResolveStrength;
                }

                float3 pushDir = math.normalizesafe(away, new float3(0f, 1f, 0f));
                totalForce += pushDir * math.rsqrt(awayDistanceSqr) * collisionResolveStrength;
            }

            return ClampMagnitude(totalForce, maxCollisionResolveStrength);
        }

        private void SwapBuffers()
        {
            (boids, nextBoids) = (nextBoids, boids);
        }

        private void SyncTransforms()
        {
            for (int i = 0; i < boidsCount; i++)
            {
                boidTransforms[i].position = boids[i].Position;
                boidTransforms[i].forward =
                    math.normalizesafe(boids[i].Velocity, new float3(0f, 0f, 1f));
            }
        }

        private static float3 ClampMagnitude(float3 vector, float maxLength)
        {
            float lengthSq = math.lengthsq(vector);
            float maxLengthSq = maxLength * maxLength;

            if (lengthSq <= maxLengthSq || lengthSq < Epsilon)
                return vector;

            return vector * math.rsqrt(lengthSq) * maxLength;
        }
    }
}
