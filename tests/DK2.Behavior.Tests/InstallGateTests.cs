// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// The guard on the install gate itself, and the reason it exists is a defect
/// that hid for months.
///
/// <para><b>What went wrong.</b> Every test that reads the shipped Dungeon
/// Keeper 2 data used to open with
/// <c>string? install = Dk2Install.Find(); if (install is null) return;</c>.
/// That makes the test <b>pass</b> when the install is absent — it does not
/// skip. On CI, which has no install, <b>266 of 1169 tests</b> reported green
/// while asserting nothing, and seventeen file headers claimed <i>"Skips when
/// no installation is configured (DK2_DIR), so CI stays green"</i>, which was
/// untrue in the one word that mattered.</para>
///
/// <para>The replacement is <c>[InstallFact]</c> / <c>[InstallTheory]</c>
/// (<c>DK2.TestSupport</c>), which set xUnit's <c>Skip</c> so the runner
/// reports <b>Skipped</b>, plus <c>DK2_REQUIRE_INSTALL=1</c> to turn a missing
/// install into a failure on a machine that is supposed to have one.</para>
///
/// <para><b>This test is what stops it coming back.</b> The only way to
/// re-introduce the old pattern is to look the install up inside a test, so the
/// invariant is simply: <b>no test source outside <c>DK2.TestSupport</c> may
/// mention <c>Dk2Install</c></b>. A test that needs the data declares that in
/// its attribute and then uses <c>Dk2InstallGate.Path</c>, which cannot return
/// null.</para>
///
/// <para>Deliberately a source scan rather than reflection: the thing being
/// prevented is a <i>way of writing a test</i>, and that is visible in the text
/// and invisible in the compiled assembly.</para>
/// </summary>
public class InstallGateTests
{
    /// <summary>
    /// Walks up from the test binary to the directory holding <c>DK2.slnx</c>.
    /// </summary>
    private static DirectoryInfo RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DK2.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir;
    }

    private static IEnumerable<string> TestSources() =>
        Directory.EnumerateFiles(
                Path.Combine(RepoRoot().FullName, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains("DK2.TestSupport"));

    /// <summary>The file's code, with doc comments removed.</summary>
    private static string CodeOnly(IEnumerable<string> lines) =>
        string.Join(
            Environment.NewLine,
            lines.Where(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));

    [Fact]
    public void NoTestLooksTheInstallUpForItself_BecauseThatIsHowTheSilentSkipHappened()
    {
        // Two things this got wrong on the way, both worth keeping:
        //
        // Doc comments are stripped first, because this very file names the old
        // pattern in prose in order to explain it — the first run failed on
        // itself, which was a pleasing kind of correct.
        //
        // And the needle carries its dot. "Dk2Install" is a PREFIX of
        // "Dk2InstallGate", so a bare substring search flagged all 39 files
        // that use the replacement correctly. A guard that fires on the right
        // answer is worse than no guard.
        // The needle is assembled rather than written, so that this file does not
        // contain it verbatim and flag itself. The alternative — excluding this
        // file by name — would be a hole somebody could hide a violation in.
        string needle = "Dk2Install" + ".";

        string[] offenders =
        [
            .. TestSources()
               .Where(f => CodeOnly(File.ReadAllLines(f))
                           .Contains(needle, StringComparison.Ordinal))
               .Select(Path.GetFileName)
               .OfType<string>()
               .Order()
        ];

        Assert.True(
            offenders.Length == 0,
            "These test files look the DK2 install up themselves, which is how a test ends up "
            + "PASSING when the install is absent instead of skipping. Use [InstallFact] or "
            + "[InstallTheory] from DK2.TestSupport and read Dk2InstallGate.Path in the body: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void TheGateItselfIsWiredUp_SoAGreenSuiteMeansSomething()
    {
        // Non-vacuous in both directions: the attribute must skip when there is
        // no install and must NOT skip when there is one, and the require
        // switch must beat the absence. Asserted through the public surface
        // rather than by constructing the attribute, because what matters is
        // the decision, not the plumbing.
        if (Dk2InstallGate.Present)
        {
            Assert.Null(Dk2InstallGate.SkipReason);
            Assert.NotNull(Dk2InstallGate.Path);
        }
        else if (Dk2InstallGate.Required)
        {
            // DK2_REQUIRE_INSTALL=1 with no install: run and fail, never skip.
            Assert.Null(Dk2InstallGate.SkipReason);
        }
        else
        {
            Assert.NotNull(Dk2InstallGate.SkipReason);
            Assert.Contains("DK2_DIR", Dk2InstallGate.SkipReason);
        }
    }
}
