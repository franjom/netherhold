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

/// <summary>The battle step and the Call to Arms removal test against the binary's boundaries
/// (ADR-0283, computer-keeper-management.md §4 and §5).</summary>
public class KeeperBattleStepTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;
    private const byte Hero = PlayerIds.Good;
    private const int FightState = 199;

    /// <summary>A keeper that never rolls for a spell, so only the call is in play.</summary>
    private static KeeperAI NewKeeper(Func<KeeperAIParameters, KeeperAIParameters>? tune = null, byte speed = 1, ulong seed = 7)
    {
        KeeperAIParameters p = KeeperWorld.Parameters(speed: speed) with { UseKeeperSpells = 4, BACallToArmsValue = 49 };
        return new KeeperAI(Keeper, tune is null ? p : tune(p), new SimRandom(seed));
    }

    private static CreatureState Fighter(WorldState world, byte owner, int x, int y) =>
        world.SpawnCreature(Db, KeeperWorld.FighterCreature, owner, new TilePos(x, y));

    /// <summary>Unowned floor west of the keeper's patch, joined to it.</summary>
    private static WorldState World()
    {
        WorldState world = KeeperWorld.World(Db);
        for (int y = 12; y <= 18; y++)
            for (int x = 8; x <= 12; x++)
                KeeperWorld.Set(world, Db, new TilePos(x, y), KeeperWorld.DirtPath);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        return world;
    }

    /// <summary>An own fighter on the patch's edge engaged with a hero beside it, the keeper able
    /// to pay for the call and allowed to cast it.</summary>
    private static (WorldState World, CreatureState Own, CreatureState Enemy) Battle(int mana = 20000)
    {
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 13, 15);
        CreatureState enemy = Fighter(world, Hero, 12, 15);
        Engage(world, own, enemy);
        world.Player(Keeper).Mana = mana;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, CallToArmsRules.SpellId, 3);
        return (world, own, enemy);
    }

    private static void Engage(WorldState world, CreatureState attacker, CreatureState target)
    {
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, Db), attacker, FightState));
        attacker.FightTargetId = target.Id;
        attacker.FightTargetKind = FightTargetKind.Creature;
    }

    /// <summary>Runs empty ticks until the next observation's turn is <paramref name="turn"/>.</summary>
    private static void AdvanceToTurn(WorldState world, int turn)
    {
        var clock = new GameSimulation(Db, world, 1, Array.Empty<ISimulationSystem>());
        while (world.CurrentTick.Value + 1 < turn) clock.Tick(new InputFrame(Array.Empty<PlayerCommand>()));
    }

    private static CastSpellAtPlaceCommand? Call(IReadOnlyList<PlayerCommand> commands) =>
        commands.OfType<CastSpellAtPlaceCommand>().SingleOrDefault();

    [Fact]
    public void NoFighterClearsTheFightFlagAndImpCreationResumes()
    {
        WorldState world = World();
        KeeperAI keeper = NewKeeper(p => p with { MaximumImps = 4, TilesPerImpRatio = 10 });
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // The step clears the flag and finds nobody fighting; the next tick makes imps again.
        Assert.False(keeper.State.FightGoingOn);
        Assert.DoesNotContain(commands, c => c is CreateImpCommand);
        Assert.Contains(keeper.Think(Db, world), c => c is CreateImpCommand);
    }

    [Fact]
    public void AFighterKeepsTheFightFlagOnAndTakesTheTickFromImpCreation()
    {
        (WorldState world, CreatureState own, _) = Battle();
        KeeperAI keeper = NewKeeper(p => p with { MaximumImps = 4, TilesPerImpRatio = 10 });
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> fighting = keeper.Think(Db, world);
        Assert.True(keeper.State.FightGoingOn);
        Assert.DoesNotContain(fighting, c => c is CreateImpCommand);

        // The fight ends: the walk finds no fighter, the flag drops and imps come back.
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, Db), own, CreatureStateMachine.Idle));
        IReadOnlyList<PlayerCommand> quiet = keeper.Think(Db, world);
        Assert.False(keeper.State.FightGoingOn);
        Assert.DoesNotContain(quiet, c => c is CreateImpCommand);
        Assert.Contains(keeper.Think(Db, world), c => c is CreateImpCommand);
    }

    [Theory]
    [InlineData(49, 40, true)]
    [InlineData(50, 40, false)]
    [InlineData(49, 41, false)]
    [InlineData(49, 80, true)]
    public void TheCallIsCastAtTheFighterWhenTheTargetOwnersStrengthExceedsTheValueOnAFortiethTurn(
        ushort value, int turn, bool cast)
    {
        (WorldState world, CreatureState own, _) = Battle();
        AdvanceToTurn(world, turn);
        KeeperAI keeper = NewKeeper(p => p with { BACallToArmsValue = value });
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // The hero's 50 must strictly exceed the value; the twelve-tile radius admits the map.
        CastSpellAtPlaceCommand? call = Call(commands);
        Assert.Equal(cast, call is not null);
        Assert.Equal(cast, keeper.State.CallToArmsInUse);
        if (call is null) return;
        Assert.Equal((CallToArmsRules.SpellId, own.PosX, own.PosY), (call.SpellId, call.X, call.Y));
        Assert.Equal(250, keeper.State.CallToArmsCounter);
        Assert.Equal(turn, keeper.State.CallToArmsCastTurn);
    }

    [Theory]
    [InlineData(10000, false)]
    [InlineData(10001, true)]
    public void ManaEqualToTheCostRefusesTheCallWhereTheHumanCastWouldPass(int mana, bool cast)
    {
        (WorldState world, _, _) = Battle(mana);
        AdvanceToTurn(world, 40);
        KeeperAI keeper = NewKeeper();
        keeper.State.FightGoingOn = true;

        Assert.Equal(cast, Call(keeper.Think(Db, world)) is not null);
    }

    [Fact]
    public void ACallInUseAnAttackWaitOrAnUnavailableSpellRefusesTheCall()
    {
        (WorldState world, _, _) = Battle();
        AdvanceToTurn(world, 40);

        // A call the keeper already counts as its own, standing in the world and not yet due down.
        world.Player(Keeper).CallToArms = new CallToArmsState(true, 1, 13, 15, 0);
        KeeperAI inUse = NewKeeper(p => p with { RemoveCallToArmsEnemyCreatures = 0 });
        inUse.State.FightGoingOn = true;
        inUse.State.CallToArmsInUse = true;
        inUse.State.CallToArmsCounter = 250;
        inUse.State.CallToArmsCastTurn = 1;
        Assert.Null(Call(inUse.Think(Db, world)));
        Assert.True(inUse.State.CallToArmsInUse);
        world.Player(Keeper).CallToArms = default;

        KeeperAI waiting = NewKeeper();
        waiting.State.FightGoingOn = true;
        waiting.State.CannotAttackCounter = 2; // step 4 leaves 1
        Assert.Null(Call(waiting.Think(Db, world)));
        Assert.Equal(1, waiting.State.CannotAttackCounter);

        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, CallToArmsRules.SpellId, 2);
        KeeperAI unavailable = NewKeeper();
        unavailable.State.FightGoingOn = true;
        Assert.Null(Call(unavailable.Think(Db, world)));
        // The position was still stored, as DK2 writes it before the later checks.
        Assert.Equal(13 * Fp32.One + Fp32.One / 2, unavailable.State.CallToArmsX);
    }

    [Fact]
    public void LavaUnderTheFighterOrNoRouteFromTheHeartRefusesTheCall()
    {
        (WorldState lavaWorld, CreatureState own, _) = Battle();
        KeeperWorld.Set(lavaWorld, Db, own.Tile, KeeperWorld.Lava, Keeper);
        AdvanceToTurn(lavaWorld, 40);
        KeeperAI overLava = NewKeeper();
        overLava.State.FightGoingOn = true;
        Assert.Null(Call(overLava.Think(Db, lavaWorld)));

        // An island of floor: the fighter fights there, but nothing walks to it from the heart.
        WorldState island = World();
        for (int y = 3; y <= 5; y++)
            for (int x = 3; x <= 5; x++)
                KeeperWorld.Set(island, Db, new TilePos(x, y), KeeperWorld.DirtPath);
        CreatureState marooned = Fighter(island, Keeper, 4, 4);
        Engage(island, marooned, Fighter(island, Hero, 5, 4));
        island.Player(Keeper).Mana = 20000;
        island.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, CallToArmsRules.SpellId, 3);
        AdvanceToTurn(island, 40);
        Assert.False(Assert.Single(new KeeperView(Db, island, Keeper).Creatures, c => c.Id == marooned.Id).HeartRouteExists);
        KeeperAI cutOff = NewKeeper();
        cutOff.State.FightGoingOn = true;
        Assert.Null(Call(cutOff.Think(Db, island)));
        Assert.False(cutOff.State.CallToArmsInUse);
    }

    [Fact]
    public void TheCastSpendsOneAllowanceAndEndsTheStep()
    {
        (WorldState one, _, _) = Battle();
        AdvanceToTurn(one, 40);
        KeeperAI speedOne = NewKeeper(speed: 1);
        speedOne.State.FightGoingOn = true;
        Assert.NotNull(Call(speedOne.Think(Db, one)));
        // Nothing was left for the task machine.
        Assert.Equal(KeeperTask.Decide, speedOne.State.Task);

        (WorldState two, _, _) = Battle();
        AdvanceToTurn(two, 40);
        KeeperAI speedTwo = NewKeeper(speed: 2);
        speedTwo.State.FightGoingOn = true;
        Assert.NotNull(Call(speedTwo.Think(Db, two)));
        Assert.NotEqual(KeeperTask.Decide, speedTwo.State.Task);
    }

    [Fact]
    public void AnEventDispatchedThisTickLeavesNoAllowanceForTheBattleStep()
    {
        (WorldState world, _, _) = Battle();
        AdvanceToTurn(world, 40);
        world.Messages.Post(world, Keeper, PlayerMessageKind.CombatStarted, 12, 15);
        KeeperAI keeper = NewKeeper(speed: 3);

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // The message sets the flag and reserves a zero-residual event whose dispatch spends
        // the tick, so the fortieth turn passes without a call.
        Assert.True(keeper.State.FightGoingOn);
        Assert.Contains(commands, c => c is AcknowledgePlayerMessageCommand);
        Assert.Null(Call(commands));
        Assert.False(keeper.State.CallToArmsInUse);

        (WorldState twin, _, _) = Battle();
        AdvanceToTurn(twin, 40);
        KeeperAI undisturbed = NewKeeper(speed: 3);
        undisturbed.State.FightGoingOn = true;
        Assert.NotNull(Call(undisturbed.Think(Db, twin)));
    }

    [Theory]
    [InlineData(1, 2, 0, true)]    // the counter runs out this tick
    [InlineData(10, 2, 0, false)]  // two enemies within five tiles, own minimum met
    [InlineData(10, 3, 0, true)]   // fewer enemies than wanted
    [InlineData(10, 2, 3, true)]   // fewer own non-workers than wanted (the fighter and a worker)
    public void TheRemovalTestTakesTheFlagDownOnTheCounterTheEnemyCountOrTheOwnCount(
        int counter, byte enemiesWanted, byte ownWanted, bool removed)
    {
        WorldState world = World();
        Fighter(world, Keeper, 13, 15);
        world.SpawnCreature(Db, KeeperWorld.WorkerCreature, Keeper, new TilePos(14, 14));
        Fighter(world, Hero, 12, 15);
        Fighter(world, Hero, 9, 15);   // four tiles off, inside the strict five-tile range
        Fighter(world, Hero, 8, 12);   // sqrt(25 + 9): outside it
        world.Player(Keeper).CallToArms = new CallToArmsState(true, 1, 13, 15, 0);
        KeeperAI keeper = NewKeeper(p => p with
        {
            RemoveCallToArmsEnemyCreatures = enemiesWanted, RemoveCallToArmsOwnCreatures = ownWanted,
            RemoveCallToArmsWithinRange = 5, WaitBeforeAttackingAgain = 30,
        });
        keeper.State.CallToArmsInUse = true;
        keeper.State.CallToArmsCounter = counter;
        keeper.State.CallToArmsX = CreatureState.CentreOf(13);
        keeper.State.CallToArmsY = CreatureState.CentreOf(15);
        keeper.State.CallToArmsCastTurn = -100;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        Assert.Equal(removed, commands.OfType<RemoveCallToArmsCommand>().Any());
        Assert.Equal(!removed, keeper.State.CallToArmsInUse);
        Assert.Equal(removed ? 120 : 0, keeper.State.CannotAttackCounter);
    }

    [Fact]
    public void AnEnemyHeartWithinRangeCountsAsAnEnemyForTheRemovalTest()
    {
        WorldState world = World();
        Fighter(world, Keeper, 13, 15);
        Fighter(world, Hero, 12, 15);
        KeeperWorld.Set(world, Db, new TilePos(10, 13), KeeperWorld.HeartFloor, Hero);
        world.Player(Keeper).CallToArms = new CallToArmsState(true, 1, 13, 15, 0);
        KeeperAI keeper = NewKeeper(p => p with { RemoveCallToArmsEnemyCreatures = 2, RemoveCallToArmsWithinRange = 5 });
        keeper.State.CallToArmsInUse = true;
        keeper.State.CallToArmsCounter = 10;
        keeper.State.CallToArmsX = CreatureState.CentreOf(13);
        keeper.State.CallToArmsY = CreatureState.CentreOf(15);
        keeper.State.CallToArmsCastTurn = -100;

        // One enemy creature plus the hero heart's stored centre make the two wanted.
        Assert.DoesNotContain(keeper.Think(Db, world), c => c is RemoveCallToArmsCommand);
        Assert.True(keeper.State.CallToArmsInUse);
    }

    [Fact]
    public void TheRemovalWaitsForTheSimulationToShowTheCallAndForgetsARefusedCast()
    {
        WorldState world = World();
        KeeperAI keeper = NewKeeper(p => p with { RemoveCallToArmsEnemyCreatures = 6 });
        keeper.State.CallToArmsInUse = true;
        keeper.State.CallToArmsCounter = 250;
        keeper.State.CallToArmsCastTurn = 1;
        var clock = new GameSimulation(Db, world, 1, Array.Empty<ISimulationSystem>());

        // Turns 1 to 4: the cast is in flight, the removal test holds off (DK2's would remove
        // at once, its cast having been immediate).
        for (int turn = 1; turn <= KeeperBattleStep.CastGraceTurns + 1; turn++)
        {
            Assert.DoesNotContain(keeper.Think(Db, world), c => c is RemoveCallToArmsCommand);
            Assert.True(keeper.State.CallToArmsInUse);
            clock.Tick(new InputFrame(Array.Empty<PlayerCommand>()));
        }
        // Turn 5: still no call in the world, so the cast counts as refused and nothing is issued.
        Assert.DoesNotContain(keeper.Think(Db, world), c => c is RemoveCallToArmsCommand);
        Assert.False(keeper.State.CallToArmsInUse);
        Assert.Equal(0, keeper.State.CannotAttackCounter);
    }

    private static KeeperAI SpellCaster(byte attackingVsDefensive, byte preference, byte useKeeperSpells = 0, ulong seed = 7) =>
        new(Keeper, KeeperWorld.Parameters() with
        {
            UseKeeperSpells = useKeeperSpells, AttackingVsDefensiveSpellPreference = attackingVsDefensive,
            SpellPreference = preference,
        }, new SimRandom(seed));

    [Fact]
    public void TheOffensiveArmCastsThunderboltAtTheTargetOwnersCreatureWithinFiveTilesOnTheKeepersLand()
    {
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 13, 15);
        CreatureState near = Fighter(world, Hero, 14, 15);
        Fighter(world, Hero, 20, 20);
        Engage(world, own, near);
        world.Player(Keeper).Mana = 6001;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Thunderbolt, 3);
        KeeperAI keeper = SpellCaster(attackingVsDefensive: 0, preference: 4);
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // Preference 4 makes every roll succeed and the arm takes the first offensive spell, 6;
        // the far hero is outside five tiles and the near one stands on the keeper's land.
        var cast = Assert.Single(commands.OfType<CastSpellOnCreatureCommand>());
        Assert.Equal((KeeperSpellRules.Thunderbolt, near.Id), (cast.SpellId, cast.CreatureId));
        Assert.Equal(KeeperTask.Decide, keeper.State.Task);
        Assert.Equal(0, keeper.State.UnbuiltSpellChoices);
    }

    [Fact]
    public void AThunderboltTargetOffTheKeepersLandIsRefusedBeforeAnyAllowanceIsSpent()
    {
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 13, 15);
        CreatureState enemy = Fighter(world, Hero, 12, 15);
        Engage(world, own, enemy);
        world.Player(Keeper).Mana = 6001;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Thunderbolt, 3);
        KeeperAI keeper = SpellCaster(attackingVsDefensive: 0, preference: 4);
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // Location 1 wants the target's tile owned by the caster or an ally: unowned floor fails.
        Assert.False(Assert.Single(new KeeperView(Db, world, Keeper).Creatures, c => c.Id == enemy.Id).SpellTargetAllowed);
        Assert.DoesNotContain(commands, c => c is CastSpellOnCreatureCommand);
        Assert.NotEqual(KeeperTask.Decide, keeper.State.Task);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, true)]
    public void SpellPreferenceZeroNeverPassesThunderboltsRollAndFourAlwaysDoes(byte preference, bool cast)
    {
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 13, 15);
        Engage(world, own, Fighter(world, Hero, 14, 15));
        world.Player(Keeper).Mana = 6001;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Thunderbolt, 3);
        bool any = false;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            KeeperAI keeper = SpellCaster(attackingVsDefensive: 0, preference: preference, seed: seed);
            keeper.State.FightGoingOn = true;
            any |= keeper.Think(Db, world).Any(c => c is CastSpellOnCreatureCommand);
        }
        // random(6000 / 2499 + 2) draws 0 to 3: never at least 4 - 0, always at least 4 - 4.
        Assert.Equal(cast, any);
    }

    [Fact]
    public void UseKeeperSpellsFourNeverRollsForASpell()
    {
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 13, 15);
        Engage(world, own, Fighter(world, Hero, 14, 15));
        world.Player(Keeper).Mana = 6001;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Thunderbolt, 3);
        for (ulong seed = 1; seed <= 8; seed++)
        {
            KeeperAI keeper = SpellCaster(attackingVsDefensive: 0, preference: 4, useKeeperSpells: 4, seed: seed);
            keeper.State.FightGoingOn = true;
            Assert.DoesNotContain(keeper.Think(Db, world), c => c is CastSpellOnCreatureCommand);
            Assert.True(keeper.State.FightGoingOn);
        }
    }

    [Fact]
    public void TheSupportiveArmCastsHealAtTheWoundedFighterAndTheSimulationHealsItToItsMaximum()
    {
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 13, 15);
        Engage(world, own, Fighter(world, Hero, 14, 15));
        own.Health = 700; // below half of 1500, Heal's target, and above the retreat's 300 (ADR-0284)
        world.Player(Keeper).Mana = 20000;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Heal, 3);
        KeeperAI keeper = SpellCaster(attackingVsDefensive: 4, preference: 4);
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // random(4) < 4 always takes the supportive arm; Heal, the last eligible supportive spell,
        // stays selected whatever its roll, the scan finds the fighter itself, and the captured
        // validators pass: one cast through action 55, one allowance (ADR-0287).
        var cast = Assert.Single(commands.OfType<CastSpellOnCreatureCommand>());
        Assert.Equal((KeeperSpellRules.Heal, own.Id), (cast.SpellId, cast.CreatureId));
        Assert.Equal((KeeperTask.Decide, 0), (keeper.State.Task, keeper.State.UnbuiltSpellChoices));

        // Applied a tick later: the missing 800 lands, 5000 is paid, and the fighter goes idle.
        new GameSimulation(Db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]).Tick(new InputFrame(commands));
        Assert.Equal((1500, CreatureStateMachine.Idle, 15000), (own.Health, own.Dk2StateId, world.Player(Keeper).Mana));
    }

    [Fact]
    public void AWoundedFighterOnUnownedFloorIsStillHealsTargetWhereThunderboltsLocationRefuses()
    {
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 10, 15); // the unowned dirt west of the patch
        Engage(world, own, Fighter(world, Hero, 11, 15));
        own.Health = 700;
        world.Player(Keeper).Mana = 20000;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Heal, 3);
        KeeperAI keeper = SpellCaster(attackingVsDefensive: 4, preference: 4);
        keeper.State.FightGoingOn = true;

        // Location selector 4 asks no ownership of the tile, only that it is not solid.
        Assert.True(Assert.Single(new KeeperView(Db, world, Keeper).Creatures, c => c.Id == own.Id).HealTargetAllowed);
        var cast = Assert.Single(keeper.Think(Db, world).OfType<CastSpellOnCreatureCommand>());
        Assert.Equal((KeeperSpellRules.Heal, own.Id), (cast.SpellId, cast.CreatureId));
    }

    [Fact]
    public void AWoundedCreatureHeldInTheEnemysPrisonIsChosenButRefusedByTargetOneAndNothingIsSpent()
    {
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 13, 15);
        Engage(world, own, Fighter(world, Hero, 14, 15));
        KeeperWorld.Set(world, Db, new TilePos(10, 15), KeeperWorld.PrisonFloor, Hero);
        CreatureState captive = Fighter(world, Keeper, 10, 15);
        captive.Health = 700;
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, Db), captive, PrisonRules.DecidingState));
        world.Player(Keeper).Mana = 20000;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Heal, 3);
        KeeperAI keeper = SpellCaster(attackingVsDefensive: 4, preference: 4);
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // The scan admits the captive (own, within five tiles, under half), but a creature holding
        // a room id passes target 1 only when that room is the caster's: refused, allowance kept.
        Assert.False(Assert.Single(new KeeperView(Db, world, Keeper).Creatures, c => c.Id == captive.Id).HealTargetAllowed);
        Assert.DoesNotContain(commands, c => c is CastSpellOnCreatureCommand);
        Assert.NotEqual(KeeperTask.Decide, keeper.State.Task);
        Assert.Equal(0, keeper.State.UnbuiltSpellChoices);
    }

    [Theory]
    [InlineData(5000, true)]
    [InlineData(4999, false)]
    public void ManaEqualToHealsCostPassesThePredictedPermissionAndOneLessRefusesItUnspent(int mana, bool cast)
    {
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 13, 15);
        Engage(world, own, Fighter(world, Hero, 14, 15));
        own.Health = 700;
        world.Player(Keeper).Mana = mana;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Heal, 3);
        KeeperAI keeper = SpellCaster(attackingVsDefensive: 4, preference: 4);
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // 0x004C1740: a cost at most the mana passes; the selection itself never looks at mana.
        Assert.Equal(cast, commands.Any(c => c is CastSpellOnCreatureCommand));
        Assert.Equal(cast, keeper.State.Task == KeeperTask.Decide);
    }

    [Fact]
    public void AWoundedCreatureTheRetreatLiftsThisTickIsStillHealsTargetAndIsHealedWhereItLands()
    {
        WorldState world = World();
        CreatureState wounded = Fighter(world, Keeper, 12, 15);
        wounded.Health = 250; // below the retreat's 300: lifted at step 7, before the battle step
        CreatureState own = Fighter(world, Keeper, 13, 15);
        Engage(world, own, Fighter(world, Hero, 14, 15));
        world.Player(Keeper).Mana = 20000;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Heal, 3);
        var keeper = new KeeperAI(Keeper, KeeperWorld.Parameters(speed: 2) with
        {
            UseKeeperSpells = 0, AttackingVsDefensiveSpellPreference = 4, SpellPreference = 4,
        }, new SimRandom(7));
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // DK2's supportive scan skips no held creature and the ordinary arm of target 1 admits it,
        // so the lifted creature is the tick's Heal target; the retreat and the cast each spend one.
        Assert.Equal([wounded.Id], commands.OfType<PickUpCreatureCommand>().Select(c => c.CreatureId));
        Assert.Single(commands.OfType<DropCommand>());
        Assert.Equal((KeeperSpellRules.Heal, wounded.Id), Assert.Single(commands.OfType<CastSpellOnCreatureCommand>()) is var cast ? (cast.SpellId, cast.CreatureId) : default);
        Assert.Equal(KeeperTask.Decide, keeper.State.Task);

        // Applied: the hand puts it down first, then the cast finds it on the ground and heals it.
        new GameSimulation(Db, world, 1, [new HandSystem(), new KeeperSpellSystem(), new ShotSystem()]).Tick(new InputFrame(commands));
        Assert.Equal((1500, false, FightState), (wounded.Health, wounded.InHand, own.Dk2StateId));
    }

    [Fact]
    public void TheOffensiveArmCastsInfernoAtTheTargetsPositionAndTheSimulationBurnsItOnTheKeepersLand()
    {
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 13, 15);
        CreatureState near = Fighter(world, Hero, 14, 15);
        Engage(world, own, near);
        world.Player(Keeper).Mana = 50001;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Inferno, 3);
        KeeperAI keeper = SpellCaster(attackingVsDefensive: 0, preference: 4);
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        // 0x004BB8A0 then 0x004BBD30 at the target's x and y; a passed pair spends the allowance.
        var cast = Assert.Single(commands.OfType<CastSpellAtPlaceCommand>());
        Assert.Equal((KeeperSpellRules.Inferno, near.PosX, near.PosY), (cast.SpellId, cast.X, cast.Y));
        Assert.Equal((KeeperTask.Decide, 0), (keeper.State.Task, keeper.State.UnbuiltSpellChoices));
        int ownBefore = own.Health;
        var sim = new GameSimulation(Db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(cast));
        sim.Run(31);
        // Thirty hits of 100 empty the hero's 1500; the own fighter a whole tile away sits on the
        // strict bound and is spared, where one closer would burn too (KeeperChickenInfernoTests).
        Assert.Equal((0, ownBefore), (near.Health, own.Health));
    }

    [Fact]
    public void AnInfernoTargetOffTheKeepersLandIsRefusedBeforeAnyAllowanceIsSpent()
    {
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 13, 15);
        CreatureState enemy = Fighter(world, Hero, 12, 15);
        Engage(world, own, enemy);
        world.Player(Keeper).Mana = 50001;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Inferno, 3);
        KeeperAI keeper = SpellCaster(attackingVsDefensive: 0, preference: 4);
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        Assert.False(Assert.Single(new KeeperView(Db, world, Keeper).Creatures, c => c.Id == enemy.Id).InfernoPlaceAllowed);
        Assert.DoesNotContain(commands, c => c is CastSpellAtPlaceCommand or CastSpellOnCreatureCommand);
        Assert.NotEqual(KeeperTask.Decide, keeper.State.Task);
    }

    [Fact]
    public void TheOffensiveArmCastsChickenAtTheTargetAndTheSimulationTurnsIt()
    {
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 13, 15);
        CreatureState near = Fighter(world, Hero, 14, 15);
        Engage(world, own, near);
        world.Player(Keeper).Mana = 10001;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Chicken, 3);
        KeeperAI keeper = SpellCaster(attackingVsDefensive: 0, preference: 4);
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);

        var cast = Assert.Single(commands.OfType<CastSpellOnCreatureCommand>());
        Assert.Equal((KeeperSpellRules.Chicken, near.Id), (cast.SpellId, cast.CreatureId));
        Assert.Equal(KeeperTask.Decide, keeper.State.Task);
        new GameSimulation(Db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]).Tick(InputFrame.Of(cast));
        Assert.Equal((240, CreatureActivity.Chicken), (near.ChickenTicks, near.Activity));
    }

    [Fact]
    public void AnUnbuiltOffensiveChoiceIsCountedAndNotIssued()
    {
        const byte unsupportedSpell = 12;
        KeeperSpellDef unbuilt = Db.KeeperSpellById(7)! with
        {
            Name = "Synthetic unsupported", KeeperSpellId = unsupportedSpell,
        };
        var db = new GameDatabase(Db.Terrain.Values, Db.Rooms.Values, Db.Creatures.Values, Db.Variables,
            Db.KeeperSpells.Values.Append(unbuilt), Db.Objects.Values, Db.Doors.Values, Db.Traps.Values,
            Db.CreatureSpells.Values, Db.Shots.Values, Db.Effects.Values, Db.EffectElements.Values);
        WorldState world = World();
        CreatureState own = Fighter(world, Keeper, 13, 15);
        Engage(world, own, Fighter(world, Hero, 14, 15));
        world.Player(Keeper).Mana = 20000;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(KeeperSpellRules.SpellAvailabilityKind, unsupportedSpell, 3));
        KeeperAI keeper = SpellCaster(attackingVsDefensive: 0, preference: 4);
        keeper.State.FightGoingOn = true;

        IReadOnlyList<PlayerCommand> commands = keeper.Think(db, world);

        // The offensive arm's first eligible spell is a synthetic unsupported creature spell; no command
        // casts it here, so nothing is issued and nothing is spent.
        Assert.DoesNotContain(commands, c => c is CastSpellOnCreatureCommand or CastSpellAtPlaceCommand);
        Assert.Equal(1, keeper.State.UnbuiltSpellChoices);
        Assert.NotEqual(KeeperTask.Decide, keeper.State.Task);
    }

    [Fact]
    public void TheObservationCarriesTheBattleFieldsAcrossAPacketAndTheCommandsRepeat()
    {
        (WorldState world, CreatureState own, CreatureState enemy) = Battle();
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, KeeperSpellRules.Thunderbolt, 3);
        AdvanceToTurn(world, 40);
        var captured = new KeeperView(Db, world, Keeper);
        KeeperView restored = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(captured), Db);

        Assert.Equal(20000, restored.Mana);
        KeeperCreatureView fighter = Assert.Single(restored.Creatures, c => c.Id == own.Id);
        Assert.Equal((Hero, true, true, false, true), (fighter.FightTargetOwner, fighter.CallToArmsSiteAllowed, fighter.HeartRouteExists, fighter.SpellTargetAllowed, fighter.HealTargetAllowed));
        KeeperCreatureView foe = Assert.Single(restored.Creatures, c => c.Id == enemy.Id);
        Assert.Equal(((byte)0, false, false, false, false), (foe.FightTargetOwner, foe.CallToArmsSiteAllowed, foe.HeartRouteExists, foe.SpellTargetAllowed, foe.HealTargetAllowed));
        Assert.Equal(captured.AvailabilityRow(5, 3), restored.AvailabilityRow(5, 3));

        Hash64 first = Hash64.Start(), second = Hash64.Start();
        KeeperAI a = NewKeeper(seed: 3), b = NewKeeper(seed: 3);
        a.State.FightGoingOn = b.State.FightGoingOn = true;
        IReadOnlyList<PlayerCommand> fromCapture = a.Think(captured);
        new InputFrame(fromCapture).HashInto(ref first);
        new InputFrame(b.Think(restored)).HashInto(ref second);
        Assert.Equal(first.Value, second.Value);
        Assert.NotNull(Call(fromCapture));
        Assert.True(a.State.CallToArmsInUse && b.State.CallToArmsInUse);
    }

    [Fact]
    public void AnUnaidedKeeperRaisesTheCallThroughTheLiveFightAndTakesItDownWhenTheCounterRunsOut()
    {
        WorldState world = World();
        Fighter(world, Keeper, 13, 15);
        Fighter(world, Hero, 12, 15); // one against one: nobody flees and the fight lasts
        world.Player(Keeper).Mana = 25000;
        world.SetAvailability(Keeper, KeeperSpellRules.SpellAvailabilityKind, CallToArmsRules.SpellId, 3);
        var sim = new GameSimulation(Db, world, 1,
        [
            new HandSystem(), new RescuePrioritySystem(), new KeeperSpellSystem(), new CombatSystem(),
            new ShotSystem(), new CallToArmsSystem(),
        ]);
        KeeperAI keeper = NewKeeper(p => p with { RemoveCallToArmsEnemyCreatures = 0, RemoveCallToArmsOwnCreatures = 0 });
        var log = new List<(long Turn, PlayerCommand Command)>();
        var seen = new List<long>();

        for (int tick = 0; tick < 300; tick++)
        {
            IReadOnlyList<PlayerCommand> commands = keeper.Think(Db, world);
            foreach (PlayerCommand command in commands) log.Add((world.CurrentTick.Value + 1, command));
            if (world.Player(Keeper).CallToArms.On) seen.Add(world.CurrentTick.Value);
            sim.Tick(new InputFrame(commands));
        }

        // The hero's attack sets the fight flag through the combat message; on turn 40 the step
        // casts at the fighter, the simulation raises the call, and 250 turns later the counter
        // takes it down again through the slap.
        Assert.Equal(40, Assert.Single(log, e => e.Command is CastSpellAtPlaceCommand).Turn);
        Assert.Equal(290, Assert.Single(log, e => e.Command is RemoveCallToArmsCommand).Turn);
        Assert.NotEmpty(seen);
        Assert.InRange(seen[0], 40, 42);
        Assert.False(world.Player(Keeper).CallToArms.On);
        Assert.False(keeper.State.CallToArmsInUse);
        // 120 at turn 290, one off on each of turns 291 to 300.
        Assert.Equal(110, keeper.State.CannotAttackCounter);
    }
}
