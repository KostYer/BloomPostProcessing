Shader "Hidden/LayerAwareBloom/HdrTonemapping"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "HDR Tonemapping"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            // Shared color math only. No built-in post-processing shaders, LUTs, or Volume settings.
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            float _LayerTonemappingMode;

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
                float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                #if UNITY_COLORSPACE_GAMMA
                    scene.rgb = SRGBToLinear(scene.rgb);
                #endif
                float3 color = max(scene.rgb, 0.0);
                if (_LayerTonemappingMode > 1.5)
                    color = AcesTonemap(unity_to_ACES(color));
                else
                    color = NeutralTonemap(color);

                // This feature targets SDR output. Preserve scene alpha, not HDR display metadata.
                scene.rgb = saturate(color);
                #if UNITY_COLORSPACE_GAMMA
                    scene.rgb = LinearToSRGB(scene.rgb);
                #endif
                return scene;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
