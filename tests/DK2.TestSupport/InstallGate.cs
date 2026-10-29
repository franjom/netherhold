// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using Xunit;

namespace DK2.TestSupport;

/// <summary>
/// Whether the reference Dungeon Keeper 2 installation is available to a test,
/// and the one place that decides what to do when it is not.
///
/// <para><b>Why this exists.</b> Every test that reads the shipped data used to
/// begin <c>string? install = Install(); if (install is null) return;</c>, which
/// makes the test <b>pass</b> when the install is absent. It does not skip. On
/// CI, which has no install, that silently covered <b>154 of 173</b> tests in
/// <c>DK2.Behavior.Tests</c> and <b>46</b> in <c>DK2.Data.Tests</c> — about a
/// sixth of the whole suite reporting green while asserting nothing. Seventeen
/// file headers claimed <i>"Skips when no installation is configured
/// (DK2_DIR)"</i>, and that was simply untrue.</para>
///
/// <para>Worse than the count: the real-data suites are this project's only
/// defence against a reading drifting away from the shipped files, so the one
/// part of the suite that most needed to be honest about not running was the
/// part quietly claiming to have run.</para>
/// </summary>
public static class Dk2InstallGate
{
    /// <summary>
    /// Set <c>DK2_REQUIRE_INSTALL=1</c> to turn a missing install from a skip
    /// into a <b>failure</b>.
    ///
    /// <para>For a machine that is supposed to have the data — a developer box,
    /// or a CI runner with the install mounted. Without it, a runner that
    /// quietly loses its data files goes green with two hundred tests skipped,
    /// which is exactly the failure this whole gate exists to stop, one level
    /// up.</para>
    /// </summary>
    public const string RequireVariable = "DK2_REQUIRE_INSTALL";

    /// <summary>Whether an installation was found. See <see cref="Dk2Install.Find"/>.</summary>
    public static bool Present => Dk2Install.Find() is not null;

    /// <summary>Whether a missing installation must fail rather than skip.</summary>
    public static bool Required =>
        Environment.GetEnvironmentVariable(RequireVariable) == "1";

    /// <summary>
    /// The installation root, never null.
    ///
    /// <para>Safe to call from the body of an <see cref="InstallFactAttribute"/>
    /// test without a null check — that is the point of the attribute. Throws
    /// with the <c>DK2_DIR</c> instructions if called from an ungated test.</para>
    /// </summary>
    public static string Path => Dk2Install.Require();

    /// <summary>
    /// The reason to skip, or null to run. <c>null</c> when the install is
    /// present, and <b>also</b> null when it is absent but
    /// <see cref="Required"/> — so the test runs and fails on the missing data
    /// rather than being quietly passed over.
    /// </summary>
    public static string? SkipReason =>
        Present || Required
            ? null
            : $"No Dungeon Keeper 2 installation. Set DK2_DIR, or set "
              + $"{RequireVariable}=1 to make its absence a failure.";
}

/// <summary>
/// <c>[Fact]</c> for a test that needs the reference installation: reports
/// <b>Skipped</b> when there is none, instead of passing.
///
/// <para>The body may use <see cref="Dk2InstallGate.Path"/> directly and must
/// <b>not</b> contain a null check — an early return is the bug this replaces,
/// and <c>InstallGateTests</c> fails the build if one reappears.</para>
/// </summary>
public sealed class InstallFactAttribute : FactAttribute
{
    public InstallFactAttribute(string? saveName = null)
    {
        Skip = Dk2InstallGate.SkipReason;
        if (saveName is not null && Dk2InstallGate.Present &&
            !File.Exists(System.IO.Path.Combine(DK2.Data.Save.Dk2SaveReader.SaveDirectory(Dk2InstallGate.Path), saveName)))
            Skip = $"Original capture {saveName} is not installed.";
    }
}

/// <summary>
/// <c>[Theory]</c> for a test that needs the reference installation. See
/// <see cref="InstallFactAttribute"/>.
/// </summary>
public sealed class InstallTheoryAttribute : TheoryAttribute
{
    public InstallTheoryAttribute() => Skip = Dk2InstallGate.SkipReason;
}

/// <summary>
/// <c>[Fact]</c> for a wall-clock budget: runs only when the install is present and
/// <see cref="SpeedBudgetGate.Variable"/> is 1. Opt-in because a timing assert fails on a loaded machine, and
/// the suite is often run beside other work on a shared CPU; run the budget on an idle one.
/// </summary>
public sealed class SpeedBudgetFactAttribute : FactAttribute
{
    public SpeedBudgetFactAttribute() => Skip = SpeedBudgetGate.SkipReason;
}

/// <summary><c>[Theory]</c> twin of <see cref="SpeedBudgetFactAttribute"/>.</summary>
public sealed class SpeedBudgetTheoryAttribute : TheoryAttribute
{
    public SpeedBudgetTheoryAttribute() => Skip = SpeedBudgetGate.SkipReason;
}

/// <summary>The opt-in on top of <see cref="Dk2InstallGate"/>: the install's skip reason first,
/// then <see cref="Variable"/>.</summary>
public static class SpeedBudgetGate
{
    public const string Variable = "DK2_SPEED_BUDGET";

    public static string? SkipReason =>
        Dk2InstallGate.SkipReason
        ?? (Environment.GetEnvironmentVariable(Variable) == "1"
            ? null
            : $"A wall-clock budget; set {Variable}=1 on an idle machine to run it.");
}
