// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using System.Text.Json;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Tools;

/// <summary>
/// <c>dk2tools decide</c>: each of DK2's state-10 choices from a capture, put to our
/// <see cref="ImpTaskSystem.ChooseJob"/> in the world DK2 chose it in. The input is one JSON
/// line per choice (the extractor writes it from a full-state capture): the frame before the
/// choice — tiles, the keeper's task pool with its workers, every imp of that keeper with its
/// job fields — and what DK2 took. The output is one TSV row per choice, both answers side by
/// side, so a mismatch is a rule of ours that differs from DK2's with nothing else varying.
/// </summary>
/// <remarks>Rooms are not loaded: a choice in state 10 that reads one (a deposit, a room to
/// convert) is compared on its kind only. An imp holding a job is put on the tile it works
/// from, since our crowding counts that tile and nothing else about where the imp is.</remarks>
public static class DecisionCheck
{
    private const byte ImpTypeId = 1;

    private static readonly int[] Dx = [0, -1, 1, 0];
    private static readonly int[] Dy = [-1, 0, 0, 1];

    public static int Run(GameDatabase database, WorldState world, string inPath, string outPath)
    {
        var ours = new Dictionary<int, CreatureState>();
        var imps = new ImpTaskSystem();
        using var output = new StreamWriter(outPath);
        output.WriteLine("tick\timp\tlost\tat_x\tat_y\tlast_kind\tgold\tdk2_state\tdk2_kind\tdk2_x\tdk2_y"
            + "\tour_kind\tour_x\tour_y\tour_activity\tverdict");
        int rows = 0, same = 0;
        foreach (string line in File.ReadLines(inPath))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            byte player = PlayerIds.FirstKeeper;
            // Marks from the choice's own frame when the line has them: the keeper's input lands at
            // the start of a tick, before any imp chooses.
            LoadTiles(database, world, root.GetProperty("world"),
                root.TryGetProperty("marks", out JsonElement marks) ? marks : root.GetProperty("pool"), player);

            int deciderSlot = root.GetProperty("imp").GetInt32();
            CreatureState? decider = null;
            JsonElement deciderJson = default;
            var faces = new List<(CreatureState Holder, TilePos Face)>();
            foreach (JsonElement imp in root.GetProperty("imps").EnumerateArray())
            {
                int slot = imp.GetProperty("slot").GetInt32();
                if (!ours.TryGetValue(slot, out CreatureState? creature))
                {
                    creature = world.SpawnCreature(database, ImpTypeId, player, new TilePos(0, 0));
                    ours[slot] = creature;
                }
                LoadImp(database, world, creature, imp, player, holdsJob: slot != deciderSlot, faces);
                if (slot == deciderSlot)
                {
                    decider = creature;
                    deciderJson = imp;
                }
            }
            if (decider is null)
            {
                continue;
            }

            PutIn(decider, ImpStates.TaskSelection);
            decider.TargetTileIndex = -1;
            decider.TimerTicks = 0;

            var probe = new Probe(imps, decider, faces);
            var simulation = new GameSimulation(database, world, seed: 0, systems: [probe]);
            if (Environment.GetEnvironmentVariable("DK2_DECIDE_TRACE") == root.GetProperty("tick").GetInt32().ToString(CultureInfo.InvariantCulture))
            {
                simulation.ObserveWith(new TraceSink());
            }

            simulation.Tick();

            JsonElement dk2 = root.GetProperty("dk2");
            int dk2State = dk2.GetProperty("state").GetInt32();
            // A walk with no task is the store chooser's (kind 15 in our column's terms).
            int dk2Kind = dk2State == ImpStates.Resting ? 0
                : dk2.GetProperty("task_id").GetInt32() == 0 ? 15
                : dk2.GetProperty("kind").GetInt32();
            int dk2X = dk2.GetProperty("task_x").GetInt32(), dk2Y = dk2.GetProperty("task_y").GetInt32();
            int ourKind = probe.Took ? (int)decider.LastTaskKind : 0;
            if (decider.Activity == CreatureActivity.DeliveringGold)
            {
                ourKind = 15;
            }

            int ourIndex = probe.Took ? decider.TargetTileIndex : -1;
            int ourX = ourIndex < 0 ? -1 : ourIndex % world.Width, ourY = ourIndex < 0 ? -1 : ourIndex / world.Width;
            // A lost row is an imp another took its task from (task stealing, Q155), not a choice.
            bool lost = root.TryGetProperty("lost", out JsonElement l) && l.GetBoolean();
            string verdict = lost ? "lost"
                : dk2Kind != ourKind ? "kind"
                : dk2Kind is 0 or 15 || (dk2X == ourX && dk2Y == ourY) ? "same" : "tile";
            if (verdict == "same")
            {
                same++;
            }

            if (!lost)
            {
                rows++;
            }
            output.WriteLine(string.Join('\t',
                root.GetProperty("tick").GetInt32(), deciderSlot, lost ? 1 : 0,
                deciderJson.GetProperty("x").GetInt32() >> 12, deciderJson.GetProperty("y").GetInt32() >> 12,
                deciderJson.GetProperty("kind").GetInt32(), deciderJson.GetProperty("gold").GetInt32(),
                dk2State, dk2Kind, dk2X, dk2Y, ourKind, ourX, ourY, decider.Activity, verdict));
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{rows} decisions, {same} the same ({(rows == 0 ? 0 : 100.0 * same / rows):F1}%) -> {outPath}"));
        return 0;
    }

