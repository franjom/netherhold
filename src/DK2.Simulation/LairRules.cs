// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Beds. One per lair tile, one per creature, and the only place a creature
/// heals — CONFIRMED (owner, 2026-09-04; docs/research/creature-needs.md).
/// <para>A lair's capacity is its <b>tile count</b>, one bed a tile, which is not the
/// general room rule (a temple's is its rim, <c>2(w+h) − 4</c>, Q60). A bed belongs to
/// a <b>creature</b>, not a keeper, and healing needs <b>sleep</b>, not presence.</para>
/// <para>A bed is an object and the object holds the ownership, as <c>Lair.CreatureId</c>
/// in DK2's object union (objects.md §7, Q130). It exists only while owned — claiming a
/// tile creates an <see cref="ObjectState"/> of the creature's own
/// <see cref="CreatureDef.LairObjectId"/> and giving it up destroys it — so a tile is
/// taken exactly when a bed stands on it, and no dead creature strands one. CONFIRMED
/// (owner, 2026-09-05; closes Q131 and ADR-0054's one assumption): a lair tile is bare
/// until a creature drops its bed on it.</para></summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-needs.md")]
public static class LairRules
{
    public const byte MistressBedObjectId = 23;

    /// <summary>Whether this creature ever goes for a bed: its record names one
    /// (<see cref="CreatureDef.LairObjectId"/>, the object its bed <i>is</i>) <b>and</b>
    /// it sleeps at all (<see cref="CreatureDef.TimeSleep"/> ≠ 0). Both halves are read
    /// from the binary (Q104, 2026-09-05).
    /// <para>The id is 22 for a Goblin, 26 for a Vampire (a coffin), 18 for a Bile
    /// Demon, and 0 for the Imp, the Skeleton and Horny — the owner CONFIRMED that a
    /// skeleton has no lair and that Horny is a summon rather than a roster member,
    /// which is also why it carries no <see cref="CreatureFlags.AvailableViaPortal"/>
    /// (ADR-0047). The two halves disagree for exactly two creatures: the <b>Dwarf</b>
    /// and the <b>Stone Knight</b> carry the hero bed (6) with <c>TimeSleep</c> 0 and
    /// <c>TimeAwake</c> 32767, so by id alone each would take a tile it never sleeps
    /// on.</para></summary>
    public static bool NeedsBed(CreatureDef creature) =>
        creature.LairObjectId != 0 && creature.TimeSleep != 0;

    /// <summary>Whether this object is a bed. Asks the type flag, not the id, and so
    /// does DK2: its union dispatch tests <c>TypeLair</c> (0x20) rather than naming any
    /// of the twelve bed objects, which is how one branch serves all twelve
    /// (objects.md §7).</summary>
    public static bool IsBed(GameDatabase database, ObjectState instance) =>
        database.ObjectById(instance.ObjectTypeId) is { IsLair: true };

