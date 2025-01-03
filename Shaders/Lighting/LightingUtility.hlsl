#ifndef LIGHTING_UTILITY_INCLUDE
#define LIGHTING_UTILITY_INCLUDE

#if defined(UNIVERSAL_LIGHTING_INCLUDED)
    #include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"
    #if (SHADERPASS != SHADERPASS_FORWARD)
        #undef REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR
    #endif
#endif

void GetMainLightData_half(
    float3 positionWS, 
    out half3 direction, 
    out half3 color, 
    out half shadowAttenuation)
{
#if defined(UNIVERSAL_LIGHTING_INCLUDED)

    // GetMainLight defined in Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl
    half4 shadowCoord = TransformWorldToShadowCoord(positionWS);

#if !defined (LIGHTMAP_ON)
    half4 shadowMask = unity_ProbesOcclusion;
#else
    half4 shadowMask = half4(1, 1, 1, 1);
#endif

    Light mainLight = GetMainLight(shadowCoord, positionWS, shadowMask);// GetMainLight();// GetMainLight(shadowCoord, positionWS, shadowMask);

    //mainLight.shadowAttenuation = MainLightRealtimeShadow(shadowCoord);

    direction = mainLight.direction;
    color = mainLight.color * mainLight.distanceAttenuation;
    shadowAttenuation = mainLight.shadowAttenuation;

#elif defined(HD_LIGHTING_INCLUDED) 
    // ToDo: make it work for HDRP (check define above)
    // Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightDefinition.cs.hlsl
    // if (_DirectionalLightCount > 0)
    // {
    //     DirectionalLightData light = _DirectionalLightDatas[0];
    //     lightDir = -light.forward.xyz;
    //     color = light.color;
    //     ......
#else
    direction = half3(-0.3, -0.8, 0.6);
    color = half3(1, 0, 0);
    shadowAttenuation = 1.0f;
#endif
}

#endif