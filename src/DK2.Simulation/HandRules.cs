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

    /// <summary>State 75: what the pick-up preparation <c>0x004BFBD0</c> enters, through idle when
    /// the current category is outside its entry mask, before the hand handler <c>0x004BC500</c>
    /// tests capacity; both the human handler <c>0x004C0BE0</c> and the keeper's routines call it
    /// first. Its update returns 1, so a creature the hand then refuses stays there; what else
    /// leaves it is UNKNOWN (hand-of-evil.md §2).</summary>
    public const int PreparationState = 75;

    /// <summary>Whether this creature is in somebody's hand — DK2's <c>StateFlags1.InHand</c>.
    /// Belongs beside <see cref="CreatureDeathRules.IsDown"/> wherever a system makes a creature act: a held creature does not walk, work, sleep or look for a lair.</summary>
    public static bool IsHeld(CreatureState creature) => creature.InHand;

    /// <summary>DK2's <c>LastDroppedCounter</c> (<c>+0x46D</c>): a word held at zero while the
    /// creature is in the hand and counted up once a turn otherwise, from its construction or
    /// its last drop (<c>0x00489A4D</c>, <c>0x00489A5F</c>). Wraps as the word does. Read by the
    /// keeper's hand routines, which leave a creature alone for 300 turns after a drop.</summary>
    public static ushort LastDroppedCounter(CreatureState creature, long now) =>
        creature.InHand ? (ushort)0 : unchecked((ushort)(now - creature.DroppedTick));

    /// <summary>Creature permission plus hand capacity. Remaining binary gates needing
    /// missing lifecycle state are listed in hand-of-evil.md §3.</summary>
    public static bool CanPickUp(
        WorldState world, GameDatabase database, CreatureState creature, byte playerId)
        => HasPickupPermission(world, database, creature, playerId)
            && world.Player(playerId).Hand.Count < Capacity(database);

    /// <summary>The represented creature-permission gates, independent of hand capacity.
    /// State exclusions inspect the current state, not a travel resume slot.</summary>
    public static bool HasPickupPermission(
        WorldState world, GameDatabase database, CreatureState creature, byte playerId)
    {
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        // Permission also covers original states whose simulation activity is not implemented.
        if (CreatureDeathRules.HasCollapsed(creature) || creature.InHand
            || creature.Dk2StateId is CreatureDeathRules.FallingState
                or CreatureDeathRules.DyingState or CreatureStateMachine.NoState) return false;
        // A nonzero removal stamp refuses any creature, an imp or not (0x0048E6A0, ADR-0277).
        if (creature.ManaRemovalStamp != 0) return false;
        if (!definition.Flags.HasFlag(CreatureFlags.CanBePickedUp)) return false;
        if (creature.Dk2StateId is 76 or 193) return false;
        if (creature.OwnerPlayerId != playerId)
            return creature.IsTool || creature.PrisonOwnerPlayerId == playerId;
        if (creature.CombatPitFighter)
            return world.Contains(creature.Tile)
                && world.Tile(creature.Tile.X, creature.Tile.Y).OwnerPlayerId == playerId;
        if (creature.Dk2StateId is 51 or 52) return false;
        if (definition.Flags.HasFlag(CreatureFlags.IsWorker) && (creature.Flags & 9) == 0) return false;
        // The stored room must be nobody's or the owner's. A Prison writes that membership on
        // entry (0x0047B740 calls 0x004E5A70), so an own creature in another keeper's cells is
        // refused; other rooms' membership is not stored here (creature-pickup.md).
        return !PrisonRules.IsImprisoned(creature) || creature.PrisonOwnerPlayerId == playerId;
    }

    /// <summary>Whether this object is in somebody's hand.</summary>
    public static bool IsHeld(ObjectState thing) => thing.InHand;

    /// <summary>Whether <paramref name="thing"/> may be lifted by <paramref name="playerId"/> now.
    /// CONFIRMED (owner, Q196): the eligible set is <see cref="ObjectDef.CanBePickedUp"/>, 26 of
    /// the 145 records; the cursor search that reads it also admits the Dungeon Heart by id, a
    /// targeting special case. The object's owner is not tested, but <b>gold lifts only from
    /// the keeper's own claimed tile</b> (owner, Q196). An object in an imp's arms is refused,
    /// ours in form and DK2's in effect, since it takes a carried object off its tile.</summary>
    public static bool CanPickUpObject(
        WorldState world, GameDatabase database, ObjectState thing, byte playerId)
    {
        ObjectDef? definition = database.ObjectById(thing.ObjectTypeId);
        if (definition is null || !definition.CanBePickedUp) return false;
        if (thing.InHand) return false;
        if (thing.CarriedByCreatureId >= 0) return false;
        if (definition.IsGold && world.TileAtIndex(thing.TileIndex).OwnerPlayerId != playerId)
            return false;

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

    /// <summary>Whether a drop of <paramref name="creature"/> on <paramref name="tile"/> would land:
    /// the two tests <see cref="Systems.HandSystem"/> refuses on, in its order — a disposal tile
    /// (<see cref="DisposalRules"/>) or a walkable one, and then <see cref="CanDropCreatureOn"/>.</summary>
    public static bool DropLands(
        WorldState world, GameDatabase database, CreatureState creature, CreatureDef definition,
        TilePos tile, byte playerId)
    {
        if (!world.Contains(tile)) return false;
        bool disposed = !PrisonRules.IsImprisoned(creature)
            && DisposalRules.Disposes(world, database, definition, tile, playerId);
        if (!disposed && !new WorldPathGrid(database, world, definition).IsWalkable(tile.X, tile.Y))
            return false;
        return CanDropCreatureOn(world, database, tile, playerId, definition);
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

        // A prisoner answers to its jailer and to nobody else, its own keeper included:
        // "a keeper can't slap its own creature in someone else's dungeon" (owner,
        // 2026-09-11, ADR-0140). Anything at liberty is its owner's as before.
        if (PrisonRules.IsImprisoned(creature))
            return creature.PrisonOwnerPlayerId == playerId
                   && !CreatureDeathRules.IsDown(creature);

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
