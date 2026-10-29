// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using DK2.Assets;

namespace DK2.Assets.Tests;

/// <summary>
/// The architectural rules that make goal 4 more than a diagram.
/// </summary>
public class AssetArchitectureTests
{
    /// <summary>
    /// <c>DK2.Assets</c> is the <i>abstraction</i>, so it is Unity-loadable and
    /// must stay Unity-free — same rule as the simulation (ADR-0002), same
    /// reason: the engine-specific half of the asset layer belongs in the Unity
    /// project. Without this, the abstraction could quietly become a Unity
    /// dependency and take goal 4's replaceability with it.
    /// </summary>
    [Fact]
    public void TheAssetAbstractionDoesNotReferenceUnity()
    {
        Assembly assembly = typeof(AssetRef).Assembly;

        var offenders = assembly.GetReferencedAssemblies()
            .Where(r => r.Name is not null &&
                        r.Name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// <c>DK2.Assets</c> and <c>DK2.Presentation</c> target netstandard2.1, so
    /// Unity can load them. Same guard as
    /// <c>ArchitectureTests.UnityLoadableAssemblies_TargetNetStandard21</c> and
    /// for the same reason: that constraint was configuration-only for four
    /// months and never actually applied. See ADR-0036.
    /// </summary>
    [Theory]
    [InlineData(typeof(DK2.Data.Dk2Install))]
    [InlineData(typeof(AssetRef))]
    [InlineData(typeof(DK2.Presentation.WorldView))]
    public void TheRenderingAssembliesTargetNetStandard21(Type witness)
    {
        string? target = witness.Assembly
            .GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()
            ?.FrameworkName;

        Assert.Equal(".NETStandard,Version=v2.1", target);
    }

    /// <summary>
    /// The asset layer must not depend on the simulation either.
    ///
    /// <para>The dependency runs the other way in every sane design — a renderer
    /// knows about both, and neither knows about the renderer — but the
    /// temptation is real: <see cref="AssetCatalogue.CreatureAnimations"/> would
    /// be shorter as a projection of <c>CreatureActivity</c>. It is a checked
    /// copy instead, precisely so this arrow stays absent.</para>
    /// </summary>
    [Fact]
    public void TheAssetAbstractionDoesNotReferenceTheSimulation()
    {
        Assembly assembly = typeof(AssetRef).Assembly;

        var offenders = assembly.GetReferencedAssemblies()
            .Where(r => r.Name is "DK2.Simulation" or "DK2.Pathfinding" or "DK2.Data")
            .Select(r => r.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>
    /// <b>The rule the plan states as "no filename literals outside
    /// DK2.Assets" (§4.8), checked from the other side.</b> Nothing but a
    /// provider may produce a <see cref="ResolvedAsset.FilePath"/>, so there is
    /// no public way to build one — a caller cannot mint a path and pass it off
    /// as resolved.
    /// </summary>
    [Fact]
    public void OnlyAProviderCanProduceAResolvedAssetWithAPath()
    {
        // The struct's constructor is the only way in, and it is internal to the
        // assembly's providers by convention — what is checkable is that the
        // property is get-only, so nothing can attach a path after the fact.
        PropertyInfo? filePath = typeof(ResolvedAsset).GetProperty(nameof(ResolvedAsset.FilePath));

        Assert.NotNull(filePath);
        Assert.Null(filePath!.SetMethod);
    }

    /// <summary>
    /// Every asset kind must be resolvable end to end by the standard chain with
    /// nothing installed, or "the game runs without assets" is only true for the
    /// kinds somebody remembered.
    /// </summary>
    [Fact]
    public void TheStandardChainCoversEveryAssetKind()
    {
        AssetProviderChain chain = AssetProviderChain.Standard();

        foreach (AssetKind kind in Enum.GetValues<AssetKind>())
        {
            AssetRef reference = AssetRef.ForTerrain("Rock", kind);
            Assert.True(chain.TryResolve(reference, out ResolvedAsset asset), $"no answer for {kind}.");
            Assert.True(asset.IsPlaceholder);
        }
    }
}
