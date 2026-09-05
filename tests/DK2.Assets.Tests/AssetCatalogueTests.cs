// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Assets.Tests;

/// <summary>
/// The catalogue, and the promise it exists to make testable: <b>the game runs
/// with no assets at all</b> (plan §7.2). That promise is only meaningful if
/// something checks the placeholder answers for every key the real game
/// database implies — which is this file.
/// </summary>
public class AssetCatalogueTests
{
    private static GameDatabase? RealDatabase()
    {
        string? dir = Dk2Install.Find();
        return dir is null ? null : GameDatabaseLoader.Load(dir);
    }

    /// <summary>
    /// The one that matters. Every key the shipped database implies resolves,
    /// with no install-derived art and no replacement pack — so a contributor
    /// who owns no copy of DK2 can still run the game as boxes, which is the
    /// property that keeps goal 4 honest rather than aspirational.
    /// </summary>
    [Fact]
    public void EveryKeyTheRealDatabaseImpliesResolvesWithNoAssetsInstalled()
    {
        GameDatabase? database = RealDatabase();
        if (database is null) return; // skipped: no DK2 install on this machine (e.g. CI)

        AssetProviderChain chain = AssetProviderChain.Standard();
        var keys = AssetCatalogue.ForDatabase(database).ToList();

        // 41 terrain + 26 rooms x 2 + 46 creatures x 5, plus one effect per
        // damaging terrain. A floor under it so the loop cannot be vacuous.
        Assert.True(keys.Count > 300, $"the catalogue produced only {keys.Count} keys.");

        foreach (AssetRef key in keys)
        {
            Assert.True(chain.TryResolve(key, out ResolvedAsset asset), $"nothing answered for {key}.");
            Assert.True(asset.IsPlaceholder);
        }

        Assert.Equal(keys.Count, chain.ResolvedByProvider["placeholder"]);
    }

    /// <summary>
    /// Every name in the shipped database normalises, with nothing refused.
    ///
    /// <para><b>This test found a real bug in the normaliser</b> and is the
    /// reason the separator set is measured rather than assumed. The first
    /// version allowed only space, dash and apostrophe; the shipped terrain
    /// table carries <c>Hero Gate [ Final ]</c> and
    /// <c>Mercenary Gate [&lt;not used&gt;]</c>, so it threw on six records. The
    /// lesson is the project's usual one from a new angle: <b>a character set is
    /// a claim about the data, and the data was right there.</b></para>
    /// </summary>
    [Fact]
    public void EveryNameInTheShippedDatabaseNormalises()
    {
        GameDatabase? database = RealDatabase();
        if (database is null) return;

        var names = database.Terrain.Values.Select(t => t.Name)
            .Concat(database.Rooms.Values.Select(r => r.Name))
            .Concat(database.Creatures.Values.Select(c => c.Name))
            .ToList();

        Assert.True(names.Count >= 100, $"only {names.Count} names to check.");

        foreach (string name in names)
        {
            string segment = AssetRef.NormaliseSubject(name);

            // A key segment must be exactly what the file provider can turn into
            // a path safely: lowercase, digits and underscores, no leading or
            // trailing underscore, no doubled one.
            Assert.Matches("^[a-z0-9]+(_[a-z0-9]+)*$", segment);
        }
    }

    /// <summary>
    /// Keys are unique. A duplicate would make the converter produce the same
    /// file twice and, worse, would mean two different things share one
    /// appearance — which in a box view is indistinguishable from a bug in the
    /// simulation.
    /// </summary>
    [Fact]
    public void TheCatalogueHasNoDuplicateKeys()
    {
        GameDatabase? database = RealDatabase();
        if (database is null) return;

        var keys = AssetCatalogue.ForDatabase(database).ToList();
        var distinct = keys.Distinct().ToList();

        Assert.Equal(distinct.Count, keys.Count);
    }

    /// <summary>
    /// The catalogue's order must be reproducible, or a converter's output is
    /// not — the same reasoning that keeps the work queue unsorted and the
    /// system order declared once.
    /// </summary>
    [Fact]
    public void TheCatalogueIsInTheSameOrderEveryTime()
    {
        GameDatabase? database = RealDatabase();
        if (database is null) return;

        Assert.Equal(
            AssetCatalogue.ForDatabase(database).Select(k => k.Key).ToList(),
            AssetCatalogue.ForDatabase(database).Select(k => k.Key).ToList());
    }

    /// <summary>
    /// A wall is a volume and a floor is a surface, and the flag decides which —
    /// not the terrain's id, so a mod-authored terrain comes out right too.
    /// </summary>
    [Fact]
    public void ASolidTerrainAsksForAMeshAndAWalkableOneForATexture()
    {
        GameDatabase? database = RealDatabase();
        if (database is null) return;

        var byName = AssetCatalogue.ForTerrain(database).ToList();
        int checkedSolid = 0, checkedWalkable = 0;

        foreach (TerrainDef terrain in database.Terrain.Values)
        {
            string subject = AssetRef.NormaliseSubject(terrain.Name);
            AssetKind expected = terrain.IsSolid ? AssetKind.Mesh : AssetKind.Texture;
            AssetKind unexpected = terrain.IsSolid ? AssetKind.Texture : AssetKind.Mesh;

            Assert.Contains(byName, k => k.Key == $"terrain.{subject}.{AssetRef.RoleOf(expected)}");
            Assert.DoesNotContain(byName, k => k.Key == $"terrain.{subject}.{AssetRef.RoleOf(unexpected)}");

            if (terrain.IsSolid) checkedSolid++; else checkedWalkable++;
        }

        // Both branches were actually exercised by the shipped data.
        Assert.True(checkedSolid > 0 && checkedWalkable > 0);
    }

