// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The Hand of Evil: picking creatures up, dropping them, slapping them. Read from the
/// original's own handlers, whose fields these reproduce by name; addresses and evidence in
/// <c>docs/research/hand-of-evil.md</c>, decisions in ADR-0062.</summary>
public static class HandRules
{
    /// <summary>How many things one keeper may hold: <c>MaxNumberOfThingsInHand</c>, shipped at
    /// <b>64</b>. From the table, not pinned: the original compares against a per-keeper field this
    /// variable seeds.</summary>
    public static int Capacity(GameDatabase database) =>
        database[VariableTypeId.MaxNumberOfThingsInHand];

    /// <summary>How long a creature is held before the hand starts to affect its mood: <b>20 game
    /// seconds</b>, hardcoded in the original as <c>PickupTick + 20 x</c> variable 19, so moving a
    /// creature somewhere costs nothing. Trap: the table's
    /// <c>TimeInHandBeforeCreaturesBecomeAngrySeconds</c> is 1000, not this; UNKNOWN what it is for (Q160).</summary>
    public const int AngerGraceSeconds = 20;

    /// <summary>The grace period in our ticks.</summary>
    public const int AngerGraceTicks = AngerGraceSeconds * SimulationTiming.TicksPerSecond;

    /// <summary><b>There is no slap cooldown.</b> The original's <c>now &lt; LastSlapTick +</c>
    /// variable 19 test guards only the knock-back impulse; anger, damage, raised Efficiency and
    /// the speed boost land however recent the last slap was (hand-of-evil.md §4). Not implemented: the knock-back, there being no physics, so nothing reads this.</summary>
    public const int KnockBackWindowSeconds = 1;

    /// <summary>Whether this creature is in somebody's hand — DK2's <c>StateFlags1.InHand</c>.
    /// Belongs beside <see cref="CreatureDeathRules.IsDown"/> wherever a system makes a creature act: a held creature does not walk, work, sleep or look for a lair.</summary>
    public static bool IsHeld(CreatureState creature) => creature.InHand;

    /// <summary>Whether <paramref name="creature"/> may be picked up by
    /// <paramref name="playerId"/> now. The original's gates in its order:
    /// <see cref="CreatureFlags.CanBePickedUp"/>, not in a hand, ownership, <c>CreatureDying</c>
    /// (bit <c>0x200000</c>), two states we lack (hand-of-evil.md §3). Neither a corpse nor a
    /// creature bleeding out may be lifted (Q161, CONFIRMED owner); a prisoner is the one ownership exception (<see cref="CreatureState.PrisonOwnerPlayerId"/>, ADR-0064).</summary>
    public static bool CanPickUp(
        WorldState world, GameDatabase database, CreatureState creature, byte playerId)
    {
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if (!definition.Flags.HasFlag(CreatureFlags.CanBePickedUp)) return false;
        if (creature.InHand) return false;
        if (creature.OwnerPlayerId != playerId
            && creature.PrisonOwnerPlayerId != playerId) return false;
        if (CreatureDeathRules.IsDown(creature)) return false;

        return world.Player(playerId).Hand.Count < Capacity(database);
    }

    /// <summary>Whether this object is in somebody's hand.</summary>
    public static bool IsHeld(ObjectState thing) => thing.InHand;

    /// <summary>Whether <paramref name="thing"/> may be lifted by <paramref name="playerId"/> now.
    /// CONFIRMED (owner, Q196): the eligible set is <see cref="ObjectDef.CanBePickedUp"/>, 26 of
    /// the 145 records; the cursor search that reads it also admits the Dungeon Heart by id, a
    /// targeting special case. Ownership is not tested. An object in an imp's arms is refused,
    /// ours in form and DK2's in effect, since it takes a carried object off its tile.</summary>
    public static bool CanPickUpObject(
        WorldState world, GameDatabase database, ObjectState thing, byte playerId)
    {
        ObjectDef? definition = database.ObjectById(thing.ObjectTypeId);
        if (definition is null || !definition.CanBePickedUp) return false;
        if (thing.InHand) return false;
        if (thing.CarriedByCreatureId >= 0) return false;

        return world.Player(playerId).Hand.Count < Capacity(database);
    }

