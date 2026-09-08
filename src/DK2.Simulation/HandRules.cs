// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The Hand of Evil: picking creatures up, dropping them, and slapping them.
///
/// <para>Read from the original's own handlers; addresses and evidence in
/// <c>docs/research/hand-of-evil.md</c>, decisions in ADR-0062. The fields
/// reproduced are DK2's own: <c>StateFlags1.InHand</c>, <c>PickupTick</c>,
/// <c>LastSlapTick</c>, <c>WorkFasterTimer</c>, <c>ImpJobData.JustDropped</c>,
/// <c>NumberOfThingsInHand</c>.</para>
/// </summary>
public static class HandRules
{
    /// <summary>
    /// How many things one keeper may hold: <c>MaxNumberOfThingsInHand</c>,
    /// which ships at <b>64</b>. Read from the table rather than pinned, because
    /// the original's pick-up handler compares the count against a per-keeper
    /// field that this variable seeds.
    /// </summary>
    public static int Capacity(GameDatabase database) =>
        database[VariableTypeId.MaxNumberOfThingsInHand];

    /// <summary>
    /// How long a creature is held before the hand starts to affect its mood:
    /// <b>20 game seconds</b>, hardcoded in the original as
    /// <c>PickupTick + 20 x</c> variable 19.
    ///
    /// <para>A grace period, which is why moving a creature somewhere costs
    /// nothing. Trap: the balance table's
    /// <c>TimeInHandBeforeCreaturesBecomeAngrySeconds</c> is 1000 and is not
    /// this number; what it belongs to is UNKNOWN (Q160).</para>
    /// </summary>
    public const int AngerGraceSeconds = 20;

    /// <summary>The grace period in our ticks.</summary>
    public const int AngerGraceTicks = AngerGraceSeconds * SimulationTiming.TicksPerSecond;

    /// <summary>
    /// <b>There is no slap cooldown.</b> The original tests
    /// <c>now &lt; LastSlapTick +</c> variable 19, and that test guards one
    /// thing: the knock-back impulse it writes into the creature's velocity. The
    /// anger, the damage, the raised Efficiency and the speed boost all happen
    /// however recent the last slap was, so the owner's six slaps to a sulking
    /// Salamander (Q114) were six because a human clicks at human speed.
    /// <para>Not implemented: the knock-back itself, there being no physics to be
    /// knocked back by — so this constant is read by nothing and kept because
    /// <see cref="CreatureState.LastSlapTick"/> is kept.</para>
    /// </summary>
    public const int KnockBackWindowSeconds = 1;

    /// <summary>
    /// Whether this creature is in somebody's hand — DK2's
    /// <c>StateFlags1.InHand</c>, bit 0 of the flag word its dump prints first.
    ///
    /// <para>Belongs beside <see cref="CreatureDeathRules.IsDown"/> wherever a
    /// system makes a creature act: a held creature does not walk, work, sleep
    /// or look for a lair, because as far as the dungeon is concerned it is not
    /// in it.</para>
    /// </summary>
    public static bool IsHeld(CreatureState creature) => creature.InHand;

    /// <summary>
    /// Whether <paramref name="creature"/> may be picked up by
    /// <paramref name="playerId"/> now. The original's gates, in its order:
    /// <see cref="CreatureFlags.CanBePickedUp"/>; not in a hand; ownership;
    /// <c>CreatureDying</c>; two states we lack (hand-of-evil.md §3).
    /// <para>A creature that is down is refused — neither a corpse nor an
    /// injured creature bleeding out may be lifted (Q161, CONFIRMED owner), and
    /// the handler agrees, refusing bit <c>0x200000</c> of the first state-flag
    /// word. A prisoner is the only ownership exception: the holding keeper may
    /// lift one out without freeing it
    /// (<see cref="CreatureState.PrisonOwnerPlayerId"/>, ADR-0064).</para>
    /// </summary>
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

    /// <summary>
    /// Whether <paramref name="thing"/> may be lifted by
    /// <paramref name="playerId"/> right now.
    /// <para>CONFIRMED by the owner (Q196). The eligible set is
    /// <see cref="ObjectDef.CanBePickedUp"/>, 26 of the 145 records, whose only
    /// reader is the cursor's target search — which also admits the Dungeon
    /// Heart by id, a targeting special case and not a lifting one.</para>
    /// <para>Ownership is not tested: neither the pick-up handler nor the cursor
    /// search asks who owns the object. An object in an imp's arms is refused,
    /// ours in form and DK2's in effect — it takes a carried object off its
    /// tile.</para>
    /// </summary>
    public static bool CanPickUpObject(
        WorldState world, GameDatabase database, ObjectState thing, byte playerId)
    {
        ObjectDef? definition = database.ObjectById(thing.ObjectTypeId);
        if (definition is null || !definition.CanBePickedUp) return false;
        if (thing.InHand) return false;
        if (thing.CarriedByCreatureId >= 0) return false;

        return world.Player(playerId).Hand.Count < Capacity(database);
    }

