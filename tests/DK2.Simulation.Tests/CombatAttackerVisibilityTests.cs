// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

public class CombatAttackerVisibilityTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte FighterType = 34;
    private const byte AttackerType = 35;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HearingOnlyRegisteredAttackersMustBeWithinRouteDistance(bool detour)
    {
        var (db, world) = OpenWorld();
        if (detour)
            for (int y = 1; y <= 15; y++)
                world.Tile(new TilePos(10, y)).TerrainId = Dk2TerrainFixture.Lava;
        CreatureState fighter = world.SpawnCreature(db, FighterType, Keeper, new TilePos(8, 8));
        CreatureState attacker = world.SpawnCreature(db, AttackerType, Keeper, new TilePos(11, 8));
        Assert.True(CombatRules.Register(fighter, attacker.Id, ranged: false));

        Run(db, world, context =>
        {
            CreatureDef definition = db.CreatureById(FighterType);
            var sight = new WorldSight(context.Navigation, world, db);
            Assert.True(LineOfSight.Clear(sight, fighter.PosX, fighter.PosY, attacker.PosX, attacker.PosY));
            Assert.True(CombatTargetRules.CanReach(context.Navigation, fighter, definition,
                attacker.PosX, attacker.PosY, int.MaxValue, []));
            Assert.Equal(!detour, CombatTargetRules.CanReach(context.Navigation, fighter, definition,
                attacker.PosX, attacker.PosY, CombatRules.RangeSquared(definition.DistanceCanHear), []));

            Assert.Equal(!detour, CombatTargetRules.SelectAVictim(context, sight, new HeartSites(),
                new RoomCache(), fighter, definition, []));
            Assert.Equal(detour ? FightTargetKind.None : FightTargetKind.Creature, fighter.FightTargetKind);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnImpRequiresSightOfARegisteredAttackerEvenWhenItCanRouteAroundTheWall(bool wall)
    {
        var (db, world) = OpenWorld();
        if (wall) world.Tile(new TilePos(10, 8)).TerrainId = Dk2TerrainFixture.Rock;
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(8, 8));
        CreatureState attacker = world.SpawnCreature(db, AttackerType, PlayerIds.Good, new TilePos(13, 8));
        Assert.True(CombatRules.Register(imp, attacker.Id, ranged: false));

        Run(db, world, context =>
        {
            CreatureDef definition = db.CreatureById(imp.CreatureTypeId);
            var sight = new WorldSight(context.Navigation, world, db);
            Assert.Equal(!wall, LineOfSight.Clear(sight, imp.PosX, imp.PosY, attacker.PosX, attacker.PosY));
            Assert.True(CombatTargetRules.CanReach(context.Navigation, imp, definition,
                attacker.PosX, attacker.PosY, int.MaxValue, []));

            Assert.Equal(!wall, CombatTargetRules.SelectAVictim(context, sight, new HeartSites(),
                new RoomCache(), imp, definition, []));
            Assert.Equal(wall ? FightTargetKind.None : FightTargetKind.Creature, imp.FightTargetKind);
        });
    }

    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database(
            Dk2TerrainFixture.Knight() with
            {
                CreatureId = FighterType,
                DistanceCanSee = new Fp32(4096),
                DistanceCanHear = new Fp32(16384),
            },
            Dk2TerrainFixture.Knight() with { CreatureId = AttackerType });
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static void Run(GameDatabase db, WorldState world, Action<SimulationContext> assertion) =>
        new GameSimulation(db, world, 1, [new CheckAcquisition(assertion)]).Tick();

    private sealed class CheckAcquisition(Action<SimulationContext> assertion) : ISimulationSystem
    {
        public string Name => "registered attacker visibility";
        public void Execute(SimulationContext context)
        {
            context.Navigation.Refresh();
            assertion(context);
        }
    }
}
