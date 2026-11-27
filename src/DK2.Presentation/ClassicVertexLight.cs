// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Numerics;

namespace DK2.Presentation;

/// <summary>One source of a tile's light table as the classic vertex-lit shaders read it: position and radius squared,
/// colour, and the intensity in 1/128 steps packed with the <see cref="ClassicLightFlags"/> above it.</summary>
public readonly record struct ClassicTileLightSource(Vector4 PositionRadiusSquared, Vector3 Colour, uint IntensityFlags)
{
    /// <summary>Packed intensity and flags: <c>round(I x 128) | flags &lt;&lt; 8</c>.</summary>
    public static ClassicTileLightSource Of(in ClassicPointLight light)
    {
        return new ClassicTileLightSource(
        new Vector4(light.Position.X, light.Position.Y, light.Position.Z, light.Radius * light.Radius), light.Colour,
        (uint)Math.Round(light.Intensity * 128f) | ((uint)light.Flags << 8));
    }

    public uint Flags
    {
        get
        {
            return IntensityFlags >> 8;
        }
    }

    public float Intensity
    {
        get
        {
            return (IntensityFlags & 255u) / 128f;
        }
    }
}

/// <summary>What one vertex brings to <see cref="ClassicVertexLight.TableSum"/>: its object-space position and normal, the
/// shader's light inputs (table anchor, KMF bound sphere and light kind), its object's
/// transform (System.Numerics, row vectors), and its world position and normal as the shader computed them.</summary>
public readonly record struct ClassicLitVertex(Vector3 Position, Vector3 Normal, Vector2 TileAnchor, Vector4 LightBounds,
    Vector2 LightKind, Matrix4x4 ObjectToWorld, Vector3 WorldPosition, Vector3 WorldNormal);

/// <summary>The light tables of a grid of tiles, <see cref="ClassicLightTable.MaximumSources"/> sources a tile, and how many
/// each tile holds.</summary>
public readonly ref struct ClassicTileLightGrid
{
    public ClassicTileLightGrid(ReadOnlySpan<ClassicTileLightSource> sources, ReadOnlySpan<uint> counts, int width)
    {
        Sources = sources;
        Counts = counts;
        Width = width;
    }

    public ReadOnlySpan<ClassicTileLightSource> Sources { get; }
    public ReadOnlySpan<uint> Counts { get; }
    public int Width { get; }
}

/// <summary>The tested reference for the world shader's DK2 lighting: the table a vertex reads,
/// the bound-sphere skip, the per-flag sum, and the packed diffuse. Keep the shader's float arithmetic
/// in sync with this reference. Its terms are those of
/// <see cref="ClassicPointLight"/> and <see cref="ClassicInstancePointLight"/> written as the shaders run them, which
/// add nothing where those references throw (the 256-bin overflow, zero incidence in the first bin).</summary>
public static class ClassicVertexLight
{
    /// <summary>The tile whose table lights a vertex, -1 for none: the anchor's tile when its kind is 1 or more (terrain),
    /// else the tile under the object's origin (in the renderers' frame); -1 off the grid or for an empty grid.</summary>
    public static int TableTile(Vector2 tileAnchor, Vector3 origin, int gridWidth, int gridCount)
    {
        if (gridCount <= 0 || gridWidth <= 0)
        {
            return -1;
        }

        int tile;
        if (tileAnchor.Y >= 1)
        {
            tile = (int)tileAnchor.X;
        }
        else
        {
            int x = (int)Math.Floor(origin.X), y = (int)Math.Floor(-origin.Z);
            if (x < 0 || x >= gridWidth || y < 0)
            {
                return -1;
            }

            tile = y * gridWidth + x;
        }
        return tile < 0 || tile >= gridCount ? -1 : tile;
    }

