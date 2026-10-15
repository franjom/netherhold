// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
Shader "Hidden/DK2/Classic Lighting Probe"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment Fragment
            #include "UnityCG.cginc"
            #include "ClassicPointLighting.hlsl"
            float4 _ProbeVertex, _ProbeNormal, _ProbeLightPositionRadius, _ProbeLightColourIntensity;
            float _ProbeMode;
            float4 Fragment(v2f_img input) : SV_Target
            {
                if (_ProbeMode > 0.5)
                {
                    float3 diffuse = DK2PackedDiffuse(_ProbeVertex.xyz);
                    return float4(_ProbeMode > 1.5 ? diffuse * float3(0.25, 0.5, 1) : diffuse, 1);
                }
                float3 contribution;
                bool supported = DK2PointContribution(_ProbeVertex.xyz, _ProbeNormal.xyz,
                    _ProbeLightPositionRadius, _ProbeLightColourIntensity, contribution);
                return float4(contribution, supported ? 1 : 0);
            }
            ENDHLSL
        }
    }
}
