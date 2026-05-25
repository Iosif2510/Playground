using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    public class ChunkedDensityField : MonoBehaviour
    {
        [Header("Field Properties")]
        [SerializeField] private int resolution = 128;   // 전체 해상도 (chunkSize 배수 권장)
        [SerializeField] private float unitSize = 1.0f;
        [SerializeField] private int chunkSize = 16;
        [SerializeField] private string storageFolderName = "TerraformingChunks";

        [Header("Gizmo")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Mesh gizmoMesh;
        [SerializeField] private Material gizmoMaterial;

        [SerializeField] private DensityFieldGenerator fieldGenerator;

        public NativeArray<half> Field { get; private set; }

        public int Resolution  => resolution;
        public float UnitSize  => unitSize;
        public int ChunkSize   => chunkSize;
        public Mesh GizmoMesh        => gizmoMesh;
        public Material GizmoMaterial => gizmoMaterial;
        public bool DrawGizmos       => drawGizmos;
        public string ChunkStoragePath => System.IO.Path.Combine(Application.persistentDataPath, storageFolderName, gameObject.scene.name);
        
        public event Action OnFieldUpdated;
        public event Action<List<FieldChunk>> OnChunksUpdated;

        [SerializeField] private List<FieldChunk> chunks = new();
        public IReadOnlyList<FieldChunk> Chunks => chunks;

        private readonly List<FieldChunk> expandedUpdatedChunks = new();

        // ─────────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────────
        [Button]
        public void InitializeField()
        {
            if (Field.IsCreated) Field.Dispose();

            InitializeChunks();
            NotifyFieldUpdated();
        }

        private void InitializeChunks()
        {
            foreach (var chunk in chunks) chunk.Dispose();
            chunks.Clear();

            int chunksPerAxis = Mathf.CeilToInt((float)resolution / chunkSize);
            var fieldCenter   = new float3(resolution / 2f, resolution / 2f, resolution / 2f);

            for (var cx = 0; cx < chunksPerAxis; cx++)
            for (var cy = 0; cy < chunksPerAxis; cy++)
            for (var cz = 0; cz < chunksPerAxis; cz++)
            {
                var originIndex = new int3(cx * chunkSize, cy * chunkSize, cz * chunkSize);

                // 경계 청크는 실제 크기가 chunkSize보다 작을 수 있음
                var actualSize = new int3(
                    Mathf.Min(chunkSize, resolution - originIndex.x),
                    Mathf.Min(chunkSize, resolution - originIndex.y),
                    Mathf.Min(chunkSize, resolution - originIndex.z)
                );

                // 청크 월드 원점: 전체 필드 중심을 0으로 기준
                var worldOrigin = (originIndex - fieldCenter) * unitSize
                                  + (float3)transform.position;

                var chunk = new FieldChunk(this, originIndex, actualSize, worldOrigin);
                chunk.Initialize();
                chunks.Add(chunk);
            }
        }

        // ─────────────────────────────────────────────
        //  Unity 이벤트
        // ─────────────────────────────────────────────
        private void Update()
        {
            if (!drawGizmos) return;
            foreach (var chunk in chunks)
                chunk.DrawGizmo();
        }

        private void OnDrawGizmos()
        {
            foreach (var chunk in chunks)
                chunk.DrawBoundGizmo();
        }

        private void OnDestroy()
        {
            foreach (var chunk in chunks) chunk.Dispose();
            if (Field.IsCreated) Field.Dispose();
        }

        // ─────────────────────────────────────────────
        //  청크 조회
        // ─────────────────────────────────────────────

        /// <summary>월드 좌표를 포함하는 청크 반환 (없으면 null)</summary>
        public FieldChunk GetChunk(Vector3 position)
        {
            FieldChunk result = null;
            foreach (var chunk in chunks)
            {
                bool contains = chunk.FieldBounds.Contains(position);
                chunk.DrawBounds(contains);
                if (contains) result = chunk;
            }
            return result;
        }

        /// <summary>구 반경과 겹치는 모든 청크 반환</summary>
        public void GetChunksInRadius(Vector3 position, float radius, List<FieldChunk> results)
        {
            results.Clear();
            var sqrRadius = radius * radius;

            foreach (var chunk in chunks)
            {
                var closest = chunk.FieldBounds.ClosestPoint(position);
                var overlaps = (closest - position).sqrMagnitude <= sqrRadius;
                chunk.DrawBounds(overlaps);
                if (overlaps) results.Add(chunk);
            }
        }

        // ─────────────────────────────────────────────
        //  유틸리티
        // ─────────────────────────────────────────────

        /// <summary>전체 필드 1D 인덱스 → 월드 위치</summary>
        public float3 GetWorldPositionFromIndex(int flatIndex)
        {
            var x = flatIndex % resolution;
            var y = (flatIndex / resolution) % resolution;
            var z = flatIndex / (resolution * resolution);
            var center = new float3(resolution / 2f, resolution / 2f, resolution / 2f);
            return (new float3(x, y, z) - center) * unitSize
                   + (float3)(Vector3)transform.position;
        }

        public void NotifyFieldUpdated()
        {
            OnFieldUpdated?.Invoke();
        }

        public half SampleDensity(float3 worldPosition)
        {
            return fieldGenerator != null
                ? fieldGenerator.SampleDensity(worldPosition, transform.position, resolution, unitSize)
                : math.half(1f);
        }

        public void NotifyChunksUpdated(List<FieldChunk> modifiedChunks)
        {
            if (modifiedChunks == null || modifiedChunks.Count == 0)
            {
                OnChunksUpdated?.Invoke(modifiedChunks);
                return;
            }

            expandedUpdatedChunks.Clear();
            var padding = unitSize;

            foreach (var modifiedChunk in modifiedChunks)
            {
                AddUnique(expandedUpdatedChunks, modifiedChunk);

                var bounds = modifiedChunk.FieldBounds;
                bounds.Expand(Vector3.one * padding * 2f);

                foreach (var chunk in chunks)
                {
                    if (chunk == modifiedChunk) continue;
                    if (bounds.Intersects(chunk.FieldBounds))
                    {
                        AddUnique(expandedUpdatedChunks, chunk);
                    }
                }
            }

            OnChunksUpdated?.Invoke(expandedUpdatedChunks);
        }

        private static void AddUnique(List<FieldChunk> results, FieldChunk chunk)
        {
            if (chunk != null && !results.Contains(chunk))
            {
                results.Add(chunk);
            }
        }

        /// <summary>Generator로 전체 필드를 재생성하고 모든 청크 기즈모 갱신</summary>
        [Button]
        public void RegenerateField()
        {
            foreach (var chunk in chunks)
            {
                chunk.Dispose();
            }

            InitializeChunks();
            NotifyFieldUpdated();
        }
    }
}
