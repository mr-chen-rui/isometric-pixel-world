Shader "Custom/IndirectBillboardGrass"
{
    Properties
    {
        _MainTex ("Grass Albedo (RGBA)", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _BaseColor ("Root Tint", Color) = (0.15, 0.35, 0.1, 1)
        _TipColor ("Tip Tint", Color) = (0.45, 0.75, 0.2, 1)
        _WindSpeed ("Wind Speed", Float) = 1.5
        _WindStrength ("Wind Strength", Float) = 0.25
        _WindFrequency ("Wind Frequency", Float) = 0.4
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "TransparentCutout" 
            "Queue" = "AlphaTest" 
            "RenderPipeline" = "UniversalPipeline" 
        }

        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct GrassInstance
            {
                float3 position;
                float rotationY;
                float3 normal;
                float2 scale;
            };

            StructuredBuffer<GrassInstance> _GrassInstances;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Cutoff;
                float4 _BaseColor;
                float4 _TipColor;
                float _WindSpeed;
                float _WindStrength;
                float _WindFrequency;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                GrassInstance inst = _GrassInstances[input.instanceID];

                // 1. Scale quad locally
                float3 pos = input.positionOS.xyz;
                pos.x *= inst.scale.x;
                pos.y *= inst.scale.y;

                // 2. Cylindrical (Y-Axis) Billboarding toward the camera
                // Use the blended terrain normal as the blade's up axis
                float3 up = normalize(inst.normal);

                // Vector from grass root to camera
                float3 toCamera = _WorldSpaceCameraPos - inst.position;
                
                // Project onto the plane perpendicular to 'up' so the quad doesn't tilt backward when looking down
                float3 forward = toCamera - up * dot(toCamera, up);
                float forwardLen = length(forward);
                forward = (forwardLen > 0.0001) ? (forward / forwardLen) : float3(0, 0, 1);

                // Compute right vector to form an orthonormal basis
                float3 right = normalize(cross(up, forward));

                // Transform local quad vertex into camera-facing world space
                float3 worldPos = inst.position + (right * pos.x) + (up * pos.y);

                // 3. Apply world-space wind AFTER billboarding (weighted by uv.y^2 so roots stay planted)
                float windPhase = _Time.y * _WindSpeed + (inst.position.x + inst.position.z) * _WindFrequency;
                float windWave = sin(windPhase) * cos(windPhase * 0.7);
                float bendFactor = input.uv.y * input.uv.y * _WindStrength;
                worldPos.xz += float2(windWave, windWave * 0.5) * bendFactor;

                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = up;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(texColor.a - _Cutoff);

                half3 tint = lerp(_BaseColor.rgb, _TipColor.rgb, input.uv.y);
                half3 albedo = texColor.rgb * tint;

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half NdotL = saturate(dot(input.normalWS, mainLight.direction) * 0.5 + 0.5);
                half3 ambient = SampleSH(input.normalWS);

                half3 lighting = mainLight.color * (NdotL * mainLight.shadowAttenuation) + ambient;
                return half4(albedo * lighting, 1.0);
            }
            ENDHLSL
        }
    }
}