    private static void LoadTiles(
        GameDatabase database, WorldState world, JsonElement tiles, JsonElement pool, byte player)
    {
        int w = tiles.GetProperty("w").GetInt32(), h = tiles.GetProperty("h").GetInt32();
        if (w != world.Width || h != world.Height)
        {
            throw new InvalidDataException($"The capture's map is {w}x{h}, the level's {world.Width}x{world.Height}.");
        }

        int[] terrain = Ints(tiles.GetProperty("terrain"));
        int[] owner = Ints(tiles.GetProperty("owner"));
        int[] health = Ints(tiles.GetProperty("health"));
        for (int i = 0; i < w * h; i++)
        {
            ref TileState tile = ref world.Tile(i % w, i / w);
            tile.TerrainId = (byte)terrain[i];
            tile.OwnerPlayerId = (byte)owner[i];
            tile.Health = health[i];
            tile.TaggedBy = 0;
            tile.FreshlyDug = false;
            tile.TagOrder = 0;
        }
        LoadFloorGold(database, world, tiles);

        // Each kind's list is walked from its head, the newest task first.
        int order = pool.GetArrayLength();
        foreach (JsonElement entry in pool.EnumerateArray())
        {
            int kind = entry.GetProperty("kind").GetInt32();
            ref TileState tile = ref world.Tile(entry.GetProperty("x").GetInt32(), entry.GetProperty("y").GetInt32());
            order--;
            if (kind is 1 or 2)
            {
                tile.SetTaggedBy(player, true);
                tile.TagOrder = order;
            }
            else if (kind == 14)
            {
                tile.FreshlyDug = true;
            }
        }
    }

    /// <summary>The frame's floor gold, each pile in the third-of-a-tile cell its point is in;
    /// the piles of the choice before are removed first. A line without piles loads none.</summary>
    private static void LoadFloorGold(GameDatabase database, WorldState world, JsonElement tiles)
    {
        var old = new List<int>();
        foreach (ObjectState thing in world.Objects)
        {
            if (FloorGold.IsPile(database, thing))
            {
                old.Add(thing.Id);
            }
        }
        foreach (int id in old)
        {
            world.RemoveObject(id);
        }

        if (!tiles.TryGetProperty("piles", out JsonElement piles))
        {
            return;
        }
        foreach (JsonElement pile in piles.EnumerateArray())
        {
            int x = pile.GetProperty("x").GetInt32(), y = pile.GetProperty("y").GetInt32();
            var at = new TilePos(x >> 12, y >> 12);
            if (!world.Contains(at))
            {
                continue;
            }
            ObjectState? made = FloorGold.Drop(world, database, at.Y * world.Width + at.X,
                pile.GetProperty("gold").GetInt32(), (byte)pile.GetProperty("owner").GetInt32());
            if (made is not null)
            {
                made.Cell = (byte)(((y & 0xFFF) * 3 >> 12) * 3 + ((x & 0xFFF) * 3 >> 12));
            }
        }
    }

