// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
Shader "DK2/Hand Hover Outline"
{
    Properties { _Color("Colour", Color) = (0,0,0.61328125,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" }
        Pass
        {
            ZTest LEqual ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float4 _Color;
            float4 Vert(float4 position : POSITION) : SV_POSITION { return UnityObjectToClipPos(position); }
            float4 Frag() : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
