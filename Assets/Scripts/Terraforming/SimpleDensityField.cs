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
        
        [Header("Shape Properties")]
        [SerializeField] private float radius = 3f;
        [SerializeField] private float2 period = new (1f, 1f);
        
        [Header("Debug")]
        [SerializeField] private Mesh gizmoMesh;
        [SerializeField] private float gizmoSize = 0.1f;
        [SerializeField] private Material gizmoMaterial;
        
        private DensityFieldGenerator fieldGenerator;
        
        private Bounds bounds;
        private ComputeBuffer gizmoBuffer;
        private ComputeBuffer argsBuffer;
        
        public FieldData[] Field { get; private set; }
        public int Resolution => resolution;
        public float UnitSize => unitSize;

        public void InitializeField()
        {
            timer = 0;
            var count = resolution * resolution * resolution;
            Field = new FieldData[count];
            
            // CreateSimpleSphere(transform.position);
            fieldGenerator = new SphereGenerator(radius);
            fieldGenerator = new PerlinLandscapeGenerator(period);
            // fieldGenerator.GenerateField(Field, transform.position, resolution, unitSize);
            InitializeGizmo();
        }

        private void InitializeGizmo()
        {
            int count = resolution * resolution * resolution;
            bounds = new Bounds(transform.position, Vector3.one * (resolution * unitSize * 10));
            
            gizmoBuffer = new ComputeBuffer(count, sizeof(float) * 4);
            gizmoBuffer.SetData(Field);
            
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
                fieldGenerator.GenerateField(Field, transform.position, resolution, unitSize);
                RefreshGizmo();
                timer -= refreshRate;
            }

            timer += Time.deltaTime;
                
            Graphics.DrawMeshInstancedIndirect(gizmoMesh, 0, gizmoMaterial, bounds, argsBuffer);
        }

        private float3 GetPointWorldPosition(float index)
        {
            return new float3(index % resolution * unitSize, index / resolution % resolution * unitSize, index / resolution / resolution * unitSize);
        }

        private void RefreshGizmo()
        {
            gizmoBuffer?.SetData(Field);
        }

        private void OnDestroy()
        {
            gizmoBuffer?.Release();
            argsBuffer?.Release();
        }
    }
}