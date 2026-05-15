using System.Collections.Generic;
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

        [SerializeField] private DensityFieldGenerator fieldGenerator;

        // ─── 전체 밀도 필드 ───────────────────────────
        // Field[x + y*R + z*R*R]
        // FieldChunk 들이 이 배열의 구간을 Memory<float> 슬라이스로 직접 참조함
        public float[] Field { get; private set; }

        public int Resolution  => resolution;
        public float UnitSize  => unitSize;
        public int ChunkSize   => chunkSize;
        public Mesh GizmoMesh        => gizmoMesh;
        public Material GizmoMaterial => gizmoMaterial;
        public bool DrawGizmos       => drawGizmos;

        private readonly List<FieldChunk> chunks = new();
        public IReadOnlyList<FieldChunk> Chunks => chunks;

        // ─────────────────────────────────────────────
        //  초기화
        // ─────────────────────────────────────────────
        public void InitializeField()
        {
            var count = resolution * resolution * resolution;
            Field = new float[count];

            fieldGenerator.GenerateField(Field, transform.position, resolution, unitSize);
            InitializeChunks();
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
                var worldOrigin = ((float3)originIndex - fieldCenter) * unitSize
                                  + (float3)(Vector3)transform.position;

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
        public List<FieldChunk> GetChunksInRadius(Vector3 position, float radius)
        {
            var result    = new List<FieldChunk>();
            var sqrRadius = radius * radius;

            foreach (var chunk in chunks)
            {
                var closest = chunk.FieldBounds.ClosestPoint(position);
                bool overlaps = (closest - position).sqrMagnitude <= sqrRadius;
                chunk.DrawBounds(overlaps);
                if (overlaps) result.Add(chunk);
            }
            return result;
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

        /// <summary>Generator로 전체 필드를 재생성하고 모든 청크 기즈모 갱신</summary>
        public void RegenerateField()
        {
            fieldGenerator.GenerateField(Field, transform.position, resolution, unitSize);
            // Field 배열이 재사용되므로 Memory 슬라이스 재구성 없이 바로 GPU 갱신
            foreach (var chunk in chunks) chunk.RefreshGizmo();
        }
    }
}