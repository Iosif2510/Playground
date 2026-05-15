using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    using static Utils;
    
    public class SimpleMarchingCubeGenerator : MonoBehaviour
    {
        // [SerializeField] private SDFGenerator sdfGenerator;
        // [SerializeField] private SimpleDensityField densityField;
        [SerializeField, Range(-5f, 5f)] private float isoLevel = 0.0f; // 등고선 레벨
        [SerializeField] private float refreshRate = 0.1f;
        [SerializeField] private List<SimpleDensityField> densityFieldChunks;
        private MeshCollider meshCollider;
        
        private readonly int[] cubeCorners = new int[8];
        private MeshFilter meshFilter;
        private Mesh mesh;
        private float timer;
        
        private readonly List<Vector3> vertices = new ();
        private readonly List<int> triangleIndices = new ();

        private (int first, int second)[] edgeList =
        {
            (0, 1),
            (1, 2),
            (2, 3),
            (3, 0),
            (4, 5),
            (5, 6),
            (6, 7),
            (7, 4),
            (0, 4),
            (1, 5),
            (2, 6),
            (3, 7)
        };
        
        private void Start()
        {
            foreach (var densityField in densityFieldChunks)
            {
                densityField.InitializeField();
            }
            
            meshFilter = GetComponent<MeshFilter>();
            meshCollider = GetComponent<MeshCollider>();
            mesh = new Mesh();
            timer = 0;
            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = mesh;
            // GenerateMeshes();
        }

        private void OnDisable()
        {
            mesh.Clear();
        }

        private void Update()
        {
            if (timer > refreshRate)
            {
                GenerateMeshes();
                timer -= refreshRate;
            }
            timer += Time.deltaTime;
        }

        public SimpleDensityField GetChunk(Vector3 position)
        {
            SimpleDensityField result = null;
            foreach (var densityField in densityFieldChunks)
            {
                if (densityField.FieldBounds.Contains(position))
                {
                    result = densityField;
                    densityField.DrawBounds(true);
                }
                else densityField.DrawBounds(false);
            }

            return result;
        }

        public List<SimpleDensityField> GetChunksInRadius(Vector3 position, float radius)
        {
            var result = new List<SimpleDensityField>();
            var sqrRadius = radius * radius;
            
            foreach (var densityField in densityFieldChunks)
            {
                // Find the closest point in the chunk's bounds to our sphere center
                var closestPoint = densityField.FieldBounds.ClosestPoint(position);
                
                // If the distance to the closest point is less than or equal to the radius, there is an intersection
                if ((closestPoint - position).sqrMagnitude <= sqrRadius)
                {
                    result.Add(densityField);
                    densityField.DrawBounds(true);
                }
                else
                {
                    densityField.DrawBounds(false);
                }
            }
            return result;
        }

        private void GenerateMeshes()
        {
            
            mesh.Clear();

            vertices.Clear();
            triangleIndices.Clear();

            foreach (var densityField in densityFieldChunks)
            {
                var resolution = densityField.Resolution;

                for (var x = 0; x < resolution - 1; x++)
                {
                    for (var y = 0; y < resolution - 1; y++)
                    {
                        for (var z = 0; z < resolution - 1; z++)
                        {
                            // 각 정점의 밀도 값을 가져와서 삼각형을 생성하는 로직
                            // 예: Marching Cubes 알고리즘을 사용하여 isoLevel에 따라 삼각형 생성

                            var triangles = MarchCube(new int3(x, y, z), densityField);

                            foreach (var triangle in triangles)
                            {
                                var currentVertexCount = vertices.Count;

                                vertices.Add(triangle.a);
                                vertices.Add(triangle.b);
                                vertices.Add(triangle.c);

                                triangleIndices.Add(currentVertexCount);
                                triangleIndices.Add(currentVertexCount + 1);
                                triangleIndices.Add(currentVertexCount + 2);
                            }
                        }
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

        private List<Triangle> MarchCube(int3 indexPoint, SimpleDensityField densityField)
        {
            var x = indexPoint.x;
            var y = indexPoint.y;
            var z = indexPoint.z;

            var resolution = densityField.Resolution;
            var fieldBuffer = densityField.Field;
            
            cubeCorners[0] = Flatten(new (x, y, z), resolution);
            cubeCorners[1] = Flatten(new(x + 1, y, z), resolution);
            cubeCorners[2] = Flatten(new(x + 1, y, z + 1), resolution);
            cubeCorners[3] = Flatten(new(x, y, z + 1), resolution);
            cubeCorners[4] = Flatten(new(x, y + 1, z), resolution);
            cubeCorners[5] = Flatten(new(x + 1, y + 1, z), resolution);
            cubeCorners[6] = Flatten(new(x + 1, y + 1, z + 1), resolution);
            cubeCorners[7] = Flatten(new(x, y + 1, z + 1), resolution);
            
            var cubeIndex = 0;
            for (var i = 0; i < 8; i++)
            {
                cubeIndex |= (fieldBuffer[cubeCorners[i]] < isoLevel) ? (1 << i) : 0;
            }

            var vertList = new float3[12];
            for (var i = 0; i < 12; i++)
            {
                if ((LookupTable.edgeTable[cubeIndex] & (1 << i)) == 0) continue;
                var point1 = densityField.GetWorldPositionFromIndex(cubeCorners[edgeList[i].first]);
                var point2 = densityField.GetWorldPositionFromIndex(cubeCorners[edgeList[i].second]);
                var value1 = fieldBuffer[cubeCorners[edgeList[i].first]];
                var value2 = fieldBuffer[cubeCorners[edgeList[i].second]];
                vertList[i] = Interpolate(point1, point2, value1, value2, isoLevel);
                // vertList[i] = SimpleHalf(point1, point2);
            }

            //generate triangles using interpolated vertices and triangle table
            var triangleList = new List<Triangle>();
            for (int i = 0; LookupTable.triangleTable[cubeIndex, i] != -1; i += 3)
            {
                var trigToAdd = new Triangle
                {
                    a = vertList[LookupTable.triangleTable[cubeIndex, i]],
                    b = vertList[LookupTable.triangleTable[cubeIndex, i + 1]],
                    c = vertList[LookupTable.triangleTable[cubeIndex, i + 2]]
                };
                triangleList.Add(trigToAdd);
            }
            
            return triangleList;
        }

        private static float3 SimpleHalf(float3 point0, float3 point1) => (point0 + point1) / 2;

        private static float3 Interpolate(float3 point0, float3 point1, float value0, float value1, float threshold)
        {
            var t = (threshold - value0) / (value1 - value0);
            return point0 + t * (point1 - point0);
        }

    }
}