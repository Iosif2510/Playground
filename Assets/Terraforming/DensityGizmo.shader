Shader "Custom/DensityGizmo"
{
    Properties
    {
        _Size("Gizmo Size", Float) = 0.1
        _Alpha("Alpha", Range(0, 1)) = 1
        _UnitScale("Unit Scale", Float) = 1
        _Dimensions("Dimensions", Vector) = (1, 1, 1, 0)
        _CenterOffset("Center Offset", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            
            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment frag
            #pragma instancing_options procedural:setup
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
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
            float3 _Dimensions;
            float3 _CenterOffset;
            half _UnitScale;
            float3 _Origin;
            
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
            StructuredBuffer<float> _GizmoBuffer;

            float3 GetInstancePosition(uint instanceID)
            {
                int dimX = (int)_Dimensions.x;
                int dimY = (int)_Dimensions.y;
                
                int x = instanceID % dimX;
                int y = (instanceID / dimX) % dimY;
                int z = instanceID / (dimX * dimY);
                
                float3 pos = (float3(x, y, z) - _CenterOffset) * _UnitScale + _Origin;
                return pos;
            }

            void setup()
            {
                // C#에서 넘긴 구조체를 바탕으로 현재 인스턴스의 위치 정보를 버퍼에서 가져옵니다.
                float3 pos = GetInstancePosition(unity_InstanceID);
                
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
                OUT.density = _GizmoBuffer[unity_InstanceID];
#else
                OUT.density = 0;
#endif
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                
                half3 color = lerp(half3(1, 0, 0), half3(0, 1, 0), clamp(IN.density + 0.5, 0, 1));
                
                return half4(color, _Alpha);
            }
            ENDHLSL
        }
    }
}
