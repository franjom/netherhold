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
/// that nothing owns, because releasing the bed is what removes the object.
/// STRONGLY INFERRED rather than watched — Q131.</para>
///
/// <para>The cost is a scan of the object list per query, and beds are tens
/// where tiles are tens of thousands.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/creature-needs.md")]
public static class LairRules
{
    /// <summary>
    /// Whether this creature has a bed at all, by
    /// <see cref="CreatureDef.LairObjectId"/> — the object its bed <i>is</i>.
    ///
    /// <para>22 for a Goblin, 26 for a Vampire (a coffin), 18 for a Bile Demon.
    /// <b>0 for the Imp, the Skeleton and Horny</b>, and the owner CONFIRMED
    /// both of the interesting ones: a skeleton does not have a lair, and Horny
    /// is a summon rather than a member of a roster — it only appears on
    /// particular maps when conditions are met, which is also why it carries no
    /// <see cref="CreatureFlags.AvailableViaPortal"/> (ADR-0047).</para>
    ///
    /// <para>Read from the object id rather than from the sleep cycle because
    /// the two disagree: the <b>Dwarf</b> and the <b>Stone Knight</b> both have
    /// a bed object and a <c>TimeAwake</c> of 32767, so by the record they own a
    /// bed and never sleep in it. Which of the two fields DK2 actually gates on
    /// is Q104; nothing here depends on it, because a creature that never sleeps
    /// never heals in a bed either way.</para>
    /// </summary>
    public static bool NeedsBed(CreatureDef creature) => creature.LairObjectId != 0;

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

    /// <summary>
    /// Finds a free tile in any lair this creature's keeper owns and puts the
    /// creature's own bed on it, or leaves it bedless and returns false.
    ///
    /// <para><b>Lowest tile index wins</b>, across lairs visited in
    /// <see cref="RoomIndex"/>'s own order, so two creatures spawning on the
    /// same tick claim beds in creature order rather than in whatever order a
    /// dictionary produced. Which tile DK2 picks is UNKNOWN and almost certainly
    /// nearest-to-the-door; this is deterministic, which is the property that
    /// cannot be given up (ADR-0001).</para>
    ///
    /// <para><b>The bed placed is the creature's own kind</b>, from its record's
    /// <see cref="CreatureDef.LairObjectId"/> — which is the reason the bed has
    /// to be created here rather than shipped with the room. Twelve bed objects
    /// serve 46 creatures, and which of the twelve a tile holds cannot be known
    /// before the creature that will sleep in it is.</para>
    ///
    /// <para>A keeper with no lair, or a full one, leaves the creature with no
    /// bed. In DK2 that is what <c>angerNoLair</c> is for — 100 for a Goblin,
    /// the joint-largest anger source in its record — and
    /// <see cref="CreatureAngerRules"/> charges it. Q105.</para>
    /// </summary>
    public static bool TryClaimBed(
        WorldState world, GameDatabase database, CreatureState creature)
    {
        if (HasBed(world, database, creature)) return true;

        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if (!NeedsBed(definition)) return false;

        byte lairTerrain = database.Room(RoomTypeId.Lair).TerrainId;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != creature.OwnerPlayerId) continue;
            if (room.Definition.TerrainId != lairTerrain) continue;

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
    /// <para>The placement half of <see cref="TryClaimBed"/>, separated because
    /// two other callers need it without the search: <b>a level file</b>, whose
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
