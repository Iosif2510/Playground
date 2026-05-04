using UnityEngine;
using UnityEngine.Video;

namespace BadApple
{
    public class CubeGridVideoPlayer : MonoBehaviour
    {
        private static readonly int VideoTexture = Shader.PropertyToID("_VideoTexture");
        private static readonly int CubeBuffer = Shader.PropertyToID("_CubeBuffer");
        private static readonly int GridSize = Shader.PropertyToID("_GridSize");
        ComputeBuffer cubeBuffer;

        [SerializeField] private VideoPlayer videoPlayer;
        [Header("Resources")]
        [SerializeField] private RenderTexture videoRenderTexture;
        [SerializeField] private ComputeShader computeShader;
        [Header("Cube Grid Property")] 
        [SerializeField] private Vector2Int cubeGridSize;
        [SerializeField] private Mesh cubeMesh;
        [SerializeField] private Material instancingMaterial;
        [SerializeField] private Bounds bounds;
        
        private int CubeCount => cubeGridSize.x * cubeGridSize.y;
        
        private void Start()
        {
            cubeBuffer = new ComputeBuffer(CubeCount, sizeof(float) * 7); // float3(pos) + float4(color)
        }

        private void OnGUI()
        {
            if (GUI.Button(new Rect(20,20 ,80,40), "Play"))
            {
                videoPlayer.Play();
            }
        }
        
        private void Update() {
            // 컴퓨트 셰이더 실행
            var kernel = computeShader.FindKernel("CSMain");
            computeShader.SetTexture(kernel, VideoTexture, videoRenderTexture);
            computeShader.SetBuffer(kernel, CubeBuffer, cubeBuffer);
            computeShader.SetInts(GridSize, cubeGridSize.x, cubeGridSize.y);
            computeShader.Dispatch(kernel, Mathf.CeilToInt(cubeGridSize.x / 8f), Mathf.CeilToInt(cubeGridSize.y / 8f), 1);

            // 재질에 버퍼 전달
            instancingMaterial.EnableKeyword("PROCEDURAL_INSTANCING_ON");
            instancingMaterial.SetBuffer(CubeBuffer, cubeBuffer);
            
            var renderParams = new RenderParams(instancingMaterial);
            renderParams.worldBounds = bounds;
            renderParams.layer = gameObject.layer;
            renderParams.receiveShadows = true;
            renderParams.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            
            Graphics.RenderMeshPrimitives(renderParams, cubeMesh, 0, CubeCount);
        }

        private void OnDestroy()
        {
            cubeBuffer.Release();
        }
    }
}