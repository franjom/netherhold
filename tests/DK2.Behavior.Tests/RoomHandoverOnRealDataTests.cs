// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>World action 49 hands a room over (arm <c>0x005197F7</c>, a direct call to
/// <c>Room_TransferOwnership</c> at <c>0x00519860</c>, so no health reset). Level 17's script holds
/// three records, each under its own flag (11, 12, 13), naming the one-based tile (40, 40) and
/// players 3, 4 and 5 (ADR-0760).</summary>
public class RoomHandoverOnRealDataTests
{
    [InstallFact]
    public void LevelSeventeensScriptHandsItsRoomToEachKeeperInTurn_KeepingItsHealth()
    {
        GameSimulation simulation = Level("level17");
        WorldState world = simulation.World;
        int index = (39 * world.Width) + 39;
        RoomInstance room = RoomIndex.RoomAt(world, simulation.Database, index)!;
        Assert.NotNull(room);

        foreach ((byte flag, byte keeper) in new[] { ((byte)11, (byte)3), ((byte)12, (byte)4), ((byte)13, (byte)5) })
        {
            RoomInstance before = RoomIndex.RoomAt(world, simulation.Database, index)!;
            int damaged = Math.Max(1, RoomHealthRules.Health(world, simulation.Database, before) / 2);
            RoomHealthRules.Set(world, simulation.Database, before, damaged);

            world.Triggers.Write(flag, FlagWriteMode.Assign, 1);
            simulation.Run(2);

            RoomInstance after = RoomIndex.RoomAt(world, simulation.Database, index)!;
            Assert.Equal(keeper, after.OwnerPlayerId);
            Assert.All(after.TileIndices, i => Assert.Equal(keeper, world.TileAtIndex(i).OwnerPlayerId));
            Assert.Equal(damaged, RoomHealthRules.Health(world, simulation.Database, after));
        }
    }

    private static GameSimulation Level(string name)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, name);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, name)!;
        var players = GameDatabaseLoader.LoadCampaignPlayers(install, name)!;
        var things = GameDatabaseLoader.LoadCampaignThings(install, name)!;
        var level = GameDatabaseLoader.LoadCampaignLevel(install, name)!;
        var triggers = GameDatabaseLoader.LoadCampaignTriggers(install, name)!;
        return CampaignSimulationFactory.Create(db, map, level, players, things, triggers, 1);
    }
}
