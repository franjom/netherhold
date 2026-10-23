// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
Shader "DK2/Classic Vertex Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Colour", Color) = (1,1,1,1)
        _EmissionMap("Emission", 2D) = "white" {}
        [HDR] _EmissionColor("Added light", Color) = (0,0,0,0)
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.5
        _Smoothness("Smoothness", Float) = 0
        _Metallic("Metallic", Float) = 0
        _SpecularHighlights("Specular", Float) = 0
        _EnvironmentReflections("Reflections", Float) = 0
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _Blend("Blend", Float) = 0
        [HideInInspector] _SrcBlend("Source blend", Float) = 1
        [HideInInspector] _DstBlend("Destination blend", Float) = 0
        [HideInInspector] _ZWrite("Depth write", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DK2_TILE_LIGHT_TABLES
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            // Install materials select these keywords at runtime, after player variant stripping.
            #pragma multi_compile_local _ _EMISSION
            #pragma multi_compile_local_fragment _ _SURFACE_TYPE_TRANSPARENT
            // DK2's environment pass (kmf-materials.md §8): the material's environment texture,
            // sampled where EnvironmentMapCoordinates.Of says, drawn ONE/ONE by ClassicMeshes.
            #pragma multi_compile_local_vertex _ DK2_ENVIRONMENT_MAP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "ClassicPointLighting.hlsl"

            struct TorchSource { float4 positionRadiusSquared; float4 colourIntensity; };
            #if defined(DK2_TILE_LIGHT_TABLES)
            struct TileSource { float4 positionRadiusSquared; float3 colour; uint intensityFlags; };
            StructuredBuffer<TileSource> _DK2TileLightSources;
            StructuredBuffer<uint> _DK2TileLightCounts;
            int _DK2TileLightGridWidth, _DK2TileLightGridCount;
            #else
            StructuredBuffer<TorchSource> _DK2TorchSources;
            int _DK2TorchGridWidth, _DK2TorchGridCount;
            #endif
            float3 _DK2AmbientColour;
            float _DK2InitialAmbientPreview;
            // Owner-tuned look (ADR-0522), unset (zero) means neutral: the fill's tint, and the
            // shares of the far plane, as distance from the eye, where the fade to black starts (x) and ends (y).
            float4 _DK2FillTint;
            float4 _DK2DistanceFade;
            // 1 where DK2 would light the tile, 0 where it would not (ClassicTileFillBuffer).
            StructuredBuffer<float> _DK2TileFill;
            int _DK2TileFillWidth, _DK2TileFillCount;
            // The share of the fill an unlit tile keeps: dark but not black (owner 2026-10-21).
            float _DK2UnlitFill;
            // The brightness of DK2's own light at which Unity's fill has faded out; unset (zero)
            // adds the fill everywhere.
            float _DK2FillFloor;
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 tileAnchor : TEXCOORD1;
                float4 lightBounds : TEXCOORD2;
                float2 lightKind : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 diffuse : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 LightPosition(uint loopIndex)
            {
            #if USE_CLUSTER_LIGHT_LOOP
                int index = loopIndex;
            #else
                int index = GetPerObjectLightIndex(loopIndex);
            #endif
            #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
                return _AdditionalLightsBuffer[index].position;
            #else
                return _AdditionalLightsPosition[index];
            #endif
            }

            float3 AdditionalContribution(uint index, float3 positionWS, float3 normalWS)
            {
                Light light = GetAdditionalLight(index, positionWS, half4(1,1,1,1));
                float4 sourcePosition = LightPosition(index);
                if (sourcePosition.w <= 0 && _DK2InitialAmbientPreview > .5) return 0;
                #if defined(DK2_TILE_LIGHT_TABLES)
                if (sourcePosition.w > 0) return 0;
                #else
                if (sourcePosition.w > 0 && _DK2TorchGridWidth > 0 && _DK2TorchGridCount > 0)
                {
                    int x = (int)floor(sourcePosition.x), y = (int)floor(-sourcePosition.z);
                    int tile = y * _DK2TorchGridWidth + x;
                    if (x >= 0 && x < _DK2TorchGridWidth && y >= 0 && tile >= 0 && tile < _DK2TorchGridCount)
                    {
                        TorchSource source = _DK2TorchSources[tile];
                        float3 delta = source.positionRadiusSquared.xyz - sourcePosition.xyz;
                        float3 contribution;
                        if (source.positionRadiusSquared.w > 0 && dot(delta,delta) < 0.00000001
                            && DK2PointContribution(positionWS, normalWS, source.positionRadiusSquared,
                                source.colourIntensity, contribution))
                            return contribution * light.shadowAttenuation;
                    }
                }
                #endif
                return light.color * max(dot(normalWS, light.direction), 0)
                    * light.distanceAttenuation * light.shadowAttenuation;
            }

            #if defined(DK2_TILE_LIGHT_TABLES)
            float3 TileLightContribution(Attributes input, float3 positionWS, float3 normalWS)
            {
                if (_DK2TileLightGridCount <= 0 || _DK2TileLightGridWidth <= 0) return 0;
                int tile;
                if (input.tileAnchor.y == 1) tile = (int)input.tileAnchor.x;
                else
                {
                    float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                    int x = (int)floor(origin.x), y = (int)floor(-origin.z);
                    if (x < 0 || x >= _DK2TileLightGridWidth || y < 0) return 0;
                    tile = y * _DK2TileLightGridWidth + x;
                }
                if (tile < 0 || tile >= _DK2TileLightGridCount) return 0;
                uint count = min(_DK2TileLightCounts[tile], 24u);
                float3x3 basis = (float3x3)GetObjectToWorldMatrix();
                float3 centre = mul(basis, input.lightBounds.xyz);
                float3 sampleWS = TransformObjectToWorld(float3(0, 0, 0)) + centre;
                float3 sum = 0;
                UNITY_LOOP for (uint slot = 0; slot < count; slot++)
                {
                    TileSource source = _DK2TileLightSources[tile * 24 + slot];
                    uint flags = source.intensityFlags >> 8;
                    float4 colourIntensity = float4(source.colour, (source.intensityFlags & 255u) / 128.0);
                    float3 contribution;
                    if (input.lightKind.y == 1)
                    {
                        // Native draw scale is uniform; first basis column carries that scalar.
                        float drawScale = length(mul(basis, float3(1, 0, 0)));
                        float radius = sqrt(source.positionRadiusSquared.w) + input.lightBounds.w * drawScale;
                        float3 delta = source.positionRadiusSquared.xyz - sampleWS;
                        if (dot(delta, delta) >= radius * radius) continue;
                    }
                    if (input.lightKind.x == 1)
                    {
                        if ((flags & 4u) == 4u) sum += DK2RadialContribution(TransformObjectToWorld(float3(0, 0, 0)),
                            source.positionRadiusSquared, colourIntensity);
                        if ((flags & 256u) == 256u && DK2InstancePointContribution(sampleWS, centre,
                            input.positionOS.xyz, input.normalOS, basis, source.positionRadiusSquared,
                            colourIntensity, contribution)) sum += contribution;
                    }
                    else if (DK2PointContribution(positionWS, normalWS, source.positionRadiusSquared,
                            colourIntensity, contribution))
                    {
                        if ((flags & 0x60u) == 0x60u) sum += contribution;
                        if ((flags & 0xA0u) == 0xA0u || (flags & 1u) == 1u) sum += contribution;
                    }
                }
                return sum;
            }
            #endif

            // A large object (a portal's arch) spans tiles, so its vertices take the tile under them.
            float TileFillWeight(Attributes input, float3 positionWS)
            {
                if (_DK2TileFillCount <= 0 || _DK2TileFillWidth <= 0) return 1;
                int tile;
                if (input.tileAnchor.y == 1) tile = (int)input.tileAnchor.x;
                else
                {
                    int x = (int)floor(positionWS.x), y = (int)floor(-positionWS.z);
                    if (x < 0 || x >= _DK2TileFillWidth || y < 0) return 1;
                    tile = y * _DK2TileFillWidth + x;
                }
                return tile >= 0 && tile < _DK2TileFillCount ? lerp(_DK2UnlitFill, 1, _DK2TileFill[tile]) : 1;
            }

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                // Native ambient is an RGB addition; Unity's placeholder sky/sun are not native inputs.
                float3 sum = _DK2AmbientColour, fill = 0;
                #if defined(DK2_TILE_LIGHT_TABLES)
                sum += TileLightContribution(input, position.positionWS, normalWS);
                #endif
                if (_DK2InitialAmbientPreview < .5)
                {
                    // Retain the scene fallback until native fill lights and ambient commands are supplied.
                    // Terrain tops carry DK2's inverted lighting normal (static-mesh-lighting.md section 1),
                    // which the tile lights above want; Unity's fill wants the surface's own. A terrain
                    // normal facing down is such a top, turned back; a top's vertical side is not caught.
                    float3 fillNormal = input.tileAnchor.y == 1 && normalWS.y < 0 ? -normalWS : normalWS;
                    fill = max(SampleSH(fillNormal), 0);
                    Light main = GetMainLight(TransformWorldToShadowCoord(position.positionWS));
                    fill += main.color * max(dot(fillNormal, main.direction), 0)
                        * main.distanceAttenuation * main.shadowAttenuation;
                    if (_DK2FillTint.w > 0) fill *= _DK2FillTint.rgb;
                    fill *= TileFillWeight(input, position.positionWS);
                }
                InputData inputData = (InputData)0;
                inputData.positionWS = position.positionWS;
                float4 screenPosition = ComputeScreenPos(position.positionCS);
                inputData.normalizedScreenSpaceUV = screenPosition.xy / screenPosition.w;
            #if USE_CLUSTER_LIGHT_LOOP
                UNITY_LOOP for (uint index = 0; index < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); index++)
                    sum += AdditionalContribution(index, position.positionWS, normalWS);
            #endif
            #if USE_CLUSTER_LIGHT_LOOP
                float viewDepth = -TransformWorldToView(position.positionWS).z;
                bool insideViewport = all(inputData.normalizedScreenSpaceUV >= 0)
                    && all(inputData.normalizedScreenSpaceUV < 1) && viewDepth >= _ProjectionParams.y;
                if (insideViewport)
                {
                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        sum += AdditionalContribution(lightIndex, position.positionWS, normalWS);
                    LIGHT_LOOP_END
                }
                else
                {
                    // ClusterInit indexes screen tiles without clipping; input vertices may be offscreen.
                    uint end = min(URP_FP_DIRECTIONAL_LIGHTS_COUNT + URP_FP_PROBES_BEGIN, MAX_VISIBLE_LIGHTS);
                    UNITY_LOOP for (uint index = URP_FP_DIRECTIONAL_LIGHTS_COUNT; index < end; index++)
                        sum += AdditionalContribution(index, position.positionWS, normalWS);
                }
            #else
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    sum += AdditionalContribution(lightIndex, position.positionWS, normalWS);
                LIGHT_LOOP_END
            #endif
                // Unity's fill lifts only what DK2's own lights leave dark: where they reach, they
                // alone match the owner's DK2 shots, and the fill on top flattened and yellowed them.
                // Instance-lit models keep it all: under DK2's lights alone our Heart arches, steps and
                // creatures stay near black where the owner's DK2 shots show them lit (ADR-0522).
                if (_DK2FillFloor > 0 && input.lightKind.x != 1) fill *= saturate(1 - dot(sum, float3(0.299, 0.587, 0.114)) / _DK2FillFloor);
                sum += fill;
            #if defined(_EMISSION)
                sum += _EmissionColor.rgb;
            #endif
                if (_DK2DistanceFade.y > _DK2DistanceFade.x)
                {
                    // Distance from the eye, not depth: DK2's view darkens toward its sides too.
                    float reach = length(position.positionWS - GetCameraPositionWS()) / _ProjectionParams.z;
                    sum *= saturate((_DK2DistanceFade.y - reach) / (_DK2DistanceFade.y - _DK2DistanceFade.x));
                }
                output.positionCS = position.positionCS;
            #if defined(DK2_ENVIRONMENT_MAP)
                // 0x00582290: the eye vector in camera space, normalised, and the world normal in
                // DK2's frame (x, -z, -y of ours), halved. Tested reference: EnvironmentMapCoordinates.
                // GUESS: DK2's camera space is Direct3D's (z into the screen); its axes and the sign
                // of v are unconfirmed. The uv can leave 0..1; our texture clamps, DK2's address
                // mode for this stage is unread.
                float3 viewPosition = TransformWorldToView(position.positionWS);
                float3 eye = normalize(float3(viewPosition.x, viewPosition.y, -viewPosition.z));
                float3 normalDK2 = 0.5 * float3(normalWS.x, -normalWS.z, -normalWS.y);
                output.uv = float2(normalDK2.z * eye.x - normalDK2.x * eye.z,
                    normalDK2.z * eye.y - normalDK2.y * eye.z) + 0.5;
            #else
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            #endif
                output.diffuse = DK2PackedDiffuse(sum);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
            #if !defined(_SURFACE_TYPE_TRANSPARENT)
                sampled.a = 1;
            #endif
                return half4(sampled.rgb * input.diffuse, sampled.a);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Universal Render Pipeline/Lit"
}
