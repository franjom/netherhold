Shader "Hidden/DK2/Minimap"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            StructuredBuffer<uint2> _Changes;
            float4 _GridSize;
            struct Varyings { float4 position : SV_POSITION; nointerpolation uint colourClass : TEXCOORD0; };
            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                uint2 change = _Changes[instance];
                float2 corners[6] = { float2(0,0), float2(1,0), float2(1,1),
                    float2(0,0), float2(1,1), float2(0,1) };
                float2 cell = float2(change.x & 65535, change.x >> 16) + corners[vertex];
                Varyings o;
                o.position = float4(cell.x / _GridSize.x * 2 - 1, 1 - cell.y / _GridSize.y * 2, 0, 1);
                #if !UNITY_UV_STARTS_AT_TOP
                o.position.y = -o.position.y;
                #endif
                o.colourClass = change.y; return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                return float4(i.colourClass & 255, i.colourClass >> 8, 0, 255) / 255.0;
            }
            ENDHLSL
        }
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _Classes, _Palette, _Backdrop;
            float4 _GridSize, _Palette_TexelSize;
            float _Angle;
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o; o.uv = float2((id << 1) & 2, id & 2);
                o.position = float4(o.uv * 2 - 1, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                o.uv.y = 1 - o.uv.y;
                #endif
                return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2 - 1;
                float radius = length(p);
                float alpha = 1 - smoothstep(1 - fwidth(radius), 1, radius);
                float s = sin(_Angle), c = cos(_Angle);
                float2 raster = float2(c * p.x - s * p.y, s * p.x + c * p.y) * 64 + 64;
                float scale = 90 / max(_GridSize.x, _GridSize.y);
                float2 tile = (raster - (128 - _GridSize.xy * scale) * .5) / scale;
                float tileClass = 1;
                if (all(tile >= 0) && all(tile < _GridSize.xy))
                {
                    float2 classUv = (floor(tile) + .5) / _GridSize.xy;
                    // The scatter target's row zero is the lower texture row.
                    classUv.y = 1 - classUv.y;
                    float2 packed = floor(tex2D(_Classes, classUv).rg * 255 + .5);
                    tileClass = packed.x + packed.y * 256;
                }
                float3 colour = tileClass == 1 ? tex2D(_Backdrop, raster / 128).rgb
                    : tex2D(_Palette, float2((tileClass + .5) * _Palette_TexelSize.x, .5)).rgb;
                return float4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