    /// <summary>The DK2 light a vertex takes from its table (<see cref="TableTile"/>, at most 24 sources). With light kind
    /// y 1 a source farther than its radius plus the bound sphere's (scaled by the draw scale) is skipped. Instance-lit
    /// vertices (kind x 1) add the radial term at the origin under flag 0x04 and the instance point term under 0x100;
    /// others add the point term once under 0x60 and once more under 0xA0 or 0x01.</summary>
    public static Vector3 TableSum(in ClassicTileLightGrid grid, in ClassicLitVertex vertex)
    {
        Matrix4x4 m = vertex.ObjectToWorld;
        Vector3 origin = m.Translation;
        int tile = TableTile(vertex.TileAnchor, origin, grid.Width, grid.Counts.Length);
        if (tile < 0)
        {
            return Vector3.Zero;
        }

        uint count = Math.Min(grid.Counts[tile], (uint)ClassicLightTable.MaximumSources);
        Vector3 centre = Basis(m, new Vector3(vertex.LightBounds.X, vertex.LightBounds.Y, vertex.LightBounds.Z));
        Vector3 sample = origin + centre;
        Vector3 sum = Vector3.Zero;
        for (int slot = 0; slot < count; slot++)
        {
            ClassicTileLightSource source = grid.Sources[tile * ClassicLightTable.MaximumSources + slot];
            uint flags = source.Flags;
            Vector4 light = source.PositionRadiusSquared;
            float intensity = source.Intensity;
            if (vertex.LightKind.Y == 1)
            {
                // Native draw scale is uniform; the first basis column carries it.
                float drawScale = (float)Math.Sqrt(Dot(Basis(m, Vector3.UnitX), Basis(m, Vector3.UnitX)));
                float radius = (float)Math.Sqrt(light.W) + vertex.LightBounds.W * drawScale;
                Vector3 delta = new Vector3(light.X, light.Y, light.Z) - sample;
                if (Dot(delta, delta) >= radius * radius)
                {
                    continue;
                }
            }
            if (vertex.LightKind.X == 1)
            {
                if ((flags & 4u) == 4u)
                {
                    sum += Radial(origin, light, source.Colour, intensity);
                }

                if ((flags & 256u) == 256u
                    && InstancePoint(sample, centre, vertex.Position, vertex.Normal, m, light, source.Colour, intensity,
                        out Vector3 contribution))
                {
                    sum += contribution;
                }
            }
            else if (Point(vertex.WorldPosition, vertex.WorldNormal, light, source.Colour, intensity, out Vector3 contribution))
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

    /// <summary>The light as the vertex passes it on: each channel floored to 1/255 (with Unity's 0.00001 nudge) and
    /// clamped to 0..1.</summary>
    public static Vector3 PackedDiffuse(Vector3 sum)
    {
        return new Vector3(Pack(sum.X), Pack(sum.Y), Pack(sum.Z));
    }

    private static float Pack(float channel)
    {
        return Math.Min((float)Math.Floor((channel > 0f ? channel : 0f) * 255f + 0.00001f), 255f) / 255f;
    }

    /// <summary>A dot product summed left to right, as the shaders write it (no SIMD reordering).</summary>
    private static float Dot(Vector3 a, Vector3 b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    /// <summary>The object's basis applied to a direction: <c>mul((float3x3)ObjectToWorld, v)</c>, each row of the
    /// column-vector basis (a column of the row-vector <paramref name="m"/>) dotted with it.</summary>
    private static Vector3 Basis(in Matrix4x4 m, Vector3 v)
    {
        return new Vector3(
        m.M11 * v.X + m.M21 * v.Y + m.M31 * v.Z,
        m.M12 * v.X + m.M22 * v.Y + m.M32 * v.Z,
        m.M13 * v.X + m.M23 * v.Y + m.M33 * v.Z);
    }

    /// <summary>The distance bin, <c>round(d² x 16 - 0.49999)</c> to even in float.</summary>
    private static int Bin(float distanceSquared)
    {
        return (int)Math.Round((double)(distanceSquared * 16f - 0.49999f), MidpointRounding.ToEven);
    }

    private static bool Point(Vector3 vertex, Vector3 normal, Vector4 light, Vector3 colour, float intensity, out Vector3 contribution)
    {
        contribution = Vector3.Zero;
        Vector3 toLight = new Vector3(light.X, light.Y, light.Z) - vertex;
        float distanceSquared = Dot(toLight, toLight), radiusSquared = light.W;
        if (radiusSquared <= 0 || distanceSquared >= radiusSquared)
        {
            return true;
        }

        float incidence = Dot(normal, toLight) * intensity;
        if (BitConverter.SingleToInt32Bits(incidence) < 0)
        {
            return true;
        }

        int index = Bin(distanceSquared);
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

            contribution = colour;
            return true;
        }
        float factor = incidence * (0.25f + 16f / index) * (radiusSquared - distanceSquared) / radiusSquared;
        contribution = colour * (factor <= 1f ? factor : 1f);
        return true;
    }

    private static bool InstancePoint(Vector3 sample, Vector3 centre, Vector3 vertex, Vector3 normal, in Matrix4x4 m,
        Vector4 light, Vector3 colour, float intensity, out Vector3 contribution)
    {
        contribution = Vector3.Zero;
        Vector3 delta = new Vector3(light.X, light.Y, light.Z) - sample;
        float distanceSquared = (delta.X * delta.X + delta.Y * delta.Y) + delta.Z * delta.Z, radiusSquared = light.W;
        if (radiusSquared <= 0 || distanceSquared >= radiusSquared)
        {
            return true;
        }

        int index = Bin(distanceSquared);
        if (index < 0 || index >= 256)
        {
            return false;
        }
        // The basis transposed: each basis column dotted with the delta.
        var direction = new Vector3(
            m.M11 * delta.X + m.M12 * delta.Y + m.M13 * delta.Z,
            m.M21 * delta.X + m.M22 * delta.Y + m.M23 * delta.Z,
            m.M31 * delta.X + m.M32 * delta.Y + m.M33 * delta.Z);
        Vector3 d = direction - (vertex - centre);
        float incidence = ((d.X * normal.X + d.Z * normal.Z) + d.Y * normal.Y) * intensity;
        if (BitConverter.SingleToInt32Bits(incidence) < 0)
        {
            return true;
        }

        if (index == 0)
        {
            if (incidence == 0)
            {
                return false;
            }

            contribution = colour;
            return true;
        }
        float attenuation = (radiusSquared - distanceSquared) * (0.25f + 16f / index) * (1f / radiusSquared);
        if (!(attenuation > 0))
        {
            return true;
        }

        float factor = incidence * attenuation;
        contribution = colour * (factor <= 1f ? factor : 1f);
        return true;
    }

    private static Vector3 Radial(Vector3 sample, Vector4 light, Vector3 colour, float intensity)
    {
        Vector3 delta = new Vector3(light.X, light.Y, light.Z) - sample;
        float distanceSquared = Dot(delta, delta), radiusSquared = light.W;
        if (radiusSquared <= 0 || distanceSquared >= radiusSquared)
        {
            return Vector3.Zero;
        }

        int index = Bin(distanceSquared);
        if (index < 0 || index >= 256)
        {
            return Vector3.Zero;
        }

        if (index == 0)
        {
            return colour;
        }

        float factor = (0.25f + 16f / index) * (radiusSquared - distanceSquared) * intensity * (1f / radiusSquared);
        return colour * (factor <= 1f ? factor : 1f);
    }
}
