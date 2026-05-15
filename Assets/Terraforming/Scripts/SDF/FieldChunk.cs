using System;
using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    /// <summary>
    /// ChunkedDensityField 전체 Field 배열의 일부 영역을 담당하는 청크.
    /// fieldChunk[z][y] 는 전체 Field 의 X행을 복사 없이 직접 참조합니다.
    /// </summary>
    public class FieldChunk
    {
        private static readonly int GizmoBufferProperty = Shader.PropertyToID("_GizmoBuffer");
        private static readonly int ResolutionProperty  = Shader.PropertyToID("_Resolution");
        private static readonly int UnitScaleProperty   = Shader.PropertyToID("_UnitScale");
        private static readonly int OriginProperty      = Shader.PropertyToID("_Origin");

        private readonly ChunkedDensityField owner;

        /// <summary>전체 필드 내 이 청크의 시작 좌표 (정수 인덱스)</summary>
        public readonly int3 OriginIndex;

        /// <summary>실제 청크 해상도 (경계 청크는 ChunkSize보다 작을 수 있음)</summary>
        public readonly int3 ActualSize;

        /// <summary>이 청크의 월드 공간 원점</summary>
        public readonly float3 WorldOrigin;

        // fieldChunk[z][y] → owner.Field 의 X행을 복사 없이 직접 참조하는 슬라이스
        // 길이 = ActualSize.x
        // 쓰기도 가능하므로 Memory<float> 사용
        private Memory<float>[][] fieldChunk;

        // GPU 업로드용 스테이징 버퍼 (RefreshGizmo 호출 시에만 flatten 복사)
        private float[] stagingBuffer;

        private bool drawBounds;
        private bool drawGizmos;

        private Material instantiatedMaterial;
        private ComputeBuffer gizmoBuffer;
        private ComputeBuffer argsBuffer;
        private Bounds gizmoBounds;

        public FieldChunk(ChunkedDensityField owner, int3 originIndex, int3 actualSize, float3 worldOrigin)
        {
            this.owner       = owner;
            this.OriginIndex = originIndex;
            this.ActualSize  = actualSize;
            this.WorldOrigin = worldOrigin;
        }

        // ─────────────────────────────────────────────
        //  Bounds
        // ─────────────────────────────────────────────
        public Bounds FieldBounds
        {
            get
            {
                var size   = ((float3)(ActualSize - 1) * owner.UnitSize);
                var center = (WorldOrigin + (float3)(ActualSize - 1) * owner.UnitSize * 0.5f);
                return new Bounds(center, size);
            }
        }

        // ─────────────────────────────────────────────
        //  초기화 / 해제
        // ─────────────────────────────────────────────
        public void Initialize()
        {
            int R = owner.Resolution;

            // zero-copy 슬라이스 구성: fieldChunk[z][y] = Field 의 X행
            fieldChunk = new Memory<float>[ActualSize.z][];
            for (int lz = 0; lz < ActualSize.z; lz++)
            {
                fieldChunk[lz] = new Memory<float>[ActualSize.y];
                for (int ly = 0; ly < ActualSize.y; ly++)
                {
                    int start = (OriginIndex.x)
                              + (OriginIndex.y + ly) * R
                              + (OriginIndex.z + lz) * R * R;
                    fieldChunk[lz][ly] = new Memory<float>(owner.Field, start, ActualSize.x);
                }
            }

            // GPU 스테이징 버퍼
            int count = ActualSize.x * ActualSize.y * ActualSize.z;
            stagingBuffer = new float[count];

            // Gizmo GPU Instancing
            if (owner.GizmoMesh != null && owner.GizmoMaterial != null)
            {
                gizmoBounds = new Bounds(
                    (Vector3)WorldOrigin,
                    Vector3.one * (math.cmax(ActualSize) * owner.UnitSize * 10));

                gizmoBuffer = new ComputeBuffer(count, sizeof(float));

                instantiatedMaterial = UnityEngine.Object.Instantiate(owner.GizmoMaterial);
                instantiatedMaterial.EnableKeyword("PROCEDURAL_INSTANCING_ON");
                instantiatedMaterial.SetBuffer(GizmoBufferProperty, gizmoBuffer);

                var args = new uint[5];
                args[0] = owner.GizmoMesh.GetIndexCount(0);
                args[1] = (uint)count;
                args[2] = owner.GizmoMesh.GetIndexStart(0);
                args[3] = owner.GizmoMesh.GetBaseVertex(0);

                argsBuffer = new ComputeBuffer(
                    1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
                argsBuffer.SetData(args);
            }

            drawGizmos = owner.DrawGizmos;
        }

        public void Dispose()
        {
            gizmoBuffer?.Release();
            argsBuffer?.Release();
            if (instantiatedMaterial != null)
                UnityEngine.Object.Destroy(instantiatedMaterial);
        }

        // ─────────────────────────────────────────────
        //  데이터 접근 (zero-copy)
        // ─────────────────────────────────────────────

        /// <summary>청크 로컬 좌표로 밀도 읽기 — 복사 없음</summary>
        public float GetDensity(int lx, int ly, int lz) =>
            fieldChunk[lz][ly].Span[lx];

        /// <summary>청크 로컬 좌표로 밀도 쓰기 — owner.Field 에 직접 반영, 복사 없음</summary>
        public void SetDensity(int lx, int ly, int lz, float value) =>
            fieldChunk[lz][ly].Span[lx] = value;

        /// <summary>청크 로컬 좌표 → 월드 위치</summary>
        public float3 GetWorldPosition(int lx, int ly, int lz) =>
            WorldOrigin + new float3(lx, ly, lz) * owner.UnitSize;

        // ─────────────────────────────────────────────
        //  구 범위 편집
        // ─────────────────────────────────────────────
        public enum ModifyMethod { Fill, Carve }

        public void ModifySphereVolume(float3 position, float radius, ModifyMethod method)
        {
            for (var lz = 0; lz < ActualSize.z; lz++)
            for (var ly = 0; ly < ActualSize.y; ly++)
            {
                var span = fieldChunk[lz][ly].Span;   // 한 행 전체를 Span으로 획득
                for (var lx = 0; lx < ActualSize.x; lx++)
                {
                    var worldPos = GetWorldPosition(lx, ly, lz);
                    var sdfValue = math.length(worldPos - position) - radius;
                    span[lx] = method switch
                    {
                        ModifyMethod.Fill  => math.min(span[lx], sdfValue),
                        ModifyMethod.Carve => math.max(span[lx], -sdfValue),
                        _                  => span[lx]
                    };
                }
            }
            RefreshGizmo();
        }

        // ─────────────────────────────────────────────
        //  렌더링
        // ─────────────────────────────────────────────
        public void SetDrawGizmos(bool value) => drawGizmos = value;
        public void DrawBounds(bool draw)      => drawBounds = draw;

        public void DrawGizmo()
        {
            if (!drawGizmos || instantiatedMaterial == null || owner.GizmoMesh == null) return;
            Graphics.DrawMeshInstancedIndirect(
                owner.GizmoMesh, 0, instantiatedMaterial, gizmoBounds, argsBuffer);
        }

        public void DrawBoundGizmo()
        {
            if (!drawBounds) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(FieldBounds.center, FieldBounds.size);
        }

        /// <summary>
        /// GPU 버퍼 갱신. 이 때만 staging 배열로 flatten 복사가 발생합니다.
        /// </summary>
        public void RefreshGizmo()
        {
            if (instantiatedMaterial == null) return;

            // staging 버퍼로 flatten (z→y→x 순)
            int idx = 0;
            for (var lz = 0; lz < ActualSize.z; lz++)
            for (var ly = 0; ly < ActualSize.y; ly++)
            {
                fieldChunk[lz][ly].Span.CopyTo(
                    new Span<float>(stagingBuffer, idx, ActualSize.x));
                idx += ActualSize.x;
            }

            instantiatedMaterial.SetInteger(ResolutionProperty, ActualSize.x);
            instantiatedMaterial.SetFloat(UnitScaleProperty, owner.UnitSize);
            instantiatedMaterial.SetVector(OriginProperty,
                new Vector4(WorldOrigin.x, WorldOrigin.y, WorldOrigin.z, 0));
            gizmoBuffer?.SetData(stagingBuffer);
        }
    }
}