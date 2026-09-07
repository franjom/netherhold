// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Where creatures come from: the portal, and the four questions it has to
/// answer before it may produce one.
///
/// <para><b>Why this is the first room behaviour built.</b> Nothing placed a
/// non-imp creature before it. THINGS is unparsed, so no level's own starting
/// roster loads, and <see cref="Systems.ImpTaskSystem"/> skips anything without
/// <see cref="CreatureFlags.IsWorker"/> — a non-worker dropped into the world
/// stood still forever. Every other room's behaviour needs occupants, and this
/// is the room whose behaviour <i>is</i> producing them. It also needs no
/// creature health, hunger, mood or experience, none of which exists yet.</para>
///
/// <para><b>Four gates, and the data holds all four.</b> A portal produces a
/// creature only when every one of these says yes:</para>
/// <list type="number">
/// <item><b>Is it unlocked?</b> <c>Availability</c> kind 2 (CREATURE) for this
/// keeper. Per level and per player — the global table carries no creature
/// availability at all, so this only works because a level's own VARIABLES are
/// merged in (ADR-0040). Level 1 unlocks exactly one creature for its keeper:
/// the Goblin.</item>
/// <item><b>Is there one left?</b> <c>CreaturePool</c>, also per level and per
/// player. Level 1's whole pool is <c>creature7 = 15</c>.</item>
/// <item><b>Is anything attracting it?</b> <see cref="CreatureAttraction"/>,
/// two slots at CREATURES 0x0DAC. The Goblin wants a Lair of any size, so
/// <b>level 1 produces nothing at all until its keeper builds a lair</b> — which
/// is what the tutorial teaches, in that order.</item>
/// <item><b>Is there room for it?</b> <c>CreaturesSupportedByFirstPortal</c>
/// (15) plus <c>CreaturesSupportedPerAdditionalPortal</c> (5) for each portal
/// after the first.</item>
/// </list>
///
/// <para><b>Confidence.</b> The numbers and the four gates are CONFIRMED —
/// every one is a named field in the shipped v1.70 data, and the 1999 manual
/// states the cap arithmetic and the attraction rule independently
/// (docs/research/manual-1999.md, "Portal", p.22). What is <b>STRONGLY
/// INFERRED</b> is how they compose: the interval scaling below, that a
/// two-slot creature needs both its rooms, and that the pool is a standing
/// limit rather than a draining quota. Those three come from OpenKeeper's
/// implementation read as a research source, cross-checked against the shipped
/// data and the manual — see docs/research/portals.md for what each rests on.
/// Q86 lists what only playing the original can settle.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/portals.md")]
public static class PortalRules
{
    // Availability record kinds and values. Decoded from the shipped table
    // (docs/research/kwd-variables.md) and independently corroborated: these
    // are OpenKeeper's names and numbers for the same fields, arrived at from
    // the other direction.
    private const ushort KindCreature = 2;
    private const int Researchable = 2;
    private const int Available = 3;

    /// <summary>
    /// Whether this room is one creatures walk out of.
    ///
    /// <para>Two rooms are, and the pair is not arbitrary: Portal (3) and Hero
    /// Portal (25) are the only two rooms in the shipped data with
    /// <see cref="TileConstruction.ThreeByThree"/> construction, which is the
    /// 3x3-with-a-hole-in-the-middle shape a creature climbs out of. Named by id
    /// here for legibility, and <c>PortalTests</c> asserts that those two really
    /// are the only ThreeByThree rooms, so the coincidence cannot rot
    /// silently.</para>
    ///
    /// <para>Both count as entrances; only the Portal proper counts toward the
    /// cap (see <see cref="CreatureCapacity"/>), which is what the two variable
    /// names say.</para>
    /// </summary>
    public static bool IsEntrance(RoomDef room) =>
        room.RoomId is (byte)RoomTypeId.Portal or (byte)RoomTypeId.HeroPortal;

    /// <summary>Whether this room is a keeper's portal, as opposed to a hero one.</summary>
    public static bool IsKeeperPortal(RoomDef room) => room.RoomId == (byte)RoomTypeId.Portal;

