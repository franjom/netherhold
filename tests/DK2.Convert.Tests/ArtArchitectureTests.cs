// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using DK2.Art.Kmf;

namespace DK2.Convert.Tests;

/// <summary>
/// <c>DK2.Art</c> is what the Unity adapter reads the player's install with (ADR-0408), so it is
/// held to the same two rules as every other Unity-loadable assembly (ADR-0002).
/// </summary>
public class ArtArchitectureTests
{
    /// <summary>Unity cannot load a net10.0 assembly; read from the built artefact, as
    /// <c>ArchitectureTests</c> does, because the csproj has lied about this before.</summary>
    [Fact]
    public void TheArtReadersTargetNetStandard21()
    {
        string? target = typeof(KmfReader).Assembly
            .GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()
            ?.FrameworkName;

        Assert.Equal(".NETStandard,Version=v2.1", target);
    }

    /// <summary>The readers decode into engine-neutral data; turning it into an engine's mesh is
    /// the adapter's job, and the writers' JSON and PNG stay in <c>DK2.Convert</c>.</summary>
    [Fact]
    public void TheArtReadersReferenceNeitherUnityNorTheWriters()
    {
        var offenders = typeof(KmfReader).Assembly.GetReferencedAssemblies()
            .Where(r => r.Name is not null &&
                        (r.Name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) ||
                         r.Name is "DK2.Convert" or "System.Text.Json"))
            .Select(r => r.Name)
            .ToList();

        Assert.Empty(offenders);
    }
}
