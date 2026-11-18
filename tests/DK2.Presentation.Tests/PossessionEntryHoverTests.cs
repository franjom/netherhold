// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class PossessionEntryHoverTests
{
    [Theory]
    [InlineData(WorldHoverKind.Creature, true)]
    [InlineData(WorldHoverKind.Object, false)]
    [InlineData(WorldHoverKind.Door, false)]
    [InlineData(WorldHoverKind.Trap, false)]
    public void ValidSpellEntryHoverPublishesOnlyACreature(WorldHoverKind kind, bool published)
    {
        var pointer = new WorldHoverHit(kind, new TilePos(3, 7), 42);
        WorldHoverHit? result = PossessionHighlight.EntryPointerTarget(pointer, entering: true,
            ordinaryTargetAllowed: false, spellTargetAllowed: true);
        Assert.Equal(published ? pointer : (WorldHoverHit?)null, result);
    }

    [InstallFact]
    public void OrdinaryHandAndSharedFlowerRulesStillAcceptThePointedPossessedSkeleton()
    {
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var viewer = new PlayerId(PlayerIds.FirstKeeper);
        WorldState world = WorldState.FromMap(database, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        CreatureState skeleton = world.SpawnCreature(database, 9, viewer, new TilePos(4, 4));
        skeleton.Flags |= 8;
        world.Player(viewer).PossessedCreatureId = skeleton.Id;
        var pointer = new WorldHoverHit(WorldHoverKind.Creature, skeleton.Tile, skeleton.Id);
        bool qualified = HoverTint.Applies(world, database, pointer, viewer);
        Assert.True(qualified);
        Assert.Equal(pointer, PossessionHighlight.EntryPointerTarget(pointer, entering: true, qualified));
        var flowers = new StatusFlowers(database, viewer);
        Assert.Equal(skeleton.Id, Assert.Single(flowers.Capture(world, world.CurrentTick, new(skeleton.Id))).CreatureId);
        Assert.Empty(flowers.Capture(world, world.CurrentTick, CreatureQueryTarget.None));
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, false)]
    public void OnlyTheVisibleEntryHudKeepsOrdinaryPointerFeedback(bool possessing, bool settled, bool guiVisible, bool expected)
    {
        Assert.Equal(expected, PossessionHighlight.EntryPointerFeedback(possessing, settled, new(guiVisible, false)));
    }

    [Fact]
    public void EntryTintNamesTheActualQualifiedPointerCreatureRatherThanThePossessedBody()
    {
        var pointer = new WorldHoverHit(WorldHoverKind.Creature, new TilePos(3, 7), 42);
        Assert.Equal(pointer, PossessionHighlight.EntryPointerTarget(pointer, entering: true, ordinaryTargetAllowed: true));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void LandingOrAnOrdinaryToolRefusalDoesNotManufactureATint(bool entering, bool targetAllowed)
    {
        var pointer = new WorldHoverHit(WorldHoverKind.Creature, new TilePos(3, 7), 42);
        Assert.Null(PossessionHighlight.EntryPointerTarget(pointer, entering, targetAllowed));
    }

    [Theory]
    [InlineData(WorldHoverKind.None)]
    [InlineData(WorldHoverKind.Tile)]
    [InlineData(WorldHoverKind.Widget)]
    public void NoPointedThingIncludingAnOverHudPickProducesNoEntryTint(WorldHoverKind kind)
    {
        Assert.Null(PossessionHighlight.EntryPointerTarget(new(kind, new TilePos(3, 7)), entering: true, ordinaryTargetAllowed: true));
        Assert.Null(PossessionHighlight.EntryPointerTarget(null, entering: true, ordinaryTargetAllowed: true));
    }
}
