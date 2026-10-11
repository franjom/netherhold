// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
Shader "DK2/Probe/VAT Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base texture", 2D) = "white" {}
        [MainColor] _BaseColor("Base color", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Float) = 0
        _Metallic("Metallic", Float) = 0
        _SpecColor("Specular", Color) = (0,0,0,1)
        _Cutoff("Cutoff", Float) = 0
        _BumpScale("Bump scale", Float) = 1
        _OcclusionStrength("Occlusion", Float) = 1
        _EmissionMap("Emission", 2D) = "white" {}
        _EmissionColor("Emission color", Color) = (0,0,0,0)
        _Surface("Surface", Float) = 0
        _VatPositions("Vertex positions", 2D) = "black" {}
        _VatFrame("Frame", Float) = 0
        _VatFrameCount("Frame count", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="Lit" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VatVertex
            #pragma fragment LitPassFragment
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _EMISSION
            #define _SPECULARHIGHLIGHTS_OFF
            #define _ENVIRONMENTREFLECTIONS_OFF
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            Texture2D<float4> _VatPositions;
            float _VatFrame;
            float _VatFrameCount;
            Varyings VatVertex(Attributes input, uint vertexID : SV_VertexID)
            {
                float frame = max(_VatFrame, 0);
                int first = (int)frame;
                int second = min(first + 1, (int)_VatFrameCount - 1);
                input.positionOS.xyz = lerp(_VatPositions.Load(int3(vertexID, first, 0)).xyz,
                    _VatPositions.Load(int3(vertexID, second, 0)).xyz, frac(frame));
                return LitPassVertex(input);
            }
            ENDHLSL
        }
    }
}
