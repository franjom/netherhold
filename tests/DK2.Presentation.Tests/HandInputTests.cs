// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public class HandInputTests
{
    private static readonly PlayerId Keeper = new(3);
    private static readonly TilePos Start = new(2, 2), End = new(4, 3);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HandClickOrDragUsesTheStartingTilesInverseTag(bool tagged, bool rectangle)
    {
        var input = new PlayerInputTranslator();
        var snapshot = new PlayerInputSnapshot(false, excavation: new ExcavationTarget(Start, tagged));
        Assert.Empty(input.Translate(Intent(Start, PointerButtonTransition.Pressed), snapshot).All);
        Assert.Equal(!tagged, input.ExcavationPreviewTagged);
        TilePos end = rectangle ? End : Start;
        TagTilesCommand command = Assert.IsType<TagTilesCommand>(Assert.Single(
            input.Translate(Intent(end, PointerButtonTransition.Released), snapshot).All));
        Assert.Equal(!tagged, command.Tagged);
        TilePos[] expected = rectangle
            ? [new(2, 2), new(3, 2), new(4, 2), new(2, 3), new(3, 3), new(4, 3)] : [Start];
        Assert.Equal(expected, command.Tiles.OrderBy(t => t.Y).ThenBy(t => t.X));
        Assert.Null(input.ExcavationPreviewTagged);
    }

    [Fact]
    public void RightClickWithAnEmptyHandSlapsTheCreature()
    {
        var snapshot = Creature(false);
        var input = new PlayerInputTranslator();
        SlapCreatureCommand slap = Assert.IsType<SlapCreatureCommand>(Assert.Single(input.Translate(
            Intent(Start, secondary: PointerButtonTransition.Pressed), snapshot).All));
        Assert.Equal(40, slap.CreatureId);
        Assert.Empty(input.Translate(Intent(Start, secondary: PointerButtonTransition.Released), snapshot).All);
        Assert.Empty(input.Translate(Intent(Start), snapshot).All);
    }

    [Fact]
    public void RightClickCancelsAnUnfinishedExcavationAndDropsBeforeSlapping()
    {
        var input = new PlayerInputTranslator();
        input.Translate(Intent(Start, PointerButtonTransition.Pressed),
            new PlayerInputSnapshot(false, excavation: new ExcavationTarget(Start, false)));
        DropCommand drop = Assert.IsType<DropCommand>(Assert.Single(input.Translate(
            Intent(End, secondary: PointerButtonTransition.Pressed), new PlayerInputSnapshot(true)).All));
        Assert.Equal(End, drop.Tile);
        Assert.Null(input.ExcavationPreviewTagged);
        Assert.Empty(input.Translate(Intent(End, PointerButtonTransition.Released), new PlayerInputSnapshot(false)).All);
    }

    [Fact]
    public void CreaturePickupTakesPriorityOverExcavationAndNeverRepeatsOnRelease()
    {
        var input = new PlayerInputTranslator();
        var snapshot = new PlayerInputSnapshot(false, Creature(false).Targets, new ExcavationTarget(Start, false));
        Assert.IsType<PickUpCreatureCommand>(Assert.Single(input.Translate(
            Intent(Start, PointerButtonTransition.Pressed), snapshot).All));
        Assert.Null(input.ExcavationPreviewTagged);
        Assert.Empty(input.Translate(Intent(Start, PointerButtonTransition.Released), snapshot).All);
    }

    /// <summary>A store pile is a view decoration; the click lifts it by its own tile, which the
    /// hover keeps when it re-anchors the target on the pointer tile.</summary>
    [Fact]
    public void AClickOnAStorePileLiftsItByItsOwnTile()
    {
        var pileTile = new TilePos(7, 7);
        var captured = new PlayerInputSnapshot(false,
            [new PlayerInputTarget(PlayerInputTargetKind.StoreGold, -5, pileTile, pileTile)]);
        PlayerInputSnapshot snapshot = captured.ForHover(new WorldHoverHit(WorldHoverKind.Object, Start, -5));

        PickUpStoreGoldCommand lift = Assert.IsType<PickUpStoreGoldCommand>(Assert.Single(new PlayerInputTranslator()
            .Translate(Intent(Start, PointerButtonTransition.Pressed), snapshot).All));

        Assert.Equal(pileTile, lift.Tile);
    }

    [Fact]
    public void SwitchingToolsCancelsExcavationEvenWithoutAMouseEdge()
    {
        var input = new PlayerInputTranslator();
        var snapshot = new PlayerInputSnapshot(false, excavation: new ExcavationTarget(Start, false));
        input.Translate(Intent(Start, PointerButtonTransition.Pressed), snapshot);
        input.Translate(new PlayerIntent(Keeper, PlayerToolMode.BuildRoom, Start, PointerButtonTransition.None), snapshot);
        Assert.Null(input.ExcavationPreviewTagged);
        Assert.Empty(input.Translate(Intent(End, PointerButtonTransition.Released), snapshot).All);
    }

    [Fact]
    public void EmptyGroundClicksAndOffMapDropsDoNothing()
    {
        var input = new PlayerInputTranslator();
        Assert.Empty(input.Translate(Intent(Start, PointerButtonTransition.Pressed), new PlayerInputSnapshot(false)).All);
        Assert.Empty(input.Translate(Intent(Start, secondary: PointerButtonTransition.Pressed), new PlayerInputSnapshot(false)).All);
        Assert.Empty(input.Translate(Intent(null, secondary: PointerButtonTransition.Pressed), new PlayerInputSnapshot(true)).All);
    }

    [InstallFact]
    public void HandClicksUseTheSimulationsDropRulesAndRetainAnInvalidDropInHand()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        byte floor = db.Terrain.Values.Single(t => t.Name == "Claimed Path").TerrainId;
        var tiles = Enumerable.Repeat(new MapTile(floor, Keeper.Value, 0, 0), 49).ToArray();
        WorldState world = WorldState.FromMap(db, new LevelMap(7, 7, tiles), [PlayerIds.Neutral, Keeper.Value]);
        world.Tile(End).TerrainId = db.Terrain.Values.Single(t => t.Name == "Water").TerrainId;
        world.Tile(End).OwnerPlayerId = PlayerIds.Neutral;
        CreatureState creature = world.SpawnCreature(db, db.CreatureByName("Goblin")!.CreatureId, Keeper, Start);
        var view = new WorldView(db, Keeper);
        var input = new PlayerInputTranslator();
        var sim = new GameSimulation(db, world, 1, [new HandSystem()]);

        sim.Tick(input.Translate(Intent(Start, PointerButtonTransition.Pressed),
            PlayerInputSnapshot.Capture(view, world, Keeper, db, Start)));
        Assert.True(creature.InHand);
        Assert.Single(world.Player(Keeper).Hand);
        sim.Tick(input.Translate(Intent(End, secondary: PointerButtonTransition.Pressed),
            PlayerInputSnapshot.Capture(view, world, Keeper, db, End)));
        Assert.True(creature.InHand);
        Assert.Single(world.Player(Keeper).Hand);
        sim.Tick(input.Translate(Intent(Start, secondary: PointerButtonTransition.Pressed),
            PlayerInputSnapshot.Capture(view, world, Keeper, db, Start)));
        Assert.False(creature.InHand);
        Assert.Empty(world.Player(Keeper).Hand);
    }

    [InstallFact]
    public void TheHandActivatesASpecialOnItsKeepersTileAndNeverLiftsOne()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        byte floor = db.Terrain.Values.Single(t => t.Name == "Claimed Path").TerrainId;
        var tiles = Enumerable.Repeat(new MapTile(floor, Keeper.Value, 0, 0), 49).ToArray();
        WorldState world = WorldState.FromMap(db, new LevelMap(7, 7, tiles), [PlayerIds.Neutral, Keeper.Value]);
        world.Tile(End).OwnerPlayerId = PlayerIds.Neutral;
        ObjectState mine = world.SpawnObject(db, 117, PlayerIds.Neutral, Start.Y * 7 + Start.X);
        world.SpawnObject(db, 117, PlayerIds.Neutral, End.Y * 7 + End.X);
        var view = new WorldView(db, Keeper);
        var input = new PlayerInputTranslator();

        ActivateSpecialCommand activate = Assert.IsType<ActivateSpecialCommand>(Assert.Single(input.Translate(
            Intent(Start, PointerButtonTransition.Pressed), PlayerInputSnapshot.Capture(view, world, Keeper, db, Start)).All));
        Assert.Equal(mine.Id, activate.ObjectId);
        PlayerInputSnapshot hovered = PlayerInputSnapshot.Capture(view, world, Keeper, db, Start)
            .ForHover(new WorldHoverHit(WorldHoverKind.Object, Start, mine.Id));
        Assert.Equal(PlayerInputTargetKind.Special, Assert.Single(hovered.Targets).Kind);
        Assert.Empty(input.Translate(Intent(End, PointerButtonTransition.Pressed),
            PlayerInputSnapshot.Capture(view, world, Keeper, db, End)).All);
    }

    [InstallFact]
    public void SnapshotReadsOnlyThePointedTilesOwnTagAndPreviewDoesNotMutateIt()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var tiles = Enumerable.Repeat(new MapTile(db.Terrain.Values.Single(t => t.Name == "Rock").TerrainId,
            PlayerIds.Neutral, 0, 0), 49).ToArray();
        WorldState world = WorldState.FromMap(db, new LevelMap(7, 7, tiles), [PlayerIds.Neutral, Keeper.Value]);
        var view = new WorldView(db, Keeper);
        world.Tile(Start).SetTaggedBy(Keeper, true);
        Assert.Equal(new ExcavationTarget(Start, true), PlayerInputSnapshot.Capture(view, world, Keeper, db, Start).Excavation);
        int preview = view.PreviewVersion;
        view.PreviewTags(Start, Start, tagged: false);
        Assert.False(view.TileAt(world, Start.X, Start.Y).Tagged);
        Assert.True(world.Tile(Start).IsTaggedBy(Keeper));
        view.PreviewTags(Start, Start, tagged: true);
        Assert.True(view.TileAt(world, Start.X, Start.Y).Tagged);
        Assert.True(view.PreviewVersion >= preview + 2);
        view.PreviewTags(null, null);
        Assert.True(view.TileAt(world, Start.X, Start.Y).Tagged);
        Assert.Null(PlayerInputSnapshot.Capture(view, world, Keeper, db, new TilePos(-1, 0)).Excavation);
    }

    [InstallTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasedTagPreviewSurvivesUntilTheCommandTick(bool initiallyTagged)
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var tiles = Enumerable.Repeat(new MapTile(db.Terrain.Values.Single(t => t.Name == "Rock").TerrainId,
            PlayerIds.Neutral, 0, 0), 49).ToArray();
        WorldState world = WorldState.FromMap(db, new LevelMap(7, 7, tiles), [PlayerIds.Neutral, Keeper.Value]);
        var view = new WorldView(db, Keeper);
        world.Tile(Start).SetTaggedBy(Keeper, initiallyTagged);
        view.PreviewTags(Start, End, !initiallyTagged);
        view.QueueTagPreview(TagTilesCommand.Rect(Keeper, Start.X, Start.Y, End.X, End.Y, !initiallyTagged),
            world.CurrentTick.Next);
        view.PreviewTags(null, null);
        view.AcknowledgeTagPreviews(world.CurrentTick);
        for (int frame = 0; frame < 4; frame++)
        {
            Assert.Equal(!initiallyTagged, view.TileAt(world, Start.X, Start.Y).Tagged);
            Assert.Equal(initiallyTagged, world.Tile(Start).IsTaggedBy(Keeper));
        }
        world.Tile(Start).SetTaggedBy(Keeper, !initiallyTagged);
        view.AcknowledgeTagPreviews(world.CurrentTick.Next);
        Assert.Equal(!initiallyTagged, view.TileAt(world, Start.X, Start.Y).Tagged);
        world.Tile(Start).SetTaggedBy(Keeper, initiallyTagged);
        Assert.Equal(initiallyTagged, view.TileAt(world, Start.X, Start.Y).Tagged);
    }

    private static PlayerInputSnapshot Creature(bool holding) => new(holding,
        [new PlayerInputTarget(PlayerInputTargetKind.Creature, 40, Start)]);

    private static PlayerIntent Intent(TilePos? at, PointerButtonTransition primary = PointerButtonTransition.None,
        PointerButtonTransition secondary = PointerButtonTransition.None) =>
        new(Keeper, PlayerToolMode.Hand, at, primary, SecondaryButton: secondary);
}
