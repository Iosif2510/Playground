using Unity.Mathematics;
using UnityEngine;

namespace Terraforming
{
    public class SimpleDensityField : MonoBehaviour
    {
        private static readonly int GizmoBufferProperty = Shader.PropertyToID("_GizmoBuffer");
        private static readonly int ResolutionProperty = Shader.PropertyToID("_Resolution");
        private static readonly int UnitScaleProperty = Shader.PropertyToID("_UnitScale");
        private static readonly int OriginProperty = Shader.PropertyToID("_Origin");

        [Header("Field Properties")]
        [SerializeField] private int resolution = 16;
        [SerializeField] private float unitSize = 1.0f;
        [SerializeField] private float refreshRate = 0.3f;
        private float timer = 0f;
        
        [SerializeField] private DensityFieldGenerator fieldGenerator;
        
        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Mesh gizmoMesh;
        [SerializeField] private float gizmoSize = 0.1f;
        [SerializeField] private Material gizmoMaterial;
        
        private Material instantiatedGizmoMaterial;
        
        private Bounds bounds;
        private ComputeBuffer gizmoBuffer;
        private ComputeBuffer argsBuffer;
        
        public float[] Field { get; private set; }
        public int Resolution => resolution;
        public float UnitSize => unitSize;

        public void InitializeField()
        {
            timer = 0;
            var count = resolution * resolution * resolution;
            Field = new float[count];
            
            // CreateSimpleSphere(transform.position);
            fieldGenerator.GenerateField(Field, transform.position, resolution, unitSize);
            InitializeGizmo();
        }

        private void InitializeGizmo()
        {
            int count = resolution * resolution * resolution;
            bounds = new Bounds(transform.position, Vector3.one * (resolution * unitSize * 10));
            
            gizmoBuffer = new ComputeBuffer(count, sizeof(float));
            gizmoBuffer.SetData(Field);
            
            instantiatedGizmoMaterial = Instantiate(gizmoMaterial);
            
            instantiatedGizmoMaterial.EnableKeyword("PROCEDURAL_INSTANCING_ON");
            instantiatedGizmoMaterial.SetBuffer(GizmoBufferProperty, gizmoBuffer);

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
            if (drawGizmos) Graphics.DrawMeshInstancedIndirect(gizmoMesh, 0, instantiatedGizmoMaterial, bounds, argsBuffer);
        }
        
        public static float3 GetWorldPositionFromIndex(int index, int resolution, float unitSize, float3 origin)
        {
            var x = index % resolution;
            var y = (index / resolution) % resolution;
            var z = index / (resolution * resolution);
            var center = new float3(resolution / 2f, resolution / 2f, resolution / 2f);
            var pos = (new float3(x, y, z) - center) * unitSize + origin;
            
            return pos;
        }

        public float3 GetWorldPositionFromIndex(int index)
        {
            return GetWorldPositionFromIndex(index, resolution, unitSize, transform.position);
        }

        private void RefreshGizmo()
        {
            if (!drawGizmos) return;
            instantiatedGizmoMaterial.SetInteger(ResolutionProperty, resolution);
            instantiatedGizmoMaterial.SetFloat(UnitScaleProperty, unitSize);
            instantiatedGizmoMaterial.SetVector(OriginProperty, transform.position);
            gizmoBuffer?.SetData(Field);
        }

        private void OnDestroy()
        {
            gizmoBuffer?.Release();
            argsBuffer?.Release();
        }
    }
}