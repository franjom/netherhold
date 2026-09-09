// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>Objects crossing the presentation boundary: beds, coops, eggs and
/// piles of gold have been in <see cref="WorldState"/> since ADR-0054, and until
/// these tests every renderer drew a bare floor where one stood. They run against
/// the real install for the reason <c>WorldViewTests</c> gives: the mapping's job
/// is to be right for 145 object definitions it did not choose.</summary>
public class ObjectViewTests
{
    private static (GameDatabase Database, WorldState World)? RealLevel()
    {
        string dir = Dk2InstallGate.Path;
        if (dir is null) return null;

        GameDatabase database = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")
            ?? throw new InvalidOperationException("the install has no Level1 map.");
        return (database, WorldState.FromMap(database, map, [0, 1, 3]));
    }

    [InstallFact]
    public void AnObjectInTheHandIsNotDrawnInTheDungeon()
    {
        if (RealLevel() is not var (database, world)) return;

        ObjectState held = world.SpawnObject(database, 1, PlayerIds.Neutral, 40);
        held.InHand = true;

        Assert.DoesNotContain(new WorldView(database).Objects(world), o => o.Id == held.Id);
    }

    /// <summary>
    /// Every object in the world gets a mesh key, and it is the same key the
    /// catalogue lists — the agreement that stops a view inventing keys no
    /// converter will ever produce.
    /// </summary>
    [InstallFact]
    public void EveryObjectAsksForAKeyTheCatalogueLists()
    {
        if (RealLevel() is not var (database, world)) return;

        var catalogue = new HashSet<string>(
            AssetCatalogue.ForObjects(database).Select(k => k.Key), StringComparer.Ordinal);

        Assert.NotEmpty(catalogue);
        Assert.Equal(database.Objects.Count, catalogue.Count);

        // One of each kind the simulation can place today.
        world.SpawnObject(database, 1, PlayerIds.Neutral, 40);
        world.SpawnObject(database, 22, 3, 41);
        world.SpawnObject(database, 35, 3, 42);
        world.SpawnObject(database, 47, 3, 43);

        var view = new WorldView(database);
        var seen = view.Objects(world).ToList();

        Assert.Equal(4, seen.Count);
        foreach (ObjectView instance in seen)
        {
            Assert.False(instance.Mesh.IsEmpty, $"object {instance.ObjectTypeId} has no mesh key");
            Assert.Equal(AssetKind.Mesh, instance.Mesh.Kind);
            Assert.Contains(instance.Mesh.Key, catalogue);
        }
    }

    /// <summary>
    /// An object's key is built from its <i>name</i>, like every other key in
    /// this project — a name survives a mod and a re-extract where an id does
    /// not (the reasoning in <see cref="AssetRef"/>).
    /// </summary>
    [InstallFact]
    public void AKeyIsBuiltFromTheObjectsName()
    {
        if (RealLevel() is not var (database, world)) return;

        world.SpawnObject(database, 1, PlayerIds.Neutral, 40);
        ObjectView pile = new WorldView(database).Objects(world).Single();

        Assert.Equal("object.gold.mesh", pile.Mesh.Key);
    }

    /// <summary>
    /// A pile of gold carries its amount across the boundary, and everything
    /// else carries zero. A renderer needs the amount: DK2 draws a bigger heap
    /// for more gold.
    /// </summary>
    [InstallFact]
    public void OnlyGoldCrossesWithAnAmount()
    {
        if (RealLevel() is not var (database, world)) return;

        FloorGold.Drop(world, database, 40, 1750);
        world.SpawnObject(database, 22, 3, 41);

        var byType = new WorldView(database).Objects(world).ToDictionary(o => o.ObjectTypeId);

        Assert.Equal(1750, byType[1].GoldHeld);
        Assert.Equal(0, byType[22].GoldHeld);
        Assert.Equal(PlayerIds.Neutral, byType[1].OwnerPlayerId);
        Assert.Equal(3, byType[22].OwnerPlayerId);
    }

