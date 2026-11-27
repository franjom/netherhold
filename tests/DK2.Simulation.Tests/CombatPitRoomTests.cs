// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;
namespace DK2.Simulation.Tests;

public sealed class CombatPitRoomTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private sealed class DestructionProbe(CreatureState creature) : ISimulationSystem
    {
        public string Name
        {
            get
            {
                return "pit destruction probe";
            }
        }

        public void Execute(SimulationContext context)
        {
            CombatPitRoomStep.CreatureDestroyed(context, creature);
        }
    }
    private sealed class NeighbourProbe(CreatureState creature) : ISimulationSystem
    {
        public string Name
        {
            get
            {
                return "pit neighbour probe";
            }
        }

        public int Chosen { get; private set; } = -2;
        public void Execute(SimulationContext context)
        {
            RoomInstance pit = CombatPitRules.PitAt(context.World, context.Database, context.World.TileIndex(creature.Tile))!;
            Chosen = CombatPitRules.NeighbourStep(context, pit, creature, empty: true);
        }
    }
    private sealed class ResetProbe(RoomInstance pit) : ISimulationSystem
    {
        public string Name
        {
            get
            {
                return "pit reset probe";
            }
        }

        public void Execute(SimulationContext context)
        {
            CombatPitSystem.Reset(context, pit);
        }
    }
    private sealed class MutationProbe(Action<SimulationContext> mutate) : ISimulationSystem
    {
        public string Name
        {
            get
            {
                return "pit mutation probe";
            }
        }

        public void Execute(SimulationContext context)
        {
            mutate(context);
        }
    }
    private static (GameSimulation Sim, CreatureState First, CreatureState Second) Setup(bool effects = false, bool convertible = false, ushort? tickRate = null)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var db = new GameDatabase(basis.Terrain.Values.Select(t => convertible && t.TerrainId == Dk2TerrainFixture.CombatPitTerrain
                ? t with { Flags = t.Flags | TerrainFlags.Room | TerrainFlags.Attackable } : t),
            basis.Rooms.Values.Select(r => r.TypeId == RoomTypeId.CombatPit
                ? r with { RoomEffect421Id = effects ? (ushort)90 : (ushort)0, CreatedObjectEffectId = effects ? (ushort)52 : (ushort)0 } : r),
            basis.Creatures.Values, basis.Variables, basis.KeeperSpells.Values, objects: basis.Objects.Values, shots: basis.Shots.Values,
            level: tickRate is { } rate ? Level(rate) : null);
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(16, 16), [PlayerIds.Neutral, Keeper]);
        for (int y = 4; y <= 8; y++)
        {
            for (int x = 4; x <= 8; x++)
            {
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.CombatPit), Keeper);
            }
        }

        CreatureState first = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(6, 6));
        CreatureState second = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(6, 6));
        return (new GameSimulation(db, world, 42, [new CombatPitRoomStep()]), first, second);
    }

    private static LevelDef Level(ushort rate)
    {
        return new LevelDef([])
        {
            Name = "",
            Description = "",
            Author = "",
            Email = "",
            Information = "",
            Unknown_1184 = default,
            Messages = [],
            SoundCategory = "",
            TicksPerSecond = rate,
            PreviousRewards = default,
            NextRewards = default,
            IntroductionOverrideObjects = default,
            IntroductionOverrideTextIds = [],
            TerrainPath = "",
            Unknown_6403 = default,
            Paths = [],
            UnknownPathTail = [],
        };
    }

    [Fact]
    public void OnlyTheTransitionFromSeveralStandingToOneWithADownCreaturePostsMessageNine()
    {
        (GameSimulation sim, _, CreatureState second) = Setup();
        sim.Tick();
        Assert.Empty(sim.World.Messages.Entries);
        StatePlacement.Place(second, CreatureDeathRules.DyingState);
        sim.Tick();
        PlayerMessage message = Assert.Single(sim.World.Messages.Entries);
        Assert.Equal((PlayerMessageKind)9, message.Kind);
        sim.Tick();
        Assert.Single(sim.World.Messages.Entries);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ARemovedOrRimOrHeldCreatureDoesNotCountAsDownOnTheFloor(bool rim, bool held)
    {
        (GameSimulation sim, _, CreatureState second) = Setup();
        sim.Tick();
        if (rim) { second.PosX = CreatureState.CentreOf(4); second.PosY = CreatureState.CentreOf(4); }
        else if (held)
        {
            second.InHand = true;
        }
        else { second.PosX = CreatureState.CentreOf(1); second.PosY = CreatureState.CentreOf(1); }
        StatePlacement.Place(second, CreatureDeathRules.DyingState);
        sim.Tick();
        Assert.Empty(sim.World.Messages.Entries);
    }

    [Fact]
    public void ADownCreaturePresentOnTheFirstTurnDoesNotReportAnUnobservedFight()
    {
        (GameSimulation sim, _, CreatureState second) = Setup();
        StatePlacement.Place(second, CreatureDeathRules.DyingState);
        sim.Tick();
        Assert.Empty(sim.World.Messages.Entries);
    }

    [Fact]
    public void ReloadingBothDisabledEffectsStillConsumesTwoRandomDraws()
    {
        (GameSimulation sim, _, _) = Setup();
        var oracle = SimRandom.Restore(sim.Rng.SaveState());
        oracle.NextInt(SimulationTiming.OriginalTicksPerGameSecond * 4);
        oracle.NextInt(SimulationTiming.OriginalTicksPerGameSecond * 32);
        sim.Tick();
        Assert.Equal(oracle.SaveState(), sim.Rng.SaveState());
    }

    [Fact]
    public void ReloadsRequestTheTwoShippedEffectsAtFloorHeightWithNoSoundRequest()
    {
        (GameSimulation sim, _, _) = Setup(effects: true);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        sim.Tick();
#pragma warning disable IDE0305 // Preserve the original collection construction or overload.
        ViewEffectRequest[] requests = events.Events.Where(e => e.EffectRequest.HasValue).Select(e => e.EffectRequest!.Value).ToArray();
        Assert.Equal([new EffectId(90), new EffectId(52)], requests.Select(r => r.EffectId).ToArray());
#pragma warning restore IDE0305
        Assert.All(requests, r =>
        {
            Assert.Equal(new PlayerId(Keeper), r.Owner);
            Assert.Equal(FloorHeightRules.At(sim.World, sim.Database, r.Position.X.Raw, r.Position.Y.Raw), r.Position.Z.Raw);
            Assert.Equal(default, r.Heading);
        });
        Assert.DoesNotContain(events.Events, e => e.ThingSound.HasValue);
    }

    [Fact]
    public void DestructionRequestsTheSecondEffectAtTheCreaturePositionAndHeading()
    {
        (GameSimulation sim, CreatureState first, _) = Setup(effects: true);
        first.Heading = 0x345;
        first.PosZ = -0x500;
        var events = new ListEventSink();
        var destruction = new GameSimulation(sim.Database, sim.World, 42, [new DestructionProbe(first)]);
        destruction.ObserveWith(events);
        destruction.Tick();
        ViewEffectRequest effect = Assert.Single(events.Events, e => e.EffectRequest.HasValue).EffectRequest!.Value;
        Assert.Equal(new EffectId(52), effect.EffectId);
        Assert.Equal(new FpPosition3(new Fp32(first.PosX), new Fp32(first.PosY), new Fp32(first.PosZ + FloorHeightRules.Ordinary)), effect.Position);
        Assert.Equal(new Dk2Angle(0x345), effect.Heading);
    }

    private static void AddPit(GameSimulation sim, TilePos at, byte owner = Keeper)
    {
        byte terrain = sim.World.Tile(at).TerrainId;
        PlayerId previousOwner = sim.World.Tile(at).OwnerPlayerId;
        RoomRules.Place(sim.Database, ref sim.World.Tile(at), sim.Database.Room(RoomTypeId.CombatPit), owner);
        SlabChain.Changed(sim.World, sim.Database, sim.World.TileIndex(at), terrain, previousOwner);
    }

    private static void RemovePit(GameSimulation sim, TilePos at)
    {
        byte terrain = sim.World.Tile(at).TerrainId;
        PlayerId owner = sim.World.Tile(at).OwnerPlayerId;
        sim.World.Tile(at).TerrainId = Dk2TerrainFixture.ClaimedPath;
        SlabChain.Changed(sim.World, sim.Database, sim.World.TileIndex(at), terrain, owner);
    }

    private static void SeedRuntime(ref TileState tile, byte shortTicks, byte longTicks, byte previous, long stamp)
    {
        tile.PitAmbientShortTicks = shortTicks;
        tile.PitAmbientLongTicks = longTicks;
        tile.PitPreviousStandingCount = previous;
        tile.PitFightSeenTick = stamp;
    }

    private static void AssertRuntime(TileState tile, byte shortTicks, byte longTicks, byte previous, long stamp)
    {
        Assert.Equal((shortTicks, longTicks, previous, stamp),
            (tile.PitAmbientShortTicks, tile.PitAmbientLongTicks, tile.PitPreviousStandingCount, tile.PitFightSeenTick));
    }

    [Fact]
    public void AJoinedRoomKeepsItsRuntimeWhenItsLowestTileMovesAndAnotherPitIsAbsorbed()
    {
        (GameSimulation sim, _, _) = Setup();
        AddPit(sim, new TilePos(2, 4));
        SeedRuntime(ref sim.World.Tile(4, 4), 17, 29, 3, 99);
        SeedRuntime(ref sim.World.Tile(2, 4), 61, 62, 2, 88);
        AddPit(sim, new TilePos(3, 4));
        AssertRuntime(sim.World.Tile(2, 4), 17, 29, 3, 99);
        AssertRuntime(sim.World.Tile(4, 4), 0, 0, 0, 0);
    }

    [Fact]
    public void ANewPitAtANonzeroTurnStartsWithZeroCountersAndTheCurrentFightStamp()
    {
        (GameSimulation sim, _, _) = Setup();
        sim.Run(3);
        AddPit(sim, new TilePos(12, 12));
        AssertRuntime(sim.World.Tile(12, 12), 0, 0, 0, sim.World.CurrentTick.Value);
    }

    [Fact]
    public void LosingTheConnectingTileConstructsFreshRuntimeForBothPieces()
    {
        (GameSimulation sim, _, _) = Setup();
        sim.Run(3);
        foreach (int y in new[] { 4, 5, 7, 8 })
        {
            RemovePit(sim, new TilePos(6, y));
        }

        SeedRuntime(ref sim.World.Tile(4, 4), 17, 29, 3, 99);
        RemovePit(sim, new TilePos(6, 6));
        AssertRuntime(sim.World.Tile(4, 4), 0, 0, 0, sim.World.CurrentTick.Value);
        AssertRuntime(sim.World.Tile(7, 4), 0, 0, 0, sim.World.CurrentTick.Value);
        AssertRuntime(sim.World.Tile(6, 6), 0, 0, 0, 0);
    }

    [Fact]
    public void CaptureKeepsTheCapturedPitsRuntimeWhenItAbsorbsANewOwnersLowerTile()
    {
        (GameSimulation sim, _, _) = Setup();
        AddPit(sim, new TilePos(3, 4), PlayerIds.Neutral);
        SeedRuntime(ref sim.World.Tile(4, 4), 17, 29, 3, 99);
        SeedRuntime(ref sim.World.Tile(3, 4), 61, 62, 2, 88);
        RoomInstance pit = RoomIndex.RoomAt(sim.World, sim.Database, sim.World.TileIndex(new TilePos(4, 4)))!;
        Assert.Equal(RoomHealthOutcome.TookOver,
            RoomHealthRules.Apply(sim.World, sim.Database, pit, -1_000_000, PlayerIds.Neutral));
        AssertRuntime(sim.World.Tile(3, 4), 17, 29, 3, 99);
        AssertRuntime(sim.World.Tile(4, 4), 0, 0, 0, 0);
    }

    [Fact]
    public void ANeighbourWithAnotherRawNavigationByteIsRefusedEvenWhenItsLowClassBitsMatch()
    {
        (GameSimulation source, CreatureState seeker, _) = Setup();
        WorldState world = source.World;
        foreach (TilePos at in new[] { new TilePos(7, 6), new TilePos(6, 7), new TilePos(5, 6) })
        {
            world.SpawnCreature(source.Database, Dk2TerrainFixture.GoblinCreatureId, Keeper, at);
        }

        ThingProjection.Place(world, source.Database, [new DoorThing(new byte[20])
            { X = 6, Y = 5, PlayerId = Keeper, Flags = 0, DoorId = 1, Unknown_0011 = new byte[3] }]);
        byte[] bytes = WorldSteering.CaptureNavigationBytes(source.Database, world);
        byte here = bytes[world.TileIndex(seeker.Tile)], other = bytes[world.TileIndex(new TilePos(6, 5))];
        Assert.NotEqual(here, other);
        Assert.Equal(here & 3, other & 3);
        var probe = new NeighbourProbe(seeker);
        var sim = new GameSimulation(source.Database, world, 42, [probe]);
        sim.Tick();
        Assert.Equal(-1, probe.Chosen);
    }

    [Fact]
    public void RebuildingThePitAlsoThrowsOutAttachedSpectatorsWhoAreNotFighters()
    {
        (GameSimulation source, CreatureState spectator, _) = Setup();
        spectator.PosX = CreatureState.CentreOf(4);
        StatePlacement.Place(spectator, CombatPitSpectatorRules.ChoosingState);
        spectator.HeldPlace = new RoomPlace(source.World.TileIndex(spectator.Tile), 1);
        spectator.CombatPitFighter = false;
        RoomInstance pit = CombatPitRules.PitAt(source.World, source.Database, source.World.TileIndex(spectator.Tile))!;
        var sim = new GameSimulation(source.Database, source.World, 42, [new ResetProbe(pit)]);
        sim.Tick();
        Assert.Equal(CombatPitRules.ThrownOutState, spectator.Dk2StateId);
        Assert.False(spectator.HeldPlace.IsHeld);
    }

    [Fact]
    public void LiveSlabGrowthDispatchesThePitResetWithoutDependingOnABuildCommand()
    {
        (GameSimulation source, CreatureState fighter, _) = Setup();
        fighter.CombatPitFighter = true;
        StatePlacement.Place(fighter, CombatPitRules.WaitingState);
        var sim = new GameSimulation(source.Database, source.World, 42, [new MutationProbe(context =>
        {
            var at = new TilePos(3, 4);
            byte terrain = context.World.Tile(at).TerrainId;
            PlayerId owner = context.World.Tile(at).OwnerPlayerId;
            RoomRules.Place(context.Database, ref context.World.Tile(at), context.Database.Room(RoomTypeId.CombatPit), Keeper);
            SlabChain.Changed(context.World, context.Database, context.World.TileIndex(at), terrain, owner, context.Rng, context);
        })]);
        sim.Tick();
        Assert.Equal(CombatPitRules.ThrownOutState, fighter.Dk2StateId);
        Assert.False(fighter.CombatPitFighter);
    }

    [Fact]
    public void AnOwnershipChangeReleasesAttachedSpectatorsButDoesNotRunTheMapFighterReset()
    {
        (GameSimulation source, CreatureState spectator, CreatureState fighter) = Setup();
        spectator.PosX = CreatureState.CentreOf(4);
        StatePlacement.Place(spectator, CombatPitSpectatorRules.ChoosingState);
        spectator.HeldPlace = new RoomPlace(source.World.TileIndex(spectator.Tile), 1);
        fighter.CombatPitFighter = true;
        StatePlacement.Place(fighter, CombatPitRules.WaitingState);
        var sim = new GameSimulation(source.Database, source.World, 42, [new MutationProbe(context =>
        {
            RoomInstance pit = CombatPitRules.PitAt(context.World, context.Database, context.World.TileIndex(fighter.Tile))!;
            RoomHealthRules.Apply(context.World, context.Database, pit, -1_000_000, PlayerIds.Neutral, context);
        })]);
        sim.Tick();
        Assert.Equal(CombatPitRules.ThrownOutState, spectator.Dk2StateId);
        Assert.False(spectator.HeldPlace.IsHeld);
        Assert.True(fighter.CombatPitFighter);
        Assert.Equal(CombatPitRules.WaitingState, fighter.Dk2StateId);
    }

    [Fact]
    public void SellingTheTileUnderAnUnattachedFighterDoesNotScanItAsPartOfTheRemainingRoom()
    {
        (GameSimulation source, CreatureState fighter, _) = Setup();
        Dk2TerrainFixture.Sellable(source.World);
        StatePlacement.Place(fighter, CombatPitRules.WaitingState);
        fighter.CombatPitFighter = true;
        fighter.HeldPlace = RoomPlace.None;
        var sim = new GameSimulation(source.Database, source.World, 42, [new RoomSellSystem()]);
        sim.Tick(InputFrame.Of(new SellRoomCommand(Keeper, [fighter.Tile])));
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, source.World.Tile(fighter.Tile).TerrainId);
        Assert.True(fighter.CombatPitFighter);
        Assert.Equal(CombatPitRules.WaitingState, fighter.Dk2StateId);
    }

    [Fact]
    public void ConversionThatAbsorbsANeighbouringPitResetsTheMergedPitsMapFighters()
    {
        (GameSimulation source, CreatureState fighter, _) = Setup(convertible: true);
        for (int y = 4; y <= 8; y++)
        {
            for (int x = 4; x <= 8; x++)
            {
                source.World.Tile(x, y).OwnerPlayerId = PlayerIds.Neutral;
            }
        }

        AddPit(source, new TilePos(3, 4));
        fighter.PosX = CreatureState.CentreOf(3);
        fighter.PosY = CreatureState.CentreOf(4);
        StatePlacement.Place(fighter, CombatPitRules.WaitingState);
        fighter.CombatPitFighter = true;
        fighter.HeldPlace = RoomPlace.None;
        RoomInstance captured = CombatPitRules.PitAt(source.World, source.Database, source.World.TileIndex(new TilePos(4, 4)))!;
        RoomHealthRules.Set(source.World, source.Database, captured, 1);
        var sim = new GameSimulation(source.Database, source.World, 42, [new MutationProbe(context =>
            Assert.Equal(TileWorkOutcome.Transformed, RoomHealthRules.Convert(context.World, context.Database,
                new TilePos(4, 4), Keeper, context: context).Outcome))]);
        sim.Tick();
        Assert.False(fighter.CombatPitFighter);
        Assert.Equal(CombatPitRules.ThrownOutState, fighter.Dk2StateId);
    }
    [Fact]
    public void PositiveEffectCountdownsDecrementWithoutDrawingRandomness()
    {
        (GameSimulation sim, _, _) = Setup();
        int anchor = CombatPitRules.PitAt(sim.World, sim.Database, sim.World.TileIndex(new TilePos(6, 6)))!.TileIndices[0];
        sim.World.TileAtIndex(anchor).PitAmbientShortTicks = 3;
        sim.World.TileAtIndex(anchor).PitAmbientLongTicks = 2;
        (ulong S0, ulong S1, ulong S2, ulong S3) before = sim.Rng.SaveState();
        sim.Tick();
        Assert.Equal(before, sim.Rng.SaveState());
        Assert.Equal((2, 1), ((int)sim.World.TileAtIndex(anchor).PitAmbientShortTicks, (int)sim.World.TileAtIndex(anchor).PitAmbientLongTicks));
    }

    [Theory]
    [InlineData(256, false)]
    [InlineData(258, true)]
    public void ThePreviousStandingCountRetainsOnlyItsNativeByte(int standing, bool announces)
    {
        (GameSimulation sim, _, _) = Setup();
        for (int i = 2; i < standing; i++)
        {
            sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(6, 6));
        }

        sim.Tick();
        foreach (CreatureState creature in sim.World.Creatures.Skip(1))
        {
            StatePlacement.Place(creature, CreatureDeathRules.DyingState);
        }

        sim.Tick();
        Assert.Equal(announces, sim.World.Messages.Entries.Any(m => m.Kind == PlayerMessageKind.CombatPitFightOver));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EffectReloadsUseTheLevelTickRateAndThenItsVariablesOverride(bool overrides)
    {
        (GameSimulation sim, _, _) = Setup(tickRate: 3);
        if (overrides)
        {
            sim.Database.Variables.Misc[VariableTypeId.TicksPerSecond] = new MiscVariable(VariableTypeId.TicksPerSecond, 7, 0, 0);
        }

        var oracle = SimRandom.Restore(sim.Rng.SaveState());
        int rate = overrides ? 7 : 3;
        byte expectedShort = unchecked((byte)oracle.NextInt(rate * 4));
        byte expectedLong = unchecked((byte)oracle.NextInt(rate * 32));
        sim.Tick();
        RoomInstance pit = CombatPitRules.PitAt(sim.World, sim.Database, sim.World.TileIndex(new TilePos(6, 6)))!;
        ref TileState tile = ref sim.World.TileAtIndex(pit.TileIndices[0]);
        Assert.Equal((expectedShort, expectedLong), (tile.PitAmbientShortTicks, tile.PitAmbientLongTicks));
    }

    [Fact]
    public void ZeroUnitFighterClaimsAreReleasedInNativeHeadInsertionOrder()
    {
        (GameSimulation source, CreatureState first, CreatureState second) = Setup();
        var sim = new GameSimulation(source.Database, source.World, 42, [new MutationProbe(context =>
        {
            first.CombatPitFighter = second.CombatPitFighter = true;
            Assert.True(CreatureStateMachine.TrySetState(context.Hooks, first, CombatPitRules.PlacingState));
            Assert.True(CreatureStateMachine.TrySetState(context.Hooks, second, CombatPitRules.PlacingState));
            Assert.True(first.HeldPlace.IsHeld);
            Assert.True(second.HeldPlace.IsHeld);
            RoomInstance pit = CombatPitRules.PitAt(context.World, context.Database, context.World.TileIndex(first.Tile))!;
            RoomHealthRules.Apply(context.World, context.Database, pit, -1_000_000, PlayerIds.Neutral, context);
        })]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        sim.Tick();
        string[] released = [.. events.Events.Where(e => e.Kind == "pit_thrown_out").Select(e => e.Detail)];
        Assert.Equal(2, released.Length);
        Assert.StartsWith($"creature={second.Id} ", released[0]);
        Assert.StartsWith($"creature={first.Id} ", released[1]);
    }

}
