Shader "Playground/Fluid Particle Indirect 3D"
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

            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"

            struct Particle
            {
                float3 position;
                float3 velocity;
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

                InitIndirectDrawArgs(0);

                uint instanceID =
                    GetIndirectInstanceID(input.instanceID);

                Particle particle = _Particles[instanceID];

                float3 positionWS =
                    particle.position +
                    input.positionOS * _ParticleRenderSize;

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
