// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

/// <summary>Q129: every shipped heart starts at variable 174 less its room record's missing
/// percentage, and a percentage of 0 is a ruined heart room with no heart (<c>0x004DB700</c>,
/// win-and-defeat.md §3). Predicted from the binary; no capture of Level 7 or 8's first second.</summary>
public class HeartStartOnRealDataTests(ITestOutputHelper output)
{
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
}
