using System;
using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    /// <summary>
    /// ChunkedDensityField 전체 Field 배열의 일부 영역을 담당하는 청크.
    /// </summary>
    [Serializable]
    public class FieldChunk
    {
        private static readonly int GizmoBufferProperty = Shader.PropertyToID("_GizmoBuffer");
        private static readonly int DimensionsProperty  = Shader.PropertyToID("_Dimensions");
        private static readonly int CenterOffsetProperty = Shader.PropertyToID("_CenterOffset");
        private static readonly int UnitScaleProperty   = Shader.PropertyToID("_UnitScale");
        private static readonly int OriginProperty      = Shader.PropertyToID("_Origin");

        public readonly ChunkedDensityField owner;

        /// <summary>전체 필드 내 이 청크의 시작 좌표 (정수 인덱스)</summary>
        public readonly int3 OriginIndex;

        /// <summary>실제 청크 해상도 (경계 청크는 ChunkSize보다 작을 수 있음)</summary>
        public readonly int3 ActualSize;

        /// <summary>이 청크의 월드 공간 원점</summary>
        public readonly float3 WorldOrigin;

        // GPU 업로드용 스테이징 버퍼 (RefreshGizmo 호출 시에만 복사)
        private float[] stagingBuffer;

        private bool drawBounds;
        [SerializeField] private bool drawGizmos;

        private Material instantiatedMaterial;
        private ComputeBuffer gizmoBuffer;
        private ComputeBuffer argsBuffer;
        private Bounds gizmoBounds;
        private Bounds fieldBounds;
        

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
                if (fieldBounds == default)
                {
                    var size = (Vector3)((float3)(ActualSize - 1) * owner.UnitSize);
                    var center = Vector3.Lerp(WorldOrigin, WorldOrigin + (float3)(ActualSize - 1) * owner.UnitSize,
                        0.5f);
                    fieldBounds = new Bounds(center, size);

                }

                return fieldBounds;
            }
        }

        // ─────────────────────────────────────────────
        //  LOD 및 렌더링 설정
        // ─────────────────────────────────────────────
        [SerializeField] private bool renderMesh = true;
        [SerializeField] private int lod = 0;
        public bool RenderMesh => renderMesh;
        public int LodStep => 1 << lod;

        // ─────────────────────────────────────────────
        //  초기화 / 해제
        // ─────────────────────────────────────────────
        public void Initialize()
        {
            // GPU 스테이징 버퍼
            int count = ActualSize.x * ActualSize.y * ActualSize.z;
            stagingBuffer = new float[count];

            // Gizmo GPU Instancing
            if (owner.GizmoMesh != null && owner.GizmoMaterial != null)
            {
                gizmoBounds = new Bounds(
                    new Vector3(WorldOrigin.x, WorldOrigin.y, WorldOrigin.z),
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
            RefreshGizmo();
        }

        public void Dispose()
        {
            gizmoBuffer?.Release();
            argsBuffer?.Release();
            if (instantiatedMaterial != null)
                UnityEngine.Object.Destroy(instantiatedMaterial);
        }

        // ─────────────────────────────────────────────
        //  데이터 접근 (1D 배열 인덱싱, Zero-copy)
        // ─────────────────────────────────────────────
        
        /// <summary>청크 로컬 좌표 → 전체 필드의 1D 인덱스</summary>
        private int FlattenGlobal(int lx, int ly, int lz)
        {
            return (OriginIndex.x + lx) 
                   + (OriginIndex.y + ly) * owner.Resolution 
                   + (OriginIndex.z + lz) * owner.Resolution * owner.Resolution;
        }

        /// <summary>청크 로컬 좌표로 밀도 읽기 (이웃 청크의 값도 초과해서 읽을 수 있음)</summary>
        public float GetDensity(int lx, int ly, int lz) =>
            owner.Field[FlattenGlobal(lx, ly, lz)];

        /// <summary>청크 로컬 좌표로 밀도 쓰기 (전체 필드에 즉시 반영)</summary>
        public void SetDensity(int lx, int ly, int lz, half value)
        {
            int flatIdx = FlattenGlobal(lx, ly, lz);
            var fieldArray = owner.Field;
            if (fieldArray[flatIdx].value != value.value)
            {
                fieldArray[flatIdx] = value;
                owner.NotifyFieldUpdated();
            }
        }

        /// <summary>청크 로컬 좌표 → 월드 위치</summary>
        public float3 GetWorldPosition(int lx, int ly, int lz) =>
            WorldOrigin + new float3(lx, ly, lz) * owner.UnitSize;

        // ─────────────────────────────────────────────
        //  구 범위 편집
        // ─────────────────────────────────────────────
        public enum ModifyMethod { Fill, Carve }

        public bool ModifySphereVolume(float3 position, float radius, ModifyMethod method)
        {
            bool modified = false;
            var fieldArray = owner.Field;
            
            for (var lz = 0; lz < ActualSize.z; lz++)
            for (var ly = 0; ly < ActualSize.y; ly++)
            for (var lx = 0; lx < ActualSize.x; lx++)
            {
                var worldPos = GetWorldPosition(lx, ly, lz);
                // 미리 범위를 체크하여 구 계산의 상당 부분을 스킵 (박스 컬링)
                if (math.abs(worldPos.x - position.x) > radius ||
                    math.abs(worldPos.y - position.y) > radius ||
                    math.abs(worldPos.z - position.z) > radius) continue;

                var sdfValue = math.length(worldPos - position) - radius;
                var flatIdx = FlattenGlobal(lx, ly, lz);
                
                var currentVal = fieldArray[flatIdx];
                var currentFloat = (float)currentVal;
                
                // switch문과 math.half 변환이 이너 루프에서 계속 박싱/GC를 유발할 수 있으므로 분기문으로 변경
                float newFloat = currentFloat;
                if (method == ModifyMethod.Fill)
                    newFloat = math.min(currentFloat, sdfValue);
                else if (method == ModifyMethod.Carve)
                    newFloat = math.max(currentFloat, -sdfValue);

                var newVal = math.half(newFloat);
                
                if (currentVal.value != newVal.value)
                {
                    fieldArray[flatIdx] = newVal;
                    modified = true;
                }
            }
            if (modified)
            {
                RefreshGizmo();
                // owner.NotifyFieldUpdated() 제거 -> 최적화를 위해 외부에서 취합하여 한 번만 호출
            }
            return modified;
        }

        // ─────────────────────────────────────────────
        //  기즈모 렌더링
        // ─────────────────────────────────────────────
        public void SetDrawGizmos(bool value) => drawGizmos = value;
        public void DrawBounds(bool draw) => drawBounds = draw;

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
        /// GPU 버퍼 갱신. 이 때만 staging 배열로 flatten 복사가 발생.
        /// </summary>
        public void RefreshGizmo()
        {
            if (!drawGizmos || instantiatedMaterial == null) return;

            int idx = 0;
            for (var lz = 0; lz < ActualSize.z; lz++)
            for (var ly = 0; ly < ActualSize.y; ly++)
            for (var lx = 0; lx < ActualSize.x; lx++)
            {
                stagingBuffer[idx++] = owner.Field[FlattenGlobal(lx, ly, lz)];
            }

            instantiatedMaterial.SetVector(DimensionsProperty, new Vector4(ActualSize.x, ActualSize.y, ActualSize.z, 0));
            instantiatedMaterial.SetVector(CenterOffsetProperty, Vector4.zero); // 청크 내부에서는 오프셋 없음
            instantiatedMaterial.SetFloat(UnitScaleProperty, owner.UnitSize);
            instantiatedMaterial.SetVector(OriginProperty,
                new Vector4(WorldOrigin.x, WorldOrigin.y, WorldOrigin.z, 0));
            gizmoBuffer?.SetData(stagingBuffer);
        }
    }
}