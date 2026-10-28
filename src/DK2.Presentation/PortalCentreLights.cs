// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

public readonly record struct PortalLightId(int Value);
public readonly record struct PortalLightView(PortalLightId Id, ClassicPointLight Light);

/// <summary>Standalone entrance sources and the native wall-clock pulse; portal-visibility.md.</summary>
public sealed class PortalCentreLights
{
    private sealed class Source(TilePos tile)
    {
        public readonly TilePos Tile = tile;
        public int Phase = 1;
        public bool Rising = true;
        public TerrainLightColour Colour = new(255, 33, 26);
    }

    private readonly List<Source> _sources = new();
    private readonly List<PortalLightView> _lights = new();
    private long _anchorMilliseconds;

    public PortalCentreLights(EffectTime start) => _anchorMilliseconds = start.Milliseconds;
    public void Create(TilePos tile) => _sources.Add(new Source(tile));

    public IReadOnlyList<PortalLightView> Advance(EffectTime now, bool cheapLighting = false)
    {
        long elapsed = now.Milliseconds - _anchorMilliseconds;
        if (elapsed < 0) throw new ArgumentOutOfRangeException(nameof(now));
        if (elapsed > 50)
        {
            _anchorMilliseconds += elapsed / 50 * 50;
            foreach (Source source in _sources)
            {
                int q = source.Phase;
                // The native bit4 pulse swaps the stored green and blue (0x0043A2E0).
                source.Colour = new((byte)(255 * q >> 8), (byte)(26 * q >> 8), (byte)(33 * q >> 8));
                source.Phase += source.Rising ? 10 : -10;
                if (source.Phase > 249) source.Rising = false;
                else if (source.Phase < 11) source.Rising = true;
            }
        }
        _lights.Clear();
        for (int i = 0; i < _sources.Count; i++)
        {
            Source source = _sources[i];
            _lights.Add(new PortalLightView(new PortalLightId(i), ClassicPointLight.FromPortal(source.Tile, source.Colour, cheapLighting)));
        }
        return _lights;
    }
}
