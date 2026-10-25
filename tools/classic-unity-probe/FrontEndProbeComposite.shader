// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

Shader "Hidden/DK2/FrontEndProbeComposite"
{
    Properties { _MainTex ("Overlay", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 frag(v2f_img i) : SV_Target { return tex2D(_MainTex, i.uv); }
            ENDCG
        }
    }
}
