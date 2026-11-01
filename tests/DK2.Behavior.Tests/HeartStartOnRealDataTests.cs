// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

/// <summary>Q129: every shipped heart starts at variable 174 less its room record's missing
/// percentage, and a percentage of 0 is a ruined heart room with no heart (<c>0x004DB700</c>,
/// win-and-defeat.md §3). Predicted from the binary; no capture of Level 7 or 8's first second.</summary>
public class HeartStartOnRealDataTests(ITestOutputHelper output)
{
    [InstallFact]
    public void LevelSevenGiantsDestroyTheRivalsHeartWithoutAnOpeningFreeImp()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level7");
        GameSimulation simulation = CampaignSimulationFactory.Create(db,
            GameDatabaseLoader.LoadCampaignMap(install, "Level7")!,
            GameDatabaseLoader.LoadCampaignLevel(install, "Level7")!,
            GameDatabaseLoader.LoadCampaignPlayers(install, "Level7")!,
            GameDatabaseLoader.LoadCampaignThings(install, "Level7")!,
            GameDatabaseLoader.LoadCampaignTriggers(install, "Level7")!, 7);
        var events = new ListEventSink();
        simulation.ObserveWith(events);

        simulation.Run(50);

        foreach (SimEvent entry in events.Events.Where(e =>
                     e.Kind is "imp_generated" or "heart_destroyed" or "heart_ruined"))
            output.WriteLine($"tick {entry.Tick.Value}: {entry.Kind} {entry.Detail}");
        Assert.Equal(0, simulation.World.Player(4).HeartHealth);
        Assert.NotEqual(0, simulation.World.Player(4).KilledByPlayerId);
        Assert.Equal(0, ImpSupplyRules.ImpCount(simulation.World, db, new PlayerId(4)));
        // Object_Update 0x0049BD20 requests the Heart's authored death effect before deletion.
        SimEvent explosion = Assert.Single(events.Events,
            e => e.EffectRequest?.EffectId.Value == 76 && e.EffectRequest.Value.Owner.Value == 4);
        Assert.Equal(simulation.World.Player(4).HeartDiedTick, explosion.Tick);
        Assert.Equal(new DK2.Core.TilePos(22, 13), explosion.EffectRequest!.Value.Tile);
        Assert.Equal(0, explosion.EffectRequest.Value.Position.Z.Raw); // 0x00454626: Heart centre floor.
        Assert.DoesNotContain(events.Events,
            e => e.Kind == "imp_generated" && e.Detail.StartsWith("player=4 ", StringComparison.Ordinal));
    }

    [InstallFact]
    public void LevelSevenLiveRivalsFreeSupplyArmsAfterTheHeartOpening()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level7");
        var players = GameDatabaseLoader.LoadCampaignPlayers(install, "Level7")!;
        var things = GameDatabaseLoader.LoadCampaignThings(install, "Level7")!;
        WorldState world = WorldState.FromMap(db,
            GameDatabaseLoader.LoadCampaignMap(install, "Level7")!, players.Select(p => p.PlayerId));
        ThingProjection.Place(world, db, things);
        Assert.Equal(new PlayerId[] { new(4), new(3), new(2), new(1) }, world.ActivePlayerOrder);
        Assert.Equal(1000, world.Player(4).HeartHealth);
        Assert.Equal(0, ImpSupplyRules.ImpCount(world, db, new PlayerId(4)));
        var simulation = new GameSimulation(db, world, 7,
            [new DungeonHeartSystem(), new ImpSupplySystem()]);

        simulation.Run(37);
        Assert.Equal(0, ImpSupplyRules.ImpCount(world, db, new PlayerId(4)));
        Assert.Equal(new DK2.Core.GameTick(37), world.Player(4).NextFreeImpTick);
        simulation.Tick();
        Assert.Equal(1, ImpSupplyRules.ImpCount(world, db, new PlayerId(4)));
    }

    [InstallFact]
    public void EveryShippedHeartStartsAtItsRoomRecordsPercentage_AndAZeroIsARuin_PredictedNotObserved()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        int full = db[VariableTypeId.DungeonHeartObjectHealth];
        string maps = Path.Combine(install, "Data", "editor", "maps");
        var notFull = new List<string>();

        foreach (string file in Directory.EnumerateFiles(maps, "*Things.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Things.kld".Length];
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            List<ThingRecord>? things = GameDatabaseLoader.LoadCampaignThings(install, level);
            if (map is null || things is null) continue;

            WorldState world = WorldState.FromMap(
                db, map, [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper,
                    (byte)(PlayerIds.FirstKeeper + 1), (byte)(PlayerIds.FirstKeeper + 2), (byte)(PlayerIds.FirstKeeper + 3), (byte)(PlayerIds.FirstKeeper + 4)]);
            ThingProjection.Place(world, db, things);

            foreach (RoomThing heart in things.OfType<RoomThing>()
                         .Where(r => db.RoomByTerrain(r.RoomType)?.TypeId == RoomTypeId.DungeonHeart))
            {
                RoomInstance? room = RoomIndex.RoomAt(world, db, (heart.Y * world.Width) + heart.X);
                if (room is null || !world.HasPlayer(room.OwnerPlayerId)) continue;
                PlayerState owner = world.Player(room.OwnerPlayerId);
                int pct = heart.InitialHealth;
                if (pct == 0)
                {
                    Assert.True(owner.HeartRuined, $"{level}: a 0% heart is a ruin");
                    Assert.Equal(0, owner.HeartHealth);
                }
                else
                {
                    Assert.False(owner.HeartRuined);
                    Assert.Equal(full - (int)((long)full * (100 - pct) / 100), owner.HeartHealth);
                }
                if (pct != 100) notFull.Add($"{level} player {room.OwnerPlayerId}: {pct}% -> {owner.HeartHealth}");
            }
        }

        foreach (string line in notFull) output.WriteLine(line);
        Assert.Contains(notFull, l => l.EndsWith(": 0% -> 0", StringComparison.Ordinal));
        Assert.Contains(notFull, l => l.EndsWith(": 1% -> 1000", StringComparison.Ordinal));
    }

    /// <summary>Q601: the builder's byte 3 (<c>0x004DBB65</c>) lands on chain tile 12, and on every
    /// shipped heart that is its 5x5's centre, the tile Level 4's and Level 10's caches hold 3 on.</summary>
    [InstallFact]
    public void EveryShippedHeartRecordStampsLavaOnItsCentre()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        string maps = Path.Combine(install, "Data", "editor", "maps");
        int hearts = 0;
        foreach (string file in Directory.EnumerateFiles(maps, "*Things.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Things.kld".Length];
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(install, level);
            List<ThingRecord>? things = GameDatabaseLoader.LoadCampaignThings(install, level);
            if (map is null || things is null) continue;
            WorldState world = WorldState.FromMap(
                db, map, [PlayerIds.Unowned, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper,
                    (byte)(PlayerIds.FirstKeeper + 1), (byte)(PlayerIds.FirstKeeper + 2), (byte)(PlayerIds.FirstKeeper + 3), (byte)(PlayerIds.FirstKeeper + 4)]);
            ThingProjection.Place(world, db, things);

            var expected = new List<NavStamp>();
            foreach (RoomThing heart in things.OfType<RoomThing>()
                         .Where(r => db.RoomByTerrain(r.RoomType)?.TypeId == RoomTypeId.DungeonHeart))
            {
                RoomInstance room = RoomIndex.RoomAt(world, db, (heart.Y * world.Width) + heart.X)!;
                int[] tiles = [.. room.TileIndices];
                int cx = (tiles.Min(t => t % world.Width) + tiles.Max(t => t % world.Width)) / 2;
                int cy = (tiles.Min(t => t / world.Width) + tiles.Max(t => t / world.Width)) / 2;
                Assert.True(tiles.Length == 25, $"{level}: a heart of {tiles.Length} tiles");
                expected.Add(new NavStamp((cy * world.Width) + cx, world.TileAtIndex((cy * world.Width) + cx).TerrainId, 3));
                hearts++;
            }
            Assert.True(expected.SequenceEqual(world.NavStamps), $"{level}: {string.Join(", ", world.NavStamps)}");
        }
        output.WriteLine($"{hearts} heart records");
        Assert.True(hearts > 0);
    }
}
