// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class CreateGoldTests
{
    private const byte Keeper = 3, Rival = 4, SpellId = 13;
    private static readonly TilePos At = new(10, 10);

    private static KeeperSpellDef Spell() => new()
    {
        Name = "Create Gold", KeeperSpellId = SpellId, ManaCost = 15000,
        ManaUpkeepPerSecond = 0, ShotTypeId = 5, ShotData1 = 1, ShotData2 = 1000,
        BonusShotTypeId = 5, BonusShotData1 = 1, BonusShotData2 = 2000,
        LocationSelector = 1, TargetSelector = 4,
    };

    private static (GameDatabase Db, WorldState World) Setup(KeeperSpellDef? spell = null)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values,
            basis.Variables, [spell ?? Spell()], objects: basis.Objects.Values, shots: basis.Shots.Values);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        world.Player(Keeper).Mana = 45000;
        world.Player(Keeper).Availabilities.Add(new AvailabilityState(5, SpellId, 3));
        world.Tile(At).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(At).OwnerPlayerId = Keeper;
        return (db, world);
    }

    private static CastSpellAtPlaceCommand Cast() =>
        new(Keeper, SpellId, CreatureState.CentreOf(At.X), CreatureState.CentreOf(At.Y));

    private static ISimulationSystem[] Systems() => [new KeeperSpellSystem(), new ShotSystem()];

    [Theory]
    [InlineData(false, 1000)]
    [InlineData(true, 2000)]
    public void CastCreatesNeutralFloorGoldAtThePositionPaysOnceAndStampsBothClocks(bool upgraded, int amount)
    {
        var (db, world) = Setup();
        world.SetUpgraded(Keeper, 5, SpellId, upgraded);
        var sim = new GameSimulation(db, world, 1, Systems());
        sim.Run(7);
        sim.Tick(InputFrame.Of(Cast()));
        ObjectState pile = Assert.Single(world.Objects);
        Assert.Equal(FloorGold.FloorGoldObjectId, pile.ObjectTypeId);
        Assert.Equal(PlayerIds.Neutral, pile.OwnerPlayerId);
        Assert.Equal(At.Y * world.Width + At.X, pile.TileIndex);
        Assert.Equal(amount, pile.GoldHeld);
        Assert.Equal(30000, world.Player(Keeper).Mana);
        Assert.Equal(15000, world.Player(Keeper).OneShotManaSpent);
        var row = Assert.Single(world.Player(Keeper).Availabilities);
        Assert.Equal((7L, 7L), (row.LastCastTick, row.LastCheckedTick));
        Assert.Equal(0, GoldStore.Total(world, Keeper));
        sim.Run(20);
        Assert.Equal(amount, Assert.Single(world.Objects).GoldHeld);
        Assert.Equal(15000, world.Player(Keeper).OneShotManaSpent);
    }

    [Theory]
    [InlineData(0, KeeperSpellRefusal.Mana)]
    [InlineData(1, KeeperSpellRefusal.Location)]
    [InlineData(2, KeeperSpellRefusal.Location)]
    [InlineData(3, KeeperSpellRefusal.Location)]
    [InlineData(4, KeeperSpellRefusal.UnsupportedFlags)]
    [InlineData(5, KeeperSpellRefusal.NoSpellEntry)]
    [InlineData(6, KeeperSpellRefusal.Location)]
    public void RefusalDoesNotCreateGoldPayOrStamp(int change, KeeperSpellRefusal reason)
    {
        var (db, world) = Setup();
        CastSpellAtPlaceCommand cast = Cast();
        switch (change)
        {
            case 0: world.Player(Keeper).Mana = 14999; break;
            case 1: world.Tile(At).OwnerPlayerId = Rival; break;
            case 2: world.Tile(At).OwnerPlayerId = PlayerIds.Neutral; break;
            case 3: world.Tile(At).TerrainId = Dk2TerrainFixture.Rock; break;
            case 4: cast = cast with { Flags = 1 }; break;
            case 5: world.Player(Keeper).Availabilities.Clear(); break;
            case 6: cast = cast with { X = -1 }; break;
        }
        int before = world.Player(Keeper).Mana;
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, Systems());
        sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(cast));
        Assert.Contains($"reason={reason}", Assert.Single(sink.Events, e => e.System == "keeper_spells").Detail);
        Assert.Empty(world.Objects);
        Assert.Empty(world.Shots);
        Assert.Equal(before, world.Player(Keeper).Mana);
        Assert.Equal(0, world.Player(Keeper).OneShotManaSpent);
        Assert.All(world.Player(Keeper).Availabilities, row => Assert.Equal((0L, 0L), (row.LastCastTick, row.LastCheckedTick)));
    }

    [Theory]
    [InlineData(RoomTypeId.Prison)]
    [InlineData(RoomTypeId.Portal)]
    public void OwnSpecialRoomInteriorIsRefused(RoomTypeId kind)
    {
        var (db, world) = Setup();
        byte terrain = db.Rooms.Values.Single(r => r.TypeId == kind).TerrainId;
        for (int y = 8; y <= 12; y++)
            for (int x = 8; x <= 12; x++)
            {
                world.Tile(x, y).TerrainId = terrain;
                world.Tile(x, y).OwnerPlayerId = Keeper;
            }
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, 1, Systems());
        sim.ObserveWith(sink);
        sim.Tick(InputFrame.Of(Cast()));
        Assert.Contains("reason=Location", Assert.Single(sink.Events, e => e.System == "keeper_spells").Detail);
        Assert.Empty(world.Objects);
        Assert.Equal(45000, world.Player(Keeper).Mana);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(250)]
    public void AFullTreasuryDoesNotRefuseTheCastAndASweepOnlyTakesWhatFits(int space)
    {
        var (db, world) = Setup();
        world.Tile(At).TerrainId = db.Rooms.Values.Single(r => r.TypeId == RoomTypeId.Treasury).TerrainId;
        int capacity = GoldStore.Capacity(db, world.Tile(At));
        world.Tile(At).GoldHeld = capacity - space;
        var sim = new GameSimulation(db, world, 1, Systems());
        sim.Tick(InputFrame.Of(Cast()));
        Assert.Equal(capacity - space, world.Tile(At).GoldHeld);
        Assert.Equal(1000, Assert.Single(world.Objects).GoldHeld);
        var withSweep = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db,
            [new KeeperSpellSystem(), new ShotSystem(), new StoreSweepSystem()]);
        withSweep.Run(StoreSweepSystem.SweepTurns);
        Assert.Equal(capacity, withSweep.World.Tile(At).GoldHeld);
        Assert.Equal(1000 - space, Assert.Single(withSweep.World.Objects).GoldHeld);
        Assert.Equal(30000, withSweep.World.Player(Keeper).Mana);
    }

    [Fact]
    public void AlliedLandAndExactManaCostAreEnoughWithoutStoredGold()
    {
        var (db, world) = Setup();
        world.SetAllied(Keeper, Rival, true);
        world.Tile(At).OwnerPlayerId = Rival;
        world.Player(Keeper).Mana = db.KeeperSpellById(SpellId)!.ManaCost;
        new GameSimulation(db, world, 1, Systems()).Tick(InputFrame.Of(Cast()));
        Assert.Equal(0, world.Player(Keeper).Mana);
        Assert.Equal(1000, Assert.Single(world.Objects).GoldHeld);
        Assert.Equal(0, GoldStore.Total(world, Keeper));
    }

    [Fact]
    public void PendingCastSurvivesASnapshotAndUsesTheDescriptorRatherThanFixedAmounts()
    {
        var (db, world) = Setup(Spell() with { ManaCost = 321, ShotData2 = 4321 });
        var sim = new GameSimulation(db, world, 1, [new KeeperSpellSystem()]);
        sim.Tick(InputFrame.Of(Cast()));
        Assert.Equal(4321, Assert.Single(world.Shots).Data2);
        Assert.Equal(45000 - 321, world.Player(Keeper).Mana);
        var copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, Systems());
        var original = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, Systems());
        Assert.Equal(sim.StateHash(), copy.StateHash());
        copy.Run(20);
        original.Run(20);
        Assert.Equal(original.StateHash(), copy.StateHash());
        Assert.Equal(4321, Assert.Single(copy.World.Objects).GoldHeld);
        Assert.Equal(PlayerIds.Neutral, Assert.Single(copy.World.Objects).OwnerPlayerId);
    }
}
