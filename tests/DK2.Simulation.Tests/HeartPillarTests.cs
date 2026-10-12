// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class HeartPillarTests
{
    [Theory]
    [InlineData(0, 12, 4, 0x800, 0x4CD)]
    [InlineData(1, 11, 6, 0x667, 0x19A)]
    [InlineData(2, 13, 6, 0x999, 0x19A)]
    public void TheThreePillarsKeepTheirBuilderPositionsForApproachAndSpellAim(
        int ordinal, int tileX, int tileY, int offsetX, int offsetY)
    {
        (GameDatabase db, WorldState world) = HeartWorld();
        var sites = new HeartSites();
        sites.Refresh(world, db);
        HeartPillarTarget[] targets = Assert.Single(sites.Sites).Pillars;
        Assert.Equal(3, targets.Length);
        HeartPillarTarget target = targets[ordinal];
        Assert.Equal(new TilePos(tileX, tileY), target.Tile);
        Assert.Equal((tileX << 12) + offsetX, target.X.Raw);
        Assert.Equal((tileY << 12) + offsetY, target.Y.Raw);
        CreatureState caster = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, PlayerIds.FirstKeeper, new TilePos(9, 5));
        caster.FightTargetKind = caster.SwingTargetKind = FightTargetKind.Heart;
        caster.FightTargetId = caster.SwingTargetId = HeartPillarId.For(world, target).Value;
        caster.FightTargetPlayerId = caster.SwingTargetPlayerId = PlayerIds.FirstKeeper + 1;
        caster.FightTargetTileIndex = (tileY * world.Width) + tileX;
        caster.AttackSpellSlot = 0;
        Assert.True(FightRules.TargetPosition(world, db, caster, out int x, out int y, out TilePos tile));
        Assert.Equal((target.X.Raw, target.Y.Raw, target.Tile), (x, y, tile));
        var sim = new GameSimulation(db, world, 1, [new FireAtPillar(caster)]);
        sim.Tick();
        ShotState shot = Assert.Single(world.Shots);
        Assert.Equal((target.X.Raw, target.Y.Raw), (shot.TargetX, shot.TargetY));
        Assert.Equal(HeartPillarId.For(world, target).Value, shot.TargetId);
        Assert.Equal((target.X.Raw << 4, target.Y.Raw << 4),
            (caster.Body.PathTargetPos.X, caster.Body.PathTargetPos.Y));
    }

    [Fact]
    public void AHeartShotHitsItsPillarPositionRatherThanTheTileCentre()
    {
        (GameDatabase db, WorldState world) = HeartWorld();
        HeartPillarTarget target = HeartPillarRules.At(new TilePos(11, 6), HeartPillar.Second);
        var shot = new ShotState
        {
            TargetKind = FightTargetKind.Heart, TargetPlayerId = PlayerIds.FirstKeeper + 1,
            TargetTileIndex = (6 * world.Width) + 11, TargetId = HeartPillarId.For(world, target).Value,
            PosX = target.X.Raw, PosY = target.Y.Raw,
        };
        ShotDef definition = db.Shots[Dk2TerrainFixture.FireballShotId] with { Radius = 0 };
        Assert.True(ShotRules.HitsHeart(world, db, shot, definition));
        shot.PosX = CreatureState.CentreOf(11);
        shot.PosY = CreatureState.CentreOf(6);
        Assert.False(ShotRules.HitsHeart(world, db, shot, definition));
    }

    private sealed class FireAtPillar(CreatureState caster) : ISimulationSystem
    {
        public string Name => "test pillar cast";
        public void Execute(SimulationContext context)
        {
            FightRules.FireSpell(context, caster, context.Database.CreatureById(caster.CreatureTypeId));
            Assert.True(FightRules.TargetPosition(context.World, context.Database, caster, out int x, out int y, out _));
            Assert.True(ApproachRules.Enter(context, caster, x, y));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void InvalidOrStalePillarsAreRejectedWithoutDamageOrExceptions(int fault)
    {
        (GameDatabase db, WorldState world) = HeartWorld();
        HeartPillarTarget target = HeartPillarRules.At(new TilePos(11, 6), HeartPillar.Second);
        int tile = 6 * world.Width + 11;
        int id = HeartPillarId.For(world, target).Value;
        byte owner = PlayerIds.FirstKeeper + 1;
        switch (fault)
        {
            case 0: id = -1; break;
            case 1: id = world.TileCount * 3; break;
            case 2: id = ((5 * world.Width + 10) * 3) + 1; break;
            case 3: owner = PlayerIds.FirstKeeper; break;
            case 4: world.Player(owner).HeartRuined = true; break;
            case 5: world.Player(owner).HeartHealth = 0; break;
            case 6: world.TileAtIndex(tile).TerrainId = Dk2TerrainFixture.DirtPath; break;
            case 7: world.TileAtIndex(tile).SlabOrder = 999; break;
        }
        CreatureState caster = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, PlayerIds.FirstKeeper, new TilePos(9, 5));
        caster.FightTargetKind = caster.SwingTargetKind = FightTargetKind.Heart;
        caster.FightTargetId = caster.SwingTargetId = id;
        caster.FightTargetPlayerId = caster.SwingTargetPlayerId = owner;
        caster.FightTargetTileIndex = tile;
        caster.AttackSpellSlot = 0;
        Assert.False(FightRules.TargetIsValid(world, db, caster));
        Assert.False(FightRules.TargetPosition(world, db, caster, out _, out _, out _));
        var shot = new ShotState { TargetKind = FightTargetKind.Heart, TargetId = id, TargetPlayerId = owner, TargetTileIndex = tile };
        Assert.False(ShotRules.HitsHeart(world, db, shot, db.Shots[Dk2TerrainFixture.FireballShotId]));
        int before = world.Player(owner).HeartHealth;
        new GameSimulation(db, world, 1, [new InvalidAttack(caster)]).Tick();
        Assert.Equal(before, world.Player(owner).HeartHealth);
        Assert.Empty(world.Shots);
    }

    [Fact]
    public void APendingCastRetainsItsPillarAfterRetaliationAndSnapshot()
    {
        (GameDatabase db, WorldState world) = HeartWorld();
        HeartPillarTarget pending = HeartPillarRules.At(new TilePos(11, 6), HeartPillar.Second);
        HeartPillarTarget next = HeartPillarRules.At(new TilePos(13, 6), HeartPillar.Third);
        CreatureState caster = world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, PlayerIds.FirstKeeper, new TilePos(9, 5));
        caster.FightTargetKind = caster.SwingTargetKind = FightTargetKind.Heart;
        caster.SwingTargetId = HeartPillarId.For(world, pending).Value;
        caster.FightTargetId = HeartPillarId.For(world, next).Value;
        caster.SwingTargetPlayerId = caster.FightTargetPlayerId = PlayerIds.FirstKeeper + 1;
        caster.FightTargetTileIndex = next.Tile.Y * world.Width + next.Tile.X;
        caster.AttackSpellSlot = 0;
        var sim = new GameSimulation(db, world, 1, [new FirePending(caster)]);
        sim.Tick();
        ShotState shot = Assert.Single(world.Shots);
        Assert.Equal((pending.X.Raw, pending.Y.Raw), (shot.TargetX, shot.TargetY));
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, []);
        CreatureState after = restored.World.CreatureById(caster.Id)!;
        Assert.Equal(caster.FightTargetId, after.FightTargetId);
        Assert.Equal(caster.SwingTargetId, after.SwingTargetId);
        Assert.Equal(shot.TargetId, Assert.Single(restored.World.Shots).TargetId);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        caster.FightTargetKind = FightTargetKind.Creature;
        caster.FightTargetTileIndex = -1;
        sim.Tick();
        Assert.All(world.Shots, fired => Assert.Equal((pending.X.Raw, pending.Y.Raw), (fired.TargetX, fired.TargetY)));
        Assert.Equal(2, world.Shots.Count);
    }

    private sealed class FirePending(CreatureState caster) : ISimulationSystem
    {
        public string Name => "test pending pillar cast";
        public void Execute(SimulationContext context) => FightRules.FireSpell(context, caster, context.Database.CreatureById(caster.CreatureTypeId));
    }

    private sealed class InvalidAttack(CreatureState caster) : ISimulationSystem
    {
        public string Name => "test invalid pillar attack";
        public void Execute(SimulationContext context)
        {
            CreatureDef definition = context.Database.CreatureById(caster.CreatureTypeId);
            FightRules.LandBlow(context, caster, definition);
            FightRules.FireSpell(context, caster, definition);
        }
    }

    private static (GameDatabase, WorldState) HeartWorld()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, PlayerIds.FirstKeeper, PlayerIds.FirstKeeper + 1]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        for (int y = 3; y <= 7; y++)
            for (int x = 10; x <= 14; x++)
                RoomRules.Place(db, ref world.Tile(new TilePos(x, y)), db.Room(RoomTypeId.DungeonHeart), PlayerIds.FirstKeeper + 1);
        return (db, world);
    }
}
