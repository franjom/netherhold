// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The Hand of Evil: picking creatures up, dropping them, and slapping them.
///
/// <para>The keeper's only direct contact with a creature, and the oldest verb
/// in the series. Everything here is read from the original's own handlers —
/// <c>docs/research/hand-of-evil.md</c> gives the addresses and the evidence,
/// and ADR-0062 the decisions. DK2's debug dump names the fields this
/// reproduces: <c>StateFlags1.InHand</c>, <c>PickupTick</c>,
/// <c>LastSlapTick</c>, <c>WorkFasterTimer</c>, <c>ImpJobData.JustDropped</c>,
/// <c>NumberOfThingsInHand</c>.</para>
///
/// <para>Pure and static, like every other <c>*Rules</c> class here: the world
/// goes in as an argument and no state lives here.</para>
/// </summary>
public static class HandRules
{
    /// <summary>
    /// How many things one keeper may hold: <c>MaxNumberOfThingsInHand</c>,
    /// which ships at <b>64</b>.
    ///
    /// <para>Read from the table rather than pinned, because the original reads
    /// it from the same place — its pick-up handler compares the count against
    /// a per-keeper field that the variable seeds, and its debug dump prints
    /// both side by side.</para>
    /// </summary>
    public static int Capacity(GameDatabase database) =>
        database[VariableTypeId.MaxNumberOfThingsInHand];

    /// <summary>
    /// How long a creature is held before the hand starts to affect its mood:
    /// <b>20 game seconds</b>, hardcoded in the original as
    /// <c>PickupTick + 20 x</c> variable 19.
    ///
    /// <para>A grace period, and it is why picking a creature up to move it
    /// somewhere costs nothing. The balance table's
    /// <c>TimeInHandBeforeCreaturesBecomeAngrySeconds</c> is 1000 and is
    /// <b>not</b> this number; what it belongs to is UNKNOWN (Q160).</para>
    /// </summary>
    public const int AngerGraceSeconds = 20;

    /// <summary>The grace period in our ticks.</summary>
    public const int AngerGraceTicks = AngerGraceSeconds * SimulationTiming.TicksPerSecond;

    /// <summary>
    /// <b>There is no slap cooldown</b>, and finding that out was worth the
    /// look. The original does test <c>now &lt; LastSlapTick +</c> variable 19,
    /// and what that test guards is <i>one</i> thing: the knock-back impulse it
    /// writes into the creature's velocity. Everything else about a slap — the
    /// anger, the damage, the raised Efficiency, the speed boost — happens
    /// whether or not the last slap was a moment ago.
    ///
    /// <para>So a keeper who can click fast angers a creature as fast as they
    /// can click, and the reason the owner could count six slaps to a sulking
    /// Salamander (Q114) is simply that a human clicks at human speed. This
    /// project has no physics for a creature to be knocked back <i>by</i>, so
    /// the window is recorded here and read by nothing — the constant is kept
    /// because the field it would read is kept
    /// (<see cref="CreatureState.LastSlapTick"/>), and because a first draft of
    /// ADR-0062 had it gating the whole slap and its test caught the error.</para>
    /// </summary>
    public const int KnockBackWindowSeconds = 1;

    /// <summary>
    /// Whether this creature is in somebody's hand — DK2's
    /// <c>StateFlags1.InHand</c>, bit 0 of the flag word its dump prints first.
    ///
    /// <para>The guard that belongs beside
    /// <see cref="CreatureDeathRules.IsDown"/> wherever a system makes a
    /// creature act in the world: a held creature does not walk, work, sleep or
    /// go looking for a lair, because as far as the dungeon is concerned it is
    /// not in it.</para>
    /// </summary>
    public static bool IsHeld(CreatureState creature) => creature.InHand;

