Shader "OceanQuest/OceanWater"
{
     Properties
    {
   
        [Space(10)]
        [KeywordEnum(ManualArray, ProceduralFBM)] _WaveMode ("Wave Algorithm", Float) = 1

        [Header(Manual Array Settings)]
        _ManualWaveCount ("Wave Count", Int) = 4
     
        [Header(Procedural FBM Settings)]
        _ProcIterations ("Iterations", Range(1, 16)) = 8
        _BaseWavelength ("Base Wavelength", Float) = 20.0
        _Lacunarity ("Lacunarity (Frequency)", Range(1.0, 3.0)) = 1.48
        _Gain ("Gain (Amplitude)", Range(0.0, 1.0)) = 0.5
        _Steepness ("Steepness", Range(0.0, 2.0)) = 1.0
        _Speed ("Speed Multiplier", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline" = "UniversalPipeline" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            //Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // 启用关键字以切换算法
            #pragma shader_feature _WAVEMODE_MANUALARRAY _WAVEMODE_PROCEDURALFBM

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #include "WaterWaves.hlsl"
            #include "CustomLighting.hlsl"
           

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                //float4 positionNDC   :TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShallowColor;
                half _Smoothness;
                half _FresnelPower;

                // Procedural Params
                int _ProcIterations;
                float _BaseWavelength;
                float _Lacunarity;
                float _Gain;
                float _Steepness;
                float _Speed;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // 1. 获取世界坐标
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                
                // 2. 计算波浪
                WaveOutput waveOut;
                
                #if defined(_WAVEMODE_MANUALARRAY)
                    // 使用第一种算法
                    waveOut = CalculateManualGerstnerWaves(positionWS, 1.0);
                #else
                    // 使用第二种算法 (FBM)
                    waveOut = CalculateProceduralGerstnerWaves(
                        positionWS, 
                        _ProcIterations, 
                        _BaseWavelength, 
                        _Lacunarity, 
                        _Gain, 
                        _Steepness, 
                        _Speed,
                        1.0
                    );
                #endif

                // 3. 应用偏移
                float3 displacement=0;
                displacement += waveOut.positionOffset;
                output.positionWS=positionWS+displacement;
              
                // 4. 应用法线
                output.normalWS = waveOut.normal;

                // 5. 转换到裁剪空间
              float3 positionOS=TransformWorldToObject(output.positionWS);
              VertexPositionInputs positionInputs=GetVertexPositionInputs(positionOS);
              output.positionHCS=positionInputs.positionCS;
              //output.positionNDC=positionInputs.positionNDC;
                
            return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                //half2 screenUV=input.postionNDC.xy/positionNDC.W;
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionHCS);

                float3 N=normalize(input.normalWS);
                float3 V=normalize(_WorldSpaceCameraPos-input.positionWS);

                half3 reflction=SamplerReflections(N,V,screenUV);
                return half4(reflction,1);
            }
            ENDHLSL
        }
    }
}