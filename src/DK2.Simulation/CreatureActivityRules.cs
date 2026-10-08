// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>A creature's activity code, <c>0x00478A20</c>: the keeper's slap step
/// (<c>0x00502F00</c>) slaps only on <see cref="Slappable"/> (computer-keeper-management.md §8).</summary>
/// <remarks>Not modelled: the state object's <c>+0x3C</c> overrides (1 gives 26, 2 gives 25),
/// whose writers are unread.</remarks>
public static class CreatureActivityRules
{
    /// <summary>The code the slap step asks for.</summary>
    public const int Slappable = 5;

    /// <summary>A worker in combat; any other busy worker is <see cref="WorkerBusy"/>.</summary>
    public const int WorkerFighting = 4;
    public const int WorkerBusy = 6;

    /// <summary>The runtime table <c>0x006996C4</c> (stride <c>0x40</c>) by state category, as
    /// <c>0x004798F0</c> fills it at start-up.</summary>
    private static readonly int[] ByCategory =
    {
        2, 5, 5, 0x1D, 0x14, 0x14, 6, 6, 6, 0xC, 5, 0x16, 0x12, 0x15, 0x10, 0x11,
        0x15, 0x13, 6, 5, 6, 6, 4, 5, 6, 6, 5, 5, 5, 5, 5, 0x15,
    };

    /// <summary>A non-worker reads its category's entry, the walk's resumed state's through a walk.
    /// A worker is <see cref="Slappable"/> with no task (<c>+0x3D1</c>) or when its last task kind
    /// (state object <c>+0x98</c>) is 6, reinforce: the kind stays until state 10 zeroes it
    /// (<c>0x00464010</c>), so through a fight too (Q574); a repair is 16, not 6 (<c>0x0046AEA0</c>).
    /// Otherwise <see cref="WorkerFighting"/> in the combat category, else <see cref="WorkerBusy"/>.</summary>
    [Dk2Behavior(Confidence.Confirmed, "docs/research/computer-keeper-management.md")]
    public static int Code(CreatureDef definition, CreatureState creature)
    {
        CreatureStateCategory category = NeedRules.ResolvedCategory(creature);
        if (!definition.Flags.HasFlag(CreatureFlags.IsWorker))
            return (int)category < ByCategory.Length ? ByCategory[(int)category] : 0;

        if (creature.WorkerIdle || creature.LastTaskKind == ImpTaskKind.Reinforce) return Slappable;
        return category == CreatureStateCategory.Combat ? WorkerFighting : WorkerBusy;
    }
}
