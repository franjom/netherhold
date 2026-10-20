// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

Shader "DK2/ClassicEffect"
{
    Properties { _MainTex("Sprite", 2D) = "white" {} _DstBlend("Destination blend", Float) = 10
        _TintByLuminance("Tint by twice the luminance", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Blend SrcAlpha [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _TintByLuminance;
            struct Input { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Output { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            Output Vertex(Input v)
            {
                Output o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.uv = v.uv; o.color = v.color; return o;
            }
            half4 Fragment(Output v) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, v.uv);
                // A pale sprite painted to be recoloured: the colour at up to full strength.
                if (_TintByLuminance > 0) texel.rgb = saturate(dot(texel.rgb, half3(.299, .587, .114)) * 2);
                return texel * v.color;
            }
            ENDHLSL
        }
    }
}
