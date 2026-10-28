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
    int Unmapped, int PilesReplaced, int MarksSet, IReadOnlyList<string> Notes,
    int DoorsAndTrapsChanged = 0, int CratesPlaced = 0, int CrateTripsReissued = 0,
    int CreaturesPaired = 0, int CreaturesSetIdle = 0, int TriggerValuesChanged = 0,
    int CreaturesAdded = 0, int CreaturesRemoved = 0, IReadOnlyList<(int Slot, int Ours)>? Pairs = null)
{
    public override string ToString() =>
        $"tiles changed {TilesChanged}, marks set {MarksSet}, doors and traps changed {DoorsAndTrapsChanged}, "
        + $"crates placed {CratesPlaced}, trigger flags and timers changed {TriggerValuesChanged}, "
        + $"creatures added {CreaturesAdded}, removed {CreaturesRemoved}, "
        + $"placed {CreaturesPlaced} (paired by type and point {CreaturesPaired}), jobs reissued {JobsReissued} "
        + $"(crate trips {CrateTripsReissued}), workers set choosing {WorkersSetChoosing}, others set idle {CreaturesSetIdle}, "
        + $"creatures left as they were {CreaturesLeft}, unmapped {Unmapped}, floor piles replaced {PilesReplaced}";
}

/// <summary>
/// Continues our simulation from a DK2 capture's state, the controlled-input test (ADR-0556,
/// ADR-0558, <c>resync-replay.md</c>): between two ticks of a replay the native tick's tiles,
/// marks, doors, traps, crates, manufacture list, trigger banks, creatures (made or removed to
/// match; pose, level, health, fear), Imp jobs, floor gold, mana and seed are copied over ours,
/// and the replay goes on with the same packets (ADR-0560, Q665). An Imp on tile work or a crate
/// trip is sent to the same work by our own walk, any other chooses again; another creature keeps
/// its state when DK2's is the same or both stand free, else goes to idle. Rooms, the pool's
/// holders, trigger records' state, lairs and parties stay ours; DK2's money is only reported.
/// </summary>
public static class WorldResync
{
    public const string SystemName = "resync";

    private const int WalkState = 6;
    private const int CarriedFlag = 0x4, InHandFlag = 0x10;   // object +0x146 bits 2 and 4 (objects.md 7)
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
        int doorsAndTraps = ApplyDoorsAndTraps(context, state, notes);
        simulation.Navigation.Refresh();
        int piles = ReplaceFloorGold(world, database, state);
        int crates = ReplaceCrates(world, database, state);

