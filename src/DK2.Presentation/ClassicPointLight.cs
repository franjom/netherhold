// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>Native light inputs and a reference vertex contribution; classic-lighting.md section5.</summary>
public readonly record struct ClassicPointLight
{
    private ClassicPointLight(Vector3 position, Vector3 colour, float intensity, float radius, ClassicLightFlags flags)
    {
        Position = position;
        Colour = colour;
        Intensity = intensity;
        Radius = radius;
        Flags = flags;
    }

    public Vector3 Position { get; }
    public Vector3 Colour { get; }
    public float Intensity { get; }
    public float Radius { get; }
    public ClassicLightFlags Flags { get; }

    public static ClassicPointLight FromTerrain(TilePos tile, Fp32 height, TerrainLightColour colour)
        => new(new Vector3(tile.X + .5f, height.Raw / (float)Fp32.One, -tile.Y - .5f),
            new Vector3(colour.Red / 255f, colour.Green / 255f, colour.Blue / 255f),
            intensity: 1, radius: 4608f / Fp32.One, ClassicLightFlags.Terrain);

    /// <summary>The existing vertex-light reference uses the cheap torch classification.</summary>
    public static ClassicPointLight FromTorch(in TorchView torch)
        => FromTorch(torch, ClassicLightFlags.CheapTorch);

    public static ClassicPointLight FromTorch(in TorchView torch, ClassicLightFlags flags)
    {
        if (torch.Radius < 0 || torch.Radius > TorchLayout.MaxRadius)
            throw new ArgumentOutOfRangeException(nameof(torch), "Torch radius must already satisfy the native clamp.");
        return new ClassicPointLight(
            new Vector3(torch.PosX / (float)Fp32.One, torch.Height / (float)Fp32.One, -torch.PosY / (float)Fp32.One),
            new Vector3(torch.Red / 255f, torch.Green / 255f, torch.Blue / 255f),
            intensity: torch.Intensity / 128f, radius: torch.Radius / (float)Fp32.One, flags);
    }

    public static ClassicPointLight? FromObject(in ObjectView thing, bool cheapLighting = false, float fraction = 1)
    {
        KwdLight? light = thing.Light;
        if (light is null) return null;
        float t = Math.Max(0, Math.Min(1, fraction));
        return FromDescriptor(light, new PlayerId(thing.OwnerPlayerId),
            thing.PreviousPosX + ((float)thing.PosX - thing.PreviousPosX) * t,
            thing.PreviousPosY + ((float)thing.PosY - thing.PreviousPosY) * t,
            thing.PreviousHeight + ((float)thing.Height - thing.PreviousHeight) * t,
            intensity: 180, rampStep: 1, rampSteps: 1, cheapLighting);
    }

    /// <summary>An effect instance's light (<c>0x004347E0</c> through bridge slot <c>+0xA0</c>,
    /// intensity 0x80), at the instance's point, its colour scaled by the fade ramp's
    /// <paramref name="effect"/> step as <c>0x0043A2E0</c> scales it (base × step / steps).</summary>
    public static ClassicPointLight? FromEffect(in EffectLightView effect, bool cheapLighting = false)
        => FromDescriptor(effect.Light, effect.Owner, effect.PosX, effect.PosY, effect.Height,
            intensity: 128, effect.RampStep, effect.RampSteps, cheapLighting);

    /// <summary>The Hand's light, startup handle <c>+0x33E</c>: made by <c>0x00447D50</c> through bridge
    /// slot <c>+0xA4</c> with RGB 511, intensity 255 and radius <c>0x1C00</c>, then moved each
    /// presentation update by <c>0x0043B220</c>'s default view arm to the picked point at height
    /// <c>0x1C00</c> (<c>0x1E00</c> when the pick result's <c>+0x18</c> is set). It is white over an
    /// explored cell (<c>+9</c> bit <c>0x20</c>); an unexplored cell or one on the map's outer
    /// row or column turns it black, so null. Its classification is taken as an object light's
    /// (inferred: <c>+0x81</c>, written 0x1F or 0x3F, is unread).</summary>
    public static ClassicPointLight? FromHand(float x, float y, bool raised, bool explored, int mapWidth, int mapHeight,
        bool cheapLighting = false)
    {
        int tileX = (int)Math.Floor(x), tileY = (int)Math.Floor(y);
        if (!explored || tileX <= 0 || tileY <= 0 || tileX >= mapWidth - 1 || tileY >= mapHeight - 1) return null;
        const float channel = 511 / 255f;
        return new ClassicPointLight(new Vector3(x, (raised ? 0x1E00 : 0x1C00) / (float)Fp32.One, -y),
            new Vector3(channel, channel, channel), intensity: 255 / 128f, radius: 0x1C00 / (float)Fp32.One,
            cheapLighting ? ClassicLightFlags.CheapObject : ClassicLightFlags.NormalObject);
    }

    /// <summary>The descriptor rules of <c>0x00439880</c>: flag 1, offsets added, radius × 2 / 3
    /// clamped by the factory, 256 added under flags 0x800/0x1000/0x2000, the owner's palette under 0x80.</summary>
    private static ClassicPointLight? FromDescriptor(KwdLight light, PlayerId owner, float x, float y, float height,
        int intensity, int rampStep, int rampSteps, bool cheapLighting)
    {
        if ((light.Flags & 1) == 0 && (light.Red | light.Green | light.Blue) == 0) return null;
        uint radius = unchecked((uint)light.Radius << 1) / 3;
        radius = Math.Min(radius, 10112u);
        if (radius == 0) return null;
        int red = light.Red + ((light.Flags & 0x800) != 0 ? 256 : 0);
        int green = light.Green + ((light.Flags & 0x1000) != 0 ? 256 : 0);
        int blue = light.Blue + ((light.Flags & 0x2000) != 0 ? 256 : 0);
        if ((light.Flags & 0x80) != 0)
        {
            var colour = ClassicKeeperPalette.Colour(owner);
            red = Math.Max(32, red * colour.Red / 255);
            green = Math.Max(32, green * colour.Green / 255);
            blue = Math.Max(32, blue * colour.Blue / 255);
        }
        if (rampSteps <= 0) throw new ArgumentOutOfRangeException(nameof(rampSteps));
        red = red * rampStep / rampSteps;
        green = green * rampStep / rampSteps;
        blue = blue * rampStep / rampSteps;
        var position = new Vector3((x + light.X) / Fp32.One, (height + light.Z) / Fp32.One, -(y + light.Y) / Fp32.One);
        return new ClassicPointLight(position, new Vector3(red / 255f, green / 255f, blue / 255f),
            intensity: intensity / 128f, radius: radius / (float)Fp32.One,
            cheapLighting ? ClassicLightFlags.CheapObject : ClassicLightFlags.NormalObject);
    }

    /// <summary>The light a sprite takes at its centre (<c>0x0057C080</c>): no normal, the same distance
    /// bins as <see cref="ContributionAt"/>, the full colour in the first bin and at or past a factor
    /// of 1.</summary>
    public Vector3 RadialAt(Vector3 sample)
    {
        float distanceSquared = (Position - sample).LengthSquared(), radiusSquared = Radius * Radius;
        if (Radius <= 0 || distanceSquared >= radiusSquared) return Vector3.Zero;
        int index = (int)Math.Round(distanceSquared * 16.0 - 0.49999f, MidpointRounding.ToEven);
        if (index <= 0) return Colour;
        if (index >= 256) return Vector3.Zero;
        float factor = Intensity * (0.25f + 16f / index) * (radiusSquared - distanceSquared) / radiusSquared;
        return Colour * (factor <= 1f ? factor : 1f);
    }

    public Vector3 ContributionAt(Vector3 vertex, Vector3 normal)
    {
        Vector3 toLight = Position - vertex;
        float distanceSquared = toLight.LengthSquared(), radiusSquared = Radius * Radius;
        if (Radius <= 0 || distanceSquared >= radiusSquared) return Vector3.Zero;
        float incidence = Vector3.Dot(normal, toLight) * Intensity;
        if (BitConverter.SingleToInt32Bits(incidence) < 0) return Vector3.Zero;

        int index = (int)Math.Round(distanceSquared * 16.0 - 0.49999f, MidpointRounding.ToEven);
        if (index == 0)
        {
            if (incidence > 0) return Colour;
            throw new NotSupportedException("Native zero-incidence singular-bin propagation depends on the FPU exception mask.");
        }
        if ((uint)index >= 256) throw new ArgumentOutOfRangeException(nameof(vertex), "Native light lookup has 256 bins.");
        float lookup = 0.25f + 16f / index;
        float factor = incidence * lookup * (radiusSquared - distanceSquared) / radiusSquared;
        return Colour * (factor <= 1f ? factor : 1f);
    }
}