    /// <summary>
    /// Where a keeper may put an <b>object</b> down: <b>a tile it owns</b> —
    /// CONFIRMED by the owner (Q198), with a screenshot where the drop is
    /// offered on a Claimed Path tooltipped <i>"Your territory"</i>. For
    /// creatures the rule has one exception: <see cref="CanDropCreatureOn"/>.
    /// <para>Where the rule lives in the original is UNREAD: its drop handler
    /// (<c>FUN_004bc710</c>) has no ownership test and assumes the drop is
    /// already legal, so the gate is in the input layer that draws the marker.
    /// This reproduces an observation, not a routine. Trap: it is not
    /// <c>CanBeDroppedOnAnyLand</c>, which is set on one record, Call To Arms,
    /// and has no reader anywhere in the executable.</para>
    /// </summary>
    public static bool CanDropObjectOn(WorldState world, TilePos tile, byte playerId) =>
        world.Contains(tile) && world.Tile(tile.X, tile.Y).OwnerPlayerId == playerId;

    /// <summary>
    /// Where a keeper may put a <b>creature</b> down: <b>a tile it owns</b> —
    /// and, for a worker only, <b>unclaimed floor</b>.
    /// <para>CONFIRMED by the owner (Q198): imps are the only creature that may
    /// be dropped onto unclaimed dirt path, everything else on the keeper's own
    /// ground as <see cref="CanDropObjectOn"/> has it. Where the rule lives in
    /// the original is UNREAD, exactly as for objects.</para>
    /// <para>Deviates: keyed on <see cref="CreatureFlags.IsWorker"/>, which also
    /// admits the Dwarf, where the owner said <i>imps</i>. Deliberate — worker
    /// behaviour is keyed on the flag, not the id, throughout
    /// (docs/research/imp-tasks.md) — and unreachable today.</para>
    /// </summary>
    public static bool CanDropCreatureOn(
        WorldState world, GameDatabase database, TilePos tile, byte playerId,
        CreatureDef mover)
    {
        if (!world.Contains(tile)) return false;
        if (world.Tile(tile.X, tile.Y).OwnerPlayerId == playerId) return true;
        if (!mover.Flags.HasFlag(CreatureFlags.IsWorker)) return false;

        return IsUnclaimedFloor(database.TerrainById(world.Tile(tile.X, tile.Y).TerrainId));
    }

    /// <summary>
    /// Floor that no keeper can have claimed: <b>Dirt Path</b> and the
    /// <b>Mana Vault</b>, and nothing else in the shipped table of 41.
    /// <para>Structural rather than by id — not <see cref="TerrainFlags.Solid"/>
    /// so it is floor, not <see cref="TerrainFlags.Ownable"/> and not
    /// <see cref="TerrainFlags.Room"/> so no keeper's name can be on it. Both
    /// carry <c>0x10008000</c> where their claimed counterparts are
    /// <c>Ownable</c>. Water and lava are excluded as a deliberate stop, not a
    /// reading: both would otherwise qualify and 44 of 46 creatures can wade, so
    /// an imp could be dropped in a lake. UNKNOWN whether the original offers
    /// that (checklist item 5); refusing invents least.</para>
    /// </summary>
    public static bool IsUnclaimedFloor(TerrainDef terrain) =>
        !terrain.IsSolid && !terrain.IsOwnable && !terrain.IsRoom
        && !terrain.IsWater && !terrain.IsLava;

    /// <summary>
    /// Whether <paramref name="creature"/> may be slapped by
    /// <paramref name="playerId"/>.
    /// <para><see cref="CreatureFlags.CanBeSlapped"/>, the keeper's own creature,
    /// not in a hand, not already down, and no timing gate (see
    /// <see cref="KnockBackWindowSeconds"/>). A prisoner is not slappable though
    /// it is pickable: the ownership exception <see cref="CanPickUp"/> makes is
    /// for lifting one out of a cell.</para>
    /// <para>Horny is excluded, and the original excludes him before every other
    /// test: a slap on a creature carrying <see cref="CreatureFlags.IsHorny"/>
    /// runs a different routine and never reaches the work bonus (Q158).</para>
    /// </summary>
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

    /// <summary>
    /// Whether a held creature's mood should take <c>AngerInHand</c> on this
    /// tick: once a game second, from
    /// <see cref="AngerGraceTicks"/> after it was picked up.
    ///
    /// <para>The original tests <c>now % </c>variable 19<c> == 0</c>, the last
    /// tick of each game second — the cadence the anger ladder already runs on
    /// (ADR-0043), so this rides the same clock.</para>
    /// </summary>
    public static bool AngerDueThisTick(CreatureState creature, GameTick now)
    {
        if (!creature.InHand) return false;
        if (now.Value - creature.PickupTick <= AngerGraceTicks) return false;

        return (now.Value + 1) % SimulationTiming.TicksPerSecond == 0;
    }
}
