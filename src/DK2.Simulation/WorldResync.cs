// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>What <see cref="WorldResync.Apply"/> did and could not do.</summary>
public sealed record ResyncReport(
    int TilesChanged, int CreaturesPlaced, int JobsReissued, int WorkersSetChoosing, int CreaturesLeft,
    int Unmapped, int PilesReplaced, int MarksSet, IReadOnlyList<string> Notes)
{
    public override string ToString() =>
        $"tiles changed {TilesChanged}, marks set {MarksSet}, creatures placed {CreaturesPlaced}, jobs reissued {JobsReissued}, "
        + $"workers set choosing {WorkersSetChoosing}, creatures left as they were {CreaturesLeft}, "
        + $"unmapped {Unmapped}, floor piles replaced {PilesReplaced}";
}

/// <summary>
/// Continues our simulation from a DK2 capture's state, the controlled-input test (ADR-0556,
/// <c>resync-replay.md</c>): between two ticks of a replay the native tick's tiles, marks,
/// creature poses, Imp jobs, floor gold, mana and world seed are copied over ours, and the replay
/// goes on with the same packets, so each Imp's next choice is made from DK2's inputs (Q665).
/// An Imp walking to or working at tile work is sent to the same tile, side and slot by our own
/// walk; any other Imp chooses again. Rooms, blueprints, the manufacture queue and the pool's
/// holders stay ours (the same packets built them, and the reissued jobs re-hold their slots),
/// as does a tile the capture gives no owner, and the marks when the state lists none. DK2's
/// money is only reported (treasury gold is tile stores, ADR-0015). A creature that is not an
/// Imp keeps its state and moves only when it stands free: a sleeper moved off its bed would break its handler.
/// </summary>
public static class WorldResync
{
    public const string SystemName = "resync";

    private const int WalkState = 6;
    private static readonly int[] FreeStates = [1, 6, 10, 12, 87];

