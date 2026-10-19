// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
// Radius, colour and pulse are read (0x0059DD30). The material's texture and blend are not:
// additive blend and the bright-centre profile across the ribbon are matched to an owner
// screenshot of DK2 (hand-cursor-rendering.md), so the band reads as a thin light line.
Shader "DK2/Hand Hover Outline"
{
    Properties { _Color("Colour", Color) = (0,0,0.61328125,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" }
        Pass
        {
            ZTest LEqual ZWrite Off Cull Off Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float4 _Color;
            struct Varyings { float4 position : SV_POSITION; float across : TEXCOORD0; };
            Varyings Vert(float4 position : POSITION, float2 uv : TEXCOORD0)
            {
                Varyings o; o.position = UnityObjectToClipPos(position); o.across = uv.y; return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                float centre = 1 - abs(i.across * 2 - 1);
                // Exponent 2.15: the half-brightness core is 27.5% of the band, a third wider than
                // the cubic first tried (owner, 2026-10-19).
                float profile = pow(centre, 2.15);
                return float4(_Color.rgb * profile * 1.6, 0);
            }
            ENDHLSL
        }
    }
}