    /// <summary>Where a keeper may put an <b>object</b> down: <b>a tile it owns</b>. CONFIRMED
    /// (owner, Q198); creatures have one exception, <see cref="CanDropCreatureOn"/>. Where the rule
    /// lives in the original is UNREAD — the drop handler has no ownership test, so the gate is in
    /// the input layer and this reproduces an observation, not a routine. Trap: it is not <c>CanBeDroppedOnAnyLand</c>, set on one record with no reader in the executable.</summary>
    public static bool CanDropObjectOn(WorldState world, TilePos tile, byte playerId) =>
        world.Contains(tile) && world.Tile(tile.X, tile.Y).OwnerPlayerId == playerId;

    /// <summary>Where a keeper may put a <b>creature</b> down: <b>a tile it owns</b>, and for a
    /// worker only, <b>unclaimed floor</b>. CONFIRMED (owner, Q198): imps are the only creature
    /// droppable onto unclaimed dirt path. Where the rule lives is UNREAD, as for objects.
    /// Deviates: keyed on <see cref="CreatureFlags.IsWorker"/>, which also admits the Dwarf, where
    /// the owner said <i>imps</i> — worker behaviour is keyed on the flag throughout, and the
    /// difference is unreachable today (docs/research/imp-tasks.md).</summary>
    public static bool CanDropCreatureOn(
        WorldState world, GameDatabase database, TilePos tile, byte playerId,
        CreatureDef mover)
    {
        if (!world.Contains(tile)) return false;
        if (world.Tile(tile.X, tile.Y).OwnerPlayerId == playerId) return true;
        if (!mover.Flags.HasFlag(CreatureFlags.IsWorker)) return false;

        return IsUnclaimedFloor(database.TerrainById(world.Tile(tile.X, tile.Y).TerrainId));
    }

    /// <summary>Floor that no keeper can have claimed: <b>Dirt Path</b> and the <b>Mana Vault</b>,
    /// and nothing else in the shipped table of 41. Structural, not by id — not
    /// <see cref="TerrainFlags.Solid"/>, not <see cref="TerrainFlags.Ownable"/>, not
    /// <see cref="TerrainFlags.Room"/>. Water and lava are excluded as a deliberate stop: both
    /// would qualify and 44 of 46 creatures wade, so an imp could be dropped in a lake. UNKNOWN whether the original offers that (checklist item 5); refusing invents least.</summary>
    public static bool IsUnclaimedFloor(TerrainDef terrain) =>
        !terrain.IsSolid && !terrain.IsOwnable && !terrain.IsRoom
        && !terrain.IsWater && !terrain.IsLava;

    /// <summary>Whether <paramref name="creature"/> may be slapped by
    /// <paramref name="playerId"/>: <see cref="CreatureFlags.CanBeSlapped"/>, the keeper's own
    /// creature, not in a hand, not down, and no timing gate (see
    /// <see cref="KnockBackWindowSeconds"/>). A prisoner is pickable but not slappable. Horny is
    /// excluded before every other test, as in the original: a slap on
    /// <see cref="CreatureFlags.IsHorny"/> runs another routine and never reaches the work bonus
    /// (Q158).</summary>
    public static bool CanSlap(
        GameDatabase database, CreatureState creature, byte playerId)
    {
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if (!definition.Flags.HasFlag(CreatureFlags.CanBeSlapped)) return false;
        if (definition.Flags.HasFlag(CreatureFlags.IsHorny)) return false;
        if (creature.InHand) return false;
        if (creature.OwnerPlayerId != playerId) return false;
        return !CreatureDeathRules.IsDown(creature);
    }

    /// <summary>Whether a held creature's mood should take <c>AngerInHand</c> on this tick: once a
    /// game second, from <see cref="AngerGraceTicks"/> after it was picked up. The original tests
    /// <c>now % </c>variable 19<c> == 0</c>, the last tick of each game second — the cadence the
    /// anger ladder already runs on (ADR-0017), so this rides the same clock.</summary>
    public static bool AngerDueThisTick(CreatureState creature, GameTick now)
    {
        if (!creature.InHand) return false;
        if (now.Value - creature.PickupTick <= AngerGraceTicks) return false;

        return (now.Value + 1) % SimulationTiming.TicksPerSecond == 0;
    }
}
