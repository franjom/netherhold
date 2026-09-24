// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>DK2's worker states (<c>imp-state-exits.md</c>): tile work (ADR-0181), gold (ADR-0182),
/// objects (ADR-0183) and bodies (ADR-0184). Each kind of tile work is an odd entry state, which
/// re-tests the task and the stance, and the even state after it, which repeats its creature
/// action until the tile is done and then sets <see cref="TaskSelection"/>.</summary>
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

    /// <summary>State 40: takes the corpse on its tile and walks it to a Graveyard in 6.</summary>
    public const int PickingUpCorpse = 40;

    /// <summary>State 41: buries the carried corpse and sets 10, or re-routes to another
    /// Graveyard.</summary>
    public const int CarryingCorpse = 41;

    /// <summary>State 42: takes the unconscious creature on its tile and chooses by ownership,
    /// the keeper's own to its Lair (45), an enemy to a Prison (43).</summary>
    public const int PickingUpCasualty = 42;

    /// <summary>State 43: carries an enemy to a Prison, then walks the inner leg to 44.</summary>
    public const int CarryingToPrison = 43;

    /// <summary>State 44: admission. Admitted walks away to 46; refused goes to 47.</summary>
    public const int AtThePrisonDoor = 44;

    /// <summary>State 45: delivers the keeper's own casualty to its Lair and sets 10.</summary>
    public const int CarryingToLair = 45;

    /// <summary>State 46: the admitted prisoner's carrier finishes walking away and sets 10.</summary>
    public const int LeavingThePrisoner = 46;

    /// <summary>State 47: chooses a put-down point in the Prison for a refused body and walks
    /// there, resuming in 48.</summary>
    public const int TakingTheRefusedBody = 47;

    /// <summary>State 48: puts the refused body down, releases the imp and sets 10.</summary>
    public const int PuttingDownTheRefusedBody = 48;

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