    /// <summary>
    /// Whether <paramref name="creature"/> may be picked up by
    /// <paramref name="playerId"/> right now.
    ///
    /// <para>The original's gates, in its order: the record must carry
    /// <see cref="CreatureFlags.CanBePickedUp"/>; the thing must not already be
    /// in a hand; an ownership test; the state flag <c>CreatureDying</c>; and
    /// two creature states this project does not have — see
    /// docs/research/hand-of-evil.md §3.</para>
    ///
    /// <para><b>A creature that is down is refused, and that is DK2's rule
    /// rather than ours</b> — CONFIRMED by the owner (2026-09-05): <i>"a corpse
    /// can not be picked up by hand of evil and dropped to a graveyard, only an
    /// imp can carry it to its grave, also an injured creature, lying on the
    /// floor bleeding out, can't be picked up by hand of evil"</i>. The binary
    /// agrees: the pick-up handler refuses bit <c>0x200000</c> of the first
    /// state-flag word, and DK2's own creature dump names that bit
    /// <c>CreatureDying</c>. ADR-0062 recorded the opposite for a day, on the
    /// strength of having read the handler without having named the bit — a
    /// narrowing invented to excuse a missing mechanic, which is the shape this
    /// project has been caught by four times. Q161.</para>
    ///
    /// <para><b>A prisoner is the exception to ownership</b>, and the only one:
    /// <i>"a hand of evil can then pick up that creature from prison and drop it
    /// around the dungeon, back in prison, or onto a torture rack in the torture
    /// chamber"</i> (owner, same day). So the keeper who holds a creature in a
    /// cell may lift it out, and does not thereby free it —
    /// <see cref="CreatureState.PrisonOwnerPlayerId"/> survives the trip and the
    /// drop decides (ADR-0064).</para>
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
    ///
    /// <para><b>The mechanic is CONFIRMED by the owner</b> (2026-09-06):
    /// <i>"I can pick up a chicken and drop it in the prison, or anywhere in
    /// the dungeon, on my owned tiles"</i>. Q196.</para>
    ///
    /// <para><b>The eligible set is read.</b>
    /// <see cref="ObjectDef.CanBePickedUp"/> is 26 of the 145 records — the
    /// three golds, the Chicken, Call To Arms, all fifteen specials and six odds
    /// and ends — and its <b>only reader in the whole executable</b> is the
    /// cursor's target search. That search also admits the <b>Dungeon Heart</b>
    /// by id regardless of the flag, which is a targeting special case and not a
    /// lifting one; the Heart is not liftable and nothing here pretends
    /// otherwise.</para>
    ///
    /// <para><b>Ownership is not tested.</b> Neither the original's pick-up
    /// handler nor the cursor search asks who owns the object, and the owner's
    /// account concerns where a thing may be <i>put down</i> rather than what
    /// may be lifted. Left out rather than guessed at.</para>
    ///
    /// <para><b>An object in an imp's arms is refused</b>, which is ours in form
    /// and the original's in effect — DK2 takes a carried object off its tile
    /// entirely, so the cursor search cannot find it.</para>
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
    /// Where a keeper may put an <b>object</b> down: <b>a tile it owns</b>.

    /// <para><b>Not objects only — see <see cref="CanDropCreatureOn"/>.</b> This
    /// comment argued for a day that a creature <i>can</i> be dropped on
    /// somebody else's ground, on the grounds that DK2's drop handler has a
    /// branch changing the creature's owner when the tile belongs to neither
    /// keeper, and that the branch would otherwise be unreachable. <b>The
    /// inference was wrong</b> (Q198, owner 2026-09-06): it shows only that the
    /// <i>handler</i> does not refuse, which was already known of the ownership
    /// rule here too — the gate is in the input layer that offers the drop. A
    /// handler that does not refuse is not a UI that offers. Whatever reaches
    /// the owner-changing branch is not the ordinary drop.</para>
    ///
    /// <para><b>CONFIRMED by the owner</b> (2026-09-06) — <i>"drop it in the
    /// prison, or anywhere in the dungeon, on my owned tiles"</i> — and by the
    /// screenshot beside it, where the drop is offered on a Claimed Path with
    /// the tile's own tooltip reading <i>"Your territory"</i>.</para>
    ///
    /// <para><b>Where the rule lives in the original is UNREAD</b>, and saying
    /// so matters. Its drop handler (<c>FUN_004bc710</c>) contains no ownership
    /// test at all; it assumes the drop is already legal, which puts the gate in
    /// the input layer that draws the marker the screenshot shows. So this
    /// reproduces an observation, not a routine.</para>
    ///
    /// <para><b>And it is emphatically NOT <c>CanBeDroppedOnAnyLand</c>.</b>
    /// That flag is set on exactly one record, Call To Arms, and a raw scan of
    /// the executable finds <b>no reader for it anywhere</b> — a fourth dead
    /// data field, after three dead balance variables. Its name is OpenKeeper's
    /// reading of a bit, it fits this rule beautifully, and building on it would
    /// have been this project's standing mistake for the fourth time. The check
    /// cost one scan.</para>
    /// </summary>
    public static bool CanDropObjectOn(WorldState world, TilePos tile, byte playerId) =>
        world.Contains(tile) && world.Tile(tile.X, tile.Y).OwnerPlayerId == playerId;