    private static void LoadImp(
        GameDatabase database, WorldState world, CreatureState creature, JsonElement imp, byte player, bool holdsJob,
        List<(CreatureState Holder, TilePos Face)> faces)
    {
        creature.StopMoving();
        creature.PosX = creature.LastPosX = imp.GetProperty("x").GetInt32();
        creature.PosY = creature.LastPosY = imp.GetProperty("y").GetInt32();
        int state = imp.GetProperty("state").GetInt32();
        PutIn(creature, state is ImpStates.Resting or ImpStates.Wandering ? state : ImpStates.TaskSelection);
        creature.GoldHeld = imp.GetProperty("gold").GetInt32();
        if (imp.TryGetProperty("level", out JsonElement level) && level.GetInt32() >= CreatureLevelRules.FirstLevel)
        {
            creature.Level = level.GetInt32();
        }
        creature.JustDropped = imp.GetProperty("just_dropped").GetInt32() != 0;
        creature.DroppedTask = (ImpTaskKind)imp.GetProperty("dropped_task").GetInt32();
        int kind = imp.GetProperty("kind").GetInt32();
        creature.LastTaskKind = (ImpTaskKind)kind;
        int tx = imp.GetProperty("task_x").GetInt32(), ty = imp.GetProperty("task_y").GetInt32();
        bool hasTile = kind != 0 && world.Contains(new TilePos(tx, ty));
        creature.LastTaskTileIndex = hasTile ? ty * world.Width + tx : -1;
        creature.TargetTileIndex = -1;
        creature.WorkSlot = MiningSlots.None;
        creature.TimerTicks = 0;

        // A task held: the pool's id is live and the imp is not choosing or resting.
        if (!holdsJob || !hasTile || imp.GetProperty("task_id").GetInt32() == 0
            || state is ImpStates.TaskSelection or ImpStates.Resting)
        {
            return;
        }

        var target = new TilePos(tx, ty);
        ref TileState tile = ref world.Tile(target);
        TileWorkKind work;
        switch (kind)
        {
            case 1 when TileWorkRules.TryTaggedWork(database, tile, player, out TileWorkKind dig):
                work = dig;
                break;
            case 1:
                work = TileWorkKind.Dig;
                break;
            case 2:
                work = TileWorkKind.MineGold;
                break;
            case 3:
                work = TileWorkRules.Applies(database, tile, TileWorkKind.AttackTile, player)
                    ? TileWorkKind.AttackTile : TileWorkKind.Claim;
                break;
            case 6:
                work = TileWorkKind.Reinforce;
                break;
            default:
                return;
        }
        // The side and slot the handler wrote (+0x94, +0x96); a claim is worked from the tile.
        int side = imp.TryGetProperty("task_side", out JsonElement sd) ? sd.GetInt32() & 3 : -1;
        TilePos face = kind == 3 ? target
            : side >= 0 ? new TilePos(tx + SideDx[side], ty + SideDy[side])
            : FaceFor(database, world, creature.Tile, target);
        creature.TargetTileIndex = ty * world.Width + tx;
        creature.TargetWork = work;
        // A holder still walking stays in the walk (6, resuming in the work's entry state): task
        // stealing measures it from where it is, an arrived one from its stance point.
        if (state == WalkState)
        {
            CreatureStateMachine.Release(creature);
            CreatureStateMachine.BeginTravel(creature, WalkState, ImpStates.For(work).Entry);
        }
        else
        {
            PutIn(creature, ImpStates.For(work).Work);
        }

        if (kind is 1 or 2 && MiningSlots.Face(face, target) is int f and >= 0)
        {
            int along = imp.TryGetProperty("task_slot", out JsonElement sl) ? sl.GetInt32() % MiningSlots.PerFace : 0;
            creature.WorkSlot = (byte)(f * MiningSlots.PerFace + along);
        }
        faces.Add((creature, face));
    }

    private const int WalkState = 6;

    /// <summary>DK2's sides, north, east, south, west (<c>0x00698A60</c>).</summary>
    private static readonly int[] SideDx = [0, 1, 0, -1];
    private static readonly int[] SideDy = [-1, 0, 1, 0];

    /// <summary>The side of a wall an imp works from: where it stands if that is a side, else the
    /// open side nearest to it.</summary>
    private static TilePos FaceFor(GameDatabase database, WorldState world, TilePos at, TilePos target)
    {
        if (Math.Abs(at.X - target.X) + Math.Abs(at.Y - target.Y) == 1)
        {
            return at;
        }

        TilePos best = at;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < Dx.Length; i++)
        {
            var side = new TilePos(target.X + Dx[i], target.Y + Dy[i]);
            if (!world.Contains(side) || database.TerrainById(world.Tile(side).TerrainId).IsSolid)
            {
                continue;
            }

            int d = Math.Abs(side.X - at.X) + Math.Abs(side.Y - at.Y);
            if (d >= bestDistance)
            {
                continue;
            }

            best = side;
            bestDistance = d;
        }
        return best;
    }

    /// <summary>Loads a state DK2 was in: from no state, which every move is allowed from, so
    /// nothing here is a transition of ours.</summary>
    private static void PutIn(CreatureState creature, int state)
    {
        CreatureStateMachine.Release(creature);
        CreatureStateMachine.TrySetState(creature, state);
    }

    private static int[] Ints(JsonElement array)
    {
        var values = new int[array.GetArrayLength()];
        int i = 0;
        foreach (JsonElement v in array.EnumerateArray())
        {
            values[i++] = v.GetInt32();
        }

        return values;
    }

    /// <summary>Prints a traced choice's events (<c>DK2_DECIDE_TRACE=&lt;tick&gt;</c>).</summary>
    private sealed class TraceSink : ISimulationEventSink
    {
        public void Emit(in SimEvent simEvent) =>
            Console.WriteLine($"  {simEvent.System} {simEvent.Kind} {simEvent.Detail}");
    }

    /// <summary>The one system the check runs: the decider's choice, and nothing else moving.</summary>
    private sealed class Probe(
        ImpTaskSystem imps, CreatureState decider, List<(CreatureState Holder, TilePos Face)> faces) : ISimulationSystem
    {
        public string Name => "DecisionProbe";

        public bool Took { get; private set; }

        public void Execute(SimulationContext context)
        {
            // A holder keeps where DK2 had it, for the stealing distance, and walks to its face,
            // which is what our crowding counts; one with no route is put there.
            foreach ((CreatureState holder, TilePos face) in faces)
            {
                if (holder.Tile == face || context.Navigation.TryRoute(holder, face, Simulation.Navigation.MoveOptions.Plain))
                {
                    continue;
                }

                holder.PosX = holder.LastPosX = CreatureState.CentreOf(face.X);
                holder.PosY = holder.LastPosY = CreatureState.CentreOf(face.Y);
            }

            Took = imps.ChooseJob(context, decider);
        }
    }
}
