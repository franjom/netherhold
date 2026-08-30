using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The one place where this project uses a number the shipped data does not
/// contain, and the reasoning for each.
///
/// <para><b>This type is a deliberate, narrow exception to a standing rule</b>
/// ("a balance constant hardcoded in simulation code is a bug"), and it exists
/// because two of this project's own rules point in opposite directions for
/// exactly one variable. Fidelity priority 1 is <i>observed gameplay
/// behaviour</i>, and the original install is the behavioural oracle; fidelity
/// priority 5 says the internals need not match, and the data is read rather
/// than transcribed. When a shipped value demonstrably does not produce the
/// shipped behaviour, one of those has to give.</para>
///
/// <para><b>The rules for adding to this list</b>, so it does not become a
/// dumping ground for anything inconvenient:</para>
/// <list type="number">
/// <item>the behaviour must be <b>measured in the original</b>, not inferred,
/// not remembered and not taken from a community source;</item>
/// <item>the shipped value must be shown to be <b>incapable</b> of producing it,
/// with the arithmetic written down;</item>
/// <item>the balance table must have been <b>searched for an alternative
/// variable</b>, and the search must have come back empty — a misread field is
/// far likelier than bad data, and is a fix rather than an override;</item>
/// <item>the correction must fire <b>only on the exact shipped value</b> it was
/// measured against, so a mod, a patch or a different data set is left
/// alone;</item>
/// <item>the raw value stays readable from <see cref="GameDatabase"/>, so
/// nothing is hidden and a test can assert both.</item>
/// </list>
///
/// <para>The owner approved this route on 2026-08-30 for the single entry below.
/// See ADR-0023, Q20 and docs/research/tile-work.md.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/tile-work.md")]
public static class ObservedCorrections
{
    /// <summary>What <c>DigRockHealth</c> reads in the shipped GlobalVariables.kwd.</summary>
    public const int ShippedDigRockHealth = -5000;

    /// <summary>
    /// What it has to be for the original's behaviour to happen: <b>-500</b>.
    ///
    /// <para><b>The measurement.</b> The owner dug out plain unreinforced wall
    /// (terrain 2, "Rock") in the original and counted <b>two swings</b>, being
    /// careful to note that a third animation plays over the block already
    /// crumbling — so two applications do the damage, not three.</para>
    ///
    /// <para><b>Why the shipped value cannot do that.</b> Rock has 165 health.
    /// Two applications bound the per-application damage from both sides: it
    /// must be under 165, or one swing would finish the tile, and at least 83,
    /// or two would not. At
    /// <see cref="SimulationTiming.WorkApplicationsPerSecond"/> (five, CONFIRMED
    /// by Q47) the per-second figure is therefore in <b>[415, 820]</b>. The
    /// shipped -5000 is six to twelve times too big, and would fell a wall in a
    /// single swing.</para>
    ///
    /// <para><b>Why -500.</b> It is the only round number in that window, and it
    /// is exactly a tenth of the shipped value — an extra zero is the most
    /// ordinary way for a number to be wrong. It gives 100 a swing: 165 → 65 →
    /// destroyed, two applications, with no coincidence required. And every
    /// other tile delta in the table is in the tens or low hundreds (825, 125,
    /// -300, -130, -8, 5, 50, 22), so -5000 is the sole four-digit one and -500
    /// puts it back in the family it belongs to.</para>
    ///
    /// <para><b>Why it is not a misread field.</b> The balance table was scanned
    /// for any variable holding a value in [415, 820]: the six hits are two
    /// payday intervals, a boulder fear amount, a tremor damage figure, a music
    /// threshold and the mana gain cap. There is no alternative candidate, and
    /// the surrounding model is confirmed everywhere else it has been checked —
    /// the same five-a-second reading gives a gold seam 273 gold a swing and
    /// claiming four swings, both measured in the original.</para>
    ///
    /// <para><b>What is still unknown</b>: whether the real engine reads a
    /// different field, applies a divisor nobody has found, or ships a typo.
    /// This corrects the behaviour, not the explanation.</para>
    /// </summary>
    public const int CorrectedDigRockHealth = -500;

    /// <summary>
    /// The per-second health delta for a tile-work variable, with any correction
    /// above applied.
    ///
    /// Everything that needs a work rate goes through here rather than reading
    /// <see cref="GameDatabase"/> directly, so there is exactly one place where
    /// a corrected number can enter the simulation.
    /// </summary>
    public static int TileWorkPerSecond(GameDatabase database, VariableTypeId variable)
    {
        // Reading through the database's indexer, so a variable this database
        // does not carry still throws with its own name in the message. That is
        // the convention everywhere else, and correcting a value is not a
        // licence to invent a missing one.
        int shipped = database[variable];

        // Guarded on the exact shipped value, per rule 4 above: a data set that
        // already carries a workable number is not second-guessed.
        if (variable == VariableTypeId.DigRockHealth && shipped == ShippedDigRockHealth)
            return CorrectedDigRockHealth;

        return shipped;
    }

    /// <summary>
    /// True if <paramref name="variable"/> would be corrected for this database.
    ///
    /// <para>For reporting: the scenario harness names every corrected value in
    /// its caveats, because a run that used a number the game's own files do not
    /// contain should say which. <b>Absent is not corrected</b> — a database
    /// that does not carry the variable at all answers false rather than
    /// throwing, because asking "did this run use a corrected number" must be
    /// safe on any database, including the deliberately minimal ones tests
    /// build.</para>
    /// </summary>
    public static bool IsCorrected(GameDatabase database, VariableTypeId variable) =>
        database.Variables.TryGet(variable, out int shipped)
        && variable == VariableTypeId.DigRockHealth
        && shipped == ShippedDigRockHealth;
}
