// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// Reading DK2's task pool back as input frames. The pool is a work pipeline,
/// not a set of marks — a tile sits in several kind lists at once and migrates
/// between them — so the questions here are about which pool changes are the
/// player and which are DK2 giving its own imps a job.
/// </summary>
public class InputLogTests
{
    private static InputLog Read(
        string body, long lag = 1, long origin = 1, HashSet<byte>? kinds = null) =>
        InputLog.Read(
            new StringReader(InputLog.Header + "\n" + body), "test", lag, origin, kinds);

    /// <summary>
    /// wall,tick,player,event,kind,x,y,type,data,id,workers. <c>workers</c> is
    /// how many of the task's twelve Worker slots were filled when the probe
    /// last saw it, which is what tells a finished dig from a dropped mark.
    /// </summary>
    private static string Row(
        long tick, string ev, byte kind, int x, int y, byte player = 3, int workers = 0) =>
        $"0.0,{tick},{player},{ev},{kind},{x},{y},{kind},0,1,{workers}\n";

    private static TagTilesCommand Only(InputFrame frame) =>
        Assert.IsType<TagTilesCommand>(Assert.Single(frame.All));

    // ---- what counts as an event -----------------------------------------

    /// <summary>
    /// A tile entering the pool under a mark kind is the player marking it, and
    /// leaving it altogether is the mark going away. The stamp is one turn late
    /// (ADR-0119), so a row stamped 41 is a command issued on 40.
    /// </summary>
    [Fact]
    public void EnteringThePoolIsATagAndLeavingItIsAnUntag()
    {
        InputLog log = Read(Row(41, "tag", 1, 10, 20) + Row(61, "untag", 1, 10, 20));

        Assert.True(Only(log.At(40)).Tagged);
        Assert.False(Only(log.At(60)).Tagged);
    }

    /// <summary>
    /// The real trap, from the first live capture: a tile moves between kind
    /// lists as an imp takes it up, and every one of those rows is a tag or an
    /// untag. Reading rows as events emits an untag the player never issued,
    /// which on replay would stop our imps digging.
    /// </summary>
    [Fact]
    public void MigratingBetweenKindListsIsNotAnEvent()
    {
        InputLog log = Read(
            Row(41, "tag", 1, 14, 24)
            + Row(61, "tag", 14, 14, 24)
            + Row(62, "untag", 1, 14, 24)
            + Row(63, "tag", 3, 14, 24));

        Assert.Equal([40L], log.Turns);
        Assert.Equal(3, log.Tally.Migrations);
    }

    /// <summary>
    /// And when that tile finally leaves, the leaving is the work finishing —
    /// an imp had taken it, so the player is not the one letting go.
    /// </summary>
    [Fact]
    public void LeavingAfterAnImpTookItIsWorkFinishing()
    {
        InputLog log = Read(
            Row(41, "tag", 1, 14, 24)
            + Row(61, "tag", 14, 14, 24)
            + Row(62, "untag", 1, 14, 24)
            + Row(80, "untag", 14, 14, 24));

        Assert.Equal([40L], log.Turns);
        Assert.Equal(1, log.Tally.Worked);
    }

    /// <summary>
    /// The case the kind transition misses. Gold dug in run 10 was never
    /// sampled in a work kind at all — it entered as kind 2 and left as kind 2,
    /// which reads as the player letting go. What says otherwise is that the
    /// tile is then claimed, which only a dug tile can be.
    /// </summary>
    [Fact]
    public void ADigThatFinishedWithoutAWorkKindEverBeingSampledIsStillWork()
    {
        InputLog log = Read(
            Row(41, "tag", 2, 16, 23)
            + Row(90, "untag", 2, 16, 23, workers: 1)
            + Row(91, "tag", InputLog.ClaimKind, 16, 23, workers: 1));

        // Two: the dig's removal, and the claim task's own arrival.
        Assert.Equal([40L], log.Turns);
        Assert.Equal(2, log.Tally.Worked);
    }

