Shader "Hidden/DK2/HudPilot"
{
    Properties
    {
        _Classes ("Class grid", 2D) = "black" {}
        _Palette ("Install palette", 2D) = "white" {}
        _Backdrop ("Install backdrop", 2D) = "black" {}
        _Glyph ("Font raster or field", 2D) = "white" {}
        _Angle ("Angle", Float) = 0
        _Font ("Font pass", Float) = 0
        _DistanceField ("Distance field", Float) = 0
    }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _Classes, _Palette, _Backdrop, _Glyph;
            float4 _Palette_TexelSize;
            float _Angle, _Font, _DistanceField;
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                o.uv = float2((id << 1) & 2, id & 2);
                o.position = float4(o.uv * 2 - 1, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                o.uv.y = 1 - o.uv.y;
                #endif
                return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                if (_Font > .5)
                {
                    float coverage = tex2D(_Glyph, i.uv).a;
                    if (_DistanceField > .5)
                    {
                        float edge = max(fwidth(coverage), 1.0 / 255.0);
                        coverage = smoothstep(.5 - edge, .5 + edge, coverage);
                    }
                    return float4(1, .83, .55, coverage);
                }
                float2 p = i.uv * 2 - 1;
                float radius = length(p);
                float alpha = 1 - smoothstep(1 - fwidth(radius), 1, radius);
                float s = sin(_Angle), c = cos(_Angle);
                float2 map = float2(c * p.x - s * p.y, s * p.x + c * p.y) * .5 + .5;
                float tileClass = floor(tex2D(_Classes, map).r * 255 + .5);
                float2 paletteUv = (float2(fmod(tileClass, _Palette_TexelSize.z),
                    floor(tileClass / _Palette_TexelSize.z)) + .5) * _Palette_TexelSize.xy;
                float3 colour = tileClass == 1 ? tex2D(_Backdrop, map).rgb : tex2D(_Palette, paletteUv).rgb;
                return float4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
