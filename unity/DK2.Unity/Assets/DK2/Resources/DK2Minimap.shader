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
            float4 _GridSize, _Palette_TexelSize, _MapRect;
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
                p.y = -p.y;
                float radius = length(p);
                float alpha = 1 - smoothstep(1 - fwidth(radius), 1, radius);
                float s = sin(_Angle), c = cos(_Angle);
                float2 raster = float2(c * p.x - s * p.y, s * p.x + c * p.y) * 64 + 64;
                float2 tile = (raster - _MapRect.xy) * _GridSize.xy / max(_MapRect.zw, 1);
                float tileClass = 1;
                if (all(tile >= 0) && all(tile < _GridSize.xy))
                {
                    float2 classUv = (floor(tile) + .5) / _GridSize.xy;
                    float2 packed = floor(tex2D(_Classes, classUv).rg * 255 + .5);
                    tileClass = packed.x + packed.y * 256;
                }
                float3 colour = tileClass == 1 ? tex2D(_Backdrop, raster / 128).rgb
                    : tex2D(_Palette, float2((tileClass + .5) * _Palette_TexelSize.x, .5)).rgb;
                return float4(colour, alpha);
            }
            ENDHLSL
        }
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha, Zero One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            struct Marker { int2 position; uint kind, colour; int2 endPosition; };
            StructuredBuffer<Marker> _Markers;
            float4 _GridSize, _MapRect;
            float _Angle;
            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 local : TEXCOORD0;
                float2 disc : TEXCOORD1;
                nointerpolation float4 colour : COLOR0;
                nointerpolation uint kind : TEXCOORD2;
                nointerpolation float radius : TEXCOORD3;
                nointerpolation float2 extent : TEXCOORD4;
            };
            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                Marker marker = _Markers[instance];
                int2 tile = marker.position >> 12;
                int2 origin = tile * int2(_MapRect.zw) / int2(_GridSize.xy) + int2(_MapRect.xy);
                float2 size = 3;
                float2 centre = origin + .5;
                uint kind = marker.kind & 65535;
                uint radius = marker.kind >> 16;
                if (kind == 2 || kind == 3)
                {
                    int2 axis = kind == 2 ? int2(0,1) : int2(1,0);
                    int2 span = (tile + axis) * int2(_MapRect.zw) / int2(_GridSize.xy) + int2(_MapRect.xy) - origin;
                    size = max(1 - axis, span);
                    centre = origin + size * .5;
                    if (dot(span, axis) <= 0) size = 0;
                }
                else if (kind == 4) size = radius <= 1 ? radius * 2 + 1 : 2 * (radius + 1);
                else if (kind == 5)
                {
                    int2 endTile = marker.endPosition >> 12;
                    int2 end = endTile * int2(_MapRect.zw) / int2(_GridSize.xy) + int2(_MapRect.xy);
                    size = max(end - origin, 0);
                    centre = origin + size * .5;
                }
                float2 corners[6] = { float2(-.5,-.5), float2(.5,-.5), float2(.5,.5),
                    float2(-.5,-.5), float2(.5,.5), float2(-.5,.5) };
                Varyings o;
                o.local = corners[vertex] * 2;
                float2 p = (centre + corners[vertex] * size - 64) / 64;
                float s = sin(_Angle), c = cos(_Angle);
                o.disc = float2(c * p.x + s * p.y, -s * p.x + c * p.y);
                o.position = float4(o.disc, 0, 1);
                #if !UNITY_UV_STARTS_AT_TOP
                o.position.y = -o.position.y;
                #endif
                uint colour = marker.colour;
                o.colour = float4((colour >> 16) & 255, (colour >> 8) & 255, colour & 255, 255) / 255.0;
                o.kind = kind;
                o.radius = radius;
                o.extent = size;
                return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                float radius = length(i.disc);
                float alpha = 1 - smoothstep(1 - fwidth(radius), 1, radius);
                if (i.kind < 2 || (i.kind == 4 && i.radius == 1))
                {
                    float shape = length(i.local);
                    float edge = fwidth(shape) * .5;
                    alpha *= 1 - smoothstep(1 - edge, 1 + edge, shape);
                }
                else if (i.kind == 4 && i.radius > 1)
                {
                    float shape = abs(length(i.local) * (i.radius + 1) - i.radius) - .5;
                    float edge = max(fwidth(shape) * .5, .001);
                    alpha *= 1 - smoothstep(-edge, edge, shape);
                }
                else if (i.kind == 5)
                {
                    float2 pixels = (i.local + 1) * .5 * i.extent;
                    float2 distance = min(pixels, i.extent - pixels);
                    float border = min(distance.x, distance.y);
                    float edge = max(fwidth(border) * .5, .001);
                    alpha *= 1 - smoothstep(1 - edge, 1 + edge, border);
                }
                return float4(i.colour.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
