// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class OneShotReaperTests
{
    private const byte Keeper = 3, SpellId = 14;

    private static (GameDatabase Db, WorldState World, TilePos At) Setup(int durationSeconds = 99, byte level = 3, bool metadata = true)
    {
        var (basis, world, at) = SummonHornyTests.Setup();
        KeeperSpellDef spell = basis.KeeperSpellById(5)! with
        {
            Name = "OneShotReaper", KeeperSpellId = SpellId,
            ShotData1 = durationSeconds, ManaCost = 0, ManaUpkeepPerSecond = 125,
        };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values, basis.Variables,
            basis.KeeperSpells.Values.Where(s => s.KeeperSpellId != SpellId).Append(spell),
            objects: basis.Objects.Values, shots: basis.Shots.Values,
            level: metadata ? new LevelDef([])
            {
                Name = "fixture", Description = "", Author = "", Email = "", Information = "",
                Unknown_1184 = ReadOnlyMemory<byte>.Empty, Messages = [], SoundCategory = "",
                PreviousRewards = ReadOnlyMemory<byte>.Empty, NextRewards = ReadOnlyMemory<byte>.Empty,
                IntroductionOverrideObjects = ReadOnlyMemory<byte>.Empty, IntroductionOverrideTextIds = [],
                TerrainPath = "", Unknown_6403 = new byte[] { level }, Paths = [], UnknownPathTail = [],
            } : null);
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, SpellId, 3));
        return (db, world, at);
    }

    [Fact]
    public void CastCreatesTheTimedReaperForTheCaster_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, SpellId,
            CreatureState.CentreOf(at.X), CreatureState.CentreOf(at.Y))));
        CreatureState reaper = Assert.Single(world.Creatures);
        Assert.Equal(Keeper, reaper.OwnerPlayerId);
        Assert.Equal(235, reaper.Dk2StateId);
        Assert.Equal(396, reaper.ReaperTicks);
        Assert.True(reaper.IsOneShotReaper);
        Assert.Equal(3, reaper.Level);
        Assert.True(world.Player(Keeper).OneShotReaperUsed);
        Assert.Equal(200000, world.Player(Keeper).Mana);
        Assert.Equal(0, world.Player(Keeper).OneShotManaSpent);
    }

    private static CastSpellAtPlaceCommand Cast(TilePos at, byte spell = SpellId) =>
        new(Keeper, spell, CreatureState.CentreOf(at.X), CreatureState.CentreOf(at.Y));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(255, 10)]
    public void LevelComesFromTheLevelRecordAndUsesOrdinaryLevelGrants_ConfirmedFromBinary(byte selected, int expected)
    {
        var (db, world, at) = Setup(level: selected);
        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]).Tick(InputFrame.Of(Cast(at)));
        Assert.Equal(expected, Assert.Single(world.Creatures).Level);
    }

    [Theory]
    [InlineData(99, 396, true)]
    [InlineData(16384, 0, true)]
    [InlineData(65536, 0, true)]
    [InlineData(-1, 65532, true)]
    [InlineData(0, 0, false)]
    public void NonzeroDwordSelectsTheVariantAndDurationMultipliesAsAWord_ConfirmedFromBinary(int seconds, int ticks, bool variant)
    {
        var (db, world, at) = Setup(seconds);
        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]).Tick(InputFrame.Of(Cast(at)));
        CreatureState reaper = Assert.Single(world.Creatures);
        Assert.Equal(ticks, reaper.ReaperTicks);
        Assert.Equal(variant, reaper.IsOneShotReaper);
        Assert.Equal(variant ? 3 : 10, reaper.Level);
    }

    [Fact]
    public void ExpiryRunsDepartureAgeOneOnTheSameTurnAndRemovalThirtyUpdatesLater_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        var sim = new GameSimulation(db, world, 1,
            [new CreatureStatusSystem(), new ReaperSystem(), new ReaperRemovalSystem(), new KeeperSpellSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(Cast(at)));
        CreatureState reaper = Assert.Single(world.Creatures);
        sim.Run(395);
        Assert.Equal(1, reaper.ReaperTicks);
        Assert.Equal(238, reaper.Dk2StateId);
        int health = reaper.Health;
        sim.Tick();
        Assert.Equal(0, reaper.ReaperTicks);
        Assert.Equal(242, reaper.Dk2StateId);
        Assert.Equal(1u, reaper.ReaperStateAgeTicks);
        Assert.Equal(health, reaper.Health);
        Assert.False(reaper.ReaperCanAcquireFight);
        Assert.False(reaper.ReaperIsAttackable);
        sim.Run(29);
        Assert.Single(world.Creatures);
        sim.Tick();
        Assert.Empty(world.Creatures);
        sim.Tick(InputFrame.Of(Cast(at)));
        Assert.Empty(world.Creatures);
        Assert.True(world.Player(Keeper).OneShotReaperUsed);
    }

    [Theory]
    [InlineData(PlayerStatus.Playing)]
    [InlineData(PlayerStatus.Won)]
    [InlineData(PlayerStatus.Conquered)]
    [InlineData(PlayerStatus.Lost)]
    public void OwnerResultDoesNotPauseTheReaperCountdown_ConfirmedFromBinary(byte status)
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, 201, Keeper, at);
        reaper.Dk2StateId = 238;
        reaper.ReaperTicks = 2;
        world.Player(Keeper).Status = status;
        var sim = new GameSimulation(db, world, 1, [new CreatureStatusSystem()]);
        sim.Run(2);
        Assert.Equal(0, reaper.ReaperTicks);
        Assert.Equal(242, reaper.Dk2StateId);
        Assert.False(reaper.IsOneShotReaper);
    }

    [Fact]
    public void HoldingCanRefuseExpiryStateButStillSpendsTimerAndClearsCombat_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, 201, Keeper, at);
        reaper.Dk2StateId = 76;
        reaper.InHand = true;
        reaper.ReaperTicks = 1;
        new GameSimulation(db, world, 1, [new CreatureStatusSystem()]).Tick();
        Assert.Equal(0, reaper.ReaperTicks);
        Assert.Equal(76, reaper.Dk2StateId);
        Assert.False(reaper.ReaperCanAcquireFight);
        Assert.False(reaper.ReaperIsAttackable);
    }

    [Theory]
    [InlineData(0, 238, 0)]
    [InlineData(2, -1, 2)]
    public void ZeroTimerAndNonliveCreatureDoNotEnterDeparture_ConfirmedFromBinary(ushort timer, int state, ushort expected)
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, 201, Keeper, at);
        reaper.Dk2StateId = state;
        reaper.ReaperTicks = timer;
        new GameSimulation(db, world, 1, [new CreatureStatusSystem()]).Tick();
        Assert.Equal(expected, reaper.ReaperTicks);
        Assert.Equal(state, reaper.Dk2StateId);
    }

    [Fact]
    public void OneShotCanCoexistWithNormalReaperAndFirstOwnedVariantSuppressesUpkeep_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        CreatureState normal = world.SpawnCreature(db, 201, Keeper, at);
        normal.Dk2StateId = 238;
        new GameSimulation(db, world, 1, []).Run(1000);
        Assert.Equal(2000, ManaRules.UpkeepPerSecondByPlayerIndex(world, db).Sum());
        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]).Tick(InputFrame.Of(Cast(at)));
        Assert.Equal(2, world.Creatures.Count);
        Assert.Equal(0, ManaRules.UpkeepPerSecondByPlayerIndex(world, db).Sum());
        var reaper = world.Creatures.Single(c => c.IsOneShotReaper);
        world.RemoveCreature(reaper.Id);
        Assert.Equal(2000, ManaRules.UpkeepPerSecondByPlayerIndex(world, db).Sum());
    }

    [Fact]
    public void CreaturePoolFailureStillSpendsTheOneUse_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        for (int i = 0; i < 448; i++) world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, at);
        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]).Tick(InputFrame.Of(Cast(at)));
        Assert.Equal(448, world.Creatures.Count);
        Assert.True(world.Player(Keeper).OneShotReaperUsed);
    }

    [Fact]
    public void MissingLevelMetadataRefusesInsteadOfGuessingTheReaperLevel()
    {
        var (db, world, at) = Setup(metadata: false);
        new GameSimulation(db, world, 1, [new CallbackSystem(context =>
            Assert.Equal(KeeperSpellRefusal.MissingLevelDefinition, KeeperSpellRules.CanCastAtPlace(context, Cast(at))))]).Tick();
        Assert.False(world.Player(Keeper).OneShotReaperUsed);
    }

    [Fact]
    public void UsedFlagAffectsHashAndSnapshotPreservesCountdownAndRefusalAfterRemoval()
    {
        var (db, world, at) = Setup();
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        ulong before = sim.StateHash();
        world.Player(Keeper).OneShotReaperUsed = true;
        Assert.NotEqual(before, sim.StateHash());
        world.Player(Keeper).OneShotReaperUsed = false;
        sim.Tick(InputFrame.Of(Cast(at)));
        sim = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db,
            [new CreatureStatusSystem(), new ReaperSystem(), new ReaperRemovalSystem()]);
        sim.Run(390);
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db,
            [new CreatureStatusSystem(), new ReaperSystem(), new ReaperRemovalSystem()]);
        sim.Run(40);
        restored.Run(40);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Empty(restored.World.Creatures);
        Assert.True(restored.World.Player(Keeper).OneShotReaperUsed);
    }

    [Fact]
    public void InvalidLocationDoesNotSpendTheUseAndSuccessfulCastStampsBothClocks_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(Cast(new TilePos(-1, -1))));
        Assert.False(world.Player(Keeper).OneShotReaperUsed);
        Assert.Empty(world.Creatures);
        sim.Tick();
        sim.Tick(InputFrame.Of(Cast(at)));
        AvailabilityState row = world.Player(Keeper).Availabilities.Single(a => a.TypeId == SpellId);
        Assert.Equal(2, row.LastCastTick);
        Assert.Equal(2, row.LastCheckedTick);
        sim.Tick(InputFrame.Of(Cast(at)));
        Assert.Single(world.Creatures);
        Assert.Equal(200000, world.Player(Keeper).Mana);
        Assert.Equal(2, world.Player(Keeper).Availabilities.Single(a => a.TypeId == SpellId).LastCastTick);
    }

    [Fact]
    public void DownCreatureStillSpendsItsPrefixTimer_ConfirmedFromBinary()
    {
        var (db, world, at) = Setup();
        CreatureState reaper = world.SpawnCreature(db, 201, Keeper, at);
        reaper.Dk2StateId = 238;
        reaper.Health = 0;
        reaper.ReaperTicks = 1;
        new GameSimulation(db, world, 1, [new CreatureStatusSystem()]).Tick();
        Assert.Equal(0, reaper.ReaperTicks);
        Assert.Equal(242, reaper.Dk2StateId);
        Assert.Equal(0, reaper.Health);
    }

    private sealed class CallbackSystem(Action<SimulationContext> execute) : ISimulationSystem
    {
        public string Name => "oneshot-test";
        public void Execute(SimulationContext context) => execute(context);
    }
}
