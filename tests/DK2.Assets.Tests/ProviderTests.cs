// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;

namespace DK2.Assets.Tests;

/// <summary>
/// The provider chain, and the two properties the plan depends on: the
/// placeholder is total, and the order is the policy.
/// </summary>
public class ProviderTests
{
    /// <summary>A probe that says yes to a fixed set of paths. No files touched.</summary>
    private sealed class FakeFiles(params string[] present) : FileAssetProvider.IFileProbe
    {
        private readonly HashSet<string> _present = new(present, StringComparer.OrdinalIgnoreCase);
        public bool Exists(string path) => _present.Contains(path);
    }

    private static string PathFor(string root, AssetRef reference, string extension) =>
        Path.Combine(root, FileAssetProvider.RelativePathWithoutExtension(reference) + extension);

    // ---- The placeholder, which must never fail ----

    [Fact]
    public void ThePlaceholderAnswersForAnyValidKey()
    {
        foreach (AssetKind kind in Enum.GetValues<AssetKind>())
        {
            AssetRef reference = AssetRef.ForCreature("Imp", kind);
            Assert.True(PlaceholderProvider.Instance.TryResolve(reference, out ResolvedAsset asset));
            Assert.True(asset.IsPlaceholder);
            Assert.Null(asset.FilePath);
            Assert.Equal(reference, asset.Reference);
        }
    }

    [Fact]
    public void ThePlaceholderRefusesOnlyAnEmptyKey()
    {
        Assert.False(PlaceholderProvider.Instance.TryResolve(default, out _));
    }

    /// <summary>
    /// The property that makes a box view readable: the same key is the same
    /// colour every time. Trivial to satisfy and trivial to break by reaching
    /// for a random number, which is why it is pinned.
    /// </summary>
    [Fact]
    public void APlaceholdersLookIsDerivedFromItsKeyAndNeverVaries()
    {
        AssetRef rock = AssetRef.ForTerrain("Rock", AssetKind.Mesh);

        PlaceholderLook first = PlaceholderProvider.LookFor(rock);
        PlaceholderLook second = PlaceholderProvider.LookFor(rock);

        Assert.Equal(first.ToString(), second.ToString());
        Assert.NotEqual(
            first.ToString(),
            PlaceholderProvider.LookFor(AssetRef.ForTerrain("Gold Seam", AssetKind.Mesh)).ToString());
    }

    [Fact]
    public void APlaceholderColourIsNeverNearBlackOrNearWhite()
    {
        // Every terrain-shaped key in a wide sweep: the mid-range squeeze has to
        // hold for all of them, not for the one that was tried by hand.
        for (int i = 0; i < 500; i++)
        {
            PlaceholderLook look = PlaceholderProvider.LookFor(
                AssetRef.ForTerrain($"terrain_{i}", AssetKind.Mesh));

            Assert.InRange(look.Red, 64, 223);
            Assert.InRange(look.Green, 64, 223);
            Assert.InRange(look.Blue, 64, 223);
        }
    }

    [Theory]
    [InlineData(AssetKind.Mesh, AssetRef.CreatureCategory, PlaceholderShape.Figure)]
    [InlineData(AssetKind.Mesh, AssetRef.TerrainCategory, PlaceholderShape.Block)]
    [InlineData(AssetKind.Texture, AssetRef.TerrainCategory, PlaceholderShape.Slab)]
    [InlineData(AssetKind.Sound, AssetRef.TerrainCategory, PlaceholderShape.None)]
    [InlineData(AssetKind.Effect, AssetRef.TerrainCategory, PlaceholderShape.None)]
    public void APlaceholdersShapeFollowsWhatTheAssetIs(
        AssetKind kind, string category, PlaceholderShape expected)
    {
        AssetRef reference = AssetRef.Create(category, "thing", kind);
        Assert.Equal(expected, PlaceholderProvider.ShapeFor(reference));
    }

    /// <summary>
    /// A creature is drawn standing up and never taller than a tile, so it
    /// cannot hide the tile it is on — the one thing a box view must not do.
    /// </summary>
    [Fact]
    public void ACreatureFigureStandsUpAndFitsUnderTheCeiling()
    {
        for (int i = 0; i < 200; i++)
        {
            PlaceholderLook look = PlaceholderProvider.LookFor(
                AssetRef.ForCreature($"creature_{i}", AssetKind.Mesh));

            Assert.Equal(PlaceholderShape.Figure, look.Shape);
            Assert.InRange(look.HeightPermille, 500, 999);
        }
    }

    // ---- File providers: the only place a path exists ----

    [Fact]
    public void AKeyBecomesAPathByReplacingDotsWithSeparators()
    {
        AssetRef reference = AssetRef.ForCreature("Imp", AssetKind.Mesh);

        Assert.Equal(
            Path.Combine("creature", "imp", "mesh"),
            FileAssetProvider.RelativePathWithoutExtension(reference));
    }

