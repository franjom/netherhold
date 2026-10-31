// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class CasinoWinnerSlapTests
{
    private static (GameSimulation Sim, CreatureState Winner) Start(int winnings = 4000, int held = 10000)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, (byte)3, (byte)4]);
        CreatureState winner = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 3, new TilePos(3, 3));
        winner.JackpotDeadlineTicks = 100;
        winner.JackpotWinnings = winnings;
        winner.GoldHeld = held;
        winner.Anger[AngerBucket.Treatment] = 400;
        StatePlacement.Place(winner, CreatureStateMachine.Idle);
        Dk2TerrainFixture.SetKnownIdentities(world);
        return (new GameSimulation(db, world, seed: 7, [new HandSystem()]), winner);
    }

    private static void Slap(GameSimulation sim, CreatureState winner) =>
        sim.Tick(InputFrame.Of(new SlapCreatureCommand(3, winner.Id)));

    [Theory]
    [InlineData(3996, 1, 999)]
    [InlineData(4000, 2, 500)]
    [InlineData(11996, 2, 1499)]
    [InlineData(12000, 3, 1000)]
    public void FirstSlapDebitsQuarterSplitsGoldAndUsesTwoSimulationDrawsPerPile(int winnings, int count, int each)
    {
        var (sim, winner) = Start(winnings);
        var expected = SimRandom.Restore(sim.Rng.SaveState());
        Slap(sim, winner);
        ObjectState[] piles = sim.World.Objects.Where(o => o.ObjectTypeId == 1).ToArray();
        Assert.Equal(count, piles.Length);
        foreach (ObjectState pile in piles)
        {
            Assert.Equal(each, pile.GoldHeld);
            Assert.Equal(PlayerIds.Neutral, pile.OwnerPlayerId);
            Assert.Equal((winner.PosX & ~4095) | expected.NextInt(4096), pile.PosX);
            Assert.Equal((winner.PosY & ~4095) | expected.NextInt(4096), pile.PosY);
            Assert.Equal(0x3000, pile.PosZ);
            Assert.True(FloorGoldLandingRules.HasPosition(pile));
            Assert.Equal((byte)(Math.Min((pile.PosY & 4095) / 0x555, 2) * 3 + Math.Min((pile.PosX & 4095) / 0x555, 2)), pile.Cell);
        }
        Assert.Equal(expected.SaveState(), sim.Rng.SaveState());
        Assert.Equal(10000 - winnings / 4, winner.GoldHeld);
        Assert.Equal(winnings, winner.JackpotWinnings);
        Assert.Equal(500, winner.Anger[AngerBucket.Treatment]);
        Assert.Equal(1, winner.CasinoBigWinnerSlappedCount);
        Assert.DoesNotContain(sim.World.Messages.Entries, m => m.Kind == PlayerMessageKind.JackpotWinnerSlapped);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(500, 0, 0)]
    [InlineData(1000, 0, 0)]
    [InlineData(1500, 500, 500)]
    [InlineData(2500, 1500, 1000)]
    public void SpawnAmountIsCappedAgainAfterPurseDebit(int held, int remaining, int spawned)
    {
        var (sim, winner) = Start(held: held);
        var before = sim.Rng.SaveState();
        Slap(sim, winner);
        Assert.Equal(remaining, winner.GoldHeld);
        Assert.Equal(spawned, sim.World.Objects.Sum(o => o.GoldHeld));
        if (spawned == 0) Assert.Equal(before, sim.Rng.SaveState());
    }

    [Fact]
    public void SecondSlapMessagesAndChargesEveryOtherCreatureOfTheWinnerOwner()
    {
        var (sim, winner) = Start();
        CreatureState other = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId, 3, new TilePos(4, 3));
        CreatureState rival = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId, 4, new TilePos(5, 3));
        Slap(sim, winner);
        Assert.Equal(0, other.Anger[AngerBucket.Treatment]);
        Slap(sim, winner);
        Assert.Equal(625, winner.Anger[AngerBucket.Treatment]);
        Assert.Equal(5000, other.Anger[AngerBucket.Treatment]);
        Assert.Equal(0, rival.Anger[AngerBucket.Treatment]);
        Assert.Equal(2, winner.CasinoBigWinnerSlappedCount);
        Assert.Equal(4000, winner.JackpotWinnings);
        PlayerMessage message = Assert.Single(sim.World.Messages.Entries, m => m.Kind == PlayerMessageKind.JackpotWinnerSlapped);
        Assert.Equal((uint)0x4C, (uint)message.Kind);
        Assert.Equal((byte)3, message.PlayerId.Value);
        Assert.Equal(0, message.Data1);
        Assert.Equal(0, message.Data2);
    }

    [Fact]
    public void CounterWrapsAndIsHashedAndSaved()
    {
        var (sim, winner) = Start();
        winner.CasinoBigWinnerSlappedCount = 255;
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, [new HandSystem()]);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal(255, restored.World.CreatureById(winner.Id)!.CasinoBigWinnerSlappedCount);
        Slap(sim, winner);
        Assert.Equal(0, winner.CasinoBigWinnerSlappedCount);
        ulong before = sim.StateHash();
        winner.CasinoBigWinnerSlappedCount = 1;
        Assert.NotEqual(before, sim.StateHash());
    }

    [Fact]
    public void NoOutstandingDeadlineUsesOrdinarySlapEvenWithWinningsAndCounter()
    {
        var (sim, winner) = Start();
        winner.JackpotDeadlineTicks = 0;
        winner.CasinoBigWinnerSlappedCount = 1;
        Slap(sim, winner);
        Assert.Equal(10000, winner.GoldHeld);
        Assert.Equal(1, winner.CasinoBigWinnerSlappedCount);
        Assert.Empty(sim.World.Objects);
        Assert.Equal(400 + sim.Database.CreatureById(winner.CreatureTypeId).AngerSlap, winner.Anger[AngerBucket.Treatment]);
    }

    [Theory]
    [InlineData(76)]
    [InlineData(255)]
    public void EarlyReturnAndShortSlapArmsDoNotReachWinnerRoutine(int state)
    {
        var (sim, winner) = Start();
        StatePlacement.Place(winner, state);
        Slap(sim, winner);
        Assert.Equal(10000, winner.GoldHeld);
        Assert.Equal(0, winner.CasinoBigWinnerSlappedCount);
        Assert.Equal(400, winner.Anger[AngerBucket.Treatment]);
        Assert.Empty(sim.World.Objects);
    }

    [Fact]
    public void LandingUsesScatteredCandidatePositionInsteadOfTileCentre()
    {
        var (sim, winner) = Start(winnings: 3996);
        Slap(sim, winner);
        ObjectState scattered = Assert.Single(sim.World.Objects);
        // Strict landing bounds reject the exact edge; a cell-centre approximation would merge.
        scattered.PosX = winner.PosX & ~4095;
        scattered.PosY = (winner.PosY & ~4095) + 100;
        scattered.Cell = 0;
        ObjectState incoming = FloorGold.Drop(sim.World, sim.Database, scattered.TileIndex, 7)!;
        incoming.PosX = scattered.PosX + 100;
        incoming.PosY = scattered.PosY;
        var landing = new GameSimulation(sim.Database, sim.World, seed: 1, [new Land(incoming)]);
        landing.Tick();
        Assert.Equal(2, sim.World.Objects.Count);
        Assert.Equal(999, scattered.GoldHeld);
        Assert.Equal(7, incoming.GoldHeld);
    }

    private sealed class Land(ObjectState pile) : ISimulationSystem
    {
        public string Name => "gold landing test";
        public void Execute(SimulationContext context) => FloorGoldLandingRules.Land(context, pile);
    }
}
