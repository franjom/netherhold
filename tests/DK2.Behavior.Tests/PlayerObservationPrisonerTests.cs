// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Players;

namespace DK2.Behavior.Tests;

/// <summary>R20 groundwork: another player's creature held in the viewer's Prison is not in the creature panel until it
/// converts (owner, 2026-11-17; schema 10), only in view, and every creature in view carries its flower's level and
/// petals, since enemies' flowers always show (owner, 2026-11-17; schema 8).</summary>
public class PlayerObservationPrisonerTests
{
    private static PlayerObservation Observe(GameDatabase database, WorldState world) =>
        new PlayerObservationBuilder(database, PlayerObservationTests.Viewer).Capture(world, PlayerObservationTests.Stamp(world), []).Observation;

    [Fact]
    public void ARivalHeldInTheViewersPrisonIsSeenInTheWorldNeverInTheCreaturePanel()
    {
        (GameDatabase? database, WorldState? world) = PlayerObservationTests.World();
        CreatureState captive = world.SpawnCreature(database, PlayerObservationTests.Fighter, PlayerObservationTests.Rival, PlayerObservationTests.OwnFloor);
        captive.PrisonOwnerPlayerId = PlayerObservationTests.Viewer.Value;
        captive.Dk2StateId = PrisonRules.ReturningState;   // a captive's state (category 14 or 126)
        captive.Level = 3;

        PlayerObservation observation = Observe(database, world);
        Assert.Empty(observation.Creatures);
        PlayerVisibleThing seen = Assert.Single(observation.Things, t => t.Kind == PlayerThingKind.Creature);
        Assert.Equal((PlayerObservationTests.Rival, PlayerObservationTests.OwnFloor, 3), (seen.Owner, seen.Tile, seen.Level));
    }

    [Fact]
    public void ThePortalSummaryCarriesTheCapOfEveryPortalButNotTheArrivalsHeld()
    {
        GameDatabase basic = PlayerObservationTests.Database();
        const byte PortalTerrain = 9;
        basic.Variables.Misc[VariableTypeId.CreaturesSupportedByFirstPortal] = new MiscVariable(VariableTypeId.CreaturesSupportedByFirstPortal, 15, 0, 0);
        basic.Variables.Misc[VariableTypeId.CreaturesSupportedPerAdditionalPortal] = new MiscVariable(VariableTypeId.CreaturesSupportedPerAdditionalPortal, 5, 0, 0);
        RoomDef treasury = basic.Rooms.Values.Single(r => r.RoomId == (byte)RoomTypeId.Treasury);
        TerrainDef treasuryTerrain = basic.TerrainById(treasury.TerrainId);
        var database = new GameDatabase(
            basic.Terrain.Values.Append(treasuryTerrain with { TerrainId = PortalTerrain, Name = "Portal", MaxHealthTypeTerrainId = PortalTerrain, DestroyedTypeTerrainId = PortalTerrain }),
            basic.Rooms.Values.Append(treasury with { Name = "Portal", RoomId = (byte)RoomTypeId.Portal, TerrainId = PortalTerrain }),
            basic.Creatures.Values, basic.Variables, basic.KeeperSpells.Values, basic.Objects.Values);
        int width = PlayerObservationTests.Width, height = PlayerObservationTests.Height;
        var tiles = new MapTile[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                tiles[y * width + x] = new MapTile(PlayerObservationTests.DirtPath, x < PlayerObservationTests.Seen ? (byte)3 : (byte)4, 0, 0);
        // Two portals, apart: 15 for the first and 5 for the second.
        tiles[0] = new MapTile(PortalTerrain, 3, 0, 0);
        tiles[2] = new MapTile(PortalTerrain, 3, 0, 0);
        var world = WorldState.FromMap(database, new LevelMap(width, height, tiles), [3, 4], fogOfWarEnabled: true);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < PlayerObservationTests.Seen; x++)
                world.Tile(x, y).SetExploredBy(3);
        // Two arrivals and one creature that did not come through a portal: no screen shows the arrivals (owner, 2026-11-17).
        foreach (bool arrived in new[] { true, true, false })
            world.SpawnCreature(database, PlayerObservationTests.Fighter, PlayerObservationTests.Viewer, PlayerObservationTests.OwnFloor).CameThroughPortal = arrived;

        PlayerRoomSummary portal = Assert.Single(Observe(database, world).Rooms, r => r.Type == RoomTypeId.Portal);
        Assert.Equal((2, 20, 0), (portal.TileCount, portal.Capacity, portal.Used));
    }

    [Fact]
    public void ARivalCreatureInViewShowsItsFlowersLevelAndPetals()
    {
        (GameDatabase? database, WorldState? world) = PlayerObservationTests.World();
        CreatureState rival = world.SpawnCreature(database, PlayerObservationTests.Fighter, PlayerObservationTests.Rival, PlayerObservationTests.OwnFloor);
        rival.Level = 6;
        rival.Health = Math.Max(1, CreatureHealthRules.MaxHealth(database, database.CreatureById(PlayerObservationTests.Fighter), rival) / 2);

        PlayerVisibleThing seen = Assert.Single(Observe(database, world).Things, t => t.Owner == PlayerObservationTests.Rival);
        Assert.Equal((6, 4), (seen.Level, seen.Petals));   // half health: two quarters lost
    }
}
