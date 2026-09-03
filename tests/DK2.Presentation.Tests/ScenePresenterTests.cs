using System.Text;
using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;

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

        public void Flush()
        {
            Calls.Add("flush");
            Flushes++;
        }

        public void Clear()
        {
            Calls.Clear();
            Creates = Updates = CreatureCreates = CreatureUpdates = Removes = Flushes = 0;
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
        string? dir = Dk2Install.Find();
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

    /// <summary>
    /// The first push creates every tile exactly once, in row order, and every
    /// tile resolves — with no DK2 art installed, so all of it is placeholders.
    /// </summary>
    [Fact]
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
    [Fact]
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
    [Fact]
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
    [Fact]
    public void ACreatureIsCreatedThenUpdatedThenRemoved()
    {
        if (BuildRig() is not { } rig) return;

        WorldState world = rig.Simulation.World;
        rig.Presenter.Present(world);
        rig.Target.Clear();

        CreatureDef imp = rig.Database.CreatureByName("Imp")!;
        TilePos at = FirstWalkable(rig.Database, world);
        CreatureState spawned = world.SpawnCreature(imp.CreatureId, 1, at);

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
    [Fact]
    public void ChangingOnlyTheActivityStillPushesTheCreature()
    {
        if (BuildRig() is not { } rig) return;

        WorldState world = rig.Simulation.World;
        CreatureDef imp = rig.Database.CreatureByName("Imp")!;
        CreatureState spawned = world.SpawnCreature(
            imp.CreatureId, 1, FirstWalkable(rig.Database, world));

        rig.Presenter.Present(world);
        rig.Target.Clear();

        spawned.Activity = CreatureActivity.Working;
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
    [Fact]
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
    [Fact]
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
    [Fact]
    public void AChainWithoutAPlaceholderFailsLoudly()
    {
        if (BuildRig() is not { } rig) return;

        var presenter = new ScenePresenter(
            new WorldView(rig.Database),
            new AssetProviderChain(FileAssetProvider.Converted("no/such/directory")),
            rig.Target);

        Assert.Throws<InvalidOperationException>(() => presenter.Present(rig.Simulation.World));
    }

    [Fact]
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
}