    /// <summary>The view reports the tile the object stands on, in world coordinates.</summary>
    [InstallFact]
    public void AnObjectReportsItsTile()
    {
        if (RealLevel() is not var (database, world)) return;

        int index = (7 * world.Width) + 5;
        world.SpawnObject(database, 1, PlayerIds.Neutral, index);

        Assert.Equal(new TilePos(5, 7), new WorldView(database).Objects(world).Single().Tile);
    }

    /// <summary>
    /// Spawn order, matching creatures, so a renderer diffing two frames sees a
    /// stable sequence.
    /// </summary>
    [InstallFact]
    public void ObjectsComeOutInSpawnOrder()
    {
        if (RealLevel() is not var (database, world)) return;

        world.SpawnObject(database, 1, PlayerIds.Neutral, 60);
        world.SpawnObject(database, 22, 3, 20);
        world.SpawnObject(database, 35, 3, 40);

        Assert.Equal(
            new byte[] { 1, 22, 35 },
            new WorldView(database).Objects(world).Select(o => o.ObjectTypeId).ToArray());
    }

    // ---- the text renderer -------------------------------------------------

    /// <summary>
    /// Gold draws as gold whether it is in a store or on the floor, and every
    /// other object draws as furniture.
    /// </summary>
    [InstallFact]
    public void GoldDrawsAsGoldAndEverythingElseAsFurniture()
    {
        if (RealLevel() is not var (database, world)) return;

        var view = new WorldView(database);
        var renderer = new TextRenderer(view, new PlaceholderProvider());

        FloorGold.Drop(world, database, 40, 500);
        world.SpawnObject(database, 22, 3, 41);

        var glyphs = view.Objects(world)
            .ToDictionary(o => o.ObjectTypeId, renderer.GlyphFor);

        Assert.Equal(TextRenderer.GoldGlyph, glyphs[1]);
        Assert.Equal(TextRenderer.ObjectGlyph, glyphs[22]);
    }

    /// <summary>
    /// And it reaches the picture: an object on a tile changes the character
    /// drawn there. This is the test that would have failed before this work —
    /// the picture showed bare floor where a bed stood.
    /// </summary>
    [InstallFact]
    public void AnObjectChangesTheCharacterDrawnForItsTile()
    {
        if (RealLevel() is not var (database, world)) return;

        var view = new WorldView(database);
        var renderer = new TextRenderer(view, new PlaceholderProvider());

        // A walkable tile, so the object is what changes rather than the wall.
        int x = -1, y = -1;
        for (int i = 0; i < world.TileCount && x < 0; i++)
        {
            if (database.TerrainById(world.TileAtIndex(i).TerrainId).IsSolid) continue;
            if (world.TileAtIndex(i).GoldHeld > 0) continue;
            x = i % world.Width;
            y = i / world.Width;
        }

        Assert.True(x >= 0, "the level has no plain walkable tile");

        string before = renderer.Render(world, x, y, 1, 1);
        world.SpawnObject(database, 22, 3, (y * world.Width) + x);
        string after = renderer.Render(world, x, y, 1, 1);

        Assert.NotEqual(before, after);
        Assert.Equal(TextRenderer.ObjectGlyph, after[0]);
    }

    /// <summary>
    /// A creature standing on an object hides it, not the other way round —
    /// the precedence the shared view owns so that two renderers cannot
    /// disagree about it.
    /// </summary>
    [InstallFact]
    public void ACreatureStandingOnAnObjectHidesIt()
    {
        if (RealLevel() is not var (database, world)) return;

        var renderer = new TextRenderer(new WorldView(database), new PlaceholderProvider());

        int x = -1, y = -1;
        for (int i = 0; i < world.TileCount && x < 0; i++)
        {
            if (database.TerrainById(world.TileAtIndex(i).TerrainId).IsSolid) continue;
            x = i % world.Width;
            y = i / world.Width;
        }

        world.SpawnObject(database, 22, 3, (y * world.Width) + x);
        world.SpawnCreature(database, database.Creatures.Keys.First(), 3, new TilePos(x, y));

        Assert.Equal(TextRenderer.CreatureGlyph, renderer.Render(world, x, y, 1, 1)[0]);
    }
}
