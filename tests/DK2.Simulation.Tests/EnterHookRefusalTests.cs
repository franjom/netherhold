// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Training and Casino enter hooks' refusals before their claims (ADR-0313):
/// Training's gold test with message 55 (<c>0x0047B230</c>), the Casino's lever and purse tests
/// (<c>0x0047ACB0</c>), each ending the job through the refused-claim path of ADR-0309.</summary>
public sealed class EnterHookRefusalTests
{
    private const byte Keeper = 4;
    private const byte Rival = 5;
    private const byte GoblinId = 7;
    private static readonly TilePos Vault = new(12, 8);

    private static int Second => SimulationTiming.TicksPerSecond;
    private static int TrainState => JobRules.StateFor(JobType.Train)!.Value;
    private static int DrinkState => JobRules.StateFor(JobType.Drink)!.Value;

    /// <summary>Paved ground at (1..12, 1..8) the keeper owns, with a treasury tile holding
    /// <paramref name="gold"/> at (12, 8).</summary>
    private static WorldState World(GameDatabase db, int gold = 100_000)
    {
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 12; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        Fund(world, gold);
        return world;
    }

    private static void Fund(WorldState world, int gold)
    {
        ref TileState treasury = ref world.Tile(Vault.X, Vault.Y);
        treasury.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
        treasury.GoldHeld = gold;
    }

