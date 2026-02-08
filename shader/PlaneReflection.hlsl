#ifndef PLANE_REFLECTIONS_INCLUDED
#define PLANE_REFLECTIONS_INCLUDED

TEXTURE2D(_ReflectionTex);
SAMPLER(sampler_ReflectionTex);
half4 SamplerPlaneReflections(float2 uv)
{
    return SAMPLE_TEXTURE2D(_ReflectionTex,sampler_ReflectionTex,uv);
}

#endif