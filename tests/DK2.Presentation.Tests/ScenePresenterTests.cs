// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>
/// The half of the Unity view that can be tested here.
///
/// <para>Everything a renderer could get wrong twice — what counts as a visible
/// change, what order things are pushed in, create versus update versus remove —
/// lives in <see cref="ScenePresenter"/> and is asserted here against a real
/// campaign level. What is left for Unity is six methods with no logic in them,
/// which is the point of pushing the logic down.</para>
/// </summary>
public class ScenePresenterTests
{
    /// <summary>
    /// A target that writes down what it was asked to do. The transcript is the
    /// artefact: order is part of the contract, so a recorded sequence is
    /// testable rather than impressionistic.
    /// </summary>
    private sealed class RecordingTarget : IRenderTarget
    {
        public readonly List<string> Calls = [];
        public int Creates, Updates, CreatureCreates, CreatureUpdates, Removes, Flushes;
        public int ObjectCreates, ObjectUpdates, ObjectRemoves;
        public readonly List<ResolvedAsset> Grounds = [];

        public void CreateTile(int i, in TileView t, in ResolvedAsset g, in ResolvedAsset f)
        {
            Calls.Add($"create-tile {i}");
            Grounds.Add(g);
            Creates++;
        }

        public void UpdateTile(int i, in TileView t, in ResolvedAsset g, in ResolvedAsset f)
        {
            Calls.Add($"update-tile {i}");
            Updates++;
        }

        public void CreateCreature(int id, in CreatureView c, in ResolvedAsset m, in ResolvedAsset a)
        {
            Calls.Add($"create-creature {id} {a.Reference}");
            CreatureCreates++;
        }

        public void UpdateCreature(int id, in CreatureView c, in ResolvedAsset m, in ResolvedAsset a)
        {
            Calls.Add($"update-creature {id} {a.Reference}");
            CreatureUpdates++;
        }

        public void RemoveCreature(int id)
        {
            Calls.Add($"remove-creature {id}");
            Removes++;
        }

        public void CreateObject(int id, in ObjectView o, in ResolvedAsset m)
        {
            Calls.Add($"create-object {id} {m.Reference}");
            ObjectCreates++;
        }

        public void UpdateObject(int id, in ObjectView o, in ResolvedAsset m)
        {
            Calls.Add($"update-object {id} {m.Reference}");
            ObjectUpdates++;
        }

        public void RemoveObject(int id)
        {
            Calls.Add($"remove-object {id}");
            ObjectRemoves++;
        }

        public void CreateStructure(StructureId id, in StructureView s, in ResolvedAsset m) =>
            Calls.Add($"create-structure {id} {m.Reference}");
        public void UpdateStructure(StructureId id, in StructureView s, in ResolvedAsset m) =>
            Calls.Add($"update-structure {id} {m.Reference}");
        public void RemoveStructure(StructureId id) => Calls.Add($"remove-structure {id}");

        public void CreateTorch(int i, in TorchView t, in ResolvedAsset m) => Calls.Add($"create-torch {i} {m.Reference}");

        public void UpdateTorch(int i, in TorchView t, in ResolvedAsset m) => Calls.Add($"update-torch {i}");

        public void RemoveTorch(int i) => Calls.Add($"remove-torch {i}");

        public void Flush()
        {
            Calls.Add("flush");
            Flushes++;
        }

        public void Clear()
        {
            Calls.Clear();
            Creates = Updates = CreatureCreates = CreatureUpdates = Removes = Flushes = 0;
            ObjectCreates = ObjectUpdates = ObjectRemoves = 0;
            Grounds.Clear();
        }
    }

    private sealed record Rig(
        GameDatabase Database,
        GameSimulation Simulation,
        ScenePresenter Presenter,
        RecordingTarget Target);

