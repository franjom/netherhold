// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using System.Runtime.CompilerServices;
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
    [InlineData("DK2.Pathfinding")]
    // DK2.Assets, DK2.Data, DK2.Presentation and DK2.AI are guarded the same way
    // in their own test projects — the guard lives beside the assembly it guards
    // rather than dragging references into the simulation's own tests. Between
    // them the five files cover every assembly Unity loads, on both rules.
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

    [Fact]
    public void NothingOutsideTheStateMachineAssignsADk2StateId()
    {
        // ADR-0135 step one. The point of the gate is that thirty activities retire onto
        // state ids one group at a time, over months, and a single direct assignment
        // anywhere is a transition rule this project invented rather than read. There is
        // nothing to count yet - nothing writes the field - so this guards the future,
        // which is the only moment it can be added without also rewriting callers.
        string root = RepositoryRoot();
        string[] files = Directory
            .GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();
        Assert.NotEmpty(files);

        const string write = @"[.\s]Dk2StateId\s*(?:=[^=]|\+\+|--)";
        var offenders = files
            .Where(f => Regex.IsMatch(File.ReadAllText(f), write))
            .Select(f => Path.GetFileName(f))
            // The gate itself; the field's own declaration; and the snapshot reader and the
            // generated world copy (ADR-0693), which restore a state a gate already allowed
            // rather than taking a transition.
            .Where(f => f is not "CreatureStateMachine.cs"
                         and not "CreatureState.cs"
                         and not "WorldSnapshot.cs"
                         and not "StateCopy.g.cs")
            .ToArray();

        Assert.Empty(offenders);

        // And the guard is not vacuous: the four files it exempts do contain a write, so
        // a pattern that stopped matching would fail here rather than pass everything.
        Assert.Equal(4, files.Count(f => Regex.IsMatch(File.ReadAllText(f), write)));
    }

    /// <summary>Static fields allowed to hold shared mutable state, with the reason each is safe.</summary>
    private static readonly Dictionary<string, string> SharedStaticAllowList = new(StringComparer.Ordinal)
    {
        ["DK2.Simulation.StateEntryLog.FirstCaller"] = "diagnostics only, outside the state hash; a ConcurrentDictionary",
        ["DK2.Simulation.SimulationMarker.ById"] = "profiling registry, outside the state hash; written under its lock",
        ["DK2.Simulation.SimulationMarker.ByName"] = "profiling registry, outside the state hash; written under its lock",
        ["DK2.Simulation.ActivityRules.JobStates"] = "a lookup filled by its initialiser and only read",
        ["DK2.Simulation.InputLog.TagKinds"] = "the default tag filter, filled by its initialiser and only read",
    };

    [Fact]
    public void NoStaticFieldSharesMutableStateBetweenSimulations()
    {
        // Two worlds tick on different threads (the Unity worker, parallel tests), and a static
        // they both write races: FightRules.RouteScratch did (cbee8a3d). A mutable static, or a
        // static readonly mutable collection, must be [ThreadStatic] or on the allow-list.
        var offenders = typeof(WorldState).Assembly.GetTypes()
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(f => !f.IsLiteral
                     && !f.IsDefined(typeof(ThreadStaticAttribute), false)
                     && !f.IsDefined(typeof(CompilerGeneratedAttribute), false)
                     && !f.Name.StartsWith('<')
                     && (!f.IsInitOnly || IsMutableCollection(f.FieldType)))
            .Select(f => $"{f.DeclaringType!.FullName}.{f.Name}")
            .Where(name => !SharedStaticAllowList.ContainsKey(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Empty(offenders);
        Assert.All(SharedStaticAllowList.Keys, name => Assert.NotNull(
            typeof(WorldState).Assembly.GetType(name[..name.LastIndexOf('.')])?
                .GetField(name[(name.LastIndexOf('.') + 1)..], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)));
    }

    /// <summary>A System.Collections type that can be written through: not an array (lookup tables
    /// are readonly arrays by convention) and not a read-only, immutable or frozen collection.</summary>
    private static bool IsMutableCollection(Type type)
        => !type.IsArray
           && type.Namespace is { } ns
           && ns.StartsWith("System.Collections", StringComparison.Ordinal)
           && !ns.Contains("Immutable", StringComparison.Ordinal)
           && !ns.Contains("Frozen", StringComparison.Ordinal)
           && !type.Name.StartsWith("ReadOnly", StringComparison.Ordinal)
           && !type.Name.StartsWith("IReadOnly", StringComparison.Ordinal)
           && !type.Name.StartsWith("IEnumerable", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DK2.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
