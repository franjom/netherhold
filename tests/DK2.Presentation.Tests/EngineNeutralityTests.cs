// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using System.Runtime.Versioning;

namespace DK2.Presentation.Tests;

/// <summary><c>DK2.Presentation</c> targets .NET 10 and stays independent of engine assemblies
/// (ADR-0002). The guard lives beside the assembly it guards, as
/// <c>DK2.Simulation.Tests.ArchitectureTests</c> does for the simulation.
/// <para>This is the translation layer between the simulation and whatever draws it, so it is where an engine type leaks in first.</para></summary>
public class EngineNeutralityTests
{
    [Fact]
    public void ThePresentationLayer_ReferencesNoEngineAssembly()
    {
        string[] engines = ["Unity", "Godot", "UnityEngine", "UnityEditor"];
        List<string> offenders = Assembly.Load("DK2.Presentation").GetReferencedAssemblies()
            .Where(r => r.Name is not null && engines.Any(e =>
                r.Name.StartsWith(e, StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.Name!)
            .ToList();

        Assert.Empty(offenders);
    }

    /// <remarks>Read from the built artefact, never the csproj — the constraint
    /// was expressed only in build configuration for four months and never held.</remarks>
    [Fact]
    public void ThePresentationLayer_TargetsNet10()
    {
        string? target = Assembly.Load("DK2.Presentation")
            .GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

        Assert.Equal(".NETCoreApp,Version=v10.0", target);
    }
}
