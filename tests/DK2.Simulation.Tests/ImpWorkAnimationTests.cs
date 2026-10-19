// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The stored action animation clock behind the dig and mining actions' end
/// (animation-clock.md, ADR-0514). The captured cases replay native dates from the Level 1-4
/// captures through the real per-turn clock rather than a phase reconstructed from entry.</summary>
public sealed class ImpWorkAnimationTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly TilePos Face = new(4, 2);
    private const long Collapse = 165;

    // Captured completions (imp-dig-stop-clock.md): the work state entered at `entered`, the
    // wall gone before turn 165, state 14 kept through `completed`.
    [Theory]
    [InlineData(150, 168)]
    [InlineData(158, 167)]
    [InlineData(137, 167)]
    public void AnExhaustedDigHoldsUntilItsAnimationCompletes_CapturedDates(long entered, long completed)
    {
        GameSimulation sim = Start(TileWorkKind.Dig, entered);
        RunTo(sim, Collapse);
        ExhaustFace(sim, TileWorkKind.Dig);
        CreatureState imp = sim.World.Creatures[0];
        while (sim.CurrentTick.Value < completed)
        {
            sim.Tick();
            Assert.Equal(14, imp.Dk2StateId);
        }
        sim.Tick();
        Assert.Equal(ImpStates.TaskSelection, imp.Dk2StateId);
    }

    // Captured mining holds (mining-finish.md): the seam gone `age` turns after entry holds the
    // miner in state 26 for `hold` more turns, and the fresh claim waits for the end.
    [Theory]
    [InlineData(8, 4)]
    [InlineData(7, 2)]
    [InlineData(6, 3)]
    [InlineData(5, 4)]
    [InlineData(4, 2)]
    public void AnExhaustedSeamHoldsUntilItsAnimationCompletes_CapturedHolds(int age, int hold)
    {
        GameSimulation sim = Start(TileWorkKind.MineGold, entered: 100);
        RunTo(sim, 100 + age);
        ExhaustFace(sim, TileWorkKind.MineGold);
        CreatureState miner = sim.World.Creatures[0];
        int work = ImpStates.For(TileWorkKind.MineGold).Work;
        for (int i = 0; i < hold; i++)
        {
            sim.Tick();
            Assert.Equal(work, miner.Dk2StateId);
            Assert.False(sim.World.Tile(Face).FreshlyDug);
        }
        sim.Tick();
        Assert.Equal(ImpStates.TaskSelection, miner.Dk2StateId);
        Assert.True(sim.World.Tile(Face).FreshlyDug);
    }

    [Fact]
    public void TheMinersOwnLastStrikeRequestsTheStopInTheSameTurn()
    {
        GameSimulation sim = Start(TileWorkKind.MineGold, entered: 100);
        RunTo(sim, 108);
        CreatureState miner = sim.World.Creatures[0];
        sim.World.Tile(Face).Health = 60;
        miner.TimerTicks = 1;
        sim.Tick();
        Assert.Equal(Dk2TerrainFixture.DirtPath, sim.World.Tile(Face).TerrainId);
        Assert.True((miner.ActionAnimationFlags & ActionAnimationFlags.Stop) != 0);
        for (int i = 0; i < 3; i++)
        {
            sim.Tick();
            Assert.Equal(ImpStates.For(TileWorkKind.MineGold).Work, miner.Dk2StateId);
        }
        sim.Tick();
        Assert.Equal(ImpStates.TaskSelection, miner.Dk2StateId);
    }

    [Fact]
    public void AFaceGoneBeforeTheFirstActionEndsAtOnce()
    {
        GameSimulation sim = Start(TileWorkKind.Dig, entered: 100);
        sim.Tick();
        ExhaustFace(sim, TileWorkKind.Dig);
        sim.Tick();
        Assert.Equal(ImpStates.TaskSelection, sim.World.Creatures[0].Dk2StateId);
    }

    // Not captured: the job removed while the wall still stands. The handler, called until it is
    // done, would strike once more and finish; the action then ends with its animation: at the
    // first wrap, then at once (0x00496AA0).
    [Theory]
    [InlineData(102, 105)]
    [InlineData(110, 110)]
    public void AnUntaggedWallEndsAtTheFirstWrapThenAtOnce_Unvalidated(long untagged, long leaves)
    {
        GameSimulation sim = Start(TileWorkKind.Dig, entered: 100);
        RunTo(sim, untagged);
        sim.World.Tile(Face).SetTaggedBy(Keeper, false);
        CreatureState imp = sim.World.Creatures[0];
        while (sim.CurrentTick.Value < leaves)
        {
            sim.Tick();
            Assert.Equal(14, imp.Dk2StateId);
        }
        sim.Tick();
        Assert.NotEqual(14, imp.Dk2StateId);
    }

    // Not captured (review of 0x00496AA0): once this worker's handler has struck it is not called
    // again in that action, so a wall taken by another worker in the first cycle sets no stop
    // bit and the action ends at the wrap, not after the End segment.
    [Fact]
    public void AWallTakenAfterThisWorkersStrikeEndsAtTheWrapWithoutStopping_Unvalidated()
    {
        GameSimulation sim = Start(TileWorkKind.Dig, entered: 100);
        CreatureState imp = sim.World.Creatures[0];
        imp.TimerTicks = WorkCadence.FirstApplicationTicks;
        while (!imp.ActionHandlerDone)
        {
            sim.Tick();
            Assert.Equal(14, imp.Dk2StateId);
        }
        Assert.False((imp.ActionAnimationFlags & ActionAnimationFlags.Wrapped) != 0);
        ExhaustFace(sim, TileWorkKind.Dig);
        long wrappedAfter = -1;
        while (imp.Dk2StateId == 14)
        {
            Assert.False((imp.ActionAnimationFlags & ActionAnimationFlags.Stop) != 0);
            sim.Tick();
            if (wrappedAfter < 0 && (imp.ActionAnimationFlags & ActionAnimationFlags.Wrapped) != 0)
                wrappedAfter = sim.CurrentTick.Value;
        }
        Assert.Equal(wrappedAfter + 1, sim.CurrentTick.Value);
    }

    [Fact]
    public void LeavingWorkPutsTheStandingClipBackSoTheNextBoutStartsFresh()
    {
        GameSimulation sim = Start(TileWorkKind.Dig, entered: 100);
        RunTo(sim, 105);
        CreatureState imp = sim.World.Creatures[0];
        Assert.Equal(5, imp.ActionArtSlot);
        Assert.True((imp.ActionAnimationFlags & ActionAnimationFlags.Wrapped) != 0);
        sim.World.Tile(Face).SetTaggedBy(Keeper, false);
        sim.Tick();
        Assert.NotEqual(14, imp.Dk2StateId);
        Assert.Equal(0, imp.ActionArtSlot);
        Assert.Equal(ActionAnimationFlags.None, imp.ActionAnimationFlags);
    }

    [Fact]
    public void ASaveInsideTheEndSegmentResumesWithTheSameHashes()
    {
        GameSimulation original = Start(TileWorkKind.Dig, entered: 158);
        RunTo(original, Collapse);
        ExhaustFace(original, TileWorkKind.Dig);
        original.Run(2);
        Assert.True((original.World.Creatures[0].ActionAnimationFlags & ActionAnimationFlags.InEnd) != 0);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original),
            original.Database, [new ImpTaskSystem()]);
        Assert.Equal(original.StateHash(), restored.StateHash());
        for (int i = 0; i < 6; i++)
        {
            original.Tick();
            restored.Tick();
            Assert.Equal(original.StateHash(), restored.StateHash());
        }
    }

    [Fact]
    public void TheClockIsHashed()
    {
        GameSimulation sim = Start(TileWorkKind.Dig, entered: 100);
        RunTo(sim, 103);
        ulong before = sim.StateHash();
        sim.World.Creatures[0].ActionAnimationPhase++;
        Assert.NotEqual(before, sim.StateHash());
    }

    // Slot 5's Main advances 1365 a turn from the turn after entry, so 4095 after three turns:
    // the loop wraps in the fourth turn's advance, E+4. How that lines up with WorkCadence's
    // measured second strike at E+6 depends on the strike handler, which is not read.
    [Fact]
    public void TheFirstWrapIsInTheFourthTurnAfterEntry()
    {
        GameSimulation sim = Start(TileWorkKind.Dig, entered: 100);
        CreatureState imp = sim.World.Creatures[0];
        RunTo(sim, 104);
        Assert.Equal(4095, imp.ActionAnimationPhase);
        Assert.False((imp.ActionAnimationFlags & ActionAnimationFlags.Wrapped) != 0);
        sim.Tick();
        Assert.Equal(4095 + 1365 - 4096, imp.ActionAnimationPhase);
        Assert.True((imp.ActionAnimationFlags & ActionAnimationFlags.Wrapped) != 0);
    }

    private static GameSimulation Start(TileWorkKind kind, long entered)
    {
        GameDatabase fixture = Dk2TerrainFixture.Database();
        var art = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        art[5] = new CreatureAnimationTiming
        {
            ResourceType = 6, Flags = CreatureAnimationFlags.Present | CreatureAnimationFlags.EndSegment,
            FrameCount = 15, FramesPerSecond = 20, EndSegmentFrames = 8,
        };
        var database = new GameDatabase(fixture.Terrain.Values, fixture.Rooms.Values,
            fixture.Creatures.Values.Select(c => c.CreatureId == Dk2TerrainFixture.ImpCreatureId
                ? c with { ArtSlots = art } : c), fixture.Variables, objects: fixture.Objects.Values,
            keeperSpells: fixture.KeeperSpells.Values);
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [(byte)0, PlayerIds.Neutral, Keeper]);
        CreatureState imp = world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 2));
        ref TileState tile = ref world.Tile(Face);
        tile.TerrainId = kind == TileWorkKind.MineGold ? Dk2TerrainFixture.Gold : Dk2TerrainFixture.Rock;
        tile.OwnerPlayerId = PlayerIds.Neutral;
        tile.Health = 30000;
        tile.SetTaggedBy(Keeper, true);
        imp.TargetWork = kind;
        imp.TargetTileIndex = Face.Y * world.Width + Face.X;
        imp.Dk2StateId = ImpStates.For(kind).Work;
        imp.StateEnteredTick = entered;
        imp.TimerTicks = 1_000_000;
        typeof(WorldState).GetProperty(nameof(WorldState.CurrentTick))!.SetValue(world, new GameTick(entered));
        return new GameSimulation(database, world, 99, [new ImpTaskSystem()]);
    }

    private static void RunTo(GameSimulation sim, long tick)
    {
        while (sim.CurrentTick.Value < tick)
        {
            sim.Tick();
            Assert.Equal(ImpStates.For(sim.World.Creatures[0].TargetWork).Work, sim.World.Creatures[0].Dk2StateId);
        }
    }

    private static void ExhaustFace(GameSimulation sim, TileWorkKind kind)
    {
        ref TileState tile = ref sim.World.Tile(Face);
        tile.TerrainId = Dk2TerrainFixture.DirtPath;
        tile.SetTaggedBy(Keeper, false);
        if (kind == TileWorkKind.MineGold) tile.DigTaskOutlivesWallFor = tile.DigTaskOutlivesWallFor.With(Keeper);
    }
}
