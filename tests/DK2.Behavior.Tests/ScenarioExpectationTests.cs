// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Harness;
using DK2.TestSupport;
using Xunit.Abstractions;

namespace DK2.Behavior.Tests;

/// <summary>
/// The gate that turns a scenario from a description into a regression test: every scenario
/// carrying expectations meets them, one case per file, so a failure names its scenario and the
/// test log carries each one's duration and where it went.
///
/// <para>Install-gated on purpose: an expectation worth writing is a claim about DK2's own
/// numbers, and the synthetic database has four terrain types and one creature -- a goblin
/// scenario cannot even run against it.</para>
/// </summary>
public class ScenarioExpectationTests(ITestOutputHelper output)
{
    /// <summary>The files this theory runs: every scenario with expectations, less those
    /// <see cref="ScenarioCorpus.RunByTheirOwnTest"/> leaves to a test of their own.</summary>
    public static TheoryData<string> WithExpectations()
    {
        var data = new TheoryData<string>();
        foreach (string file in ScenarioCorpus.Files())
            if (!ScenarioCorpus.RunByTheirOwnTest.Contains(file) && ScenarioCorpus.Load(file).Expect.Count > 0)
                data.Add(file);
        return data;
    }

    [InstallTheory]
    [MemberData(nameof(WithExpectations))]
    public void EveryScenario_MeetsItsExpectations(string file)
    {
        ScenarioResult result = ScenarioCorpus.Run(ScenarioCorpus.Load(file), output);
        Assert.True(result.ExpectationFailures.Count == 0,
            $"{file}:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", result.ExpectationFailures));
    }
}
