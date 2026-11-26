// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using DK2.Art.Kmf;

namespace DK2.Convert.Tests;

/// <summary>Checks the target and engine boundary of the shared art readers (ADR-0002, ADR-0408).</summary>
public class ArtArchitectureTests
{
    /// <summary>Checks the target framework on the compiled art assembly.</summary>
    [Fact]
    public void TheArtReadersTargetNet10()
    {
        string? target = typeof(KmfReader).Assembly
            .GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()
            ?.FrameworkName;

        Assert.Equal(".NETCoreApp,Version=v10.0", target);
    }

    /// <summary>The readers decode into engine-neutral data; turning it into an engine's mesh is
    /// the adapter's job, and the writers' JSON and PNG stay in <c>DK2.Convert</c>.</summary>
    [Fact]
    public void TheArtReadersReferenceNeitherEnginesNorTheWriters()
    {
        var offenders = typeof(KmfReader).Assembly.GetReferencedAssemblies()
            .Where(r => r.Name is not null &&
                        (r.Name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) ||
                         r.Name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase) ||
                         r.Name is "DK2.Convert" or "System.Text.Json"))
            .Select(r => r.Name)
            .ToList();

        Assert.Empty(offenders);
    }
}
