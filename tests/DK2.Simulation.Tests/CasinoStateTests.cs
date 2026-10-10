// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Casino's states 145–153, the jackpot and its winner's 152 and 155–157
/// (casino.md, ADR-0396).</summary>
public class CasinoStateTests
{
    private const byte Keeper = 4;
    private const byte GoblinId = 7;
    private static readonly TilePos Vault = new(8, 3);

    /// <summary>The fixture with a Goblin that has art in the slots the Casino's actions play: a clip
    /// of 20 frames at 20 a second, one game second a play.</summary>
    private static GameDatabase Db(bool art = true)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var clip = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 20, FramesPerSecond = 20 };
        var slots = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        foreach (int slot in new[] { 1, 8, 9, 14, 19, 20, 28, 34, 37 }) slots[slot] = clip;
        TerrainDef wall = basis.TerrainById(Dk2TerrainFixture.ReinforcedWall);
        wall = wall with { Flags = wall.Flags | TerrainFlags.AllowRoomWalls };   // so a Casino gets wall furniture
        return new GameDatabase(basis.Terrain.Values.Select(t => t.TerrainId == wall.TerrainId ? wall : t), basis.Rooms.Values,
            basis.Creatures.Values.Select(c => art && c.CreatureId == GoblinId ? c with { ArtSlots = slots } : c),
            basis.Variables, objects: basis.Objects.Values, shots: basis.Shots.Values);
    }

    /// <summary>A square Casino of the keeper's from (1,1), and a Treasury tile at <see cref="Vault"/>.</summary>
    private static WorldState World(GameDatabase db, int size)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y <= size; y++)
        for (int x = 1; x <= size; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Casino), Keeper);
        return world;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world, params ISimulationSystem[] extra) =>
        new(db, world, seed: 42,
        [
            new AutoFurnitureSystem(), new CasinoSystem(), new CreatureMovementSystem(), new CasinoJackpotSystem(),
            new CreatureJobSystem(), .. extra,
        ]);

    private static CreatureState Gambler(GameDatabase db, WorldState world, TilePos at, int purse = 1000)
    {
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at);
        goblin.CurrentJob = JobType.Drink;
        StatePlacement.As(goblin, CreatureActivity.AtWork);
        StatePlacement.Place(goblin, CasinoStep.ChoosingState);
        goblin.HeldPlace = new RoomPlace((at.Y * world.Width) + at.X, RoomCapacityRules.Units);   // the enter hook's claim
        goblin.GoldHeld = purse;
        return goblin;
    }

    private static RoomInstance Casino(GameDatabase db, WorldState world) =>
        RoomIndex.Build(world, db).Single(r => r.Definition.TypeId == RoomTypeId.Casino);

    /// <summary>Runs and gathers the states the creature is seen in, a walk named by its resume.</summary>
    private static HashSet<int> Watch(GameSimulation sim, CreatureState creature, int ticks)
    {
        var seen = new HashSet<int>();
        for (int i = 0; i < ticks; i++)
        {
            sim.Tick();
            seen.Add(CreatureStateTable.Get(creature.Dk2StateId).IsUtility ? creature.UtilityNewState : creature.Dk2StateId);
        }

        return seen;
    }

    // ---------------------------------------------------------------------------- 145

    [Fact]
    public void AGamblerInAFurnishedCasinoWalksToATableOrAnAlcoveFacesItPlaysAndChoosesAgain()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        GameSimulation sim = Simulation(db, world);
        sim.Run(1);
        Assert.Contains(world.Objects, o => o.ObjectTypeId is 87 or 59 or 67);
        CreatureState goblin = Gambler(db, world, new TilePos(3, 2));

        HashSet<int> seen = Watch(sim, goblin, 40 * SimulationTiming.TicksPerSecond);

        Assert.True(seen.Contains(CasinoStep.AtTheTableState) || seen.Contains(CasinoStep.AtTheAlcoveState));
        Assert.True(seen.Contains(CasinoStep.PlayingState) || seen.Contains(CasinoStep.DrinkingState));
        Assert.Contains(CasinoStep.ChoosingState, seen);
        Assert.Equal(JobType.Drink, goblin.CurrentJob);
    }

    [Fact]
    public void WithOnlyAnAlcoveFreeAGamblerStandsInFrontOfItFacesItDrinksAndChoosesAgain()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        // A north wall: DK2 stands a drinker at 45-135 degrees, south of the alcove, so only there is it in the room.
        for (int i = 1; i <= 5; i++) world.Tile(i, 0).TerrainId = Dk2TerrainFixture.ReinforcedWall;
        GameSimulation sim = Simulation(db, world);
        sim.Run(1);
        RoomInstance room = Casino(db, world);
        foreach (ObjectState table in world.Objects.Where(o => o.ObjectTypeId is 87 or 59 or 67 or 68).ToList())
            world.RemoveObject(table.Id);
        int slot = room.TileIndices.First(t => AutoFurnitureRules.WallSide(world, db, room, t) == 0);
        ObjectState alcove = world.SpawnObject(db, CasinoStep.AlcoveObjectId, Keeper, slot);
        alcove.AutoPlaced = true;
        CreatureState goblin = Gambler(db, world, new TilePos(3, 3));
        sim = new GameSimulation(db, world, seed: 42,
            [new CasinoSystem(), new CreatureMovementSystem(), new CasinoJackpotSystem(), new CreatureJobSystem()]);

        HashSet<int> seen = Watch(sim, goblin, 30 * SimulationTiming.TicksPerSecond);

        Assert.Contains(CasinoStep.AtTheAlcoveState, seen);
        Assert.Contains(CasinoStep.DrinkingState, seen);
        Assert.Contains(CasinoStep.ChoosingState, seen);
        // The alcove's own position is 0x700 toward its wall, not its tile's centre.
        (int x, int y) = CasinoStep.FurniturePosition(world, db, room, alcove);
        Assert.NotEqual((CreatureState.CentreOf(slot % world.Width), CreatureState.CentreOf(slot / world.Width)), (x, y));
    }

    [Fact]
    public void AnAlcoveOnASouthWallPutsItsDrinkersSpotInTheWallSoTheChooserTriesAgain_UnwatchedQ572()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        for (int i = 1; i <= 5; i++) world.Tile(i, 6).TerrainId = Dk2TerrainFixture.ReinforcedWall;
        new GameSimulation(db, world, seed: 42, [new AutoFurnitureSystem()]).Tick();
        RoomInstance room = Casino(db, world);
        foreach (ObjectState table in world.Objects.Where(o => o.ObjectTypeId is 87 or 59 or 67 or 68).ToList())
            world.RemoveObject(table.Id);
        world.SpawnObject(db, CasinoStep.AlcoveObjectId, Keeper,
            room.TileIndices.First(t => AutoFurnitureRules.WallSide(world, db, room, t) == 2)).AutoPlaced = true;
        CreatureState goblin = Gambler(db, world, new TilePos(3, 3));
        var sim = new GameSimulation(db, world, seed: 42,
            [new CasinoSystem(), new CreatureMovementSystem(), new CasinoJackpotSystem(), new CreatureJobSystem()]);

        HashSet<int> seen = Watch(sim, goblin, 10 * SimulationTiming.TicksPerSecond);

        Assert.DoesNotContain(CasinoStep.AtTheAlcoveState, seen);
        Assert.Equal(CasinoStep.ChoosingState, goblin.Dk2StateId);
        Assert.Equal(JobType.Drink, goblin.CurrentJob);
    }

    [Fact]
    public void InACasinoWithOnlyItsLeverAGamblerTakesABareTilePlaysActionSixtyEightOnceAndChoosesAgain()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        GameSimulation sim = Simulation(db, world);
        sim.Run(1);
        CreatureState goblin = Gambler(db, world, new TilePos(1, 1));

        HashSet<int> seen = Watch(sim, goblin, 20 * SimulationTiming.TicksPerSecond);

        Assert.Contains(CasinoStep.OnTheFloorState, seen);
        Assert.Contains(CasinoStep.StandingState, seen);
        Assert.DoesNotContain(CasinoStep.AtTheTableState, seen);
        Assert.Equal(JobType.Drink, goblin.CurrentJob);
    }

    [Fact]
    public void TheChooserSendsAGamblerIdleInACasinoWithNoLever()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 1);
        GameSimulation sim = Simulation(db, world);
        CreatureState goblin = Gambler(db, world, new TilePos(1, 1));

        sim.Run(2);

        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.Equal(JobType.None, goblin.CurrentJob);
    }

    [Fact]
    public void TheChooserSendsAGamblerIdleOnItsOwnPurseTestBeforeTheHookActs()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        GameSimulation sim = Simulation(db, world);
        sim.Run(1);
        CreatureState goblin = Gambler(db, world, new TilePos(1, 1), purse: 8);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(2);

        Assert.Equal(CreatureActivity.Idle, goblin.Activity);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "gambled");
    }

    [Fact]
    public void WithAPartyOnTheChooserWalksToItsOwnSpotAndDances()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        GameSimulation sim = Simulation(db, world);
        sim.Run(1);
        world.TileAtIndex(Casino(db, world).TileIndices[0]).CasinoParty = CasinoJackpotRules.PartyBit;
        world.Player(Keeper).CasinoParties = 1;
        world.TileAtIndex(Casino(db, world).TileIndices[0]).CasinoPartyStartTick = 1;
        CreatureState goblin = Gambler(db, world, new TilePos(1, 1));

        HashSet<int> seen = Watch(sim, goblin, 3);

        Assert.Contains(CasinoStep.DancingState, seen);
        Assert.True(goblin.ActionUntilTick > sim.World.CurrentTick.Value);
    }

    // ------------------------------------------------------------------------- 148's choice

    [Theory]
    [InlineData(true, true, true, false, 49, 65)]
    [InlineData(true, true, true, false, 50, 66)]
    [InlineData(true, true, true, true, 74, 67)]
    [InlineData(true, true, true, false, 75, 68)]
    [InlineData(false, true, true, false, 0, 66)]
    [InlineData(false, true, true, true, 75, 68)]
    public void TheTableActionIsChosenByTheArtAndARollOfAHundred(bool a, bool b, bool d, bool stingy, int roll, int action) =>
        Assert.Equal(action, CasinoStep.TableAction(a, b, d, stingy, () => roll));

    [Theory]
    [InlineData(true, false, true, 68)]
    [InlineData(true, true, false, 0)]
    [InlineData(false, false, false, 0)]
    public void WithoutTheSettingsArtOrArtTwentyThereIsNoRollAndActionZeroMeansNone(bool a, bool b, bool d, int action) =>
        Assert.Equal(action, CasinoStep.TableAction(a, b, d, stingy: false, () => throw new InvalidOperationException("no roll")));

    // ----------------------------------------------------------------------- art and timing

    [Theory]
    [InlineData(19, 9)] [InlineData(28, 8)] [InlineData(34, 1)] [InlineData(37, 1)]
    [InlineData(8, 0)] [InlineData(14, 5)] [InlineData(20, 1)] [InlineData(24, 4)]
    public void AMissingArtSlotFallsBackByTheBinarysSwitch(int slot, int fallback)
    {
        CreatureDef bare = Db(art: false).CreatureById(GoblinId);
        Assert.Equal(fallback, AnimationTimingRules.ResolveArtSlot(bare, slot));
        CreatureDef dressed = Db().CreatureById(GoblinId);
        if (AnimationTimingRules.HasArt(dressed, slot)) Assert.Equal(slot, AnimationTimingRules.ResolveArtSlot(dressed, slot));
    }

    [Fact]
    public void ACountedActionPlaysCountPlusOneTimes_LikelyFromTheRenderersCycleFlags()
    {
        CreatureDef goblin = Db().CreatureById(GoblinId);
        // One play of 20 frames at 20 a second is four turns; count 3 is four plays, count 64 sixty-five.
        Assert.Equal(16, AnimationTimingRules.CountedHoldTicks(goblin, 19, 3));
        Assert.Equal(260, AnimationTimingRules.CountedHoldTicks(goblin, 28, 64));
        Assert.Equal(AnimationTimingRules.ActionHoldTicks(goblin.ArtSlot(20)), AnimationTimingRules.CountedHoldTicks(goblin, 20, 1));
    }

    // ------------------------------------------------------------------------ the jackpot

    [Fact]
    public void TheJackpotSendsTheOtherGamblersDancingStartsThePartyAndSendsTheWinnerToCelebrate()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        new GameSimulation(db, world, seed: 1, [new AutoFurnitureSystem()]).Tick();
        CreatureState winner = Gambler(db, world, new TilePos(1, 1));
        CreatureState other = Gambler(db, world, new TilePos(3, 3));
        bool rolled = false;
        var sim = new GameSimulation(db, world, seed: 1, [new Probe(context =>
        {
            if (!rolled) CasinoJackpotRules.Jackpot(context, "Test", winner);
            rolled = true;
        })]);

        sim.Tick();

        Assert.Equal(CasinoStep.DancingState, other.Dk2StateId);
        Assert.Equal(JobType.Drink, other.CurrentJob);
        Assert.Equal(CasinoJackpotRules.CelebratingState, winner.Dk2StateId);
        Assert.Equal(CreatureActivity.CollectingWinnings, winner.Activity);
        Assert.False(winner.HeldPlace.IsHeld);
        Assert.Equal(1, world.Player(Keeper).CasinoParties);
        Assert.True(CasinoJackpotRules.IsPartyOn(world, Casino(db, world)));
        Assert.Equal(240 * SimulationTiming.OriginalTicksPerGameSecond, winner.JackpotDeadlineTicks);
        Assert.Equal(Math.Max(1000, CasinoJackpotRules.WageBill(world, db, Keeper)), winner.JackpotWinnings);
        Assert.Contains(world.Messages.Entries, m => m.Kind == PlayerMessageKind.CasinoJackpot && m.Data1 == winner.Id);
    }

    [Fact]
    public void ASecondJackpotIsNothingWhileAWinnerOfTheKeepersStillHasADeadline()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        CreatureState winner = Gambler(db, world, new TilePos(1, 1));
        CreatureState earlier = Gambler(db, world, new TilePos(3, 3));
        earlier.JackpotDeadlineTicks = 5;
        bool rolled = false;
        var sim = new GameSimulation(db, world, seed: 1, [new Probe(context =>
        {
            if (!rolled) CasinoJackpotRules.Jackpot(context, "Test", winner);
            rolled = true;
        })]);

        sim.Tick();

        Assert.Equal(CasinoStep.ChoosingState, winner.Dk2StateId);
        Assert.Equal(0, world.Player(Keeper).CasinoParties);
    }

    [Fact]
    public void TheWinnerCelebratesTenSecondsThenWalksToTheTreasuryAndTakesItsWinnings()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        for (int x = Vault.X; x <= Vault.X + 1; x++)
        {
            RoomRules.Place(db, ref world.Tile(x, Vault.Y), db.Room(RoomTypeId.Treasury), Keeper);
            world.Tile(x, Vault.Y).GoldHeld = 1500;
        }
        for (int x = 4; x < Vault.X; x++)
            world.Tile(x, Vault.Y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        for (int y = Vault.Y; y >= 1; y--) world.Tile(4, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
        CreatureState winner = Gambler(db, world, new TilePos(3, 2));
        GameSimulation sim = Simulation(db, world, new Probe(context =>
        {
            if (context.Tick.Value == 1) CasinoJackpotRules.Jackpot(context, "Test", winner);
        }));

        HashSet<int> seen = Watch(sim, winner, 60 * SimulationTiming.TicksPerSecond);

        Assert.Contains(CasinoJackpotRules.CelebratingState, seen);
        Assert.Contains(CasinoJackpotRules.SeekingTheGoldState, seen);
        Assert.Contains(CasinoJackpotRules.ArrivedState, seen);
        Assert.Contains(CasinoJackpotRules.CollectingState, seen);
        Assert.Equal(0, winner.JackpotDeadlineTicks);
        Assert.Equal(3000 - 1000, world.Tile(Vault.X, Vault.Y).GoldHeld + world.Tile(Vault.X + 1, Vault.Y).GoldHeld);
    }

    [Fact]
    public void AMissedDeadlineRaisesEveryKeeperCreaturesTreatmentByVariable208PercentAndPosts91()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        CreatureState winner = Gambler(db, world, new TilePos(1, 1));
        CreatureState other = Gambler(db, world, new TilePos(3, 3));
        winner.JackpotDeadlineTicks = 1;
        winner.Anger.Treatment = 300;
        other.Anger.Treatment = 1000;
        var sim = new GameSimulation(db, world, seed: 1, [new Probe(context =>
            CasinoJackpotRules.CountDown(context, "Test", winner))]);

        sim.Tick();

        Assert.Equal(600, winner.Anger.Treatment);
        Assert.Equal(2000, other.Anger.Treatment);
        Assert.Contains(world.Messages.Entries, m => m.Kind == PlayerMessageKind.JackpotMissed);
    }

    [Fact]
    public void APartyLiftsThePurseTestOfTheDrinkJobAndOfTheCasinosEnterHook()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        new GameSimulation(db, world, seed: 1, [new AutoFurnitureSystem()]).Tick();
        CreatureState broke = Gambler(db, world, new TilePos(1, 1), purse: 0);
        CreatureDef definition = db.CreatureById(GoblinId);
        Assert.NotNull(JobRules.Refused(world, db, definition, broke, JobType.Drink));

        world.Player(Keeper).CasinoParties = 1;

        Assert.Null(JobRules.Refused(world, db, definition, broke, JobType.Drink));
        Assert.Null(CasinoRules.EnterRefusal(world, db, broke));
    }

    // ------------------------------------------------------------------------ the party

    [Fact]
    public void APartyEndsAfterNinetySecondsAndItsDancersGoBackToTheChooser()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        CreatureState dancer = Gambler(db, world, new TilePos(1, 1));
        StatePlacement.Place(dancer, CasinoStep.DancingState);
        dancer.ActionUntilTick = long.MaxValue;
        world.TileAtIndex(Casino(db, world).TileIndices[0]).CasinoParty = CasinoJackpotRules.PartyBit;
        world.Player(Keeper).CasinoParties = 1;
        var sim = new GameSimulation(db, world, seed: 1, [new CasinoSystem()]);

        sim.Run(90 * SimulationTiming.OriginalTicksPerGameSecond + 1);
        Assert.Equal(CasinoStep.DancingState, dancer.Dk2StateId);
        sim.Run(2);

        Assert.Equal(0, world.Player(Keeper).CasinoParties);
        Assert.Equal(CasinoStep.ChoosingState, dancer.Dk2StateId);
        Assert.Equal(0, world.TileAtIndex(Casino(db, world).TileIndices[0]).CasinoParty);
    }

    [Fact]
    public void APartyDrawsOnceEveryTwelveSecondsForItsTileEffect_ThePresentationsDrawIsTheSimulations()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        CreatureState dancer = Gambler(db, world, new TilePos(1, 1));
        StatePlacement.Place(dancer, CasinoStep.DancingState);
        world.TileAtIndex(Casino(db, world).TileIndices[0]).CasinoParty = CasinoJackpotRules.PartyBit;
        world.Player(Keeper).CasinoParties = 1;
        var draws = new Counting();
        var sim = new GameSimulation(db, world, new SimRandom(draws), [new CasinoSystem()]);

        sim.Run(2 * CasinoJackpotRules.EffectSeconds * SimulationTiming.OriginalTicksPerGameSecond);

        Assert.Equal(2, draws.ByBound.GetValueOrDefault(9));
    }

    [Fact]
    public void ACasinoSettlesNothingAtItsLeverWhileAPartyIsOn()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        GameSimulation sim = Simulation(db, world);
        sim.Run(1);
        CreatureState dancer = Gambler(db, world, new TilePos(1, 1));
        StatePlacement.Place(dancer, CasinoStep.DancingState);
        dancer.ActionUntilTick = long.MaxValue;
        world.TileAtIndex(Casino(db, world).TileIndices[0]).CasinoParty = CasinoJackpotRules.PartyBit;
        world.TileAtIndex(Casino(db, world).TileIndices[0]).CasinoPartyStartTick = sim.World.CurrentTick.Value;
        world.Player(Keeper).CasinoParties = 1;
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(3 * CasinoRules.SettlementSeconds * SimulationTiming.TicksPerSecond);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "settled");

        // The control: the same room with the party off settles at its next turn for it.
        world.TileAtIndex(Casino(db, world).TileIndices[0]).CasinoParty = 0;
        world.Player(Keeper).CasinoParties = 0;
        sim.Run(CasinoRules.SettlementSeconds * SimulationTiming.TicksPerSecond);
        Assert.Contains(sink.Events, e => e.Kind == "settled");
    }

    private sealed class Counting : IRandomAlgorithm
    {
        public readonly Dictionary<int, int> ByBound = [];
        public int NextInt(int exclusiveMax)
        {
            ByBound[exclusiveMax] = ByBound.GetValueOrDefault(exclusiveMax) + 1;
            return 0;
        }
        public void HashInto(ref Hash64 hash) { }
    }

    [Fact]
    public void ATimedOutPartyInAnEmptyCasinoIsCountedOffTwice_ADk2Quirk()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 3);
        ref TileState first = ref world.TileAtIndex(Casino(db, world).TileIndices[0]);
        first.CasinoParty = CasinoJackpotRules.PartyBit;
        first.CasinoPartyStartTick = -(90 * SimulationTiming.OriginalTicksPerGameSecond) - 1;
        world.Player(Keeper).CasinoParties = 2;
        var sim = new GameSimulation(db, world, seed: 1, [new CasinoSystem()]);

        sim.Tick();

        Assert.Equal(0, world.Player(Keeper).CasinoParties);
    }

    [Fact]
    public void ADancerDancesSixtyFourTimesOverThenGoesThroughIdleBackToTheChooser()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        GameSimulation sim = Simulation(db, world);
        sim.Run(1);
        CreatureState dancer = Gambler(db, world, new TilePos(1, 1));
        StatePlacement.Place(dancer, CasinoStep.DancingState);
        dancer.StateEnteredTick = sim.World.CurrentTick.Value;

        sim.Run(2);
        Assert.Equal(sim.World.CurrentTick.Value - 1 + 260, dancer.ActionUntilTick);

        HashSet<int> seen = Watch(sim, dancer, 262);
        Assert.Contains(CasinoStep.ChoosingState, seen);
    }

    // ------------------------------------------------------------------------- drinking

    [Fact]
    public void MoreThanTwoDrunkOccupantsPostMessage75AndMoreThanTenPost89()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        var drinkers = Enumerable.Range(0, 11).Select(i => Gambler(db, world, new TilePos(1 + (i % 5), 1 + (i / 5)))).ToList();
        var sim = new GameSimulation(db, world, seed: 1, [new Probe(context =>
        {
            if (context.Tick.Value == 0) foreach (CreatureState c in drinkers.Take(3)) CasinoStep.GetDrunk(context, "Test", c);
            if (context.Tick.Value == 1) foreach (CreatureState c in drinkers.Skip(3)) CasinoStep.GetDrunk(context, "Test", c);
        })]);

        sim.Tick();
        Assert.Contains(world.Messages.Entries, m => m.Kind == PlayerMessageKind.CasinoDrinking);
        Assert.DoesNotContain(world.Messages.Entries, m => m.Kind == PlayerMessageKind.CasinoRowdy);
        sim.Tick();

        Assert.Contains(world.Messages.Entries, m => m.Kind == PlayerMessageKind.CasinoRowdy && m.Data1 == CasinoStep.RowdyVariant);
        Assert.All(drinkers, c => Assert.Equal(45 * SimulationTiming.OriginalTicksPerGameSecond, c.DrunkTicks));
    }

    private sealed class Probe(Action<SimulationContext> body) : ISimulationSystem
    {
        public string Name => "Probe";
        public void Execute(SimulationContext context) => body(context);
    }

    // ----------------------------------------------------------------------- 154 (ADR-0402)

    /// <summary>A tile joins the room at (x, y), which stamps the rebuilt room (the type's slot +0x09).</summary>
    private static void Grow(GameDatabase db, WorldState world, RoomTypeId type, int x, int y)
    {
        RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), Keeper);
        SlabChain.Changed(world, db, (y * world.Width) + x, Dk2TerrainFixture.ClaimedPath, Keeper);
    }

    [Fact]
    public void ARebuiltCasinoSendsItsGamblerTo154_WhichWaitsATurnAndGoesBackTo145()
    {
        GameDatabase db = Db();
        WorldState world = World(db, 5);
        CreatureState it = Gambler(db, world, new TilePos(3, 3));
        GameSimulation sim = Simulation(db, world);
        sim.Tick();

        Grow(db, world, RoomTypeId.Casino, 6, 3);
        var order = new List<int>();
        for (int i = 0; i < 6; i++)
        {
            sim.Tick();
            order.Add(CreatureStateTable.Get(it.Dk2StateId).IsUtility ? it.UtilityNewState : it.Dk2StateId);
        }

        int back = order.IndexOf(CasinoStep.ReturningState);
        Assert.True(back >= 0, string.Join(" ", order));
        Assert.Contains(order.Skip(back + 1), s => s == CasinoStep.ChoosingState);
        Assert.DoesNotContain(CasinoStep.ReturningState, order.Skip(back + 2));
    }
}
