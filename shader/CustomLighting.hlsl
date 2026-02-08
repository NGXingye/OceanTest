#ifndef LIGHTING_INCLUDED
#define LIGHTING_INCLUDED

#include "PlaneReflection.hlsl"

half3 SamplerReflections(half3 normalWS,half3 viewDirectionWS,half2 screenUV)
{
    half3 reflction=0;

    half2 distorUV=screenUV+normalWS.xz*half2(0.02,0.15);
    half3 planeReflection=SamplerPlaneReflections(distorUV).rgb;
    reflction+=planeReflection;
    
    return reflction;
}
#endif