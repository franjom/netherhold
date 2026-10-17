// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

public enum MinimapMarkerKind { Creature, Trap, VerticalDoor, HorizontalDoor }

[Flags]
public enum MinimapMarkerFlags : byte
{
    None = 0, Explored = 1, InViewerHand = 2, PickupPending = 4, Hidden = 8,
}

public readonly record struct MinimapMarkerFacts(
    MinimapMarkerKind Kind, FpPosition3 Position, PlayerId Owner, MinimapMarkerFlags Flags);

public readonly record struct MinimapMarker(MinimapMarkerKind Kind, FpPosition3 Position, uint ColourArgb);

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/minimap-rendering.md")]
public static class MinimapMarkerRules
{
    public static bool TryCreate(in MinimapMarkerFacts facts, PlayerId viewer, GameTick tick,
        MinimapPalette palette, out MinimapMarker marker)
    {
        marker = default;
        bool own = facts.Owner == viewer;
        if (!own && (facts.Flags & MinimapMarkerFlags.Explored) == 0) return false;
        if (facts.Kind == MinimapMarkerKind.Creature
            && (facts.Flags & (MinimapMarkerFlags.InViewerHand | MinimapMarkerFlags.PickupPending)) != 0) return false;
        if (facts.Kind == MinimapMarkerKind.Trap && !own && (facts.Flags & MinimapMarkerFlags.Hidden) != 0) return false;
        bool door = facts.Kind is MinimapMarkerKind.VerticalDoor or MinimapMarkerKind.HorizontalDoor;
        bool coloured = (tick.Value & 1) == (door ? 0 : 1);
        uint colour = coloured ? palette.Colours[17 + facts.Owner.Value] : 0xFF000000;
        marker = new(facts.Kind, facts.Position, colour);
        return true;
    }
}

public readonly record struct MinimapMapBounds(int Left, int Top, int Width, int Height)
{
    public static MinimapMapBounds Fit(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        return width < height
            ? new(20 + ((height - width) / 2) * 90 / height, 20, width * 90 / height, 90)
            : new(20, 20 + ((width - height) / 2) * 90 / width, 90, height * 90 / width);
    }
}