    private static void Room(GameDatabase db, WorldState world, RoomTypeId type, int x0, int y0, int size, byte owner = Keeper)
    {
        for (int y = y0; y < y0 + size; y++)
        for (int x = x0; x < x0 + size; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), owner);
    }

    private static RoomInstance RoomAt(GameDatabase db, WorldState world, int x, int y) =>
        RoomIndex.RoomAt(world, db, (y * world.Width) + x)!;

    private static StateHookContext Hooks(GameDatabase db, WorldState world) => new(world, db);

    private static IEnumerable<PlayerMessage> Unpaid(WorldState world) =>
        world.Messages.Entries.Where(m => m.Kind == PlayerMessageKind.TrainingUnpaid);

    /// <summary>A Training Room at (5..7, 1..3), one dummy and two places, furnished.</summary>
    private static WorldState TrainingWorld(GameDatabase db, int gold)
    {
        WorldState world = World(db, gold);
        Room(db, world, RoomTypeId.TrainingRoom, 5, 1, 3);
        Dk2TerrainFixture.FurnishRooms(db, world);
        return world;
    }

    /// <summary>A furnished 5×5 Casino at (2..6, 2..6): a lever, and tables to give it places.
    /// A 3×3's lever takes its one slot, which leaves it no table and so no place.</summary>
    private static WorldState CasinoWorld(GameDatabase db, int size = 5)
    {
        WorldState world = World(db);
        Room(db, world, RoomTypeId.Casino, 2, 2, size);
        Dk2TerrainFixture.FurnishRooms(db, world);
        return world;
    }

    private static CreatureState Gambler(GameDatabase db, WorldState world, int purse, TilePos? at = null)
    {
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, at ?? new TilePos(4, 5));
        goblin.GoldHeld = purse;
        return goblin;
    }

    // ---- Training, 0x0047B230 ------------------------------------------------------------

    [Fact]
    public void Training_GoldSpentDuringTheWalk_IsRefusedOnArrival_WithMessage55_AndTheJobEnds()
    {
        // The handler's test passes at the choice; the enter hook asks again on arrival.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = TrainingWorld(db, gold: 100_000);
        CreatureState walker = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(1, 7));
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureMovementSystem(), new CreatureJobSystem()]);
        sim.ObserveWith(sink);
        for (int i = 0; i < Second * 10 && walker.Activity != CreatureActivity.GoingToJob; i++) sim.Tick();
        Assert.Equal(CreatureActivity.GoingToJob, walker.Activity);
        Assert.Equal(JobType.Train, walker.CurrentJob);

        Fund(world, TrainingRules.GoldPerSecond(db) - 1);
        for (int i = 0; i < Second * 20 && walker.Activity == CreatureActivity.GoingToJob; i++) sim.Tick();

        Assert.Contains(sink.Events, e => e.Kind == "job_lost" && e.Detail.Contains($"creature={walker.Id} ")
            && e.Detail.Contains("reason=keeper_cannot_pay message=0x37"));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "job_lost" && e.Detail.Contains("room_full"));
        Assert.Equal(CreatureStateMachine.Idle, walker.Dk2StateId);
        Assert.Equal(JobType.None, walker.CurrentJob);
        Assert.False(walker.HeldPlace.IsHeld);
        PlayerMessage message = Assert.Single(Unpaid(world));
        Assert.Equal(Keeper, message.PlayerId);
        Assert.Equal(TrainingRules.UnpaidMessageVariant, message.Data1);
        Assert.Equal((uint)SimulationTiming.TicksFromSeconds(240), message.Timeout);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(4, false)]
    public void Training_TheEnterHookAcceptsGoldOfExactlyTheCost_AndRefusesOneLess(int gold, bool accepted)
    {
        // CMP 5, Money; JBE: 5 <= Money passes.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = TrainingWorld(db, gold);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2));
        Assert.Equal(5, TrainingRules.GoldPerSecond(db));

        Assert.Equal(accepted, CreatureStateMachine.TrySetState(Hooks(db, world), goblin, TrainState));

        Assert.Equal(accepted ? TrainState : CreatureStateMachine.Idle, goblin.Dk2StateId);
        Assert.Equal(accepted, goblin.HeldPlace.IsHeld);
        Assert.Equal(accepted ? 0 : 1, Unpaid(world).Count());
        Assert.Equal(gold, GoldStore.Total(world, Keeper));
    }

    [Fact]
    public void Training_ASecondRefusalWhileMessage55IsUnexpired_RaisesNoSecondMessage()
    {
        // Type 55 is a subtype message with duplicates off: the unexpired record is returned.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = TrainingWorld(db, gold: 0);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2));

        Assert.False(CreatureStateMachine.TrySetState(Hooks(db, world), goblin, TrainState));
        Assert.False(CreatureStateMachine.TrySetState(Hooks(db, world), goblin, TrainState));

        Assert.Single(Unpaid(world));
    }

    // ---- Casino, 0x0047ACB0 --------------------------------------------------------------

    [Fact]
    public void Casino_PurseSpentDuringTheWalk_IsRefusedOnArrival_AndTheJobEnds()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = CasinoWorld(db);
        Assert.True(AutoFurnitureRules.TryTotalCapacity(world, RoomAt(db, world, 4, 4), out int capacity) && capacity > 0);
        CreatureState goblin = Gambler(db, world, purse: 100);
        goblin.CurrentJob = JobType.Drink;
        CreatureStateMachine.BeginTravel(Hooks(db, world), goblin, 7, DrinkState);
        Assert.Equal(CreatureActivity.GoingToJob, goblin.Activity);

        goblin.GoldHeld = CasinoRules.Stake(db);
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, seed: 42, [new CreatureMovementSystem(), new CreatureJobSystem()]);
        sim.ObserveWith(sink);
        sim.Tick();

        Assert.Contains(sink.Events, e => e.Kind == "job_lost" && e.Detail.Contains($"creature={goblin.Id} ")
            && e.Detail.Contains("reason=cannot_afford_to_gamble message=none"));
        Assert.Equal(CreatureStateMachine.Idle, goblin.Dk2StateId);
        Assert.Equal(JobType.None, goblin.CurrentJob);
        Assert.False(goblin.HeldPlace.IsHeld);
        Assert.Empty(world.Messages.Entries);
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(9, true)]
    public void Casino_APurseOfExactlyTheStakeIsRefused_AndOneMoreIsAccepted(int purse, bool accepted)
    {
        // CMP +0x3FB, 8; JLE refuses: the purse must beat the stake.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = CasinoWorld(db);
        CreatureState goblin = Gambler(db, world, purse);
        Assert.Equal(8, CasinoRules.Stake(db));

        Assert.Equal(accepted, CreatureStateMachine.TrySetState(Hooks(db, world), goblin, DrinkState));

        Assert.Equal(accepted ? DrinkState : CreatureStateMachine.Idle, goblin.Dk2StateId);
        Assert.Equal(accepted, goblin.HeldPlace.IsHeld);
        Assert.Equal(purse, goblin.GoldHeld);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void Casino_TheKeepersOwnCasinoWithNoLever_RefusesEntryBeforeTheClaim(int size)
    {
        // A Casino under 3x3 has no lever; a 5x5 whose lever is gone isolates the test from the claim.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = CasinoWorld(db, size);
        if (size == 5)
            world.RemoveObject(world.Objects.Single(o => o.ObjectTypeId == AutoFurnitureRules.CasinoLeverObjectId).Id);
        Assert.Null(CasinoRules.Lever(world, RoomAt(db, world, 2, 2)));
        CreatureState goblin = Gambler(db, world, purse: 100, at: new TilePos(2, 3));

        Assert.Equal("no_lever", CasinoRules.EnterRefusal(world, db, goblin));
        Assert.False(CreatureStateMachine.TrySetState(Hooks(db, world), goblin, DrinkState));
        Assert.Equal(CreatureStateMachine.Idle, goblin.Dk2StateId);
        Assert.False(goblin.HeldPlace.IsHeld);
    }

    [Fact]
    public void Casino_OnARivalsCasino_TheLeverIsNotAsked_AndTheClaimRefusesIt()
    {
        // 0x004539B0(15, owner, 0) fails, so the lever test is skipped; 0x004E7F80 then refuses
        // another keeper's Casino. A rival's 2x2 has no lever.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);
        Room(db, world, RoomTypeId.Casino, 2, 2, 2, owner: Rival);
        CreatureState goblin = Gambler(db, world, purse: 100, at: new TilePos(2, 2));

        Assert.Null(CasinoRules.EnterRefusal(world, db, goblin));
        Assert.False(CreatureStateMachine.TrySetState(Hooks(db, world), goblin, DrinkState));
        Assert.Equal(CreatureStateMachine.Idle, goblin.Dk2StateId);
    }

    [Fact]
    public void Casino_OffAnyRoom_OnlyThePurseIsAsked_AndTheClaimRefuses()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = World(db);

        Assert.Null(CasinoRules.EnterRefusal(world, db, Gambler(db, world, purse: 100)));
        Assert.Equal("cannot_afford_to_gamble", CasinoRules.EnterRefusal(world, db, Gambler(db, world, purse: 0)));
    }

    // ---- No hook on a walk inside one category ------------------------------------------

    [Fact]
    public void AWalkFromTrainingBackToTraining_RunsNoEnterHook_SoAKeeperGoneBrokeCannotRefuseIt()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = TrainingWorld(db, gold: 100_000);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2));
        Assert.True(CreatureStateMachine.TrySetState(Hooks(db, world), goblin, TrainState));

        Fund(world, 0);
        CreatureStateMachine.BeginTravel(Hooks(db, world), goblin, 7, TrainState);
        Assert.True(CreatureStateMachine.Arrive(Hooks(db, world), goblin));

        Assert.Equal(TrainState, goblin.Dk2StateId);
        Assert.True(goblin.HeldPlace.IsHeld);
        Assert.Empty(Unpaid(world));
    }

    [Fact]
    public void AWalkFromTheCasinoBackToTheCasino_RunsNoEnterHook_SoAnEmptyPurseCannotRefuseIt()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = CasinoWorld(db);
        CreatureState goblin = Gambler(db, world, purse: 100);
        Assert.True(CreatureStateMachine.TrySetState(Hooks(db, world), goblin, DrinkState));

        goblin.GoldHeld = 0;
        CreatureStateMachine.BeginTravel(Hooks(db, world), goblin, 7, DrinkState);
        Assert.True(CreatureStateMachine.Arrive(Hooks(db, world), goblin));

        Assert.Equal(DrinkState, goblin.Dk2StateId);
        Assert.True(goblin.HeldPlace.IsHeld);
    }

    // ---- The countdown: seeded after the refusing tests, before the claim ---------------

    [Fact]
    public void Training_AGoldRefusalLeavesTheCountdownAsItWas_AsDk2LeavesPlus0xB3Unwritten()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = TrainingWorld(db, gold: 0);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2));
        goblin.HookCountdown = 1;

        Assert.False(CreatureStateMachine.TrySetState(Hooks(db, world), goblin, TrainState));

        Assert.Equal(1, goblin.HookCountdown);
    }

    [Fact]
    public void Training_AFullRoomStillSeedsTheCountdown_AsDk2SeedsPlus0xB3BeforeItsClaim()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = TrainingWorld(db, gold: 100_000);
        CreatureState[] goblins = [.. Enumerable.Range(0, 3).Select(_ => world.SpawnCreature(db, GoblinId, Keeper, new TilePos(6, 2)))];
        Assert.True(CreatureStateMachine.TrySetState(Hooks(db, world), goblins[0], TrainState));
        Assert.True(CreatureStateMachine.TrySetState(Hooks(db, world), goblins[1], TrainState));
        goblins[2].HookCountdown = 1;

        Assert.False(CreatureStateMachine.TrySetState(Hooks(db, world), goblins[2], TrainState));

        Assert.Equal(CreatureStateMachine.HookCountdownSeed, goblins[2].HookCountdown);
        Assert.Empty(Unpaid(world));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, true)]
    public void Casino_APurseRefusalLeavesTheCountdownAsItWas_AndAnEntrySeedsIt(int purse, bool seeded)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = CasinoWorld(db);
        CreatureState goblin = Gambler(db, world, purse);
        goblin.HookCountdown = 1;

        Assert.Equal(seeded, CreatureStateMachine.TrySetState(Hooks(db, world), goblin, DrinkState));

        Assert.Equal(seeded ? CreatureStateMachine.HookCountdownSeed : 1, goblin.HookCountdown);
    }

    [Fact]
    public void TheStaleSeedOurOldOrderLeftWasUnread_EveryCountdownReaderIsGatedOnItsOwnCategory()
    {
        // Step 2 of ADR-0313: a refused creature is in state 1, whose category has no update arm,
        // and its next countdown category is entered through a hook that seeds it.
        Assert.Null(CategoryHooks.Slots.SingleOrDefault(s =>
            s.Category == CreatureStateTable.Get(CreatureStateMachine.Idle).Category && s.Kind == HookKind.Update));
        Assert.All(CategoryHooks.Slots.Where(s => s.SeedsCountdown), s => Assert.Equal(HookKind.Enter, s.Kind));
    }
}
