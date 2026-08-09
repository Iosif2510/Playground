Shader "Playground/Raymarching Water"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.35, 0.85, 0.95, 0.55)
        _DeepColor ("Deep Color", Color) = (0.02, 0.18, 0.32, 0.85)
        _ReflectionColor ("Reflection Color", Color) = (0.9, 0.98, 1.0, 1.0)
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 0.8)

        _IsoLevel ("Iso Level", Range(0.01, 1)) = 0.32
        _StepSize ("Target Step Size", Float) = 0.04
        _MaxSteps ("Max Steps", Int) = 192
        _NormalEpsilon ("Normal Epsilon", Float) = 0.06

        _SurfaceAlpha ("Surface Alpha", Range(0, 1)) = 0.7
        _VolumeAlpha ("Volume Alpha", Range(0, 1)) = 0.35
        _Absorption ("Absorption", Float) = 1.4
        _DepthAbsorption ("Depth Absorption", Float) = 0.35

        _FresnelPower ("Fresnel Power", Float) = 4
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.45
        _RefractionStrength ("Refraction Strength", Range(0, 1)) = 0.08
        _SpecularStrength ("Specular Strength", Range(0, 4)) = 1.2
        _SpecularPower ("Specular Power", Float) = 96

        _FoamThreshold ("Foam Threshold", Range(0, 1)) = 0.82
        _FoamStrength ("Foam Strength", Range(0, 4)) = 1
        _FoamWidth ("Foam Width", Float) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "RaymarchWater"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM

            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE3D(_DensityVolume);
            SAMPLER(sampler_DensityVolume);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _ReflectionColor;
                float4 _FoamColor;

                float _IsoLevel;
                float _StepSize;
                int _MaxSteps;
                float _NormalEpsilon;

                float _SurfaceAlpha;
                float _VolumeAlpha;
                float _Absorption;
                float _DepthAbsorption;

                float _FresnelPower;
                float _ReflectionStrength;
                float _RefractionStrength;
                float _SpecularStrength;
                float _SpecularPower;

                float _FoamThreshold;
                float _FoamStrength;
                float _FoamWidth;

                float3 _BoundsMin;
                float3 _BoundsMax;
                float3 _VolumeBoundsMin;
                float3 _VolumeBoundsMax;
                float4 _DensityVolumeSize;
                float4 _DensityVolumeTexelSize;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                output.positionCS =
                    float4(input.positionOS.xy, 0.0, 1.0);

                output.screenPos =
                    ComputeScreenPos(output.positionCS);

                return output;
            }

            bool RayBoxIntersection(
                float3 rayOrigin,
                float3 rayDirection,
                float3 boundsMin,
                float3 boundsMax,
                out float tNear,
                out float tFar)
            {
                float3 invDirection = 1.0 / rayDirection;
                float3 t0 = (boundsMin - rayOrigin) * invDirection;
                float3 t1 = (boundsMax - rayOrigin) * invDirection;

                float3 tMin = min(t0, t1);
                float3 tMax = max(t0, t1);

                tNear = max(max(tMin.x, tMin.y), tMin.z);
                tFar = min(min(tMax.x, tMax.y), tMax.z);

                return tFar >= max(tNear, 0.0);
            }

            float3 WorldToVolumeUv(float3 worldPosition)
            {
                float3 boundsSize =
                    max(_VolumeBoundsMax - _VolumeBoundsMin, 1e-5);

                return (worldPosition - _VolumeBoundsMin) / boundsSize;
            }

            float SampleDensityUv(float3 uvw)
            {
                return SAMPLE_TEXTURE3D(
                    _DensityVolume,
                    sampler_DensityVolume,
                    saturate(uvw)).r;
            }

            float SampleDensityWorld(float3 worldPosition)
            {
                float3 uvw = WorldToVolumeUv(worldPosition);

                if (any(uvw < 0.0) || any(uvw > 1.0))
                    return 0.0;

                return SampleDensityUv(uvw);
            }

            float3 EstimateNormal(float3 worldPosition)
            {
                float e = max(_NormalEpsilon, 1e-4);

                float dx =
                    SampleDensityWorld(worldPosition + float3(e, 0, 0)) -
                    SampleDensityWorld(worldPosition - float3(e, 0, 0));

                float dy =
                    SampleDensityWorld(worldPosition + float3(0, e, 0)) -
                    SampleDensityWorld(worldPosition - float3(0, e, 0));

                float dz =
                    SampleDensityWorld(worldPosition + float3(0, 0, e)) -
                    SampleDensityWorld(worldPosition - float3(0, 0, e));

                float3 gradient = float3(dx, dy, dz);

                if (dot(gradient, gradient) < 1e-8)
                    return float3(0, 1, 0);

                return -normalize(gradient);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 rayOrigin = GetCameraPositionWS();
                float2 screenUv =
                    input.screenPos.xy / input.screenPos.w;

#if UNITY_REVERSED_Z
                float farDepth = 0.0;
#else
                float farDepth = 1.0;
#endif

                float3 farWorldPosition =
                    ComputeWorldSpacePosition(
                        screenUv,
                        farDepth,
                        UNITY_MATRIX_I_VP);

                float3 rayDirection =
                    normalize(farWorldPosition - rayOrigin);

                float tEnter;
                float tExit;

                if (!RayBoxIntersection(
                    rayOrigin,
                    rayDirection,
                    _VolumeBoundsMin,
                    _VolumeBoundsMax,
                    tEnter,
                    tExit))
                {
                    discard;
                }

                float tStart = max(tEnter, 0.0);
                float totalDistance = max(tExit - tStart, 1e-5);
                float targetStepSize = max(_StepSize, 1e-5);
                int requiredSteps =
                    (int)ceil(totalDistance / targetStepSize);

                int maxSteps =
                    clamp(max(_MaxSteps, requiredSteps), 1, 512);

                float stepSize = totalDistance / maxSteps;

                float t = tStart;
                float previousT = t;
                float previousDensity =
                    SampleDensityWorld(rayOrigin + rayDirection * t);

                float3 volumeColor = 0;
                float transmittance = 1.0;

                bool hit = false;
                float hitT = tExit;

                [loop]
                for (int step = 0; step < 512; step++)
                {
                    if (step >= maxSteps || t > tExit)
                        break;

                    float3 samplePosition =
                        rayOrigin + rayDirection * t;

                    float density =
                        SampleDensityWorld(samplePosition);

                    float absorption =
                        density * _Absorption * stepSize;

                    float alpha =
                        1.0 - exp(-absorption);

                    float3 densityColor =
                        lerp(
                            _ShallowColor.rgb,
                            _DeepColor.rgb,
                            saturate(density));

                    volumeColor +=
                        transmittance * alpha * densityColor;

                    transmittance *= 1.0 - alpha;

                    if (density >= _IsoLevel)
                    {
                        hit = true;
                        hitT = t;

                        float lowT = previousT;
                        float highT = t;

                        [unroll]
                        for (int refine = 0; refine < 5; refine++)
                        {
                            float midT = (lowT + highT) * 0.5;
                            float midDensity =
                                SampleDensityWorld(
                                    rayOrigin +
                                    rayDirection * midT);

                            if (midDensity >= _IsoLevel)
                                highT = midT;
                            else
                                lowT = midT;
                        }

                        hitT = highT;
                        break;
                    }

                    if (transmittance < 0.01)
                        break;

                    previousT = t;
                    previousDensity = density;
                    t += stepSize;
                }

                float volumeAlpha =
                    saturate((1.0 - transmittance) * _VolumeAlpha);

                if (!hit)
                {
                    if (volumeAlpha <= 0.001)
                        discard;

                    return half4(volumeColor, volumeAlpha);
                }

                float3 hitPosition =
                    rayOrigin + rayDirection * hitT;

                float3 normal =
                    EstimateNormal(hitPosition);

                float3 viewDirection =
                    normalize(rayOrigin - hitPosition);

                float2 refractionUv =
                    saturate(
                        screenUv +
                        normal.xy * _RefractionStrength * 0.05);

                float3 sceneColor =
                    SampleSceneColor(refractionUv);

                float thickness =
                    max(tExit - hitT, 0.0);

                float depthFactor =
                    saturate(1.0 - exp(-thickness * _DepthAbsorption));

                float3 absorptionTint =
                    lerp(_ShallowColor.rgb, _DeepColor.rgb, depthFactor);

                Light mainLight = GetMainLight();
                float3 lightDirection =
                    normalize(mainLight.direction);

                float diffuse =
                    saturate(dot(normal, lightDirection)) * 0.25;

                float3 halfVector =
                    normalize(lightDirection + viewDirection);

                float specular =
                    pow(
                        saturate(dot(normal, halfVector)),
                        _SpecularPower) *
                    _SpecularStrength *
                    mainLight.shadowAttenuation;

                float fresnel =
                    pow(
                        1.0 - saturate(dot(normal, viewDirection)),
                        _FresnelPower) *
                    _ReflectionStrength;

                float3 reflectedColor =
                    _ReflectionColor.rgb * mainLight.color;

                float3 surfaceColor =
                    sceneColor * absorptionTint;

                surfaceColor =
                    lerp(surfaceColor, reflectedColor, saturate(fresnel));

                surfaceColor +=
                    diffuse * _ShallowColor.rgb +
                    specular * _ReflectionColor.rgb;

                float foamDensity =
                    SampleDensityWorld(
                        hitPosition - normal * max(_FoamWidth, 0.0));

                float foam =
                    saturate(
                        (foamDensity - _FoamThreshold) *
                        _FoamStrength);

                surfaceColor =
                    lerp(
                        surfaceColor,
                        _FoamColor.rgb,
                        foam * _FoamColor.a);

                float3 finalColor =
                    surfaceColor + volumeColor * 0.35;

                float finalAlpha =
                    saturate(max(_SurfaceAlpha, volumeAlpha));

                return half4(finalColor, finalAlpha);
            }

            ENDHLSL
        }
    }
}
