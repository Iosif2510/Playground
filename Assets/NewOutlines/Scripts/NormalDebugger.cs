using System;
using Unity.Mathematics;
using UnityEngine;

namespace NewOutlines
{
    public class NormalDebugger : MonoBehaviour
    {
        private MeshFilter meshFilter;

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
        }
        
        private void OnDrawGizmosSelected()
        {
            meshFilter ??= GetComponent<MeshFilter>();
            Gizmos.color = Color.cyan;
            var sharedMesh = meshFilter.sharedMesh;
            var vertices = sharedMesh.vertices;
            var uv8 = sharedMesh.uv8;
            Debug.Log(vertices.Length);
            for (var i = 0; i < sharedMesh.vertexCount; i++)
            {
                Debug.Log($"Vertex {i}: Position={vertices[i]}");
                
                var uv = uv8[i];
                float z = math.sqrt(1 - math.saturate(math.dot(uv, uv)));
                var unzippedNormal = new Vector3(uv.x, uv.y, z);
                Debug.Log($"Vertex {i}: UV={unzippedNormal}");
                Gizmos.DrawLine(transform.position + vertices[i], transform.position + vertices[i] + unzippedNormal * 0.1f);
            }
            
        }
    }
}