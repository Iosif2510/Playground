Shader "Custom/DensityGizmo"
{
    Properties
    {
        _Size("Gizmo Size", Float) = 0.1
        _Alpha("Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment frag
            #pragma instancing_options procedural:setup
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct GizmoData {
                float3 position;
                float density;
            };

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float density : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            half _Alpha;
            half _Size;
            
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
            StructuredBuffer<GizmoData> _GizmoBuffer;

            void setup()
            {
                // C#에서 넘긴 구조체를 바탕으로 현재 인스턴스의 위치 정보를 버퍼에서 가져옵니다.
                float3 pos = _GizmoBuffer[unity_InstanceID].position;
                
                // TransformObjectToHClip이 동작하도록 위치 오프셋과 스케일을 포함한 TRS 행렬을 재구성합니다.
                float4x4 dataMatrix = float4x4(
                    _Size, 0, 0, pos.x,
                    0, _Size, 0, pos.y,
                    0, 0, _Size, pos.z,
                    0, 0, 0, 1
                );

                unity_ObjectToWorld = dataMatrix;

                // 역행렬 계산 (1 / _Size)
                float invSize = 1.0f / _Size;
                float4x4 invMatrix = float4x4(
                    invSize, 0, 0, -pos.x * invSize,
                    0, invSize, 0, -pos.y * invSize,
                    0, 0, invSize, -pos.z * invSize,
                    0, 0, 0, 1
                );

                unity_WorldToObject = invMatrix;
            }
#endif

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                OUT.density = _GizmoBuffer[unity_InstanceID].density;
#else
                OUT.density = 0;
#endif
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                // 음수일 경우 0.0 으로, 양수일 경우 1.0 으로 시각화 (임시 설정)
                half d = clamp(IN.density, 0, 1);
                
                return half4(d, d, d, _Alpha);
            }
            ENDHLSL
        }
    }
}
