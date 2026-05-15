using UnityEngine;

namespace Terraforming
{
    public class SDFGenerator : MonoBehaviour
    {
        private static readonly int ResolutionKey = Shader.PropertyToID("resolution");
        private static readonly int SphereRadiusKey = Shader.PropertyToID("sphereRadius");
        private static readonly int ResultKey = Shader.PropertyToID("Result");
        [SerializeField] private ComputeShader sdfComputeShader;
        
        [Range(0.1f, 1.0f)]
        public float sphereRadius = 0.5f;

        [SerializeField] private RenderTexture sdfTexture3D;

        // private void Start()
        // {
        //     GenerateSDF();
        // }
        
        public RenderTexture GenerateSDF(int resolution)
        {
            // 3D RenderTexture 생성 (SDF는 실수형 값이므로 RFloat 사용)
            sdfTexture3D = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RFloat)
            {
                name = "SDFTexture3D",
                dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
                volumeDepth = resolution, // Z축 깊이 설정
                enableRandomWrite = true // Compute Shader에서 RWTexture로 쓰기 위해 필수
            };
            sdfTexture3D.Create();

            // Compute Shader 커널 ID 찾기
            var kernelHandle = sdfComputeShader.FindKernel("CSMain");

            // Shader 변수 세팅
            sdfComputeShader.SetFloat(ResolutionKey, resolution);
            sdfComputeShader.SetFloat(SphereRadiusKey, sphereRadius);
            sdfComputeShader.SetTexture(kernelHandle, ResultKey, sdfTexture3D);

            // 스레드 그룹 크기 계산 (8x8x8 단위이므로 해상도를 8로 나눔)
            var threadGroups = Mathf.CeilToInt(resolution / 8.0f);
        
            // Compute Shader 실행
            sdfComputeShader.Dispatch(kernelHandle, threadGroups, threadGroups, threadGroups);
        
            Debug.Log("구형 SDF 3D Texture 생성 완료!");
            
            return sdfTexture3D;
        }

        private void OnDestroy()
        {
            // 메모리 해제
            if (sdfTexture3D != null)
            {
                sdfTexture3D.Release();
            }
        }
    }
}