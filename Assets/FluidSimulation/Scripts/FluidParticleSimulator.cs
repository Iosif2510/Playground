using System;
using Sirenix.OdinInspector;
using Unity.Burst;
using UnityEngine;
using UnityEngine.Serialization;

namespace Playground.FluidSimulation
{
    public class FluidParticleSimulator : MonoBehaviour
    {
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        [BurstCompile, Serializable]
        public struct Particle
        {
            public Vector2 position;
            public Vector2 velocity;
        }
        
        private Particle[] particles;
        private Matrix4x4[] matrices;

        [Title("Simulation")] 
        [SerializeField] private Bounds bounds;
        [SerializeField] int numParticles;
        [SerializeField] float particleMass = 1.0f;
        [SerializeField, ReadOnly] float sampleDensity = 1.0f;
        [SerializeField, Range(0.1f, 3)] float densityMultiplier = 1.0f;
        [SerializeField] float pressureMultiplier = 1.0f;
        [SerializeField] private float smoothingRadius = 0.1f;
        [SerializeField] private float gravity = 0.0f;
        [SerializeField] private float lookAheadFactor = 1f / 120f;

        private float[] densities;
        private Vector4[] particlePositions;
        private Vector2[] predictedPositions;
        
        private float targetDensity => sampleDensity * densityMultiplier;
        
        private const float DensityEpsilon = 1e-6f;
        
        [Title("Rendering")]
        [SerializeField] private Mesh particleMesh;
        [SerializeField] private Material particleMaterial;
        [SerializeField] private float particleRenderSize = 0.1f;
        [SerializeField] private Color particleColor = Color.white;

        [SerializeField] private Material fieldMaterial;
        [SerializeField] private Color zeroPressureColor = Color.white;
        [SerializeField] private Color positivePressureColor = Color.red;
        [SerializeField] private Color negativePressureColor = Color.blue;
        private MaterialPropertyBlock particlePropertyBlock;

        private void Start()
        {
            particles = new Particle[numParticles];
            matrices = new Matrix4x4[numParticles];
            densities = new float[numParticles];
            particlePositions = new Vector4[numParticles];
            predictedPositions = new Vector2[numParticles];
            particlePropertyBlock = new MaterialPropertyBlock();
            
            sampleDensity = CalibrateTargetDensity();
            InitializeParticles();
        }

        private void Update()
        {
            DrawParticles();
            UpdateBackgroundField();
        }
        
