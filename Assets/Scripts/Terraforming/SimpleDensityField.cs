using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    public struct FieldData
    {
        public float3 position;
        public float density;
    }

    public class SimpleDensityField : MonoBehaviour
    {
        private static readonly int GizmoBufferProperty = Shader.PropertyToID("_GizmoBuffer");
        
        [Header("Field Properties")]
        [SerializeField] private int resolution = 16;
        [SerializeField] private float unitSize = 1.0f;
        [SerializeField] private float refreshRate = 0.3f;
        private float timer = 0f;
        
        [Header("Sphere Shape Properties")]
        [SerializeField] private float radius = 3f;
        
        [Header("Debug")]
        [SerializeField] private Mesh gizmoMesh;
        [SerializeField] private float gizmoSize = 0.1f;
        [SerializeField] private Material gizmoMaterial;
        
        private Bounds bounds;
        private ComputeBuffer gizmoBuffer;
        private ComputeBuffer argsBuffer;
        
        public FieldData[] DensityField { get; private set; }
        public int Resolution => resolution;
        public float UnitSize => unitSize;

        public void InitializeField()
        {
            int count = resolution * resolution * resolution;
            DensityField = new FieldData[count];
            
            CreateSimpleSphere(transform.position);
            InitializeGizmo();
        }

        private void InitializeGizmo()
        {
            int count = resolution * resolution * resolution;
            bounds = new Bounds(transform.position, Vector3.one * (resolution * unitSize * 10));
            
            gizmoBuffer = new ComputeBuffer(count, sizeof(float) * 4);
            gizmoBuffer.SetData(DensityField);
            
            gizmoMaterial.EnableKeyword("PROCEDURAL_INSTANCING_ON");
            gizmoMaterial.SetBuffer(GizmoBufferProperty, gizmoBuffer);

            uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
            if (gizmoMesh != null)
            {
                args[0] = gizmoMesh.GetIndexCount(0);
                args[1] = (uint)count;
                args[2] = gizmoMesh.GetIndexStart(0);
                args[3] = gizmoMesh.GetBaseVertex(0);
            }
            
            argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            argsBuffer.SetData(args);
        }

        private void Update()
        {
            if (gizmoBuffer == null || argsBuffer == null) return;
            if (timer > refreshRate)
            {
                CreateSimpleSphere(transform.position);
                timer -= refreshRate;
            }

            timer += Time.deltaTime;
                
            Graphics.DrawMeshInstancedIndirect(gizmoMesh, 0, gizmoMaterial, bounds, argsBuffer);
        }
        
        private void CreateSimpleSphere(float3 position)
        {
            for (var i = 0; i < resolution * resolution * resolution; i++)
            {
                var x = i % resolution;
                var y = (i / resolution) % resolution;
                var z = i / (resolution * resolution);
                var center = new float3(resolution / 2f, resolution / 2f, resolution / 2f);
                var pos = (new float3(x, y, z) - center) * unitSize + position;
                
                DensityField[i].position = pos;
                DensityField[i].density = math.length(pos - position) - radius;
            }

            gizmoBuffer?.SetData(DensityField);
        }

        private void OnDestroy()
        {
            gizmoBuffer?.Release();
            argsBuffer?.Release();
        }
    }
}