    /// <summary>
    /// Whether a creature standing on this tile is standing on a way out of its
    /// keeper's dungeon: portal terrain, owned by that keeper.
    ///
    /// <para><b>The exit a deserter walks to</b> (Q116, CONFIRMED by the owner:
    /// <i>"angry evil creature goes to the nearest available portal and leaves
    /// the dungeon"</i>). Written as a tile test rather than a room test so a
    /// path search can use it as a goal predicate without building a
    /// <see cref="RoomIndex"/> for every step — and terrain plus owner is
    /// exactly what a <see cref="RoomInstance"/> is made of.</para>
    ///
    /// <para><b>"Available" is read as "reachable and yours"</b>, which is our
    /// reading of one word and is where this could be wrong. It deliberately
    /// does <i>not</i> require <see cref="CanProduce"/>: a portal that has lost
    /// its centre tile to a rival is still a hole in the floor to walk into, and
    /// nothing says a creature refuses to leave through a portal it could not
    /// have arrived through. Hero portals are excluded for the same kind of
    /// reason in reverse — an evil creature deserting a keeper has no business
    /// walking into the hero side's gate, and the converted creature that might
    /// is the <c>Rebel</c> branch, which does not exist yet.</para>
    /// </summary>
    public static bool IsExitTile(
        WorldState world, GameDatabase database, TilePos at, byte playerId)
    {
        if (!world.Contains(at)) return false;

        ref TileState tile = ref world.Tile(at.X, at.Y);
        if (tile.OwnerPlayerId != playerId) return false;

        return tile.TerrainId == database.Room(RoomTypeId.Portal).TerrainId;
    }

    /// <summary>
    /// Whether this tile is the <b>hole</b> — the centre of a portal this player
    /// owns, which is a different and much smaller thing than
    /// <see cref="IsExitTile"/>.
    ///
    /// <para><b>The hole has two jobs and this is the second</b> (Q88, CONFIRMED
    /// by the owner 2026-09-07): <i>"portal's center is reserved for creature's
    /// 'arriving' animation and for hand of evil to throw creatures in, removing
    /// them from dungeon"</i>. So <b>dropping a creature into it is how a keeper
    /// gets rid of one</b> — a keeper power this project did not have, and the
    /// counterpart to the arrival <see cref="EntranceTileIndex"/> already
    /// modelled.</para>
    ///
    /// <para><b>Requires a middle to speak of</b>, the same guard
    /// <see cref="StructureBlocks"/> and <see cref="RoomInstance.IsCentrePlatform"/>
    /// make: a portal dug down below 3x3 is a broken portal and has no hole. It
    /// deliberately does <i>not</i> require <see cref="CanProduce"/>, for the
    /// reason <see cref="IsExitTile"/> gives — a hole in the floor is a hole
    /// whether or not anything can still come out of it.</para>
    /// </summary>
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

    /// <summary>
    /// The tile a creature appears on: the centre of the portal's bounding box.
    ///
    /// <para>STRONGLY INFERRED, and the same tile OpenKeeper uses. It is the
    /// hole in the middle of the 3x3, which is where the original's animation
    /// puts them.</para>
    ///
    /// <para><b>That loose end is tied, and the owner named a second purpose for
    /// the same tile</b> (Q88, CONFIRMED 2026-09-07): <i>"portal's center is
    /// reserved for creature's 'arriving' animation and for hand of evil to
    /// throw creatures in, removing them from dungeon"</i>. So the centre is
    /// <b>not walkable</b> — <see cref="StructureBlocks"/> now says so and
    /// <see cref="WorldPathGrid"/> honours it — and this spawn is the arriving
    /// animation the owner is describing, which promotes it from
    /// STRONGLY INFERRED to the tile's actual job.</para>
    ///
    /// <para><b>Arriving on an unwalkable tile works, and by accident of good
    /// design rather than by a special case.</b> <see cref="Pathfinding.GridPathfinder"/>
    /// tests the destination and every neighbour it expands but never the
    /// <i>start</i>, so a creature standing on the hole can route off it while
    /// nothing can route onto it; and
    /// <c>CreatureMovementSystem.KeepOffTheCorner</c> already nudges a creature
    /// that finds itself somewhere unwalkable. Both were written for other
    /// reasons.</para>
    /// </summary>
    public static int EntranceTileIndex(WorldState world, RoomInstance portal)
    {
        int x = (portal.MinX + portal.MaxX) / 2;
        int y = (portal.MinY + portal.MaxY) / 2;
        return (y * world.Width) + x;
    }

    /// <summary>
    /// Whether this portal is in a state to produce anything: big enough to be a
    /// portal at all, and holding its own centre tile.
    ///
    /// <para>The centre clause is not decoration. A <see cref="RoomInstance"/> is
    /// tiles of one terrain with <i>one owner</i>, so a portal half taken from a
    /// rival is two rooms, and the half that does not contain the hole is not
    /// the thing creatures come out of. It is also what makes the spawn clock
    /// safe to keep on the centre tile: a portal that qualifies always owns the
    /// tile its clock lives on.</para>
    /// </summary>
    public static bool CanProduce(WorldState world, RoomInstance portal) =>
        portal.IsFunctional
        && portal.TileIndices.Contains(EntranceTileIndex(world, portal));

