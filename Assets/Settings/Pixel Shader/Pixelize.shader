Shader "Custom/PixelateFullscreen"
{
    Properties
    {
        _DownsampleScale ("Downsample Scale", Range(1, 64)) = 4
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off 
        Cull Off 
        ZTest Always

        Pass
        {
            Name "PointDownsampleUpsamplePass"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            // _BlitTexture, _BlitTexture_TexelSize, and sampler_PointClamp are provided by Blit.hlsl
            float _DownsampleScale;

            half4 Frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;

                // _BlitTexture_TexelSize.zw contains the full screen width and height in pixels
                float2 screenRes = _BlitTexture_TexelSize.zw;

                // Calculate the target downsampled resolution
                float scale = max(1.0, _DownsampleScale);
                float2 lowRes = floor(screenRes / scale);

                // Snap UVs to the center of each low-res pixel cell
                float2 snappedUV = (floor(uv * lowRes) + 0.5) / lowRes;

                // Sample the screen texture using point filtering
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, snappedUV);

                return color;
            }
            ENDHLSL
        }
    }
}