    /// <summary>
    /// Where a keeper may put a <b>creature</b> down: <b>a tile it owns</b> —
    /// and, for a worker only, <b>unclaimed floor</b>.
    ///
    /// <para><b>CONFIRMED by the owner</b> (2026-09-06, Q198): <i>"imps are the
    /// only creature that can be dropped onto dirt path tiles, those are tiles
    /// that are unclaimed by any keeper"</i>. Everything else goes down on the
    /// keeper's own ground, which is the same rule
    /// <see cref="CanDropObjectOn"/> already had for objects — so the hand has
    /// one ownership rule with one exception, not two rules.</para>
    ///
    /// <para><b>Keyed on <see cref="CreatureFlags.IsWorker"/>, which is a
    /// deliberate widening of what was watched.</b> The owner said <i>imps</i>;
    /// the flag falls on exactly two creatures, the Imp and the <b>Dwarf</b>,
    /// and this project has decided twice already to key worker behaviour on the
    /// flag rather than on the id (docs/research/imp-tasks.md, portals.md). A
    /// hero keeper dropping a Dwarf is not a thing anything here can do yet, so
    /// the widening costs nothing today and is recorded rather than hidden.</para>
    ///
    /// <para><b>Where the rule lives in the original is UNREAD</b>, exactly as
    /// for objects: <c>FUN_004bc710</c> has no ownership test at all, so the
    /// gate is in the input layer that draws the drop marker, and the imp
    /// exception lives there too. This reproduces an observation, not a
    /// routine.</para>
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
    ///
    /// <para>Structural rather than by id — not <see cref="TerrainFlags.Solid"/>
    /// so it is floor, not <see cref="TerrainFlags.Ownable"/> and not
    /// <see cref="TerrainFlags.Room"/> so no keeper's name can be on it. Dirt
    /// Path and Mana Vault carry the identical flag word <c>0x10008000</c>
    /// (<c>FillInable, TerrainLight</c>), and their claimed counterparts —
    /// Claimed Path and Claimed Vault — are `Ownable`, which is the pair that
    /// makes "unclaimed" mean something.</para>
    ///
    /// <para><b>Water and lava are excluded on purpose, and that is a stop
    /// rather than a reading.</b> Both are unownable non-solid terrain and would
    /// otherwise qualify, and 44 of the 46 creatures can wade — so without this
    /// clause a keeper here could drop an imp in the middle of a lake. Whether
    /// the original offers that is <b>UNKNOWN</b> and is on the observation
    /// checklist (item 5), together with a bridge and a rival's <i>unclaimed</i>
    /// room floor. Refusing is the narrower reading of <i>"unclaimed by any
    /// keeper"</i> and the one that invents least.</para>
    /// </summary>
    public static bool IsUnclaimedFloor(TerrainDef terrain) =>
        !terrain.IsSolid && !terrain.IsOwnable && !terrain.IsRoom
        && !terrain.IsWater && !terrain.IsLava;

    /// <summary>
    /// Whether <paramref name="creature"/> may be slapped by
    /// <paramref name="playerId"/> on tick <paramref name="now"/>.
    ///
    /// <para><see cref="CreatureFlags.CanBeSlapped"/>, the keeper's own
    /// creature, not in a hand, and not already down. <b>A prisoner is not
    /// slappable</b> even though it is pickable: the ownership exception
    /// <see cref="CanPickUp"/> makes is for lifting one out of a cell, and
    /// nothing the owner described extends it to slapping somebody else's
    /// creature. <b>No timing gate</b> —
    /// see <see cref="KnockBackWindowSeconds"/> for the one the original has
    /// and what it actually guards.</para>
    ///
    /// <para><b>Horny is excluded</b>, and the original excludes him first,
    /// before every other test: a slap on a creature whose record carries
    /// <see cref="CreatureFlags.IsHorny"/> runs a different routine entirely
    /// and never reaches the work bonus. That flag had been sitting in this
    /// project's enum as one of two unnamed bits until the slap handler named
    /// it (Q158).</para>
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
    /// <para>The original tests <c>now % </c>variable 19<c> == 0</c>, which is
    /// the last tick of each game second — the same cadence the anger ladder
    /// already runs on (ADR-0043), so this rides the same clock rather than a
    /// second one.</para>
    /// </summary>
    public static bool AngerDueThisTick(CreatureState creature, GameTick now)
    {
        if (!creature.InHand) return false;
        if (now.Value - creature.PickupTick <= AngerGraceTicks) return false;

        return (now.Value + 1) % SimulationTiming.TicksPerSecond == 0;
    }
}
