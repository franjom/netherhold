// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Data;
using DK2.Data.Text;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class WorldHoverTextTests
{
    [InstallFact]
    public void ShippedHeartAndPortalNamesAndCreatureStatusComeFromSettledDataWithoutChangingHash()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "Level1")!;
        WorldState world = WorldState.FromMap(database, map, [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        Dk2StringTable strings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", "Text.str"));
        var capture = new WorldHoverText(database, strings.Resolve, PlayerIds.FirstKeeper);
        RoomInstance heart = RoomIndex.Build(world, database).First(r => r.Definition.TypeId == RoomTypeId.DungeonHeart);
        var hit = new WorldHoverHit(WorldHoverKind.Tile, world.TilePosition(heart.TileIndices[0]));
        HoverTextSnapshot snapshot = capture.Capture(world, hit);
        Assert.Equal("Dungeon Heart", snapshot.TooltipName);
        Assert.StartsWith("Dungeon Heart.  Health:", snapshot.TopStatus);
        Assert.DoesNotContain("%37", snapshot.TopStatus);
        RoomInstance portal = RoomIndex.Build(world, database).First(r => r.Definition.TypeId == RoomTypeId.Portal);
        foreach (int tileIndex in portal.TileIndices) world.TileAtIndex(tileIndex).OwnerPlayerId = PlayerIds.FirstKeeper;
        var portalCapture = new WorldHoverText(database, strings.Resolve, PlayerIds.FirstKeeper);
        HoverTextSnapshot portalText = portalCapture.Capture(world, new(WorldHoverKind.Tile, world.TilePosition(portal.TileIndices[0])));
        Assert.Equal("Portal", portalText.TooltipName);
        Assert.Contains("Portals owned: 1.", portalText.TopStatus);
        Assert.DoesNotContain("%43", portalText.TopStatus);
        CreatureState imp = world.SpawnCreature(database, database.CreatureByName("Imp")!.CreatureId,
            PlayerIds.FirstKeeper, hit.Tile);
        imp.Dk2StateId = 8; imp.UtilityNewState = 14;
        var simulation = new GameSimulation(database, world, seed: 7);
        ulong before = simulation.StateHash();
        var creatureHit = new WorldHoverHit(WorldHoverKind.Creature, hit.Tile, imp.Id);
        HoverTextSnapshot creature = capture.Capture(world, creatureHit, "Delver");
        Assert.Contains("Name: Delver", creature.TopStatus);
        Assert.Contains("Job: " + strings.Resolve(2600), creature.TopStatus);
        Assert.Contains("Efficiency: 80%", creature.TopStatus);
        Assert.Contains("Mood: Happy", creature.TopStatus);
        Assert.Equal(before, simulation.StateHash());
        Assert.Same(creature.TopStatus, capture.Capture(world, creatureHit, "Delver").TopStatus);
    }

    [Fact]
    public void LocalizedTokensAreParsedWholeAndReplacementTextIsNeverReparsed()
    {
        Assert.Equal("Imp.  Name: %30.  Efficiency: 80%.", WorldHoverText.Substitute(
            "%30.  Name: %29.  Efficiency: %74%.",
            new Dictionary<int, string> { [30] = "Imp", [29] = "%30", [74] = "80" }));
    }

    [InstallFact]
    public void UnknownTilesNeverRevealCreatureStatusAndNonOwnedRoomsUseTheOriginalWarning()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        WorldState world = WorldState.FromMap(database, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: true);
        Dk2StringTable strings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", "Text.str"));
        RoomInstance portal = RoomIndex.Build(world, database).First(r => r.Definition.TypeId == RoomTypeId.Portal);
        TilePos tile = world.TilePosition(portal.TileIndices[0]);
        world.Tile(tile).ClearExploredBy(PlayerIds.FirstKeeper);
        var fog = new WorldHoverText(database, strings.Resolve, PlayerIds.FirstKeeper);
        HoverTextSnapshot hidden = fog.Capture(world, new(WorldHoverKind.Creature, tile, 999));
        Assert.Equal("Fog of War", hidden.TooltipName);
        Assert.Equal(strings.Resolve(2473), hidden.TopStatus);
        world.Tile(tile).SetExploredBy(PlayerIds.FirstKeeper);
        var visible = new WorldHoverText(database, strings.Resolve, PlayerIds.FirstKeeper);
        HoverTextSnapshot neutral = visible.Capture(world, new(WorldHoverKind.Tile, tile));
        Assert.Equal("Portal", neutral.TooltipName);
        Assert.Equal(strings.Resolve(2471), neutral.TopStatus);
    }

    [Theory]
    [InlineData(1, 0, 2599)]
    [InlineData(8, 14, 2600)]
    [InlineData(-1, 0, 2599)]
    public void WalkingDisplaysTheDestinationStatesJob(int state, int resume, int expected)
    {
        Assert.Equal(expected, WorldHoverText.ActionStringId(state, resume));
    }

    [Fact]
    public void WorldTooltipWaitsForTheSameTargetAndCanHideOnlyCreatureNames()
    {
        var delay = new HoverTooltipDelay();
        var creature = new WorldHoverHit(WorldHoverKind.Creature, new TilePos(2, 3), 4);
        var snapshot = new HoverTextSnapshot("status", "Imp", creature);
        Assert.False(delay.Visible(snapshot, 100));
        Assert.False(delay.Visible(snapshot, 1600));
        Assert.True(delay.Visible(snapshot, 1601));
        Assert.False(delay.Visible(snapshot, 1700, showCreatureTooltip: false));
        Assert.Equal("status", snapshot.TopStatus);
        Assert.False(delay.Visible(snapshot with { Hit = creature with { EntityId = 5 } }, 1800));
        Assert.False(delay.Visible(HoverTextSnapshot.Empty, 5000));
    }
}
