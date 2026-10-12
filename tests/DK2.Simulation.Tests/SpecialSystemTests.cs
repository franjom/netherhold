// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class SpecialSystemTests
{
    [Fact]
    public void Action88DoesNotRequireOwnedLandOrNearbyCreatures()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        const byte keeper = PlayerIds.FirstKeeper;
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, 2, keeper]);
        CreatureState creature = world.SpawnCreature(db, 7, keeper, new TilePos(1, 1));
        int far = world.TileCount - 1;
        world.TileAtIndex(far).OwnerPlayerId = PlayerIds.Good;
        ObjectState item = world.SpawnObject(db, 117, PlayerIds.Good, far);
        var sim = new GameSimulation(db, world, 1, [new SpecialSystem()]);
        sim.Tick(InputFrame.Of(new ActivateSpecialCommand(keeper, item.Id)));
        Assert.Equal(2, creature.Level);
        Assert.Null(world.ObjectById(item.Id));
    }

    [Fact]
    public void IncreaseLevelPromotesAndHealsThePlayerOnlyThenIsConsumed()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        const byte keeper = PlayerIds.FirstKeeper;
        const byte other = 1;
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [(byte)0, other, PlayerIds.Neutral, keeper]);
        CreatureState wounded = world.SpawnCreature(db, 7, keeper, new TilePos(2, 2));
        wounded.Health = 890;
        CreatureState full = world.SpawnCreature(db, 7, keeper, new TilePos(2, 2));
        CreatureState enemy = world.SpawnCreature(db, 7, other, new TilePos(2, 2));
        ObjectState special = world.SpawnObject(db, 117, PlayerIds.Neutral, 2 + 2 * world.Width);
        Assert.True(db.ObjectById(117)!.IsSpecial);

        var sim = new GameSimulation(db, world, 42, [new SpecialSystem()]);
        sim.Tick(new InputFrame([new ActivateSpecialCommand(keeper, special.Id)]));

        Assert.Equal((2, 1875), (wounded.Level, wounded.Health));
        Assert.Equal((2, 1875), (full.Level, full.Health));
        Assert.Equal((1, 1500), (enemy.Level, enemy.Health));
        Assert.Null(world.ObjectById(special.Id));

        sim.Tick(new InputFrame([new ActivateSpecialCommand(keeper, special.Id)]));
        Assert.Equal(2, wounded.Level);
    }

    /// <summary>Special 124 (<c>0x004A4980</c>): ten level-5 Imps two tiles round the Heart's
    /// centre, clockwise from north at 204/2048 of a turn apart. The ten tiles are the ones the
    /// owner's Level 4 capture shows round its Heart at turn 5783 (2026-10-12).</summary>
    [Fact]
    public void ReceiveImpsPutsTenLevelFiveImpsOnACircleRoundTheHeart()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        const byte keeper = PlayerIds.FirstKeeper;
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, PlayerIds.Neutral, keeper]);
        RoomDef heart = db.Room(RoomTypeId.DungeonHeart);
        for (int y = 3; y < 8; y++)
            for (int x = 3; x < 8; x++)
                RoomRules.Place(db, ref world.Tile(x, y), heart, keeper);
        ObjectState special = world.SpawnObject(db, Dk2TerrainFixture.ReceiveImpsSpecialObjectId, PlayerIds.Neutral, 1 + world.Width);
        Assert.True(db.ObjectById(Dk2TerrainFixture.ReceiveImpsSpecialObjectId)!.IsSpecial);
        Assert.Equal(10, db[VariableTypeId.SpecialReceiveImpsAmount]);

        var sim = new GameSimulation(db, world, 7, [new SpecialSystem()]);
        sim.Tick(InputFrame.Of(new ActivateSpecialCommand(keeper, special.Id)));

        CreatureState[] imps = [.. world.Creatures.Where(c => c.OwnerPlayerId == keeper)];
        CreatureDef imp = db.CreatureById(imps[0].CreatureTypeId);
        Assert.True(imp.Flags.HasFlag(CreatureFlags.IsWorker));
        Assert.Equal(
            new (int, int)[] { (5, 3), (6, 3), (7, 4), (7, 6), (6, 7), (5, 7), (4, 7), (3, 6), (3, 4), (4, 3) },
            imps.Select(c => (c.Tile.X, c.Tile.Y)));
        Assert.All(imps, c =>
        {
            Assert.Equal(5, c.Level);
            Assert.Equal(CreatureHealthRules.MaxHealth(db, imp, c), c.Health);
            Assert.InRange(c.Heading, 0, 0x7FF);
            Assert.True(c.JustDropped);
        });
        Assert.Equal((5 * 0x1000) + 0x800, imps[0].PosX);
        Assert.Equal((5 * 0x1000) + 0x800 - 0x2000, imps[0].PosY);
        Assert.Null(world.ObjectById(special.Id));
    }
}