    [Fact]
    public void AFileProviderAnswersOnlyForFilesThatExist()
    {
        AssetRef imp = AssetRef.ForCreature("Imp", AssetKind.Mesh);
        AssetRef dwarf = AssetRef.ForCreature("Dwarf", AssetKind.Mesh);
        string root = Path.Combine("build", "converted");

        var provider = FileAssetProvider.Converted(root, new FakeFiles(PathFor(root, imp, ".glb")));

        Assert.True(provider.TryResolve(imp, out ResolvedAsset found));
        Assert.Equal(AssetSource.ConvertedFile, found.Source);
        Assert.Equal(PathFor(root, imp, ".glb"), found.FilePath);
        Assert.False(found.IsPlaceholder);

        Assert.False(provider.TryResolve(dwarf, out _));
    }

    [Fact]
    public void ExtensionsAreTriedInPreferenceOrder()
    {
        AssetRef imp = AssetRef.ForCreature("Imp", AssetKind.Mesh);
        string root = "art";

        // Both present: the first in the preference list wins.
        var provider = FileAssetProvider.Converted(
            root,
            new FakeFiles(PathFor(root, imp, ".obj"), PathFor(root, imp, ".glb")));

        Assert.True(provider.TryResolve(imp, out ResolvedAsset asset));
        Assert.EndsWith(".glb", asset.FilePath);
    }

    [Fact]
    public void EveryAssetKindHasAtLeastOneOpenFormat()
    {
        foreach (AssetKind kind in Enum.GetValues<AssetKind>())
        {
            IReadOnlyList<string> extensions = FileAssetProvider.ExtensionsFor(kind);
            Assert.NotEmpty(extensions);
            Assert.All(extensions, e => Assert.StartsWith(".", e));
        }
    }

    [Fact]
    public void AFileProviderCannotClaimToBeThePlaceholder()
    {
        Assert.Throws<ArgumentException>(() =>
            new FileAssetProvider("root", AssetSource.Placeholder));
    }

    // ---- The chain: order is the policy ----

    [Fact]
    public void AReplacementPackWinsOverConvertedArtWhichWinsOverABox()
    {
        AssetRef imp = AssetRef.ForCreature("Imp", AssetKind.Mesh);

        var chain = new AssetProviderChain(
            FileAssetProvider.Replacement("pack", probe: new FakeFiles(PathFor("pack", imp, ".glb"))),
            FileAssetProvider.Converted("converted", new FakeFiles(PathFor("converted", imp, ".glb"))),
            PlaceholderProvider.Instance);

        Assert.Equal(AssetSource.ReplacementFile, chain.Resolve(imp).Source);
    }

    [Fact]
    public void ConvertedArtIsUsedWhenNoReplacementExists()
    {
        AssetRef imp = AssetRef.ForCreature("Imp", AssetKind.Mesh);

        var chain = new AssetProviderChain(
            FileAssetProvider.Replacement("pack", probe: new FakeFiles()),
            FileAssetProvider.Converted("converted", new FakeFiles(PathFor("converted", imp, ".glb"))),
            PlaceholderProvider.Instance);

        Assert.Equal(AssetSource.ConvertedFile, chain.Resolve(imp).Source);
    }

    /// <summary>
    /// The configuration the project must always support: no DK2 install, no art
    /// pack, and the game still runs.
    /// </summary>
    [Fact]
    public void TheStandardChainWithNoRootsAtAllStillAnswersEverything()
    {
        AssetProviderChain chain = AssetProviderChain.Standard();

        Assert.Single(chain.Providers);
        foreach (AssetKind kind in Enum.GetValues<AssetKind>())
            Assert.True(chain.Resolve(AssetRef.ForCreature("Imp", kind)).IsPlaceholder);
    }

    [Fact]
    public void AChainWithoutAPlaceholderCanMissAndSaysSoLoudly()
    {
        var chain = new AssetProviderChain(
            FileAssetProvider.Converted("converted", new FakeFiles()));

        AssetRef imp = AssetRef.ForCreature("Imp", AssetKind.Mesh);

        Assert.False(chain.TryResolve(imp, out _));
        Assert.Throws<InvalidOperationException>(() => chain.Resolve(imp));
    }

    [Fact]
    public void AnEmptyChainIsRefusedAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new AssetProviderChain());
    }

    /// <summary>
    /// The run report's raw material: a run that drew everything as boxes should
    /// be able to say so, rather than leaving a viewer to notice.
    /// </summary>
    [Fact]
    public void TheChainCountsWhatEachProviderAnswered()
    {
        AssetRef imp = AssetRef.ForCreature("Imp", AssetKind.Mesh);
        AssetRef dwarf = AssetRef.ForCreature("Dwarf", AssetKind.Mesh);

        var chain = new AssetProviderChain(
            FileAssetProvider.Converted("converted", new FakeFiles(PathFor("converted", imp, ".glb"))),
            PlaceholderProvider.Instance);

        chain.Resolve(imp);
        chain.Resolve(dwarf);
        chain.Resolve(dwarf);

        Assert.Equal(1, chain.ResolvedByProvider["converted"]);
        Assert.Equal(2, chain.ResolvedByProvider["placeholder"]);
        Assert.Contains("1 from converted", chain.Summarise());
        Assert.Contains("2 from placeholder", chain.Summarise());

        chain.ResetCounts();
        Assert.Equal("no assets were requested", chain.Summarise());
    }
}
