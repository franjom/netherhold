// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The one place where this project may use a number the shipped data does
/// not contain: the sanctioned exception to "a balance constant hardcoded in
/// simulation code is a bug" (ADR-0023). <b>It holds no entries</b>: its only one,
/// <c>DigRockHealth</c>, was retired by ADR-0280 (Q20).</summary>
/// <remarks>All five admission conditions must hold: (1) the behaviour is
/// <b>measured in the original</b>, not inferred, remembered or taken from a
/// community source; (2) the shipped value is shown <b>incapable</b> of producing
/// it, with the arithmetic written down; (3) the balance table has been searched
/// for an <b>alternative variable</b> and came back empty, a misread field being
/// far likelier than bad data; (4) the correction fires <b>only on the exact
/// shipped value</b> measured against, leaving a mod or a different data set
/// alone; (5) the raw value stays readable from <see cref="GameDatabase"/>.</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/tile-work.md")]
public static class ObservedCorrections
{
    /// <summary>
    /// The per-second health delta for a tile-work variable, with any correction
    /// above applied. Everything that needs a work rate goes through here rather
    /// than reading <see cref="GameDatabase"/> directly, so there is exactly one
    /// place where a corrected number can enter the simulation.
    /// </summary>
    public static int TileWorkPerSecond(GameDatabase database, VariableTypeId variable)
    {
        // Through the indexer, so a variable this database does not carry still
        // throws with its own name: correcting a value is not a licence to invent
        // a missing one.
        return database[variable];
    }

    /// <summary>
    /// True if <paramref name="variable"/> would be corrected for this database;
    /// the scenario harness names every corrected value in its caveats.
    /// <para><b>Absent is not corrected</b> — a database that does not carry the
    /// variable answers false rather than throwing, so "did this run use a
    /// corrected number" is safe to ask on any database, including the
    /// deliberately minimal ones tests build.</para>
    /// </summary>
    public static bool IsCorrected(GameDatabase database, VariableTypeId variable) => false;
}
