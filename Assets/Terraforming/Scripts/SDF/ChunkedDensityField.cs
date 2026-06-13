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

        [Header("Gizmo")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Mesh gizmoMesh;
        [SerializeField] private Material gizmoMaterial;

        [SerializeField] private float chunkModifyRadiusMargin = 0.1f;

        [SerializeField] private DensityFieldGenerator fieldGenerator;

        // ─── 전체 밀도 필드 ───────────────────────────
        // Field[x + y*R + z*R*R]
        // FieldChunk 들이 이 배열의 구간을 참조
        public NativeArray<half> Field { get; private set; }

        public int Resolution  => resolution;
        public float UnitSize  => unitSize;
        public int ChunkSize   => chunkSize;
        public Mesh GizmoMesh        => gizmoMesh;
        public Material GizmoMaterial => gizmoMaterial;
        public bool DrawGizmos       => drawGizmos;
        
        public event Action OnFieldUpdated;
        public event Action<List<FieldChunk>> OnChunksUpdated;

        [SerializeField] private List<FieldChunk> chunks = new();
        public IReadOnlyList<FieldChunk> Chunks => chunks;

        // private readonly List<FieldChunk> expandedUpdatedChunks = new();
        private readonly List<FieldChunk> highlightedChunks = new();

        private FieldChunk[] chunkGrid = Array.Empty<FieldChunk>();
        private int chunksPerAxis;

        // ─────────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────────
        [Button]
        public void InitializeField()
        {
            var count = resolution * resolution * resolution;
            
            if (Field.IsCreated) Field.Dispose();
            Field = new NativeArray<half>(count, Allocator.Persistent);

            fieldGenerator.GenerateField(Field, transform.position, resolution, unitSize);

            InitializeChunks();
            NotifyFieldUpdated();
        }

        private void InitializeChunks()
        {
            ClearHighlightedChunks();
            foreach (var chunk in chunks) chunk.Dispose();
            chunks.Clear();

            chunksPerAxis = Mathf.CeilToInt((float)resolution / chunkSize);
            chunkGrid = new FieldChunk[chunksPerAxis * chunksPerAxis * chunksPerAxis];
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
                chunkGrid[FlattenChunkIndex(cx, cy, cz)] = chunk;
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
            ClearHighlightedChunks();
            FieldChunk result = null;
            foreach (var chunk in chunks)
            {
                bool contains = chunk.FieldBounds.Contains(position);
                chunk.DrawBounds(contains);
                if (contains) result = chunk;
            }

            if (result != null)
            {
                highlightedChunks.Add(result);
            }

            return result;
        }

        /// <summary>구 반경과 겹치는 모든 청크 반환</summary>
        public void GetChunksInRadius(Vector3 position, float radius, List<FieldChunk> results)
        {
            results.Clear();
            ClearHighlightedChunks();

            if (chunks.Count == 0 || chunkGrid.Length == 0 || chunkSize <= 0 || unitSize <= 0f || radius < 0f)
            {
                return;
            }

            var fieldPosition = WorldToFieldPosition(position);
            var radiusInFieldUnits = radius / unitSize;

            var minField = new int3(
                math.clamp(Mathf.FloorToInt(fieldPosition.x - radiusInFieldUnits), 0, resolution - 1),
                math.clamp(Mathf.FloorToInt(fieldPosition.y - radiusInFieldUnits), 0, resolution - 1),
                math.clamp(Mathf.FloorToInt(fieldPosition.z - radiusInFieldUnits), 0, resolution - 1));
            var maxField = new int3(
                math.clamp(Mathf.CeilToInt(fieldPosition.x + radiusInFieldUnits), 0, resolution - 1),
                math.clamp(Mathf.CeilToInt(fieldPosition.y + radiusInFieldUnits), 0, resolution - 1),
                math.clamp(Mathf.CeilToInt(fieldPosition.z + radiusInFieldUnits), 0, resolution - 1));

            if (minField.x > maxField.x || minField.y > maxField.y || minField.z > maxField.z)
            {
                return;
            }

            var minChunk = new int3(minField.x / chunkSize, minField.y / chunkSize, minField.z / chunkSize);
            var maxChunk = new int3(maxField.x / chunkSize, maxField.y / chunkSize, maxField.z / chunkSize);
            var sqrRadius = radius * radius;

            // for (var cz = math.max(minChunk.z - 1, 0); cz <= math.min(maxChunk.z + 1, chunksPerAxis); cz++)
            // for (var cy = math.max(minChunk.y - 1, 0); cy <= math.min(maxChunk.y + 1, chunksPerAxis); cy++)
            // for (var cx = math.max(minChunk.x - 1, 0); cx <= math.min(maxChunk.x + 1, chunksPerAxis); cx++)
            for (var cz = minChunk.z; cz <= maxChunk.z; cz++)
            for (var cy = minChunk.y; cy <= maxChunk.y; cy++)
            for (var cx = minChunk.x; cx <= maxChunk.x; cx++)
            {
                var chunk = chunkGrid[FlattenChunkIndex(cx, cy, cz)];
                if (chunk == null) continue;

                var closest = chunk.FieldBounds.ClosestPoint(position);
                var overlaps = (closest - position).sqrMagnitude <= sqrRadius + chunkModifyRadiusMargin;
                if (!overlaps) continue;

                // chunk.DrawBounds(true);
                highlightedChunks.Add(chunk);
                results.Add(chunk);
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

        public void NotifyChunksUpdated(List<FieldChunk> modifiedChunks)
        {
            OnChunksUpdated?.Invoke(modifiedChunks);
        }

        private static void AddUnique(List<FieldChunk> results, FieldChunk chunk)
        {
            if (chunk != null && !results.Contains(chunk))
            {
                results.Add(chunk);
            }
        }

        private int FlattenChunkIndex(int cx, int cy, int cz)
        {
            return cx + cy * chunksPerAxis + cz * chunksPerAxis * chunksPerAxis;
        }

        private float3 WorldToFieldPosition(Vector3 position)
        {
            var fieldCenter = new float3(resolution / 2f, resolution / 2f, resolution / 2f);
            return ((float3)position - (float3)transform.position) / unitSize + fieldCenter;
        }

        private void ClearHighlightedChunks()
        {
            foreach (var chunk in highlightedChunks)
            {
                chunk.DrawBounds(false);
            }

            highlightedChunks.Clear();
        }

        /// <summary>Generator로 전체 필드를 재생성하고 모든 청크 기즈모 갱신</summary>
        [Button]
        public void RegenerateField()
        {
            if (!Field.IsCreated) return;
            fieldGenerator.GenerateField(Field, transform.position, resolution, unitSize);

            // Field 배열이 재사용되므로 Memory 슬라이스 재구성 없이 바로 GPU 갱신
            foreach (var chunk in chunks) chunk.RefreshGizmo();
            NotifyFieldUpdated();
        }
    }
}
