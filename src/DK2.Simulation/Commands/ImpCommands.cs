// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>
/// Casts Create Imp: buy one imp at the dungeon heart for mana.
///
/// The third real player command, and <b>the first thing in this simulation that
/// spends mana</b>. Until now mana was income with no outgoings, which every
/// scenario run said out loud; a keeper can now turn it into workers.
///
/// <para>CONFIRMED that the spell exists, that it costs 1500 for the first and
/// rises by 1500 each time (owner, 2026-08-30), and that 1500 is the number on
/// DK2's own <c>Create Imp</c> record. What is HYPOTHESIS is the exact form of
/// the rise — see <see cref="ImpSupplyRules.CreateImpCost"/> and Q54.</para>
///
/// <para>Deliberately a bare command with no target. In the original a keeper
/// casts it onto a tile and the imp appears there; here it appears at the
/// dungeon heart, because where a cast lands is a UI question and because a
/// keeper with no heart has already lost (Q40). When keeper spells arrive at M7
/// this becomes one case of a general spell command rather than its own type.</para>
/// </summary>
public sealed record CreateImpCommand(byte PlayerId) : PlayerCommand(PlayerId);
