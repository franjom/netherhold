// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The keeper's Heal (spell 10) through action 55: <c>0x004BBA40</c>'s validators with
/// location selector 4 and target selector 1, the cast helper's maximum-health refusal, shot 4's
/// forced collision and the Modify Health process (keeper-spell-casting.md §4, ADR-0287).</summary>
public class KeeperHealTests
{
    private const byte Keeper = 3, Rival = 4;
    private const int FightState = 199;

    [Theory]
    [InlineData(false, 500)]
    [InlineData(true, 700)]
    public void TheCastHealsTheLesserOfTheDataWordAndTheMissingHealthPaysAndStampsBothClocks(bool upgraded, int amount)
    {
        var (db, world, target) = Setup(Spell() with { ShotData1 = 500, BonusShotData1 = 700 });
        world.SetUpgraded(Keeper, 5, KeeperSpellRules.Heal, upgraded);
        target.Health = 300;
        var boundary = new Boundary();
        var sim = new GameSimulation(db, world, 9, [boundary, new ShotSystem()]);
        sim.Run(7);

        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));

        // The helper's return is zero after success, as Thunderbolt's is; the shot stood on its
        // target, hit it through the forced word this tick and died on it.
        Assert.Equal((0, KeeperSpellRefusal.None), (boundary.Result, boundary.Refusal));
        Assert.Equal(300 + amount, target.Health);
        Assert.Empty(world.Shots);
        Assert.Equal((7000, 5000), (world.Player(Keeper).Mana, world.Player(Keeper).OneShotManaSpent));
        var row = Assert.Single(world.Player(Keeper).Availabilities);
        Assert.Equal((7L, 7L), (row.LastCastTick, row.LastCheckedTick));
    }

    [Fact]
    public void TheShippedWordExceedsAnyMissingHealthSoTheHealLandsExactlyTheDeficitAndClampsAtTheMaximum()
    {
        var (db, world, target) = Setup();
        target.Health = 1;
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.ObserveWith(sink);

        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));

        Assert.Equal(1500, target.Health);
        SimEvent healed = Assert.Single(sink.Events, e => e.Kind == "healed");
        Assert.Contains("amount=1499 landed=1499 health=1500", healed.Detail);
    }

    [Theory]
    [InlineData(0, KeeperSpellRefusal.UnsupportedSpell)]
    [InlineData(1, KeeperSpellRefusal.UnsupportedFlags)]
    [InlineData(2, KeeperSpellRefusal.Mana)]
    [InlineData(3, KeeperSpellRefusal.MissingCreature)]
    [InlineData(4, KeeperSpellRefusal.Target)]
    [InlineData(5, KeeperSpellRefusal.Target)]
    [InlineData(6, KeeperSpellRefusal.Location)]
    [InlineData(7, KeeperSpellRefusal.TargetAtMaxHealth)]
    [InlineData(8, KeeperSpellRefusal.Target)]
    public void ARefusalChangesNeitherManaClocksNorShotsNorTheTarget(int change, KeeperSpellRefusal expected)
    {
        var (db, world, target) = Setup();
        byte spell = KeeperSpellRules.Heal; int id = target.Id; ushort flags = 0;
        switch (change)
        {
            case 0: spell = byte.MaxValue; break; // Synthetic unsupported spell.
            case 1: flags = 4; break;
            case 2: world.Player(Keeper).Mana = 4999; break;
            case 3: id = 999; break;
            case 4: target.OwnerPlayerId = Rival; break;
            case 5: target.OwnerPlayerId = PlayerIds.Neutral; break;
            case 6: world.Tile(target.Tile).TerrainId = Dk2TerrainFixture.Rock; break;
            case 7: target.Health = 1500; break; // 0x004BC13C: at the maximum, refused before paying
            case 8: target.CreatureTypeId = HornyCreatureId; break; // IsHorny, the ordinary arm's exclusion
        }
        int health = target.Health, mana = world.Player(Keeper).Mana;
        var boundary = new Boundary();

        new GameSimulation(db, world, 1, [boundary, new ShotSystem()]).Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, spell, id, flags)));

        Assert.Equal((expected, 0), (boundary.Refusal, boundary.Result));
        Assert.Empty(world.Shots);
        Assert.Equal((mana, 0), (world.Player(Keeper).Mana, world.Player(Keeper).OneShotManaSpent));
        Assert.Equal((0L, 0L), (Assert.Single(world.Player(Keeper).Availabilities).LastCastTick, Assert.Single(world.Player(Keeper).Availabilities).LastCheckedTick));
        Assert.Equal(health, target.Health);
    }

    [Fact]
    public void ManaEqualToTheCostPassesAndTheOwnTargetsFloorMayBelongToAnybody_UnlikeThunderbolts()
    {
        var (db, world, target) = Setup();
        world.Player(Keeper).Mana = 5000;
        world.Tile(target.Tile).OwnerPlayerId = Rival;
        var boundary = new Boundary();

        new GameSimulation(db, world, 1, [boundary, new ShotSystem()]).Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));

        // 0x004C1740 passes a cost at most the mana; location 4 asks no ownership, only that the
        // navigation grid's solid bit is clear.
        Assert.Equal(KeeperSpellRefusal.None, boundary.Refusal);
        Assert.Equal((0, 1500), (world.Player(Keeper).Mana, target.Health));
    }

    [Fact]
    public void ACooldownRefusesUntilTheRawSecondsHaveElapsedAndEqualityIsReady()
    {
        var (db, world, target) = Setup(Spell() with { CooldownRaw = 0x1000 });
        world.Player(Keeper).Mana = 100000;
        var boundary = new Boundary();
        var sim = new GameSimulation(db, world, 1, [boundary, new ShotSystem()]);
        var cast = new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id);

        sim.Run(3); // a last cast at tick 0 would read as never cast
        sim.Tick(InputFrame.Of(cast));
        Assert.Equal(KeeperSpellRefusal.None, boundary.Refusal);
        var refusals = new List<KeeperSpellRefusal>();
        for (int i = 0; i < SimulationTiming.OriginalTicksPerGameSecond; i++)
        {
            target.Health = 300;
            sim.Tick(InputFrame.Of(cast));
            refusals.Add(boundary.Refusal);
        }

        // One raw second is ticksPerSecond turns: three refusals, then the fourth cast lands.
        Assert.Equal([KeeperSpellRefusal.Cooldown, KeeperSpellRefusal.Cooldown, KeeperSpellRefusal.Cooldown, KeeperSpellRefusal.None], refusals);
    }

    [Theory]
    [InlineData(true, true, false, KeeperSpellRefusal.None)]   // a Rival's captive in the keeper's Prison: the room's owner decides
    [InlineData(false, true, false, KeeperSpellRefusal.Target)] // the keeper's own captive in the Rival's Prison: refused
    [InlineData(true, false, true, KeeperSpellRefusal.None)]   // a Rival's Combat Pit fighter standing on the keeper's room
    [InlineData(true, false, false, KeeperSpellRefusal.Target)] // the same creature without the mark
    public void TargetOneAdmitsByTheRoomsOwnerForAnAttachedCreatureAndAPitFighterOnTheRoom(bool foreign, bool attached, bool pitFighter, KeeperSpellRefusal expected)
    {
        var (db, world, target) = Setup();
        byte roomOwner = foreign ? Keeper : Rival;
        byte prison = db.Rooms.Values.Single(r => r.TypeId == RoomTypeId.Prison).TerrainId;
        for (int y = 9; y <= 11; y++)
            for (int x = 9; x <= 11; x++)
            {
                world.Tile(x, y).TerrainId = prison;
                world.Tile(x, y).OwnerPlayerId = roomOwner;
            }
        target.OwnerPlayerId = foreign ? Rival : Keeper;
        if (attached) Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), target, PrisonRules.DecidingState));
        target.CombatPitFighter = pitFighter;
        var boundary = new Boundary();

        new GameSimulation(db, world, 1, [boundary, new ShotSystem()]).Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));

        Assert.Equal(expected, boundary.Refusal);
        Assert.Equal(expected == KeeperSpellRefusal.None ? 1500 : 300, target.Health);
    }

    [Fact]
    public void ADyingTargetPassesThePermissionConsumesManaAndIsNotHealed()
    {
        var (db, world, target) = Setup();
        target.Dk2StateId = CreatureDeathRules.DyingState;
        target.Health = 0;
        target.DyingTicks = 19;

        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()])
            .Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));

        // The live tag passes 0x004BBA40 and 0 is below the maximum, so the shot is made and paid
        // for; the health routine then refuses a positive delta on the dying flag (0x004B2D9F).
        Assert.Equal(7000, world.Player(Keeper).Mana);
        Assert.Equal((0, 19, CreatureDeathRules.DyingState), (target.Health, target.DyingTicks, target.Dk2StateId));
    }

    [Fact]
    public void TheHealSetsAFightingTargetIdleBeforeTheHealthLands()
    {
        var (db, world, target) = Setup();
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), target, FightState));
        target.FightTargetId = enemy.Id;
        target.FightTargetKind = FightTargetKind.Creature;

        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()])
            .Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));

        // 0x0048AD30(1) runs before the health routine (rebellion.md §7.2): the fight is interrupted.
        Assert.Equal((CreatureStateMachine.Idle, 1500), (target.Dk2StateId, target.Health));
    }

    [Theory]
    [InlineData(0)] // frozen
    [InlineData(2)] // a Combat Pit fighter
    [InlineData(3)] // attacking abnormal targets
    public void TheIdleIsSkippedForAFrozenHeldPitOrAbnormalTargetingCreatureButTheHealStillLands(int which)
    {
        var (db, world, target) = Setup();
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(11, 10));
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), target, FightState));
        target.FightTargetId = enemy.Id;
        target.FightTargetKind = FightTargetKind.Creature;
        switch (which)
        {
            case 0: target.FrozenTicks = 10; break;
            case 1: target.InHand = true; break;
            case 2: target.CombatPitFighter = true; break;
            case 3: target.AttackAbnormalTargets = true; break;
        }

        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()])
            .Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));

        Assert.Equal((FightState, 1500), (target.Dk2StateId, target.Health));
    }

    /// <summary>0x004ABD60: a forced hit wants its target on the map (layer +0x16 not 2), so a Heal at
    /// a creature in the hand costs its mana and lands nothing that tick (keeper-spell-inferno-chicken.md).</summary>
    [Fact]
    public void AForcedHealMissesACreatureInTheHand()
    {
        var (db, world, target) = Setup();
        target.InHand = true;
        int mana = world.Player(Keeper).Mana;

        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()])
            .Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));

        Assert.Equal(300, target.Health);
        Assert.True(world.Player(Keeper).Mana < mana);
    }

    [Fact]
    public void TheIdleDropsTheTargetsWalkSoItStopsWhereTheHealFoundIt()
    {
        var (db, world, target) = Setup();
        Walk(target, 16);

        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new CreatureMovementSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));
        Assert.Equal((CreatureStateMachine.Idle, 1500, false), (target.Dk2StateId, target.Health, target.IsRouted));
        sim.Run(12);

        // No route to follow: the body brakes where the heal found it, four tiles short of the aim.
        Assert.Equal(10, target.Tile.X);
    }

    [Theory]
    [InlineData(0)] // frozen: the idle is skipped and the walk goes on
    [InlineData(1)] // a Combat Pit fighter
    public void ASkippedIdleLeavesTheWalkRunning(int which)
    {
        var (db, world, target) = Setup();
        Walk(target, 16);
        if (which == 0) target.FrozenTicks = 100; else target.CombatPitFighter = true;

        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new CreatureMovementSystem(), new ShotSystem()]);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));
        Assert.Equal((1500, true), (target.Health, target.IsRouted));
        sim.Run(12);

        Assert.True(target.Tile.X > 10, $"the creature stood still at {target.Tile}");
    }

    [Fact]
    public void AKindOnlyAttackableByHornyPaysForTheHealAndGainsNothing()
    {
        var (db, world, target) = Setup();
        target.CreatureTypeId = OnlyHornyCreatureId;
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);
        sim.ObserveWith(sink);

        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));

        // The permission and the cast helper pass, so the mana goes and the shot hits; the health
        // routine (0x004B2CE0) then refuses the kind for a positive delta as it does for a negative.
        Assert.Equal((7000, 300, CreatureStateMachine.Idle), (world.Player(Keeper).Mana, target.Health, target.Dk2StateId));
        Assert.Contains("amount=1200 landed=0 health=300", Assert.Single(sink.Events, e => e.Kind == "healed").Detail);
    }

    [Fact]
    public void TheForcedCollisionHitsTheTargetAndNotTheCreatureStackedOnItThatTheFinderWouldTake()
    {
        // Spawned first, so the overlap finder's equal-distance tie would keep it; owned by the
        // Rival so collide class 3 admits it.
        CreatureState stacked = null!;
        var (db, world, target) = Setup(before: (d, w) => stacked = w.SpawnCreature(d, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 10)));
        stacked.Health = 300;

        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()])
            .Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));

        Assert.Equal((1500, 300), (target.Health, stacked.Health));
    }

    [Fact]
    public void TheForcedWordSurvivesSaveLoadAndIsSpentOnTheFirstCollisionCheck()
    {
        var (db, world, target) = Setup();
        var boundary = new Boundary();
        var sim = new GameSimulation(db, world, 1, [boundary]);
        sim.Tick(InputFrame.Of(new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Heal, target.Id)));
        ShotState pending = Assert.Single(world.Shots);
        Assert.True(pending.ForcedCollision);

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new ShotSystem()]);
        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.True(Assert.Single(copy.World.Shots).ForcedCollision);
        pending.ForcedCollision = false;
        Assert.NotEqual(sim.StateHash(), copy.StateHash());

        // The word is spent by the first collision check, which hits the target through it.
        copy.Tick();
        Assert.Equal(1500, copy.World.CreatureById(target.Id)!.Health);
        Assert.Empty(copy.World.Shots);
    }

    private sealed class Boundary : ISimulationSystem
    {
        public string Name => "boundary_probe";
        public int Result = -1;
        public KeeperSpellRefusal Refusal;
        public void Execute(SimulationContext context)
        {
            foreach (var command in context.Input.All.OfType<CastSpellOnCreatureCommand>())
                Result = KeeperSpellRules.CastOnCreature(context, command, out Refusal);
        }
    }

    /// <summary>KeeperSpells.kwd record 10 as shipped: shot 4 with 16000, upgraded 25000, 5000 mana,
    /// no cooldown, location 4, target 1, the supportive flag.</summary>
    private static KeeperSpellDef Spell() => new()
    {
        Name = "Heal", KeeperSpellId = KeeperSpellRules.Heal, ManaCost = 5000, ManaUpkeepPerSecond = 0,
        ShotTypeId = 4, ShotData1 = 16000, BonusShotTypeId = 4, BonusShotData1 = 25000,
        LocationSelector = 4, TargetSelector = 1, Flags = KeeperSpellFlags.Supportive,
    };

    /// <summary>Shots.kwd record 4 as shipped: process 3 on the thing struck, flags 0x23, collide
    /// class 3, 128 health, speed 3686, no death shot.</summary>
    private static ShotDef Shot() => new(new byte[239])
    {
        Name = "Modify Health", ShotId = 4, MeshResource = new("", 0, default, 0, 0, 0, 0),
        Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0), SoundCategory = "", Speed = 3686,
        Radius = 819, Health = 128, Flags = 0x23, ShotProcessFlags = 0x4, CollideType = 3, ProcessType = 3, AttackCategory = 9,
    };

    /// <summary>A Goblin-shaped kind with <c>IsHorny</c>, which target 1's ordinary arm refuses, and
    /// one with <c>OnlyAttackableByHorny</c>, which the health routine refuses.</summary>
    private const byte HornyCreatureId = 40, OnlyHornyCreatureId = 41;

    /// <summary>A straight route east along the claimed row, as a walking state's would be loaded.</summary>
    private static void Walk(CreatureState creature, int toX)
    {
        var aim = new DK2.Simulation.Steering.Fixed16Vector(CreatureState.CentreOf(toX) << 4, creature.PosY << 4);
        DK2.Simulation.Steering.RouteWindow.Load(creature.Body, [aim], aim, 0);
    }

    private static (GameDatabase, WorldState, CreatureState) Setup(KeeperSpellDef? spell = null, Action<GameDatabase, WorldState>? before = null)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        CreatureDef goblin = basis.CreatureById(Dk2TerrainFixture.GoblinCreatureId);
        CreatureDef horny = goblin with { Name = "Horny", CreatureId = HornyCreatureId, Flags = goblin.Flags | CreatureFlags.IsHorny };
        CreatureDef onlyHorny = goblin with { Name = "Stone", CreatureId = OnlyHornyCreatureId, Flags = goblin.Flags | CreatureFlags.OnlyAttackableByHorny };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, [.. basis.Creatures.Values, horny, onlyHorny],
            basis.Variables, [spell ?? Spell()], objects: basis.Objects.Values, shots: [Shot()]);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        world.Player(Keeper).Mana = 12000;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(5, KeeperSpellRules.Heal, 3));
        for (int y = 8; y <= 12; y++)
            for (int x = 8; x <= 12; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = Keeper;
            }
        before?.Invoke(db, world);
        CreatureState target = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(10, 10));
        target.Health = 300;
        return (db, world, target);
    }
}
