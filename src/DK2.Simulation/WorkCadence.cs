// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>When an imp at a tile applies its work, and when it may leave (ADR-0280). The work
/// state E follows a one-tick entry state; the first application lands at E+2, the second a
/// kind-dependent gap later, then one every tick. The imp looks at its task only when an
/// action ends, so it holds the work state after the tile is done.</summary>
/// <remarks>Measured outcomes in the Level 1 and Level 2 full-state captures
/// (mining-cadence.md §5, §6). The hold is read: state 14 (<c>0x004655D0</c>) returns at once
/// while <c>0x00496D00</c> reports an action running, and only then checks the tile and starts
/// the next action or sets 10. The action lengths are measured (a dirt dig leaves at E+6 after
/// its strike at E+2, a claim one or two ticks after its last application).
/// These constants remain a fallback with known phase-dependent mismatches;
/// see imp-dig-stop-clock.md for the native clock and rejected correction.</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/mining-cadence.md")]
public static class WorkCadence
{
    /// <summary>Arrival (the entry state's tick) to the first application: E+2.</summary>
    public const int FirstApplicationTicks = 3;

    /// <summary>The first application to the second. Ordinary dig art advances 1365 per tick:
    /// three advances reach 4095, so its first cycle takes four (mining-cadence.md §5).
    /// Conversion, attack and repair retain their unconfirmed family defaults.</summary>
    public static int SecondApplicationGap(TileWorkKind kind) => kind switch
    {
        TileWorkKind.MineGold => 4,
        TileWorkKind.Dig or TileWorkKind.DigOwnWall or TileWorkKind.DigEnemyWall => 4,
        TileWorkKind.Reinforce or TileWorkKind.RepairWall => 3,
        _ => 1,
    };

    /// <summary>The ticks until the next application, just after one lands at
    /// <paramref name="now"/> for a worker whose work state began at
    /// <paramref name="enteredWorkState"/>.</summary>
    public static int AfterApplication(TileWorkKind kind, long now, long enteredWorkState) =>
        now - enteredWorkState == FirstApplicationTicks - 1 ? SecondApplicationGap(kind) : 1;

    /// <summary>How long one work action lasts. Dig 6: a one-strike dig runs E to E+5, and a
    /// wall's 42 ticks are seven actions. Claim 2: runs of 6 and 8 after last applications at
    /// E+5 and E+6. UNKNOWN for the rest, which leave as soon as the task is gone.</summary>
    public static int ActionTicks(TileWorkKind kind) => kind switch
    {
        TileWorkKind.Dig or TileWorkKind.DigOwnWall or TileWorkKind.DigEnemyWall => 6,
        TileWorkKind.Claim => 2,
        _ => 1,
    };

    /// <summary>Whether an action ends at <paramref name="now"/>, so a finished task is noticed.</summary>
    public static bool ActionEnded(TileWorkKind kind, long now, long enteredWorkState) =>
        (now - enteredWorkState) % ActionTicks(kind) == 0;
}
