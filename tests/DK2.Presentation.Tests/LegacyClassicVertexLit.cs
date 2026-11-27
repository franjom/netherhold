// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation.Tests;

/// <summary>The Unity shader's DK2 light as it stood at 4f398b59, transcribed before it gained its tested reference
/// <see cref="ClassicVertexLight"/>: <c>TileLightContribution</c> of <c>ClassicVertexLit.shader</c> (lines 140-188) and
/// <c>DK2PointContribution</c>, <c>DK2PackedDiffuse</c>, <c>DK2InstancePointContribution</c> and
/// <c>DK2RadialContribution</c> of <c>ClassicPointLighting.hlsl</c>, in float, with HLSL's column-vector
/// <c>float3x3</c> held by rows, <c>round</c> to even, <c>min</c> and <c>max</c> dropping a NaN, and dot products summed left
/// to right. The structured buffers are arrays; <c>_DK2TileLightGridWidth</c> and <c>_DK2TileLightGridCount</c> parameters.</summary>
internal static class LegacyClassicVertexLit
{
    public struct Float3(float x, float y, float z)
    {
        public float X = x, Y = y, Z = z;
        public static Float3 operator +(Float3 a, Float3 b)
        {
            return new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static Float3 operator -(Float3 a, Float3 b)
        {
            return new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static Float3 operator *(Float3 a, float s)
        {
            return new(a.X * s, a.Y * s, a.Z * s);
        }
    }

    public struct Float4(float x, float y, float z, float w)
    {
        public float X = x, Y = y, Z = z, W = w;
        public Float3 Xyz
        {
            get
            {
                return new(X, Y, Z);
            }
        }
    }

    /// <summary>A float3x3 by rows: <c>mul(m, v)</c> dots each row with v.</summary>
    public struct Float3x3(Float3 row0, Float3 row1, Float3 row2)
    {
        public Float3 R0 = row0, R1 = row1, R2 = row2;
        public readonly Float3 Mul(Float3 v)
        {
            return new(Dot(R0, v), Dot(R1, v), Dot(R2, v));
        }

        public readonly Float3x3 Transpose()
        {
            return new(new Float3(R0.X, R1.X, R2.X), new Float3(R0.Y, R1.Y, R2.Y), new Float3(R0.Z, R1.Z, R2.Z));
        }
    }

    public struct TileSource
    {
        public Float4 PositionRadiusSquared;
        public Float3 Colour;
        public uint IntensityFlags;
    }

    /// <summary>The shader's vertex inputs and the object's transform (basis by rows, plus the origin).</summary>
    public struct Input
    {
        public Float3 PositionOS, NormalOS, PositionWS, NormalWS;
        public float AnchorX, AnchorY;
        public Float4 LightBounds;
        public float KindX, KindY;
        public Float3x3 Basis;
        public Float3 Origin;
    }

    public static float Dot(Float3 a, Float3 b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    private static int Round(float x)
    {
        return (int)Math.Round((double)x, MidpointRounding.ToEven);
    }

    private static float Min(float a, float b)
    {
        return float.IsNaN(a) ? b : float.IsNaN(b) ? a : Math.Min(a, b);
    }

    private static float Max(float a, float b)
    {
        return float.IsNaN(a) ? b : float.IsNaN(b) ? a : Math.Max(a, b);
    }

    private static uint AsUint(float f)
    {
        return unchecked((uint)BitConverter.SingleToInt32Bits(f));
    }

    public static bool DK2PointContribution(Float3 vertex, Float3 normal, Float4 positionRadiusSquared,
        Float4 colourIntensity, out Float3 contribution)
    {
        contribution = default;
        Float3 toLight = positionRadiusSquared.Xyz - vertex;
        float distanceSquared = Dot(toLight, toLight);
        float radiusSquared = positionRadiusSquared.W;
        if (radiusSquared <= 0 || distanceSquared >= radiusSquared)
        {
            return true;
        }

        float incidence = Dot(normal, toLight) * colourIntensity.W;
        if ((AsUint(incidence) & 0x80000000u) != 0)
        {
            return true;
        }

        int index = Round(distanceSquared * 16.0f - 0.49999f);
        if (index < 0 || index >= 256)
        {
            return false;
        }

        if (index == 0)
        {
            if (incidence == 0)
            {
                return false;
            }

            contribution = colourIntensity.Xyz;
            return true;
        }
        float lookup = 0.25f + 16.0f / index;
        float factor = incidence * lookup * (radiusSquared - distanceSquared) / radiusSquared;
        contribution = colourIntensity.Xyz * Min(factor, 1.0f);
        return true;
    }

    public static Float3 DK2PackedDiffuse(Float3 sum)
    {
        return new(Pack(sum.X), Pack(sum.Y), Pack(sum.Z));
    }

    private static float Pack(float c)
    {
        return Min((float)Math.Floor(Max(c, 0.0f) * 255.0f + 0.00001f), 255.0f) / 255.0f;
    }

    public static bool DK2InstancePointContribution(Float3 sampleWS, Float3 centre, Float3 vertexOS, Float3 normalOS,
        Float3x3 basis, Float4 positionRadiusSquared, Float4 colourIntensity, out Float3 contribution)
    {
        contribution = default;
        Float3 delta = positionRadiusSquared.Xyz - sampleWS;
        float distanceSquared = (delta.X * delta.X + delta.Y * delta.Y) + delta.Z * delta.Z;
        float radiusSquared = positionRadiusSquared.W;
        if (radiusSquared <= 0 || distanceSquared >= radiusSquared)
        {
            return true;
        }

        int index = Round(distanceSquared * 16.0f - 0.49999f);
        if (index < 0 || index >= 256)
        {
            return false;
        }

        Float3 direction = basis.Transpose().Mul(delta);
        Float3 d = direction - (vertexOS - centre);
        float incidence = ((d.X * normalOS.X + d.Z * normalOS.Z) + d.Y * normalOS.Y) * colourIntensity.W;
        if ((AsUint(incidence) & 0x80000000u) != 0)
        {
            return true;
        }

        if (index == 0)
        {
            if (incidence == 0)
            {
                return false;
            }

            contribution = colourIntensity.Xyz;
            return true;
        }
        float attenuation = (radiusSquared - distanceSquared) * (0.25f + 16.0f / index) * (1.0f / radiusSquared);
        if (!(attenuation > 0))
        {
            return true;
        }

        contribution = colourIntensity.Xyz * Min(incidence * attenuation, 1.0f);
        return true;
    }

    public static Float3 DK2RadialContribution(Float3 sampleWS, Float4 positionRadiusSquared, Float4 colourIntensity)
    {
        Float3 delta = positionRadiusSquared.Xyz - sampleWS;
        float distanceSquared = Dot(delta, delta), radiusSquared = positionRadiusSquared.W;
        if (radiusSquared <= 0 || distanceSquared >= radiusSquared)
        {
            return default;
        }

        int index = Round(distanceSquared * 16.0f - 0.49999f);
        if (index < 0 || index >= 256)
        {
            return default;
        }

        if (index == 0)
        {
            return colourIntensity.Xyz;
        }

        float factor = (0.25f + 16.0f / index) * (radiusSquared - distanceSquared)
            * colourIntensity.W * (1.0f / radiusSquared);
        return colourIntensity.Xyz * Min(factor, 1.0f);
    }

    public static Float3 TileLightContribution(Input input, TileSource[] sources, uint[] counts, int gridWidth, int gridCount)
    {
        if (gridCount <= 0 || gridWidth <= 0)
        {
            return default;
        }

        int tile;
        if (input.AnchorY >= 1)
        {
            tile = (int)input.AnchorX;
        }
        else
        {
            Float3 origin = input.Origin;
            int x = (int)Math.Floor(origin.X), y = (int)Math.Floor(-origin.Z);
            if (x < 0 || x >= gridWidth || y < 0)
            {
                return default;
            }

            tile = y * gridWidth + x;
        }
        if (tile < 0 || tile >= gridCount)
        {
            return default;
        }

        uint count = Math.Min(counts[tile], 24u);
        Float3x3 basis = input.Basis;
        Float3 centre = basis.Mul(input.LightBounds.Xyz);
        Float3 sampleWS = input.Origin + centre;
        Float3 sum = default;
        for (uint slot = 0; slot < count; slot++)
        {
            TileSource source = sources[tile * 24 + slot];
            uint flags = source.IntensityFlags >> 8;
            var colourIntensity = new Float4(source.Colour.X, source.Colour.Y, source.Colour.Z, (source.IntensityFlags & 255u) / 128.0f);
            Float3 contribution;
            if (input.KindY == 1)
            {
                Float3 column = basis.Mul(new Float3(1, 0, 0));
                float drawScale = (float)Math.Sqrt(Dot(column, column));
                float radius = (float)Math.Sqrt(source.PositionRadiusSquared.W) + input.LightBounds.W * drawScale;
                Float3 delta = source.PositionRadiusSquared.Xyz - sampleWS;
                if (Dot(delta, delta) >= radius * radius)
                {
                    continue;
                }
            }
            if (input.KindX == 1)
            {
                if ((flags & 4u) == 4u)
                {
                    sum += DK2RadialContribution(input.Origin, source.PositionRadiusSquared, colourIntensity);
                }

                if ((flags & 256u) == 256u && DK2InstancePointContribution(sampleWS, centre, input.PositionOS, input.NormalOS,
                        basis, source.PositionRadiusSquared, colourIntensity, out contribution))
                {
                    sum += contribution;
                }
            }
            else if (DK2PointContribution(input.PositionWS, input.NormalWS, source.PositionRadiusSquared, colourIntensity,
                         out contribution))
            {
                if ((flags & 0x60u) == 0x60u)
                {
                    sum += contribution;
                }

                if ((flags & 0xA0u) == 0xA0u || (flags & 1u) == 1u)
                {
                    sum += contribution;
                }
            }
        }
        return sum;
    }

    /// <summary><c>ClassicTileLightBuffer.Set</c>'s packing of one source at 4f398b59.</summary>
    public static TileSource Entry(in ClassicPointLight light)
    {
        return new()
        {
            PositionRadiusSquared = new Float4(light.Position.X, light.Position.Y, light.Position.Z, light.Radius * light.Radius),
            Colour = new Float3(light.Colour.X, light.Colour.Y, light.Colour.Z),
            IntensityFlags = (uint)Math.Round(light.Intensity * 128f) | ((uint)light.Flags << 8),
        };
    }
}