    /// <summary>
    /// How long this portal's next creature takes to arrive, in ticks:
    /// <c>(n² + EntranceGenerationSpeedSeconds)</c> game seconds, where
    /// <c>n</c> is how many creatures <b>this portal</b> has already generated.
    ///
    /// <para><b>CONFIRMED, from the binary and then twice by stopwatch.</b> DK2's
    /// portal unit computes the due time in six instructions at
    /// <c>0x004DBBE0</c> — <c>(n * n + base) * variable19 + room[0x49]</c> —
    /// where <c>room[0x4D]</c> is the per-room tally incremented once per
    /// creature generated and <c>room[0x49]</c> is the tick of the last
    /// generation. So the interval is an <b>addition of n² seconds</b> to the
    /// base, not a multiple of it.</para>
    ///
    /// <para><b>The base</b> is <c>EntranceGenerationSpeedSeconds</c>: 25 on 61
    /// of the 66 shipped levels, with level1 18, level2 20, level3 22, Level10
    /// 30 and Secret1 10000 — that last being a portal switched off rather than
    /// slowed. Levels 1 to 3 ramping to the global 25 is a tutorial gradient.
    /// Pinned by
    /// <c>LevelVariablesOnRealDataTests.FiveLevelsOverrideThePortalInterval_NotOnlyLevelOne</c>.
    /// </para>
    ///
    /// <para><b>The two measurements</b> (owner, 2026-09-07, base 25). Portal 1
    /// gave gaps of <c>25, 26, 32, 33, 42</c> seconds against a predicted
    /// <c>25, 26, 29, 34, 41</c> — mean absolute error <b>1.0 s</b>. An earlier
    /// run gave <c>25, 26, 30, 40, 41, 51, 59</c> against <c>25, 26, 29, 34, 41,
    /// 50, 61</c> — mean absolute error <b>1.4 s</b>. Recovering the base from
    /// the first run's gaps by subtracting n² gives 25, 25, 28, 24, 26: mean
    /// 25.6 against a shipped 25.</para>
    ///
    /// <para><b>And <c>n</c> is per portal, which was Q86's last open half.</b>
    /// A second portal claimed after the first had produced five creatures
    /// started its own series again: gaps of <c>27, 30, 31, 33</c> seconds where
    /// a per-room tally predicts <c>25, 26, 29, 34</c>, a keeper-wide tally
    /// predicts <c>50, 61, 74, 89</c>, and the formula this replaced predicted
    /// <c>62, 75, 87, 100</c>. Mean absolute error <b>2.3 s</b> against 38.3 s
    /// and 50.8 s. So a freshly claimed portal runs at full speed however worn
    /// out its neighbour is.</para>
    ///
    /// <para><b>What this replaced, and why it is worth remembering.</b> The old
    /// requirement was <c>max(base, base × living non-worker creatures ÷ 2)</c>,
    /// taken from OpenKeeper's <c>CreatureSpawnSystem</c> and flagged in its own
    /// doc comment as *"the step is still ours"*. It shipped, was pinned by a
    /// passing test, and sat for weeks. It was wrong three ways at once: the
    /// shape (a multiple, not an addition), the slope, and the counter — it read
    /// the keeper's <i>living</i> creatures, so it sped a portal back up on every
    /// death. DK2's tally cannot fall: 129 references to a <c>+0x4D</c> field
    /// across the executable and not one is a <c>DEC</c> or a <c>SUB</c>.</para>
    ///
    /// <para><b>Integer seconds throughout</b>, so this is exact — no rounding
    /// anywhere, unlike the halving the old formula needed.</para>
    ///
    /// <para><b>UNKNOWN, and the only part still open:</b> whether the tally is
    /// ever zeroed. Two routines store a literal zero into <c>room[0x4D]</c> and
    /// both are reached only through a vtable. See
    /// <see cref="TileState.EntranceCreaturesGenerated"/>.</para>
    /// </summary>
    public static int SpawnIntervalTicks(GameDatabase database, int creaturesGenerated)
    {
        int n = Math.Max(0, creaturesGenerated);

        // n * n in seconds before converting, exactly as the original does it:
        // it forms (n * n + base) and then multiplies the sum by the ticks in a
        // game second. Squaring the tick count instead would be wrong by a
        // factor of variable19.
        return SimulationTiming.TicksFromSeconds(
            (n * n) + database[VariableTypeId.EntranceGenerationSpeedSeconds]);
    }

