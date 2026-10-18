// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

Shader "Hidden/DK2/FullMap"
{
    SubShader
    {
        Tags { "Queue"="Transparent" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _Classes, _Palette;
            float4 _Palette_TexelSize;
            fixed4 Frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 packed = floor(tex2D(_Classes, uv).rg * 255 + .5);
                float tileClass = packed.x + packed.y * 256;
                return tileClass == 1 ? fixed4(0,0,0,1)
                    : fixed4(tex2D(_Palette,float2((tileClass+.5)*_Palette_TexelSize.x,.5)).rgb,1);
            }
            ENDHLSL
        }
    }
}
