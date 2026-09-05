// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Beds. One per lair tile, one per creature, and the only place a creature
/// heals.
///
/// <para><b>CONFIRMED by the owner</b> (2026-09-04), which is the whole rule:
/// <i>"a lair is a room with tiles, on each tile only one bed/chair/whatever per
/// creature can be present. When a creature enters a dungeon through a portal it
/// goes to a lair, finds a free tile and occupies it with its bed. When a
/// creature is injured it heals while it sleeps in its own bed."</i></para>
///
/// <para>Three things follow, and the third is the one that matters:</para>
/// <list type="number">
/// <item><b>A lair's capacity is its tile count.</b> Not its rim, not a
/// variable — one bed per tile. Worth stating because it is <i>not</i> the
/// general rule for rooms: a temple's capacity is its rim, <c>2(w+h) − 4</c>
/// (Q60), and a dungeon heart stores its gold in an outer ring. Capacity is
/// per room type and the lair's happens to be the obvious one.</item>
/// <item><b>A bed belongs to a creature, not to a keeper.</b> Which is what
/// replaced the first implementation's proxy — it healed anything standing on
/// any of its keeper's lair tiles, and that is two mistakes.</item>
/// <item><b>Healing needs sleep, not presence.</b> A wounded creature standing
/// awake in its own lair heals nothing.</item>
/// </list>
///
/// <para><b>A bed is an object, and the object holds the ownership</b> — since
/// 2026-09-04, and it used to be the other way round. The relation lived on the
/// creature, as a tile index; DK2 keeps it on the object, as
/// <c>Lair.CreatureId</c> in the object's union. The binary settled it (see
/// <c>docs/research/objects.md</c> §7 and Q130), and there is now exactly one
/// copy of the fact, because two copies of one fact is the shape that has
/// already cost this project three long-lived defects.</para>
///
/// <para><b>The bed exists only while it is owned.</b> Claiming a tile creates
/// an <see cref="ObjectState"/> of the creature's own
/// <see cref="CreatureDef.LairObjectId"/> — a Goblin's bed, a Vampire's coffin —
/// and giving the bed up destroys it. That is why occupancy needs no separate
/// bookkeeping: <b>a lair tile is taken exactly when a bed object stands on
/// it</b>, and a creature that dies, leaves or is removed cannot strand a bed
/// that nothing owns, because releasing the bed is what removes the object.</para>
///
/// <para><b>CONFIRMED by the owner</b> (2026-09-05), which closes Q131 and the
/// one assumption ADR-0054 rested on: <i>"yes a lair is bare, green lair texture
/// on the floor tile, only when a creature enters a lair and claims a tile by
/// dropping its bed does that tile get furniture on top of it."</i> So a lair
/// tile really is bare until it is claimed, the bed really is created at that
/// moment, and it really is <b>that creature's</b> bed being put down.</para>
///
/// <para>The cost is a scan of the object list per query, and beds are tens
/// where tiles are tens of thousands.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-needs.md")]
public static class LairRules
{
    /// <summary>
    /// Whether this creature ever goes for a bed: its record names one
    /// (<see cref="CreatureDef.LairObjectId"/> — the object its bed <i>is</i>)
    /// <b>and</b> it sleeps at all (<see cref="CreatureDef.TimeSleep"/> ≠ 0).
    ///
    /// <para>22 for a Goblin, 26 for a Vampire (a coffin), 18 for a Bile Demon.
    /// <b>0 for the Imp, the Skeleton and Horny</b>, and the owner CONFIRMED
    /// both of the interesting ones: a skeleton does not have a lair, and Horny
    /// is a summon rather than a member of a roster — it only appears on
    /// particular maps when conditions are met, which is also why it carries no
    /// <see cref="CreatureFlags.AvailableViaPortal"/> (ADR-0047).</para>
    ///
    /// <para><b>Both halves, read from the binary (Q104, 2026-09-05).</b> The
    /// original's lair attempt is gated on <c>TimeSleep</c> being non-zero as
    /// well as on the bed id, and the two disagree for exactly two creatures:
    /// the <b>Dwarf</b> and the <b>Stone Knight</b> carry the hero bed (6) with
    /// <c>TimeSleep</c> 0 and <c>TimeAwake</c> 32767. By the id alone they would
    /// each take a lair tile they never sleep on; the original never sends them
    /// for one, and neither does this. The gate excludes Imp, Skeleton, Unique
    /// Skeleton, Horny, Dwarf and Stone Knight by <c>TimeSleep</c>, and the
    /// seven heroes with no bed id by the id.</para>
    /// </summary>
    public static bool NeedsBed(CreatureDef creature) =>
        creature.LairObjectId != 0 && creature.TimeSleep != 0;

