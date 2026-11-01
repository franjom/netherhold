// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class HeartDefenceSelectionTests
{
    private const byte FighterType = 34;
    private const byte Keeper = PlayerIds.FirstKeeper;

    [Fact]
    public void SelectAVictim_0045D69D_HeroTakesNearerImpAndReportingImpFlees_0045EC80()
    {
        var (db, world) = World(FightStyle.Flanker);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(8, 5));
        StatePlacement.Place(imp, 254);
        CreatureState hero = world.SpawnCreature(db, FighterType, PlayerIds.Good, new TilePos(9, 5));

        new GameSimulation(db, world, 1, [new CombatSystem()]).Tick();

        // Newest first: the hero registers on the Imp, whose scan and 199 handler
        // then run in the same update (0045E9F0, 00476FC0, 0045EC80).
        Assert.Equal(FightTargetKind.Creature, hero.FightTargetKind);
        Assert.Equal(imp.Id, hero.FightTargetId);
        Assert.Equal(203, imp.Dk2StateId);
    }

    [Theory]
    [InlineData(FightStyle.Blitzer)]
    [InlineData(FightStyle.Flanker)]
    [InlineData(FightStyle.Support)]
    public void SelectAVictim_0045D69D_ReplacesStrategyScoreWithDistanceBeforeHeartScan(FightStyle style)
    {
        var (db, world) = World(style);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(8, 5));
        CreatureState hero = world.SpawnCreature(db, FighterType, PlayerIds.Good, new TilePos(9, 5));

        Select(db, world, hero);

        Assert.Equal(FightTargetKind.Creature, hero.FightTargetKind);
        Assert.Equal(imp.Id, hero.FightTargetId);
        Assert.Contains(hero.Id, imp.EngagedMelee);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectAVictim_0045D69D_RemovesAlignmentDiscountBeforeHeartScan(bool registeredAttacker)
    {
        var (db, world) = World(FightStyle.Blocker);
        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(11, 5));
        CreatureState hero = world.SpawnCreature(db, FighterType, PlayerIds.Good, new TilePos(9, 5));
        if (registeredAttacker) Assert.True(CombatRules.Register(hero, imp.Id, ranged: false));

        Select(db, world, hero);

        // The nearer second pillar beats the Imp's raw four-tile-squared distance,
        // but loses to its 7/8-discounted score (0045D6AD-0045D701).
        Assert.Equal(FightTargetKind.Heart, hero.FightTargetKind);
    }

    private static void Select(GameDatabase db, WorldState world, CreatureState hero) =>
        new GameSimulation(db, world, 1, [new SelectOnce(hero)]).Tick();

    private sealed class SelectOnce(CreatureState hero) : ISimulationSystem
    {
        public string Name => "select at Heart";
        public void Execute(SimulationContext context)
        {
            context.Navigation.Refresh();
            var hearts = new HeartSites();
            hearts.Refresh(context.World, context.Database);
            var sight = new WorldSight(context.Navigation, context.World, context.Database);
            Assert.True(CombatTargetRules.SelectAVictim(context, sight, hearts, new RoomCache(), hero,
                context.Database.CreatureById(hero.CreatureTypeId), []));
        }
    }

    private static (GameDatabase, WorldState) World(FightStyle style)
    {
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Goblin() with
        {
            CreatureId = FighterType, FightStyle = style, Flags = CreatureFlags.IsGood,
            DistanceCanSee = new Fp32(8 * Fp32.One), Spells = [],
        });
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        for (int y = 3; y <= 7; y++)
            for (int x = 10; x <= 14; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.DungeonHeart), Keeper);
        return (db, world);
    }
}
