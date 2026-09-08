// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Commands;

/// <summary>
/// Casts Create Imp: buy one imp at the dungeon heart for mana.
/// <para>CONFIRMED (owner, 2026-08-30) that the spell exists, costs 1500 for the
/// first and rises by 1500 each time, and that 1500 is the number on DK2's own
/// <c>Create Imp</c> record. HYPOTHESIS: the exact form of the rise — see
/// <see cref="ImpSupplyRules.CreateImpCost"/> and Q54.</para>
/// <para>Deviates: a bare command with no target. The original casts onto a tile
/// and the imp appears there; here it appears at the dungeon heart, because
/// where a cast lands is a UI question and a keeper with no heart has already
/// lost (Q40).</para>
/// </summary>
public sealed record CreateImpCommand(byte PlayerId) : PlayerCommand(PlayerId);