    /// <summary>
    /// How many creatures this keeper's portals will support:
    /// <c>CreaturesSupportedByFirstPortal</c> (15) for the first, plus
    /// <c>CreaturesSupportedPerAdditionalPortal</c> (5) for each one after.
    ///
    /// <para><b>CONFIRMED, twice over.</b> The two variables say it in their own
    /// names, and the 1999 manual states it as a rule: the cap is per portal,
    /// not per keeper. No portals means no capacity at all, which needs no extra
    /// clause — a keeper with no portal gets no creatures for want of anywhere
    /// to put them.</para>
    ///
    /// <para>Counts keeper portals only. A hero portal is an entrance but it is
    /// not a portal for this purpose, which is what the variable names say and
    /// what OpenKeeper does.</para>
    /// </summary>
    public static int CreatureCapacity(GameDatabase database, int keeperPortalCount) =>
        keeperPortalCount <= 0
            ? 0
            : database[VariableTypeId.CreaturesSupportedByFirstPortal]
              + ((keeperPortalCount - 1) * database[VariableTypeId.CreaturesSupportedPerAdditionalPortal]);

    /// <summary>
    /// How many creatures this keeper holds against the cap.
    ///
    /// <para><b>Workers do not count.</b> An imp is not a creature the portal
    /// supports — a keeper with fifteen goblins and forty imps is at their cap,
    /// not four times over it. That is how OpenKeeper reads it too (its own
    /// counter is documented "excluding imps"), and it has to be right: free
    /// imps arrive on their own clock up to <c>MinimumImpThreshold</c>
    /// regardless of portals, and Create Imp has no portal precondition at
    /// all.</para>
    ///
    /// <para>By <see cref="CreatureFlags.IsWorker"/> rather than by imp id,
    /// which is the same rule <see cref="ImpSupplyRules.ImpCount"/> uses and
    /// also excludes the Dwarf — the heroes' worker, and equally not something a
    /// portal supports.</para>
    /// </summary>
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

    /// <summary>
    /// Whether this keeper has this creature unlocked — <c>Availability</c> kind
    /// 2, this player, state AVAILABLE or RESEARCHABLE.
    ///
    /// <para>Both states count, which is OpenKeeper's reading. It makes no
    /// difference on the shipped data: <b>no level uses RESEARCHABLE for a
    /// creature</b> — creature availability is only ever 1 or 3 — and
    /// <c>PortalOnRealDataTests</c> asserts that, so the day it stops being true
    /// a test says so rather than a portal quietly producing something.</para>
    ///
    /// <para>A creature the level never mentions is not available. That is the
    /// only reading that makes level 1 a tutorial: its keeper's whole creature
    /// availability is one line, and the other 45 creatures are unmentioned
    /// rather than explicitly barred.</para>
    /// </summary>
    /// <summary>
    /// Whether a portal can produce this creature type at all, before any
    /// question of levels, pools or rooms.
    ///
    /// <para><b>Two creature flags, and the binary tests exactly these two</b>
    /// (ADR-0046, <c>docs/research/binary-entrance.md</c>): the candidate
    /// builder in <c>Entrance.cpp</c> rejects a type unless it carries
    /// <see cref="CreatureFlags.AvailableViaPortal"/>, and rejects it outright
    /// if it carries <see cref="CreatureFlags.IsUnique"/>. The community name of
    /// bit 10 turns out to mean precisely what it says.</para>
    ///
    /// <para><b>This gate is not redundant and its absence was a real defect.</b>
    /// Nine hero creatures — Knight, Giant, Fairy, Monk, Wizard, Thief, Guard,
    /// Royal Guard and Elven Archer — are marked AVAILABLE <i>and</i> listed in
    /// the creature pool for keepers 3 to 6 on the three Frosty levels, and none
    /// of them carries the flag. Without this check a keeper's portal on those
    /// levels produces Knights and Fairies, which DK2 will not do. The other
    /// three gates cannot catch it: the level really does unlock them, and it
    /// really does pool them. They are obtainable by other means, and the
    /// entrance is simply not one of them — which is also why Vampire and
    /// Skeleton lack the flag, being the graveyard's and the prison's business
    /// instead.</para>
    ///
    /// <para><b>The <see cref="CreatureFlags.IsUnique"/> half never fires on
    /// shipped data</b> — no campaign level makes a unique creature available or
    /// pools one — so it is unfalsifiable from the files and rests on the binary
    /// alone. It is implemented anyway, because the thirteen unique creatures
    /// <i>do</i> all carry <see cref="CreatureFlags.AvailableViaPortal"/>, which
    /// is exactly why DK2 needs the second test: without it, a level that pooled
    /// a Unique Dark Angel would get one out of a portal.</para>
    ///
    /// <para>A hero portal has its own flag in a second flag word this project
    /// does not parse. It costs nothing today — <see cref="PortalSystem"/> only
    /// produces for keepers — and is noted so that whoever teaches the hero side
    /// to use its gates knows the rule is a different bit, not this one.</para>
    /// </summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/binary-entrance.md")]
    public static bool CanComeFromPortal(CreatureDef creature) =>
        creature.Flags.HasFlag(CreatureFlags.AvailableViaPortal)
        && !creature.Flags.HasFlag(CreatureFlags.IsUnique);

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

