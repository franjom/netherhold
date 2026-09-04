// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;

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

    /// <summary>
    /// Every assembly Unity has to load targets <b>netstandard2.1</b>.
    ///
    /// <para><b>This test exists because the constraint it checks was claimed
    /// for four months and never held.</b> ADR-0002 said netstandard2.1 was
    /// "enforced by the compiler from day one"; in fact
    /// <c>Directory.Build.props</c> selected it behind a condition on a marker
    /// property that the project body set <i>after</i> the props file had
    /// already been evaluated, so the condition was always false and every
    /// assembly built as net10.0. <c>Polyfills.cs</c> sat behind
    /// <c>#if NETSTANDARD2_1</c> and was never once compiled.</para>
    ///
    /// <para>Nothing noticed, because nothing had yet tried to hand a DLL to
    /// Unity — and Unity cannot load a net10.0 assembly, so the whole
    /// presentation plan would have failed at the first attempt to use it
    /// (2026-09-04, ADR-0036). The lesson is the one this project keeps
    /// relearning: <b>a constraint expressed only in build configuration is a
    /// hope.</b> This asserts the outcome from the built artefact, where a
    /// misconfiguration cannot hide.</para>
    ///
    /// <para>Read from the assembly rather than from the csproj deliberately: a
    /// test that parsed the project files would be checking the same intent that
    /// was already wrong, rather than the result.</para>
    /// </summary>
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
}
