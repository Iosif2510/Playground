Shader "Custom/Custom Channel Outline"
{
    Properties
    {
        _OutlineColor("Outline Color", Color) = (0,0,0,1)
        _OutlineSize("Outline Size", Range(0.0, 10)) = 1
        _OutlineMinMaxSize("Outline Min Max Size", Vector, 2) = (0.01, 0.05, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" "Queue" = "Geometry+1" }
            
            Name "Outline"
            Cull Front
//            ZTest LEqual
//            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile _ DOTS_INSTANCING_ON
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv8        : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)

            half4 _OutlineColor;
            half _OutlineSize;
            half4 _OutlineMinMaxSize;
            
            CBUFFER_END
            
            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(UserPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _OutlineColor)
                UNITY_DOTS_INSTANCED_PROP(float,  _OutlineSize)
                UNITY_DOTS_INSTANCED_PROP(float4, _OutlineMinMaxSize)
            UNITY_DOTS_INSTANCING_END(UserPropertyMetadata)

            #define _OutlineColor      UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _OutlineColor)
            #define _OutlineSize       UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float,  _OutlineSize)
            #define _OutlineMinMaxSize UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _OutlineMinMaxSize)
            #endif

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 smoothNormalTS = float3(IN.uv8.xy, 0.0);
                smoothNormalTS.z = sqrt(saturate(1.0 - dot(smoothNormalTS.xy, smoothNormalTS.xy)));

                float tangentSign = IN.tangentOS.w * GetOddNegativeScale();
                float3 binormalOS = cross(IN.normalOS, IN.tangentOS.xyz) * tangentSign;
                float3x3 tbn = float3x3(IN.tangentOS.xyz, binormalOS, IN.normalOS);

                float3 smoothNormalOS = normalize(mul(smoothNormalTS, tbn));
                float3 normalWS = TransformObjectToWorldNormal(smoothNormalOS);
                normalWS = normalize(normalWS);
                // float3 normalVS = mul((float3x3)GetWorldToViewMatrix(), normalWS);

                float4 clipPos = TransformObjectToHClip(IN.positionOS.xyz);

                float2 clipNormal = mul((float2x2)GetWorldToHClipMatrix(), normalWS);
                // clipNormal = normalize(clipNormal);

                float outlineSize = clamp(_OutlineSize * clipPos.w, _OutlineMinMaxSize.x, _OutlineMinMaxSize.y);

                float2 offset = clipNormal * outlineSize;
                clipPos.xy += offset * 2.0;

                // clipPos.z -= 0.002 * clipPos.w;
                OUT.positionHCS = clipPos;
                return OUT;
            }
            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}