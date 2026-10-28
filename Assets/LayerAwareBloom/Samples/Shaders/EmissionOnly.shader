Shader "LayerAwareBloom/EmissionOnly"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _EmissionMap ("Emission Map", 2D) = "white" {}
        [HDR] _EdgeColor ("Edge Color", Color) = (0, 0, 0, 1)
        _EdgeWidth ("Edge Width (Face UV)", Range(0, 0.25)) = 0.025
        _EdgeStrength ("Edge Opacity", Range(0, 1)) = 0.55
        _FaceShading ("Face Shading", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float4 _EmissionColor;
            float4 _EmissionMap_ST;
            float4 _EdgeColor;
            float _EdgeWidth;
            float _EdgeStrength;
            float _FaceShading;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EmissionMap);
        SAMPLER(sampler_EmissionMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float2 faceUV : TEXCOORD1;
            float3 normalWS : TEXCOORD2;
            float2 baseUV : TEXCOORD3;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = TRANSFORM_TEX(input.uv, _EmissionMap);
            output.baseUV = TRANSFORM_TEX(input.uv, _BaseMap);
            output.faceUV = input.uv;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            // Unity's cube maps each face to 0..1. Keep the edge mask independent
            // of texture tiling, and antialias it for small distant cubes.
            float2 edgeDistance = min(input.faceUV, 1.0 - input.faceUV);
            float2 edgeAA = max(fwidth(input.faceUV), float2(0.0001, 0.0001));
            float2 interior = smoothstep(_EdgeWidth - edgeAA, _EdgeWidth + edgeAA, edgeDistance);
            float edge = (1.0 - min(interior.x, interior.y)) * step(0.00001, _EdgeWidth);

            // Keep the simple face shading on the surface color and the edges.
            float3 shapeLight = normalize(float3(-0.45, 0.8, -0.35));
            float faceLight = saturate(dot(normalize(input.normalWS), shapeLight));
            float shape = lerp(1.0, 0.4 + 0.6 * faceLight, _FaceShading);
            float3 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.baseUV).rgb
                * _BaseColor.rgb * shape;
            // Emission is independent of surface color and face shading.
            float3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb
                * _EmissionColor.rgb;
            float3 color = lerp(baseColor + emission, _EdgeColor.rgb * shape, edge * _EdgeStrength);
            return half4(color, 1.0);
        }

        half4 DepthFrag(Varyings input) : SV_Target
        {
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "EmissionOnly"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack Off
}
