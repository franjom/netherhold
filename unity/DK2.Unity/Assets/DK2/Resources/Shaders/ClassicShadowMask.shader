// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
Shader "Hidden/DK2/Classic Shadow Mask"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZTest Always ZWrite Off Blend One One
            ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4x4 _CasterLocalToWorld;
            float4 _CasterCentreRadius, _CrossAxis, _AlongCos;
            float _ProjectionSin;
            float4 Vertex(float3 positionOS : POSITION) : SV_POSITION
            {
                float3 relative = mul(_CasterLocalToWorld, float4(positionOS, 1)).xyz - _CasterCentreRadius.xyz;
                float2 projected = float2(dot(relative, _CrossAxis.xyz),
                    dot(relative, _AlongCos.xyz) * _AlongCos.w + relative.y * _ProjectionSin) / _CasterCentreRadius.w;
                #if UNITY_UV_STARTS_AT_TOP
                    projected.y = -projected.y;
                #endif
                return float4(projected, 0.5, 1);
            }
            half4 Fragment() : SV_Target { return half4(1, 1, 1, 1); }
            ENDHLSL
        }
    }
}
