// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation.Tests;

/// <summary>The hover tint's draws moved out of Unity's DungeonScene (migration plan step 2j): the frame's red creatures
/// and each submesh's swap and red pass.</summary>
public sealed class HoverOverlayPlanTests
{
    private static readonly WorldHoverHit Imp = new WorldHoverHit(WorldHoverKind.Creature, new TilePos(1, 1), 7);
    private static readonly WorldHoverHit Gold = new WorldHoverHit(WorldHoverKind.Object, new TilePos(1, 1), 9);

    [Fact]
    public void AFirstDrawDrawsRedOnOpaqueMaterialsOnly()
    {
        var plan = new HoverOverlayPlan();
        plan.BeginFrame();
        Assert.True(plan.TryBegin(Imp, coversBlended: false, out HoverOverlayDraw draw));
        Assert.False(draw.RedAlreadyDrawn);
        Assert.Equal(new HoverSubmeshDraw(false, true), HoverOverlayPlan.Submesh(draw, swapAvailable: false, blended: false));
        Assert.Equal(new HoverSubmeshDraw(false, false), HoverOverlayPlan.Submesh(draw, swapAvailable: false, blended: true));
    }

    [Fact]
    public void ACoveringDrawAlsoTintsBlendedMaterials()
    {
        var plan = new HoverOverlayPlan();
        Assert.True(plan.TryBegin(Gold, coversBlended: true, out HoverOverlayDraw draw));
        Assert.Equal(new HoverSubmeshDraw(true, true), HoverOverlayPlan.Submesh(draw, swapAvailable: true, blended: true));
        Assert.Equal(new HoverSubmeshDraw(true, true), HoverOverlayPlan.Submesh(draw, swapAvailable: true, blended: false));
    }

    [Fact]
    public void TheSwapFollowsTheHoverTextureWhateverTheRedPass()
    {
        var plan = new HoverOverlayPlan();
        Assert.True(plan.TryBegin(Gold, coversBlended: false, out HoverOverlayDraw draw));
        Assert.Equal(new HoverSubmeshDraw(true, false), HoverOverlayPlan.Submesh(draw, swapAvailable: true, blended: true));
        Assert.Equal(new HoverSubmeshDraw(false, true), HoverOverlayPlan.Submesh(draw, swapAvailable: false, blended: false));
    }

    [Fact]
    public void ACreatureDrawnRedOnceThisFrameIsNotDrawnRedAgain()
    {
        var plan = new HoverOverlayPlan();
        plan.BeginFrame();
        Assert.True(plan.TryBegin(Imp, coversBlended: false, out HoverOverlayDraw first));
        plan.Drawn(first);

        Assert.False(plan.TryBegin(Imp, coversBlended: false, out _));
        // A covering draw still tints the blended materials the first draw skipped, and only those.
        Assert.True(plan.TryBegin(Imp, coversBlended: true, out HoverOverlayDraw again));
        Assert.True(again.RedAlreadyDrawn);
        Assert.Equal(new HoverSubmeshDraw(false, true), HoverOverlayPlan.Submesh(again, swapAvailable: false, blended: true));
        Assert.Equal(new HoverSubmeshDraw(false, false), HoverOverlayPlan.Submesh(again, swapAvailable: false, blended: false));
        // Another creature is not red yet.
        Assert.True(plan.TryBegin(Imp with { EntityId = 8 }, coversBlended: false, out HoverOverlayDraw other));
        Assert.False(other.RedAlreadyDrawn);
    }

    [Fact]
    public void OnlyACreatureStaysRedAndOnlyOnceDrawn()
    {
        var plan = new HoverOverlayPlan();
        Assert.True(plan.TryBegin(Gold, coversBlended: false, out HoverOverlayDraw gold));
        plan.Drawn(gold);
        Assert.True(plan.TryBegin(Gold, coversBlended: false, out _));
        // A planned draw that was never drawn (no mesh) leaves the creature untinted.
        Assert.True(plan.TryBegin(Imp, coversBlended: false, out _));
        Assert.True(plan.TryBegin(Imp, coversBlended: false, out _));
    }

    [Fact]
    public void ANewFrameForgetsTheRedCreatures()
    {
        var plan = new HoverOverlayPlan();
        Assert.True(plan.TryBegin(Imp, coversBlended: false, out HoverOverlayDraw draw));
        plan.Drawn(draw);
        plan.BeginFrame();
        Assert.True(plan.TryBegin(Imp, coversBlended: false, out HoverOverlayDraw next));
        Assert.False(next.RedAlreadyDrawn);
    }
}