    private static Rig? BuildRig()
    {
        string dir = Dk2InstallGate.Path;
        if (dir is null) return null;

        GameDatabase database = GameDatabaseLoader.Load(dir);
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(dir, "Level1")
            ?? throw new InvalidOperationException("the install has no Level1 map.");

        var simulation = new GameSimulation(
            database, WorldState.FromMap(database, map, [0, 1]), seed: 11);

        var target = new RecordingTarget();
        var presenter = new ScenePresenter(
            new WorldView(database), AssetProviderChain.Standard(), target);

        return new Rig(database, simulation, presenter, target);
    }

    [InstallFact]
    public void AHeartBecomingRuinedUpdatesEveryFloorTile()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level8");
        WorldState world = WorldState.FromMap(db,
            GameDatabaseLoader.LoadCampaignMap(install, "Level8")!, [0, 1, 2, 3, 4]);
        var target = new RecordingTarget();
        var presenter = new ScenePresenter(new WorldView(db), AssetProviderChain.Standard(), target);
        presenter.Present(world);
        target.Clear();
        world.Player(4).HeartRuined = true;
        presenter.Present(world);
        Assert.Equal(25, target.Updates);
    }

    /// <summary>
    /// With a <see cref="TorchLayout"/>, the torches come after the tiles, once each in floor-tile
    /// order; an unchanged world pushes none, and a resync draws them all again (ADR-0391).
    /// </summary>
    [InstallFact]
    public void TorchesArePushedAfterTheTiles_OnceEach_AndAgainAfterAResync()
    {
        if (BuildRig() is not { } rig) return;
        var target = new RecordingTarget();
        var presenter = new ScenePresenter(new WorldView(rig.Database), AssetProviderChain.Standard(),
            target, new TorchLayout(rig.Database));

        presenter.Present(rig.Simulation.World);
        List<string> torches = target.Calls.Where(c => c.StartsWith("create-torch", StringComparison.Ordinal)).ToList();
        List<int> floors = torches.Select(c => int.Parse(c.Split(' ')[1])).ToList();
        Assert.NotEmpty(floors);
        Assert.Equal(floors.OrderBy(i => i), floors);
        Assert.True(target.Calls.FindLastIndex(c => c.StartsWith("create-tile", StringComparison.Ordinal))
            < target.Calls.FindIndex(c => c.StartsWith("create-torch", StringComparison.Ordinal)));
        Assert.Equal(floors.Count, presenter.TorchesPushed);

        target.Clear();
        presenter.Present(rig.Simulation.World);
        Assert.Equal(0, presenter.TorchesPushed);

        target.Clear();
        presenter.Resync();
        presenter.Present(rig.Simulation.World);
        Assert.Equal(torches, target.Calls.Where(c => c.StartsWith("create-torch", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The first push creates every tile exactly once, in row order, and every
    /// tile resolves — with no DK2 art installed, so all of it is placeholders.
    /// </summary>
    [InstallFact]
    public void TheFirstPresentCreatesEveryTileOnceInRowOrder()
    {
        if (BuildRig() is not { } rig) return; // skipped: no install

        rig.Presenter.Present(rig.Simulation.World);

        int tiles = rig.Simulation.World.TileCount;
        Assert.Equal(tiles, rig.Target.Creates);
        Assert.Equal(0, rig.Target.Updates);
        Assert.Equal(1, rig.Target.Flushes);
        Assert.Equal(tiles, rig.Presenter.TilesPushed);

        // Row order: the tile-create calls are indices 0..n-1 in sequence.
        var createdIndices = rig.Target.Calls
            .Where(c => c.StartsWith("create-tile", StringComparison.Ordinal))
            .Select(c => int.Parse(c.Split(' ')[1]))
            .ToList();

        Assert.Equal(Enumerable.Range(0, tiles), createdIndices);

        // And every one of them got an asset, all of them placeholders.
        Assert.Equal(tiles, rig.Target.Grounds.Count);
        Assert.All(rig.Target.Grounds, g => Assert.True(g.IsPlaceholder));
    }

    /// <summary>
    /// <b>The property the whole class exists for.</b> Presenting an unchanged
    /// world pushes nothing at all. A Unity target that rebuilt 3,721 tile
    /// objects per frame would be unusable, and this is what makes sure it never
    /// has to.
    /// </summary>
    [InstallFact]
    public void PresentingAnUnchangedWorldPushesNothing()
    {
        if (BuildRig() is not { } rig) return;

        rig.Presenter.Present(rig.Simulation.World);
        rig.Target.Clear();

        rig.Presenter.Present(rig.Simulation.World);

        Assert.Equal(0, rig.Target.Creates);
        Assert.Equal(0, rig.Target.Updates);
        Assert.Equal(0, rig.Target.CreatureCreates);
        Assert.Equal(0, rig.Target.CreatureUpdates);
        Assert.Equal(0, rig.Target.Removes);
        Assert.Equal(0, rig.Presenter.TilesPushed);

        // Flush still happens: a batching target needs to know a frame ended
        // even when nothing in it moved.
        Assert.Equal(1, rig.Target.Flushes);
    }

    /// <summary>
    /// One tile changing pushes one tile — an update, not a create, because the
    /// target already has an object for it.
    /// </summary>
    [InstallFact]
    public void ChangingOneTilePushesExactlyThatTile()
    {
        if (BuildRig() is not { } rig) return;

        rig.Presenter.Present(rig.Simulation.World);
        rig.Target.Clear();

        // Mark a diggable tile: visible to a player, so visible to a renderer.
        WorldState world = rig.Simulation.World;
        int marked = -1;
        for (int i = 0; i < world.TileCount && marked < 0; i++)
            if (rig.Database.TerrainById(world.TileAtIndex(i).TerrainId).IsTaggable) marked = i;

        Assert.True(marked >= 0, "level 1 has no taggable tile.");
        world.TileAtIndex(marked).SetTaggedBy(1, true);

        rig.Presenter.Present(world);

        Assert.Equal(0, rig.Target.Creates);
        Assert.Equal(1, rig.Target.Updates);
        Assert.Contains($"update-tile {marked}", rig.Target.Calls);
    }

    /// <summary>
    /// A creature appears, then moves, then goes — create, update, remove, in
    /// that order and with nothing spurious in between.
    /// </summary>
    [InstallFact]
    public void ACreatureIsCreatedThenUpdatedThenRemoved()
    {
        if (BuildRig() is not { } rig) return;

        WorldState world = rig.Simulation.World;
        rig.Presenter.Present(world);
        rig.Target.Clear();

        CreatureDef imp = rig.Database.CreatureByName("Imp")!;
        TilePos at = FirstWalkable(rig.Database, world);
        CreatureState spawned = world.SpawnCreature(rig.Simulation.Database, imp.CreatureId, 1, at);

        rig.Presenter.Present(world);
        Assert.Equal(1, rig.Target.CreatureCreates);
        Assert.Equal(0, rig.Target.CreatureUpdates);

        // Same state again: nothing.
        rig.Target.Clear();
        rig.Presenter.Present(world);
        Assert.Equal(0, rig.Target.CreatureCreates);
        Assert.Equal(0, rig.Target.CreatureUpdates);

        // Moved: an update.
        rig.Target.Clear();
        spawned.PosX += 1;
        rig.Presenter.Present(world);
        Assert.Equal(1, rig.Target.CreatureUpdates);
        Assert.Equal(0, rig.Target.CreatureCreates);

        // Gone: a remove, exactly once.
        rig.Target.Clear();
        Assert.True(world.RemoveCreature(spawned.Id));
        rig.Presenter.Present(world);
        Assert.Equal(1, rig.Target.Removes);
        Assert.Contains($"remove-creature {spawned.Id}", rig.Target.Calls);

        rig.Target.Clear();
        rig.Presenter.Present(world);
        Assert.Equal(0, rig.Target.Removes);
    }

    /// <summary>
    /// A creature that changes only what it is <i>doing</i> is still a change: a
    /// renderer has to swap the animation. Cheap to get wrong by diffing on
    /// position alone.
    /// </summary>
    [InstallFact]
    public void ChangingOnlyTheActivityStillPushesTheCreature()
    {
        if (BuildRig() is not { } rig) return;

        WorldState world = rig.Simulation.World;
        CreatureDef imp = rig.Database.CreatureByName("Imp")!;
        CreatureState spawned = world.SpawnCreature(
            rig.Database, imp.CreatureId, 1, FirstWalkable(rig.Database, world));

        rig.Presenter.Present(world);
        rig.Target.Clear();

        spawned.Dk2StateId = 14; // digging: the state names Working (ADR-0211)
        rig.Presenter.Present(world);

        Assert.Equal(1, rig.Target.CreatureUpdates);
        Assert.Contains(
            rig.Target.Calls,
            c => c.Contains("creature.imp.animation.working", StringComparison.Ordinal));
    }

    /// <summary>
    /// <see cref="ScenePresenter.Resync"/> is how a target that threw its scene
    /// away asks to be filled in again.
    /// </summary>
    [InstallFact]
    public void ResyncMakesTheNextPresentRebuildEverything()
    {
        if (BuildRig() is not { } rig) return;

        rig.Presenter.Present(rig.Simulation.World);
        rig.Target.Clear();

        rig.Presenter.Resync();
        rig.Presenter.Present(rig.Simulation.World);

        Assert.Equal(rig.Simulation.World.TileCount, rig.Target.Creates);
        Assert.Equal(0, rig.Target.Updates);
    }

    /// <summary>
    /// Presenting a running simulation over many ticks pushes a small fraction
    /// of the world, and — the assertion that matters — <b>never changes it</b>.
    /// The determinism gate pointed at the presentation layer, over a run rather
    /// than a single frame.
    /// </summary>
    [InstallFact]
    public void PresentingEveryTickOfARunChangesNothingAndPushesLittle()
    {
        if (BuildRig() is not { } rig) return;

        rig.Presenter.Present(rig.Simulation.World);

        var hashes = new List<ulong>();
        int pushed = 0;

        for (int t = 0; t < 60; t++)
        {
            rig.Simulation.Tick();
            ulong afterTick = rig.Simulation.StateHash();

            rig.Presenter.Present(rig.Simulation.World);
            pushed += rig.Presenter.TilesPushed;

            // Presenting must not have moved the world on.
            Assert.Equal(afterTick, rig.Simulation.StateHash());
            hashes.Add(afterTick);
        }

        Assert.Equal(60, hashes.Count);

        // A dungeon at rest changes a handful of tiles a tick at most. This is a
        // ceiling, not a measurement: the point is that it is nowhere near
        // "every tile, every tick".
        int wholeWorldRedraws = rig.Simulation.World.TileCount * 60;
        Assert.True(
            pushed < wholeWorldRedraws / 10,
            $"pushed {pushed} tile updates over 60 ticks, which is not far off " +
            $"redrawing the whole world every tick ({wholeWorldRedraws}).");
    }

    /// <summary>
    /// A chain with no placeholder can miss, and the presenter must say so
    /// loudly rather than pushing a half-built scene — a missing asset is a
    /// configuration bug, not a missing file, once the placeholder exists.
    /// </summary>
    [InstallFact]
    public void AChainWithoutAPlaceholderFailsLoudly()
    {
        if (BuildRig() is not { } rig) return;

        var presenter = new ScenePresenter(
            new WorldView(rig.Database),
            new AssetProviderChain(FileAssetProvider.Converted("no/such/directory")),
            rig.Target);

        Assert.Throws<InvalidOperationException>(() => presenter.Present(rig.Simulation.World));
    }

    [InstallFact]
    public void ThePresenterRefusesNullsAtConstruction()
    {
        if (BuildRig() is not { } rig) return;

        var view = new WorldView(rig.Database);
        AssetProviderChain chain = AssetProviderChain.Standard();

        Assert.Throws<ArgumentNullException>(() => new ScenePresenter(null!, chain, rig.Target));
        Assert.Throws<ArgumentNullException>(() => new ScenePresenter(view, null!, rig.Target));
        Assert.Throws<ArgumentNullException>(() => new ScenePresenter(view, chain, null!));
    }

    private static TilePos FirstWalkable(GameDatabase database, WorldState world)
    {
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
                if (!database.TerrainById(world.Tile(x, y).TerrainId).IsSolid)
                    return new TilePos(x, y);

        throw new InvalidOperationException("the level has no walkable tile.");
    }
    // ---- objects -----------------------------------------------------------

    /// <summary>
    /// An object appears, changes, and goes — the same create/update/remove
    /// lifecycle a creature gets, and the reason the presenter had to learn one:
    /// objects have been in the world since ADR-0054 and nothing drew them.
    /// </summary>
    [InstallFact]
    public void AnObjectIsCreatedThenUpdatedThenRemoved()
    {
        if (BuildRig() is not { } rig) return; // skipped: no install

        rig.Presenter.Present(rig.Simulation.World);
        rig.Target.Clear();

        ObjectState pile = FloorGold.Drop(
            rig.Simulation.World, rig.Database, 40, 500)!;

        rig.Presenter.Present(rig.Simulation.World);
        Assert.Equal(1, rig.Target.ObjectCreates);
        Assert.Equal(1, rig.Presenter.ObjectsPushed);
        Assert.Contains(rig.Target.Calls, c => c.StartsWith("create-object", StringComparison.Ordinal));

        // Nothing changed: nothing is pushed. That is the whole point of the diff.
        rig.Target.Clear();
        rig.Presenter.Present(rig.Simulation.World);
        Assert.Equal(0, rig.Target.ObjectCreates);
        Assert.Equal(0, rig.Target.ObjectUpdates);

        // The pile shrinks, so it is pushed again as an update.
        FloorGold.Take(rig.Simulation.World, rig.Database, pile, 200);
        rig.Presenter.Present(rig.Simulation.World);
        Assert.Equal(1, rig.Target.ObjectUpdates);
        Assert.Equal(0, rig.Target.ObjectCreates);

        // Emptied, so destroyed, so removed.
        rig.Target.Clear();
        FloorGold.Take(rig.Simulation.World, rig.Database, pile, 1000);
        rig.Presenter.Present(rig.Simulation.World);
        Assert.Equal(1, rig.Target.ObjectRemoves);
        Assert.Contains(rig.Target.Calls, c => c == $"remove-object {pile.Id}");
    }

    /// <summary>
    /// Objects are pushed before creatures, because a creature stands on an
    /// object. One order, decided in the presenter, so the Unity target and the
    /// text renderer cannot disagree about what occludes what.
    /// </summary>
    [InstallFact]
    public void ObjectsArePushedBeforeCreatures()
    {
        if (BuildRig() is not { } rig) return; // skipped: no install

        FloorGold.Drop(rig.Simulation.World, rig.Database, 40, 500);
        rig.Simulation.World.SpawnCreature(
            rig.Database, rig.Database.Creatures.Keys.First(), 1, new TilePos(1, 1));

        rig.Presenter.Present(rig.Simulation.World);

        int objectAt = rig.Target.Calls.FindIndex(
            c => c.StartsWith("create-object", StringComparison.Ordinal));
        int creatureAt = rig.Target.Calls.FindIndex(
            c => c.StartsWith("create-creature", StringComparison.Ordinal));

        Assert.True(objectAt >= 0 && creatureAt >= 0);
        Assert.True(objectAt < creatureAt, "objects should be pushed before creatures");
    }

    /// <summary>
    /// A resync forgets objects too, or a target that threw everything away
    /// would be left with holes where the beds were.
    /// </summary>
    [InstallFact]
    public void AResyncCreatesTheObjectsAgain()
    {
        if (BuildRig() is not { } rig) return; // skipped: no install

        FloorGold.Drop(rig.Simulation.World, rig.Database, 40, 500);
        rig.Target.Clear();
        rig.Presenter.Present(rig.Simulation.World);
        int created = rig.Target.ObjectCreates; // the pile and the Heart's decorations

        rig.Target.Clear();
        rig.Presenter.Resync();
        rig.Presenter.Present(rig.Simulation.World);

        Assert.True(created > 1);
        Assert.Equal(created, rig.Target.ObjectCreates);
    }
}
