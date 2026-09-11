// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Where creatures come from: the four gates a portal passes before
/// producing one — unlocked (<c>Availability</c> kind 2 CREATURE, per level and per
/// player, so only because a level's VARIABLES are merged in, ADR-0040), in the pool
/// (<c>CreaturePool</c>, likewise), attracted (<see cref="CreatureAttraction"/>, two
/// slots at CREATURES 0x0DAC) and under <see cref="CreatureCapacity"/>. Gates and
/// numbers CONFIRMED (shipped v1.70 data; 1999 manual "Portal" p.22); how they
/// compose STRONGLY INFERRED (docs/research/portals.md, Q86).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/portals.md")]
public static class PortalRules
{
    // Availability kinds and values, decoded from the shipped table and corroborated
    // by OpenKeeper: UNAVAILABLE 1, RESEARCHABLE 2, AVAILABLE 3 (kwd-variables.md).
    private const ushort KindCreature = 2;
    private const int Researchable = 2;
    private const int Available = 3;

    /// <summary>Whether this room is one creatures walk out of. Exactly two are —
    /// Portal (3) and Hero Portal (25), the only shipped rooms with
    /// <see cref="TileConstruction.ThreeByThree"/> construction (<c>PortalTests</c>).
    /// Both count as entrances and both pay into <see cref="CreatureCapacity"/>: DK2
    /// runs one implementation for the pair and separates them by room type inside it
    /// (<see cref="CanComeFromPortal"/>).</summary>
    public static bool IsEntrance(RoomDef room) =>
        room.RoomId is (byte)RoomTypeId.Portal or (byte)RoomTypeId.HeroPortal;

    /// <summary>Whether this room is a keeper's portal, as opposed to a hero one.</summary>
    public static bool IsKeeperPortal(RoomDef room) => room.RoomId == (byte)RoomTypeId.Portal;

    /// <summary>Whether a creature on this tile is on a way out of its keeper's
    /// dungeon: portal terrain, owned by that keeper — the exit a deserter walks to
    /// (Q116, CONFIRMED by the owner). A tile test so a path search can use it as a
    /// goal predicate without a <see cref="RoomIndex"/>. "Available" reads as
    /// "reachable and yours" — one word, and where this could be wrong — so no
    /// <see cref="CanProduce"/>, and hero portals are excluded (the <c>Rebel</c>
    /// branch that would use one does not exist).</summary>
    public static bool IsExitTile(
        WorldState world, GameDatabase database, TilePos at, byte playerId)
    {
        if (!world.Contains(at)) return false;

        ref TileState tile = ref world.Tile(at.X, at.Y);
        if (tile.OwnerPlayerId != playerId) return false;

        return tile.TerrainId == database.Room(RoomTypeId.Portal).TerrainId;
    }

    /// <summary>Whether this tile is the <b>hole</b> — the centre of a portal this
    /// player owns, a much narrower thing than <see cref="IsExitTile"/>. Dropping a
    /// creature in is how a keeper gets rid of one: CONFIRMED (owner, 2026-09-07;
    /// Q88). Requires a 3x3 middle, the guard
    /// <see cref="RoomInstance.IsCentrePlatform"/> also makes; no
    /// <see cref="CanProduce"/>, as for <see cref="IsExitTile"/>.</summary>
    public static bool IsHoleTile(
        WorldState world, GameDatabase database, TilePos at, byte playerId)
    {
        if (!IsExitTile(world, database, at, playerId)) return false;

        int index = (at.Y * world.Width) + at.X;
        RoomInstance? portal = RoomIndex.RoomAt(world, database, index);

        return portal is not null
            && portal.Definition.TypeId == RoomTypeId.Portal
            && portal.Width >= 3 && portal.Height >= 3
            && EntranceTileIndex(world, portal) == index;
    }

    /// <summary>Whether this terrain id is a portal's, either kind — 12 for Portal and
    /// 40 for Hero Portal in the shipped data. Read from whichever portal ROOMS records
    /// the database has rather than hardcoded, so a renumbered terrain cannot silently
    /// stop matching and a fixture with no Hero Portal does not throw.</summary>
    public static bool IsPortalTerrain(GameDatabase database, byte terrainId)
    {
        foreach (RoomDef room in database.Rooms.Values)
        {
            if (IsEntrance(room) && room.TerrainId == terrainId) return true;
        }
        return false;
    }

    /// <summary>A portal changing hands restarts it: the new owner gets the base
    /// interval however worn out the old left it, and the clock is zeroed with the
    /// tally so a rival cannot inherit a nearly-due portal. A portal built fresh
    /// starts at zero too. CONFIRMED both ways (owner and binary, 2026-09-07; closes
    /// Q86). Applied to the centre tile, which holds both
    /// (<see cref="TileState.EntranceCreaturesGenerated"/>).</summary>
    public static void OnOwnerChanged(ref TileState tile)
    {
        tile.EntranceCreaturesGenerated = 0;
        tile.EntranceIdleTicks = 0;
    }

