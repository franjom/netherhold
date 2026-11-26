// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
namespace DK2.Simulation.Tests;

public sealed class PossessionTargetPreviewTests
{
    private const byte Keeper = PlayerIds.FirstKeeper, Rival = Keeper + 1;
    private static (GameDatabase Db, WorldState World, CreatureState Me) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database(Dk2TerrainFixture.Possessor());
        var world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [1, 2, Keeper, Rival]);
        CreatureState me = world.SpawnCreature(db, Dk2TerrainFixture.PossessorCreatureId, Keeper, new(10, 10));
        me.Possessed = true;
        me.Heading = 0x200;
        return (db, world, me);
    }
    private static PossessionTarget Preview(GameDatabase db, WorldState world, CreatureState me, bool melee = true, int slot = 0) =>
        PossessionRules.PreviewTarget(world, db, new ClearSight(), me,
            new ActPossessedCreatureCommand(Keeper,
                new(new(new(me.PosX), new(me.PosY), new(Fp32.One + me.PosZ)), new(0x200), new(0x200)), melee, new(slot)));
    private sealed class ClearSight : ISightBlocker { public bool Blocks(int x, int y) => false; }

    [Fact]
    public void MeleeHighlightsOneTrainingDummyInReach()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        ObjectState dummy = world.SpawnObject(db, 14, Keeper, 10 * world.Width + 10);
        dummy.PosX = me.PosX + 1024;
        dummy.PosY = me.PosY;
        Assert.Equal(new PossessionTarget(FightTargetKind.Object, dummy.Id, new(10, 10)), Preview(db, world, me));
    }
    [Fact]
    public void MeleeHighlightsOneEnemyDoor()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
            { X = 11, Y = 10, PlayerId = Rival, DoorId = 1, Unknown_0011 = new byte[3] }]);
        DoorState door = Assert.Single(world.Doors);
        Assert.Equal(new PossessionTarget(FightTargetKind.Door, door.Id, new(11, 10)), Preview(db, world, me));
    }
    [Fact]
    public void MeleeNamesOnlyOneEnemyFromSeveralAndChangesWithTheAim()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        CreatureState east = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new(11, 10));
        CreatureState north = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new(10, 9));
        Assert.Equal(east.Id, Preview(db, world, me).Id);
        var command = new ActPossessedCreatureCommand(Keeper,
            new(new(new(me.PosX), new(me.PosY), new(Fp32.One + me.PosZ)), new(0), new(0x200)), true, new(0));
        Assert.Equal(north.Id, PossessionRules.PreviewTarget(world, db, new ClearSight(), me, command).Id);
    }
    [Fact]
    public void GroupHighlightsOneFriendlyCreatureAndNoneWhenNobodyQualifies()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        Assert.Equal(PossessionTarget.None, Preview(db, world, me, false, 3));
        CreatureState friendly = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new(11, 10));
        Assert.Equal(new PossessionTarget(FightTargetKind.Creature, friendly.Id, friendly.Tile), Preview(db, world, me, false, 3));
        Assert.Equal(PossessionTarget.None, Preview(db, world, me));
    }

    [Fact]
    public void PreviewDoesNotStampAttackRecruitOrChangeTheWorldHash()
    {
        (GameDatabase? db, WorldState? world, CreatureState? me) = Open();
        CreatureState friendly = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new(11, 10));
        var simulation = new GameSimulation(db, world, 1, []);
        ulong before = simulation.StateHash();
        Assert.Equal(friendly.Id, Preview(db, world, me, false, 3).Id);
        Assert.Equal(before, simulation.StateHash());
        Assert.Equal(0, friendly.FollowLeaderId);
        Assert.Equal(0, me.FollowerCount);
        Assert.False(me.SwingInProgress);
        Assert.Equal(CreatureState.NeverUsed, me.TimeLastUsedMelee);
    }
}
