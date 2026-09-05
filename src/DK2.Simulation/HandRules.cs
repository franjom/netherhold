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

    /// <summary>The knock-back window in our ticks. Nothing reads it yet.</summary>
    public const int KnockBackWindowTicks =
        KnockBackWindowSeconds * SimulationTiming.TicksPerSecond;

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
    /// in a hand; and a creature the keeper does not own is refused. DK2 also
    /// refuses two specific creature states and one state flag, none of which
    /// this project has yet — see docs/research/hand-of-evil.md §3.</para>
    ///
    /// <para><b>A dying creature is refused here and that is ours, not
    /// DK2's.</b> The original lets the hand take a collapsed creature, which
    /// is half of what the rescue window is for; ours cannot, because what it
    /// would then do with it — carry it to a bed or a prison — does not exist.
    /// Q161, and it goes away with carrying.</para>
    /// </summary>
    public static bool CanPickUp(
        WorldState world, GameDatabase database, CreatureState creature, byte playerId)
    {
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if (!definition.Flags.HasFlag(CreatureFlags.CanBePickedUp)) return false;
        if (creature.InHand) return false;
        if (creature.OwnerPlayerId != playerId) return false;
        if (CreatureDeathRules.IsDown(creature)) return false;

        return world.Player(playerId).Hand.Count < Capacity(database);
    }

    /// <summary>
    /// Whether <paramref name="creature"/> may be slapped by
    /// <paramref name="playerId"/> on tick <paramref name="now"/>.
    ///
    /// <para><see cref="CreatureFlags.CanBeSlapped"/>, the keeper's own
    /// creature, not in a hand, and not already down. <b>No timing gate</b> —
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
