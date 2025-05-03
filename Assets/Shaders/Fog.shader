Shader "Hidden/Fog"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _FogColor ("Fog Color", Color) = (0.5, 0.5, 0.5, 1.0)
        _FogDensity ("Fog Density", Range(0.0, 2.0)) = 0.1 
        _FogStartDistance ("Fog Start Distance", Float) = 0.0
        _FogEndDistance ("Fog End Distance", Float) = 100.0 
        _HeightFogBase ("Height Fog Base Height", Float) = 0.0
        _HeightFogFalloff ("Height Fog Falloff", Float) = 50.0
        [Toggle(USE_LINEAR_FOG)] _UseLinearFog("Use Linear Fog", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "FogPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag


            #pragma shader_feature_local USE_LINEAR_FOG

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 positionWS   : TEXCOORD1;
                float4 projectedPos : TEXCOORD2;
            };

            // Uniforms (match properties)
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            half4 _FogColor;
            float _FogDensity;
            float _FogStartDistance;
            float _FogEndDistance;
            float _HeightFogBase;
            float _HeightFogFalloff;

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);

                output.projectedPos = ComputeScreenPos(output.positionCS);

                return output;
            }

            half CalculateFogFactor(float depth, float3 worldPos)
            {
                float fogFactor = 0.0;

                #if USE_LINEAR_FOG
                    float fogRange = max(0.0001, _FogEndDistance - _FogStartDistance); 
                    fogFactor = saturate((depth - _FogStartDistance) / fogRange);
                #else
                    //fogFactor = 1.0 - exp2(-_FogDensity * _FogDensity * depth * depth * LOG2_E);
                    fogFactor = 1.0 - exp(-_FogDensity * depth);
                    fogFactor = saturate(fogFactor); // Clamp between 0 and 1
                #endif

                float heightFactor = 0.0;
                if (_HeightFogFalloff > 0.0001)
                {
                    float heightDelta = worldPos.y - _HeightFogBase;
                    heightFactor = saturate(heightDelta / _HeightFogFalloff);
                    heightFactor = 1.0 - heightFactor;
                } 
                else 
                {
                    heightFactor = (worldPos.y <= _HeightFogBase) ? 1.0 : 0.0;
                }

                return max(fogFactor, heightFactor);
            }


            half4 Frag(Varyings input) : SV_Target
            {
                half4 sceneColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                float depth = SampleSceneDepth(input.uv);

                float linearEyeDepth = LinearEyeDepth(depth, _ZBufferParams);
                float3 viewDirectionWS = normalize(input.positionWS - _WorldSpaceCameraPos.xyz);
                float3 worldPos = _WorldSpaceCameraPos.xyz + viewDirectionWS * linearEyeDepth;

                half fogAmount = CalculateFogFactor(linearEyeDepth, worldPos);

                half4 finalColor = lerp(sceneColor, _FogColor, fogAmount);

                return finalColor;
            }
            ENDHLSL
        }
    }
    Fallback Off
}