// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.AI.Tests;

/// <summary>The keeper's reactive half against the binary's rules: the message walk, the
/// position reader, the two event arms and the whole-tick allowance (ADR-0278).</summary>
public class KeeperThreatResponseTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;
    private const byte Hero = PlayerIds.Good;

    private static KeeperAI NewKeeper(byte speed = 1, bool withdraw = false, byte imprison = 0, ulong seed = 7) =>
        new(Keeper, KeeperWorld.Parameters(speed: speed, withdraw: withdraw, imprison: imprison), new SimRandom(seed));

    private static GameSimulation Sim(WorldState world, params ISimulationSystem[] systems) =>
        new(Db, world, 1, systems.Length == 0 ? [new HandSystem(), new RescuePrioritySystem()] : systems);

    private static CreatureState Fighter(WorldState world, byte owner, int x, int y) =>
        world.SpawnCreature(Db, KeeperWorld.FighterCreature, owner, new TilePos(x, y));

    private static PlayerMessage Attack(WorldState world, int x, int y, PlayerMessageKind kind = PlayerMessageKind.CombatStarted) =>
        world.Messages.Post(world, Keeper, kind, x, y)!.Value;

    /// <summary>Clears rock to unowned floor so creatures can stand and see there.</summary>
    private static void Open(WorldState world, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.DirtPath);
    }

    [Fact]
    public void AReinforcementOnAnOwnedDropSiteIsAcknowledgedDebitsThreatAndMovesNobody()
    {
        WorldState world = KeeperWorld.World(Db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        CreatureState own = Fighter(world, Keeper, 14, 15);
        Fighter(world, Hero, 16, 15);
        PlayerMessage message = Attack(world, 16, 15);
        KeeperAI keeper = NewKeeper(speed: 3);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // Enemy 50 within ten tiles, allies 50 within four: a zero residual is a real event,
        // dispatched through the reinforcement arm. The nearest owned open tile is the attacked
        // tile itself, so DK2 moves nobody and still debits the fighter's threat, retiring it.
        Assert.Equal(message.Id, Assert.Single(commands.OfType<AcknowledgePlayerMessageCommand>()).MessageId);
        Assert.DoesNotContain(commands, c => c is PickUpCreatureCommand or DropCommand);
        Assert.True(keeper.State.FightGoingOn);
        Assert.True(keeper.State.HaveBeenAttacked);
        Assert.Equal(Hero, keeper.State.AttackingPlayerId);
        Assert.Equal(0, keeper.State.Responses.Count);
        // The event returned zero allowance: the task machine did not run this tick.
        Assert.DoesNotContain(commands, c => c is TagTilesCommand);
        WorldState quiet = KeeperWorld.World(Db);
        quiet.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        Assert.Contains(NewKeeper(speed: 3).Think(Db, quiet), c => c is TagTilesCommand);
        Assert.False(own.InHand);
    }

    [Fact]
    public void AnAcknowledgedMessageIsNotEvaluatedAgainAndAFullQueueLeavesItUnhandled()
    {
        WorldState world = KeeperWorld.World(Db);
        Fighter(world, Keeper, 14, 15);
        Fighter(world, Hero, 16, 15);
        PlayerMessage message = Attack(world, 16, 15, PlayerMessageKind.HeartAttacked);
        GameSimulation sim = Sim(world);
        KeeperAI keeper = NewKeeper(speed: 0);
        for (int i = 0; i < 3; i++)
            Assert.True(keeper.State.Responses.TryReceive(() => new KeeperThreatResponse(9, new TilePos(1, 1))));

        IReadOnlyList<PlayerCommand> refused = keeper.Think(Db, world);
        Assert.DoesNotContain(refused, c => c is AcknowledgePlayerMessageCommand);
        Assert.True(keeper.State.FightGoingOn);
        Assert.False(keeper.State.HaveBeenAttacked);
        sim.Tick(new InputFrame(refused));
        Assert.False(Assert.Single(world.Messages.Entries).Handled);

        keeper.State.Responses.Dispatch(1, _ => new KeeperResponseAttempt(false, null));
        IReadOnlyList<PlayerCommand> accepted = keeper.Think(Db, world);
        Assert.Equal(message.Id, Assert.Single(accepted.OfType<AcknowledgePlayerMessageCommand>()).MessageId);
        Assert.Equal(3, keeper.State.Responses.Count);
        sim.Tick(new InputFrame(accepted));
        Assert.True(Assert.Single(world.Messages.Entries).Handled);

        // Retained for 120 seconds but handled: the walk skips it from now on.
        Assert.DoesNotContain(keeper.Think(Db, world), c => c is AcknowledgePlayerMessageCommand);
        Assert.Equal(3, keeper.State.Responses.Count);
    }

    [Fact]
    public void ACancelledReservationStillAcknowledgesTheMessage()
    {
        WorldState world = KeeperWorld.World(Db);
        Fighter(world, Keeper, 14, 15);
        Fighter(world, Keeper, 15, 14);
        Fighter(world, Hero, 16, 15);
        PlayerMessage message = Attack(world, 16, 15);
        KeeperAI keeper = NewKeeper();

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // Allies 100 against 50 wanted: a negative deficit with a drop site cancels the slot.
        Assert.Equal(message.Id, Assert.Single(commands.OfType<AcknowledgePlayerMessageCommand>()).MessageId);
        Assert.Equal(0, keeper.State.Responses.Count);
        Assert.True(keeper.State.FightGoingOn);
    }

    [Fact]
    public void WithdrawalPicksUpTheNearbyCreatureInListOrderAndDropsItOnTheHeart()
    {
        WorldState world = KeeperWorld.World(Db);
        Open(world, 3, 3, 9, 9);
        CreatureState first = Fighter(world, Keeper, 6, 5);
        CreatureState second = Fighter(world, Keeper, 5, 7);
        Fighter(world, Hero, 7, 5);
        Fighter(world, Hero, 7, 6);
        Fighter(world, Hero, 7, 7);
        Attack(world, 5, 5);
        KeeperAI keeper = NewKeeper(speed: 3, withdraw: true);
        GameSimulation sim = Sim(world);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // No owned tile within five of (5,5): 150 wanted against 100 allies withdraws -100.
        // The list is most recently acquired first, so the second fighter goes first; both
        // moves add 50 and the event retires at zero, ending the tick's allowance.
        Assert.Equal(
            [second.Id, first.Id],
            commands.OfType<PickUpCreatureCommand>().Select(c => c.CreatureId).ToArray());
        Assert.All(commands.OfType<DropCommand>(), d => Assert.Equal(KeeperWorld.HeartAt, d.Tile));
        Assert.Equal(2, commands.OfType<DropCommand>().Count());
        Assert.Equal(0, keeper.State.Responses.Count);
        Assert.DoesNotContain(commands, c => c is TagTilesCommand);

        sim.Tick(new InputFrame(commands));
        Assert.Equal(KeeperWorld.HeartAt, first.Tile);
        Assert.Equal(KeeperWorld.HeartAt, second.Tile);
        Assert.False(first.InHand);
        Assert.Empty(world.Player(Keeper).Hand);
    }

    [Fact]
    public void WithdrawalLeavesARecentlyDroppedCreatureAloneOnceTheLevelIsPastTurnThreeHundred()
    {
        WorldState world = KeeperWorld.World(Db);
        Open(world, 3, 3, 9, 9);
        CreatureState settled = Fighter(world, Keeper, 6, 5);
        GameSimulation sim = Sim(world);
        sim.Run(400);
        // Spawned now: its counter is zero, and it heads the list.
        CreatureState fresh = Fighter(world, Keeper, 5, 6);
        Fighter(world, Hero, 7, 5);
        Fighter(world, Hero, 7, 6);
        Fighter(world, Hero, 7, 7);
        Attack(world, 5, 5);
        KeeperAI keeper = NewKeeper(speed: 2, withdraw: true);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        Assert.Equal(settled.Id, Assert.Single(commands.OfType<PickUpCreatureCommand>()).CreatureId);
        Assert.NotEqual(fresh.Id, settled.Id);
        // One move of 50 against -100 leaves -50; the second attempt finds nobody and retires.
        Assert.Equal(0, keeper.State.Responses.Count);
    }

    [Fact]
    public void ARefusedPickupPermissionSpendsTheAttemptWithoutThreat()
    {
        WorldState world = KeeperWorld.World(Db);
        CreatureState own = Fighter(world, Keeper, 14, 15);
        own.ManaRemovalStamp = 5; // 0x0048E6A0 refuses any stamped creature
        Fighter(world, Hero, 16, 15);
        Fighter(world, Hero, 16, 14);
        Attack(world, 16, 15);
        KeeperAI keeper = NewKeeper(speed: 2);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        Assert.DoesNotContain(commands, c => c is PickUpCreatureCommand or DropCommand);
        KeeperThreatResponse current = Assert.NotNull(keeper.State.Responses.Current);
        Assert.Equal(50, current.RemainingThreat);
        Assert.Equal(1, keeper.State.Responses.Count);
        Assert.DoesNotContain(commands, c => c is TagTilesCommand);
    }

    [Fact]
    public void ImprisonmentIsRerolledForEveryEnemyOnlyWhenTheKeeperOwnsAPrison()
    {
        WorldState world = KeeperWorld.World(Db);
        Fighter(world, Keeper, 14, 15);
        CreatureState near = Fighter(world, Hero, 16, 15);
        CreatureState far = Fighter(world, Hero, 3, 3);
        GameSimulation sim = Sim(world);
        Attack(world, 16, 15);
        Assert.DoesNotContain(NewKeeper(imprison: 100).Think(Db, world), c => c is SetRescuePriorityCommand);
        sim.Tick(); // a zero-timeout message is swept, never retained as a duplicate

        KeeperWorld.Set(world, Db, new TilePos(13, 13), KeeperWorld.PrisonFloor, Keeper);
        Attack(world, 16, 15);
        IReadOnlyList<PlayerCommand> marks = NewKeeper(imprison: 100).Think(Db, world);
        Assert.Equal([near.Id, far.Id], marks.OfType<SetRescuePriorityCommand>()
            .OrderBy(c => c.CreatureId).Select(c => c.CreatureId).ToArray());
        Assert.All(marks.OfType<SetRescuePriorityCommand>(), c => Assert.True(c.Enabled));
        sim.Tick(new InputFrame(marks));
        Assert.Equal(1 << (Keeper - 1), near.RescuePriorityMask);

        // A zero percentage clears a mark it finds set and says nothing about the rest.
        Attack(world, 16, 15);
        IReadOnlyList<PlayerCommand> clears = NewKeeper(imprison: 0).Think(Db, world);
        Assert.Equal(2, clears.OfType<SetRescuePriorityCommand>().Count());
        Assert.All(clears.OfType<SetRescuePriorityCommand>(), c => Assert.False(c.Enabled));
    }

    [Fact]
    public void AnUnownedPayloadSpiralsToTheFirstOwnedOpenTileNotTheNearest()
    {
        WorldState world = KeeperWorld.World(Db);
        CreatureState own = Fighter(world, Keeper, 14, 15);
        var view = new KeeperView(Db, world, Keeper);
        KeeperAI keeper = NewKeeper();
        var responder = new KeeperThreatResponder(view, keeper.State, KeeperWorld.Parameters(), new SimRandom(1), []);

        // From (19,15) the walk goes right, down, left, up and right again: (17,17) is the first
        // owned tile it meets, two rows below the nearer (17,15).
        Assert.Equal(new TilePos(17, 17), responder.ReplacementSite(new TilePos(19, 15)));
        Assert.Equal(new TilePos(16, 15), responder.ReplacementSite(new TilePos(16, 15)));
        Assert.True(view.IsMine(Assert.NotNull(responder.ReplacementSite(new TilePos(1, 1)))));

        Assert.True(keeper.State.Responses.TryReceive(() => new KeeperThreatResponse(60, new TilePos(19, 15))));
        IReadOnlyList<PlayerCommand> commands = keeper.Think(view);
        Assert.Equal(own.Id, Assert.Single(commands.OfType<PickUpCreatureCommand>()).CreatureId);
        Assert.Equal(new TilePos(17, 17), Assert.Single(commands.OfType<DropCommand>()).Tile);
        Assert.Equal(new KeeperThreatResponse(10, new TilePos(17, 17)), keeper.State.Responses.Current);
    }

    [Fact]
    public void ReinforcementEligibilityFollowsTheBinaryTests()
    {
        WorldState world = KeeperWorld.World(Db);
        Open(world, 3, 3, 9, 9);
        // Far from the fight, so none of these counts as an ally, but every one is a candidate.
        world.SpawnCreature(Db, KeeperWorld.WorkerCreature, Keeper, new TilePos(4, 4));
        CreatureState weak = Fighter(world, Keeper, 5, 4);
        weak.Health = 750; // exactly half: not strictly above
        CreatureState stunned = Fighter(world, Keeper, 6, 4);
        Assert.True(CreatureStateMachine.TrySetState(stunned, 255)); // the landing stun, 0x0048F350
        CreatureState fit = Fighter(world, Keeper, 15, 14);
        Fighter(world, Hero, 16, 15);
        Fighter(world, Hero, 16, 14);
        Fighter(world, Hero, 16, 16);
        Attack(world, 16, 15);

        // 150 wanted against the fit fighter's 50: residual 100, and one attempt debits 50.
        GameSimulation sim = Sim(world);
        KeeperAI keeper = NewKeeper();
        keeper.Think(Db, world);
        Assert.Equal(50, Assert.NotNull(keeper.State.Responses.Current).RemainingThreat);
        sim.Tick();

        // Just above half qualifies the weak one too; the worker and the stunned one never do.
        weak.Health = 751;
        Attack(world, 16, 15);
        KeeperAI second = NewKeeper(speed: 2);
        second.Think(Db, world);
        Assert.Equal(0, second.State.Responses.Count);
        sim.Tick();

        // Nobody eligible once the fit one is at a quarter: the first attempt finds no candidate
        // and retires the event as it is.
        weak.Health = 750;
        fit.Health = 375;
        Attack(world, 16, 15);
        KeeperAI third = NewKeeper();
        third.Think(Db, world);
        Assert.Equal(0, third.State.Responses.Count);
    }

    [Fact]
    public void TheObservationCarriesTheNewCreatureFactsAcrossAPacket()
    {
        WorldState world = KeeperWorld.World(Db);
        Open(world, 3, 3, 9, 9);
        CreatureState older = Fighter(world, Keeper, 6, 5);
        GameSimulation sim = Sim(world);
        sim.Run(12);
        CreatureState newer = Fighter(world, Keeper, 5, 6);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, older.Id)));

        KeeperView view = KeeperViewSnapshot.FromBytes(
            KeeperViewSnapshot.ToBytes(new KeeperView(Db, world, Keeper)), Db);
        KeeperCreatureView held = Assert.Single(view.Creatures, c => c.Id == older.Id);
        KeeperCreatureView fresh = Assert.Single(view.Creatures, c => c.Id == newer.Id);
        Assert.True(held.InMyHand);
        Assert.Equal(0, held.LastDroppedCounter);
        Assert.Equal(1, held.OwnershipRank);
        Assert.Equal(0, fresh.OwnershipRank);
        Assert.Equal(1, fresh.LastDroppedCounter);
        Assert.Equal(1500, fresh.MaxHealth);
        Assert.True(fresh.MayFight);
        Assert.Equal(1, fresh.StateId);
        Assert.Equal(1, fresh.ResolvedStateId);

        // Dropped during tick 13: the next tick's keeper reads one, as DK2's does the turn after.
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, new TilePos(15, 15))));
        Assert.Equal(1, HandRules.LastDroppedCounter(older, world.CurrentTick.Value));
        sim.Run(3);
        Assert.Equal(4, HandRules.LastDroppedCounter(older, world.CurrentTick.Value));
    }

    [Fact]
    public void AnAttackTriggersAnUnaidedWithdrawalThroughTheLiveCombatMessage()
    {
        WorldState world = KeeperWorld.World(Db);
        Open(world, 3, 3, 9, 9);
        CreatureState defender = Fighter(world, Keeper, 5, 5);
        Fighter(world, Hero, 7, 5);
        Fighter(world, Hero, 5, 7);
        var sim = new GameSimulation(Db, world, 1, [new CombatSystem(), new HandSystem(), new RescuePrioritySystem()]);
        KeeperAI keeper = NewKeeper(speed: 2, withdraw: true);
        var log = new List<(long Tick, PlayerCommand Command)>();

        for (int tick = 0; tick < 6; tick++)
        {
            IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
            foreach (PlayerCommand command in commands) log.Add((tick, command));
            sim.Tick(new InputFrame(commands));
        }

        // The defender engages on the first tick and its keeper receives type 6 for its tile;
        // outnumbered two to one with no owned tile in reach, the keeper pulls it back.
        (long Tick, PlayerCommand Command) pick = Assert.Single(log, e => e.Command is PickUpCreatureCommand);
        Assert.Equal(defender.Id, ((PickUpCreatureCommand)pick.Command).CreatureId);
        Assert.Contains(log, e => e.Tick == pick.Tick && e.Command is AcknowledgePlayerMessageCommand);
        var drop = (DropCommand)Assert.Single(log, e => e.Command is DropCommand).Command;
        Assert.Equal(KeeperWorld.HeartAt, drop.Tile);
        Assert.Equal(KeeperWorld.HeartAt, defender.Tile);
        Assert.True(keeper.State.FightGoingOn);
    }

    [Fact]
    public void WithdrawalDoesNotFreeAnOwnCreatureFromAnotherKeepersPrison()
    {
        WorldState world = KeeperWorld.World(Db);
        Open(world, 3, 3, 9, 9);
        KeeperWorld.Set(world, Db, new TilePos(6, 5), KeeperWorld.PrisonFloor, Hero);
        CreatureState jailed = Fighter(world, Keeper, 6, 5);
        Assert.True(PrisonRules.Imprison(world, Db, jailed, Hero, (5 * world.Width) + 6));
        Fighter(world, Hero, 7, 5);
        Fighter(world, Hero, 7, 6);
        Fighter(world, Hero, 7, 7);
        Attack(world, 5, 5);
        KeeperAI keeper = NewKeeper(speed: 2, withdraw: true);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // Its stored room is the hero's Prison (0x0047B740 writes the membership 0x0048E6A0
        // reads): the walk finds it, permission refuses it, both attempts are spent for nothing.
        Assert.False(Assert.Single(new KeeperView(Db, world, Keeper).Creatures, c => c.Id == jailed.Id).PickupPermitted);
        Assert.DoesNotContain(commands, c => c is PickUpCreatureCommand or DropCommand);
        Assert.Equal(1, keeper.State.Responses.Count);
        Assert.Equal(-50, Assert.NotNull(keeper.State.Responses.Current).RemainingThreat);
    }

    [Fact]
    public void TwoRerollsInOneTickLeaveTheLastRollsMark()
    {
        // Find a seed whose first two rolls disagree, so the last one is visible.
        ulong seed = 1;
        while (true)
        {
            var probe = new SimRandom(seed);
            bool first = probe.NextInt(100) < 50, second = probe.NextInt(100) < 50;
            if (first != second) break;
            seed++;
        }
        var replay = new SimRandom(seed);
        replay.NextInt(100);
        bool expected = replay.NextInt(100) < 50;

        WorldState world = KeeperWorld.World(Db);
        KeeperWorld.Set(world, Db, new TilePos(13, 13), KeeperWorld.PrisonFloor, Keeper);
        Fighter(world, Keeper, 14, 15);
        CreatureState enemy = Fighter(world, Hero, 16, 15);
        Attack(world, 16, 15);
        Attack(world, 16, 14);
        GameSimulation sim = Sim(world);
        KeeperAI keeper = NewKeeper(speed: 0, imprison: 50, seed: seed);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
        sim.Tick(new InputFrame(commands));

        Assert.Equal(2, commands.OfType<AcknowledgePlayerMessageCommand>().Count());
        Assert.Equal(expected, (enemy.RescuePriorityMask & (1 << (Keeper - 1))) != 0);
        Assert.Equal(expected, commands.OfType<SetRescuePriorityCommand>().Last().Enabled);
    }

    [Fact]
    public void AFullHandLeavesTheCreatureWhereItStandsAndTheNextAttemptTakesItAgain()
    {
        GameDatabase db = KeeperWorld.Database(handCapacity: 1);
        WorldState world = KeeperWorld.World(db);
        Open(world, 3, 3, 9, 9);
        CreatureState held = world.SpawnCreature(db, KeeperWorld.FighterCreature, Keeper, new TilePos(14, 15));
        CreatureState near = world.SpawnCreature(db, KeeperWorld.FighterCreature, Keeper, new TilePos(6, 5));
        var sim = new GameSimulation(db, world, 1, [new HandSystem()]);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, held.Id)));
        Assert.True(held.InHand);
        sim.Run(400);
        // Counts as an ally at the fight but, just spawned past turn 300, is never withdrawn.
        world.SpawnCreature(db, KeeperWorld.FighterCreature, Keeper, new TilePos(5, 6));
        for (int i = 0; i < 4; i++) world.SpawnCreature(db, KeeperWorld.FighterCreature, Hero, new TilePos(7, 4 + i));
        Attack(world, 5, 5);
        KeeperAI keeper = NewKeeper(speed: 2, withdraw: true);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(db, world);

        // 200 wanted against 100 allies withdraws -100. Permission passes and the hand refuses
        // (0x004BC500): DK2 debits the same creature's 50 on both attempts, -100 to 0, and
        // retires; the creature is asked for twice and never dropped.
        Assert.Equal([near.Id, near.Id], commands.OfType<PickUpCreatureCommand>().Select(c => c.CreatureId).ToArray());
        Assert.DoesNotContain(commands, c => c is DropCommand);
        Assert.Equal(0, keeper.State.Responses.Count);
        sim.Tick(new InputFrame(commands));
        Assert.Equal(new TilePos(6, 5), near.Tile);
    }

    [Fact]
    public void ACreatureUnderItsKeepersCallToArmsIsCapturedAsSuchAndNeverReinforces()
    {
        WorldState world = KeeperWorld.World(Db);
        CreatureState called = Fighter(world, Keeper, 14, 15);
        CreatureState free = Fighter(world, Keeper, 15, 14);
        called.HeedingCallToArms = true; // bit 20, as the flag's recruit sets it
        Fighter(world, Hero, 16, 15);
        Fighter(world, Hero, 16, 14);
        Fighter(world, Hero, 16, 16);
        Attack(world, 16, 15);
        KeeperView view = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(new KeeperView(Db, world, Keeper)), Db);
        Assert.True(Assert.Single(view.Creatures, c => c.Id == called.Id).UnderCallToArms);
        Assert.False(Assert.Single(view.Creatures, c => c.Id == free.Id).UnderCallToArms);
        Assert.False(view.CallToArms.On);

        // 150 wanted against 100 allies: residual 50; the called one heads the list and is
        // skipped (0x004888C0), so the free one's 50 retires the event.
        KeeperAI keeper = NewKeeper();
        keeper.Think(Db, world);
        Assert.Equal(0, keeper.State.Responses.Count);

        // With the keeper's own call on, every creature of its counts as under it and nobody reinforces.
        ref PlayerState player = ref world.Player(Keeper);
        player.CallToArms = player.CallToArms with { On = true, X = 15, Y = 15 };
        Attack(world, 16, 14);
        KeeperAI another = NewKeeper();
        another.Think(Db, world);
        Assert.Equal(50, Assert.NotNull(another.State.Responses.Current).RemainingThreat);
        KeeperView on = new(Db, world, Keeper);
        Assert.True(on.CallToArms.On);
        Assert.All(on.Creatures.Where(c => c.OwnerPlayerId == Keeper), c => Assert.True(c.UnderCallToArms));
    }

    [Fact]
    public void SameSeedsGiveTheSameCommandStreamUnderAttack()
    {
        static ulong Run(ulong seed)
        {
            WorldState world = KeeperWorld.World(Db);
            KeeperWorld.Set(world, Db, new TilePos(13, 13), KeeperWorld.PrisonFloor, Keeper);
            Fighter(world, Keeper, 14, 15);
            for (int i = 0; i < 4; i++) Fighter(world, Hero, 16 + (i % 2), 14 + (i / 2));
            GameSimulation sim = Sim(world);
            KeeperAI keeper = NewKeeper(speed: 2, withdraw: true, imprison: 50, seed: seed);
            Hash64 hash = Hash64.Start();
            for (int tick = 0; tick < 40; tick++)
            {
                if (tick % 5 == 0) Attack(world, 16, 15, tick % 10 == 0 ? PlayerMessageKind.CombatStarted : PlayerMessageKind.HeartAttacked);
                var frame = new InputFrame(keeper.Think(Db, world));
                frame.HashInto(ref hash);
                sim.Tick(frame);
            }
            return hash.Value;
        }

        Assert.Equal(Run(42), Run(42));
        Assert.NotEqual(Run(42), Run(43));
    }
}
