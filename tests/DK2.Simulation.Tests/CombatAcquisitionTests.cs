// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>How a Blocker finds its enemy — docs/research/combat-acquisition.md, ADR-0163. Only
/// the acquisition is built: a creature that engages stands and holds its target, and nothing
/// swings yet (Q8). The fixture's Bile Demon and Knight run the Blocker arm; its Goblin is a
/// Blitzer, whose arm is not built, so it never acquires and serves as the target.</summary>
public class CombatAcquisitionTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Fact]
    public void ABlockerEngagesTheNearestHostileItCanSee()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = world.SpawnCreature(db, Dk2TerrainFixture.BileDemonCreatureId, Keeper, new TilePos(5, 5));
        CreatureState near = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, 5));
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(5, 8));

        Combat(db, world).Run(1);

        Assert.Equal(CreatureActivity.Fighting, demon.Activity);
        Assert.Equal(near.Id, demon.FightTargetId);
        Assert.Equal(CombatRules.AttackMode.MeleeAtCreature, demon.AttackType);
        Assert.Equal(
            CombatRules.MeleeReachSquared(db.CreatureById(demon.CreatureTypeId), db.CreatureById(near.CreatureTypeId)),
            demon.AttackRangeSquared);
        Assert.Equal(demon.Id, near.EngagedMelee[0]);
        // The Goblin's own arm, Blitzer, sees no Support to dive at and takes the nearest too.
        Assert.Equal(demon.Id, near.FightTargetId);
    }

    [Fact]
    public void AlliesAndOwnCreaturesAreNotEnemies()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = world.SpawnCreature(db, Dk2TerrainFixture.BileDemonCreatureId, Keeper, new TilePos(5, 5));
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(6, 5));
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, 5));
        world.SetAllied(Keeper, Rival, true);

        Combat(db, world).Run(1);

        Assert.Equal(CreatureActivity.Idle, demon.Activity);
        Assert.Equal(CombatRules.NoTarget, demon.FightTargetId);
    }

    [Fact]
    public void ANeutralCreatureNeverLooksForAFight_ButAHeroDoes()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState neutral = world.SpawnCreature(db, Dk2TerrainFixture.BileDemonCreatureId, PlayerIds.Neutral, new TilePos(5, 5));
        CreatureState knight = world.SpawnCreature(db, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, new TilePos(10, 5));
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 5));

        Combat(db, world).Run(1);

        Assert.Equal(CreatureActivity.Idle, neutral.Activity);
        Assert.Equal(CreatureActivity.Fighting, knight.Activity);
        Assert.Equal(goblin.Id, knight.FightTargetId);
    }

    [Fact]
    public void AWallBlocksSight_ButAWalkWithinHearingStillEngages()
    {
        // An L of three floor tiles: the diagonal line of sight brushes the rock at the corner,
        // and the walk round it is well under four tiles.
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        foreach ((int x, int y) in new[] { (5, 5), (5, 4), (6, 4) })
            world.Tile(new TilePos(x, y)).TerrainId = Dk2TerrainFixture.DirtPath;
        CreatureState demon = world.SpawnCreature(db, Dk2TerrainFixture.BileDemonCreatureId, Keeper, new TilePos(5, 5));
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(6, 4));

        Combat(db, world).Run(1);

        Assert.Equal(goblin.Id, demon.FightTargetId);
    }

    [Fact]
    public void AWallBlocksSight_AndALongWayRoundIsOutOfHearing()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        for (int y = 1; y <= 9; y++) world.Tile(new TilePos(6, y)).TerrainId = Dk2TerrainFixture.Rock;
        CreatureState demon = world.SpawnCreature(db, Dk2TerrainFixture.BileDemonCreatureId, Keeper, new TilePos(5, 5));
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, 5));

        Combat(db, world).Run(1);

        Assert.Equal(CombatRules.NoTarget, demon.FightTargetId);
        Assert.Equal(CreatureActivity.Idle, demon.Activity);
    }

    [Fact]
    public void TheOppositeAlignmentReadsAnEighthNearer()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState knight = world.SpawnCreature(db, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, new TilePos(5, 5));
        // An evil Goblin three tiles off scores 7/8 of 9 tiles² = 7.875; a good Knight 2.9 tiles off
        // scores 8.41 undiscounted, so the farther Goblin wins.
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8, 5));
        CreatureState rival = world.SpawnCreature(db, Dk2TerrainFixture.KnightCreatureId, Rival, new TilePos(5, 8));
        rival.PosY = knight.PosY + (int)(2.9 * Fp32.One);

        Combat(db, world).Run(1);

        Assert.Equal(goblin.Id, knight.FightTargetId);
    }

    [Fact]
    public void AStandingCreatureLooksEveryTenthTick_AndAtOnceAfterChangingTile()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = world.SpawnCreature(db, Dk2TerrainFixture.BileDemonCreatureId, Keeper, new TilePos(5, 5));
        GameSimulation sim = Combat(db, world);
        sim.Run(1);
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, 5));

        sim.Run(9);
        Assert.Equal(CombatRules.NoTarget, demon.FightTargetId);
        sim.Run(1);
        Assert.NotEqual(CombatRules.NoTarget, demon.FightTargetId);

        CreatureState mover = world.SpawnCreature(db, Dk2TerrainFixture.BileDemonCreatureId, Keeper, new TilePos(10, 8));
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(12, 8));
        sim.Run(1);
        Assert.NotEqual(CombatRules.NoTarget, mover.FightTargetId);
    }

    [Fact]
    public void ANinthMeleeAttackerCannotEngage()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, 5));
        var demons = new List<CreatureState>();
        for (int i = 0; i < 9; i++)
            demons.Add(world.SpawnCreature(db, Dk2TerrainFixture.BileDemonCreatureId, Keeper, new TilePos(4 + (i % 3), 3 + (i / 3))));

        Combat(db, world).Run(1);

        Assert.All(demons.Skip(1), d => Assert.Equal(goblin.Id, d.FightTargetId));
        Assert.Equal(CombatRules.NoTarget, demons[0].FightTargetId);
        Assert.Equal(CreatureActivity.Idle, demons[0].Activity);
        Assert.Equal(demons.Skip(1).Reverse().Select(d => d.Id), goblin.EngagedMelee);
        Assert.True(CombatRules.IsAttackable(goblin), "eight melee slots taken still leaves the ranged eight");
    }

    [Fact]
    public void WhenAnApproachedTargetIsGone_ResumeTheFightThenIdle()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = world.SpawnCreature(db, Dk2TerrainFixture.BileDemonCreatureId, Keeper, new TilePos(5, 5));
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, 5));
        GameSimulation sim = Combat(db, world);
        sim.Run(1);
        Assert.Equal(goblin.Id, demon.FightTargetId);

        Assert.Equal(8, demon.Dk2StateId);
        world.RemoveCreature(goblin.Id);
        sim.Run(1);
        // 0x00470E66 resumes 199; its no-target branch runs on the next turn.
        Assert.Equal(199, demon.Dk2StateId);
        Assert.Equal(CombatRules.NoTarget, demon.FightTargetId);
        sim.Run(1);

        Assert.Equal(CreatureActivity.Idle, demon.Activity);
        Assert.Equal(CombatRules.NoTarget, demon.FightTargetId);
        Assert.Equal(0, demon.AttackRangeSquared);
    }

    [Fact]
    public void AnEngagementSurvivesASnapshot()
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        CreatureState demon = world.SpawnCreature(db, Dk2TerrainFixture.BileDemonCreatureId, Keeper, new TilePos(5, 5));
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(7, 5));
        GameSimulation original = Combat(db, world);
        original.Run(1);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), db);

        CreatureState after = restored.World.CreatureById(demon.Id)!;
        Assert.Equal(demon.FightTargetId, after.FightTargetId);
        Assert.Equal(demon.AttackRangeSquared, after.AttackRangeSquared);
        Assert.Equal(demon.LastFightScanTileIndex, after.LastFightScanTileIndex);
        Assert.Equal(original.StateHash(), restored.StateHash());
    }

    [Fact]
    public void TheLineWalkAsksEveryTileEnteredAndNeverTheFirst()
    {
        var grid = new BitGrid(8, 8);
        Assert.True(LineOfSight.Clear(grid, Centre(1), Centre(1), Centre(5), Centre(1)));

        grid.Block(3, 1);
        Assert.False(LineOfSight.Clear(grid, Centre(1), Centre(1), Centre(5), Centre(1)));
        Assert.False(LineOfSight.Clear(grid, Centre(5), Centre(1), Centre(1), Centre(1)));

        // The tile the walk starts in is never asked.
        grid.Clear();
        grid.Block(1, 1);
        Assert.True(LineOfSight.Clear(grid, Centre(1), Centre(1), Centre(5), Centre(1)));
    }

    [Fact]
    public void AtACornerEitherNeighbourBlocks_AndOnlyThenIsTheDiagonalAsked()
    {
        var grid = new BitGrid(8, 8);
        // Centre to centre along the diagonal crosses every tile corner exactly.
        Assert.True(LineOfSight.Clear(grid, Centre(1), Centre(1), Centre(3), Centre(3)));

        grid.Block(2, 1);
        Assert.False(LineOfSight.Clear(grid, Centre(1), Centre(1), Centre(3), Centre(3)));

        grid.Clear();
        grid.Block(1, 2);
        Assert.False(LineOfSight.Clear(grid, Centre(1), Centre(1), Centre(3), Centre(3)));

        grid.Clear();
        grid.Block(2, 2);
        Assert.False(LineOfSight.Clear(grid, Centre(1), Centre(1), Centre(3), Centre(3)));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(6, false)]
    public void AClosedDoorBlocksSightAndABarricadeDoesNot(byte doorId, bool blocks)
    {
        (GameDatabase db, WorldState world) = OpenWorld();
        ThingProjection.Place(world, db,
            [new DoorThing(new byte[20]) { X = 6, Y = 5, PlayerId = Rival, Flags = 0, DoorId = doorId, Unknown_0011 = new byte[3] }]);
        var navigation = new Navigation.WorldSteering(db, world);
        navigation.Refresh();
        var sight = new WorldSight(navigation, world, db);

        Assert.Equal(blocks, sight.Blocks(6, 5));
        Assert.Equal(!blocks, LineOfSight.Clear(sight, Centre(5), Centre(5), Centre(7), Centre(5)));
        Assert.True(sight.Blocks(0, 0), "the impenetrable border is solid");
    }

    // ------------------------------------------------------------------ helpers

    private static int Centre(int tile) => CreatureState.CentreOf(tile);

    /// <summary>The fixture map with its rock dug out into one open floor, five players.</summary>
    private static (GameDatabase, WorldState) OpenWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
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

    private static GameSimulation Combat(GameDatabase db, WorldState world) =>
        new(db, world, 1, [new CombatSystem()]);

    private sealed class BitGrid : ISightBlocker
    {
        private readonly bool[,] _blocked;
        public BitGrid(int width, int height) => _blocked = new bool[width, height];
        public void Block(int x, int y) => _blocked[x, y] = true;
        public void Clear() => Array.Clear(_blocked);
        public bool Blocks(int x, int y) => _blocked[x, y];
    }
}
