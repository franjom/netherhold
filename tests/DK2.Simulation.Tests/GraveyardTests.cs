// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The Graveyard's burial <c>0x004DD470</c>, its capacity gate <c>0x004DDCA0</c>, its
/// Vampire <c>0x004DD820</c> (graveyard.md §2, ADR-0370) and the Vampire's reincarnation
/// <c>0x0048FFC0</c> (creature-death.md §2.3, ADR-0371).</summary>
public class GraveyardTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte CorpseId = 61;

    [Fact]
    public void FiveGoblinCorpsesRaiseTheRoomsCreatedCreature_OnOneOfItsTiles()
    {
        (GameDatabase db, WorldState world) = Setup(Corpse(200));
        RoomInstance room = Graveyard(db, world, 2, 2);
        GraveyardBurialResult result = default;

        for (int i = 0; i < 5; i++) result = Bury(db, world, room);

        CreatureState vampire = Assert.Single(world.Creatures, c => c.CreatureTypeId == room.Definition.CreatedCreatureId);
        Assert.Equal(vampire.Id, result.SpawnedCreatureId);
        Assert.Contains((vampire.Tile.Y * world.Width) + vampire.Tile.X, room.TileIndices);
        Assert.Equal(0, Assert.Single(world.Graveyards).DecomposeValue);
    }

    [Fact]
    public void SeparateRoomsKeepSeparateTallies()
    {
        (GameDatabase db, WorldState world) = Setup(Corpse(400));
        RoomInstance first = Graveyard(db, world, 2, 2);
        RoomInstance second = Graveyard(db, world, 7, 2);

        Bury(db, world, first);
        Bury(db, world, first);
        Bury(db, world, second);

        Assert.Equal([400, 800], world.Graveyards.Select(g => g.DecomposeValue).Order().ToArray());
        Assert.Empty(world.Creatures);
    }

    /// <summary>The threshold zeroes the tally; the excess is lost and one burial raises one
    /// creature however large (Q319, Q320). Ours subtracted 1000 and kept 1200.</summary>
    [Fact]
    public void TheThresholdZeroesTheTally_TheExcessIsLost()
    {
        (GameDatabase db, WorldState world) = Setup(Corpse(2200));
        RoomInstance room = Graveyard(db, world, 2, 2);

        GraveyardBurialResult result = Bury(db, world, room);

        Assert.True(result.SpawnedCreatureId > 0);
        Assert.Equal(0, result.DecomposeValue);
        Assert.Equal(0, Assert.Single(world.Graveyards).DecomposeValue);
        Assert.Single(world.Creatures);
    }

    /// <summary>The gate: a Graveyard's capacity is its floor slots, so a one-tile Graveyard holds
    /// no Vampire; the tally keeps growing and message 42 is posted. Ours had no gate.</summary>
    [Fact]
    public void AGraveyardWithNoFloorSlotRaisesNothing_AndKeepsCounting()
    {
        (GameDatabase db, WorldState world) = Setup(Corpse(800));
        RoomInstance room = Graveyard(db, world, 2, 2, size: 1);

        Bury(db, world, room);
        GraveyardBurialResult result = Bury(db, world, room);

        Assert.Equal(1600, result.DecomposeValue);
        Assert.Empty(world.Creatures);
        Assert.Contains(world.Messages.Entries, m => m.PlayerId == Keeper && m.Kind == PlayerMessageKind.RoomFull);
    }

    /// <summary>Q321: selling any tile of a Graveyard rebuilds what is left with its tally at 0.
    /// Ours kept the tally of a part-sold room.</summary>
    [Fact]
    public void SellingPartOfAGraveyardLosesItsTally()
    {
        (GameDatabase db, WorldState world) = Setup(Corpse(600));
        RoomInstance room = Graveyard(db, world, 2, 2);
        Bury(db, world, room);
        Assert.Equal(600, Assert.Single(world.Graveyards).DecomposeValue);

        new GameSimulation(db, world, 1, [new RoomSellSystem()])
            .Tick(InputFrame.Of(SellRoomCommand.At(Keeper, new TilePos(4, 4))));
        RoomInstance rest = RoomIndex.RoomAt(world, db, (2 * world.Width) + 2)!;
        Bury(db, world, rest);

        Assert.Equal(600, Assert.Single(world.Graveyards).DecomposeValue);
    }

    [Fact]
    public void PersistentTally_SurvivesSnapshotAndChangesTheStateHash()
    {
        (GameDatabase db, WorldState world) = Setup(Corpse(200));
        RoomInstance room = Graveyard(db, world, 2, 2);
        var sim = new GameSimulation(db, world, 42, [new GraveyardSystem()]);
        ulong before = sim.StateHash();
        Bury(db, world, room);

        Assert.NotEqual(before, sim.StateHash());
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [new GraveyardSystem()]);

        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal(200, Assert.Single(restored.World.Graveyards).DecomposeValue);
    }

    /// <summary>Above level 1 the burial scales the corpse's value by stat 19 and keeps it within
    /// 0 to 0xFFFE (<c>0x004DD470</c>); at level 1 it adds the raw word. The shipped stat-19 row is
    /// 100% at every level, so only the clamp tells the two paths apart.</summary>
    [Theory]
    [InlineData(1, 0xFFFF)]
    [InlineData(3, 0xFFFE)]
    public void ABurialAboveLevelOneIsClampedTo0xFFFE_AtLevelOneItIsTheRawWord(int level, int tally)
    {
        (GameDatabase db, WorldState world) = Setup(Corpse(0xFFFF));
        RoomInstance room = Graveyard(db, world, 2, 2, size: 1);

        Assert.Equal(tally, Bury(db, world, room, level).DecomposeValue);
    }

    /// <summary>Q329: a Vampire whose health runs out comes back at a Graveyard one level down; at
    /// level 1, or with no Graveyard, it does not. Its health is the OLD level's maximum, since
    /// <c>Creature_LevelDown</c> indexes the old level both times; its experience is zeroed and its
    /// Combat Pit mark cleared. The place is the room's centre field, which is STRONGLY INFERRED
    /// (Q515).</summary>
    [Theory]
    [InlineData(3, true, true)]
    [InlineData(1, true, false)]
    [InlineData(3, false, false)]
    public void AVampireReincarnatesOnlyAboveLevelOneWithAGraveyard_AtTheInferredCentreField(int level, bool graveyard, bool back)
    {
        (GameDatabase db, WorldState world) = Setup(Corpse(200));
        RoomInstance? room = graveyard ? Graveyard(db, world, 2, 2) : null;
        CreatureState vampire = DyingVampire(db, world, level, new TilePos(9, 9));

        new GameSimulation(db, world, 1, [new CreatureHealthSystem()]).Run(4);

        if (back)
        {
            Assert.Equal(new TilePos(room!.CentreFieldX, room.CentreFieldY), vampire.Tile);
            Assert.Equal(level - 1, vampire.Level);
            Assert.Equal(CreatureLevelRules.MaxHealth(db, db.CreatureById(vampire.CreatureTypeId), level), vampire.Health);
            Assert.Equal(0, vampire.ExperiencePoints);
            Assert.False(vampire.CombatPitFighter);
        }
        else
        {
            Assert.True(!world.Creatures.Contains(vampire) || CreatureDeathRules.IsDown(vampire));
        }
    }

    /// <summary>The gate counts the owner's living <c>CanBeResurrected</c> creatures, so a 3x3
    /// Graveyard's one floor slot taken by a living Vampire refuses a second.</summary>
    [Fact]
    public void AFullGraveyardGateRefusesReincarnation()
    {
        (GameDatabase db, WorldState world) = Setup(Corpse(200));
        Graveyard(db, world, 2, 2);
        world.SpawnCreature(db, Dk2TerrainFixture.VampireCreatureId, Keeper, new TilePos(12, 12));
        CreatureState vampire = DyingVampire(db, world, 3, new TilePos(9, 9));

        new GameSimulation(db, world, 1, [new CreatureHealthSystem()]).Run(4);

        Assert.Equal(3, vampire.Level);
        Assert.True(!world.Creatures.Contains(vampire) || CreatureDeathRules.IsDown(vampire));
    }

    /// <summary>A Vampire killed on an interior Temple cell stays dead.</summary>
    [Fact]
    public void AVampireKilledOnAnInteriorTempleCellIsNotReincarnated()
    {
        (GameDatabase db, WorldState world) = Setup(Corpse(200));
        Graveyard(db, world, 2, 2);
        for (int y = 8; y < 11; y++)
            for (int x = 8; x < 11; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Temple), Keeper);
        CreatureState vampire = DyingVampire(db, world, 3, new TilePos(9, 9));

        new GameSimulation(db, world, 1, [new CreatureHealthSystem()]).Run(4);

        Assert.Equal(3, vampire.Level);
        Assert.True(!world.Creatures.Contains(vampire) || CreatureDeathRules.IsDown(vampire));
    }

    private static CreatureState DyingVampire(GameDatabase db, WorldState world, int level, TilePos at)
    {
        CreatureState vampire = world.SpawnCreature(db, Dk2TerrainFixture.VampireCreatureId, Keeper, at);
        vampire.Level = level;
        vampire.Health = 0;
        vampire.ExperiencePoints = 50;
        vampire.CombatPitFighter = true;
        return vampire;
    }

    private static CreatureDef Corpse(ushort decompose) =>
        Dk2TerrainFixture.Goblin() with { Name = "Corpse", CreatureId = CorpseId, DecomposeValue = decompose };

    private static (GameDatabase Db, WorldState World) Setup(CreatureDef corpse)
    {
        GameDatabase db = Dk2TerrainFixture.Database(corpse);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Neutral, Keeper]);
        return (db, world);
    }

    private static RoomInstance Graveyard(GameDatabase db, WorldState world, int x0, int y0, int size = 3)
    {
        for (int y = y0; y < y0 + size; y++)
            for (int x = x0; x < x0 + size; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Graveyard), Keeper);
        return RoomIndex.RoomAt(world, db, (y0 * world.Width) + x0)!;
    }

    private static GraveyardBurialResult Bury(GameDatabase db, WorldState world, RoomInstance room, int level = 1)
    {
        GraveyardBurialResult result = default;
        new GameSimulation(db, world, 7, [new Probe(context =>
            result = GraveyardRules.Bury(context, room, db.CreatureById(CorpseId), level))]).Tick(InputFrame.Empty);
        return result;
    }

    private sealed class Probe(Action<SimulationContext> run) : ISimulationSystem
    {
        public string Name => "probe";
        public void Execute(SimulationContext context) => run(context);
    }
}
