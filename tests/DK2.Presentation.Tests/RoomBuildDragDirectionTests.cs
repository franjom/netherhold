// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class RoomBuildDragDirectionTests
{
    [InstallTheory]
    [InlineData(1, 1, 2, 2)]
    [InlineData(2, 2, 1, 1)]
    [InlineData(1, 2, 2, 1)]
    [InlineData(2, 1, 1, 2)]
    public void MouseDragBuildsAndEmitsEffectsFromReleaseCornerBackToPress(
        int pressX, int pressY, int releaseX, int releaseY)
    {
        var database = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level8");
        byte claimedPath = database.Terrain.Values.Single(t => t.Name == "Dirt Path").MaxHealthTypeTerrainId;
        var map = new LevelMap(7, 7, Enumerable.Repeat(
            new MapTile(claimedPath, PlayerIds.FirstKeeper, 0, 0), 49).ToArray());
        var world = WorldState.FromMap(database, map,
            [PlayerIds.Unowned, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        RoomRules.Place(database, ref world.Tile(new TilePos(3, 1)),
            database.Room(RoomTypeId.Treasury), PlayerIds.FirstKeeper);
        GoldStore.Deposit(world, database, PlayerIds.FirstKeeper, 10000);
        var simulation = new GameSimulation(database, world, 7,
            [new RoomBuildSystem(), new RoomBuildWaveSystem()]);
        var events = new ListEventSink();
        simulation.ObserveWith(events);
        var translator = new PlayerInputTranslator();
        var snapshot = new PlayerInputSnapshot(false);
        var press = new TilePos(pressX, pressY);
        var release = new TilePos(releaseX, releaseY);
        Assert.Empty(translator.Translate(new PlayerIntent(PlayerIds.FirstKeeper,
            PlayerToolMode.BuildRoom, press, PointerButtonTransition.Pressed, RoomTypeId.Lair), snapshot).All);

        simulation.Tick(translator.Translate(new PlayerIntent(PlayerIds.FirstKeeper,
            PlayerToolMode.BuildRoom, release, PointerButtonTransition.Released, RoomTypeId.Lair), snapshot));
        Assert.Equal(database.Room(RoomTypeId.Lair).TerrainId, world.Tile(release).TerrainId);
        Assert.Equal(claimedPath, world.Tile(press).TerrainId);
        Assert.Equal(release, Assert.Single(Effects()).Tile);
        simulation.Run(2);

        Assert.Equal(new[] { release, new TilePos(releaseX, pressY),
            new TilePos(pressX, releaseY), press }, Effects().Select(e => e.Tile));
        Assert.All(Effects(), e => Assert.Equal(database.Room(RoomTypeId.Lair).TerrainId, world.Tile(e.Tile).TerrainId));

        RoomConstructionEffect[] Effects() => events.Events
            .Where(e => e.ConstructionEffect.HasValue).Select(e => e.ConstructionEffect!.Value).ToArray();
    }
}
