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
        if (light is null || ((light.Flags & 1) == 0 && (light.Red | light.Green | light.Blue) == 0)) return null;
        uint radius = unchecked((uint)light.Radius << 1) / 3;
        radius = Math.Min(radius, 10112u);
        if (radius == 0) return null;
        int red = light.Red + ((light.Flags & 0x800) != 0 ? 256 : 0);
        int green = light.Green + ((light.Flags & 0x1000) != 0 ? 256 : 0);
        int blue = light.Blue + ((light.Flags & 0x2000) != 0 ? 256 : 0);
        if ((light.Flags & 0x80) != 0)
        {
            var colour = ClassicKeeperPalette.Colour(new PlayerId(thing.OwnerPlayerId));
            red = Math.Max(32, red * colour.Red / 255);
            green = Math.Max(32, green * colour.Green / 255);
            blue = Math.Max(32, blue * colour.Blue / 255);
        }
        float t = Math.Max(0, Math.Min(1, fraction));
        var position = new Vector3(
            (thing.PreviousPosX + ((float)thing.PosX - thing.PreviousPosX) * t + light.X) / Fp32.One,
            (thing.PreviousHeight + ((float)thing.Height - thing.PreviousHeight) * t + light.Z) / Fp32.One,
            -(thing.PreviousPosY + ((float)thing.PosY - thing.PreviousPosY) * t + light.Y) / Fp32.One);
        return new ClassicPointLight(position, new Vector3(red / 255f, green / 255f, blue / 255f),
            intensity: 180f / 128, radius: radius / (float)Fp32.One,
            cheapLighting ? ClassicLightFlags.CheapObject : ClassicLightFlags.NormalObject);
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
