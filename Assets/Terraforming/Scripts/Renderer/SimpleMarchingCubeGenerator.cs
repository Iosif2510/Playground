using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terraforming
{
    [Obsolete]
    public class SimpleMarchingCubeGenerator : MonoBehaviour
    {
        private struct Triangle
        {
            public Vector3 A;
            public Vector3 B;
            public Vector3 C;
        }

        [SerializeField] private ChunkedDensityField densityField;
        [SerializeField, Range(-5f, 5f)] private float isoLevel;
        [SerializeField] private float refreshRate = 0.1f;

        private MeshCollider meshCollider;
        private MeshFilter meshFilter;
        private Mesh mesh;
        private float timer;

        private readonly List<Vector3> vertices = new();
        private readonly List<int> triangleIndices = new();

        private readonly float[] cubeValues = new float[8];
        private readonly float3[] cubePoints = new float3[8];
        private readonly float3[] vertList = new float3[12];

        private (int first, int second)[] edgeList =
        {
            (0, 1), (1, 2), (2, 3), (3, 0),
            (4, 5), (5, 6), (6, 7), (7, 4),
            (0, 4), (1, 5), (2, 6), (3, 7)
        };

        private void Start()
        {
            if (densityField != null)
            {
                densityField.OnFieldUpdated += GenerateMeshes;
                densityField.OnChunksUpdated += GenerateMeshesPartial;
                densityField.InitializeField();
            }

            meshFilter = GetComponent<MeshFilter>();
            meshCollider = GetComponent<MeshCollider>();

            mesh = new Mesh
            {
                name = "Generated Marching Cube Mesh",
                indexFormat = IndexFormat.UInt32
            };
            mesh.MarkDynamic();

            if (meshFilter != null) meshFilter.sharedMesh = mesh;
            if (meshCollider != null) meshCollider.sharedMesh = mesh;
            
            timer = 0;
            GenerateMeshes();
        }

        private void OnDisable()
        {
            if (densityField != null)
            {
                densityField.OnFieldUpdated -= GenerateMeshes;
                densityField.OnChunksUpdated -= GenerateMeshesPartial;
            }

            if (mesh != null)
            {
                mesh.Clear();
            }
        }

        // public FieldChunk GetChunk(Vector3 position)
        // {
        //     return densityField == null ? null : densityField.GetChunk(position);
        // }
        //
        // public List<FieldChunk> GetChunksInRadius(Vector3 position, float radius)
        // {
        //     return densityField == null ? null : densityField.GetChunksInRadius(position, radius);
        // }

        [Button]
        private void GenerateMeshes()
        {
            if (mesh == null || densityField == null || densityField.Chunks == null) return;
            ProcessChunks(densityField.Chunks);
        }

        private void GenerateMeshesPartial(List<FieldChunk> modifiedChunks)
        {
            // SimpleMarchingCubeGenerator는 단일 Mesh를 사용하므로
            // 청크 단위 로컬 갱신이 불가능하여 전체를 다시 그립니다.
            // 부분 업데이트를 원하신다면 개별 오브젝트를 생성하는 TerrainRenderer를 사용하세요.
            if (mesh == null || densityField == null || densityField.Chunks == null) return;
            ProcessChunks(densityField.Chunks);
        }

        private void ProcessChunks(IReadOnlyList<FieldChunk> chunks)
        {
            mesh.Clear();
            vertices.Clear();
            triangleIndices.Clear();

            foreach (var chunk in chunks)
            {
                var size = chunk.ActualSize;
                int step = math.max(1, chunk.LodStep);

                // Allow processing up to size.x because GetDensity reads global Field seamlessly
                int maxReadX = (chunk.OriginIndex.x + size.x < densityField.Resolution) ? size.x : size.x - 1;
                int maxReadY = (chunk.OriginIndex.y + size.y < densityField.Resolution) ? size.y : size.y - 1;
                int maxReadZ = (chunk.OriginIndex.z + size.z < densityField.Resolution) ? size.z : size.z - 1;

                for (var x = 0; x + step <= maxReadX; x += step)
                for (var y = 0; y + step <= maxReadY; y += step)
                for (var z = 0; z + step <= maxReadZ; z += step)
                {
                    if (!chunk.RenderMesh) continue;
                    var triangles = MarchCube(new int3(x, y, z), chunk, step);

                    foreach (var triangle in triangles)
                    {
                        var currentVertexCount = vertices.Count;

                        vertices.Add(triangle.A);
                        vertices.Add(triangle.B);
                        vertices.Add(triangle.C);

                        triangleIndices.Add(currentVertexCount);
                        triangleIndices.Add(currentVertexCount + 1);
                        triangleIndices.Add(currentVertexCount + 2);
                    }
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangleIndices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (meshCollider != null)
            {
                meshCollider.sharedMesh = mesh;
            }
        }

        private List<Triangle> MarchCube(int3 indexPoint, FieldChunk chunk, int step)
        {
            var x = indexPoint.x;
            var y = indexPoint.y;
            var z = indexPoint.z;

            // We safely read beyond chunk's local bounds because GetDensity accesses the global Field flat array.
            cubeValues[0] = chunk.GetDensity(x, y, z);
            cubeValues[1] = chunk.GetDensity(x + step, y, z);
            cubeValues[2] = chunk.GetDensity(x + step, y, z + step);
            cubeValues[3] = chunk.GetDensity(x, y, z + step);
            cubeValues[4] = chunk.GetDensity(x, y + step, z);
            cubeValues[5] = chunk.GetDensity(x + step, y + step, z);
            cubeValues[6] = chunk.GetDensity(x + step, y + step, z + step);
            cubeValues[7] = chunk.GetDensity(x, y + step, z + step);

            cubePoints[0] = chunk.GetWorldPosition(x, y, z);
            cubePoints[1] = chunk.GetWorldPosition(x + step, y, z);
            cubePoints[2] = chunk.GetWorldPosition(x + step, y, z + step);
            cubePoints[3] = chunk.GetWorldPosition(x, y, z + step);
            cubePoints[4] = chunk.GetWorldPosition(x, y + step, z);
            cubePoints[5] = chunk.GetWorldPosition(x + step, y + step, z);
            cubePoints[6] = chunk.GetWorldPosition(x + step, y + step, z + step);
            cubePoints[7] = chunk.GetWorldPosition(x, y + step, z + step);

            var cubeIndex = 0;
            for (var i = 0; i < 8; i++)
            {
                cubeIndex |= (cubeValues[i] < isoLevel) ? (1 << i) : 0;
            }
            
            var triangleList = new List<Triangle>();

            if (cubeIndex == 0 || cubeIndex == 255) return triangleList;

            for (var i = 0; i < 12; i++)
            {
                if ((LookupTable.edgeTable[cubeIndex] & (1 << i)) == 0) continue;
                var point1 = cubePoints[edgeList[i].first];
                var point2 = cubePoints[edgeList[i].second];
                var value1 = cubeValues[edgeList[i].first];
                var value2 = cubeValues[edgeList[i].second];
                vertList[i] = Interpolate(point1, point2, value1, value2, isoLevel);
            }

            for (int i = 0; LookupTable.triangleTable[cubeIndex, i] != -1; i += 3)
            {
                triangleList.Add(new Triangle
                {
                    A = vertList[LookupTable.triangleTable[cubeIndex, i]],
                    B = vertList[LookupTable.triangleTable[cubeIndex, i + 1]],
                    C = vertList[LookupTable.triangleTable[cubeIndex, i + 2]]
                });
            }

            return triangleList;
        }

        private static float3 Interpolate(float3 point0, float3 point1, float value0, float value1, float threshold)
        {
            var t = (threshold - value0) / (value1 - value0);
            return point0 + t * (point1 - point0);
        }

    }
}