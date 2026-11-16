// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;

namespace DK2.Presentation;

/// <summary>One hover overlay draw: the hit, whether its draw flags hold 0x400 (<see cref="HoverTint.CoversBlended"/>),
/// and whether the creature's red pass was already drawn this frame.</summary>
public readonly record struct HoverOverlayDraw(WorldHoverHit Hit, bool CoversBlended, bool RedAlreadyDrawn);

/// <summary>What one submesh of a hover overlay draws: the hover texture swap, then the red pass.</summary>
public readonly record struct HoverSubmeshDraw(bool Swap, bool Red);

/// <summary>The Hand's hover tint as draws: DK2 draws the hover id a second time with AA_RedTexture each frame
/// (Presentation_Update, 0x0043BE40). A gold object first swaps each material to its _HIGHLIGHT twin; the red pass skips
/// blended materials unless the draw covers them (<see cref="HoverTint.DrawsOverlay"/>; gold-hover-and-sparkle.md §2).
/// A creature drawn red once a frame (burning, or the hover) is not drawn red again, except on its blended materials
/// when the draw covers them. The adapter draws the meshes; this keeps the frame's red creatures.</summary>
public sealed class HoverOverlayPlan
{
    private readonly HashSet<int> _redCreatures = new();

    /// <summary>Starts a frame: no creature is red yet.</summary>
    public void BeginFrame() => _redCreatures.Clear();

    /// <summary>Plans a draw of <paramref name="hit"/>; false when it draws nothing, a creature already red this frame
    /// whose draw does not cover blended materials.</summary>
    public bool TryBegin(WorldHoverHit hit, bool coversBlended, out HoverOverlayDraw draw)
    {
        bool redAlreadyDrawn = hit.Kind == WorldHoverKind.Creature && _redCreatures.Contains(hit.EntityId);
        draw = new HoverOverlayDraw(hit, coversBlended, redAlreadyDrawn);
        return !redAlreadyDrawn || coversBlended;
    }

    /// <summary>What one submesh draws: the swap when its hover texture exists, the red pass unless the creature is
    /// already red and the material is not blended, and only where <see cref="HoverTint.DrawsOverlay"/> lets it.</summary>
    public static HoverSubmeshDraw Submesh(in HoverOverlayDraw draw, bool swapAvailable, bool blended) =>
        new(swapAvailable, (!draw.RedAlreadyDrawn || blended) && HoverTint.DrawsOverlay(blended, draw.CoversBlended));

    /// <summary>Records a drawn overlay: a creature is red for the rest of the frame.</summary>
    public void Drawn(in HoverOverlayDraw draw)
    {
        if (draw.Hit.Kind == WorldHoverKind.Creature) _redCreatures.Add(draw.Hit.EntityId);
    }
}
