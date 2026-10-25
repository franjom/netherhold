// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

Shader "Hidden/DK2/FrontEndFontAlpha"
{
    Properties { _MainTex ("Glyph atlas", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 frag(v2f_img i) : SV_Target { return fixed4(1, 1, 1, tex2D(_MainTex, i.uv).a); }
            ENDCG
        }
    }
}
