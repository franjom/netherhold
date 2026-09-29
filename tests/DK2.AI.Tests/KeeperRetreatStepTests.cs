// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.AI.Tests;

/// <summary>The retreat of the wounded against the binary's branches (computer-keeper-threat.md
/// §4, ADR-0284): thresholds, the dropped-counter guard, the exclusions, the destination, the
/// hand's refusals and the allowance.</summary>
public class KeeperRetreatStepTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;
    private const byte Hero = PlayerIds.Good;
    private const int FightState = 199;
    private const int PrisonState = 115;
    private const int MaxHealth = 1500;

    /// <summary>The 2x2 Lair's bounding-box middle, where a retreat lands.</summary>
    private static readonly TilePos LairCentre = new(13, 13);

    private static KeeperAI NewKeeper(Func<KeeperAIParameters, KeeperAIParameters>? tune = null, byte speed = 1, ulong seed = 7)
    {
        KeeperAIParameters p = KeeperWorld.Parameters(speed: speed) with { IACreatureSkillLevel = 9 };
        return new KeeperAI(Keeper, tune is null ? p : tune(p), new SimRandom(seed));
    }

    private static WorldState World(bool lair = true)
    {
        WorldState world = KeeperWorld.World(Db);
        if (lair) Lair(world, 13, 13);
        return world;
    }

    private static void Lair(WorldState world, int x, int y)
    {
        for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
                KeeperWorld.Set(world, Db, new TilePos(x + dx, y + dy), KeeperWorld.LairFloor, Keeper);
    }

    private static CreatureState Wounded(WorldState world, int health = 299, int x = 17, int y = 17, byte type = KeeperWorld.FighterCreature)
    {
        CreatureState creature = world.SpawnCreature(Db, type, Keeper, new TilePos(x, y));
        creature.Health = health;
        return creature;
    }

    /// <summary>Runs empty ticks until the next observation's turn is <paramref name="turn"/>.</summary>
    private static void AdvanceToTurn(WorldState world, int turn)
    {
        var clock = new GameSimulation(Db, world, 1, Array.Empty<ISimulationSystem>());
        while (world.CurrentTick.Value + 1 < turn) clock.Tick(new InputFrame(Array.Empty<PlayerCommand>()));
    }

    private static int[] Lifted(IReadOnlyList<PlayerCommand> commands) =>
        commands.OfType<PickUpCreatureCommand>().Select(c => c.CreatureId).ToArray();

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, false)]
    public void ACreatureStrictlyBelowTheRetreatPercentageIsLiftedToTheLair(int health, bool lifted)
    {
        WorldState world = World();
        CreatureState creature = Wounded(world, health);

        IReadOnlyList<PlayerCommand> commands = NewKeeper().Think(Db, world);

        // 20 % of the level-scaled 1500 is 300; DK2 compares the health strictly below it.
        Assert.Equal(lifted ? [creature.Id] : Array.Empty<int>(), Lifted(commands));
        if (!lifted) return;
        Assert.Equal(LairCentre, Assert.Single(commands.OfType<DropCommand>()).Tile);
        var ordered = commands.ToList();
        Assert.Equal(ordered.FindIndex(c => c is PickUpCreatureCommand) + 1, ordered.FindIndex(c => c is DropCommand));
    }

    [Fact]
    public void TheDropGoesToARandomOwnLairOrToTheHeartWithoutOne()
    {
        WorldState bare = World(lair: false);
        Wounded(bare);
        Assert.Equal(KeeperWorld.HeartAt, Assert.Single(NewKeeper().Think(Db, bare).OfType<DropCommand>()).Tile);

        var picks = new HashSet<TilePos>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            WorldState world = World();
            Lair(world, 16, 13);
            Wounded(world);
            picks.Add(Assert.Single(NewKeeper(seed: seed).Think(Db, world).OfType<DropCommand>()).Tile);
        }
        // Both Lairs are drawn over twelve seeds, and nothing else is.
        Assert.Equal(new HashSet<TilePos> { LairCentre, new(16, 13) }, picks);
    }

    [Theory]
    [InlineData(749, 9, true)]
    [InlineData(750, 9, false)]
    [InlineData(750, 1, true)]
    [InlineData(1500, 1, true)]
    public void APitFighterIsLiftedBelowHalfHealthOrFromTheSkillLevelUp(int health, byte skill, bool lifted)
    {
        WorldState world = World();
        CreatureState fighter = Wounded(world, health);
        fighter.CombatPitFighter = true;

        IReadOnlyList<PlayerCommand> commands = NewKeeper(p => p with { IACreatureSkillLevel = skill }).Think(Db, world);

        // Half of 1500 is 750, tested strictly; at or above it the level-1 fighter needs the
        // skill level at 1, and 9 refuses it whatever its health.
        Assert.Equal(lifted ? [fighter.Id] : Array.Empty<int>(), Lifted(commands));
    }

    [Theory]
    [InlineData(300, 301, false)]
    [InlineData(301, 301, true)]
    [InlineData(300, 300, true)]
    public void TheDroppedCounterGuardHoldsFromTurnThreeHundredAndOne(int counter, int turn, bool lifted)
    {
        WorldState world = World();
        AdvanceToTurn(world, turn);
        CreatureState creature = Wounded(world);
        creature.DroppedTick = world.CurrentTick.Value - counter;

        IReadOnlyList<PlayerCommand> commands = NewKeeper().Think(Db, world);

        Assert.Equal(counter, HandRules.LastDroppedCounter(creature, world.CurrentTick.Value));
        Assert.Equal(lifted ? [creature.Id] : Array.Empty<int>(), Lifted(commands));
    }

    [Fact]
    public void AFightingCreatureBelowTheThresholdIsLiftedWhateverTheCounterAndAPitFighterGoesByLevel()
    {
        WorldState world = World();
        AdvanceToTurn(world, 400);
        CreatureState fighting = Wounded(world, 299, 17, 17);
        CreatureState enemy = world.SpawnCreature(Db, KeeperWorld.FighterCreature, Hero, new TilePos(17, 16));
        Assert.True(CreatureStateMachine.TrySetState(fighting, FightState));
        fighting.FightTargetId = enemy.Id;
        fighting.FightTargetKind = FightTargetKind.Creature;
        fighting.DroppedTick = world.CurrentTick.Value - 10;
        CreatureState idle = Wounded(world, 299, 13, 17);
        idle.DroppedTick = world.CurrentTick.Value - 10;
        CreatureState pit = Wounded(world, 1500, 14, 17);
        pit.CombatPitFighter = true;
        pit.DroppedTick = world.CurrentTick.Value - 10;

        IReadOnlyList<PlayerCommand> commands = NewKeeper(p => p with { IACreatureSkillLevel = 1 }, speed: 3).Think(Db, world);

        // Newest first: the guard sends the Pit fighter to its level branch, leaves the idle
        // creature where it stands, and the combat branch precedes it for the fighter.
        Assert.Equal([pit.Id, fighting.Id], Lifted(commands));
    }

    public static IEnumerable<object[]> Exclusions()
    {
        yield return [(Action<CreatureState>)(c => c.Dk2StateId = DropLandingRules.LandingState), "landing"];
        yield return [(Action<CreatureState>)(c => c.Dk2StateId = 255), "stunned"];
        yield return [(Action<CreatureState>)(c => c.Dk2StateId = PrisonState), "attached to a room"];
        yield return [(Action<CreatureState>)(c => { c.Dk2StateId = 6; c.StateBeforeUtilityState = PrisonState; c.UtilityNewState = 1; }), "walking out of a room"];
        yield return [(Action<CreatureState>)(c => c.Dk2StateId = LairRules.MakingAHomeState), "in the Lair category"];
    }

    [Theory]
    [MemberData(nameof(Exclusions))]
    public void TheOtherBranchSkipsTheExcludedAndSpendsNothingOnThem(Action<CreatureState> exclude, string reason)
    {
        WorldState world = World();
        CreatureState later = Wounded(world, 299, 13, 17);
        CreatureState excluded = Wounded(world, 299, 17, 17);
        exclude(excluded);
        Assert.Equal(0, KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(new KeeperView(Db, world, Keeper)), Db)
            .Creatures.Single(c => c.Id == excluded.Id).OwnershipRank);

        IReadOnlyList<PlayerCommand> commands = NewKeeper().Think(Db, world);

        // The list is newest first; the single allowance passes the excluded creature and
        // reaches the one behind it.
        Assert.True(Lifted(commands).SequenceEqual([later.Id]), reason);
    }

    [Fact]
    public void AWalkIntoARoomIsNotYetAttachedAndAHeldCreatureIsSkipped()
    {
        WorldState world = World();
        CreatureState arriving = Wounded(world, 299, 17, 17);
        arriving.Dk2StateId = 6;
        arriving.StateBeforeUtilityState = CreatureStateMachine.Idle;
        arriving.UtilityNewState = PrisonState;
        Assert.False(RoomMembershipRules.IsAttached(arriving));
        Assert.Equal([arriving.Id], Lifted(NewKeeper().Think(Db, world)));

        WorldState held = World();
        CreatureState later = Wounded(held, 299, 13, 17);
        CreatureState inHand = Wounded(held, 299, 17, 17);
        new GameSimulation(Db, held, 1, [new HandSystem()]).Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, inHand.Id)));
        Assert.Equal([later.Id], Lifted(NewKeeper().Think(Db, held)));
    }

    [Fact]
    public void ARefusedHandPredicateSpendsTheAllowanceAndIssuesNothing()
    {
        WorldState world = World();
        CreatureState later = Wounded(world, 299, 13, 17);
        CreatureState worker = Wounded(world, 99, 17, 17, KeeperWorld.WorkerCreature);
        worker.Flags = 0; // 0x0048E6A0 refuses a worker with both pickup bits clear

        IReadOnlyList<PlayerCommand> commands = NewKeeper().Think(Db, world);

        Assert.DoesNotContain(commands, c => c is PickUpCreatureCommand or DropCommand);
        // Two allowances reach the second creature.
        Assert.Equal([later.Id], Lifted(NewKeeper(speed: 2).Think(Db, world)));
    }

    [Fact]
    public void AFullHandGetsThePickUpButNoDropAndStillSpendsTheAllowance()
    {
        GameDatabase full = KeeperWorld.Database(handCapacity: 0);
        WorldState world = KeeperWorld.World(full);
        CreatureState behind = world.SpawnCreature(full, KeeperWorld.FighterCreature, Keeper, new TilePos(17, 17));
        behind.Health = 299;
        CreatureState first = world.SpawnCreature(full, KeeperWorld.FighterCreature, Keeper, new TilePos(13, 17));
        first.Health = 299;

        IReadOnlyList<PlayerCommand> commands = NewKeeper().Think(full, world);

        // 0x004BC500 refuses the lift, 0x004BCAE0 says it is not held, and the allowance is gone.
        Assert.Equal([first.Id], Lifted(commands));
        Assert.DoesNotContain(commands, c => c is DropCommand);

        // Applied, the refused lift has still run 0x004BFBD0: the creature stands in state 75,
        // out of the hand, and the next tick's walk selects it again on the other branch.
        new GameSimulation(full, world, 1, [new HandSystem()]).Tick(new InputFrame(commands));
        Assert.Equal((HandRules.PreparationState, false), (first.Dk2StateId, first.InHand));
        Assert.Equal([first.Id], Lifted(NewKeeper().Think(full, world)));
        Assert.Equal(HandRules.PreparationState, first.Dk2StateId);
    }

    [Fact]
    public void AFullHandStillTakesAFighterOutOfTheBattleStepsWalkAndOutOfItsFight()
    {
        GameDatabase full = KeeperWorld.Database(handCapacity: 0);
        WorldState world = KeeperWorld.World(full);
        CreatureState own = world.SpawnCreature(full, KeeperWorld.FighterCreature, Keeper, new TilePos(13, 15));
        own.Health = 299;
        CreatureState enemy = world.SpawnCreature(full, KeeperWorld.FighterCreature, Hero, new TilePos(12, 15));
        Assert.True(CreatureStateMachine.TrySetState(own, FightState));
        own.FightTargetId = enemy.Id;
        own.FightTargetKind = FightTargetKind.Creature;
        KeeperAI keeper = NewKeeper(speed: 4);
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(full, world);

        // DK2's preparation has moved the fighter to 75 before the hand refused it, so the same
        // tick's battle step finds nobody fighting; the simulation shows the same a tick later.
        Assert.Equal([own.Id], Lifted(commands));
        Assert.DoesNotContain(commands, c => c is DropCommand or CastSpellAtPlaceCommand);
        Assert.False(keeper.State.FightGoingOn);
        new GameSimulation(full, world, 1, [new HandSystem()]).Tick(new InputFrame(commands));
        Assert.Equal((HandRules.PreparationState, CreatureActivity.BeingLifted, false), (own.Dk2StateId, own.Activity, own.InHand));
    }

    [Theory]
    [InlineData(2, 2, false)]
    [InlineData(3, 3, false)]
    [InlineData(4, 3, true)]
    public void TheWalkFollowsTheListOrderAndStopsWhenTheAllowanceIsSpent(byte speed, int lifted, bool impsAfter)
    {
        WorldState world = World();
        CreatureState a = Wounded(world, 299, 17, 17);
        CreatureState b = Wounded(world, 299, 13, 17);
        CreatureState c = Wounded(world, 299, 17, 13);

        IReadOnlyList<PlayerCommand> commands = NewKeeper(p => p with { MaximumImps = 4, TilesPerImpRatio = 10 }, speed: speed).Think(Db, world);

        // DK2's owned list has the newest creature at its head.
        Assert.Equal(new[] { c.Id, b.Id, a.Id }.Take(lifted), Lifted(commands));
        Assert.Equal(impsAfter, commands.Any(x => x is CreateImpCommand));
    }

    [Fact]
    public void APendingEventTakesTheWholeTickBeforeTheRetreat()
    {
        WorldState world = World();
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        CreatureState wounded = Wounded(world, 299, 17, 17);
        world.SpawnCreature(Db, KeeperWorld.FighterCreature, Keeper, new TilePos(14, 14));
        world.SpawnCreature(Db, KeeperWorld.FighterCreature, Hero, new TilePos(13, 13));
        // The attack is more than five tiles from the wounded creature, out of the withdrawal's reach.
        world.Messages.Post(world, Keeper, PlayerMessageKind.CombatStarted, 13, 13);
        KeeperAI keeper = NewKeeper(speed: 3);
        var sim = new GameSimulation(Db, world, 1, [new HandSystem(), new RescuePrioritySystem()]);

        IReadOnlyList<PlayerCommand> during = keeper.Think(Db, world);

        // The event ran and returned zero allowance: neither the retreat nor the task machine
        // got the tick, though the heart's gold would have been tagged for.
        Assert.Contains(during, c => c is AcknowledgePlayerMessageCommand);
        Assert.Empty(Lifted(during));
        Assert.DoesNotContain(during, c => c is TagTilesCommand);
        Assert.Equal(0, keeper.State.Responses.Count);

        // Acknowledged, the message is not read again; the next tick's allowance reaches the retreat.
        sim.Tick(new InputFrame(during));
        Assert.Equal([wounded.Id], Lifted(keeper.Think(Db, world)));
    }

    [Fact]
    public void ALiftedFighterLeavesTheBattleStepsWalkInTheSameTick()
    {
        WorldState world = World();
        CreatureState own = Wounded(world, 299, 13, 15);
        CreatureState enemy = world.SpawnCreature(Db, KeeperWorld.FighterCreature, Hero, new TilePos(12, 15));
        Assert.True(CreatureStateMachine.TrySetState(own, FightState));
        own.FightTargetId = enemy.Id;
        own.FightTargetKind = FightTargetKind.Creature;
        KeeperAI keeper = NewKeeper(speed: 4);
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // In DK2 the lifted fighter sits in state 78 when the battle step walks: nobody fights.
        Assert.Equal([own.Id], Lifted(commands));
        Assert.False(keeper.State.FightGoingOn);

        KeeperAI quiet = NewKeeper(p => p with { BARetreatHealth = 0 }, speed: 4);
        quiet.State.FightGoingOn = true;
        Assert.Empty(Lifted(quiet.Think(Db, world)));
        Assert.True(quiet.State.FightGoingOn);
    }

    [Fact]
    public void TheNewFieldsRoundTripAndTheCommandStreamIsRepeatable()
    {
        WorldState world = World();
        CreatureState pit = Wounded(world, 1500, 17, 17);
        pit.CombatPitFighter = true;
        CreatureState jailed = Wounded(world, 299, 13, 17);
        jailed.Dk2StateId = PrisonState;

        KeeperView view = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(new KeeperView(Db, world, Keeper)), Db);
        Assert.True(Assert.Single(view.Creatures, c => c.Id == pit.Id).CombatPitFighter);
        Assert.False(Assert.Single(view.Creatures, c => c.Id == pit.Id).AttachedToRoom);
        Assert.True(Assert.Single(view.Creatures, c => c.Id == jailed.Id).AttachedToRoom);

        Lair(world, 16, 13);
        var first = new List<string>();
        var second = new List<string>();
        KeeperAI one = NewKeeper(p => p with { IACreatureSkillLevel = 1 }, seed: 11);
        KeeperAI two = NewKeeper(p => p with { IACreatureSkillLevel = 1 }, seed: 11);
        for (int tick = 0; tick < 4; tick++)
        {
            first.AddRange(one.Think(view).Select(c => c.ToString()!));
            second.AddRange(two.Think(view).Select(c => c.ToString()!));
        }
        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void AWoundedCreatureIsCarriedToTheLairUnaided()
    {
        WorldState world = World();
        CreatureState creature = Wounded(world, 299, 17, 17);
        var sim = new GameSimulation(Db, world, 1, [new HandSystem(), new CombatSystem(), new RescuePrioritySystem()]);
        KeeperAI keeper = NewKeeper();
        var log = new List<(int Tick, PlayerCommand Command)>();

        for (int tick = 0; tick < 4; tick++)
        {
            IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
            foreach (PlayerCommand command in commands) log.Add((tick, command));
            sim.Tick(new InputFrame(commands));
        }

        // Lifted on the first tick and put down on the Lair through the real hand; put down, its
        // counter is fresh, so nothing lifts it again.
        (int Tick, PlayerCommand Command) pick = Assert.Single(log, e => e.Command is PickUpCreatureCommand);
        Assert.Equal((0, creature.Id), (pick.Tick, ((PickUpCreatureCommand)pick.Command).CreatureId));
        Assert.Equal(LairCentre, ((DropCommand)Assert.Single(log, e => e.Command is DropCommand).Command).Tile);
        Assert.Equal(LairCentre, creature.Tile);
        Assert.False(creature.InHand);
    }
}