    /// <summary>Whether a bed already stands on this tile.</summary>
    public static bool BedTaken(WorldState world, GameDatabase database, int tileIndex)
    {
        IReadOnlyList<ObjectState> objects = world.Objects;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i].TileIndex == tileIndex && IsBed(database, objects[i]))
                return true;
        return false;
    }

    /// <summary>This creature's own bed, or null if it has none.</summary>
    public static ObjectState? BedOf(
        WorldState world, GameDatabase database, CreatureState creature)
    {
        IReadOnlyList<ObjectState> objects = world.Objects;
        for (int i = 0; i < objects.Count; i++)
            if (objects[i].CreatureId == creature.Id && IsBed(database, objects[i]))
                return objects[i];
        return null;
    }

    /// <summary>Where this creature's bed is, as a tile index, or −1 if it has none —
    /// derived from the bed object, the one place the fact lives.</summary>
    public static int BedTileIndex(
        WorldState world, GameDatabase database, CreatureState creature) =>
        BedOf(world, database, creature)?.TileIndex ?? -1;

    /// <summary>Whether this creature owns a bed.</summary>
    public static bool HasBed(
        WorldState world, GameDatabase database, CreatureState creature) =>
        BedOf(world, database, creature) is not null;

    /// <summary>How many creatures a lair can hold: one per tile — the lair's rule,
    /// not the general room rule (see the type doc).</summary>
    public static int Capacity(RoomInstance lair) => lair.TileIndices.Count;

    /// <summary>How many of a lair's beds are already owned.</summary>
    public static int Occupied(WorldState world, GameDatabase database, RoomInstance lair)
    {
        int taken = 0;
        foreach (int index in lair.TileIndices)
            if (BedTaken(world, database, index)) taken++;
        return taken;
    }

    /// <summary>Whether this room is a lair.</summary>
    public static bool IsLairRoom(GameDatabase database, RoomInstance room) =>
        room.Definition.TerrainId == database.Room(RoomTypeId.Lair).TerrainId;

    /// <summary>Whether this tile is lair terrain held by this keeper.</summary>
    public static bool IsLairTileOf(
        WorldState world, GameDatabase database, int tileIndex, PlayerId ownerPlayerId)
    {
        if (tileIndex < 0 || tileIndex >= world.TileCount) return false;
        ref TileState tile = ref world.TileAtIndex(tileIndex);
        return tile.OwnerPlayerId == ownerPlayerId
            && tile.TerrainId == database.Room(RoomTypeId.Lair).TerrainId;
    }

    /// <summary>The one test every candidate tile is put through: this keeper's lair
    /// tile, with no bed of <i>any</i> kind on it. The original's predicate, read from
    /// its lair state (2026-09-05) — room type and owner, then whether any object on
    /// the tile carries <c>TypeLair</c>. Not "no bed of my kind" or "of my side":
    /// alignment is a preference between <i>rooms</i>
    /// (<see cref="HoldsBedOfOtherAlignment"/>).</summary>
    public static bool IsFreeLairTile(
        WorldState world, GameDatabase database, int tileIndex, PlayerId ownerPlayerId) =>
        IsLairTileOf(world, database, tileIndex, ownerPlayerId)
        && !BedTaken(world, database, tileIndex);

    /// <summary>
    /// Whether one more bed fits: the original's free-slot test is
    /// <c>used + 1 ≤ capacity</c>, and for a lair the capacity is the tile
    /// count (Q156, read from the binary).
    /// </summary>
    public static bool HasFreeSlot(WorldState world, GameDatabase database, RoomInstance lair) =>
        Occupied(world, database, lair) + 1 <= Capacity(lair);

    /// <summary>Whether this lair already holds a bed whose owner is of the other
    /// side — the test that makes a creature prefer one lair over a nearer one (Q151).
    /// Each bed's owning creature is looked up and
    /// <see cref="CreatureFlags.IsGood"/>/<see cref="CreatureFlags.IsEvil"/> read both
    /// ways. A bed whose owner has gone counts for nothing, and a creature neither
    /// good nor evil — none shipped, but a record can say so — objects to
    /// nobody.</summary>
    public static bool HoldsBedOfOtherAlignment(
        WorldState world, GameDatabase database, RoomInstance lair, CreatureDef me)
    {
        bool good = me.Flags.HasFlag(CreatureFlags.IsGood);
        bool evil = me.Flags.HasFlag(CreatureFlags.IsEvil);
        if (!good && !evil) return false;

        IReadOnlyList<ObjectState> objects = world.Objects;
        for (int i = 0; i < objects.Count; i++)
        {
            ObjectState candidate = objects[i];
            if (!IsBed(database, candidate)) continue;
            if (!lair.TileIndices.Contains(candidate.TileIndex)) continue;

            CreatureState? owner = world.CreatureById(candidate.CreatureId);
            if (owner is null) continue;

            CreatureFlags theirs = database.CreatureById(owner.CreatureTypeId).Flags;
            if (good && theirs.HasFlag(CreatureFlags.IsEvil)) return true;
            if (evil && theirs.HasFlag(CreatureFlags.IsGood)) return true;
        }

        return false;
    }

    /// <summary>The room's centre tile — the point the original measures a lair's
    /// distance from a creature by. The binary keeps a centre pair on the room; how it
    /// rounds an even side is unread, so the bounding box's midpoint rounded down is
    /// <b>ours</b>. For an L-shape the midpoint need not be a room tile, which is fine:
    /// this is a point to measure to, not a place to stand
    /// (<see cref="ArrivalTile"/>).</summary>
    public static TilePos CentreTile(RoomInstance room) =>
        new((room.MinX + room.MaxX) / 2, (room.MinY + room.MaxY) / 2);

    /// <summary>Squared distance, in position units, from where this creature stands
    /// to the centre of a room's centre tile. Squared so it stays an integer; only the
    /// ordering is used.</summary>
    public static long DistanceToCentreSquared(RoomInstance room, CreatureState creature)
    {
        TilePos centre = CentreTile(room);
        long dx = creature.PosX - CreatureState.CentreOf(centre.X);
        long dy = creature.PosY - CreatureState.CentreOf(centre.Y);
        return (dx * dx) + (dy * dy);
    }

    /// <summary>The tile a creature walks to when it heads for this room: the room
    /// tile nearest the centre, lowest index among equals. <b>Ours</b> (Q157) — the
    /// original hands the room to an unread travel-to-room routine, and its lair state
    /// only requires <i>some</i> tile of the keeper's lair underfoot. The centre agrees
    /// with the finder measuring rooms by their centres, and because the tile search is
    /// local-first this choice decides where a bed usually lands.</summary>
    public static int ArrivalTile(WorldState world, RoomInstance room)
    {
        TilePos centre = CentreTile(room);
        int centreIndex = (centre.Y * world.Width) + centre.X;
        if (room.TileIndices.Contains(centreIndex)) return centreIndex;

        int best = room.TileIndices[0];
        long bestDistance = long.MaxValue;
        foreach (int index in room.TileIndices)
        {
            long dx = (index % world.Width) - centre.X;
            long dy = (index / world.Width) - centre.Y;
            long distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }

    /// <summary>The eight neighbours a creature tries after the tile underfoot, in
    /// the original's own order — a ring from the north-west corner down the west side,
    /// along the south, up the east, ending due north. Read from the binary's offset
    /// table, 2026-09-05.</summary>
    private static readonly int[] NeighbourDx = [-1, -1, -1, 0, 1, 1, 1, 0];
    private static readonly int[] NeighbourDy = [-1, 0, 1, 1, 1, 0, -1, -1];

    /// <summary>How a free tile was found — for the event log, not for any rule.</summary>
    public enum TileFound
    {
        None,
        Underfoot,
        Neighbour,
        Walk,
    }

    /// <summary>The on-arrival tile search: the tile underfoot, then the eight
    /// neighbours in the fixed order, then a walk through the room from a random start,
    /// each candidate put through <see cref="IsFreeLairTile"/>. False and
    /// <paramref name="tileIndex"/> −1 when the room has no free tile.
    /// <para>The random start and onward walk use the room's slab chain. The neighbour
    /// ring is not confined to the room, so a lair joined at a corner qualifies.</para>
    /// </summary>
    public static bool TryFindFreeTile(
        WorldState world, GameDatabase database, RoomInstance room, TilePos from,
        PlayerId ownerPlayerId, SimRandom rng, out int tileIndex, out TileFound how)
    {
        int here = (from.Y * world.Width) + from.X;
        if (IsFreeLairTile(world, database, here, ownerPlayerId))
        {
            tileIndex = here;
            how = TileFound.Underfoot;
            return true;
        }

        for (int i = 0; i < NeighbourDx.Length; i++)
        {
            int nx = from.X + NeighbourDx[i];
            int ny = from.Y + NeighbourDy[i];
            if (!world.Contains(nx, ny)) continue;

            int index = (ny * world.Width) + nx;
            if (!IsFreeLairTile(world, database, index, ownerPlayerId)) continue;

            tileIndex = index;
            how = TileFound.Neighbour;
            return true;
        }

        int[] tiles = SlabChain.Order(world, room);
        int count = tiles.Length;
        int start = rng.NextInt(count);
        for (int step = 0; step < count; step++)
        {
            int index = tiles[(start + step) % count];
            if (!IsFreeLairTile(world, database, index, ownerPlayerId)) continue;

            tileIndex = index;
            how = TileFound.Walk;
            return true;
        }

        tileIndex = -1;
        how = TileFound.None;
        return false;
    }

    /// <summary>How long an idle, bedless creature waits between attempts to get a
    /// lair, in ticks. <b>Ours</b>: a failed attempt in the original falls through to a
    /// job and is retried at the next idle decision, and with no job system for
    /// non-workers here the idle state's own fallback — wander 12 ticks, decide again —
    /// is the only cadence to borrow. Whether the original's tick is this project's
    /// tick is not established, so it has the right shape and is not measured.</summary>
    public const int IdleRedecisionTicks = 12;

    /// <summary>DK2's lair and sleep states (<c>lair-acquisition.md</c> §9, ADR-0178): 80
    /// walks to a lair and places the bed, 81 walks to the bed, 82 lines up on it and 83
    /// sleeps. State 79 selects a bed after a hand drop or casualty delivery.</summary>
    public const int SelectingBedState = 79;
    public const int MakingAHomeState = 80;
    public const int GoingToBedState = 81;
    public const int LyingDownState = 82;
    public const int SleepingState = 83;
    public const int MistressBedExitState = 84;

    /// <summary>Puts this creature's own bed on the first free tile of any lair its
    /// keeper owns, from wherever it happens to be — <b>with no journey</b>. Not how a
    /// creature gets a bed: that is <see cref="Systems.CreatureSleepSystem"/>, which
    /// walks it to a room chosen by distance and finds the tile on arrival (ADR-0060).
    /// No system calls this; it is for level setup and for a test that wants a creature
    /// already bedded.
    /// <para>The bed placed is the creature's own kind, from
    /// <see cref="CreatureDef.LairObjectId"/>, which is why a bed is created here
    /// rather than shipped with the room: twelve bed objects serve 46 creatures, and
    /// which one a tile holds cannot be known before its sleeper is.</para>
    /// </summary>
    public static bool TryPlaceBedInAnyLair(
        WorldState world, GameDatabase database, CreatureState creature)
    {
        if (HasBed(world, database, creature)) return true;

        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if (!NeedsBed(definition)) return false;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != creature.OwnerPlayerId) continue;
            if (!IsLairRoom(database, room)) continue;

            foreach (int index in room.TileIndices)
            {
                if (BedTaken(world, database, index)) continue;
                PlaceBed(world, database, creature, index);
                return true;
            }
        }

        return false;
    }

    /// <summary>Puts this creature's own bed on a given tile and hands it over,
    /// without asking whether the tile is a lair or whether the creature wanted one.
    /// The placement alone, because three callers need it without a search:
    /// <see cref="Systems.CreatureSleepSystem"/> once its creature stands on the tile
    /// it found, a level file whose objects arrive already placed (DK2 marks them
    /// <c>AutoPlaced</c>), and a test. Unchecked on purpose: a bed placed on rock
    /// cannot be told from one a level ships, and <see cref="BedStillValid"/> drops it
    /// on the next tick anyway.</summary>
    public static ObjectState PlaceBed(
        WorldState world, GameDatabase database, CreatureState creature, int tileIndex)
    {
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        ObjectState bed = world.SpawnObject(
            database, definition.LairObjectId, creature.OwnerPlayerId, tileIndex);
        bed.CreatureId = creature.Id;
        return bed;
    }

    /// <summary>Creates a runtime bed and requests the Lair's original creation effect.</summary>
    public static ObjectState PlaceBed(SimulationContext context, CreatureState creature, TilePos at)
    {
        ObjectState bed = PlaceBed(context.World, context.Database, creature, context.World.TileIndex(at));
        context.RequestEffect("CreatureSleep", ViewEffectRequest.AtTileCentre(
            context.Database.Room(RoomTypeId.Lair).CreatedObjectEffectId, creature.OwnerPlayerId,
            at, new Fp32(0), new Dk2Angle((ushort)bed.Heading)));
        return bed;
    }

    /// <summary>Gives up this creature's bed, destroying the bed object with it, and
    /// says whether there was one. A method rather than an assignment because an
    /// unreleased bed sits on its tile for ever with a creature id nothing answers to,
    /// taking the tile permanently.</summary>
    public static bool ReleaseBed(
        WorldState world, GameDatabase database, CreatureState creature)
    {
        ObjectState? bed = BedOf(world, database, creature);
        if (bed is null) return false;
        return world.RemoveObject(bed.Id);
    }

    /// <summary>Whether this creature's bed is still on a lair tile its keeper owns. A
    /// bed is not a promise: the lair can be sold, dug out or claimed by a rival while
    /// its owner is away, and a creature holding a bed on a tile that is no longer a
    /// lair would heal on rock. Checked every time, and the bed is dropped when it
    /// fails.</summary>
    public static bool BedStillValid(
        WorldState world, GameDatabase database, CreatureState creature)
    {
        ObjectState? bed = BedOf(world, database, creature);
        if (bed is null) return false;

        int index = bed.TileIndex;
        if (index < 0 || index >= world.TileCount) return false;

        ref TileState tile = ref world.TileAtIndex(index);
        return tile.OwnerPlayerId == creature.OwnerPlayerId
            && tile.TerrainId == database.Room(RoomTypeId.Lair).TerrainId;
    }

    /// <summary>Whether anger cools this tick. DK2 runs the lair effect from the update hooks
    /// of state <b>categories</b> 6, 9 and 10 — seven states, 79 to 85 — and tests nothing
    /// about tile or bed, so <b>awake states cool too</b> (Q123, ADR-0171). Read off the
    /// dispatcher's category (ADR-0185), so a walk cools only between two states of one of
    /// those categories: 80's re-walk does, the walk in from idle and 81's walk to 82 do not.
    /// <para>Healing stays on <see cref="AsleepInOwnBed"/>: the same routine heals behind a
    /// predicate nobody has read.</para></summary>
    public static bool CoolsAngerInLair(CreatureState creature) =>
        CreatureStateMachine.UpdatingCategory(creature)
            is CreatureStateCategory.Lair
            or CreatureStateCategory.Sleeping
            or CreatureStateCategory.Sulking;

    /// <summary>Whether this creature is asleep in its own bed — the healing condition.</summary>
    public static bool AsleepInOwnBed(
        WorldState world, GameDatabase database, CreatureState creature)
    {
        if (creature.Activity != CreatureActivity.Sleeping) return false;

        int bed = BedTileIndex(world, database, creature);
        if (bed < 0) return false;

        TilePos at = creature.Tile;
        return world.Contains(at) && (at.Y * world.Width) + at.X == bed;
    }

    /// <summary>A bedded creature routes for unpaid sleep debt; wounds alone do not create debt.</summary>
    public static bool WantsToSleep(
        GameDatabase database, CreatureDef definition, CreatureState creature) =>
        NeedsBed(definition) && NeedRules.IsTired(definition, creature);

    /// <summary>Whether this creature may wake. DK2's state 83 leaves only when its rest
    /// need has run out <b>and</b> its health has reached the level-scaled maximum, so a
    /// wounded creature sleeps past its record's <c>TimeSleep</c> until it is whole
    /// (Q403, ADR-0173; `lair-acquisition.md` §9).</summary>
    /// <remarks>The health half is what makes a bed a hospital rather than a timer, and it
    /// only became visible once combat could wound anything (ADR-0163). A creature that
    /// cannot heal to full would sleep for ever; so would DK2's, and losing the bed is the
    /// way out either way (<see cref="BedStillValid"/>).</remarks>
    public static bool HasSleptEnough(
        GameDatabase database, CreatureDef definition, CreatureState creature) =>
        creature.SleepDebtTicks == 0
        && creature.Health >= CreatureHealthRules.MaxHealth(database, definition, creature);
}
