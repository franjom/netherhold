// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The keeper's Chicken (spell 11, shot 10, process 15, state 96) and Inferno (spell 9,
/// shot 15, process 30), as docs/research/keeper-spell-inferno-chicken.md reads them.</summary>
public class KeeperChickenInfernoTests
{
    private const byte Keeper = 3, Rival = 4;
    private const byte ImmuneId = 40, SalamanderId = 41, FireflyId = 42, BoltId = 99;

    // ------------------------------------------------------------------ Chicken

    [Fact]
    public void AChickenedEnemyCarriesSixtySecondsEntersStateNinetySixAndPaysTheMana()
    {
        var (db, world) = Setup();
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 10));

        Tick(db, world, new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Chicken, enemy.Id));

        Assert.Equal((240, ChickenRules.ChickenState, CreatureActivity.Chicken), (enemy.ChickenTicks, enemy.Dk2StateId, enemy.Activity));
        Assert.Equal(100000 - 10000, world.Player(Keeper).Mana);
        Assert.Empty(world.Shots);
        Assert.False(CombatRules.MayLookForAFight(world, db, enemy, force: false));
    }

    [Fact]
    public void AnImmuneKindCostsTheTenThousandAndIsUnchanged()
    {
        var (db, world) = Setup();
        CreatureState prince = world.SpawnCreature(db, ImmuneId, Rival, new TilePos(10, 10));
        int state = prince.Dk2StateId;

        Tick(db, world, new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Chicken, prince.Id));

        Assert.Equal((0, state), (prince.ChickenTicks, prince.Dk2StateId));
        Assert.Equal(100000 - 10000, world.Player(Keeper).Mana);
    }

    /// <summary>Doubt kept in the name: a dying creature refuses state 96 by the transition rule and
    /// still carries the timer, because the store comes first; no original run shows it (Q498).</summary>
    [Fact]
    public void ADyingCreatureRefusesTheStateButStillCarriesTheTimer_AsRead()
    {
        var (db, world) = Setup();
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 10));
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), enemy, CreatureDeathRules.DyingState));

        Tick(db, world, new CastSpellOnCreatureCommand(Keeper, KeeperSpellRules.Chicken, enemy.Id));

        Assert.Equal((240, CreatureDeathRules.DyingState), (enemy.ChickenTicks, enemy.Dk2StateId));
    }

    [Fact]
    public void TheSpentTimerSendsTheCreatureToIdleWhateverItIsDoing()
    {
        var (db, world) = Setup();
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 10));
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), enemy, ChickenRules.ChickenState));
        enemy.ChickenTicks = 2;
        var sim = new GameSimulation(db, world, 1, [new CreatureStatusSystem()]);

        sim.Tick();
        Assert.Equal((1, ChickenRules.ChickenState), (enemy.ChickenTicks, enemy.Dk2StateId));
        sim.Tick();
        Assert.Equal((0, CreatureStateMachine.Idle), (enemy.ChickenTicks, enemy.Dk2StateId));
    }

    [Fact]
    public void WithoutAHatcheryTheChickenWalksToANeighbouringTileAndResumesTheState()
    {
        var (db, world) = Setup();
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 10));
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), enemy, ChickenRules.ChickenState));
        enemy.ChickenTicks = 100;

        new GameSimulation(db, world, 7, [new ChickenSystem()]).Tick();

        Assert.Equal((6, ChickenRules.ChickenState), (enemy.Dk2StateId, enemy.UtilityNewState));
        TilePos to = enemy.Destination;
        Assert.Equal(1, Math.Abs(to.X - 10) + Math.Abs(to.Y - 10));
    }

    [Fact]
    public void WithAReachableHatcheryOfItsOwnKeeperTheChickenWalksIntoIt()
    {
        var (db, world) = Setup((_, w) =>
        {
            for (int y = 9; y <= 11; y++)
                for (int x = 14; x <= 16; x++)
                {
                    w.Tile(x, y).TerrainId = Dk2TerrainFixture.HatcheryTerrain;
                    w.Tile(x, y).OwnerPlayerId = Rival;
                }
        });
        CreatureState enemy = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 10));
        Assert.True(CreatureStateMachine.TrySetState(new StateHookContext(world, db), enemy, ChickenRules.ChickenState));
        enemy.ChickenTicks = 100;

        new GameSimulation(db, world, 7, [new ChickenSystem()]).Tick();

        Assert.Equal((7, ChickenRules.ChickenState), (enemy.Dk2StateId, enemy.UtilityNewState));
        Assert.InRange(enemy.Destination.X, 14, 16);
        Assert.InRange(enemy.Destination.Y, 9, 11);
    }

    // ------------------------------------------------------------------ Inferno

    [Fact]
    public void AnInfernoHurtsEveryOwnersCreatureWithinOneTileOnceATickForThirtyTicks()
    {
        var (db, world) = Setup();
        CreatureState own = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(10, 10));
        CreatureState near = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 10));
        near.PosX += 0x600;
        CreatureState far = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(12, 10));
        foreach (CreatureState c in new[] { own, near, far }) c.Health = 1000;
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);

        sim.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, KeeperSpellRules.Inferno, own.PosX, own.PosY)));
        sim.Run(40);

        // Data1 is 10 here: thirty runs, the caster's own creature included; two tiles out, nothing.
        Assert.Equal((1000 - 300, 1000 - 300, 1000), (own.Health, near.Health, far.Health));
        Assert.Empty(world.Shots);
        Assert.Equal(100000 - 50000, world.Player(Keeper).Mana);
    }

    [Fact]
    public void TheFirstTickHalvesTheRadiusSquaredSoACreatureFourFifthsOfATileOutTakesTwentyNine()
    {
        var (db, world) = Setup();
        CreatureState edge = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10, 10));
        edge.Health = 1000;
        int x = edge.PosX - 0xCCC, y = edge.PosY;
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);

        sim.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, KeeperSpellRules.Inferno, x, y)));
        sim.Run(40);

        Assert.Equal(1000 - 290, edge.Health);
    }

    [Fact]
    public void ALavaWalkerThatCannotFlyIsSparedAndOneThatFliesBurns_ConfirmedInPlay()
    {
        var (db, world) = Setup();
        CreatureState salamander = world.SpawnCreature(db, SalamanderId, Rival, new TilePos(10, 10));
        CreatureState firefly = world.SpawnCreature(db, FireflyId, Rival, new TilePos(10, 10));
        salamander.Health = firefly.Health = 1000;
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]);

        sim.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, KeeperSpellRules.Inferno, salamander.PosX, salamander.PosY)));
        sim.Run(40);

        Assert.Equal((1000, 700), (salamander.Health, firefly.Health));
    }

    [Fact]
    public void TheCooldownRefusesASecondInfernoForSixtySecondsAndRivalLandRefusesTheFirst()
    {
        var (db, world) = Setup((_, w) => w.Tile(13, 10).OwnerPlayerId = Rival);
        var boundary = new PlaceBoundary();
        var sim = new GameSimulation(db, world, 1, [boundary]);
        int at = CreatureState.CentreOf(10), rival = CreatureState.CentreOf(13), y = CreatureState.CentreOf(10);

        sim.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, KeeperSpellRules.Inferno, rival, y)));
        Assert.Equal(KeeperSpellRefusal.Location, boundary.Refusal);
        sim.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, KeeperSpellRules.Inferno, at, y)));
        Assert.Equal(KeeperSpellRefusal.None, boundary.Refusal);
        sim.Run(238);
        sim.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, KeeperSpellRules.Inferno, at, y)));
        Assert.Equal(KeeperSpellRefusal.Cooldown, boundary.Refusal);
        sim.Tick(InputFrame.Of(new CastSpellAtPlaceCommand(Keeper, KeeperSpellRules.Inferno, at, y)));
        Assert.Equal(KeeperSpellRefusal.None, boundary.Refusal);
    }

    [Fact]
    public void AShotTheWorldStopsWithoutDiesOnSolidStillCountsDown()
    {
        var (db, world) = Setup();
        var shot = ShotRules.Spawn(world, db.Shots[BoltId], Keeper, CreatureState.CentreOf(16), CreatureState.CentreOf(10), 0, 0, 0,
            FightTargetKind.None, -1, Keeper, -1, 0, 0);
        shot.VelX = 0x1000; // into the rock at x = 17
        var sim = new GameSimulation(db, world, 1, [new ShotSystem()]);

        // Stopped on the first tick, it lives on and counts down: two left, then one, then gone.
        sim.Run(1);
        Assert.Equal((0, 2), (Assert.Single(world.Shots).VelX, world.Shots[0].Health));
        sim.Run(1);
        Assert.Equal(1, Assert.Single(world.Shots).Health);
        sim.Run(1);
        Assert.Empty(world.Shots);
    }

    // ------------------------------------------------------------------ fixture

    private static void Tick(GameDatabase db, WorldState world, PlayerCommand command) =>
        new GameSimulation(db, world, 1, [new KeeperSpellSystem(), new ShotSystem()]).Tick(InputFrame.Of(command));

    private sealed class PlaceBoundary : ISimulationSystem
    {
        public string Name => "place_probe";
        public KeeperSpellRefusal Refusal;
        public void Execute(SimulationContext context)
        {
            foreach (var command in context.Input.All.OfType<CastSpellAtPlaceCommand>())
                KeeperSpellRules.CastAtPlace(context, command, out Refusal);
        }
    }

    /// <summary>KeeperSpells.kwd records 9 and 11 as shipped, except Inferno's data1 of 100 is 10 so
    /// thirty runs stay countable below a Goblin's health.</summary>
    private static KeeperSpellDef Inferno() => new()
    {
        Name = "Inferno", KeeperSpellId = KeeperSpellRules.Inferno, ManaCost = 50000, ManaUpkeepPerSecond = 0, CooldownRaw = 0x3C000,
        ShotTypeId = 15, ShotData1 = 10, ShotData2 = 1, BonusShotTypeId = 15, BonusShotData1 = 125, BonusShotData2 = 1,
        LocationSelector = 1, TargetSelector = 5, Flags = KeeperSpellFlags.Offensive,
    };

    private static KeeperSpellDef Chicken() => new()
    {
        Name = "Chicken", KeeperSpellId = KeeperSpellRules.Chicken, ManaCost = 10000, ManaUpkeepPerSecond = 0, CooldownRaw = 0x3C000,
        ShotTypeId = 10, ShotData1 = 60, BonusShotTypeId = 10, BonusShotData1 = 90,
        LocationSelector = 1, TargetSelector = 3, Flags = KeeperSpellFlags.Offensive,
    };

    /// <summary>Shots.kwd records 10 and 15 as shipped (shots.md §2 offsets).</summary>
    private static ShotDef Shot(byte id, byte process, uint flags, int health, uint processFlags = 0, byte collide = 0) =>
        new(new byte[239])
        {
            Name = $"shot {id}", ShotId = id, MeshResource = new("", 0, default, 0, 0, 0, 0),
            Light = new(0, 0, 0, 0, 0, 0, 0, 0, 0), SoundCategory = "", Speed = 2048, Radius = 819,
            Health = (ushort)health, Flags = flags, ShotProcessFlags = processFlags, CollideType = collide, ProcessType = process,
        };

    private static (GameDatabase, WorldState) Setup(Action<GameDatabase, WorldState>? before = null)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        CreatureDef goblin = basis.CreatureById(Dk2TerrainFixture.GoblinCreatureId);
        CreatureDef immune = goblin with { Name = "Prince", CreatureId = ImmuneId, Flags = goblin.Flags | CreatureFlags.IsImmuneToChicken };
        CreatureDef salamander = goblin with { Name = "Salamander", CreatureId = SalamanderId, Flags = goblin.Flags | CreatureFlags.CanWalkOnLava };
        CreatureDef firefly = goblin with
        {
            Name = "Firefly", CreatureId = FireflyId, Flags = goblin.Flags | CreatureFlags.CanWalkOnLava | CreatureFlags.CanFly,
        };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, [.. basis.Creatures.Values, immune, salamander, firefly],
            basis.Variables, [Inferno(), Chicken()], objects: basis.Objects.Values,
            shots: [Shot(10, ShotRules.ChickenProcess, 0x6, 16, ShotRules.HitThingEvent, ShotRules.CollideEnemies),
                    Shot(15, ShotRules.InfernoProcess, 0x104, 30, ShotRules.PreMoveEvent),
                    Shot(BoltId, 0, ShotRules.CountsDown, 3)]);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        world.Player(Keeper).Mana = 100000;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(5, KeeperSpellRules.Inferno, 3));
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(5, KeeperSpellRules.Chicken, 3));
        for (int y = 8; y <= 12; y++)
            for (int x = 3; x <= 16; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = Keeper;
            }
        before?.Invoke(db, world);
        return (db, world);
    }
}