    /// <summary>The tile a creature appears on: the centre of the portal's bounding
    /// box, the hole in the 3x3, reserved for the arriving animation — CONFIRMED
    /// (owner, 2026-09-07; Q88). The centre is <b>not walkable</b>
    /// (<see cref="StructureBlocks"/>, <see cref="WorldPathGrid"/>) and arriving still
    /// works, because <see cref="Pathfinding.GridPathfinder"/> never tests the start
    /// tile and <c>CreatureMovementSystem.KeepOffTheCorner</c> nudges it clear.</summary>
    public static int EntranceTileIndex(WorldState world, RoomInstance portal)
    {
        int x = (portal.MinX + portal.MaxX) / 2;
        int y = (portal.MinY + portal.MaxY) / 2;
        return (y * world.Width) + x;
    }

    /// <summary>Whether this portal can produce anything: big enough to be a portal,
    /// and holding its own centre tile. A <see cref="RoomInstance"/> is tiles of one
    /// terrain with one owner, so a portal half taken from a rival is two rooms and
    /// the half without the hole produces nothing — which is also what makes the
    /// spawn clock safe on the centre tile.</summary>
    public static bool CanProduce(WorldState world, RoomInstance portal) =>
        portal.IsFunctional
        && portal.TileIndices.Contains(EntranceTileIndex(world, portal));

    /// <summary>Ticks until this portal's next creature:
    /// <c>(n² + EntranceGenerationSpeedSeconds)</c> game seconds, <c>n</c> being how
    /// many creatures <b>this portal</b> has generated — an addition of n², not a
    /// multiple, in integer seconds, so exact. The tally only ever rises and is reset
    /// only by <see cref="OnOwnerChanged"/>. CONFIRMED at <c>0x004DBBE0</c> and by two
    /// stopwatch runs (mean absolute error 1.0 s and 1.4 s; a newly claimed portal ran
    /// its own series at 2.3 s against 38.3 s for a keeper-wide tally). Base is 25 on
    /// 61 of 66 levels, overridden per level (<c>LevelVariablesOnRealDataTests</c>, Q86, docs/research/portals.md).</summary>
    public static int SpawnIntervalTicks(GameDatabase database, int creaturesGenerated)
    {
        int n = Math.Max(0, creaturesGenerated);

        // n * n in seconds before converting, as the original does: it forms
        // (n * n + base) then multiplies by the ticks in a game second. Squaring
        // the tick count instead would be wrong by a factor of variable19.
        return SimulationTiming.TicksFromSeconds(
            (n * n) + database[VariableTypeId.EntranceGenerationSpeedSeconds]);
    }

    /// <summary>How many creatures this keeper's portals support:
    /// <c>CreaturesSupportedByFirstPortal</c> (15) plus
    /// <c>CreaturesSupportedPerAdditionalPortal</c> (5) per portal after the first, 0
    /// with none, counting both portal types. CONFIRMED (binary; the two variable
    /// names; 1999 manual — docs/research/binary-entrance.md §2). Strictly less than,
    /// so at capacity nothing arrives and nothing is culled: a keeper who loses a
    /// portal keeps every creature (CONFIRMED, owner). A portal counts however broken,
    /// DK2's counter testing only <c>room[0x18] == type</c> — a dead Hero Portal raises the limit, a split portal counts twice (fidelity 4).</summary>
    public static int CreatureCapacity(GameDatabase database, int keeperPortalCount) =>
        keeperPortalCount <= 0
            ? 0
            : database[VariableTypeId.CreaturesSupportedByFirstPortal]
              + ((keeperPortalCount - 1) * database[VariableTypeId.CreaturesSupportedPerAdditionalPortal]);

