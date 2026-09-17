// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>One attempt to go and get a lair, shared by everything that makes it: in
/// the original it is one function, <c>FUN_0046f390</c>, called from four places —
/// the idle state, the sleep need, and the unhappy and angry job choosers
/// (docs/research/lair-acquisition.md §5, §8).</summary>
/// <remarks>It lives here rather than in <see cref="CreatureSleepSystem"/> because
/// <see cref="CreatureAngerSystem"/> makes the same attempt before rolling an angry
/// job (ADR-0061), and a second copy of "which lair, and can I get there" would be a
/// second set of answers. The preconditions are the attempt's own, not the caller's;
/// callers keep only their own cadence — the idle countdown, the five-second angry
/// gate — which is where the original keeps it. A failed attempt is reported under
/// the calling system's name, so the log says which caller asked.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/lair-acquisition.md")]
public sealed class LairSeeker
{
    private readonly IPathfinder _pathfinder;
    private readonly List<TilePos> _route = [];
    private readonly List<TilePos> _fallbackRoute = [];

    public LairSeeker(IPathfinder pathfinder) =>
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));

    /// <summary>The gate the original's attempt opens with: conditions that have
    /// nothing to do with why anybody is asking.</summary>
    /// <remarks>Read from <c>FUN_0046f390</c> (Q104, Q150): a record that names a bed
    /// and a non-zero <c>TimeSleep</c> (<see cref="LairRules.NeedsBed"/>), not held,
    /// not imprisoned, not down, not already lair-seeking, and no bed yet. Leaving is
    /// ours, not the original's, which gets it from its state machine instead: a
    /// creature that gave up its bed to walk out must not take another one on the way
    /// past (docs/research/lair-acquisition.md).</remarks>
    public static bool CanAttempt(
        WorldState world, GameDatabase database,
        CreatureState creature, CreatureDef definition)
    {
        if (!LairRules.NeedsBed(definition)) return false;
        if (HandRules.IsHeld(creature)) return false;
        if (PrisonRules.IsImprisoned(creature)) return false;
        if (CreatureDeathRules.IsDown(creature)) return false;
        if (creature.Activity is CreatureActivity.SeekingLair
            or CreatureActivity.Leaving) return false;
        return !LairRules.HasBed(world, database, creature);
    }

    /// <summary>The whole attempt, preconditions and all: <see cref="CanAttempt"/>
    /// then <see cref="TrySetOutForALair"/>. A creature that fails the preconditions
    /// fails silently, as the original's does — it is not a lair failure because it
    /// was never a lair question.</summary>
    public bool TryGoForALair(
        SimulationContext context, string systemName,
        CreatureState creature, CreatureDef definition)
    {
        if (!CanAttempt(context.World, context.Database, creature, definition))
            return false;

        return TrySetOutForALair(context, systemName, creature, definition);
    }

    /// <summary>The attempt: choose a room and set off for it, or report why not.</summary>
    /// <remarks>The finder keeps two answers as it walks the keeper's lairs in order of
    /// distance to their centres: the nearest reachable one with a free slot, and the
    /// nearest such that also holds no bed of the other alignment (Q151). The second
    /// wins when it exists. Room type, owner, reach, a free slot, distance and the
    /// alignment of existing beds are all it tests — nothing about size or being
    /// "functional". Reach is a real route over this creature's own
    /// <see cref="WorldPathGrid"/>, so a lair behind water is reachable for a goblin
    /// and not for a vampire; equal distances fall back to the lowest tile index,
    /// <see cref="RoomIndex.Build"/>'s own order (ADR-0001). The failure reasons are
    /// the original's three interface messages — no lair, every lair full, every lair
    /// unreachable — with no grace period invented.</remarks>
    public bool TrySetOutForALair(
        SimulationContext context, string systemName,
        CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        var candidates = new List<(long Distance, int FirstTile, RoomInstance Room)>();
        int lairs = 0;
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != creature.OwnerPlayerId) continue;
            if (!LairRules.IsLairRoom(database, room)) continue;
            lairs++;

            if (!LairRules.HasFreeSlot(world, database, room)) continue;
            candidates.Add((
                LairRules.DistanceToCentreSquared(room, creature), room.TileIndices[0], room));
        }

        candidates.Sort((a, b) =>
            a.Distance != b.Distance
                ? a.Distance.CompareTo(b.Distance)
                : a.FirstTile.CompareTo(b.FirstTile));

        var grid = new WorldPathGrid(database, world, definition);
        RoomInstance? chosen = null;
        RoomInstance? fallback = null;
        bool preferred = false;
        int reachable = 0;

        foreach ((_, _, RoomInstance room) in candidates)
        {
            int arrival = LairRules.ArrivalTile(world, room);
            var at = new TilePos(arrival % world.Width, arrival / world.Width);
            if (!_pathfinder.TryFindPath(grid, creature.Tile, at, _route)) continue;
            reachable++;

            if (!LairRules.HoldsBedOfOtherAlignment(world, database, room, definition))
            {
                chosen = room;
                preferred = true;
                break;
            }

            if (fallback is null)
            {
                fallback = room;
                _fallbackRoute.Clear();
                _fallbackRoute.AddRange(_route);
            }
        }

        if (chosen is null && fallback is not null)
        {
            chosen = fallback;
            _route.Clear();
            _route.AddRange(_fallbackRoute);
        }

        if (chosen is null)
        {
            string reason = lairs == 0 ? "no_lair"
                : candidates.Count == 0 ? "lair_full"
                : "lair_unreachable";
            context.Emit(systemName, "lair_attempt_failed",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} at=({creature.Tile.X},{creature.Tile.Y}) " +
                $"reason={reason} lairs={lairs} with_slot={candidates.Count} reachable={reachable}");
            return false;
        }

        int chosenArrival = LairRules.ArrivalTile(world, chosen);
        if (!context.Navigation.TryRoute(creature, new TilePos(chosenArrival % world.Width, chosenArrival / world.Width), Navigation.MoveOptions.Spot))
            return false;
        creature.TimerTicks = 0;
        creature.Activity = CreatureActivity.SeekingLair;

        TilePos centre = LairRules.CentreTile(chosen);
        int target = LairRules.ArrivalTile(world, chosen);
        context.Emit(systemName, "lair_sought",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} at=({creature.Tile.X},{creature.Tile.Y}) " +
            $"room_centre=({centre.X},{centre.Y}) tiles={chosen.TileCount} " +
            $"free={LairRules.Capacity(chosen) - LairRules.Occupied(world, database, chosen)} " +
            $"to=({target % world.Width},{target / world.Width}) steps={_route.Count} " +
            $"{(preferred ? "preferred" : "fallback")} candidates={candidates.Count}");
        return true;
    }
}