    /// <param name="creatureOf">Our creature id for a native thing-table slot, or null when the
    /// slot is not mapped at this turn (the packet replay's entity map).</param>
    public static ResyncReport Apply(GameSimulation simulation, ResyncState state, Func<int, int?> creatureOf)
    {
        if (simulation is null) throw new ArgumentNullException(nameof(simulation));
        if (state is null) throw new ArgumentNullException(nameof(state));
        if (creatureOf is null) throw new ArgumentNullException(nameof(creatureOf));
        SimulationContext context = simulation.Context;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (world.Width != state.Width || world.Height != state.Height)
            throw new InvalidDataException($"the capture's map is {state.Width}x{state.Height}, ours {world.Width}x{world.Height}");

        var notes = new List<string>();
        int tilesChanged = ApplyTiles(world, state);
        int marksSet = ApplyMarks(world, state);
        simulation.Navigation.Refresh();
        int piles = ReplaceFloorGold(world, database, state);

        int placed = 0, reissued = 0, choosing = 0, left = 0, unmapped = 0;
        var unmappedNotes = new List<string>();
        var otherResumes = new SortedDictionary<int, int>();
        foreach (ResyncCreature native in state.Creatures)
        {
            if (native.Owner == 0) continue;   // a pooled slot
            int? id = creatureOf(native.Slot);
            CreatureState? ours = id is int i ? world.CreatureById(i) : null;
            if (ours is null)
            {
                unmapped++;
                if (unmappedNotes.Count < 12)
                    unmappedNotes.Add($"slot {native.Slot} (type {native.Type}, player {native.Owner}, state {native.State}) {(id is null ? "unmapped" : $"maps to {id}, which is gone")}");
                continue;
            }

            bool worker = native.Job is not null && PlayerIds.IsKeeper(ours.OwnerPlayerId)
                && database.CreatureById(ours.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker);
            if (!worker)
            {
                if (Array.IndexOf(FreeStates, native.State) >= 0 && Array.IndexOf(FreeStates, ours.Dk2StateId) >= 0)
                {
                    ours.PosX = native.X;
                    ours.PosY = native.Y;
                    ours.Health = native.Health;
                    placed++;
                }
                else
                {
                    left++;
                }
                continue;
            }

            ResyncJob job = native.Job!;
            ours.PosX = native.X;
            ours.PosY = native.Y;
            ours.Health = native.Health;
            ours.GoldHeld = job.Gold;
            ours.LastTaskKind = KindOrNone(job.LastKind);
            ours.LastTaskTileIndex = job.LastKind != 0 && world.Contains(job.TaskX, job.TaskY) ? (job.TaskY * world.Width) + job.TaskX : -1;
            // The aim is +0x9D, what state 10 retries before anything else (00463DF0); the
            // dumper's DroppedToDoJobId is another field.
            ours.DroppedTask = KindOrNone(job.AimedKind);
            ours.JustDropped = job.JustDropped != 0;
            placed++;

            if (!CreatureStateMachine.HasState(ours))
            {
                left++;
                continue;
            }
            int resume = native.State == WalkState ? job.Resume : native.State;
            TileWorkKind? work = WorkOf(resume);
            bool free = Array.IndexOf(FreeStates, native.State) >= 0;
            if (work is null && !free)
            {
                left++;
                notes.Add($"imp {ours.Id} (slot {native.Slot}) left in our state {ours.Dk2StateId}; DK2's is {native.State}");
                continue;
            }

            // Back to its choice first: a worker mid-job holds a task of ours, and the walk
            // below files the new one as a choice would.
            ours.ClearJob();
            ours.TimerTicks = 0;
            if (!StateShadow.TryMove(context, SystemName, ours, ImpStates.TaskSelection))
            {
                left++;
                notes.Add($"imp {ours.Id} (slot {native.Slot}) refused state 10 from {ours.Dk2StateId}; DK2's is {native.State}");
                continue;
            }

            if (work is TileWorkKind kind && world.Contains(job.TaskX, job.TaskY))
            {
                int tileIndex = (job.TaskY * world.Width) + job.TaskX;
                // The marked kinds tell a wall of the keeper's own from a seam; the resume state only
                // says "dig". The tile decides, as the walk's own predicate does.
                if (kind == TileWorkKind.Dig && TileWorkRules.TryTaggedWork(database, world.TileAtIndex(tileIndex), ours.OwnerPlayerId, out TileWorkKind tagged))
                    kind = tagged;
                if (ImpTaskSystem.ReissueJob(context, ours, tileIndex, kind, job.TaskSide, (byte)job.TaskSlot))
                {
                    // The walk writes the last kind and tile as a choice would; DK2's stand.
                    ours.LastTaskKind = KindOrNone(job.LastKind);
                    ours.LastTaskTileIndex = (job.TaskY * world.Width) + job.TaskX;
                    reissued++;
                    continue;
                }
                notes.Add($"imp {ours.Id} (slot {native.Slot}): no walk to {kind} at ({job.TaskX},{job.TaskY}) side {job.TaskSide}; set choosing");
            }
            else if (native.State == WalkState)
            {
                otherResumes[job.Resume] = otherResumes.GetValueOrDefault(job.Resume) + 1;
            }
            choosing++;
        }

        foreach (ResyncPlayer player in state.Players)
        {
            if (!PlayerIds.IsKeeper((byte)player.Id) || !world.HasPlayer((byte)player.Id)) continue;
            ref PlayerState keeper = ref world.Player((byte)player.Id);
            notes.Add($"player {player.Id}: DK2 money {player.Money}, mana {player.Mana} (ours {keeper.Mana}); mana copied, money reported only");
            keeper.Mana = player.Mana;
        }

        if (simulation.Rng.Algorithm is Dk2Lcg lcg) lcg.Reseed(state.Seed);
        else notes.Add($"DK2 seed {state.Seed} not applied: the run does not draw from DK2's generator (--rng dk2)");

        if (otherResumes.Count > 0)
            notes.Add("walks set choosing instead, by resume state: " + string.Join(", ", otherResumes.Select(p => $"{p.Key}x{p.Value}")));
        notes.AddRange(unmappedNotes);
        return new ResyncReport(tilesChanged, placed, reissued, choosing, left, unmapped, piles, marksSet, notes);
    }

