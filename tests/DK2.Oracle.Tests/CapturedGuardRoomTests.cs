// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
using Xunit.Abstractions;
using static DK2.Oracle.Tests.OriginalCpu;

namespace DK2.Oracle.Tests;

/// <summary>
/// Our C# on DK2's own game states: ticks of Level 4 seed 1's full-state capture, rebuilt as our
/// world (<see cref="CapturedState"/>) and run through DKII.EXE on the emulator beside it.
/// </summary>
public class CapturedGuardRoomTests(ITestOutputHelper log)
{
    private const string Capture = "reverse-engineering/dk2runs/level4-live1/seed01/level4.dk2cap";

    /// <summary>The reader itself: at the first stored tick the capture's terrain and owners
    /// are what our loader builds from the level file, tile for tile.</summary>
    [CapturedFact(Capture)]
    public void TheFirstTick_IsOurLevelMap()
    {
        CapturedState state = CapturedState.Load(Capture, [2]).Single();
        (_, WorldState world) = CapturedState.Level("Level4");
        var differ = new Dictionary<string, int>();
        for (int y = 0; y < state.Height; y++)
        for (int x = 0; x < state.Width; x++)
        {
            ref TileState ours = ref world.Tile(x, y);
            if (ours.TerrainId != state.Terrain(x, y)) Count(differ, $"terrain ours {ours.TerrainId}, DK2 {state.Terrain(x, y)}");
            if (ours.OwnerPlayerId != state.Owner(x, y)) Count(differ, $"owner ours {ours.OwnerPlayerId}, DK2 {state.Owner(x, y)}");
        }
        Assert.True(differ.Count == 0, "tiles differ: " + string.Join("; ", differ.Select(kv => $"{kv.Key} x{kv.Value}")));
    }

    /// <summary><c>0x00460B50</c> with ECX each creature that has a target, against
    /// <see cref="FearRules.NearOwnGuardRooms"/> on our rooms, which <see cref="RoomIndex"/>
    /// builds from the captured tiles. Compared where the original's own early-outs pass (a
    /// keeper's creature, a creature target); those belong to our callers. Variable 190 is no
    /// capture's, so both sides get our database's value: this does not check it.</summary>
    [CapturedFact(Capture)]
    public void NearOwnGuardRooms_OnCapturedTicks_MatchesTheOriginal()
    {
        IReadOnlyList<CapturedState> states = CapturedState.Load(Capture, Enumerable.Range(1, 136).Select(i => i * 50));
        (GameDatabase database, WorldState world) = CapturedState.Level("Level4");
        int g = unchecked((byte)database.Variables[VariableTypeId.GuardRoomReportingDistanceTiles]);
        // The globals it reads outside the world object, which no capture stores: the neutral
        // and hero players' tags (words, 0x00513C09 and 0x00513C1C) and variable 190 (a byte).
        static (uint, uint)[] Globals(CapturedState state, int g) =>
        [
            (0x006F2548, (uint)state.Players.Single(p => p.Id == PlayerIds.Neutral).Tag),
            (0x006F254C, (uint)state.Players.Single(p => p.Id == PlayerIds.Good).Tag),
            (0x006F2974, (uint)g),
        ];

        var mismatches = new List<string>();
        var reached = new Dictionary<string, int> { ["inside"] = 0, ["outside"] = 0 };
        int compared = 0, guardRooms = 0;
        foreach (CapturedState state in states)
        {
            CapturedCreature[] asking = [.. state.Creatures.Where(c => c.Target != 0)];
            if (asking.Length == 0) continue;
            state.ApplyTiles(world);
            var context = new SimulationContext(database, world, new SimRandom(0), NullEventSink.Instance);
            var rooms = new RoomCache();
            guardRooms += state.Rooms.Count(r => r.Active && r.Type == (int)RoomTypeId.GuardRoom);

            (uint, uint)[] globals = Globals(state, g);
            IReadOnlyList<Outcome> original = Run([.. asking.Select(c => new Request(0x00460B50, [], globals, Ecx: c.Ptr))], state.MemoryImage);
            Dictionary<int, CapturedCreature> bySlot = state.Creatures.ToDictionary(c => c.Slot);
            for (int i = 0; i < asking.Length; i++)
            {
                CapturedCreature me = asking[i];
                string at = $"tick {state.Tick} creature {me.Slot}";
                if (original[i].Fault is { } fault) { mismatches.Add($"{at}: original fault ({fault})"); continue; }
                if (!PlayerIds.IsKeeper((byte)me.Owner) || me.TargetClass != 0
                    || !bySlot.TryGetValue(me.Target, out CapturedCreature? target))
                {
                    if (original[i].Eax != 0) mismatches.Add($"{at}: original 1 past an early-out");
                    continue;
                }

                var tile = new TilePos(target.X >> 12, target.Y >> 12);
                bool ours = FearRules.NearOwnGuardRooms(context, rooms, new PlayerId((byte)me.Owner), tile);
                bool theirs = original[i].Eax != 0;
                compared++;
                reached[theirs ? "inside" : "outside"]++;
                if (ours != theirs) mismatches.Add($"{at} target {me.Target} at {tile}: original {theirs}, ours {ours}");
            }
        }
        log.WriteLine($"{states.Count} ticks, {guardRooms} guard-room samples, {compared} compared; " +
                      string.Join(", ", reached.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} differ; first:\n" + string.Join("\n", mismatches.Take(15)));
        Assert.True(reached.Values.All(n => n > 0), "an answer no captured creature reaches");
    }

    private static void Count(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
}