    /// <summary>
    /// The most of this creature type this keeper's pool allows at once, or 0 if
    /// the pool does not mention it.
    ///
    /// <para><b>A standing limit, not a draining quota</b> — this is the reading
    /// to argue with first if arrivals ever look wrong. Under it, level 1's
    /// <c>creature7 = 15</c> means "no more than fifteen goblins at a time", so
    /// sacking one lets another in; under the other reading it means "fifteen
    /// goblins, ever". Three things point the same way. OpenKeeper compares the
    /// pool value against the keeper's <i>current</i> count of that type. The
    /// manual says a sacked creature <i>"[is] replaced by a new creature"</i>,
    /// which a spent quota could not do. And the pool is stored as one value per
    /// type rather than as a countdown, so nothing in the file has anywhere to
    /// record depletion.</para>
    ///
    /// <para>The manual's <i>"multiplayer pools are finite"</i> is the case
    /// against, and it is about multiplayer skirmish rather than campaign
    /// levels. UNKNOWN which the shipped campaign uses; on level 1 the two are
    /// indistinguishable, because the pool and the first portal's cap are both
    /// 15. Q87.</para>
    ///
    /// <para>Last entry wins where a level lists a type twice, which is
    /// OpenKeeper's dictionary behaviour. No shipped level does — asserted in
    /// <c>PortalOnRealDataTests</c> — so the choice is recorded rather than
    /// relied on.</para>
    /// </summary>
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

    /// <summary>
    /// Whether this keeper's dungeon holds what this creature wants.
    ///
    /// <para>Two slots, and <b>a creature that fills both needs both</b>. The
    /// Bile Demon wants a Workshop of any size <i>and</i> a Hatchery of at least
    /// 25 tiles; the Royal Guard a Torture Chamber <i>and</i> a Training Room of
    /// 25. STRONGLY INFERRED — OpenKeeper reads it that way, and it is the
    /// reading that makes the second slot mean anything, since an
    /// either-or would make the demanding half of every pair
    /// redundant.</para>
    ///
    /// <para><b>The size is one room's tile count, not the keeper's total.</b>
    /// Five 5-tile hatcheries do not add up to the Bile Demon's 25. Also
    /// OpenKeeper's reading, and it matches what the requirement is for — the
    /// manual talks about the size of a room, and 25 is exactly 5x5.</para>
    ///
    /// <para><b>Not gated on <see cref="RoomInstance.IsFunctional"/></b>, and the
    /// data forces that: the Maiden Of The Nest wants a Temple of 9 tiles, a
    /// Temple's own <c>RecommendedSize</c> is 5x5 = 25, so gating on functional
    /// would make her own requirement unsatisfiable. The attraction carries its
    /// own minimum and that minimum is the requirement. It does leave a real
    /// oddity — a 3x3 temple would attract her while doing nothing as a temple
    /// (Q45) — recorded as Q89 rather than resolved.</para>
    ///
    /// <para>A creature with no attraction at all is attracted by nothing and
    /// therefore never blocked: Horny, the uniques and every Lord Of The Land
    /// have both slots empty. They are gated by availability and the pool
    /// instead, which is how a level scripts them in.</para>
    /// </summary>
    public static bool IsAttracted(CreatureDef creature, IReadOnlyList<RoomInstance> keeperRooms)
    {
        foreach (CreatureAttraction attraction in creature.Attractions)
        {
            // Present is 0 exactly where RoomId is 0 on all 46 shipped
            // creatures, so either would do; both are checked because an empty
            // slot is the common case and neither field is documented.
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
