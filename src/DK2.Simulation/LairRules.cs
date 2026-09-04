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
/// <para><b>Occupancy is derived, never stored.</b> A tile is taken if some
/// creature's <see cref="CreatureState.LairTileIndex"/> points at it — the same
/// discipline <see cref="RoomInstance"/> follows, and for the same reason: a
/// creature that dies, leaves or is removed cannot strand a bed that nothing
/// owns. The cost is a scan of the creature list per query, and creatures are
/// tens where tiles are tens of thousands.</para>
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

    /// <summary>Whether any creature already owns the bed on this tile.</summary>
    public static bool BedTaken(WorldState world, int tileIndex)
    {
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
            if (creatures[i].LairTileIndex == tileIndex) return true;
        return false;
    }

    /// <summary>
    /// How many creatures a lair can hold: one per tile.
    ///
    /// <para>See the type doc — this is the lair's rule, not rooms' rule.</para>
    /// </summary>
    public static int Capacity(RoomInstance lair) => lair.TileIndices.Count;

    /// <summary>How many of a lair's beds are already owned.</summary>
    public static int Occupied(WorldState world, RoomInstance lair)
    {
        int taken = 0;
        foreach (int index in lair.TileIndices)
            if (BedTaken(world, index)) taken++;
        return taken;
    }

    /// <summary>
    /// Finds a free bed for this creature in any lair its keeper owns and takes
    /// it, or leaves it bedless and returns false.
    ///
    /// <para><b>Lowest tile index wins</b>, across lairs visited in
    /// <see cref="RoomIndex"/>'s own order, so two creatures spawning on the
    /// same tick claim beds in creature order rather than in whatever order a
    /// dictionary produced. Which tile DK2 picks is UNKNOWN and almost certainly
    /// nearest-to-the-door; this is deterministic, which is the property that
    /// cannot be given up (ADR-0001).</para>
    ///
    /// <para>A keeper with no lair, or a full one, leaves the creature with no
    /// bed. In DK2 that is what <c>angerNoLair</c> is for — 100 for a Goblin,
    /// the joint-largest anger source in its record — and anger does not exist
    /// yet, so today it simply never heals. Q105.</para>
    /// </summary>
    public static bool TryClaimBed(
        WorldState world, GameDatabase database, CreatureState creature)
    {
        if (creature.LairTileIndex >= 0) return true;

        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if (!NeedsBed(definition)) return false;

        byte lairTerrain = database.Room(RoomTypeId.Lair).TerrainId;

        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != creature.OwnerPlayerId) continue;
            if (room.Definition.TerrainId != lairTerrain) continue;

            foreach (int index in room.TileIndices)
            {
                if (BedTaken(world, index)) continue;
                creature.LairTileIndex = index;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gives up this creature's bed. Called when it dies, and the reason it is a
    /// method rather than an assignment is that forgetting it would leak a bed
    /// out of a lair permanently.
    /// </summary>
    public static void ReleaseBed(CreatureState creature) => creature.LairTileIndex = -1;

    /// <summary>
    /// Whether this creature's bed is still a lair tile its keeper owns.
    ///
    /// <para>A bed is not a promise. The lair can be sold, dug out, or claimed
    /// by a rival while its owner is off somewhere, and a creature holding a bed
    /// on a tile that is no longer a lair would heal on a patch of rock. Checked
    /// every time rather than trusted, and the bed is dropped when it fails.</para>
    /// </summary>
    public static bool BedStillValid(
        WorldState world, GameDatabase database, CreatureState creature)
    {
        int index = creature.LairTileIndex;
        if (index < 0 || index >= world.TileCount) return false;

        ref TileState tile = ref world.TileAtIndex(index);
        return tile.OwnerPlayerId == creature.OwnerPlayerId
            && tile.TerrainId == database.Room(RoomTypeId.Lair).TerrainId;
    }

    /// <summary>Whether this creature is asleep in its own bed — the healing condition.</summary>
    public static bool AsleepInOwnBed(WorldState world, CreatureState creature)
    {
        if (creature.Activity != CreatureActivity.Sleeping) return false;
        if (creature.LairTileIndex < 0) return false;

        TilePos at = creature.Tile;
        return world.Contains(at)
            && (at.Y * world.Width) + at.X == creature.LairTileIndex;
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
