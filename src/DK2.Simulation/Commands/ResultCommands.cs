// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Commands;

/// <summary>Game action 134 (<c>0x00512950</c> → <c>0x004C0120</c>): carry on playing after the
/// level's result (campaign-win-sequence.md §6).</summary>
public sealed record ContinueAfterResultCommand(PlayerId PlayerId) : PlayerCommand(PlayerId);

/// <summary>Game action 75 (<c>0x00505D70</c>): explore the whole map for the issuing player,
/// as the result screen and a cheat do (game-actions.md §3.1).</summary>
public sealed record RevealMapCommand(PlayerId PlayerId) : PlayerCommand(PlayerId);
