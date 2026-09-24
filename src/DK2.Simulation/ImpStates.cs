// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

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

    /// <summary>State 27: takes the floor gold on its tile, then walks to a store in 6 resuming
    /// in <see cref="AtTheStore"/>, or sets 10 with nothing to carry or nowhere to take it.</summary>
    public const int PickingUpGold = 27;

    /// <summary>State 28: waits for a free standing point at the store tile, then moves to
    /// <see cref="PuttingDownGold"/>. The wait is not modelled.</summary>
    public const int AtTheStore = 28;

    /// <summary>State 29: deposits what fits and sets 10; a tile that no longer stores this
    /// keeper's gold returns 0, which is idle. 10 then walks on with any remainder.</summary>
    public const int PuttingDownGold = 29;

    /// <summary>State 30: takes the loose object on its tile and walks it to its room through
    /// the destination helper <c>0x0046E480</c>, or sets 10.</summary>
    public const int PickingUpObject = 30;

    /// <summary>The state a carried object walks into, by the flag the destination helper tests
    /// first: 31 a spell book, 33 a Special, 32 a crate (<c>travel-state-call-sites.md</c> §3).
    /// Each stores the object and sets 10, or re-routes through the helper.</summary>
    public static int CarryingStateFor(ObjectFlags flags) =>
        flags.HasFlag(ObjectFlags.TypeSpellBook) ? 31
        : flags.HasFlag(ObjectFlags.TypeSpecial) ? 33
        : flags.HasFlag(ObjectFlags.TypeCrate) ? 32
        : throw new ArgumentOutOfRangeException(nameof(flags), flags, "Not a carried object.");

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
