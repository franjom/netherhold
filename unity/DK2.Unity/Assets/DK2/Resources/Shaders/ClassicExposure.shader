// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
// The brightness slider: one full-screen triangle that scales the camera's finished image in place
// through the blender (Blend DstColor Zero darkens by _Scale, Blend DstColor One brightens by
// 1 + _Scale), so no copy and no intermediate target is needed. In the project's Gamma colour
// space the scale applies to display-encoded values.
Shader "Hidden/DK2/Classic Exposure"
{
    Properties
    {
        [HideInInspector] _Scale("Scale", Float) = 1
        [HideInInspector] _DstBlend("Destination blend", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZTest Always ZWrite Off
            Blend DstColor [_DstBlend]
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            float _Scale;
            float4 Vertex(uint vertexID : SV_VertexID) : SV_POSITION
            {
                return float4(float2((vertexID << 1) & 2, vertexID & 2) * 2 - 1, 0.5, 1);
            }
            half4 Fragment() : SV_Target
            {
                return half4(_Scale.xxx, 1);
            }
            ENDHLSL
        }
    }
}
