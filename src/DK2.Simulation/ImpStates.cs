// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>DK2's worker states for tile work (<c>imp-state-exits.md</c>, ADR-0181). Each kind of
/// work is an odd entry state, which re-tests the task and the stance, and the even state after
/// it, which repeats its creature action until the tile is done and then sets
/// <see cref="TaskSelection"/>.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-state-exits.md")]
public static class ImpStates
{
    /// <summary>State 10, the worker's task selection (<c>0x00463DF0</c>). Its task handlers
    /// start every walk to work, and every even work state returns here.</summary>
    public const int TaskSelection = 10;

    /// <summary>The (entry, work) pair for a kind of tile work. The pairing is by the creature
    /// action each even state starts, read against the action table's work handlers
    /// (<c>balance-variables-in-the-binary.md</c>): action 1 reads all three dig rates, 2
    /// mining, 4 claiming, 5 attacking a floor, 7 reinforcing, 8 repairing and 10 converting a
    /// room.</summary>
    public static (int Entry, int Work) For(TileWorkKind kind) => kind switch
    {
        TileWorkKind.Dig or TileWorkKind.DigOwnWall or TileWorkKind.DigEnemyWall => (13, 14),
        TileWorkKind.MineGold => (25, 26),
        TileWorkKind.Claim => (15, 16),
        TileWorkKind.ConvertRoom => (17, 18),
        TileWorkKind.AttackTile => (19, 20),
        TileWorkKind.Reinforce => (21, 22),
        TileWorkKind.RepairWall => (23, 24),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No worker state pair."),
    };
}
