// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Angry job 16: walk out through your own keeper's nearest Portal, or, failing
/// that, wait and change sides.
/// <para>Rebelling is not a roll. DK2's handler tries the Portal first and turning Good is
/// its fallback, which is why the wiki and the creature record both looked right about
/// Q116 — they describe different levels. The three <c>Rebel*Percentage</c> variables and
/// <c>RebelMaxFollowers</c> are written by their loader stubs and read nowhere in the
/// executable, so the shipped game has one outcome and no followers
/// (docs/research/rebellion.md §5).</para></summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/rebellion.md")]
public static class RebellionRules
{
    /// <summary>Ticks a creature with no reachable Portal waits before defecting — DK2
    /// sets its deadline to the game tick plus <b>30</b>, so 7.5 game seconds. Not a
    /// multiple of the tick rate, which is why the countdown runs every tick rather than
    /// with the rest of the anger ladder.</summary>
    public const int DefectDelayTicks = 30;

    /// <summary>Who a rebel defects to: the hero side, read from the global DK2 keeps the
    /// hero player id in (Q266, ADR-0142).</summary>
    public const byte DefectsTo = PlayerIds.Good;

    /// <summary>Whether job 16 may start at all. DK2's three guards, in its order: not a
    /// captive, not a Combat Pit fighter, not in the Hand.</summary>
    public static bool CanRebel(CreatureState creature) =>
        !PrisonRules.IsImprisoned(creature)
        && !creature.CombatPitFighter
        && !HandRules.IsHeld(creature);

    /// <summary>Is this creature waiting out the defection countdown?</summary>
    public static bool IsDefecting(CreatureState creature) =>
        creature.RebelCountdownTicks > 0;
}