    /// <summary>
    /// Whether this object is a bed.
    ///
    /// <para><b>Asks the type flag, not the id</b>, and so does DK2: the
    /// original's own union dispatch tests <c>TypeLair</c> (0x20) rather than
    /// naming any of the twelve bed objects, which is how one branch serves all
    /// twelve. Flags beat ids in its dispatch order (objects.md §7).</para>
    /// </summary>
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

    /// <summary>
    /// Where this creature's bed is, as a tile index, or −1 if it has none.
    ///
    /// <para>The replacement for the field <c>CreatureState.LairTileIndex</c>
    /// used to be. Same answer, derived from the one place the fact now
    /// lives.</para>
    /// </summary>
    public static int BedTileIndex(
        WorldState world, GameDatabase database, CreatureState creature) =>
        BedOf(world, database, creature)?.TileIndex ?? -1;

    /// <summary>Whether this creature owns a bed.</summary>
    public static bool HasBed(
        WorldState world, GameDatabase database, CreatureState creature) =>
        BedOf(world, database, creature) is not null;

    /// <summary>
    /// How many creatures a lair can hold: one per tile.
    ///
    /// <para>See the type doc — this is the lair's rule, not rooms' rule.</para>
    /// </summary>
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
        WorldState world, GameDatabase database, int tileIndex, byte ownerPlayerId)
    {
        if (tileIndex < 0 || tileIndex >= world.TileCount) return false;
        ref TileState tile = ref world.TileAtIndex(tileIndex);
        return tile.OwnerPlayerId == ownerPlayerId
            && tile.TerrainId == database.Room(RoomTypeId.Lair).TerrainId;
    }

    /// <summary>
    /// The one test every candidate tile is put through: this keeper's lair
    /// tile, with no bed of <i>any</i> kind standing on it.
    ///
    /// <para>The original's predicate, read from its lair state (2026-09-05):
    /// it asks the tile's room type and owner, then whether any object on the
    /// tile carries the <c>TypeLair</c> flag. Not "no bed of my kind", not "no
    /// bed of my side" — any bed. Alignment is a preference between
    /// <i>rooms</i>, not a rule about tiles (<see cref="HoldsBedOfOtherAlignment"/>).</para>
    /// </summary>
    public static bool IsFreeLairTile(
        WorldState world, GameDatabase database, int tileIndex, byte ownerPlayerId) =>
        IsLairTileOf(world, database, tileIndex, ownerPlayerId)
        && !BedTaken(world, database, tileIndex);

    /// <summary>
    /// Whether one more bed fits: the original's free-slot test is
    /// <c>used + 1 ≤ capacity</c>, and for a lair the capacity is the tile
    /// count (Q156, read from the binary).
    /// </summary>
    public static bool HasFreeSlot(WorldState world, GameDatabase database, RoomInstance lair) =>
        Occupied(world, database, lair) + 1 <= Capacity(lair);

    /// <summary>
    /// Whether this lair already holds a bed whose owner is of the other side —
    /// the test that makes a creature prefer one lair over a nearer one (Q151).
    ///
    /// <para>For each bed in the room the owning creature is looked up and the
    /// pair <see cref="CreatureFlags.IsGood"/> / <see cref="CreatureFlags.IsEvil"/>
    /// is read both ways: <i>if I am good, is it evil? if I am evil, is it
    /// good?</i> A bed whose owner has gone (the creature id answers to nobody)
    /// counts for nothing. A creature that is neither good nor evil by its
    /// flags — none shipped, but a record can say so — objects to nobody.</para>
    /// </summary>
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

    /// <summary>
    /// The room's centre tile — the point the original measures a lair's
    /// distance from a creature by.
    ///
    /// <para>The binary keeps a centre tile pair on the room and measures to
    /// its centre point; how it rounds an even side is unread, so the midpoint
    /// of the bounding box, rounded down, is <b>ours</b>. For an L-shaped room
    /// the box's midpoint need not be a room tile; that is fine here, because
    /// this is a point to measure to, not a place to stand — see
    /// <see cref="ArrivalTile"/> for that.</para>
    /// </summary>
    public static TilePos CentreTile(RoomInstance room) =>
        new((room.MinX + room.MaxX) / 2, (room.MinY + room.MaxY) / 2);

    /// <summary>
    /// Squared distance, in position units, from where this creature stands to
    /// the centre of a room's centre tile. Squared so that it stays an integer;
    /// only the ordering is used.
    /// </summary>
    public static long DistanceToCentreSquared(RoomInstance room, CreatureState creature)
    {
        TilePos centre = CentreTile(room);
        long dx = creature.PosX - CreatureState.CentreOf(centre.X);
        long dy = creature.PosY - CreatureState.CentreOf(centre.Y);
        return (dx * dx) + (dy * dy);
    }

    /// <summary>
    /// The tile a creature walks to when it heads for this room: the room tile
    /// nearest the room's centre, lowest tile index among equals.
    ///
    /// <para><b>Ours — Q157.</b> The original hands the room to a travel-to-room
    /// routine shared by every room-going state, and that routine is unread;
    /// its lair state only requires <i>some</i> tile of the keeper's lair
    /// underfoot when it begins. The centre is the obvious guess and agrees
    /// with the finder measuring rooms by their centres. Where a creature's bed
    /// lands relative to the door follows from this choice most of the time,
    /// because the tile search is local-first.</para>
    /// </summary>
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

    /// <summary>
    /// The eight neighbours a creature tries after the tile it is standing on,
    /// in the original's own order: (−1,−1), (−1,0), (−1,+1), (0,+1), (+1,+1),
    /// (+1,0), (+1,−1), (0,−1) — a ring from the north-west corner down the
    /// west side, along the south, up the east, ending due north. Read from
    /// the binary's offset table, 2026-09-05.
    /// </summary>
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

    /// <summary>
    /// The on-arrival tile search: the tile underfoot, then the eight
    /// neighbours in the fixed order, then a walk through the room from a
    /// random start, each candidate put through <see cref="IsFreeLairTile"/>.
    /// Returns false, and <paramref name="tileIndex"/> −1, when the room has no
    /// free tile at all.
    ///
    /// <para><b>The walk is the original's in shape and ours in order.</b> The
    /// original draws <c>rand(tileCount)</c>, steps that far along the room's
    /// own tile chain from its bounding-box origin, and follows the chain until
    /// a candidate passes or it is back where it began. Its chain is the order
    /// tiles joined the room; <see cref="RoomInstance.TileIndices"/> is
    /// ascending tile order, so the same draw lands on a different tile. The
    /// random start is the behaviour — two creatures that fail the local search
    /// spread out rather than queueing on the same tile — and it is kept; the
    /// chain order is not observable without the room's history and is not
    /// reproduced.</para>
    ///
    /// <para>The neighbour ring is <i>not</i> confined to the room: the
    /// predicate asks only that the tile be the keeper's lair, so a neighbour
    /// belonging to a lair joined at a corner qualifies, exactly as in the
    /// original.</para>
    /// </summary>
    public static bool TryFindFreeTile(
        WorldState world, GameDatabase database, RoomInstance room, TilePos from,
        byte ownerPlayerId, SimRandom rng, out int tileIndex, out TileFound how)
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

        int count = room.TileIndices.Count;
        int start = rng.NextInt(count);
        for (int step = 0; step < count; step++)
        {
            int index = room.TileIndices[(start + step) % count];
            if (!IsFreeLairTile(world, database, index, ownerPlayerId)) continue;

            tileIndex = index;
            how = TileFound.Walk;
            return true;
        }

        tileIndex = -1;
        how = TileFound.None;
        return false;
    }

    /// <summary>
    /// How long an idle, bedless creature waits between attempts to get a lair,
    /// in ticks.
    ///
    /// <para><b>Ours.</b> A failed attempt in the original falls through to a
    /// job and is retried at the creature's next idle decision; with no job
    /// system for non-workers here, the idle state's own fallback is the only
    /// cadence to borrow — it wanders for 12 ticks and decides again. Whether
    /// the original's tick is this project's tick is not established, so this
    /// is a number with the right shape rather than a measured one.</para>
    /// </summary>
    public const int IdleRedecisionTicks = 12;

    /// <summary>
    /// Puts this creature's own bed on the first free tile of any lair its
    /// keeper owns, from wherever the creature happens to be — <b>with no
    /// journey</b>.
    ///
    /// <para><b>Not how a creature gets a bed.</b> That is
    /// <see cref="Systems.CreatureSleepSystem"/>, which walks the creature to a
    /// room chosen by distance and finds the tile on arrival (ADR-0060). This is
    /// the placement without the walk, for a test that wants a creature already
    /// bedded and for level setup; no system calls it, and the name says
    /// "anywhere" so that nobody mistakes it for the mechanic it used to
    /// be.</para>
    ///
    /// <para><b>The bed placed is the creature's own kind</b>, from its record's
    /// <see cref="CreatureDef.LairObjectId"/> — which is the reason the bed has
    /// to be created here rather than shipped with the room. Twelve bed objects
    /// serve 46 creatures, and which of the twelve a tile holds cannot be known
    /// before the creature that will sleep in it is.</para>
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

    /// <summary>
    /// Puts this creature's own bed on a given tile and hands it over, without
    /// asking whether the tile is a lair or whether the creature wanted one.
    ///
    /// <para>The placement alone, separated because three callers need it
    /// without a search: <see cref="Systems.CreatureSleepSystem"/> once its
    /// creature is standing on the tile it found, <b>a level file</b>, whose
    /// objects arrive already placed (DK2 marks them <c>AutoPlaced</c>), and a
    /// test that wants a creature in a bed without walking it there.</para>
    ///
    /// <para>Unchecked on purpose. A caller that places a bed on rock has made a
    /// mistake this method cannot tell from a level that ships one — and
    /// <see cref="BedStillValid"/> already drops a bed whose tile is not a lair,
    /// on the next tick, which is the same answer arrived at honestly.</para>
    /// </summary>
    public static ObjectState PlaceBed(
        WorldState world, GameDatabase database, CreatureState creature, int tileIndex)
    {
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        ObjectState bed = world.SpawnObject(
            database, definition.LairObjectId, creature.OwnerPlayerId, tileIndex);
        bed.CreatureId = creature.Id;
        return bed;
    }

    /// <summary>
    /// Gives up this creature's bed, destroying the bed object with it, and says
    /// whether there was one.
    ///
    /// <para>A method rather than an assignment because forgetting it would
    /// leak a bed out of a lair permanently — and now literally so: the bed is a
    /// thing, and an unreleased one would sit on its tile forever with a
    /// creature id nothing answers to.</para>
    /// </summary>
    public static bool ReleaseBed(
        WorldState world, GameDatabase database, CreatureState creature)
    {
        ObjectState? bed = BedOf(world, database, creature);
        if (bed is null) return false;
        return world.RemoveObject(bed.Id);
    }

    /// <summary>
    /// Whether this creature's bed is still on a lair tile its keeper owns.
    ///
    /// <para>A bed is not a promise. The lair can be sold, dug out, or claimed
    /// by a rival while its owner is off somewhere, and a creature holding a bed
    /// on a tile that is no longer a lair would heal on a patch of rock. Checked
    /// every time rather than trusted, and the bed is dropped when it
    /// fails.</para>
    /// </summary>
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

    /// <summary>
    /// Whether it is time for this creature to go to bed: either it has been
    /// awake for the record's <c>TimeAwake</c>, or it is hurt past
    /// <c>CreatureSleepsWhenBelowPercentHealth</c> (50%).
    ///
    /// <para>The wounded clause is the data's: DK2 carries a percentage for
    /// exactly this, and it is what makes a losing fight end with creatures
    /// streaming home rather than dying in place.</para>
    ///
    /// <para><b>The cycle itself is a reading (Q103).</b> A Goblin's record says
    /// <c>TimeAwake</c> 240 and <c>TimeSleep</c> 9, and taking both as seconds
    /// gives four minutes up and nine seconds down — which is a strange ratio
    /// until you notice that nine seconds at 100 health a second is 900, well
    /// over half a Goblin's 1500. So a short sleep is a big heal, and the ratio
    /// stops looking odd. Still a reading, and the owner can settle it.</para>
    /// </summary>
    public static bool WantsToSleep(
        GameDatabase database, CreatureDef definition, CreatureState creature)
    {
        if (!NeedsBed(definition)) return false;
        if (definition.TimeAwake >= CreatureHealthRules.Never) return false;

        if (CreatureHealthRules.ShouldSleepFromWounds(database, definition, creature))
            return true;

        return creature.AwakeTicks >= SimulationTiming.TicksFromSeconds(definition.TimeAwake);
    }

    /// <summary>Whether this creature has slept for its record's <c>TimeSleep</c>.</summary>
    public static bool HasSleptEnough(CreatureDef definition, CreatureState creature) =>
        creature.SleepTicks >= SimulationTiming.TicksFromSeconds(definition.TimeSleep);
}