    /// <summary>
    /// Run 11's untag B, and the reason the worker count is not the test: the
    /// owner cancelled a tile with **three** imps already digging it, the same
    /// count a finished dig shows. No claim follows a cancelled tile, because
    /// it is still rock — that is what separates them (Q293).
    /// </summary>
    [Fact]
    public void CancellingADigThreeImpsAreOnIsStillThePlayer()
    {
        InputLog log = Read(
            Row(127, "tag", 1, 11, 27)
            + Row(141, "untag", 1, 11, 27, workers: 3));

        Assert.Equal([126L, 140L], log.Turns);
        Assert.False(Only(log.At(140)).Tagged);
        Assert.Equal(0, log.Tally.Worked);
    }

    /// <summary>
    /// And a claim that arrives after the tile is marked again belongs to the
    /// later dig. Without that bound, re-marking a tile would retrospectively
    /// turn an earlier cancellation into a completion.
    /// </summary>
    [Fact]
    public void AClaimAfterTheTileIsMarkedAgainDoesNotExcuseTheEarlierUntag()
    {
        InputLog log = Read(
            Row(41, "tag", 1, 11, 27)
            + Row(50, "untag", 1, 11, 27, workers: 3)
            + Row(60, "tag", 1, 11, 27)
            + Row(80, "untag", 1, 11, 27, workers: 3)
            + Row(81, "tag", InputLog.ClaimKind, 11, 27, workers: 1));

        Assert.Contains(49L, log.Turns);
        Assert.False(Only(log.At(49)).Tagged);
        Assert.DoesNotContain(79L, log.Turns);
    }

    /// <summary>A tile the player drops before any imp touches it is theirs.</summary>
    /// <summary>Run 11's untag A: dropped before any imp reached it.</summary>
    [Fact]
    public void LeavingWithNoImpEverOnItIsThePlayerLettingGo()
    {
        InputLog log = Read(Row(109, "tag", 1, 8, 30) + Row(119, "untag", 1, 8, 30));

        Assert.Equal(0, log.Tally.Worked);
        Assert.Equal([108L, 118L], log.Turns);
    }

    [Fact]
    public void EnteringUnderAWorkKindIsDk2GivingItselfAJob()
    {
        InputLog log = Read(Row(41, "tag", 6, 13, 23));

        Assert.Empty(log.Turns);
        Assert.Equal(1, log.Tally.Worked);
    }

    /// <summary>
    /// A player marking tiles one at a time looks exactly like a trickle, so
    /// arrival rate says nothing about who caused it — CONFIRMED (owner,
    /// 2026-09-10) against an earlier reading of this class that treated a
    /// lone arrival as suspect. The kind is the discriminator; the cadence is
    /// not.
    /// </summary>
    [Fact]
    public void MarksArrivingOneAtATimeAreStillThePlayers()
    {
        InputLog log = Read(
            Row(96, "tag", 1, 14, 23) + Row(102, "tag", 1, 15, 23)
            + Row(109, "tag", 2, 16, 23) + Row(117, "tag", 2, 17, 23));

        Assert.Equal([95L, 101L, 108L, 116L], log.Turns);
        Assert.Equal(0, log.Tally.Worked);
    }

    // ---- the shape of a frame --------------------------------------------

    [Fact]
    public void ADragArrivesAsOneCommandInRowMajorOrder()
    {
        InputLog log = Read(
            Row(41, "tag", 1, 11, 20) + Row(41, "tag", 1, 10, 21) + Row(41, "tag", 1, 10, 20));

        Assert.Equal(
            [new TilePos(10, 20), new TilePos(11, 20), new TilePos(10, 21)],
            Only(log.At(40)).Tiles);
    }

    [Fact]
    public void PlayersAreOrderedSoAFrameIsNotTheProbesEmissionOrder()
    {
        InputLog log = Read(
            Row(41, "tag", 1, 10, 20, player: 5) + Row(41, "tag", 1, 11, 20, player: 2));

        Assert.Collection(
            log.At(40).All,
            c => Assert.Equal(2, ((TagTilesCommand)c).PlayerId),
            c => Assert.Equal(5, ((TagTilesCommand)c).PlayerId));
    }

