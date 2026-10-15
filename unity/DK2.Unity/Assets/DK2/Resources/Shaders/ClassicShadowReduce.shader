// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
Shader "Hidden/DK2/Classic Shadow Reduce"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZTest Always ZWrite Off Blend Off
            ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            Texture2D<float4> _MainTex;
            float4 Vertex(uint vertexID : SV_VertexID) : SV_POSITION
            {
                return float4(float2((vertexID << 1) & 2, vertexID & 2) * 2 - 1, 0.5, 1);
            }
            half4 Fragment(float4 positionCS : SV_POSITION) : SV_Target
            {
                int2 origin = int2(positionCS.xy) * 8;
                float sum = 0;
                [unroll] for (int y = 0; y < 8; y++)
                    [unroll] for (int x = 0; x < 8; x++)
                        sum += _MainTex.Load(int3(origin + int2(x, y), 0)).r;
                return half4(sum / 64.0, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
