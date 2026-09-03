using DK2.Assets;

namespace DK2.Assets.Tests;

/// <summary>
/// The key type. Most of these are about what an <see cref="AssetRef"/> refuses,
/// because a key that silently normalises to something plausible is the one bug
/// this design would otherwise invite: the placeholder answers, the game runs,
/// and nobody finds out for months.
/// </summary>
public class AssetRefTests
{
    [Theory]
    [InlineData("Rock", "terrain.rock.mesh")]
    [InlineData("Gold Seam", "terrain.gold_seam.mesh")]
    [InlineData("Impenetrable Rock", "terrain.impenetrable_rock.mesh")]
    // These three are real shipped terrain names, and they are why the separator
    // set is measured rather than assumed — the first version of the normaliser
    // allowed only space, dash and apostrophe and the terrain table refuted it.
    [InlineData("Hero Gate [ Final ]", "terrain.hero_gate_final.mesh")]
    [InlineData("Hero Gate [ Tile ] [<not used>]", "terrain.hero_gate_tile_not_used.mesh")]
    [InlineData("Mercenary Gate [<not used>]", "terrain.mercenary_gate_not_used.mesh")]
    public void ARecordNameBecomesALowercaseUnderscoredKey(string name, string expected)
    {
        Assert.Equal(expected, AssetRef.ForTerrain(name, AssetKind.Mesh).Key);
    }

    [Theory]
    [InlineData("Hero Gate [ 2x2 ]", "hero_gate_2x2")]
    [InlineData("  Rock  ", "rock")]
    [InlineData("A -- B", "a_b")]
    public void RunsOfPunctuationCollapseAndTheEdgesAreTrimmed(string name, string expected)
    {
        Assert.Equal(expected, AssetRef.NormaliseSubject(name));
    }

    [Fact]
    public void TheKindIsPartOfTheKeyAndPartOfTheIdentity()
    {
        AssetRef mesh = AssetRef.ForCreature("Imp", AssetKind.Mesh);
        AssetRef texture = AssetRef.ForCreature("Imp", AssetKind.Texture);

        Assert.Equal("creature.imp.mesh", mesh.Key);
        Assert.Equal("creature.imp.texture", texture.Key);
        Assert.NotEqual(mesh, texture);
    }

    [Fact]
    public void AVariantAddsAFourthSegment()
    {
        Assert.Equal(
            "creature.imp.animation.walk",
            AssetRef.ForCreature("Imp", AssetKind.Animation, "walk").Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("__")]
    public void ANameThatNormalisesToNothingIsRefused(string name)
    {
        Assert.Throws<ArgumentException>(() => AssetRef.ForTerrain(name, AssetKind.Mesh));
    }

    /// <summary>
    /// The important refusal. DK2's record names are ordinary words, so a name
    /// carrying a separator, a dot or a control character did not come from a
    /// record — it came from a truncated string, a wrong offset, or a path. Any
    /// of those would produce a believable key for the wrong thing.
    /// </summary>
    [Theory]
    [InlineData("../secrets")]
    [InlineData("terrain.rock")]
    [InlineData("C:\\Windows")]
    [InlineData("rock/../..")]
    [InlineData("rock\u0000")]
    public void ANameThatCouldBeAPathIsRefusedRatherThanSanitised(string name)
    {
        Assert.Throws<ArgumentException>(() => AssetRef.ForTerrain(name, AssetKind.Mesh));
    }

    [Fact]
    public void AnUnknownCategoryIsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            AssetRef.Create("spaceship", "imp", AssetKind.Mesh));
    }

    /// <summary>
    /// Every kind must have a role segment, or a key could not be built for it.
    /// Written as a loop over the enum so adding a kind and forgetting its role
    /// fails here rather than at whatever call site reaches it first.
    /// </summary>
    [Fact]
    public void EveryAssetKindHasARoleSegment()
    {
        foreach (AssetKind kind in Enum.GetValues<AssetKind>())
        {
            string role = AssetRef.RoleOf(kind);
            Assert.False(string.IsNullOrWhiteSpace(role));
            Assert.Equal(role, role.ToLowerInvariant());
        }
    }

    /// <summary>
    /// The hash has to be stable across machines and runs, because placeholder
    /// colours come from it — an unstable one would mean rock is brown here and
    /// green on the next machine, and a screenshot would stop being evidence.
    /// Pinned to a literal for the same reason the simulation's state hash is.
    /// </summary>
    [Fact]
    public void TheStableHashIsPinned()
    {
        Assert.Equal(0x4ADEAE1B971C7285UL, AssetRef.ForTerrain("Rock", AssetKind.Mesh).StableHash());
    }

    [Fact]
    public void TwoKeysBuiltTheSameWayAreEqualAndHashAlike()
    {
        AssetRef a = AssetRef.ForCreature("Imp", AssetKind.Mesh);
        AssetRef b = AssetRef.ForCreature("imp", AssetKind.Mesh);

        Assert.Equal(a, b);
        Assert.Equal(a.StableHash(), b.StableHash());
        Assert.True(a == b);
    }

    [Fact]
    public void ADefaultRefIsEmptyAndHasNoKey()
    {
        AssetRef empty = default;

        Assert.True(empty.IsEmpty);
        Assert.Throws<InvalidOperationException>(() => empty.Key);
    }
}
