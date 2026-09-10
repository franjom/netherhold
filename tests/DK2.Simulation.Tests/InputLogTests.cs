// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// Reading DK2's task-pool log back as input frames. The log is a diff of a
/// sampled list, not a stream of clicks, so the questions here are about what
/// that costs: when a change happened, and which changes were never commands.
/// </summary>
public class InputLogTests
{
    private static InputLog Read(string body, long lag = 1, long origin = 1) =>
        InputLog.Read(new StringReader(InputLog.Header + "\n" + body), "test", lag, origin);

    private static TagTilesCommand Only(InputFrame frame)
    {
        PlayerCommand command = Assert.Single(frame.All);
        return Assert.IsType<TagTilesCommand>(command);
    }

    // ---- when a command happened -----------------------------------------

    /// <summary>
    /// The probe sees a change one turn after the game made it (ADR-0119), so a
    /// row stamped 41 is a command issued on 40. Nothing in the file says this;
    /// it is the caller's claim, which is why it is an argument.
    /// </summary>
    [Fact]
    public void AStampIsOneTurnLaterThanTheCommand()
    {
        InputLog log = Read("0.0,41,3,tag,1,10,20\n");

        Assert.Equal([40L], log.Turns);
        Assert.Empty(log.At(41).All);
    }

    [Fact]
    public void LagZeroTakesTheStampAtFaceValue()
    {
        InputLog log = Read("0.0,41,3,tag,1,10,20\n", lag: 0);

        Assert.Equal([41L], log.Turns);
    }

    /// <summary>A capture that attached at turn 5000 still replays from our turn 1.</summary>
    [Fact]
    public void AnOriginRebasesEveryTurn()
    {
        InputLog log = Read("0.0,5001,3,tag,1,10,20\n0.0,5011,3,tag,1,11,20\n",
            origin: 5000);

        Assert.Equal([1L, 11L], log.Turns);
    }

    [Fact]
    public void CommandsBeforeTheRunBeginsAreDroppedAndCounted()
    {
        InputLog log = Read("0.0,1,3,tag,1,10,20\n0.0,50,3,tag,1,11,20\n");

        Assert.Equal([49L], log.Turns);
        Assert.Equal(1, log.Tally.Before);
        Assert.Equal(1, log.Tally.Commands);
    }

    // ---- what was never a command ----------------------------------------

    /// <summary>
    /// The pool splits tagged tiles into two lists by whether the terrain holds
    /// gold, so a tile crossing between them is untagged and tagged in one
    /// sample. Replaying that pair would untag a tile the player still wants
    /// dug. It cancels because the alternative — a real untag and retag inside
    /// one sample — nets to no change in the pool and emits no rows at all.
    /// </summary>
    [Fact]
    public void ATileMovingBetweenTheTwoTagListsIsNotACommand()
    {
        InputLog log = Read("0.0,41,3,untag,1,10,20\n0.0,41,3,tag,2,10,20\n");

        Assert.Empty(log.Turns);
        Assert.Equal(1, log.Tally.Cancelled);
        Assert.Equal(2, log.Tally.Rows);
    }

    [Fact]
    public void DifferentTilesInOneSampleBothSurvive()
    {
        InputLog log = Read("0.0,41,3,untag,1,10,20\n0.0,41,3,tag,1,11,20\n");

        Assert.Equal(0, log.Tally.Cancelled);
        Assert.Collection(
            log.At(40).All,
            c => Assert.False(Assert.IsType<TagTilesCommand>(c).Tagged),
            c => Assert.True(Assert.IsType<TagTilesCommand>(c).Tagged));
    }

    /// <summary>Cancelling is per player: two keepers may mark the same tile.</summary>
    [Fact]
    public void OnePlayersUntagDoesNotCancelAnothersTag()
    {
        InputLog log = Read("0.0,41,3,untag,1,10,20\n0.0,41,4,tag,1,10,20\n");

        Assert.Equal(0, log.Tally.Cancelled);
        Assert.Equal(2, log.At(40).All.Count);
    }

    // ---- the shape of a frame --------------------------------------------

    [Fact]
    public void ADragArrivesAsOneCommandInRowMajorOrder()
    {
        InputLog log = Read(
            "0.0,41,3,tag,1,11,20\n0.0,41,3,tag,1,10,21\n0.0,41,3,tag,1,10,20\n");

        TagTilesCommand tag = Only(log.At(40));
        Assert.Equal(
            [new TilePos(10, 20), new TilePos(11, 20), new TilePos(10, 21)],
            tag.Tiles);
    }

    [Fact]
    public void PlayersAreOrderedSoAFrameIsNotTheProbesEmissionOrder()
    {
        InputLog log = Read("0.0,41,5,tag,1,10,20\n0.0,41,2,tag,1,11,20\n");

        Assert.Collection(
            log.At(40).All,
            c => Assert.Equal(2, ((TagTilesCommand)c).PlayerId),
            c => Assert.Equal(5, ((TagTilesCommand)c).PlayerId));
    }

    [Fact]
    public void AnEmptyLogYieldsNothingRatherThanFailing()
    {
        InputLog log = Read(string.Empty);

        Assert.Empty(log.Turns);
        Assert.Empty(log.At(1).All);
        Assert.Equal(0, log.Tally.Rows);
    }

    // ---- refusals --------------------------------------------------------

    [Theory]
    [InlineData("0.0,41,3,tag,1,10\n")]
    [InlineData("0.0,41,3,poke,1,10,20\n")]
    [InlineData("0.0,x,3,tag,1,10,20\n")]
    public void AMalformedRowIsRefusedRatherThanGuessedAt(string body) =>
        Assert.Throws<InvalidDataException>(() => Read(body));

    [Fact]
    public void AFileWithoutTheProbesHeaderIsRefused() =>
        Assert.Throws<InvalidDataException>(
            () => InputLog.Read(new StringReader("tick,x,y\n"), "test"));

    // ---- through the simulation ------------------------------------------

    /// <summary>
    /// The point of the whole class: a log drives the tile a player marked, at
    /// the turn they marked it. A stamp of 3 is DK2 turn 2, which is our second
    /// tick, so the tile is still unmarked when the first one has run.
    /// </summary>
    [Fact]
    public void ALogTagsTheTileTheCaptureSaysWasTagged()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, (byte)3]);
        TilePos target = Rock(database, world);
        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());

        InputLog log = Read($"0.0,3,3,tag,1,{target.X},{target.Y}\n");
        Assert.Equal([2L], log.Turns);

        sim.Tick(log.At(sim.CurrentTick.Value + 1));
        Assert.False(world.Tile(target).IsTaggedBy(3));

        sim.Tick(log.At(sim.CurrentTick.Value + 1));
        Assert.True(world.Tile(target).IsTaggedBy(3));
    }

    private static TilePos Rock(GameDatabase database, WorldState world)
    {
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                TerrainDef terrain = database.TerrainById(world.Tile(x, y).TerrainId);
                if (terrain.IsSolid && terrain.IsTaggable && !terrain.IsImpenetrable)
                    return new TilePos(x, y);
            }

        throw new InvalidOperationException("the fixture map has no diggable tile");
    }
}
