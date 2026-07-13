Shader "Custom/DensityField"
{
    Properties
    {
        _ZeroCol ("Zero Pressure Color", Color) = (1,1,1,1)
        _PosCol ("Positive Pressure Color", Color) = (1,0,0,1)
        _NegCol ("Negative Pressure Color", Color) = (0,0,1,1)
        _ColorSpread ("Color Spread (Gradation)", Range(0.01, 3.0)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0; 
            };

            int _ParticleCount;
            float4 _ParticlePositions[1000]; 
            float _SmoothingRadius;
            float _TargetDensity;
            float _PressureMultiplier;
            float _ParticleMass;

            float4 _ZeroCol;
            float4 _PosCol;
            float4 _NegCol;
            float _ColorSpread;

            float SmoothingFunction(float dst, float radius)
            {
                if (dst >= radius) return 0;

                // [Smooth Kernel]
                // float volume = 3.1415926535 * pow(radius, 8.0) / 4.0;
                // float value = max(0.0, radius * radius - dst * dst);
                // return value * value * value / volume;

                // [Spiky Kernel]
                float volume = 3.1415926535 * pow(radius, 4.0) / 6.0;
                return pow(radius - dst, 2.0) / volume;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz; 
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float density = 0;
                
                for(int j = 0; j < _ParticleCount; j++)
                {
                    float dst = distance(i.worldPos.xy, _ParticlePositions[j].xy);
                    density += _ParticleMass * SmoothingFunction(dst, _SmoothingRadius);
                }

                float pressure = (density - _TargetDensity) * _PressureMultiplier;
                float4 finalColor = _ZeroCol;
                
                float gradientRange = _PressureMultiplier * _ColorSpread;
                
                if (pressure > 0) 
                {
                    float t = saturate(pressure / gradientRange);
                    finalColor = lerp(_ZeroCol, _PosCol, t);
                }
                else if (pressure < 0) 
                {
                    float t = saturate(-pressure / gradientRange);
                    finalColor = lerp(_ZeroCol, _NegCol, t);
                }

                return finalColor;
            }
            ENDCG
        }
    }
}