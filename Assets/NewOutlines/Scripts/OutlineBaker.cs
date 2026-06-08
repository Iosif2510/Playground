using System.Collections.Generic;
using UnityEngine;

namespace NewOutlines
{
    public static class OutlineBaker
    {

        public static void BakeSmoothNormalsToUV(Mesh mesh, int targetUVChannel = 7) // 7 = uv8
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            
            // mesh.SetUVs(targetUVChannel, null);

            // 1. Smooth Normal 계산 로직 (간략화된 버전 - 위치가 같은 정점들의 법선을 평균내어 정규화)
            var smoothNormals = ComputeSmoothNormals(vertices, normals);

            var bakedData = new List<Vector2>();

            for (var i = 0; i < vertices.Length; i++)
            {
                var normal = normals[i];
                var tangent = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                var binormal = Vector3.Cross(normal, tangent) * tangents[i].w;

                // TBN 행렬의 역행렬(Transpose)을 사용하여 Object Space -> Tangent Space 변환
                // TBN 행렬 = [Tangent, Binormal, Normal]
                // 행 단위로 내적을 수행하여 변환
                var smoothNormalTS = new Vector3(
                    Vector3.Dot(tangent, smoothNormals[i]),
                    Vector3.Dot(binormal, smoothNormals[i]),
                    Vector3.Dot(normal, smoothNormals[i])
                ).normalized;

                // X와 Y값만 UV에 저장 (Z값은 셰이더에서 복원)
                bakedData.Add(new Vector2(smoothNormalTS.x, smoothNormalTS.y) );
            }
            
            mesh.SetUVs(targetUVChannel, bakedData);
        }

        private static Vector3[] ComputeSmoothNormals(Vector3[] vertices, Vector3[] normals)
        {
            var vertexCount = vertices.Length;
            var smoothedNormals = new Vector3[vertexCount];

            // Vector3 대신 해시 충돌 방지를 위해 문자열(정밀도 조절)이나 구조체를 키로 사용합니다.
            // 여기서는 간단하게 Float의 한계를 무시하기 위한 내부 래퍼 구조체 대신 IEqualityComparer를 적용하는 방식을 권장하지만, 
            // 간략화를 위해 소수점 아래 3자리까지만 반영하는 Vector3Int를 변형해 사용하는 기법을 씁니다.
            var vertexIndices = new Dictionary<Vector3Int, List<int>>(vertexCount);

            for (var i = 0; i < vertexCount; i++)
            {
                // 1000을 곱해 반올림 처리함으로써 미세한 부동소수점 오차를 무시 (ex: 0.001 단위까지 같으면 같은 위치로 취급)
                Vector3 v = vertices[i];
                Vector3Int key = new Vector3Int(
                    Mathf.RoundToInt(v.x * 1000f),
                    Mathf.RoundToInt(v.y * 1000f),
                    Mathf.RoundToInt(v.z * 1000f)
                );

                if (!vertexIndices.TryGetValue(key, out var indices))
                {
                    indices = new List<int>(4); // 일반적으로 한 점에 모이는 버텍스는 2~4개 내외
                    vertexIndices[key] = indices;
                }

                indices.Add(i);
            }

            // 각 정점 위치별로 Normal을 누적하고 평균 및 정규화
            foreach (var indices in vertexIndices.Values)
            {
                Vector3 accumulatedNormal = Vector3.zero;

                // 1차 순회: 동일 위치의 노멀 합산
                foreach (var index in indices)
                {
                    accumulatedNormal += normals[index];
                }

                // 합산된 노멀 정규화 (길이를 1로)
                accumulatedNormal.Normalize();

                // 2차 순회: 계산된 스무스 노멀을 배열에 할당
                foreach (var index in indices)
                {
                    smoothedNormals[index] = accumulatedNormal;
                }
            }

            return smoothedNormals;
        }
    }
}
