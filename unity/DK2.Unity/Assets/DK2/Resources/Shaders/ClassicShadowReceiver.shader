// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
Shader "DK2/Classic Shadow Receiver"
{
    Properties { _MainTex("Mesh coverage", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-100" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZTest LEqual ZWrite Off Blend Zero OneMinusSrcColor
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float4 colour : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float strength : TEXCOORD1; };
            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.strength = input.colour.r;
                return output;
            }
            half4 Fragment(Varyings input) : SV_Target
            {
                half darkening = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).r * input.strength;
                return half4(darkening, darkening, darkening, 1);
            }
            ENDHLSL
        }
    }
}
