// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Fear and flight — docs/research/combat-fear.md, ADR-0165: the threat a fighter radiates,
/// the heart-distance factor, the counter and the flee mark, the back-off and the run, the
/// withdrawal of a hurt creature and where it goes. Duellists stand at (12,12), beyond ten tiles
/// of (1,1), a heartless player's heart position (Q372), so fear counts in full.</summary>
public class CombatFearTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Fact]
    public void WithdrawalThroughTheCombatGroupDoesNotDispatchTheBattleHandler()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceTreasury(world, Keeper);
        CreatureState creature = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        var sim = new GameSimulation(db, world, 1, [new WithdrawWithoutBattle(creature)]);
        sim.Tick();
        Assert.Equal(CreatureActivity.Withdrawing, creature.Activity);
        Assert.Empty(world.Messages.Entries);
    }

    private sealed class WithdrawWithoutBattle(CreatureState creature) : ISimulationSystem
    {
        public string Name => "withdrawal entry test";
        public void Execute(SimulationContext context)
        {
            context.Navigation.Refresh();
            var hearts = new HeartSites();
            hearts.Refresh(context.World, context.Database);
            Assert.True(FearRules.Withdraw(context, new WorldSight(context.Navigation, context.World, context.Database),
                hearts, new RoomCache(), creature, context.Database.CreatureById(creature.CreatureTypeId),
                FearRules.StagedTarget(context.World, creature), []));
        }
    }

    /// <summary><c>0x004611D0</c> with no lair, no heart and no room: both pickers find nothing
    /// without a draw (<c>0x004C6460</c>, <c>0x004C63D0</c> on a zero count), and the failed room pick
    /// returns 0 before step 5's local draws. Original-byte controls with the pickers failing return
    /// 0 with these two seeds unchanged (decision-parity-audit.md); the zero-count no-draw is read,
    /// not emulated.</summary>
    [Theory]
    [InlineData(1u)]
    [InlineData(0x12345678u)]
    public void ARoomlessKeepersCreatureDoesNotWithdraw_AndDrawsNothing(uint seed)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);

        WithdrawCall call = CallWithdraw(db, world, goblin, seed);

        Assert.False(call.Withdrew);
        Assert.Equal(call.SeedBefore, call.SeedAfter);
        Assert.NotEqual(CreatureActivity.Withdrawing, goblin.Activity);
    }

    /// <summary><c>0x004C63D0</c> draws over the whole list, bridges included, and a draw that lands
    /// on a bridge finds no room; the withdrawal ends there after that one draw.</summary>
    [Fact]
    public void ADrawOnABridgeEndsTheWithdrawal_AfterThatOneDraw()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        ref TileState bridge = ref world.Tile(new TilePos(15, 12));
        bridge.TerrainId = Dk2TerrainFixture.WoodenBridgeTerrain;
        bridge.OwnerPlayerId = Keeper;
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);

        WithdrawCall call = CallWithdraw(db, world, goblin, 1u);

        var expected = new Dk2Lcg(call.SeedBefore);
        Assert.Equal(0, expected.NextInt(1));
        Assert.False(call.Withdrew);
        Assert.Equal(expected.Seed, call.SeedAfter);
    }

    /// <summary>Step 5 is reached only when three picked rooms all fail to route: here the one room
    /// is walled in, three picks draw random(1) each, and a local try then finds a tile beside the
    /// creature (no staged target, so up to five tries; DK2's away-from-target stage is not built).</summary>
    [Fact]
    public void ThreeUnroutableRoomPicks_FallBackToATileBesideTheCreature()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        WallInATreasury(world);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);

        WithdrawCall call = CallWithdraw(db, world, goblin, 1u);

        var picks = new Dk2Lcg(call.SeedBefore);
        for (int i = 0; i < 3; i++) picks.NextInt(1);
        Assert.True(call.Withdrew);
        Assert.NotEqual(picks.Seed, call.SeedAfter);
        Assert.InRange(goblin.Destination.X, 11, 13);
        Assert.InRange(goblin.Destination.Y, 11, 13);
    }

    /// <summary>Step 5 with a staged creature target (<c>0x00461484</c>): three unroutable picks, a
    /// random(2) for the zero y offset, one away try of random(8) and random(4) that an open field
    /// accepts, then one local try of two random(3)s — eight draws. The away point is discarded, so the
    /// destination is still a tile beside the creature.</summary>
    [Fact]
    public void AStagedCreatureTarget_SpendsTheAwayStagesDraws_BeforeTwoLocalTries()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        WallInATreasury(world);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, 14, 12);

        WithdrawCall call = CallWithdraw(db, world, goblin, 1u, demon.Tile);

        Assert.True(call.Withdrew);
        Assert.Equal(Stepped(call.SeedBefore, 8), call.SeedAfter);
        Assert.InRange(goblin.Destination.X, 11, 13);
        Assert.InRange(goblin.Destination.Y, 11, 13);
    }

    /// <summary>The same withdrawal with nothing staged: three picks and one local try, five draws.</summary>
    [Fact]
    public void WithNothingStaged_TheLocalTriesFollowThePicksDirectly()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        WallInATreasury(world);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, 14, 12);

        WithdrawCall call = CallWithdraw(db, world, goblin, 1u, staged: null);

        Assert.True(call.Withdrew);
        Assert.Equal(Stepped(call.SeedBefore, 5), call.SeedAfter);
    }

    /// <summary>Only a live creature (class 0) or trap (class 3) counts as staged
    /// (<c>0x004614A8</c>-<c>0x004614B2</c>); a door or a heart does not, nor a target that is gone.</summary>
    [Fact]
    public void OnlyALiveCreatureOrTrapTargetIsStaged()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, 14, 13);

        goblin.FightTargetKind = FightTargetKind.Creature;
        goblin.FightTargetId = demon.Id;
        Assert.Equal(new TilePos(14, 13), FearRules.StagedTarget(world, goblin));

        goblin.FightTargetId = demon.Id + 1000;
        Assert.Null(FearRules.StagedTarget(world, goblin));

        ref TileState trapTile = ref world.Tile(new TilePos(10, 14));
        trapTile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        trapTile.OwnerPlayerId = Rival;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16])
            { X = 10, Y = 14, TrapId = Dk2TerrainFixture.AlarmTrapId, PlayerId = Rival, NumberOfShots = 1 }]);
        goblin.FightTargetKind = FightTargetKind.Trap;
        goblin.FightTargetId = world.Traps[^1].Id;
        Assert.Equal(new TilePos(10, 14), FearRules.StagedTarget(world, goblin));

        goblin.FightTargetKind = FightTargetKind.Door;
        Assert.Null(FearRules.StagedTarget(world, goblin));
        goblin.FightTargetKind = FightTargetKind.Heart;
        Assert.Null(FearRules.StagedTarget(world, goblin));
    }

    /// <summary>The away stage's retries and direction (<c>0x0046152F</c>-<c>0x00461615</c>): two tiles from the
    /// west wall with the target two tiles east, a try aims random(8) tiles west and holds only when it
    /// lands on tile 1 (tile 0 is impenetrable rock the line cannot enter), so the draws are replayed
    /// here on DK2's generator under that rule: three picks, the zero y offset's random(2), the away
    /// pairs up to and including the one that holds, then one local try.</summary>
    [Theory]
    [InlineData(1u)]
    [InlineData(7u)]
    [InlineData(0x12345678u)]
    public void TheAwayStageRetriesUntilAPointWestOfItHolds(uint seed)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        WallInATreasury(world);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 2, 12);
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, 4, 12);

        WithdrawCall call = CallWithdraw(db, world, goblin, seed, demon.Tile);

        var replay = new Dk2Lcg(call.SeedBefore);
        for (int i = 0; i < 3; i++) replay.NextInt(1);
        replay.NextInt(2);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int west = replay.NextInt(8);
            replay.NextInt(4);
            if (west <= 1) break;
        }
        replay.NextInt(3);
        replay.NextInt(3);
        Assert.True(call.Withdrew);
        Assert.Equal(replay.Seed, call.SeedAfter);
    }

    private static uint Stepped(uint seed, int draws)
    {
        var lcg = new Dk2Lcg(seed);
        for (int i = 0; i < draws; i++) lcg.NextInt(2);
        return lcg.Seed;
    }

    /// <summary>A one-tile Treasury of the keeper's at (16,16) walled in by rock: picked, never routed to.</summary>
    private static void WallInATreasury(WorldState world)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                ref TileState tile = ref world.Tile(new TilePos(16 + dx, 16 + dy));
                tile.TerrainId = dx == 0 && dy == 0 ? Dk2TerrainFixture.TreasuryTerrain : Dk2TerrainFixture.Rock;
                tile.OwnerPlayerId = dx == 0 && dy == 0 ? Keeper : (byte)0;
            }
        }
    }

    private readonly record struct WithdrawCall(bool Withdrew, uint SeedBefore, uint SeedAfter);

    /// <summary>One <see cref="FearRules.Withdraw"/> call on DK2's own generator, with its position
    /// read just before and just after the call.</summary>
    private static WithdrawCall CallWithdraw(GameDatabase db, WorldState world, CreatureState creature, uint seed, TilePos? staged = null)
    {
        var caller = new WithdrawCaller(creature, staged);
        var sim = new GameSimulation(db, world, new SimRandom(new Dk2Lcg(seed)), [caller]);
        sim.Tick();
        return caller.Call;
    }

    private sealed class WithdrawCaller(CreatureState creature, TilePos? staged) : ISimulationSystem
    {
        public WithdrawCall Call { get; private set; }
        public string Name => "withdrawal call";
        public void Execute(SimulationContext context)
        {
            context.Navigation.Refresh();
            var hearts = new HeartSites();
            hearts.Refresh(context.World, context.Database);
            uint before = ((Dk2Lcg)context.Rng.Algorithm).Seed;
            bool withdrew = FearRules.Withdraw(context, new WorldSight(context.Navigation, context.World, context.Database),
                hearts, new RoomCache(), creature, context.Database.CreatureById(creature.CreatureTypeId), staged, []);
            Call = new WithdrawCall(withdrew, before, ((Dk2Lcg)context.Rng.Algorithm).Seed);
        }
    }

    /// <summary>A player with no heart keeps the initialiser's (1,1), not the origin (<c>0x004B8640</c>).
    /// At (5,5) that is four tiles, where the origin would be five.</summary>
    [Fact]
    public void AHeartlessPlayersHeartIsTileOneOne()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        var none = new HeartSites();

        Assert.Equal(4, FearRules.HeartDistance(world, none, Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 5, 5)));
        Assert.Equal(0, FearRules.HeartDistance(world, none, Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 1, 1)));
    }

    [Fact]
    public void AFighterRadiatesItsThreat_SplitAmongItsEngagements_IntoThoseItCanSee()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);

        // Newest first: the Demon radiates120 before the Goblin's handover;
        // the Goblin then radiates37/2 twice, losing the odd point, after the Demon's handover.
        sim.Run(1);
        Assert.Equal(0, demon.CurrentFear);
        Assert.Equal(36, demon.TempFear);
        Assert.Equal(120, goblin.CurrentFear);
        Assert.Equal(0, goblin.TempFear);

        // Tick2: each handover receives the same engagement-split threat again.
        sim.Run(1);
        Assert.Equal(36, demon.CurrentFear);
        Assert.Equal(36, demon.TempFear);
        Assert.Equal(120, goblin.CurrentFear);
        Assert.Equal(0, goblin.TempFear);
    }

    [Fact]
    public void AStunnedCreatureNeitherHandsOverNorRadiates_ItsIntakePilesUp()
    {
        // 0x0045E4F0 returns before the handover for 209, 255, 256 and 257 (0x0048F350).
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);
        sim.Run(1);
        Assert.Equal(120, goblin.CurrentFear);

        StatePlacement.Place(goblin, 257);   // getting up, the stunned family
        goblin.ActionUntilTick = sim.World.CurrentTick.Value + 50;
        sim.Run(1);
        int afterOne = goblin.TempFear;
        sim.Run(1);
        Assert.True(StatusEffectRules.IsStunned(goblin));
        Assert.Equal(120, goblin.CurrentFear);          // no handover while stunned
        Assert.True(goblin.TempFear > afterOne && afterOne > 0, $"{afterOne} then {goblin.TempFear}");
        Assert.Equal(0, demon.TempFear);                 // and no radiation

        // Standing again, the whole stun's intake is handed over at once, with this tick's give
        // (the Demon, newer, gives before the Goblin's handover): Level 4's Goblin took 606.
        int piled = goblin.TempFear;
        StatePlacement.Place(goblin, 1);
        sim.Run(1);
        Assert.Equal(piled + 120, goblin.CurrentFear);
    }

    [Fact]
    public void FearIsScaledByTheDistanceFromTheOwnHeart_AndIsNothingBesideIt()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceHeart(world, Keeper, 12, 12);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 13, 12);
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);

        sim.Run(2);
        // One tile from the heart: a tenth of the 120 taken, under the Goblin's Fear of 50.
        Assert.Equal(12, goblin.CurrentFear);

        sim.Run(12);
        Assert.Equal(CreatureActivity.Fighting, goblin.Activity);
        Assert.Equal(0, goblin.OverThreatCounter);
    }

    [Fact]
    public void ThreeTicksOverTheThreshold_AndTheCreatureBacksOff_ForTheBackOffTimer()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        // The newer Demon radiates before the Goblin's update; the Goblin engages on tick 1 and its
        // fight state runs in that update (ADR-0555), so ticks 1, 2, 3 count that fear over 50 and
        // tick 3 flees.
        sim.Run(2);
        Assert.Equal(2, goblin.OverThreatCounter);
        Assert.Equal(CreatureActivity.Fighting, goblin.Activity);

        sim.Run(1);
        Assert.Equal(CreatureActivity.Fleeing, goblin.Activity);
        Assert.Contains(sink.Events, e => e.Kind == "flees" && e.Detail.Contains("creature=" + goblin.Id));

        // 120 / 50 = 2 → (4 − 2) × (30 − 0) = 60 ticks, unless the demon runs faster than the Goblin.
        CreatureDef goblinDef = db.CreatureById(goblin.CreatureTypeId), demonDef = db.CreatureById(demon.CreatureTypeId);
        int expected = FearRules.RunPerTick(demonDef) > FearRules.RunPerTick(goblinDef) ? 0 : 60;
        Assert.Equal(expected, goblin.BackOffTimer);
        Assert.True(goblin.Running || expected == 0);
    }

    [Fact]
    public void AFrightenedCreatureFleesInTwoHundredThree_AndWithdrawsInSixTowardsTwoHundredTwo()
    {
        // ADR-0208: 199, 203, then the withdraw's state 6 saving 203 and resuming 202; 202 holds the
        // arrival's turn and returns 0 on the next (ADR-0580).
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceTreasury(world, Keeper);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        Dk2TerrainFixture.SetKnownIdentities(world);
        // The run to the Treasury needs the movement pass, which follows combat in the real order.
        var sim = new GameSimulation(db, world, 1, [new CombatSystem(), new CreatureMovementSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        var seen = StateTrace.Of(sim, goblin, 200, () => sink.Events.Any(e => e.Kind == "withdrawn"));

        Assert.Equal([199, 203, 6], seen.Select(s => s.State).Take(3));
        Assert.Contains((6, 203, 202), seen);
        Assert.Equal(202, goblin.Dk2StateId);
        sim.Tick();
        Assert.Equal(CreatureStateMachine.Idle, goblin.Dk2StateId);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    /// <summary>The withdraw state stamps <c>OldFightPos</c> for a non-Imp, and DK2 writes the x
    /// coordinate into both words (<c>0x004617FB</c>–<c>0x00461811</c>): the return point is (x, x).</summary>
    [Fact]
    public void AWithdrawalStampsWhereTheFightWas_WithDk2sXInBothWords()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceTreasury(world, Keeper);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 9);
        Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        for (int t = 0; t < 60 && !sink.Events.Any(e => e.Kind == "withdraws"); t++) sim.Tick();

        Assert.Contains(sink.Events, e => e.Kind == "withdraws" && e.Detail.Contains("creature=" + goblin.Id));
        Assert.InRange(goblin.OldFightTileX, 11, 13);
        Assert.Equal(goblin.OldFightTileX, goblin.OldFightTileY);
        Assert.True(goblin.OldFightTick > 0);
    }

    [Fact]
    public void ABackedOffCreatureKeepsItsTarget_AndRunsWhenTheTimerIsOut()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceTreasury(world, Keeper);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(5);
        Assert.Equal(CreatureActivity.Fleeing, goblin.Activity);
        int timer = goblin.BackOffTimer;
        if (timer > 0)
        {
            sim.Run(1);
            Assert.Equal(timer - 1, goblin.BackOffTimer);
            Assert.Equal(CreatureActivity.Fleeing, goblin.Activity);
            Assert.Equal(demon.Id, goblin.FightTargetId);
        }

        // With no lair or heart, the Goblin withdraws to its Treasury and stands idle there; it
        // never swings at the Demon on the way.
        sim.Run(timer + 3);
        Assert.Contains(sink.Events, e => e.Kind == "withdraws" && e.Detail.Contains("creature=" + goblin.Id));
        Assert.True(goblin.Activity is CreatureActivity.Withdrawing or CreatureActivity.Idle, goblin.Activity.ToString());
        Assert.Equal(CombatRules.NoTarget, goblin.FightTargetId);
    }

    [Fact]
    public void AFearlessCreatureNeverCounts()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, 12, 12);
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, knight);
        GameSimulation sim = Combat(db, world);

        sim.Run(12);
        Assert.True(knight.CurrentFear > 0);
        Assert.Equal(0, knight.OverThreatCounter);
        Assert.Equal(CreatureActivity.Fighting, knight.Activity);
    }

    [Fact]
    public void NoEscapeTile_NoFearChecks()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        // The Goblin in a one-tile pocket whose only open neighbour the Demon stands on.
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) && !(dx == 1 && dy == 0))
                    world.Tile(new TilePos(12 + dx, 12 + dy)).TerrainId = Dk2TerrainFixture.Rock;
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState demon = Spawn(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, 13, 12);
        demon.PosX = goblin.PosX + 2200;
        demon.PosY = goblin.PosY;
        GameSimulation sim = Combat(db, world);

        sim.Run(12);
        Assert.True(goblin.CurrentFear > 50);
        Assert.Equal(0, goblin.OverThreatCounter);
        Assert.Equal(CreatureActivity.Fighting, goblin.Activity);
    }

    [Fact]
    public void AHurtCreatureFarFromItsHeart_WithdrawsInsteadOfFighting()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceTreasury(world, Keeper);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        goblin.Health = 400; // under a third of 1500
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(1);
        Assert.Contains(sink.Events, e => e.Kind == "withdraws" && e.Detail.Contains("creature=" + goblin.Id));
        Assert.NotEqual(CreatureActivity.Fighting, goblin.Activity);
        Assert.Equal(CombatRules.NoTarget, goblin.FightTargetId);

        sim.Run(10);
        Assert.Equal(CreatureState.NeverUsed, goblin.TimeLastUsedMelee);
        Assert.True(demon.TimeLastUsedMelee >= 0 || demon.Activity == CreatureActivity.Fighting);
    }

    [Fact]
    public void AHurtCreatureNearItsHeartStandsAndFights()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceHeart(world, Keeper, 8, 12);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12); // four tiles off
        goblin.Health = 400;
        CreatureState knight = Touching(db, world, Dk2TerrainFixture.KnightCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Run(3);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "withdraws");
        Assert.Equal(CreatureActivity.Fighting, goblin.Activity);
        Assert.Equal(knight.Id, goblin.FightTargetId);
    }

    [Fact]
    public void TheWithdrawalRunsForTheHeartsPositionWord_TwoTilesOffOnEachAxis_IfASmallHeartsWordIsOffsetLikeACapturedOnes()
    {
        // The heart's +0x27/+0x29 word sits one tile past its centre on the three captured 5×5
        // hearts (RoomInstance.CentreFieldX, Q515); this fixture's 3×3 heart centred on (3,3) is
        // aimed at from (4,4) on the assumption that the offset does not depend on the size.
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceHeart(world, Keeper, 3, 3);
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        goblin.Health = 400;
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);

        sim.Run(1);
        Assert.Equal(CreatureActivity.Withdrawing, goblin.Activity);
        Assert.True(goblin.Running);
        TilePos to = goblin.Destination;
        Assert.Equal(2, Math.Abs(to.X - 4));
        Assert.Equal(2, Math.Abs(to.Y - 4));
    }

    [Fact]
    public void AHeartPointInTheWallSendsTheWithdrawalToARandomRoomsWordInstead()
    {
        // Level 3's imps: three of the heart word's four points are reinforced wall round a 5×5
        // heart, and the withdraw fell through to a room 15 times in 21 (ADR-0323). Here all four
        // are rock, so the run ends at a room's own word: the Treasury's (13,14), or the heart's
        // (4,4) when step 4's draw lands on the heart itself, which is a room of the keeper's too.
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceHeart(world, Keeper, 3, 3);
        foreach ((int x, int y) in new[] { (2, 2), (6, 2), (2, 6), (6, 6) })
        {
            ref TileState wall = ref world.Tile(new TilePos(x, y));
            wall.TerrainId = Dk2TerrainFixture.Rock;
            wall.OwnerPlayerId = 0;
        }
        for (int y = 14; y <= 15; y++)
        {
            for (int x = 12; x <= 15; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                tile.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
                tile.OwnerPlayerId = Keeper;
            }
        }
        CreatureState goblin = Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 9, 9);
        goblin.Health = 400;
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation sim = Combat(db, world);

        sim.Run(1);
        Assert.Equal(CreatureActivity.Withdrawing, goblin.Activity);
        Assert.Contains(goblin.Destination, new[] { new TilePos(13, 14), new TilePos(4, 4) });
        Assert.DoesNotContain(goblin.Destination, new[] { new TilePos(2, 2), new TilePos(6, 2), new TilePos(2, 6), new TilePos(6, 6) });
    }

    [Fact]
    public void ARoomsPositionWordIsItsFloorCentre_AndAHeartsOneTileFurther_AsThreeCapturedHeartsRead()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        PlaceHeart(world, Keeper, 3, 3);
        for (int y = 14; y <= 15; y++)
        {
            for (int x = 12; x <= 15; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                tile.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
                tile.OwnerPlayerId = Keeper;
            }
        }

        IReadOnlyList<RoomInstance> rooms = RoomIndex.Build(world, db);
        RoomInstance heart = rooms.Single(r => r.Definition.TypeId == RoomTypeId.DungeonHeart);
        RoomInstance treasury = rooms.Single(r => r.Definition.TypeId == RoomTypeId.Treasury);
        Assert.Equal((4, 4), (heart.CentreFieldX, heart.CentreFieldY));
        Assert.Equal((13, 14), (treasury.CentreFieldX, treasury.CentreFieldY));
    }

    [Fact]
    public void AHeroNeverWithdraws()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = Spawn(db, world, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, 12, 12);
        knight.Health = 400;
        CreatureState demon = Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Keeper, knight);
        GameSimulation sim = Combat(db, world);

        sim.Run(3);
        Assert.Equal(CreatureActivity.Fighting, knight.Activity);
        Assert.Equal(demon.Id, knight.FightTargetId);
    }

    [Fact]
    public void FearSurvivesASnapshot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        Spawn(db, world, Dk2TerrainFixture.GoblinCreatureId, Keeper, 12, 12);
        CreatureState goblin = world.Creatures[0];
        Touching(db, world, Dk2TerrainFixture.BileDemonCreatureId, Rival, goblin);
        GameSimulation original = Combat(db, world);
        original.Run(4);
        Assert.True(goblin.CurrentFear > 0 && goblin.OverThreatCounter > 0);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db, [new CombatSystem()]);
        Assert.Equal(original.StateHash(), restored.StateHash());
        original.Run(10);
        restored.Run(10);
        Assert.Equal(original.StateHash(), restored.StateHash());
    }

    // ------------------------------------------------------------------ helpers

    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
        {
            for (int x = 1; x < world.Width - 1; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                if (tile.TerrainId == Dk2TerrainFixture.Rock || tile.TerrainId == Dk2TerrainFixture.Gold
                    || tile.TerrainId == Dk2TerrainFixture.Gems)
                    tile.TerrainId = Dk2TerrainFixture.DirtPath;
            }
        }
        return (db, world);
    }

    /// <summary>A three-by-three heart centred on the tile, owned by the keeper.</summary>
    private static void PlaceHeart(WorldState world, byte owner, int cx, int cy)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                ref TileState tile = ref world.Tile(new TilePos(cx + dx, cy + dy));
                tile.TerrainId = Dk2TerrainFixture.DungeonHeartTerrain;
                tile.OwnerPlayerId = owner;
            }
        }
    }

    /// <summary>A two-by-two Treasury of the keeper's at (15..16, 15..16), within reach of the duellists:
    /// a withdrawal needs a room of its own once it has no lair or heart (<c>0x004611D0</c>).</summary>
    private static void PlaceTreasury(WorldState world, byte owner)
    {
        for (int y = 15; y <= 16; y++)
        {
            for (int x = 15; x <= 16; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                tile.TerrainId = Dk2TerrainFixture.TreasuryTerrain;
                tile.OwnerPlayerId = owner;
            }
        }
    }

    private static CreatureState Spawn(GameDatabase db, WorldState world, byte type, byte owner, int x, int y) =>
        world.SpawnCreature(db, type, owner, new TilePos(x, y));

    private static CreatureState Touching(GameDatabase db, WorldState world, byte type, byte owner, CreatureState other, int dy = 0)
    {
        CreatureState c = world.SpawnCreature(db, type, owner, other.Tile);
        c.PosX = other.PosX + (dy == 0 ? 1300 : 0);
        c.PosY = other.PosY + (dy * 1300);
        return c;
    }

    private static GameSimulation Combat(GameDatabase db, WorldState world) =>
        new(db, world, 1, [new CombatSystem()]);
}
