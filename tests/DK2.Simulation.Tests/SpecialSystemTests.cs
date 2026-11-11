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

    [Fact]
    public void LocateHiddenLandIsCarriedForTheHostAndConsumed()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        const byte keeper = PlayerIds.FirstKeeper;
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, PlayerIds.Neutral, keeper]);
        ObjectState special = world.SpawnObject(db, Dk2TerrainFixture.LocateHiddenLandSpecialObjectId, PlayerIds.Neutral, 5);
        var sim = new GameSimulation(db, world, 1, [new SpecialSystem()]);
        sim.Tick(InputFrame.Of(new ActivateSpecialCommand(keeper, special.Id)));
        Assert.Equal(1, world.Carry.HiddenLandFound);
        Assert.Equal(1, world.Player(keeper).Statistics.SpecialsFound);
        Assert.Null(world.ObjectById(special.Id));
    }

    /// <summary>Arm 0x8C of 0x004A3FC0: only the activator's own creature, only in the campaign; a
    /// refused target still uses the box up.</summary>
    [Fact]
    public void TransferCreatureStoresAnOwnCreatureWhichLeavesNoBodyAndARefusalStillUsesTheBox()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        const byte keeper = PlayerIds.FirstKeeper, rival = PlayerIds.FirstKeeper + 1;
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, PlayerIds.Neutral, keeper, rival]);
        CreatureState theirs = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, rival, new TilePos(2, 2));
        CreatureState mine = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, keeper, new TilePos(3, 2));
        mine.Level = 6;
        ObjectState first = world.SpawnObject(db, Dk2TerrainFixture.TransferCreatureSpecialObjectId, PlayerIds.Neutral, 5);
        ObjectState second = world.SpawnObject(db, Dk2TerrainFixture.TransferCreatureSpecialObjectId, PlayerIds.Neutral, 6);
        var sim = new GameSimulation(db, world, 1, [new SpecialSystem()]);

        sim.Tick(InputFrame.Of(new ActivateSpecialCommand(keeper, first.Id, theirs.Id)));
        Assert.False(world.Carry.TransferStored);
        Assert.Null(world.ObjectById(first.Id));
        Assert.True(theirs.Health > 0);

        sim.Tick(InputFrame.Of(new ActivateSpecialCommand(keeper, second.Id, mine.Id)));
        Assert.True(world.Carry.TransferStored);
        Assert.Equal((Dk2TerrainFixture.GoblinCreatureId, (byte)6),
            (world.Carry.TransferCreatureTypeId, world.Carry.TransferCreatureLevel));
        Assert.Equal(0, mine.Health);
        Assert.True(mine.CorpseSuppressed);
    }

    [Fact]
    public void TransferCreatureOutsideTheCampaignStoresNothing()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        const byte keeper = PlayerIds.FirstKeeper;
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, PlayerIds.Neutral, keeper], gameMode: GameMode.Skirmish);
        CreatureState mine = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, keeper, new TilePos(3, 2));
        ObjectState special = world.SpawnObject(db, Dk2TerrainFixture.TransferCreatureSpecialObjectId, PlayerIds.Neutral, 5);
        new GameSimulation(db, world, 1, [new SpecialSystem()]).Tick(InputFrame.Of(new ActivateSpecialCommand(keeper, special.Id, mine.Id)));
        Assert.False(world.Carry.TransferStored);
        Assert.True(mine.Health > 0);
    }

    /// <summary>0x0050C67D: two tiles toward lower Y from the heart's centre tile, level − 1 level-ups.</summary>
    [Fact]
    public void ATransferredCreatureArrivesTwoTilesNorthOfTheHeartAtItsLevel()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        const byte keeper = PlayerIds.FirstKeeper;
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, PlayerIds.Neutral, keeper]);
        RoomDef heart = db.Room(RoomTypeId.DungeonHeart);
        for (int y = 3; y < 8; y++)
            for (int x = 3; x < 8; x++)
                RoomRules.Place(db, ref world.Tile(x, y), heart, keeper);
        var sim = new GameSimulation(db, world, 7, [new SpecialSystem()]);
        CreatureState arrived = TransferCreatureRules.Arrive(sim, keeper, Dk2TerrainFixture.GoblinCreatureId, 4)!;
        Assert.Equal(new TilePos(5, 3), arrived.Tile);
        Assert.Equal((5 * 0x1000 + 0x800, 3 * 0x1000 + 0x800), (arrived.PosX, arrived.PosY));
        Assert.Equal(4, arrived.Level);
        Assert.Contains(arrived.Heading, new[] { 0, 0x200, 0x400, 0x600 });
        Assert.Null(TransferCreatureRules.Arrive(sim, keeper, 0, 4));
    }

    private static (GameDatabase Db, WorldState World, GameSimulation Sim) World(params byte[] more)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, 1, PlayerIds.Neutral, PlayerIds.FirstKeeper, .. more]);
        return (db, world, new GameSimulation(db, world, 1, [new SpecialSystem()]));
    }

    private static void Use(GameSimulation sim, WorldState world, GameDatabase db, byte type)
    {
        ObjectState box = world.SpawnObject(db, type, PlayerIds.Neutral, 5);
        sim.Tick(InputFrame.Of(new ActivateSpecialCommand(PlayerIds.FirstKeeper, box.Id)));
        Assert.Null(world.ObjectById(box.Id));
    }

    /// <summary>Special 122 (inline in <c>0x004A3FC0</c>): <c>SpecialIncreaseManaAmount</c> through
    /// <c>Player_AddMana</c>, clamped at the ceiling.</summary>
    [Fact]
    public void ManaBoost_004A3FC0_AddsFiftyThousandUpToTheCeiling()
    {
        var (db, world, sim) = World();
        world.Player(PlayerIds.FirstKeeper).Mana = 1000;
        Use(sim, world, db, Dk2TerrainFixture.ManaBoostSpecialObjectId);
        Assert.Equal(Math.Min(51000, ManaRules.Threshold(db)), world.Player(PlayerIds.FirstKeeper).Mana);

        Use(sim, world, db, Dk2TerrainFixture.ManaBoostSpecialObjectId);
        Use(sim, world, db, Dk2TerrainFixture.ManaBoostSpecialObjectId);
        Use(sim, world, db, Dk2TerrainFixture.ManaBoostSpecialObjectId);
        Use(sim, world, db, Dk2TerrainFixture.ManaBoostSpecialObjectId);
        Assert.Equal(ManaRules.Threshold(db), world.Player(PlayerIds.FirstKeeper).Mana);
    }

    /// <summary>Special 125 (<c>0x004A4BD0</c>): the keeper's creatures forget every grievance; another's keep theirs.</summary>
    [Fact]
    public void MakeHappy_004A4BD0_ZeroesTheKeepersCreaturesAngerOnly()
    {
        var (db, world, sim) = World();
        CreatureState mine = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new TilePos(2, 2));
        CreatureState theirs = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 1, new TilePos(2, 2));
        mine.Anger[AngerBucket.Treatment] = 30000;
        mine.Anger[(AngerBucket)0] = 500;
        theirs.Anger[AngerBucket.Treatment] = 30000;

        Use(sim, world, db, Dk2TerrainFixture.MakeHappySpecialObjectId);

        for (int b = 0; b < CreatureAnger.Count; b++) Assert.Equal(0, mine.Anger[(AngerBucket)b]);
        Assert.Equal(30000, theirs.Anger[AngerBucket.Treatment]);
    }

    /// <summary>Special 126 (<c>0x004A4C90</c> → <c>0x0048B870</c> → <c>0x00486ED0</c>): every other
    /// player's creature, neutral included, saturates all six counters; a never-angered one and the
    /// keeper's own are untouched.</summary>
    [Fact]
    public void MakeUnhappy_00486ED0_SaturatesEveryOtherPlayersCreatureButTheNeverAngered()
    {
        var (db, world, sim) = World();
        CreatureState mine = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new TilePos(2, 2));
        CreatureState theirs = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 1, new TilePos(2, 2));
        CreatureState neutral = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.Neutral, new TilePos(2, 2));
        CreatureState calm = world.SpawnCreature(db, Dk2TerrainFixture.SkeletonCreatureId, 1, new TilePos(2, 2));
        Assert.False(CreatureAngerRules.CanBeAngered(db.CreatureById(calm.CreatureTypeId)));

        Use(sim, world, db, Dk2TerrainFixture.MakeUnhappySpecialObjectId);

        for (int b = 0; b < CreatureAnger.Count; b++)
        {
            Assert.Equal(CreatureAngerRules.Saturation, theirs.Anger[(AngerBucket)b]);
            Assert.Equal(CreatureAngerRules.Saturation, neutral.Anger[(AngerBucket)b]);
            Assert.Equal(0, calm.Anger[(AngerBucket)b]);
            Assert.Equal(0, mine.Anger[(AngerBucket)b]);
        }
        Assert.Equal(CreatureMood.Angry, CreatureAngerRules.MoodOf(db.CreatureById(theirs.CreatureTypeId), theirs));
    }

    /// <summary>Special 128 (<c>0x004A4E50</c>): the keeper's creatures return to their level-scaled maximum.</summary>
    [Fact]
    public void HealAll_004A4E50_HealsTheKeepersCreaturesToTheirLevelMaximumOnly()
    {
        var (db, world, sim) = World();
        CreatureState mine = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new TilePos(2, 2));
        mine.Level = 3;
        mine.Health = 100;
        CreatureState theirs = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 1, new TilePos(2, 2));
        theirs.Health = 100;

        Use(sim, world, db, Dk2TerrainFixture.HealAllSpecialObjectId);

        Assert.Equal(CreatureHealthRules.MaxHealth(db, db.CreatureById(mine.CreatureTypeId), mine), mine.Health);
        Assert.True(mine.Health > 1500);
        Assert.Equal(100, theirs.Health);
    }

    /// <summary>Special 121 (<c>0x004A4710</c>): 10,000 gold, the keeper's stores filled to capacity and
    /// the rest put down at the box.</summary>
    [Fact]
    public void IncreaseGold_004A4710_FillsTheStoresAndPutsTheRestDownAtTheBox()
    {
        var (db, world, sim) = World();
        RoomDef heart = db.Room(RoomTypeId.DungeonHeart);
        for (int y = 3; y < 8; y++)
            for (int x = 3; x < 8; x++)
                RoomRules.Place(db, ref world.Tile(x, y), heart, PlayerIds.FirstKeeper);
        int before = GoldStore.Deposit(world, db, PlayerIds.FirstKeeper, 12000);
        Assert.Equal(12000, before);
        int box = 1 + world.Width;
        ObjectState special = world.SpawnObject(db, Dk2TerrainFixture.IncreaseGoldSpecialObjectId, PlayerIds.Neutral, box);

        sim.Tick(InputFrame.Of(new ActivateSpecialCommand(PlayerIds.FirstKeeper, special.Id)));

        int stored = 0;
        for (int i = 0; i < world.TileCount; i++) stored += world.TileAtIndex(i).GoldHeld;
        int floor = world.Objects.Where(o => o.TileIndex == box && o.ObjectTypeId == FloorGold.FloorGoldObjectId).Sum(o => o.GoldHeld);
        Assert.Equal(16000, stored);
        Assert.Equal(6000, floor);
        Assert.Null(world.ObjectById(special.Id));
    }

    /// <summary>Special 127 (<c>0x004A4D80</c>): another's creature dies; the activator's own is refused
    /// and the box is used up all the same.</summary>
    [Fact]
    public void KillCreatures_004A4D80_KillsAnotherKeepersTargetAndARefusalStillUsesTheBox()
    {
        var (db, world, sim) = World();
        CreatureState mine = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new TilePos(2, 2));
        CreatureState theirs = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 1, new TilePos(3, 2));
        ObjectState first = world.SpawnObject(db, Dk2TerrainFixture.KillCreaturesSpecialObjectId, PlayerIds.Neutral, 5);
        ObjectState second = world.SpawnObject(db, Dk2TerrainFixture.KillCreaturesSpecialObjectId, PlayerIds.Neutral, 6);

        sim.Tick(InputFrame.Of(new ActivateSpecialCommand(PlayerIds.FirstKeeper, first.Id, mine.Id)));
        Assert.Equal(1500, mine.Health);
        Assert.Null(world.ObjectById(first.Id));

        sim.Tick(InputFrame.Of(new ActivateSpecialCommand(PlayerIds.FirstKeeper, second.Id, theirs.Id)));
        Assert.True(theirs.Health <= 0);
        Assert.Null(world.ObjectById(second.Id));
    }

    /// <summary>Special 118 (inline in <c>0x004A3FC0</c>): <c>0x00451EF0</c> explores every tile, for the
    /// local player only.</summary>
    [Fact]
    public void RevealMap_00451EF0_ExploresTheWholeMapForTheLocalPlayerOnly()
    {
        var (db, world, sim) = World();
        world.LocalPlayerId = new PlayerId(PlayerIds.FirstKeeper);
        Use(sim, world, db, Dk2TerrainFixture.RevealMapSpecialObjectId);
        for (int i = 0; i < world.TileCount; i++)
            Assert.True(world.TileAtIndex(i).IsExploredBy(PlayerIds.FirstKeeper));

        var (db2, world2, sim2) = World();
        world2.LocalPlayerId = new PlayerId(1);
        Use(sim2, world2, db2, Dk2TerrainFixture.RevealMapSpecialObjectId);
        Assert.False(world2.Tile(new TilePos(10, 8)).IsExploredBy(PlayerIds.FirstKeeper));
    }

    /// <summary>Special 119 (<c>0x004A43E0</c>): the unowned Rock round the keeper's open floor becomes
    /// its Reinforced Wall; rock touching no floor of the keeper's stays rock.</summary>
    [Fact]
    public void MakeSafe_004A43E0_ReinforcesTheRockBorderingTheKeepersOpenFloor()
    {
        var (db, world, sim) = World();
        Use(sim, world, db, Dk2TerrainFixture.MakeSafeSpecialObjectId);

        TileState east = world.Tile(new TilePos(4, 2)), corner = world.Tile(new TilePos(4, 4));
        Assert.Equal((Dk2TerrainFixture.ReinforcedWall, PlayerIds.FirstKeeper), (east.TerrainId, east.OwnerPlayerId));
        Assert.Equal((Dk2TerrainFixture.ReinforcedWall, PlayerIds.FirstKeeper), (corner.TerrainId, corner.OwnerPlayerId));
        Assert.Equal(Dk2TerrainFixture.Rock, world.Tile(new TilePos(5, 2)).TerrainId);
        Assert.Equal(Dk2TerrainFixture.ImpenetrableRock, world.Tile(new TilePos(0, 2)).TerrainId);
    }

    /// <summary>Special 120 (<c>0x004A45F0</c>): an enemy's Reinforced Wall (a <c>PlayerColouredWall</c>)
    /// falls to its destroyed type and its owner hears of it; the keeper's own wall and unowned rock stand.</summary>
    [Fact]
    public void DestroyWalls_004A45F0_BreaksOnlyTheEnemysColouredWalls()
    {
        const byte enemy = PlayerIds.FirstKeeper + 1;
        var (db, world, sim) = World(enemy);
        ref TileState theirs = ref world.Tile(new TilePos(8, 6));
        theirs.TerrainId = Dk2TerrainFixture.ReinforcedWall; theirs.OwnerPlayerId = enemy; theirs.Health = 900;
        ref TileState mine = ref world.Tile(new TilePos(4, 2));
        mine.TerrainId = Dk2TerrainFixture.ReinforcedWall; mine.OwnerPlayerId = PlayerIds.FirstKeeper; mine.Health = 900;

        Use(sim, world, db, Dk2TerrainFixture.DestroyWallsSpecialObjectId);

        Assert.Equal(Dk2TerrainFixture.Rock, world.Tile(new TilePos(8, 6)).TerrainId);
        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, world.Tile(new TilePos(4, 2)).TerrainId);
        Assert.Equal(Dk2TerrainFixture.Rock, world.Tile(new TilePos(10, 8)).TerrainId);
        Assert.Contains(world.Messages.Entries, m => m.Kind == PlayerMessageKind.WallBreached && m.PlayerId == enemy);
    }

    /// <summary>Special 123 (<c>0x004A4870</c>): another player's worker is stunned (255, for its
    /// <c>StunDuration</c>); the keeper's own worker and another's non-worker are not.</summary>
    [Fact]
    public void StunImps_004A4870_StunsOnlyOtherPlayersWorkers()
    {
        var (db, world, sim) = World();
        CreatureState theirImp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, 1, new TilePos(2, 2));
        CreatureState myImp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, PlayerIds.FirstKeeper, new TilePos(2, 2));
        CreatureState theirGoblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 1, new TilePos(2, 2));

        Use(sim, world, db, Dk2TerrainFixture.StunImpsSpecialObjectId);

        Assert.Equal(255, theirImp.Dk2StateId);
        Assert.NotEqual(255, myImp.Dk2StateId);
        Assert.NotEqual(255, theirGoblin.Dk2StateId);
    }

    /// <summary>Object 7 has no arm in <c>0x004A3FC0</c>, yet action 88 (<c>0x00513760</c>) destroys
    /// any special it is given.</summary>
    [Fact]
    public void Action88_00513760_UsesUpEverySpecialItIsGiven()
    {
        var (db, world, sim) = World();
        foreach (byte type in new byte[] { 118, 119, 120, 121, 122, 123, 125, 126, 127, 128 })
            Use(sim, world, db, type);
    }
}
