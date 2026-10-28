Shader "Hidden/LayerAwareBloom/UIBloomSource"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _StencilRef("Stencil reference", Float) = 0
        _StencilOp("Stencil operation", Float) = 0
        _ColorMask("Color mask", Float) = 15
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ColorMask [_ColorMask]
            Stencil { Ref [_StencilRef] Comp Equal Pass [_StencilOp] }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_AlphaTex); SAMPLER(sampler_AlphaTex);
            float4 _RendererColor, _MaterialColor, _TextureSampleAdd;
            float4 _ClipRect, _ClipSoftness;
            float4x4 _WorldToCanvas;
            float _Emission, _UseClipRect, _UseAlphaTex, _IsMask;
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float2 canvasPosition : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.canvasPosition = mul(_WorldToCanvas, float4(world, 1)).xy;
                output.uv = input.uv;
                output.color = input.color * _RendererColor;
                #ifndef UNITY_COLORSPACE_GAMMA
                    output.color.rgb = SRGBToLinear(output.color.rgb);
                #endif
                output.color *= _MaterialColor;
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float sceneDepth = SampleSceneDepth(input.positionCS.xy / _ScaledScreenParams.xy);
                #if UNITY_REVERSED_Z
                    clip(input.positionCS.z - sceneDepth + 0.000001);
                #else
                    clip(sceneDepth - input.positionCS.z + 0.000001);
                #endif
                float4 sampled = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) + _TextureSampleAdd;
                if (_UseAlphaTex > 0.5) sampled.a = SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, input.uv).r;
                float4 color = sampled * input.color;
                if (_UseClipRect > 0.5)
                {
                    float2 distanceToEdge = min(input.canvasPosition - _ClipRect.xy, _ClipRect.zw - input.canvasPosition);
                    float2 pixelSize = max(fwidth(input.canvasPosition), 0.00001);
                    float2 coverage = saturate(distanceToEdge / (pixelSize * (0.5 + _ClipSoftness.xy * 0.25)));
                    color.a *= coverage.x * coverage.y;
                }
                if (_IsMask > 0.5) clip(color.a - 0.001);
                color.rgb *= _Emission;
                return color;
            }
            ENDHLSL
        }
    }
}
