// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using System.Text.RegularExpressions;

namespace DK2.Simulation.Tests;

/// <summary>
/// Guards the project's central architectural rule: the simulation must never
/// depend on Unity. (docs/DECISIONS.md ADR-0002)
/// </summary>
public class ArchitectureTests
{
    [Theory]
    [InlineData("DK2.Core")]
    [InlineData("DK2.Data.Model")]
    [InlineData("DK2.Simulation")]
    // DK2.Assets is guarded the same way, in DK2.Assets.Tests — the guard lives
    // beside the assembly it guards rather than dragging an asset reference into
    // the simulation's own tests.
    public void SimulationAssemblies_DoNotReferenceUnity(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var offenders = assembly.GetReferencedAssemblies()
            .Where(r => r.Name is not null &&
                        r.Name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>Every assembly Unity has to load targets netstandard2.1
    /// (ADR-0002); Unity cannot load a net10.0 assembly.</summary>
    /// <remarks>Read from the built artefact, not from the csproj: the constraint
    /// was expressed only in build configuration for four months and never held —
    /// <c>Directory.Build.props</c> selected netstandard2.1 behind a condition on
    /// a marker property the project body set after the props file was evaluated,
    /// so every assembly built as net10.0 and <c>Polyfills.cs</c>, behind
    /// <c>#if NETSTANDARD2_1</c>, was never compiled. A test that parsed the
    /// project files would check the same intent that was already
    /// wrong.</remarks>
    [Theory]
    [InlineData("DK2.Core")]
    [InlineData("DK2.Data.Model")]
    [InlineData("DK2.Simulation")]
    [InlineData("DK2.Pathfinding")]
    public void UnityLoadableAssemblies_TargetNetStandard21(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var target = assembly
            .GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()
            ?.FrameworkName;

        Assert.Equal(".NETStandard,Version=v2.1", target);
    }

    [Fact]
    public void SimulationSystemsCannotBypassTheWorldAngerGate()
    {
        string root = RepositoryRoot();
        string source = string.Join('\n', Directory.GetFiles(
                Path.Combine(root, "src", "DK2.Simulation", "Systems"), "*.cs")
            .Select(File.ReadAllText));
        const string call = @"CreatureAngerRules\.(?:Apply|Slap|Held)\s*\(";
        const string gated = call + @"\s*(?:context\.World|world)\s*,";

        MatchCollection calls = Regex.Matches(source, call);
        Assert.NotEmpty(calls);
        Assert.Equal(calls.Count, Regex.Matches(source, gated).Count);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DK2.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