    /// <summary>Each marking keeper's marks become the list's, in the list's order (the marking
    /// ticks), each filed after everything marked before the resync.</summary>
    private static int ApplyMarks(WorldState world, ResyncState state)
    {
        if (state.Marks is not { Length: > 0 } marks) return 0;
        var keepers = new HashSet<byte>(marks.Select(m => (byte)m.Player));
        for (int i = 0; i < world.Width * world.Height; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            foreach (byte keeper in keepers)
                if (tile.IsTaggedBy(keeper)) tile.TaggedBy = tile.TaggedBy.Without(keeper);
        }
        int set = 0;
        foreach (ResyncMark mark in marks.OrderBy(m => m.Order))
        {
            if (!world.Contains(mark.X, mark.Y)) continue;
            ref TileState tile = ref world.Tile(mark.X, mark.Y);
            tile.TaggedBy = tile.TaggedBy.With((byte)mark.Player);
            tile.TagOrder = world.NextTagOrder++;
            set++;
        }
        return set;
    }

    private static ImpTaskKind KindOrNone(int kind) =>
        kind is >= 0 and <= byte.MaxValue && Enum.IsDefined(typeof(ImpTaskKind), (byte)kind) ? (ImpTaskKind)kind : ImpTaskKind.None;

    /// <summary>The tile work a worker state names, or null for a state that is not one.</summary>
    public static TileWorkKind? WorkOf(int state) => state switch
    {
        13 or 14 => TileWorkKind.Dig,
        15 or 16 => TileWorkKind.Claim,
        17 or 18 => TileWorkKind.ConvertRoom,
        19 or 20 => TileWorkKind.AttackTile,
        21 or 22 => TileWorkKind.Reinforce,
        23 or 24 => TileWorkKind.RepairWall,
        25 or 26 => TileWorkKind.MineGold,
        _ => null,
    };

    private static int ApplyTiles(WorldState world, ResyncState state)
    {
        int changed = 0;
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < world.Width; x++)
        {
            int index = (y * world.Width) + x;
            ref TileState tile = ref world.Tile(x, y);
            byte terrain = (byte)state.Terrain(index);
            int owner = state.Owner(index);
            if (tile.TerrainId != terrain || (owner != 0 && tile.OwnerPlayerId != owner)) changed++;
            tile.TerrainId = terrain;
            if (owner != 0) tile.OwnerPlayerId = (byte)owner;
            if (state.TileHealth is { } health && index < health.Length) tile.Health = health[index];
        }
        return changed;
    }

    /// <summary>Ours off the floor, DK2's on: a pile is where a fetch and a mining slot look.</summary>
    private static int ReplaceFloorGold(WorldState world, GameDatabase database, ResyncState state)
    {
        foreach (ObjectState pile in world.Objects.ToArray())
            if (FloorGold.IsPile(database, pile) && !pile.InHand && pile.CarriedByCreatureId < 0)
                world.RemoveObject(pile.Id);
        int placed = 0;
        foreach (ResyncObject thing in state.Objects ?? [])
        {
            if (thing.Type != FloorGold.FloorGoldObjectId || thing.Gold <= 0) continue;
            int x = thing.X / Fp32.One, y = thing.Y / Fp32.One;
            if (!world.Contains(x, y)) continue;
            ObjectState? pile = FloorGold.Drop(world, database, (y * world.Width) + x, thing.Gold);
            if (pile is null) continue;
            pile.PosX = thing.X;
            pile.PosY = thing.Y;
            placed++;
        }
        return placed;
    }
}
