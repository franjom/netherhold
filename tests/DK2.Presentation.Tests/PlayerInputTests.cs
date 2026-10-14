// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation.Tests;

public class PlayerInputTests
{
    private const byte Keeper = 3;
    private readonly PlayerInputTranslator _translator = new();

    [Theory]
    [InlineData(PlayerToolMode.TagTiles, true)]
    [InlineData(PlayerToolMode.UntagTiles, false)]
    public void TagGestureEmitsOneNormalisedRectangle(PlayerToolMode tool, bool tagged)
    {
        Assert.Empty(Translate(tool, new TilePos(4, 3), PointerButtonTransition.Pressed).All);

        TagTilesCommand command = Assert.IsType<TagTilesCommand>(
            Assert.Single(Translate(tool, new TilePos(2, 1), PointerButtonTransition.Released).All));

        Assert.Equal(Keeper, command.PlayerId);
        Assert.Equal(tagged, command.Tagged);
        // Marking files the tiles in DK2's spiral from the corner the drag began at (ADR-0288);
        // unmarking has no order to keep.
        TilePos[] expected = tagged
            ? [new TilePos(4, 3), new TilePos(3, 3), new TilePos(3, 2), new TilePos(4, 2),
               new TilePos(4, 1), new TilePos(3, 1), new TilePos(2, 1), new TilePos(2, 2), new TilePos(2, 3)]
            : [new TilePos(2, 1), new TilePos(3, 1), new TilePos(4, 1),
               new TilePos(2, 2), new TilePos(3, 2), new TilePos(4, 2),
               new TilePos(2, 3), new TilePos(3, 3), new TilePos(4, 3)];
        Assert.Equal(expected, command.Tiles);
    }

    [Fact]
    public void BuildGestureKeepsTheRoomSelectedWhenTheDragStarted()
    {
        Translate(PlayerToolMode.BuildRoom, new TilePos(1, 2),
            PointerButtonTransition.Pressed, roomId: (RoomTypeId)7);

        BuildRoomCommand command = Assert.IsType<BuildRoomCommand>(Assert.Single(
            Translate(PlayerToolMode.BuildRoom, new TilePos(2, 3),
                PointerButtonTransition.Released, roomId: (RoomTypeId)12).All));

        Assert.Equal((RoomTypeId)7, command.RoomId);
        // DK2's build wave: from the corner released, a diagonal at a time (room-slab-chain.md §2).
        Assert.Equal(
            [new TilePos(2, 3), new TilePos(2, 2), new TilePos(1, 3), new TilePos(1, 2)],
            command.Tiles);
    }

    [Fact]
    public void SellGestureEmitsTheRectangleOnRelease()
    {
        Translate(PlayerToolMode.SellRoom, new TilePos(5, 6), PointerButtonTransition.Pressed);

        SellRoomCommand command = Assert.IsType<SellRoomCommand>(Assert.Single(
            Translate(PlayerToolMode.SellRoom, new TilePos(6, 6),
                PointerButtonTransition.Released).All));

        Assert.Equal([new TilePos(6, 6), new TilePos(5, 6)], command.Tiles);
    }

    [Fact]
    public void ChangingToolsCancelsAnUnfinishedDrag()
    {
        Translate(PlayerToolMode.TagTiles, new TilePos(1, 1), PointerButtonTransition.Pressed);

        Assert.Empty(Translate(PlayerToolMode.SellRoom, new TilePos(2, 2),
            PointerButtonTransition.Released).All);
    }

    [Fact]
    public void HandClickPrefersCreatureToObjectOnTheSameTile()
    {
        TilePos tile = new(4, 5);
        var snapshot = new PlayerInputSnapshot(false,
        [
            new PlayerInputTarget(PlayerInputTargetKind.Object, 70, tile),
            new PlayerInputTarget(PlayerInputTargetKind.Creature, 40, tile),
        ]);

        PickUpCreatureCommand command = Assert.IsType<PickUpCreatureCommand>(Assert.Single(
            Translate(PlayerToolMode.Hand, tile, PointerButtonTransition.Pressed, snapshot).All));

        Assert.Equal(40, command.CreatureId);
    }

    [Fact]
    public void HandClickPicksUpAnObjectWhenNoCreatureIsThere()
    {
        TilePos tile = new(4, 5);
        var snapshot = new PlayerInputSnapshot(false,
        [
            new PlayerInputTarget(PlayerInputTargetKind.Object, 70, tile),
        ]);

        PickUpObjectCommand command = Assert.IsType<PickUpObjectCommand>(Assert.Single(
            Translate(PlayerToolMode.Hand, tile, PointerButtonTransition.Pressed, snapshot).All));

        Assert.Equal(70, command.ObjectId);
    }

    [Fact]
    public void LeftHandClickWhileHoldingStillPicksUpAnotherCreature()
    {
        TilePos tile = new(4, 5);
        var snapshot = new PlayerInputSnapshot(true,
        [new PlayerInputTarget(PlayerInputTargetKind.Creature, 40, tile)]);
        PickUpCreatureCommand command = Assert.IsType<PickUpCreatureCommand>(Assert.Single(
            Translate(PlayerToolMode.Hand, tile, PointerButtonTransition.Pressed, snapshot).All));
        Assert.Equal(40, command.CreatureId);
    }

    [Fact]
    public void RightHandClickDropsBeforeConsideringTargetsWhenHoldingSomething()
    {
        TilePos tile = new(4, 5);
        var snapshot = new PlayerInputSnapshot(true,
        [
            new PlayerInputTarget(PlayerInputTargetKind.Creature, 40, tile),
        ]);

        DropCommand command = Assert.IsType<DropCommand>(Assert.Single(
            _translator.Translate(new PlayerIntent(Keeper, PlayerToolMode.Hand, tile,
                PointerButtonTransition.None, SecondaryButton: PointerButtonTransition.Pressed), snapshot).All));

        Assert.Equal(tile, command.Tile);
        Assert.Equal(40, command.TargetCreatureId);
    }

    [Fact]
    public void SlapClickTargetsTheFirstCreatureOnTheTile()
    {
        TilePos tile = new(4, 5);
        var snapshot = new PlayerInputSnapshot(false,
        [
            new PlayerInputTarget(PlayerInputTargetKind.Creature, 40, tile),
            new PlayerInputTarget(PlayerInputTargetKind.Creature, 41, tile),
        ]);

        SlapCreatureCommand command = Assert.IsType<SlapCreatureCommand>(Assert.Single(
            Translate(PlayerToolMode.Slap, tile, PointerButtonTransition.Pressed, snapshot).All));

        Assert.Equal(40, command.CreatureId);
    }

    [Fact]
    public void CreateImpNeedsNoPointerTileAndDoesNotRepeatWithoutAnEdge()
    {
        Assert.IsType<CreateImpCommand>(Assert.Single(
            Translate(PlayerToolMode.CreateImp, null, PointerButtonTransition.Pressed).All));
        Assert.Empty(Translate(PlayerToolMode.CreateImp, null, PointerButtonTransition.None).All);
    }

    private InputFrame Translate(
        PlayerToolMode tool,
        TilePos? tile,
        PointerButtonTransition transition,
        PlayerInputSnapshot? snapshot = null,
        RoomTypeId roomId = default) =>
        _translator.Translate(
            new PlayerIntent(Keeper, tool, tile, transition, roomId),
            snapshot ?? new PlayerInputSnapshot(false));
}
