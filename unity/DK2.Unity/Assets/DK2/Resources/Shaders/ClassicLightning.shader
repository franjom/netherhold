// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
// Additive is supported by the engine texture's AA material kind; Q659 remains open.
Shader "DK2/Procedural Lightning"
{
    Properties { _MainTex("Lightning", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" }
        Pass
        {
            ZTest LEqual ZWrite Off Cull Back Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(float4 position : POSITION, float2 uv : TEXCOORD0)
            {
                Varyings o; o.position = UnityObjectToClipPos(position); o.uv = uv; return o;
            }
            float4 Frag(Varyings i) : SV_Target { return tex2D(_MainTex, i.uv); }
            ENDHLSL
        }
    }
}