        private void UpdateBackgroundField()
        {
            if (fieldMaterial != null)
            {
                fieldMaterial.SetInt("_ParticleCount", numParticles);
                fieldMaterial.SetVectorArray("_ParticlePositions", particlePositions);
                fieldMaterial.SetFloat("_SmoothingRadius", smoothingRadius);
                fieldMaterial.SetFloat("_TargetDensity", targetDensity);
                fieldMaterial.SetFloat("_PressureMultiplier", pressureMultiplier);
                fieldMaterial.SetFloat("_ParticleMass", particleMass);

                fieldMaterial.SetColor("_ZeroCol", zeroPressureColor);
                fieldMaterial.SetColor("_PosCol", positivePressureColor);
                fieldMaterial.SetColor("_NegCol", negativePressureColor);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.DrawCube(bounds.center, bounds.size);
        }

        private void FixedUpdate()
        {
            UpdatePhysics(Time.fixedDeltaTime);
        }
        
        private float CalibrateTargetDensity()
        {
            return numParticles * particleMass / bounds.size.x * bounds.size.y;
        }

        private void InitializeParticles()
        {
            float spacing = 0.25f; // smoothingRadius 0.5의 절반부터 시작
            int index = 0;

            for (float y = bounds.min.y + spacing; y < bounds.max.y - spacing; y += spacing)
            {
                for (float x = bounds.min.x + spacing; x < bounds.max.x - spacing; x += spacing)
                {
                    if (index >= numParticles)
                        return;

                    particles[index].position = new Vector2(x, y);
                    particles[index].velocity = Vector2.zero;
                    index++;
                }
            }
        }

        private void UpdatePhysics(float deltaTime)
        {
            for (int i = 0; i < numParticles; i++)
            {
                predictedPositions[i] = particles[i].position + particles[i].velocity * lookAheadFactor;
            }
            
            for (var i = 0; i < numParticles; i++)
            {
                densities[i] = CalculateDensity(i);
            }
            
            for (var i = 0; i < numParticles; i++)
            {
                
                var pressureForce = CalculatePressureForce(i);

                // 유체 역학에서 a = F / d
                var pressureAcceleration = pressureForce / densities[i];

                particles[i].velocity += pressureAcceleration * deltaTime;  // 압력 적용
                particles[i].velocity.y += gravity * deltaTime;             // 중력 적용
                particles[i].position += particles[i].velocity * deltaTime; // 위치 이동

                particlePositions[i] = new Vector2(particles[i].position.x, particles[i].position.y);
            }

            for (var i = 0; i < numParticles; i++)
            {
                particles[i].position = particlePositions[i];
                SetParticlesInBound(i);
            }
        }

        private void SetParticlesInBound(int particleIndex)
        {
            var position = particles[particleIndex].position;
            var velocity = particles[particleIndex].velocity;
            if (position.x < bounds.min.x)
            {
                position.x = bounds.min.x;
                velocity.x = -velocity.x;
            }
            else if (position.x > bounds.max.x)
            {
                position.x = bounds.max.x;
                velocity.x = -velocity.x;
            }

            if (position.y < bounds.min.y)
            {
                position.y = bounds.min.y;
                velocity.y = -velocity.y;
            }
            else if (position.y > bounds.max.y)
            {
                position.y = bounds.max.y;
                velocity.y = -velocity.y;
            }
            
            particles[particleIndex].position = position;
            particles[particleIndex].velocity = velocity;
        }
        
        private void DrawParticles()
        {
            for (var i = 0; i < numParticles; i++)
            {
                matrices[i] = Matrix4x4.TRS(
                    particles[i].position,
                    Quaternion.identity,
                    Vector3.one * particleRenderSize
                );
            }

            particlePropertyBlock.SetColor(ColorProperty, particleColor);
            var drawCount = Mathf.Min(numParticles, 1023);
            Graphics.DrawMeshInstanced(particleMesh, 0, particleMaterial, matrices, drawCount, particlePropertyBlock);
        }
        
        private float CalculateDensity(int particleIndex)
        {
            var density = 0f;
            var particlePosition = predictedPositions[particleIndex];

            for (var i = 0; i < numParticles; i++)
            {
                // if (particleIndex == i) continue; // 자기 자신 무시
                var dst = Vector2.Distance(particlePosition, predictedPositions[i]);
                density += particleMass * SpikyKernel(smoothingRadius, dst);
            }
            return density;
        }
        
        private Vector2 CalculatePressureForce(int particleIndex)
        {
            var pressureForce = Vector2.zero;
            var pos = predictedPositions[particleIndex];

            for (int i = 0; i < numParticles; i++)
            {
                if (particleIndex == i) continue; // 자기 자신 무시

                var otherPos = predictedPositions[i];
                float dst = Vector2.Distance(pos, otherPos); // 두 입자 사이 거리

                if (dst == 0 || dst >= smoothingRadius) continue; // Smoothing radius 밖은 영향 X

                Vector2 dir = (otherPos - pos) / dst; // 힘이 작용할 방향 벡터 정규화
                float slope = SpikyKernelDerivative(smoothingRadius, dst); // 밀도 함수를 기반으로 기울기 값을 계산해서 압력 크기 정함
                float sharedPressure = CalculateSharedPressure(densities[particleIndex], densities[i]); // 작용-반작용 평균 압력
                
                float densityJ = Mathf.Max(densities[i], DensityEpsilon);

                pressureForce +=
                    dir * (sharedPressure * slope * particleMass) / densityJ;

                // 압력 기울기 공식을 바탕으로 힘 누적
                // pressureForce += dir * (sharedPressure * slope * particleMass) / densities[i];
            }

            return pressureForce;
        }
        
        private float CalculateSharedPressure(float densityA, float densityB)
        {
            // 뉴턴 제3법칙 -> 두 입자 압력의 평균값 사용
            float pressureA = ConvertDensityToPressure(densityA);
            float pressureB = ConvertDensityToPressure(densityB);
            return (pressureA + pressureB) / 2f;
        }

        private float ConvertDensityToPressure(float density)
        {
            // 밀도가 목표치보다 높으면 척력, 낮으면 인력 반환
            return (density - targetDensity) * pressureMultiplier;
        }
        
        private static float SpikyKernel(float radius, float dst)
        {
            if (dst >= radius) return 0;

            // [Spiky Kernel]
            float volume = Mathf.PI * Mathf.Pow(radius, 4) / 6f;
            return Mathf.Pow(radius - dst, 2) / volume;
        }

        private static float SpikyKernelDerivative(float radius, float dst)
        {
            if (dst >= radius) return 0;

            // [Spiky Kernel]
            float volume = Mathf.PI * Mathf.Pow(radius, 4) / 6f;
            return -2f * (radius - dst) / volume;
        }
    }

}