        int placed = 0, reissued = 0, crateTrips = 0, choosing = 0, idle = 0, left = 0, unmapped = 0;
        var unmappedNotes = new List<string>();
        var otherResumes = new SortedDictionary<int, int>();
        List<(ResyncCreature Native, CreatureState? Ours)> pairs = Match(world, state, creatureOf, out int paired);
        int removed = RemoveExtras(context, pairs, notes);
        int added = AddMissing(world, database, pairs);
        foreach ((ResyncCreature native, CreatureState? ours) in pairs)
        {
            if (ours is null)
            {
                unmapped++;
                if (unmappedNotes.Count < 12)
                    unmappedNotes.Add($"slot {native.Slot} (type {native.Type}, player {native.Owner}, state {native.State}, health {native.Health}): not paired and not made");
                continue;
            }
            if (ours.InHand)
            {
                left++;
                notes.Add($"creature {ours.Id} (slot {native.Slot}) left in the hand; DK2's is in state {native.State}");
                continue;
            }

            CopyFear(ours, native);
            RaiseLevel(context, ours, native, notes);
            if (IsDown(native))
            {
                // DK2's health counts the dying window down from 240 a tick at a time (the capture,
                // 3940-4180); ours lies at zero and counts DyingTicks up to the same window, a game
                // second at a time, so the death can land up to 3 ticks off DK2's.
                ours.PosX = native.X;
                ours.PosY = native.Y;
                ours.Health = 0;
                if (!CreatureDeathRules.IsDying(ours)) CreatureDeathRules.Collapse(context.Hooks, ours);
                ours.DyingTicks = Math.Max(0, CreatureDeathRules.DyingWindowTicks(database) - native.Health);
                placed++;
                continue;
            }

            bool worker = native.Job is not null && PlayerIds.IsKeeper(ours.OwnerPlayerId)
                && database.CreatureById(ours.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker);
            if (!worker)
            {
                bool standing = Array.IndexOf(FreeStates, native.State) >= 0 && Array.IndexOf(FreeStates, ours.Dk2StateId) >= 0;
                ours.PosX = native.X;
                ours.PosY = native.Y;
                ours.Health = native.Health;
                placed++;
                if (standing) continue;
                // A route laid from our point leads nowhere from DK2's; the state's own update lays the next.
                ours.StopMoving();
                if (native.State == ours.Dk2StateId) continue;
                if (CreatureStateMachine.HasState(ours) && StateShadow.TryMove(context, SystemName, ours, CreatureStateMachine.Idle))
                {
                    idle++;
                }
                else
                {
                    left++;
                    notes.Add($"creature {ours.Id} (slot {native.Slot}) left in our state {ours.Dk2StateId}; DK2's is {native.State}");
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
            bool crateTrip = CrateKindOf(resume) is not null;
            if (work is null && !free && !crateTrip)
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

            if (crateTrip)
            {
                if (ReissueCrateTrip(context, ours, job, resume))
                {
                    ours.LastTaskKind = KindOrNone(job.LastKind);
                    ours.LastTaskTileIndex = (job.TaskY * world.Width) + job.TaskX;
                    reissued++;
                    crateTrips++;
                    continue;
                }
                notes.Add($"imp {ours.Id} (slot {native.Slot}): no crate trip in state {resume} for ({job.TaskX},{job.TaskY}) crate {job.TaskSide}; set choosing");
            }
            else if (work is TileWorkKind kind && world.Contains(job.TaskX, job.TaskY))
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
            if (player.Manufacture is { } making)
            {
                keeper.ManufacturePoints = making.Points & 0xFFFF;
                if (making.Tail >= 0) keeper.ManufactureRingTail = (byte)(making.Tail % ManufactureRules.QueueCapacity);
                if (making.SlotZeroCost >= 0) keeper.ManufactureSlotZeroCost = making.SlotZeroCost & 0xFFFF;
                keeper.ManufactureQueue.Clear();
                foreach (ResyncOrder order in making.Orders.Take(ManufactureRules.QueueCapacity))
                    keeper.ManufactureQueue.Add(new ManufactureOrder((byte)order.Kind, (byte)order.Type, order.Cost));
            }
        }

        int triggerValues = ApplyTriggerBanks(world.Triggers, state, notes);

        if (simulation.Rng.Algorithm is Dk2Lcg lcg) lcg.Reseed(state.Seed);
        else notes.Add($"DK2 seed {state.Seed} not applied: the run does not draw from DK2's generator (--rng dk2)");

        if (otherResumes.Count > 0)
            notes.Add("walks set choosing instead, by resume state: " + string.Join(", ", otherResumes.Select(p => $"{p.Key}x{p.Value}")));
        notes.AddRange(unmappedNotes);
        return new ResyncReport(tilesChanged, placed, reissued, choosing, left, unmapped, piles, marksSet, notes,
            doorsAndTraps, crates, crateTrips, paired, idle, triggerValues, added, removed,
            pairs.Where(p => p.Ours is not null).Select(p => (p.Native.Slot, p.Ours!.Id)).ToList());
    }

    /// <summary>Each of DK2's creatures with ours: the map's while it names a live creature of the
    /// same player and type, else the nearest live one of that player and type no other has, taken
    /// greedily in capture order. The map is a packet replay's, and spawn order shifts under it with
    /// every rule change: a hero's slot can name a keeper's creature, or a corpse.</summary>
    private static List<(ResyncCreature Native, CreatureState? Ours)> Match(
        WorldState world, ResyncState state, Func<int, int?> creatureOf, out int paired)
    {
        ResyncCreature[] natives = state.Creatures.Where(c => c.Owner != 0).ToArray();   // owner 0: a pooled slot
        var ours = new CreatureState?[natives.Length];
        var taken = new HashSet<int>();
        for (int n = 0; n < natives.Length; n++)
            if (creatureOf(natives[n].Slot) is int id && world.CreatureById(id) is { } mapped
                && Same(natives[n], mapped) && taken.Add(mapped.Id))
                ours[n] = mapped;

        paired = 0;
        for (int n = 0; n < natives.Length; n++)
        {
            if (ours[n] is not null || natives[n].Health <= 0) continue;
            CreatureState? nearest = null;
            long best = long.MaxValue;
            foreach (CreatureState candidate in world.Creatures)
            {
                if (taken.Contains(candidate.Id) || !Same(natives[n], candidate)) continue;
                long dx = candidate.PosX - natives[n].X, dy = candidate.PosY - natives[n].Y;
                long distance = (dx * dx) + (dy * dy);
                if (distance < best)
                {
                    best = distance;
                    nearest = candidate;
                }
            }
            if (nearest is null) continue;
            taken.Add(nearest.Id);
            ours[n] = nearest;
            paired++;
        }
        return natives.Select((native, n) => (native, ours[n])).ToList();
    }

    /// <summary>Same player and type, and alive as DK2's is: one of DK2's lying in the dying
    /// window may be ours standing or dying, never a corpse.</summary>
    private static bool Same(ResyncCreature native, CreatureState ours) =>
        ours.OwnerPlayerId == native.Owner && ours.CreatureTypeId == native.Type
        && (IsDown(native) ? !CreatureDeathRules.IsCorpse(ours) : ours.Health > 0 && !CreatureDeathRules.IsDown(ours));

    private static bool IsDown(ResyncCreature native) =>
        native.State is CreatureDeathRules.FallingState or CreatureDeathRules.DyingState;

    /// <summary>Live creatures of ours DK2 has no counterpart for (a death DK2 had and ours did not,
    /// a party ours raised and DK2 did not) are removed by the health system's removal, which puts
    /// down what they carry and what carries them. One in the hand is left there and reported.</summary>
    private static int RemoveExtras(
        SimulationContext context, List<(ResyncCreature Native, CreatureState? Ours)> pairs, List<string> notes)
    {
        var matched = pairs.Where(p => p.Ours is not null).Select(p => p.Ours!.Id).ToHashSet();
        CreatureState[] extras = context.World.Creatures
            .Where(c => c.OwnerPlayerId != 0 && c.Health > 0 && !CreatureDeathRules.IsDown(c) && !matched.Contains(c.Id))
            .ToArray();
        int removed = 0;
        foreach (CreatureState extra in extras)
        {
            if (extra.InHand)
            {
                notes.Add($"creature {extra.Id} (player {extra.OwnerPlayerId}, type {extra.CreatureTypeId}) has no DK2 counterpart; left in the hand");
                continue;
            }
            notes.Add($"creature {extra.Id} (player {extra.OwnerPlayerId}, type {extra.CreatureTypeId}) has no DK2 counterpart; removed");
            CreatureHealthSystem.Remove(context, SystemName, extra);
            removed++;
        }
        return removed;
    }

    /// <summary>Each of DK2's live creatures left unpaired is made at its point, for the loop to copy
    /// as any other: made at level 1 and raised to DK2's there, with no lair or party (the capture
    /// reads neither). One whose point is off the map is left unpaired.</summary>
    private static int AddMissing(WorldState world, GameDatabase database, List<(ResyncCreature Native, CreatureState? Ours)> pairs)
    {
        int added = 0;
        for (int i = 0; i < pairs.Count; i++)
        {
            ResyncCreature native = pairs[i].Native;
            if (pairs[i].Ours is not null || native.Health <= 0 || !database.Creatures.ContainsKey((byte)native.Type)) continue;
            var tile = new TilePos(native.X / Fp32.One, native.Y / Fp32.One);
            if (!world.Contains(tile)) continue;
            pairs[i] = (native, world.SpawnCreature(database, (byte)native.Type, new PlayerId((byte)native.Owner), tile));
            added++;
        }
        return added;
    }

    /// <summary>Ours up to DK2's level by the level-up rule, as a scripted arrival is promoted
    /// (<see cref="PortalRules.CreateScriptedArrival"/>); a level ours is above is reported, as
    /// nothing in ours takes one away. The health copied after it is DK2's.</summary>
    private static void RaiseLevel(SimulationContext context, CreatureState ours, ResyncCreature native, List<string> notes)
    {
        if (native.Level <= 0 || ours.Level == native.Level) return;
        if (ours.Level > native.Level)
        {
            notes.Add($"creature {ours.Id} (slot {native.Slot}) is level {ours.Level}, DK2's {native.Level}; left");
            return;
        }
        CreatureDef definition = context.Database.CreatureById(ours.CreatureTypeId);
        int was = ours.Level;
        while (ours.Level < native.Level && CreatureLevelRules.LevelUp(context.Hooks, definition, ours)) { }
        notes.Add(ours.Level == native.Level
            ? $"creature {ours.Id} (slot {native.Slot}, type {ours.CreatureTypeId}) raised from level {was} to {ours.Level}"
            : $"creature {ours.Id} (slot {native.Slot}, type {ours.CreatureTypeId}) refused the level-up at {ours.Level}; DK2's is {native.Level}");
    }

    private static void CopyFear(CreatureState ours, ResyncCreature native)
    {
        if (native.Fear is not { } fear) return;
        ours.TempFear = (ushort)fear.Temp;
        ours.CurrentFear = (ushort)fear.Current;
        ours.OverThreatCounter = (byte)fear.Counter;
    }

    /// <summary>DK2's trigger banks: its flags, and its timers' start ticks, which a condition
    /// reads as a deadline against the same clock (trigger-state.md 5). Without them a party raised
    /// on a timer comes when ours started the timer, not when DK2 did. The records' own active and
    /// repeat state is not in the capture and stays ours.</summary>
    private static int ApplyTriggerBanks(TriggerState triggers, ResyncState state, List<string> notes)
    {
        if (state.Flags is not { } flags || state.Timers is not { } timers) return 0;
        if (flags.Length != TriggerState.FlagCount || timers.Length != TriggerState.TimerCount)
        {
            notes.Add($"trigger banks not copied: the capture has {flags.Length} flags and {timers.Length} timers");
            return 0;
        }
        int changed = 0;
        byte[] ourFlags = triggers.RawFlags;
        int[] ourTimers = triggers.RawTimers;
        for (int i = 0; i < flags.Length; i++)
        {
            if (ourFlags[i] == (byte)flags[i]) continue;
            notes.Add($"flag {i}: ours {ourFlags[i]}, DK2's {flags[i]}");
            ourFlags[i] = (byte)flags[i];
            changed++;
        }
        for (int i = 0; i < timers.Length; i++)
        {
            if (ourTimers[i] == timers[i]) continue;
            notes.Add($"timer {i}: ours started {ourTimers[i]}, DK2's {timers[i]}");
            ourTimers[i] = timers[i];
            changed++;
        }
        return changed;
    }

    /// <summary>Ours made DK2's: a door or trap DK2 lacks (by tile, owner and type) is removed, one
    /// ours lacks is laid without its gold or order (the list is copied whole), and each takes
    /// DK2's construction flag and health. Each keeper's blueprint list becomes DK2's blueprints in
    /// thing-slot order, the order they were laid while no slot was reused. A capture without door
    /// types copies nothing.</summary>
    private static int ApplyDoorsAndTraps(SimulationContext context, ResyncState state, List<string> notes)
    {
        if (state.Doors is not { } doors || state.Traps is not { } traps) return 0;
        if (doors.Any(d => d.Type == 0))
        {
            notes.Add("doors and traps not copied: the capture has no door types");
            return 0;
        }
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int changed = 0;
        foreach (DoorState door in world.Doors.ToArray())
            if (!doors.Any(d => d.X == door.X && d.Y == door.Y && d.Owner == door.PlayerId && d.Type == door.DoorId))
            {
                DoorRules.Remove(context, door);
                changed++;
            }
        foreach (TrapState trap in world.Traps.ToArray())
            if (!traps.Any(t => t.X == trap.X && t.Y == trap.Y && t.Owner == trap.PlayerId && t.Type == trap.TrapId))
            {
                TrapRules.Remove(context, trap);
                changed++;
            }

        var blueprints = new List<(int Slot, byte Owner, BlueprintRef Ref)>();
        foreach (ResyncDoor native in doors)
        {
            if (!world.Contains(native.X, native.Y) || !database.Doors.TryGetValue((byte)native.Type, out DoorDef? def)) continue;
            DoorState? door = world.Doors.FirstOrDefault(d => d.X == native.X && d.Y == native.Y && d.PlayerId == native.Owner);
            if (door is null)
            {
                world.AddDoor(new DoorState
                {
                    X = native.X, Y = native.Y, DoorId = (byte)native.Type, PlayerId = (byte)native.Owner, UnderConstruction = true,
                    Orientation = BlueprintPlacementRules.Orientation(world, database, new TilePos(native.X, native.Y), def),
                    Health = def.Health,
                    Unknown_0011 = new byte[DoorState.Unknown0011Length],
                });
                door = world.Doors[^1];
                changed++;
            }
            if (door.UnderConstruction != native.Constructing) changed++;
            door.UnderConstruction = native.Constructing;
            if (def.Flags.HasFlag(DoorFlags.Secret)) world.Tile(door.X, door.Y).SecretDoor = !native.Constructing;
            if (native.Health > 0) door.Health = native.Health;
            if (native.Constructing) blueprints.Add((native.Slot, door.PlayerId, new BlueprintRef(CrateRules.DoorKind, door.Id)));
        }
        foreach (ResyncTrap native in traps)
        {
            if (!world.Contains(native.X, native.Y) || !database.Traps.TryGetValue((byte)native.Type, out TrapDef? def)) continue;
            TrapState? trap = world.Traps.FirstOrDefault(t => t.X == native.X && t.Y == native.Y && t.PlayerId == native.Owner);
            if (trap is null)
            {
                trap = new TrapState { X = native.X, Y = native.Y, TrapId = (byte)native.Type, PlayerId = (byte)native.Owner, UnderConstruction = true };
                TrapRules.Seed(world, database, trap, def);
                world.AddTrap(trap);
                changed++;
            }
            if (trap.UnderConstruction && !native.Constructing)
            {
                TrapRules.Complete(world, database, trap);
                changed++;
            }
            else if (!trap.UnderConstruction && native.Constructing)
            {
                notes.Add($"trap {trap.Id} at ({trap.X},{trap.Y}) is built in ours and a blueprint in DK2; left built");
            }
            if (native.Health > 0) trap.Health = native.Health;
            if (trap.UnderConstruction) blueprints.Add((native.Slot, trap.PlayerId, new BlueprintRef(CrateRules.TrapKind, trap.Id)));
        }

        foreach (ResyncPlayer player in state.Players)
            if (PlayerIds.IsKeeper((byte)player.Id) && world.HasPlayer((byte)player.Id)) world.Player((byte)player.Id).Blueprints.Clear();
        foreach ((int _, byte owner, BlueprintRef blueprint) in blueprints.OrderBy(b => b.Slot))
            CrateRules.Register(world, owner, blueprint);
        return changed;
    }

    /// <summary>Every crate of ours goes, carried or not; DK2's on the floor are laid at their
    /// points, a listed one owned by its keeper on its Workshop. A crate DK2's Imp carries is given
    /// to our Imp with its trip (<see cref="ReissueCrateTrip"/>).</summary>
    private static int ReplaceCrates(WorldState world, GameDatabase database, ResyncState state)
    {
        foreach (ObjectState crate in world.Objects.ToArray())
        {
            if (!CrateRules.IsCrate(database, crate)) continue;
            if (crate.CarriedByCreatureId >= 0 && world.CreatureById(crate.CarriedByCreatureId) is { } carrier)
                ObjectCarryRules.PutDown(carrier, crate);
            world.RemoveObject(crate.Id);
        }
        int placed = 0;
        foreach (ResyncObject thing in state.Objects ?? [])
        {
            if ((thing.Flags & (CarriedFlag | InHandFlag)) != 0 || thing.Type is <= 0 or > byte.MaxValue) continue;
            if (database.ObjectById((byte)thing.Type) is null || CrateRules.KindOf(database, (byte)thing.Type) is null) continue;
            int x = thing.X / Fp32.One, y = thing.Y / Fp32.One;
            if (!world.Contains(x, y)) continue;
            ObjectState crate = world.SpawnObject(database, (byte)thing.Type, (byte)thing.Owner, (y * world.Width) + x);
            crate.PosX = thing.X;
            crate.PosY = thing.Y;
            placed++;
        }
        return placed;
    }

    /// <summary>A crate trip: on the way to a crate (resume 34 or 36), DK2's task tile is the
    /// blueprint and its side word the crate's object, and ours walks to the keeper's nearest
    /// crate of that object; carrying it (35 or 37), our Imp is given one and walks to the
    /// blueprint.</summary>
    private static bool ReissueCrateTrip(SimulationContext context, CreatureState imp, ResyncJob job, int resume)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (!world.Contains(job.TaskX, job.TaskY) || job.TaskSide is <= 0 or > byte.MaxValue) return false;
        byte crateObject = (byte)job.TaskSide;
        if (CrateRules.KindOf(database, crateObject) is not byte kind || kind != CrateKindOf(resume)) return false;
        int blueprint = (job.TaskY * world.Width) + job.TaskX;
        bool carrying = resume is CrateRules.InstallTrapState or CrateRules.InstallDoorState;

        ObjectState? crate = null;
        if (carrying)
        {
            crate = world.SpawnObject(database, crateObject, PlayerIds.Unowned, (imp.Tile.Y * world.Width) + imp.Tile.X);
            ObjectCarryRules.PickUp(imp, crate);
        }
        else
        {
            long best = long.MaxValue;
            foreach (ObjectState thing in world.Objects)
            {
                if (thing.ObjectTypeId != crateObject || thing.OwnerPlayerId != imp.OwnerPlayerId || thing.CarriedByCreatureId >= 0 || thing.InHand) continue;
                long distance = EatingRules.DistanceSquared(world, imp.Tile, thing.TileIndex);
                if (distance >= best) continue;
                best = distance;
                crate = thing;
            }
        }
        if (crate is null) return false;
        if (ImpTaskSystem.ReissueCrateJob(context, imp, blueprint, kind, crate, carrying)) return true;
        if (carrying)
        {
            ObjectCarryRules.PutDown(imp, crate);
            world.RemoveObject(crate.Id);
        }
        return false;
    }

    /// <summary>The crate kind a crate-trip state is for, or null for another state.</summary>
    private static byte? CrateKindOf(int state) => state switch
    {
        CrateRules.FetchTrapCrateState or CrateRules.InstallTrapState => CrateRules.TrapKind,
        CrateRules.FetchDoorCrateState or CrateRules.InstallDoorState => CrateRules.DoorKind,
        _ => null,
    };

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