    [Fact]
    public void OnePlayersUntagDoesNotTouchAnothersMark()
    {
        InputLog log = Read(
            Row(41, "tag", 1, 10, 20, player: 3) + Row(41, "tag", 1, 10, 20, player: 4)
            + Row(61, "untag", 1, 10, 20, player: 3));

        Assert.Equal(2, log.At(40).All.Count);
        Assert.False(Only(log.At(60)).Tagged);
    }

    // ---- bookkeeping -----------------------------------------------------

    [Fact]
    public void LagZeroTakesTheStampAtFaceValue()
    {
        Assert.Equal([41L], Read(Row(41, "tag", 1, 10, 20), lag: 0).Turns);
    }

    /// <summary>A capture that attached at turn 5000 still replays from our turn 1.</summary>
    [Fact]
    public void AnOriginRebasesEveryTurn()
    {
        InputLog log = Read(
            Row(5001, "tag", 1, 10, 20) + Row(5011, "tag", 1, 11, 20), origin: 5000);

        Assert.Equal([1L, 11L], log.Turns);
    }

    [Fact]
    public void CommandsBeforeTheRunBeginsAreDroppedAndCounted()
    {
        InputLog log = Read(Row(1, "tag", 1, 10, 20) + Row(50, "tag", 1, 11, 20));

        Assert.Equal([49L], log.Turns);
        Assert.Equal(1, log.Tally.Before);
    }

    /// <summary>The window is the capture's, so --origin can be checked against it.</summary>
    [Fact]
    public void TheWindowSpansEveryRowIncludingOnesThatIssuedNoCommand()
    {
        InputLog log = Read(Row(41, "tag", 6, 10, 20) + Row(90, "tag", 6, 11, 20));

        Assert.Equal(41, log.Tally.FirstStamp);
        Assert.Equal(90, log.Tally.LastStamp);
    }

    [Fact]
    public void AnExplicitKindSetOverridesTheDefault()
    {
        InputLog log = Read(Row(41, "tag", 7, 10, 20), kinds: [7]);

        Assert.Equal(0, log.Tally.Worked);
        Assert.Single(log.Turns);
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
    [InlineData("0.0,41,3,tag,1,10,20,1,0,1\n")]
    [InlineData("0.0,41,3,poke,1,10,20,1,0,1,0\n")]
    [InlineData("0.0,x,3,tag,1,10,20,1,0,1,0\n")]
    public void AMalformedRowIsRefusedRatherThanGuessedAt(string body) =>
        Assert.Throws<InvalidDataException>(() => Read(body));

    [Fact]
    public void AFileWithoutTheProbesHeaderIsRefused() =>
        Assert.Throws<InvalidDataException>(
            () => InputLog.Read(new StringReader("tick,x,y\n"), "test"));

    // ---- against the live capture ----------------------------------------

    /// <summary>
    /// Rows lifted from run 9, the first live capture. The player marked
    /// (10,28) and dropped it, then marked (14,24), which an imp took up and
    /// dug out. Reading rows as events gave five commands here; three of them
    /// were DK2 working.
    /// </summary>
    [Fact]
    public void TheFirstLiveCaptureYieldsThreeCommandsAndNotFive()
    {
        InputLog log = Read(
            Row(36, "tag", 1, 10, 28)
            + Row(48, "untag", 1, 10, 28)
            + Row(69, "tag", 2, 14, 24)
            + Row(121, "tag", 14, 14, 24)
            + Row(122, "untag", 2, 14, 24)
            + Row(125, "tag", InputLog.ClaimKind, 14, 24)
            + Row(136, "untag", InputLog.ClaimKind, 14, 24, workers: 1)
            + Row(136, "untag", 14, 14, 24, workers: 1));

        Assert.Equal([35L, 47L, 68L], log.Turns);
        Assert.Equal(3, log.Tally.Commands);
        Assert.Equal(8, log.Tally.Rows);
    }

    /// <summary>
    /// And the whole point: the tile the player marked is marked in our world
    /// at the turn they marked it, with no phantom untag from the imp's work.
    /// </summary>
    [Fact]
    public void ALogTagsTheTileTheCaptureSaysWasTagged()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, (byte)3]);
        TilePos target = Rock(database, world);
        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());

        InputLog log = Read(
            Row(3, "tag", 1, target.X, target.Y) + Row(9, "tag", 14, target.X, target.Y));
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
