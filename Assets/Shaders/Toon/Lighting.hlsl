// Lighting.hlsl
#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

void MainLight_half(float3 WorldPos, out half3 Direction,
                    out half3 Color, out half Attenuation)
{
#if SHADERGRAPH_PREVIEW
    Direction = half3(0.5, 0.5, 0);
    Color = 1;
    Attenuation = 1;
#else
    Light light = GetMainLight(TransformWorldToShadowCoord(WorldPos));
    Direction = light.direction;
    Color = light.color;
    Attenuation = light.shadowAttenuation * light.distanceAttenuation;
#endif
}
#endif