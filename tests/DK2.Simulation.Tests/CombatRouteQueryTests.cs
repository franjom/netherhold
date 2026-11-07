// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation.Tests;

public sealed class CombatRouteQueryTests
{
    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void HearingMeasuresTheFunnelCornersAroundTheWall(int hearingTiles, bool hears)
    {
        const byte fighterType = 34, targetType = 35;
        GameDatabase db = Dk2TerrainFixture.Database(
            Dk2TerrainFixture.Knight() with
            {
                CreatureId = fighterType, FightStyle = FightStyle.Blocker,
                DistanceCanSee = new Fp32(Fp32.One), DistanceCanHear = new Fp32(hearingTiles * Fp32.One),
            },
            Dk2TerrainFixture.Knight() with { CreatureId = targetType });
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(10, 7),
            [PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        string[] rows = ["##########", "#........#", "#........#", "#####....#", "#........#", "#........#", "##########"];
        for (int y = 0; y < rows.Length; y++)
        for (int x = 0; x < rows[y].Length; x++)
            world.Tile(x, y).TerrainId = rows[y][x] == '#' ? Dk2TerrainFixture.Rock : Dk2TerrainFixture.DirtPath;
        CreatureState fighter = world.SpawnCreature(db, fighterType, PlayerIds.Good, new TilePos(1, 5));
        CreatureState target = world.SpawnCreature(db, targetType, PlayerIds.FirstKeeper, new TilePos(1, 1));

        new GameSimulation(db, world, 1, [new Check(context =>
        {
            context.Navigation.Refresh();
            CreatureDef definition = db.CreatureById(fighterType);
            var sight = new WorldSight(context.Navigation, world, db);
            Assert.False(LineOfSight.Clear(sight, fighter.PosX, fighter.PosY, target.PosX, target.PosY));
            Assert.Equal(hears, CombatTargetRules.CanReach(context.Navigation, fighter, definition,
                target.PosX, target.PosY, CombatRules.RangeSquared(definition.DistanceCanHear), []));
            Assert.Equal(hears, CombatTargetRules.SelectAVictim(context, sight, new HeartSites(), new RoomCache(),
                fighter, definition, []));
            Assert.Equal(hears ? target.Id : CombatRules.NoTarget, fighter.FightTargetId);
        })]).Tick();
    }

    private sealed class Check(Action<SimulationContext> assertion) : ISimulationSystem
    {
        public string Name => "combat route query";
        public void Execute(SimulationContext context) => assertion(context);
    }
}
