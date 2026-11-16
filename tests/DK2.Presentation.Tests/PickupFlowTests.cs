// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The pickup flow moved out of Unity's runner (plan step 2c): which commands of a frame are posted, in which order,
/// and what each site adds. A failure here changes the journalled command stream.</summary>
public sealed class PickupFlowTests
{
    private static readonly PlayerId Keeper = PressFixture.Keeper;

    [Fact]
    public void AWorldFramePostsInItsOwnOrderSkippingARefusedPickupAndPostingAnUndeferredDrop()
    {
        GameDatabase db = PressFixture.Database();
        WorldState world = PressFixture.World(db);
        var view = new WorldView(db, Keeper);
        var flow = new PickupFlow(view, new HandCursorFeedback());
        var slap = new SlapCreatureCommand(Keeper, 5);
        var missing = new PickUpCreatureCommand(Keeper, 77);
        var drop = new DropCommand(Keeper, new TilePos(1, 1));
        var tag = new TagTilesCommand(Keeper, [new TilePos(1, 1)], true);
        var output = new List<PlayerCommand>();
        var posted = new List<bool>();

        foreach (PlayerCommand command in new PlayerCommand[] { slap, missing, drop, tag })
            posted.Add(flow.PostWorldCommand(world, db, command, 0, null, output));

        // No creature 77: the pickup is skipped and the frame goes on; no pickup is pending, so the drop is posted.
        Assert.Equal([true, false, true, true], posted);
        Assert.Equal(new PlayerCommand[] { slap, drop, tag }, output);
        Assert.Equal(0, view.PreviewVersion);
    }

    [Fact]
    public void ARefusedOverviewPickupPostsNothing()
    {
        GameDatabase db = PressFixture.Database();
        WorldState world = PressFixture.World(db);
        var view = new WorldView(db, Keeper);
        var cursor = new HandCursorFeedback();
        var output = new List<PlayerCommand>();

        Assert.False(new PickupFlow(view, cursor).PickUpFromOverview(world, db, Keeper, new CreatureQueryTarget(77),
            new CreatureOverview(db, Keeper, (_, name) => name), 0, output));

        Assert.Empty(output);
        Assert.Equal(0, view.PreviewVersion);
        Assert.Equal(HandCursorKind.Point, cursor.Resolve(HandCursorKind.Point, false, false, false, 0));
    }

    [InstallFact]
    public void AnOverviewPickupPlaysTheHeldCursorPostsAndPreviewsOnTheMinimapAndTheOverview()
    {
        var (db, world, goblin) = PressFixture.Goblin();
        var view = new WorldView(db, Keeper);
        var cursor = new HandCursorFeedback();
        var overview = new CreatureOverview(db, Keeper, (_, name) => name);
        var type = new CreatureOverviewType(goblin.CreatureTypeId);
        var output = new List<PlayerCommand>();
        Assert.Equal(CreatureOverviewActionKind.Pickup, overview.Select(world, type, CreatureOverviewSelection.PickupNext).Kind);

        Assert.True(new PickupFlow(view, cursor).PickUpFromOverview(world, db, Keeper, new CreatureQueryTarget(goblin.Id),
            overview, 100, output));

        Assert.Equal(new PickUpCreatureCommand(Keeper, goblin.Id), Assert.Single(output));
        Assert.True(view.LocalPickups.Contains(new CreatureId(goblin.Id)));
        Assert.Equal(1, view.PreviewVersion);
        Assert.Equal(HandCursorKind.HoldThing, cursor.Resolve(HandCursorKind.Point, false, false, false, 100));
        // The overview's own preview hides the creature from its next pick until the turn applies it.
        Assert.Equal(CreatureOverviewActionKind.None, overview.Select(world, type, CreatureOverviewSelection.PickupNext).Kind);
    }

    [InstallFact]
    public void AWorldPickupIsPostedOnceAndDefersTheFrameDropUntilTheCreatureIsHeld()
    {
        var (db, world, goblin) = PressFixture.Goblin();
        var view = new WorldView(db, Keeper);
        var cursor = new HandCursorFeedback();
        var flow = new PickupFlow(view, cursor);
        var overview = new CreatureOverview(db, Keeper, (_, name) => name);
        var pickup = new PickUpCreatureCommand(Keeper, goblin.Id);
        var again = new PickUpCreatureCommand(Keeper, goblin.Id);
        var drop = new DropCommand(Keeper, new TilePos(1, 1));
        var output = new List<PlayerCommand>();
        var posted = new List<bool>();

        foreach (PlayerCommand command in new PlayerCommand[] { pickup, again, drop })
            posted.Add(flow.PostWorldCommand(world, db, command, 100, null, output));

        Assert.Equal([true, false, false], posted);
        Assert.Same(pickup, Assert.Single(output));
        Assert.Equal(1, view.PreviewVersion);
        Assert.Equal(HandCursorKind.HoldThing, cursor.Resolve(HandCursorKind.Point, false, false, false, 100));
        // The world site queues no overview preview: the overview still offers the creature.
        Assert.Equal(CreatureOverviewActionKind.Pickup,
            overview.Select(world, new CreatureOverviewType(goblin.CreatureTypeId), CreatureOverviewSelection.PickupNext).Kind);
        // The deferred drop is posted once the creature is in the hand.
        view.LocalPickups.DrainReadyDrops(world, output);
        Assert.Single(output);
        goblin.InHand = true;
        world.Player(Keeper).Hand.Add(HandItem.Of(goblin));
        view.LocalPickups.DrainReadyDrops(world, output);
        Assert.Equal(new PlayerCommand[] { pickup, drop }, output);
    }
}