    /// <summary>
    /// Lava is the terrain a player must see is dangerous, so it gets an effect
    /// key; plain rock does not.
    /// </summary>
    [Fact]
    public void ATerrainThatHurtsWhatStandsOnItAsksForAnEffect()
    {
        GameDatabase? database = RealDatabase();
        if (database is null) return;

        var keys = AssetCatalogue.ForTerrain(database).ToList();

        foreach (TerrainDef terrain in database.Terrain.Values)
        {
            string subject = AssetRef.NormaliseSubject(terrain.Name);
            bool hasEffect = keys.Any(k => k.Key == $"terrain.{subject}.effect");
            Assert.Equal(terrain.Damage != 0, hasEffect);
        }

        Assert.NotNull(database.LavaTerrain);
        Assert.Contains(
            keys,
            k => k.Key == $"terrain.{AssetRef.NormaliseSubject(database.LavaTerrain!.Name)}.effect");
    }

    /// <summary>
    /// <b>The cross-assembly guard.</b> A renderer picks a creature's animation
    /// from its <see cref="CreatureActivity"/>, so the two lists have to line up:
    /// an activity with no key is undrawable, and a key with no activity is an
    /// asset nothing can ever ask for. <see cref="AssetCatalogue"/> cannot
    /// reference the simulation, so this test is what stops the copy drifting.
    /// </summary>
    [Fact]
    public void EveryCreatureActivityHasExactlyOneAnimationKey()
    {
        string[] activities = Enum.GetNames<CreatureActivity>()
            .Select(ToKeySegment)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        string[] animations = AssetCatalogue.CreatureAnimations
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(activities, animations);
    }

    /// <summary>PascalCase to snake_case, so <c>MovingToJob</c> is <c>moving_to_job</c>.</summary>
    private static string ToKeySegment(string pascalCase)
    {
        var sb = new System.Text.StringBuilder(pascalCase.Length + 4);
        for (int i = 0; i < pascalCase.Length; i++)
        {
            char c = pascalCase[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Every creature in the database, not only the two that get spawned: a
    /// converter's job is to produce what the database defines.
    /// </summary>
    [Fact]
    public void EveryCreatureInTheDatabaseGetsAMesh()
    {
        GameDatabase? database = RealDatabase();
        if (database is null) return;

        var meshes = AssetCatalogue.ForCreatures(database)
            .Where(k => k.Kind == AssetKind.Mesh)
            .ToList();

        Assert.Equal(database.Creatures.Count, meshes.Count);
        Assert.Equal(46, meshes.Count);
    }

    /// <summary>
    /// A room is paved ground and the furniture on it, and selling takes the
    /// second without the first (ADR-0029) — so they are two assets.
    /// </summary>
    [Fact]
    public void EveryRoomGetsBothAFloorAndAMesh()
    {
        GameDatabase? database = RealDatabase();
        if (database is null) return;

        var keys = AssetCatalogue.ForRooms(database).ToList();

        Assert.Equal(database.Rooms.Count * 2, keys.Count);
        Assert.Equal(database.Rooms.Count, keys.Count(k => k.Kind == AssetKind.Texture));
        Assert.Equal(database.Rooms.Count, keys.Count(k => k.Kind == AssetKind.Mesh));
    }
    /// <summary>
    /// Every object definition gets a mesh, and only a mesh.
    ///
    /// <para>Only a mesh <i>for now</i>: DK2 gives an object its own state
    /// machine and a <c>MoveTo</c> block, so a chicken pecking and a boulder
    /// rolling will want animation variants. This project cannot yet tell one
    /// object state from another, and a variant list invented here would put
    /// keys in a converter's work list that correspond to nothing — the same
    /// restraint the wall variants get.</para>
    /// </summary>
    [Fact]
    public void EveryObjectInTheDatabaseGetsAMeshAndNothingElse()
    {
        GameDatabase? database = RealDatabase();
        if (database is null) return;

        var keys = AssetCatalogue.ForObjects(database).ToList();

        Assert.Equal(database.Objects.Count, keys.Count);
        Assert.Equal(145, keys.Count);
        Assert.All(keys, k => Assert.Equal(AssetKind.Mesh, k.Kind));

        // And they are in the whole-database listing, or a converter would
        // never be asked to produce one.
        var all = new HashSet<string>(
            AssetCatalogue.ForDatabase(database).Select(k => k.Key), StringComparer.Ordinal);
        Assert.All(keys, k => Assert.Contains(k.Key, all));
    }
}