    /// <summary>How many creatures this keeper holds against the cap. Workers do not
    /// count — free imps arrive on their own clock up to <c>MinimumImpThreshold</c>
    /// regardless of portals, and Create Imp has no portal precondition. STRONGLY
    /// INFERRED (docs/research/binary-entrance.md §3). By
    /// <see cref="CreatureFlags.IsWorker"/> as <see cref="ImpSupplyRules.ImpCount"/>
    /// does, so the Dwarf is out too.</summary>
    public static int CreatureCount(WorldState world, GameDatabase database, byte playerId)
    {
        int count = 0;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (creature.OwnerPlayerId != playerId) continue;
            if (!database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker))
                count++;
        }
        return count;
    }

    /// <summary>How many of one creature type this keeper holds.</summary>
    public static int CreatureCountOfType(WorldState world, byte playerId, byte creatureTypeId)
    {
        int count = 0;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (creature.OwnerPlayerId == playerId && creature.CreatureTypeId == creatureTypeId)
                count++;
        }
        return count;
    }

    /// <summary>Whether a portal may produce this creature type at all, before any
    /// question of levels, pools or rooms: reject <see cref="CreatureFlags.IsUnique"/>,
    /// then require <see cref="CreatureFlags.AvailableViaPortal"/> for a Portal or
    /// <see cref="CreatureFlags2.AvailableViaHeroPortal"/> for a Hero Portal — flags
    /// <b>exclusive by room type, not by owner</b>, so neither portal emits the
    /// other's creatures. CONFIRMED (binary, ADR-0046, binary-entrance.md §1; owner).
    /// Not redundant: nine hero creatures are AVAILABLE and pooled for keepers 3–6 on
    /// the three Frosty levels without the flag. The IsUnique half never fires on shipped data, but all thirteen uniques carry the portal flag, which is why DK2 needs it.</summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/portals.md")]
    public static bool CanComeFromPortal(CreatureDef creature, RoomDef portal)
    {
        if (creature.Flags.HasFlag(CreatureFlags.IsUnique)) return false;

        return portal.RoomId == (byte)RoomTypeId.HeroPortal
            ? creature.Flags2.HasFlag(CreatureFlags2.AvailableViaHeroPortal)
            : creature.Flags.HasFlag(CreatureFlags.AvailableViaPortal);
    }

    /// <summary>Whether this keeper has this creature unlocked — <c>Availability</c>
    /// kind 2, this player, state AVAILABLE (3) or RESEARCHABLE (2). Both count
    /// (OpenKeeper's reading), which changes nothing on shipped data: creature
    /// availability is only ever 1 or 3, asserted by <c>PortalOnRealDataTests</c>. A
    /// creature the level never mentions is unavailable — level 1's keeper has one
    /// availability line, the other 45 creatures unmentioned rather than barred.</summary>
    public static bool IsUnlocked(GameDatabase database, byte playerId, byte creatureTypeId)
    {
        foreach (AvailabilityEntry entry in database.Variables.Availabilities)
        {
            if (entry.Kind != KindCreature) continue;
            if (entry.PlayerId != playerId) continue;
            if (entry.TypeId != creatureTypeId) continue;
            if (entry.Value is Available or Researchable) return true;
        }
        return false;
    }

    /// <summary>The most of this creature type the keeper's pool allows at once, or 0
    /// if the pool does not mention it — a standing limit, not a draining quota, so
    /// level 1's <c>creature7 = 15</c> means "no more than fifteen goblins at a time"
    /// and sacking one lets another in. STRONGLY INFERRED: OpenKeeper compares against
    /// the current count, the manual has a sacked creature "replaced by a new
    /// creature", and the file stores one value per type with nowhere to record
    /// depletion. The manual's "multiplayer pools are finite" is the case against, so
    /// UNKNOWN which the campaign uses; level 1 cannot separate them, pool and first-portal cap both being 15 (Q87).
    /// Last entry wins on a duplicate type, which no shipped level has (<c>PortalOnRealDataTests</c>).</summary>
    public static int PoolLimit(GameDatabase database, byte playerId, byte creatureTypeId)
    {
        int limit = 0;
        foreach (CreaturePoolEntry entry in database.Variables.CreaturePool)
        {
            if (entry.PlayerId == playerId && entry.CreatureId == creatureTypeId)
                limit = entry.Value;
        }
        return limit;
    }

    /// <summary>Whether this keeper's dungeon holds what this creature wants. Two
    /// slots, and a creature that fills both needs both — the Bile Demon wants a
    /// Workshop of any size <i>and</i> a Hatchery of 25 tiles. The size is one room's
    /// tile count, not the keeper's total: five 5-tile hatcheries do not add up to 25.
    /// STRONGLY INFERRED (OpenKeeper; portals.md §3). Not gated on
    /// <see cref="RoomInstance.IsFunctional"/> (Q45). No attraction, never blocked.
    /// <para><b>Diverges (Q89, owner 2026-09-11):</b> a 3x5 Temple produced no Dark
    /// Angel (its record asks size 0) and a 5x5 did — the Temple weighs 2 only while
    /// it holds a particular object (binary-entrance.md §4), so ours over-attracts.</para></summary>
    public static bool IsAttracted(CreatureDef creature, IReadOnlyList<RoomInstance> keeperRooms)
    {
        foreach (CreatureAttraction attraction in creature.Attractions)
        {
            // Present is 0 exactly where RoomId is 0 on all 46 shipped creatures, so
            // either would do; both are checked because neither field is documented.
            if (attraction.Present == 0 || attraction.RoomId == 0) continue;

            bool satisfied = false;
            for (int i = 0; i < keeperRooms.Count; i++)
            {
                RoomInstance room = keeperRooms[i];
                if (room.Definition.RoomId != attraction.RoomId) continue;
                if (attraction.RoomSize != 0 && room.TileCount < attraction.RoomSize) continue;
                satisfied = true;
                break;
            }

            if (!satisfied) return false;
        }
        return true;
    }
}
