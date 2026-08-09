Shader "Playground/Fluid Particle Indirect"
{
    Properties
    {
        _ParticleColor ("Particle Color", Color) = (1,1,1,1)
        _ParticleRenderSize ("Particle Render Size", Float) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // RenderMeshIndirect에 필요한 command / instance access 함수.
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"

            struct Particle
            {
                float2 position;
                float2 velocity;
            };

            StructuredBuffer<Particle> _Particles;

            CBUFFER_START(UnityPerMaterial)
                float4 _ParticleColor;
                float _ParticleRenderSize;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                // 반드시 vertex shader 초기에 호출.
                InitIndirectDrawArgs(0);

                uint instanceID = GetIndirectInstanceID(input.instanceID);

                Particle particle = _Particles[instanceID];

                // mesh local vertex를 particle 중심 기준으로 scale.
                float3 positionWS =
                    float3(
                        particle.position.x,
                        particle.position.y,
                        0.0)
                    + input.positionOS * _ParticleRenderSize;

                output.positionCS =
                    TransformWorldToHClip(positionWS);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _ParticleColor;
            }

            ENDHLSL
        }
    }
}