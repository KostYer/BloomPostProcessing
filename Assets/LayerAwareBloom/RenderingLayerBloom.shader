Shader "Hidden/LayerAwareBloom/RenderingLayerBloom"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off

        // URP 17.3 Bloom.shader: shared parameters and HDR encoding, kept inline for comparison.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float4 _Params; // scatter, clamp, linear threshold, threshold knee
        #define Scatter       _Params.x
        #define ClampMax      _Params.y
        #define Threshold     _Params.z
        #define ThresholdKnee _Params.w

        half4 EncodeHDR(half3 color)
        {
        #if UNITY_COLORSPACE_GAMMA
            color = sqrt(color); // linear to γ
        #endif

            return half4(color, 1.0);
        }

        half3 DecodeHDR(half4 data)
        {
            half3 color = data.xyz;

        #if UNITY_COLORSPACE_GAMMA
            color *= color; // γ to linear
        #endif

            return color;
        }

        TEXTURE2D_X(_LayerBloomTexture0);
        TEXTURE2D_X(_LayerBloomTexture1);
        TEXTURE2D_X(_LayerBloomTexture2);
        float4 _LayerBloomWeight0; // Linear tint RGB multiplied by group intensity.
        float4 _LayerBloomWeight1;
        float4 _LayerBloomWeight2;

        half3 CombinedBloom(float2 uv)
        {
            half3 a = DecodeHDR(SAMPLE_TEXTURE2D_X(_LayerBloomTexture0, sampler_LinearClamp, uv));
            half3 b = DecodeHDR(SAMPLE_TEXTURE2D_X(_LayerBloomTexture1, sampler_LinearClamp, uv));
            half3 c = DecodeHDR(SAMPLE_TEXTURE2D_X(_LayerBloomTexture2, sampler_LinearClamp, uv));
            return a * _LayerBloomWeight0.rgb + b * _LayerBloomWeight1.rgb + c * _LayerBloomWeight2.rgb;
        }
        ENDHLSL

        Pass
        {
            Name "Visible Rendering Layer Mask"
            ZWrite Off
            ZTest Equal
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(1, 1, 1, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Fullscreen Bloom Preview"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPreview
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _PreviewSource;

            half4 FragPreview(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
                half3 color = _PreviewSource > 0.5
                    ? SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb : CombinedBloom(uv);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // Passes 2-5: the same URP Gaussian bloom math, now visible in this shader.
        Pass
        {
            Name "Bloom Prefilter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPrefilter
            #pragma multi_compile_local_fragment _ _BLOOM_HQ
            #pragma multi_compile_fragment _ _ENABLE_ALPHA_OUTPUT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DynamicScalingClamping.hlsl"

            half3 SamplePrefilter(float2 uv,  float2 offset)
            {
                float2 texelSize = _BlitTexture_TexelSize.xy;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * offset);
                #if _ENABLE_ALPHA_OUTPUT
                    // When alpha is enabled, regions with zero alpha should not generate any bloom / glow. Therefore we pre-multipy the color with the alpha channel here and the rest
                    // of the computations remain float3. Still, when bloom is applied to the final image, bloom will still be spread on regions with zero alpha (see UberPost.compute)
                    color.xyz *= color.w;
                #endif
                return color.xyz;
            }

            half4 FragPrefilter(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

            #if defined(SUPPORTS_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
                UNITY_BRANCH if (_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
                {
                    uv = RemapFoveatedRenderingLinearToNonUniform(uv);
                }
            #endif

            #if _BLOOM_HQ
                half3 A = SamplePrefilter(uv, float2(-1.0, -1.0));
                half3 B = SamplePrefilter(uv, float2( 0.0, -1.0));
                half3 C = SamplePrefilter(uv, float2( 1.0, -1.0));
                half3 D = SamplePrefilter(uv, float2(-0.5, -0.5));
                half3 E = SamplePrefilter(uv, float2( 0.5, -0.5));
                half3 F = SamplePrefilter(uv, float2(-1.0,  0.0));
                half3 G = SamplePrefilter(uv, float2( 0.0,  0.0));
                half3 H = SamplePrefilter(uv, float2( 1.0,  0.0));
                half3 I = SamplePrefilter(uv, float2(-0.5,  0.5));
                half3 J = SamplePrefilter(uv, float2( 0.5,  0.5));
                half3 K = SamplePrefilter(uv, float2(-1.0,  1.0));
                half3 L = SamplePrefilter(uv, float2( 0.0,  1.0));
                half3 M = SamplePrefilter(uv, float2( 1.0,  1.0));

                half2 div = (1.0 / 4.0) * half2(0.5, 0.125);

                half3 color = (D + E + I + J) * div.x;
                color += (A + B + G + F) * div.y;
                color += (B + C + H + G) * div.y;
                color += (F + G + L + K) * div.y;
                color += (G + H + M + L) * div.y;
            #else
                half3 color = SamplePrefilter(uv, float2(0,0));
            #endif

                // User controlled clamp to limit crazy high broken spec
                color = min(ClampMax, color);

                // Thresholding
                half brightness = Max3(color.r, color.g, color.b);
                half softness = clamp(brightness - Threshold + ThresholdKnee, 0.0, 2.0 * ThresholdKnee);
                softness = (softness * softness) / (4.0 * ThresholdKnee + 1e-4);
                half multiplier = max(brightness - Threshold, softness) / max(brightness, 1e-4);
                color *= multiplier;

                // Clamp colors to positive once in prefilter. Encode can have a sqrt, and sqrt(-x) == NaN. Up/Downsample passes would then spread the NaN.
                color = max(color, 0);
                return EncodeHDR(color);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Blur Horizontal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurH
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DynamicScalingClamping.hlsl"

            half4 FragBlurH(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 texelSize = _BlitTexture_TexelSize.xy * 2.0;
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

                // 9-tap gaussian blur on the downsampled source
                half3 c0 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv - float2(texelSize.x * 4.0, 0.0), texelSize)));
                half3 c1 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv - float2(texelSize.x * 3.0, 0.0), texelSize)));
                half3 c2 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv - float2(texelSize.x * 2.0, 0.0), texelSize)));
                half3 c3 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv - float2(texelSize.x * 1.0, 0.0), texelSize)));
                half3 c4 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv                                 , texelSize)));
                half3 c5 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv + float2(texelSize.x * 1.0, 0.0), texelSize)));
                half3 c6 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv + float2(texelSize.x * 2.0, 0.0), texelSize)));
                half3 c7 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv + float2(texelSize.x * 3.0, 0.0), texelSize)));
                half3 c8 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv + float2(texelSize.x * 4.0, 0.0), texelSize)));

                half3 color = c0 * 0.01621622 + c1 * 0.05405405 + c2 * 0.12162162 + c3 * 0.19459459
                            + c4 * 0.22702703
                            + c5 * 0.19459459 + c6 * 0.12162162 + c7 * 0.05405405 + c8 * 0.01621622;

                return EncodeHDR(color);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Blur Vertical"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurV
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DynamicScalingClamping.hlsl"

            half3 SampleHDR(float2 uv,  float2 offset)
            {
                float2 texelSize = _BlitTexture_TexelSize.xy;
                return DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv - offset * texelSize, texelSize)));
            }

            half4 FragBlurV(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 texelSize = _BlitTexture_TexelSize.xy;
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

                // Optimized bilinear 5-tap gaussian on the same-sized source (9-tap equivalent)
                half3 c0 = SampleHDR(uv, -float2(0.0, 3.23076923));
                half3 c1 = SampleHDR(uv, -float2(0.0, 1.38461538));
                half3 c2 = SampleHDR(uv,  float2(0.0, 0.0));
                half3 c3 = SampleHDR(uv, +float2(0.0, 1.38461538));
                half3 c4 = SampleHDR(uv, +float2(0.0, 3.23076923));

                half3 color = c0 * 0.07027027 + c1 * 0.31621622
                            + c2 * 0.22702703
                            + c3 * 0.31621622 + c4 * 0.07027027;

                return EncodeHDR(color);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Upsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUpsample
            #pragma multi_compile_local_fragment _ _BLOOM_HQ
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
            TEXTURE2D_X(_SourceTexLowMip);
            float4 _SourceTexLowMip_TexelSize;
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DynamicScalingClamping.hlsl"

            half3 Upsample(float2 uv)
            {
                half3 highMip = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv));

            #if _BLOOM_HQ
                half3 lowMip = DecodeHDR(SampleTexture2DBicubic(TEXTURE2D_X_ARGS(_SourceTexLowMip, sampler_LinearClamp), uv, _SourceTexLowMip_TexelSize.zwxy, (1.0).xx, unity_StereoEyeIndex));
            #else
                half3 lowMip = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTexLowMip, sampler_LinearClamp, uv));
            #endif

                return lerp(highMip, lowMip, Scatter);
            }

            half4 FragUpsample(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 color = Upsample(UnityStereoTransformScreenSpaceTex(input.texcoord));
                return EncodeHDR(color);
            }
            ENDHLSL
        }
        // Pass 6: full-resolution selection before the bloom prefilter samples neighbouring pixels.
        Pass
        {
            Name "Extract Selected HDR Color"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragExtractSource
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            TEXTURE2D_X(_SelectionMask);

            half4 FragExtractSource(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
                // Point samples keep unselected neighbours out of the source at selection edges.
                half mask = SAMPLE_TEXTURE2D_X(_SelectionMask, sampler_PointClamp, uv).r;
                half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
                return half4(mask > 0 ? color * mask : half3(0, 0, 0), 1);
            }
            ENDHLSL
        }
        // Pass 7: add bloom while camera color is still HDR, before URP post-processing.
        Pass
        {
            Name "Composite Bloom Into Scene"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            half4 FragComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half3 bloom = CombinedBloom(uv);
                // Add light in linear space, then return to the camera texture's color space.
                #if UNITY_COLORSPACE_GAMMA
                    scene.rgb = SRGBToLinear(scene.rgb);
                #endif
                scene.rgb += bloom;
                #if UNITY_COLORSPACE_GAMMA
                    scene.rgb = LinearToSRGB(scene.rgb);
                #endif
                return scene; // Preserve the scene's alpha.
            }
            ENDHLSL
        }
    }
    Fallback Off
}
