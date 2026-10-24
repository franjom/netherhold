// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public sealed class HoverPickRulesTests
{
    /// <summary>Thing_DrawRenderInfo (0x0059B880) tags a creature's mesh only with CanBePickedUp and
    /// not dying, and an object's with CanBePickedUp, CanBeSlapped or Highlightable.</summary>
    [Fact]
    public void OnlyTaggedMeshesEnterTheRenderedHit()
    {
        Assert.True(HoverPickRules.RenderedCreature(CreatureFlags.CanBePickedUp, dying: false));
        Assert.False(HoverPickRules.RenderedCreature(CreatureFlags.CanBePickedUp, dying: true));
        Assert.False(HoverPickRules.RenderedCreature(CreatureFlags.CanBeSlapped, dying: false));
        Assert.True(HoverPickRules.RenderedObject(ObjectFlags.CanBeSlapped));
        Assert.True(HoverPickRules.RenderedObject(ObjectFlags.Highlightable));
        Assert.False(HoverPickRules.RenderedObject(ObjectFlags.Obstacle | ObjectFlags.TypeGold));
    }

    /// <summary>0x005992B0 gives a door (kind 5) or trap (kind 7) only to its owner; another keeper's
    /// leaves the hit on the tile.</summary>
    [Fact]
    public void OnlyTheViewersOwnDoorOrTrapResolves()
    {
        Assert.True(HoverPickRules.ResolvesStructure(PlayerIds.FirstKeeper, PlayerIds.FirstKeeper));
        Assert.False(HoverPickRules.ResolvesStructure(PlayerIds.Good, PlayerIds.FirstKeeper));
        var viewer = new PlayerId(PlayerIds.FirstKeeper);
        Assert.True(HoverPickRules.Resolves(WorldHoverKind.Door, PlayerIds.FirstKeeper, viewer));
        Assert.False(HoverPickRules.Resolves(WorldHoverKind.Trap, (byte)(PlayerIds.FirstKeeper + 1), viewer));
        Assert.False(HoverPickRules.Resolves(WorldHoverKind.Door, null, viewer));
        Assert.True(HoverPickRules.Resolves(WorldHoverKind.Creature, (byte)(PlayerIds.FirstKeeper + 1), viewer));
    }

    /// <summary>0x005992B0's object arm (kind 4): the viewer's own object anywhere; a neutral one only on
    /// the viewer's or neutral ground; another keeper's never.</summary>
    [Fact]
    public void AnObjectResolvesForItsOwnerOrWhenNeutralOnOwnOrNeutralGround()
    {
        var viewer = new PlayerId(PlayerIds.FirstKeeper);
        byte rival = PlayerIds.FirstKeeper + 1;
        Assert.True(HoverPickRules.ResolvesObject(PlayerIds.FirstKeeper, rival, viewer));
        Assert.True(HoverPickRules.ResolvesObject(PlayerIds.Neutral, PlayerIds.FirstKeeper, viewer));
        Assert.True(HoverPickRules.ResolvesObject(PlayerIds.Neutral, PlayerIds.Neutral, viewer));
        Assert.False(HoverPickRules.ResolvesObject(PlayerIds.Neutral, rival, viewer));
        Assert.False(HoverPickRules.ResolvesObject(PlayerIds.Neutral, PlayerIds.Unowned, viewer));
        Assert.False(HoverPickRules.ResolvesObject(rival, PlayerIds.FirstKeeper, viewer));
        Assert.False(HoverPickRules.ResolvesObject(PlayerIds.Good, PlayerIds.Neutral, viewer));
    }

    /// <summary>0x00588A60 / 0x00575F10: the pointer must fall inside the projected bounding sphere;
    /// a sphere reaching behind the eye always holds it.</summary>
    [Fact]
    public void TheScreenCircleIsTheProjectedBoundingSphere()
    {
        Assert.True(HoverPickRules.CircleHolds(10f, 2f, 3.9f));
        Assert.False(HoverPickRules.CircleHolds(10f, 2f, 4f));
        Assert.True(HoverPickRules.CircleHolds(1f, 2f, 100f));
        Assert.False(HoverPickRules.CircleHolds(2f, 2f, 100f));
    }

    /// <summary>0x0044DEC0: no creature of type 0x2E, dying, or in states 0xD6/0x4C/0xD1; objects
    /// with CanBePickedUp or of type 13.</summary>
    [Fact]
    public void TheFallbackSweepHasItsOwnExclusions()
    {
        Assert.True(HoverPickRules.FallbackCreature(3, dying: false, stateId: 7));
        Assert.False(HoverPickRules.FallbackCreature(0x2E, dying: false, stateId: 7));
        Assert.False(HoverPickRules.FallbackCreature(3, dying: true, stateId: 7));
        foreach (int state in new[] { 0xD6, 0x4C, 0xD1 })
            Assert.False(HoverPickRules.FallbackCreature(3, dying: false, stateId: state));
        Assert.True(HoverPickRules.FallbackObject(ObjectFlags.CanBePickedUp, 9));
        Assert.True(HoverPickRules.FallbackObject(0, 13));
        Assert.False(HoverPickRules.FallbackObject(ObjectFlags.CanBeSlapped, 9));
    }

    /// <summary>0x00572820: a triangle under the pointer beats any nearer centre; with no triangle
    /// hit the nearest centre wins.</summary>
    [Fact]
    public void ATriangleHitBeatsANearerCentre()
    {
        var pick = new RenderedPick();
        Assert.False(pick.TryResult(out _, out _));
        pick.Offer(0, null, 1f);
        pick.Offer(1, 9f, 8f);
        pick.Offer(2, 5f, 6f);
        Assert.True(pick.TryResult(out int item, out bool byTriangle));
        Assert.Equal(2, item);
        Assert.True(byTriangle);

        var centres = new RenderedPick();
        centres.Offer(0, null, 4f);
        centres.Offer(1, null, 2f);
        Assert.True(centres.TryResult(out item, out byTriangle));
        Assert.Equal(1, item);
        Assert.False(byTriangle);
    }
}
