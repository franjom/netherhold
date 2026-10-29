// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>Call to Arms against the binary's rules (call-to-arms.md, ADR-0279): the cast, the
/// flag's process, who answers, the three heeding states, relocation, the slap, the mana and the
/// gates other mechanics read.</summary>
public sealed class CallToArmsTests
{
    private const byte Keeper = 3, Rival = 4;
    private static readonly TilePos Flag = new(8, 8);

    [Fact]
    public void AnimatedFlagWaitsForAppearanceAndFadeBeforeItsTransitions()
    {
        var (fixture, _) = Setup();
        var slots = new CreatureAnimationTiming[9];
        slots[0] = slots[6] = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 30, FramesPerSecond = 30 };
        var db = new GameDatabase(fixture.Terrain.Values, fixture.Rooms.Values, fixture.Creatures.Values,
            fixture.Variables, keeperSpells: fixture.KeeperSpells.Values,
            objects: fixture.Objects.Values.Select(o => o.ObjectId == 10 ? o with { ArtSlots = slots } : o));
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [0, 1, Keeper, Rival]);
        ObjectState flag = world.SpawnObject(db, 10, Keeper, 168);
        flag.ProcessState = CallToArmsRules.Activating;
        var simulation = new GameSimulation(db, world, 1, [new CallToArmsSystem()]);
        for (int i = 0; i < 4; i++) simulation.Tick();
        Assert.Equal(CallToArmsRules.Activating, flag.ProcessState);
        simulation.Tick();
        Assert.Equal(CallToArmsRules.Recalling, flag.ProcessState);
        Assert.Equal(4, flag.AnimationStartedTick);
        flag.ProcessState = CallToArmsRules.Fading;
        simulation.Tick();
        Assert.Equal(CallToArmsRules.Faded, flag.ProcessState);
        Assert.Contains(flag, world.Objects);
        for (int i = 0; i < 4; i++) simulation.Tick();
        Assert.DoesNotContain(flag, world.Objects);
    }

    private static (GameDatabase Db, WorldState World) Setup(int mana = 20000, bool rivalStrip = false)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < 19; y++)
        {
            for (int x = 1; x < 19; x++)
            {
                ref TileState tile = ref world.Tile(x, y);
                tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
                tile.OwnerPlayerId = rivalStrip && x >= 15 ? Rival : Keeper;
            }
        }

        world.Player(Keeper).Mana = mana;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, CallToArmsRules.SpellId, 3));
        return (db, world);
    }

    private static GameSimulation Sim(GameDatabase db, WorldState world, params ISimulationSystem[] more)
    {
        ISimulationSystem[] core = [new KeeperSpellSystem(), new CreatureMovementSystem(), new ShotSystem(), new CallToArmsSystem()];
        return new GameSimulation(db, world, 1, [.. core, .. more]);
    }

    private static CastSpellAtPlaceCommand Cast(TilePos at, byte player = Keeper) =>
        new(player, CallToArmsRules.SpellId, CreatureState.CentreOf(at.X), CreatureState.CentreOf(at.Y));

    private static CreatureState Goblin(WorldState world, GameDatabase db, int x, int y, byte owner = Keeper) =>
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, owner, new TilePos(x, y));

    private static ObjectState? FlagOf(WorldState world, byte player = Keeper) =>
        world.ObjectById(world.Player(player).CallToArms.ObjectId);

    [Fact]
    public void ACastPaysMakesTheFlagRaisesTheCallAndRecruitsTheOwnersNonWorkersOnly()
    {
        var (db, world) = Setup();
        CreatureState fighter = Goblin(world, db, 10, 10);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(11, 10));
        CreatureState foreign = Goblin(world, db, 12, 12, Rival);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world);
        sim.ObserveWith(sink);

        sim.Tick(InputFrame.Of(Cast(Flag)));

        Assert.Equal(10000, world.Player(Keeper).Mana);
        Assert.Equal(10000, world.Player(Keeper).OneShotManaSpent);
        Assert.Equal((0L, 0L), (Assert.Single(world.Player(Keeper).Availabilities).LastCastTick, Assert.Single(world.Player(Keeper).Availabilities).LastCheckedTick));
        Assert.Empty(world.Shots);
        ObjectState flag = Assert.Single(world.Objects, o => o.ObjectTypeId == CallToArmsRules.ObjectTypeId);
        Assert.Equal((Keeper, 8 * 20 + 8), (flag.OwnerPlayerId, flag.TileIndex));
        CallToArmsState call = world.Player(Keeper).CallToArms;
        Assert.True(call.On);
        Assert.Equal((flag.Id, Flag), (call.ObjectId, call.Tile));
        // Turn 1 plus 10000 / 90 seconds of four turns: the prepaid period (0x004C1A90).
        Assert.Equal(1u + 111 * 4, call.ManaTick);
        Assert.Equal(CallToArmsRules.Recalling, flag.ProcessState);
        Assert.Equal((CallToArmsRules.GoingState, true), (fighter.Dk2StateId, fighter.HeedingCallToArms));
        Assert.Equal(CreatureActivity.HeedingCallToArms, fighter.Activity);
        Assert.False(imp.HeedingCallToArms);
        Assert.False(foreign.HeedingCallToArms);
        Assert.Contains(sink.Events, e => e.Kind == "call_to_arms_raised");
    }

    [Theory]
    [InlineData(0, KeeperSpellRefusal.UnsupportedSpell)]
    [InlineData(1, KeeperSpellRefusal.UnsupportedFlags)]
    [InlineData(2, KeeperSpellRefusal.NoSpellEntry)]
    [InlineData(3, KeeperSpellRefusal.Mana)]
    [InlineData(4, KeeperSpellRefusal.Location)]
    [InlineData(5, KeeperSpellRefusal.None)]
    public void RefusalsFollowThePositionHelperAndLocationFourIgnoresOwnership(int change, KeeperSpellRefusal expected)
    {
        var (db, world) = Setup(rivalStrip: true);
        CastSpellAtPlaceCommand cast = Cast(Flag);
        switch (change)
        {
            case 0: cast = cast with { SpellId = 6 }; break;
            case 1: cast = cast with { Flags = 2 }; break;
            case 2: world.Player(Keeper).Availabilities.Clear(); break;
            case 3: world.Player(Keeper).Mana = 9999; break;
            case 4: world.Tile(Flag).TerrainId = Dk2TerrainFixture.Rock; break;
            case 5: cast = Cast(new TilePos(16, 8)); break; // the rival's land
        }
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world);
        sim.ObserveWith(sink);
        int mana = world.Player(Keeper).Mana;
        sim.Tick(InputFrame.Of(cast));
        SimEvent verdict = Assert.Single(sink.Events, e => e.System == "keeper_spells");
        Assert.Contains($"reason={expected}", verdict.Detail);
        if (expected != KeeperSpellRefusal.None)
        {
            Assert.Equal(mana, world.Player(Keeper).Mana);
            Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == CallToArmsRules.ObjectTypeId);
            Assert.False(world.Player(Keeper).CallToArms.On);
        }
        else
        {
            Assert.True(world.Player(Keeper).CallToArms.On);
        }
    }

    [Theory]
    [InlineData(RoomTypeId.Prison, false)]
    [InlineData(RoomTypeId.Prison, true)]
    [InlineData(RoomTypeId.Portal, false)]
    [InlineData(RoomTypeId.Portal, true)]
    public void TargetFourRefusesOnlyTheCastersOwnSpecialRoomInteriors(RoomTypeId kind, bool foreign)
    {
        var (db, world) = Setup();
        byte terrain = db.Rooms.Values.Single(r => r.TypeId == kind).TerrainId;
        for (int y = 6; y <= 10; y++)
        {
            for (int x = 6; x <= 10; x++)
            {
                world.Tile(x, y).TerrainId = terrain;
                world.Tile(x, y).OwnerPlayerId = foreign ? Rival : Keeper;
            }
        }

        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world);
        sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(Cast(Flag)));
        string expected = foreign ? "reason=None" : "reason=Target";
        Assert.Contains(expected, Assert.Single(sink.Events, e => e.System == "keeper_spells").Detail);
    }

    [Fact]
    public void TheHeedingCreatureWalksToTheFlagAndMillsWithinTwoTilesOfIt()
    {
        var (db, world) = Setup();
        CreatureState fighter = Goblin(world, db, 14, 14);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Cast(Flag)));
        Assert.Equal(CallToArmsRules.GoingState, fighter.Dk2StateId);
        sim.Tick();
        // 190 routed and handed over to the walk resuming 191.
        Assert.Equal((6, CallToArmsRules.ArrivedState), (fighter.Dk2StateId, fighter.UtilityNewState));
        Assert.Equal(CreatureActivity.HeedingCallToArms, fighter.Activity);

        bool milled = false;
        for (int tick = 0; tick < 400; tick++)
        {
            sim.Tick();
            int resolved = NeedRules.ResolvedStateId(fighter);
            if (resolved == CallToArmsRules.MillingState)
            {
                milled = true;
                Assert.True(Math.Max(Math.Abs(fighter.Tile.X - Flag.X), Math.Abs(fighter.Tile.Y - Flag.Y)) <= 2,
                    $"tick {tick}: at {fighter.Tile}");
            }
            Assert.True(fighter.HeedingCallToArms);
        }
        Assert.True(milled);
        Assert.Equal(CallToArmsRules.Recalling, FlagOf(world)!.ProcessState);
    }

    [Fact]
    public void AnUnreachableFlagIsNotRecruitedForAndIdleReportsMessage84()
    {
        var (db, world) = Setup();
        // The flag stands on an island of floor inside rock.
        for (int y = 6; y <= 10; y++)
        {
            for (int x = 6; x <= 10; x++)
            {
                if (x != 8 || y != 8)
                {
                    world.Tile(x, y).TerrainId = Dk2TerrainFixture.Rock;
                }
            }
        }

        CreatureState fighter = Goblin(world, db, 14, 14);
        Dk2TerrainFixture.SetKnownIdentities(world);
        GameSimulation sim = Sim(db, world, new CreatureJobSystem());
        sim.Tick(InputFrame.Of(Cast(Flag)));
        Assert.True(world.Player(Keeper).CallToArms.On);
        Assert.False(fighter.HeedingCallToArms);
        sim.Run(2);
        PlayerMessage message = Assert.Single(world.Messages.Entries, m => m.Kind == PlayerMessageKind.CallToArmsUnreachable);
        Assert.Equal((Keeper, 0, 0, (uint)SimulationTiming.TicksFromSeconds(50)), (message.PlayerId, message.Data1, message.Data2, message.Timeout));
        Assert.False(fighter.HeedingCallToArms);
        // 89 goes on past the message to its chooser and fallback, 3, which wanders in 87 or plays
        // an idle action (ADR-0574, ADR-0228): not walking to the flag.
        Assert.Contains(fighter.Dk2StateId, new[] { CreatureJobSystem.DoingNothingState, ImpStates.Wandering });
        Assert.False(fighter.HeedingCallToArms);
    }

    [Fact]
    public void RecastingElsewhereFadesTheOldFlagMovesTheCallAndKeepsThePrepaidClock()
    {
        var (db, world) = Setup();
        CreatureState fighter = Goblin(world, db, 10, 10);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world);
        sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(Cast(Flag)));
        int first = world.Player(Keeper).CallToArms.ObjectId;
        uint prepaid = world.Player(Keeper).CallToArms.ManaTick;
        sim.Run(5);

        var elsewhere = new TilePos(14, 14);
        sim.Tick(InputFrame.Of(Cast(elsewhere)));

        Assert.Equal(0, world.Player(Keeper).Mana);
        CallToArmsState call = world.Player(Keeper).CallToArms;
        Assert.True(call.On);
        Assert.NotEqual(first, call.ObjectId);
        Assert.Equal((elsewhere, prepaid), (call.Tile, call.ManaTick));
        // The old flag fades over the next two ticks (20, then 19) and is gone.
        sim.Run(2);
        Assert.Null(world.ObjectById(first));
        Assert.Contains(sink.Events, e => e.Kind == "call_to_arms_faded");
        Assert.Single(world.Objects, o => o.ObjectTypeId == CallToArmsRules.ObjectTypeId);
        Assert.True(fighter.HeedingCallToArms);
        for (int tick = 0; tick < 400; tick++)
        {
            sim.Tick();
        }

        Assert.True(Math.Max(Math.Abs(fighter.Tile.X - elsewhere.X), Math.Abs(fighter.Tile.Y - elsewhere.Y)) <= 2, $"at {fighter.Tile}");
    }

    [Fact]
    public void RecastingWhileTheFlagIsInTheHandSpendsTheManaAndMakesNoFlag()
    {
        var (db, world) = Setup();
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, new HandSystem());
        sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(Cast(Flag)));
        int first = world.Player(Keeper).CallToArms.ObjectId;
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, first)));
        Assert.True(world.ObjectById(first)!.InHand);

        sim.Tick(InputFrame.Of(Cast(new TilePos(14, 14))));

        Assert.Equal(0, world.Player(Keeper).Mana);
        Assert.Contains(sink.Events, e => e.Kind == "shot_object_refused");
        Assert.Equal(first, world.Player(Keeper).CallToArms.ObjectId);
        Assert.Single(world.Objects, o => o.ObjectTypeId == CallToArmsRules.ObjectTypeId);
    }

    [Fact]
    public void ASlapTakesTheFlagDownAndReleasesTheCreaturesToIdle()
    {
        var (db, world) = Setup();
        CreatureState fighter = Goblin(world, db, 10, 10);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world);
        sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(new RemoveCallToArmsCommand(Keeper)));
        Assert.Contains(sink.Events, e => e.Kind == "refused" && e.Detail.Contains("remove_call_to_arms"));

        sim.Tick(InputFrame.Of(Cast(Flag)));
        sim.Run(3);
        Assert.True(fighter.HeedingCallToArms);
        sim.Tick(InputFrame.Of(new RemoveCallToArmsCommand(Keeper)));

        CallToArmsState call = world.Player(Keeper).CallToArms;
        Assert.False(call.On);
        Assert.Equal(0u, call.ManaTick);
        Assert.DoesNotContain(world.Objects, o => o.ObjectTypeId == CallToArmsRules.ObjectTypeId);
        Assert.False(fighter.HeedingCallToArms);
        Assert.Equal(CreatureStateMachine.Idle, fighter.Dk2StateId);
        Assert.Contains(sink.Events, e => e.Kind == "call_to_arms_removed");
    }

    [Fact]
    public void UpkeepStartsAfterThePrepaidTurnsAndTheUpgradeCutsItToSeventyPercent()
    {
        var (db, world) = Setup();
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Cast(Flag)));
        int keeper = Enumerable.Range(0, world.PlayerCount).Single(i => world.PlayerAtIndex(i).PlayerId == Keeper);
        Assert.Equal(0, ManaRules.UpkeepPerSecondByPlayerIndex(world, db)[keeper]);
        sim.Run(443);
        Assert.Equal(0, ManaRules.UpkeepPerSecondByPlayerIndex(world, db)[keeper]);
        sim.Tick();
        Assert.Equal(90, ManaRules.UpkeepPerSecondByPlayerIndex(world, db)[keeper]);
        world.SetUpgraded(Keeper, AvailabilityKind.Spell, CallToArmsRules.SpellId, true);
        Assert.Equal(63, ManaRules.UpkeepPerSecondByPlayerIndex(world, db)[keeper]);
        world.RemoveObject(world.Player(Keeper).CallToArms.ObjectId);
        Assert.Equal(0, ManaRules.UpkeepPerSecondByPlayerIndex(world, db)[keeper]);
    }

    [Fact]
    public void AnUpkeepTheManaCannotCoverFadesTheFlagButLeavesTheCallOn()
    {
        var (db, world) = Setup();
        CreatureState fighter = Goblin(world, db, 10, 10);
        var sink = new ListEventSink();
        GameSimulation sim = Sim(db, world, new ManaSystem());
        sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(Cast(Flag)));
        // Little land, so the second's income cannot cover the 90 the flag now costs.
        for (int y = 1; y < 19; y++)
        {
            for (int x = 1; x < 19; x++)
            {
                if (Math.Max(Math.Abs(x - 10), Math.Abs(y - 10)) > 1)
                {
                    world.Tile(x, y).OwnerPlayerId = Rival;
                }
            }
        }

        ref PlayerState player = ref world.Player(Keeper);
        player.CallToArms = player.CallToArms with { ManaTick = 1 };
        player.Mana = 0;
        int flag = player.CallToArms.ObjectId;

        sim.Run(4);

        Assert.Contains(sink.Events, e => e.Kind == "call_to_arms_unaffordable");
        Assert.Null(world.ObjectById(flag));
        // DK2's release for a missing flag sits behind a guard that needs the flag (§5): the
        // call stays on, nobody is released and no upkeep is charged.
        Assert.True(world.Player(Keeper).CallToArms.On);
        Assert.True(fighter.HeedingCallToArms);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "call_to_arms_removed");
        int keeper = Enumerable.Range(0, world.PlayerCount).Single(i => world.PlayerAtIndex(i).PlayerId == Keeper);
        Assert.Equal(0, ManaRules.UpkeepPerSecondByPlayerIndex(world, db)[keeper]);
        // A new cast takes the dead flag's place.
        world.Player(Keeper).Mana = 10000;
        sim.Tick(InputFrame.Of(Cast(new TilePos(12, 12))));
        Assert.NotEqual(flag, world.Player(Keeper).CallToArms.ObjectId);
        Assert.Equal(new TilePos(12, 12), world.Player(Keeper).CallToArms.Tile);
    }

    [Fact]
    public void TheCallGatesJobsNeedsTheFightRadiusAndTheDropLanding()
    {
        var (db, world) = Setup();
        CreatureState near = Goblin(world, db, 9, 9);
        CreatureState far = Goblin(world, db, 16, 16);
        GameSimulation sim = Sim(db, world);
        sim.Tick(InputFrame.Of(Cast(Flag)));
        Assert.True(CallToArmsRules.IsUnderCallToArms(world, near));
        Assert.True(NeedRules.IsBusy(world, near, sim.World.CurrentTick.Value));
        Assert.True(CombatRules.MayLookForAFight(world, db, near, force: false));
        Assert.False(CombatRules.MayLookForAFight(world, db, far, force: false));
        Assert.True(CombatRules.MayLookForAFight(world, db, far, force: true));
        // Exactly five tiles away passes; a hair farther refuses.
        far.PosX = CreatureState.CentreOf(13); far.PosY = CreatureState.CentreOf(8);
        Assert.True(CombatRules.MayLookForAFight(world, db, far, force: false));
        far.PosX += 1;
        Assert.False(CombatRules.MayLookForAFight(world, db, far, force: false));
        Assert.True(CallToArmsRules.IsUnderCallToArms(world, far));
    }

    [Fact]
    public void TwoKeepersKeepIndependentCallsAndASaveContinuesThem()
    {
        var (db, world) = Setup(rivalStrip: true);
        world.Player(Rival).Mana = 20000;
        world.Player(Rival).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, CallToArmsRules.SpellId, 3));
        CreatureState ours = Goblin(world, db, 10, 10);
        CreatureState theirs = Goblin(world, db, 16, 10, Rival);
        GameSimulation sim = Sim(db, world, new ManaSystem());
        sim.Tick(new InputFrame([Cast(Flag), Cast(new TilePos(17, 3), Rival)]));

        Assert.Equal((Flag, new TilePos(17, 3)), (world.Player(Keeper).CallToArms.Tile, world.Player(Rival).CallToArms.Tile));
        Assert.NotEqual(world.Player(Keeper).CallToArms.ObjectId, world.Player(Rival).CallToArms.ObjectId);
        Assert.True(ours.HeedingCallToArms && theirs.HeedingCallToArms);
        sim.Run(3);

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db,
            [new KeeperSpellSystem(), new CreatureMovementSystem(), new ShotSystem(), new CallToArmsSystem(), new ManaSystem()]);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.Equal(world.Player(Keeper).CallToArms, copy.World.Player(Keeper).CallToArms);
        ObjectState flag = copy.World.ObjectById(copy.World.Player(Keeper).CallToArms.ObjectId)!;
        Assert.Equal((CallToArmsRules.Recalling, FlagOf(world)!.ProcessDeadline), (flag.ProcessState, flag.ProcessDeadline));
        Assert.True(copy.World.CreatureById(ours.Id)!.HeedingCallToArms);
        for (int i = 0; i < 40; i++) { sim.Tick(); copy.Tick(); }
        Assert.Equal(sim.StateHash(), copy.StateHash());

        copy.World.CreatureById(ours.Id)!.HeedingCallToArms = false;
        Assert.NotEqual(sim.StateHash(), copy.StateHash());
    }
}
