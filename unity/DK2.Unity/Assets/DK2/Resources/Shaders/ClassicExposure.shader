// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
// The brightness slider: one full-screen triangle that redraws a copy of the camera's finished
// image through a gamma curve, pow(c, _Exponent) with _Exponent = 30 / Gamma, like a display gamma
// ramp: black and white stay put, so the top of the slider lifts mid-tones without clipping. In
// the project's Gamma colour space the curve applies to display-encoded values.
Shader "Hidden/DK2/Classic Exposure"
{
    Properties
    {
        [HideInInspector] _Exponent("Exponent", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZTest Always ZWrite Off Blend Off
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            Texture2D<float4> _DK2BrightnessSource;
            float _Exponent;
            float4 Vertex(uint vertexID : SV_VertexID) : SV_POSITION
            {
                return float4(float2((vertexID << 1) & 2, vertexID & 2) * 2 - 1, 0.5, 1);
            }
            half4 Fragment(float4 positionCS : SV_POSITION) : SV_Target
            {
                float3 c = _DK2BrightnessSource.Load(int3(positionCS.xy, 0)).rgb;
                return half4(pow(max(c, 0), _Exponent), 1);
            }
            ENDHLSL
        }
    }
}
