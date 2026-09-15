// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class TempleSacrificeTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private static readonly TilePos Floor = new(1, 1);

    [Fact]
    public void StronglyInferred_OrderedSuffixMatchClearsTheWholeRoomHistory()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = Setup(Floor);
        db.Variables.Sacrifices.Add(new SacrificeEntry(
            1, Dk2TerrainFixture.GoblinCreatureId,
            1, Dk2TerrainFixture.ImpCreatureId,
            0, 0, 1, 20, Dk2TerrainFixture.VampireCreatureId));

        DropCreature(db, world, sim, Dk2TerrainFixture.BileDemonCreatureId, 2, Floor);
        DropCreature(db, world, sim, Dk2TerrainFixture.GoblinCreatureId, 3, Floor);
        DropCreature(db, world, sim, Dk2TerrainFixture.ImpCreatureId, 4, Floor);

        CreatureState reward = Assert.Single(
            world.Creatures, c => c.CreatureTypeId == Dk2TerrainFixture.VampireCreatureId);
        Assert.Equal(3, reward.Level);
        Assert.Equal(0, Assert.Single(world.TempleSacrifices).Count);
    }

    [Fact]
    public void StronglyInferred_ReversedOfferingsMissAndRemainRemembered()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = Setup(Floor);
        db.Variables.Sacrifices.Add(new SacrificeEntry(
            1, Dk2TerrainFixture.GoblinCreatureId,
            1, Dk2TerrainFixture.ImpCreatureId,
            0, 0, 1, 20, Dk2TerrainFixture.VampireCreatureId));

        DropCreature(db, world, sim, Dk2TerrainFixture.ImpCreatureId, 1, Floor);
        DropCreature(db, world, sim, Dk2TerrainFixture.GoblinCreatureId, 1, Floor);

        Assert.DoesNotContain(world.Creatures,
            c => c.CreatureTypeId == Dk2TerrainFixture.VampireCreatureId);
        Assert.Equal(2, Assert.Single(world.TempleSacrifices).Count);
    }

    [Fact]
    public void StronglyInferred_FirstMatchingRecipeInFileOrderWins()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = Setup(Floor);
        db.Variables.Sacrifices.Add(OneCreatureRecipe(
            Dk2TerrainFixture.GoblinCreatureId, Dk2TerrainFixture.VampireCreatureId, 25));
        db.Variables.Sacrifices.Add(OneCreatureRecipe(
            Dk2TerrainFixture.GoblinCreatureId, Dk2TerrainFixture.SkeletonCreatureId, 26));

        DropCreature(db, world, sim, Dk2TerrainFixture.GoblinCreatureId, 1, Floor);

        Assert.Contains(world.Creatures,
            c => c.CreatureTypeId == Dk2TerrainFixture.VampireCreatureId);
        Assert.DoesNotContain(world.Creatures,
            c => c.CreatureTypeId == Dk2TerrainFixture.SkeletonCreatureId);
    }

    [Fact]
    public void StronglyInferred_CreatureRewardAveragesOnlyCreatureIngredientLevels()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = Setup(Floor);
        db.Variables.Sacrifices.Add(new SacrificeEntry(
            1, Dk2TerrainFixture.GoblinCreatureId,
            2, HatcheryRules.ChickenObjectId,
            1, Dk2TerrainFixture.BileDemonCreatureId,
            1, 31, Dk2TerrainFixture.VampireCreatureId));

        DropCreature(db, world, sim, Dk2TerrainFixture.GoblinCreatureId, 3, Floor);
        DropObject(db, world, sim, HatcheryRules.ChickenObjectId, Floor);
        DropCreature(db, world, sim, Dk2TerrainFixture.BileDemonCreatureId, 8, Floor);

        CreatureState reward = Assert.Single(
            world.Creatures, c => c.CreatureTypeId == Dk2TerrainFixture.VampireCreatureId);
        Assert.Equal(5, reward.Level);
        Assert.Equal(CreatureLevelRules.MaxHealth(
            db, db.CreatureById(Dk2TerrainFixture.VampireCreatureId), 5), reward.Health);
    }

    [Fact]
    public void StronglyInferred_ObjectRewardIsNeutralAndAppearsAtNewestOffering()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = Setup(Floor);
        db.Variables.Sacrifices.Add(new SacrificeEntry(
            2, HatcheryRules.ChickenObjectId, 0, 0, 0, 0,
            2, 9, Dk2TerrainFixture.TempleHandObjectId));

        DropObject(db, world, sim, HatcheryRules.ChickenObjectId, Floor);

        ObjectState reward = Assert.Single(
            world.Objects, o => o.ObjectTypeId == Dk2TerrainFixture.TempleHandObjectId);
        Assert.Equal(PlayerIds.Neutral, reward.OwnerPlayerId);
        Assert.Equal((Floor.Y * world.Width) + Floor.X, reward.TileIndex);
    }

    [Fact]
    public void StronglyInferred_GoldUsesSpeechSevenAndNeverEntersHistory()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = Setup(Floor);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        DropObject(db, world, sim, FloorGold.FloorGoldObjectId, Floor);

        Assert.Empty(world.TempleSacrifices);
        Assert.Contains(sink.Events,
            e => e.Kind == "sacrifice_gold" && e.Detail.Contains("speech=7"));
    }

    [Fact]
    public void StronglyInferred_SeparateTempleRoomsKeepSeparateHistories()
    {
        TilePos other = new(3, 1);
        (GameDatabase db, WorldState world, GameSimulation sim) = Setup(Floor, other);
        db.Variables.Sacrifices.Add(new SacrificeEntry(
            1, Dk2TerrainFixture.GoblinCreatureId,
            1, Dk2TerrainFixture.ImpCreatureId,
            0, 0, 1, 20, Dk2TerrainFixture.VampireCreatureId));

        DropCreature(db, world, sim, Dk2TerrainFixture.GoblinCreatureId, 1, Floor);
        DropCreature(db, world, sim, Dk2TerrainFixture.ImpCreatureId, 1, other);

        Assert.Equal(2, world.TempleSacrifices.Count);
        Assert.All(world.TempleSacrifices, state => Assert.Equal(1, state.Count));
        Assert.DoesNotContain(world.Creatures,
            c => c.CreatureTypeId == Dk2TerrainFixture.VampireCreatureId);
    }

    [Fact]
    public void OurAnchorPolicy_ExtensionMergeAndAnchorRemovalAreDeterministic()
    {
        TilePos left = new(1, 1);
        TilePos right = new(3, 1);
        (GameDatabase db, WorldState world, _) = Setup(left, right);
        RememberSpell(world, db, left, 4);
        RememberSpell(world, db, right, 7);

        Paint(db, world, new TilePos(1, 2));
        TempleSacrificeRules.Reconcile(world, db);
        Assert.Equal(2, world.TempleSacrifices.Count);

        Paint(db, world, new TilePos(2, 1));
        TempleSacrificeRules.Reconcile(world, db);
        TempleSacrificeState merged = Assert.Single(world.TempleSacrifices);
        Assert.Equal((left.Y * world.Width) + left.X, merged.AnchorTileIndex);

        ref TileState oldAnchor = ref world.Tile(left);
        oldAnchor.TerrainId = Dk2TerrainFixture.ClaimedPath;
        oldAnchor.Health = db.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;
        TempleSacrificeRules.Reconcile(world, db);
        Assert.Empty(world.TempleSacrifices);
    }

    [Fact]
    public void OurAnchorPolicy_OnSplitOnlyTheComponentRetainingTheAnchorInheritsHistory()
    {
        TilePos anchor = new(1, 1);
        TilePos bridge = new(2, 1);
        TilePos separated = new(3, 1);
        (GameDatabase db, WorldState world, _) = Setup(anchor, bridge, separated);
        RememberSpell(world, db, anchor, 4);

        ref TileState cut = ref world.Tile(bridge);
        cut.TerrainId = Dk2TerrainFixture.ClaimedPath;
        cut.Health = db.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;
        TempleSacrificeRules.Reconcile(world, db);

        TempleSacrificeState inherited = Assert.Single(world.TempleSacrifices);
        Assert.Equal((anchor.Y * world.Width) + anchor.X, inherited.AnchorTileIndex);
        Assert.Equal(1, inherited.Count);
        Assert.NotNull(RoomIndex.RoomAt(
            world, db, (separated.Y * world.Width) + separated.X));
    }

    [Fact]
    public void StronglyInferred_HistorySurvivesSnapshotAndChangesTheStateHash()
    {
        (GameDatabase db, WorldState world, GameSimulation sim) = Setup(Floor);
        ulong before = sim.StateHash();
        RememberSpell(world, db, Floor, 9);

        Assert.NotEqual(before, sim.StateHash());
        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(sim), db, [new HandSystem(), new TempleSacrificeSystem()]);

        Assert.Equal(sim.StateHash(), restored.StateHash());
        TempleSacrificeState history = Assert.Single(restored.World.TempleSacrifices);
        Assert.Equal(1, history.Count);
    }

    private static SacrificeEntry OneCreatureRecipe(byte input, byte output, byte speech) =>
        new(1, input, 0, 0, 0, 0, 1, speech, output);

    private static (GameDatabase Db, WorldState World, GameSimulation Sim) Setup(
        params TilePos[] temples)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        foreach (TilePos temple in temples) Paint(db, world, temple);
        return (db, world, new GameSimulation(
            db, world, seed: 42, [new HandSystem(), new TempleSacrificeSystem()]));
    }

    private static void Paint(GameDatabase db, WorldState world, TilePos tile)
    {
        ref TileState state = ref world.Tile(tile);
        RoomRules.Place(db, ref state, db.Room(RoomTypeId.Temple), Keeper);
    }

    private static void DropCreature(
        GameDatabase db, WorldState world, GameSimulation sim,
        byte creatureTypeId, int level, TilePos temple)
    {
        CreatureState creature = world.SpawnCreature(db, creatureTypeId, Keeper, new TilePos(2, 2));
        creature.Level = level;
        creature.Health = CreatureLevelRules.MaxHealth(db, db.CreatureById(creatureTypeId), level);
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, creature.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, temple)));
    }

    private static void DropObject(
        GameDatabase db, WorldState world, GameSimulation sim, byte objectTypeId, TilePos temple)
    {
        ObjectState thing = world.SpawnObject(db, objectTypeId, Keeper, (2 * world.Width) + 2);
        sim.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, thing.Id)));
        sim.Tick(InputFrame.Of(new DropCommand(Keeper, temple)));
    }

    private static void RememberSpell(
        WorldState world, GameDatabase db, TilePos templeTile, byte spellId)
    {
        int tileIndex = (templeTile.Y * world.Width) + templeTile.X;
        RoomInstance room = RoomIndex.RoomAt(world, db, tileIndex)!;
        TempleSacrificeRules.OfferKeeperSpell(world, db, room, spellId, tileIndex);
    }
}
