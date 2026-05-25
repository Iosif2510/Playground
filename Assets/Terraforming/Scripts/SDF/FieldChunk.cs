using System;
using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    [Serializable]
    public class FieldChunk
    {
        private static readonly int GizmoBufferProperty = Shader.PropertyToID("_GizmoBuffer");
        private static readonly int DimensionsProperty = Shader.PropertyToID("_Dimensions");
        private static readonly int CenterOffsetProperty = Shader.PropertyToID("_CenterOffset");
        private static readonly int UnitScaleProperty = Shader.PropertyToID("_UnitScale");
        private static readonly int OriginProperty = Shader.PropertyToID("_Origin");

        public readonly ChunkedDensityField owner;
        public readonly int3 OriginIndex;
        public readonly int3 ActualSize;
        public readonly int3 SampleSize;
        public readonly float3 WorldOrigin;

        private float[] stagingBuffer;
        private NativeArray<half> field;
        private bool isDirty;

        private bool drawBounds;
        [SerializeField] private bool drawGizmos;

        private Material instantiatedMaterial;
        private ComputeBuffer gizmoBuffer;
        private ComputeBuffer argsBuffer;
        private Bounds gizmoBounds;

        [SerializeField] private bool renderMesh = true;
        [SerializeField] private int lod = 1;

        public bool IsLoaded => field.IsCreated;
        public NativeArray<half> Field => field;
        public bool RenderMesh => renderMesh;
        public int Lod => lod;

        public FieldChunk(ChunkedDensityField owner, int3 originIndex, int3 actualSize, float3 worldOrigin)
        {
            this.owner = owner;
            this.OriginIndex = originIndex;
            this.ActualSize = actualSize;
            this.WorldOrigin = worldOrigin;
            this.SampleSize = new int3(
                actualSize.x + (originIndex.x + actualSize.x < owner.Resolution ? 1 : 0),
                actualSize.y + (originIndex.y + actualSize.y < owner.Resolution ? 1 : 0),
                actualSize.z + (originIndex.z + actualSize.z < owner.Resolution ? 1 : 0));
        }

        public Bounds FieldBounds
        {
            get
            {
                var size = (Vector3)((float3)(ActualSize - 1) * owner.UnitSize);
                var center = Vector3.Lerp(WorldOrigin, WorldOrigin + (float3)(ActualSize - 1) * owner.UnitSize, 0.5f);
                return new Bounds(center, size);
            }
        }

        public void Initialize()
        {
            int count = SampleSize.x * SampleSize.y * SampleSize.z;
            stagingBuffer = new float[count];

            if (owner.GizmoMesh != null && owner.GizmoMaterial != null)
            {
                gizmoBounds = new Bounds(
                    new Vector3(WorldOrigin.x, WorldOrigin.y, WorldOrigin.z),
                    Vector3.one * (math.cmax(SampleSize) * owner.UnitSize * 10));

                gizmoBuffer = new ComputeBuffer(count, sizeof(float));

                instantiatedMaterial = UnityEngine.Object.Instantiate(owner.GizmoMaterial);
                instantiatedMaterial.EnableKeyword("PROCEDURAL_INSTANCING_ON");
                instantiatedMaterial.SetBuffer(GizmoBufferProperty, gizmoBuffer);

                var args = new uint[5];
                args[0] = owner.GizmoMesh.GetIndexCount(0);
                args[1] = (uint)count;
                args[2] = owner.GizmoMesh.GetIndexStart(0);
                args[3] = owner.GizmoMesh.GetBaseVertex(0);

                argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
                argsBuffer.SetData(args);
            }

            drawGizmos = owner.DrawGizmos;
        }

        public void Dispose()
        {
            UnloadField();
            gizmoBuffer?.Release();
            argsBuffer?.Release();
            if (instantiatedMaterial != null)
            {
                UnityEngine.Object.Destroy(instantiatedMaterial);
            }
        }

        public void LoadOrGenerate()
        {
            if (field.IsCreated) return;

            field = new NativeArray<half>(SampleSize.x * SampleSize.y * SampleSize.z, Allocator.Persistent);

            if (!TryLoadFromDisk())
            {
                GenerateField();
                isDirty = true;
                SaveToDisk();
            }

            RefreshGizmo();
        }

        public void UnloadField()
        {
            if (!field.IsCreated) return;
            if (isDirty) SaveToDisk();
            field.Dispose();
            isDirty = false;
        }

        private void GenerateField()
        {
            for (var lz = 0; lz < SampleSize.z; lz++)
            for (var ly = 0; ly < SampleSize.y; ly++)
            for (var lx = 0; lx < SampleSize.x; lx++)
            {
                field[FlattenLocal(lx, ly, lz)] = owner.SampleDensity(GetWorldPosition(lx, ly, lz));
            }
        }

        private bool TryLoadFromDisk()
        {
            var path = GetStoragePath();
            if (!File.Exists(path)) return false;

            try
            {
                using var reader = new BinaryReader(File.OpenRead(path));
                if (reader.ReadInt32() != 1) return false;
                if (reader.ReadInt32() != SampleSize.x ||
                    reader.ReadInt32() != SampleSize.y ||
                    reader.ReadInt32() != SampleSize.z) return false;

                for (var i = 0; i < field.Length; i++)
                {
                    field[i] = new half { value = reader.ReadUInt16() };
                }

                isDirty = false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SaveToDisk()
        {
            if (!field.IsCreated) return;

            Directory.CreateDirectory(owner.ChunkStoragePath);
            using var writer = new BinaryWriter(File.Open(GetStoragePath(), FileMode.Create, FileAccess.Write));
            writer.Write(1);
            writer.Write(SampleSize.x);
            writer.Write(SampleSize.y);
            writer.Write(SampleSize.z);

            for (var i = 0; i < field.Length; i++)
            {
                writer.Write(field[i].value);
            }

            isDirty = false;
        }

        private string GetStoragePath()
        {
            return Path.Combine(owner.ChunkStoragePath, $"{OriginIndex.x}_{OriginIndex.y}_{OriginIndex.z}.chunk");
        }

        private int FlattenLocal(int lx, int ly, int lz)
        {
            return lx + ly * SampleSize.x + lz * SampleSize.x * SampleSize.y;
        }

        public float GetDensity(int lx, int ly, int lz)
        {
            return field[FlattenLocal(lx, ly, lz)];
        }

        public void SetDensity(int lx, int ly, int lz, half value)
        {
            LoadOrGenerate();
            int flatIdx = FlattenLocal(lx, ly, lz);
            if (field[flatIdx].value != value.value)
            {
                field[flatIdx] = value;
                isDirty = true;
                owner.NotifyFieldUpdated();
            }
        }

        public float3 GetWorldPosition(int lx, int ly, int lz)
        {
            return WorldOrigin + new float3(lx, ly, lz) * owner.UnitSize;
        }

        public enum ModifyMethod { Fill, Carve }

        public bool ModifySphereVolume(float3 position, float radius, ModifyMethod method)
        {
            LoadOrGenerate();
            bool modified = false;

            for (var lz = 0; lz < SampleSize.z; lz++)
            for (var ly = 0; ly < SampleSize.y; ly++)
            for (var lx = 0; lx < SampleSize.x; lx++)
            {
                var worldPos = GetWorldPosition(lx, ly, lz);
                if (math.abs(worldPos.x - position.x) > radius ||
                    math.abs(worldPos.y - position.y) > radius ||
                    math.abs(worldPos.z - position.z) > radius) continue;

                var sdfValue = math.length(worldPos - position) - radius;
                var flatIdx = FlattenLocal(lx, ly, lz);
                var currentVal = field[flatIdx];
                var currentFloat = (float)currentVal;

                float newFloat = currentFloat;
                if (method == ModifyMethod.Fill)
                    newFloat = math.min(currentFloat, sdfValue);
                else if (method == ModifyMethod.Carve)
                    newFloat = math.max(currentFloat, -sdfValue);

                var newVal = math.half(newFloat);

                if (currentVal.value != newVal.value)
                {
                    field[flatIdx] = newVal;
                    modified = true;
                }
            }

            if (modified)
            {
                isDirty = true;
                RefreshGizmo();
            }

            return modified;
        }

        public void SetDrawGizmos(bool value) => drawGizmos = value;
        public void DrawBounds(bool draw) => drawBounds = draw;

        public void DrawGizmo()
        {
            if (!drawGizmos || !field.IsCreated || instantiatedMaterial == null || owner.GizmoMesh == null) return;
            Graphics.DrawMeshInstancedIndirect(owner.GizmoMesh, 0, instantiatedMaterial, gizmoBounds, argsBuffer);
        }

        public void DrawBoundGizmo()
        {
            if (!drawBounds) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(FieldBounds.center, FieldBounds.size);
        }

        public void RefreshGizmo()
        {
            if (!drawGizmos || instantiatedMaterial == null || !field.IsCreated) return;

            int idx = 0;
            for (var lz = 0; lz < SampleSize.z; lz++)
            for (var ly = 0; ly < SampleSize.y; ly++)
            for (var lx = 0; lx < SampleSize.x; lx++)
            {
                stagingBuffer[idx++] = field[FlattenLocal(lx, ly, lz)];
            }

            instantiatedMaterial.SetVector(DimensionsProperty, new Vector4(SampleSize.x, SampleSize.y, SampleSize.z, 0));
            instantiatedMaterial.SetVector(CenterOffsetProperty, Vector4.zero);
            instantiatedMaterial.SetFloat(UnitScaleProperty, owner.UnitSize);
            instantiatedMaterial.SetVector(OriginProperty, new Vector4(WorldOrigin.x, WorldOrigin.y, WorldOrigin.z, 0));
            gizmoBuffer?.SetData(stagingBuffer);
        }
    }
}
