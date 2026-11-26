// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using System.Runtime.Versioning;

namespace DK2.Data.Tests;

/// <summary><c>DK2.Data</c> targets .NET 10 and stays independent of engine assemblies
/// (ADR-0002). The guard lives beside the assembly it guards, as
/// <c>DK2.Simulation.Tests.ArchitectureTests</c> does for the simulation.
/// <para>The KWD/WAD readers run inside the engine, so a convenience type from one would ship into every level load.</para></summary>
public class EngineNeutralityTests
{
    [Fact]
    public void TheDataLayer_ReferencesNoEngineAssembly()
    {
        string[] engines = ["Unity", "Godot", "UnityEngine", "UnityEditor"];
        List<string> offenders = Assembly.Load("DK2.Data").GetReferencedAssemblies()
            .Where(r => r.Name is not null && engines.Any(e =>
                r.Name.StartsWith(e, StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.Name!)
            .ToList();

        Assert.Empty(offenders);
    }

    /// <remarks>Read from the built artefact, never the csproj — the constraint
    /// was expressed only in build configuration for four months and never held.</remarks>
    [Fact]
    public void TheDataLayer_TargetsNet10()
    {
        string? target = Assembly.Load("DK2.Data")
            .GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

        Assert.Equal(".NETCoreApp,Version=v10.0", target);
    }
}
