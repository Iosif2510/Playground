using System.Collections.Generic;
using UnityEngine;

namespace Terraforming
{
    public class SimpleTriangleDrawer : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private Mesh mesh;
        
        private List<Vector3> vertices = new ();
        private List<int> triangleIndices = new ();
    
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        private void Start()
        {
            meshFilter = GetComponent<MeshFilter>();
            mesh = new Mesh();
            meshFilter.mesh = mesh;
        
            Draw();
        }

        private void Draw()
        {
            mesh.Clear();
        
            vertices = new()
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
            };
            triangleIndices = new()
            {
                0, 1, 2
            };

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangleIndices, 0);
        
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            meshFilter.mesh = mesh;
        }
    }
}

