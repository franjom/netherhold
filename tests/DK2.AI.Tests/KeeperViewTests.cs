// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI.Tests;

public class KeeperViewTests
{
    [Fact]
    public void LiveThreatUsesReverseActivationOrderAndPreservesItAcrossPackets()
    {
        GameDatabase basis = KeeperWorld.Database();
        var trap = new TrapDef
        {
            Name = "test trap", TrapId = 3, Threat = 74, Health = 1, GoldCost = 1,
            ManufactureRequired = 1, ShotTypeId = 0, ManaUpkeepPerSecond = 0,
        };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values, basis.Variables, traps: [trap]);
        WorldState world = KeeperWorld.World(db);
        foreach (byte owner in new byte[] { 1, 2 })
            ThingProjection.Place(world, db, [new TrapThing(new byte[16])
            {
                X = 15, Y = 15, PlayerId = owner, TrapId = 3, NumberOfShots = 0,
            }]);
        byte[] activation = [1, 3, 2];
        var captured = new KeeperView(db, world, KeeperWorld.Keeper, activation);
        activation[0] = 2;
        KeeperView restored = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(captured), db);
        Assert.Equal(new byte[] { 2, 3, 1 }, restored.PlayerOrder);
        Assert.Equal(new KeeperThreatAssessment(148, 0, 1),
            ThreatEvaluator.Assess(restored, KeeperWorld.HeartAt, false, true));
        Assert.Equal(new KeeperThreatAssessment(0, 0, null),
            ThreatEvaluator.Assess(restored, KeeperWorld.HeartAt, false, false));
        Assert.Equal(new KeeperThreatAssessment(0, 0, null),
            ThreatEvaluator.Assess(restored, KeeperWorld.HeartAt, true, true));
        world.Player(KeeperWorld.Keeper).AlliedWith = 1 << 1;
        Assert.Equal(new KeeperThreatAssessment(74, 74, 2), ThreatEvaluator.Assess(
            new KeeperView(db, world, KeeperWorld.Keeper, [1, 3, 2]), KeeperWorld.HeartAt, false, true));
        Assert.Equal(new KeeperThreatAssessment(148, 0, 1),
            ThreatEvaluator.Assess(captured, KeeperWorld.HeartAt, false, true));
    }

    [Fact]
    public void TrapObservationIncludesBlueprintsAndKeepsDefinitionThreatAcrossPackets()
    {
        GameDatabase basis = KeeperWorld.Database();
        var trap = new TrapDef
        {
            Name = "test trap", TrapId = 3, Threat = 43210, Health = 1, GoldCost = 1,
            ManufactureRequired = 1, ShotTypeId = 0, ManaUpkeepPerSecond = 0,
        };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values,
            basis.Creatures.Values, basis.Variables, traps: [trap]);
        WorldState world = KeeperWorld.World(db);
        ThingProjection.Place(world, db, [new TrapThing(new byte[16])
        {
            X = 14, Y = 16, PlayerId = KeeperWorld.Keeper, TrapId = 3, NumberOfShots = 0,
        }]);
        Assert.True(world.Traps[0].UnderConstruction);
        var captured = new KeeperView(db, world, KeeperWorld.Keeper);
        byte[] packet = KeeperViewSnapshot.ToBytes(captured);
        KeeperView restored = KeeperViewSnapshot.FromBytes(packet, db);
        Assert.Equal(new KeeperTrapView(3, KeeperWorld.Keeper, 14, 16, 43210), Assert.Single(restored.Traps));
        Assert.Equal(new KeeperThreatSource(KeeperWorld.Keeper, 14 * 4096 + 2048,
            16 * 4096 + 2048, 43210, 0, true, 3), Assert.Single(ThreatEvaluator.Sources(restored)));
        Assert.Equal(packet, KeeperViewSnapshot.ToBytes(restored));
        ThingProjection.Place(world, db, [new TrapThing(new byte[16])
        {
            X = 16, Y = 14, PlayerId = 2, TrapId = 3, NumberOfShots = 1,
        }]);
        Assert.Single(captured.Traps);
        Assert.Equal(2, new KeeperView(db, world, KeeperWorld.Keeper).Traps.Count);
    }

    [Fact]
    public void ThreatDropCandidatesUseNavigationSolidityRatherThanWalkingPassability()
    {
        GameDatabase basis = KeeperWorld.Database();
        TerrainDef floor = basis.TerrainById(KeeperWorld.ClaimedPath);
        var db = new GameDatabase(basis.Terrain.Values.Concat([
            floor with { TerrainId = 30, Flags = TerrainFlags.Water },
            floor with { TerrainId = 31, Flags = TerrainFlags.Lava }]),
            basis.Rooms.Values, basis.Creatures.Values, basis.Variables);
        WorldState world = KeeperWorld.World(db);
        var water = new TilePos(5, 5);
        var lava = new TilePos(6, 5);
        var solid = new TilePos(7, 5);
        var enemy = new TilePos(8, 5);
        KeeperWorld.Set(world, db, water, 30, KeeperWorld.Keeper);
        KeeperWorld.Set(world, db, lava, 31, KeeperWorld.Keeper);
        KeeperWorld.Set(world, db, solid, KeeperWorld.Rock, KeeperWorld.Keeper);
        KeeperWorld.Set(world, db, enemy, KeeperWorld.ClaimedPath, 2);
        for (int y = 8; y <= 10; y++)
            for (int x = 8; x <= 10; x++)
                KeeperWorld.Set(world, db, new TilePos(x, y), KeeperWorld.PortalFloor, KeeperWorld.Keeper);
        var portal = new TilePos(9, 9);
        var door = new TilePos(14, 14);
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
        {
            X = door.X, Y = door.Y, PlayerId = KeeperWorld.Keeper,
            DoorId = 1, Flags = 1, Unknown_0011 = new byte[3],
        }]);
        var captured = new KeeperView(db, world, KeeperWorld.Keeper);
        KeeperView restored = KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(captured), db);
        Assert.Equal(NavBytes.Water, restored.Tile(water).NavigationByte);
        Assert.Equal(NavBytes.Lava, restored.Tile(lava).NavigationByte);
        Assert.Equal(NavBytes.PortalHole, restored.Tile(portal).NavigationByte);
        Assert.Equal(NavBytes.Door(KeeperWorld.Keeper, true, false), restored.Tile(door).NavigationByte);
        foreach (TilePos candidate in new[] { water, lava, portal, door, KeeperWorld.HeartAt })
            Assert.True(restored.IsThreatDropCandidate(candidate));
        foreach (TilePos rejected in new[] { solid, enemy, new TilePos(-1, 5), new TilePos(world.Width, 5) })
            Assert.False(restored.IsThreatDropCandidate(rejected));
        Assert.False(restored.IsPassable(water));
        Assert.False(restored.IsPassable(lava));

        world.Doors[0].Open = true;
        KeeperWorld.Set(world, db, water, KeeperWorld.Rock, KeeperWorld.Keeper);
        var current = new KeeperView(db, world, KeeperWorld.Keeper);
        Assert.False(current.IsThreatDropCandidate(water));
        Assert.True(captured.IsThreatDropCandidate(water));
        Assert.Equal(NavBytes.Floor, current.Tile(door).NavigationByte);
        Assert.Equal(restored.Tile(door).NavigationByte, captured.Tile(door).NavigationByte);
    }

    [Fact]
    public void AlliancesAndResearchUpgradesRemainFrozenAcrossPacketRoundTrip()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        world.Player(KeeperWorld.Keeper).AlliedWith = 1 << 4;
        world.SetUpgraded(KeeperWorld.Keeper, 5, 4, true);
        var captured = new KeeperView(db, world, KeeperWorld.Keeper);
        byte[] packet = KeeperViewSnapshot.ToBytes(captured);
        KeeperView restored = KeeperViewSnapshot.FromBytes(packet, db);
        Assert.Equal(packet, KeeperViewSnapshot.ToBytes(restored));
        for (byte player = 0; player <= 8; player++)
            Assert.Equal(world.AreAllied(KeeperWorld.Keeper, player), restored.AreAllied(player));
        Assert.Equal(world.PlayerIds.Where(id => id != PlayerIds.Unowned).Reverse(), restored.PlayerOrder);
        Assert.True(restored.IsUpgraded(5, 4));
        Assert.False(restored.IsUpgraded(5, 255));

        world.Player(KeeperWorld.Keeper).AlliedWith = 0;
        world.SetUpgraded(KeeperWorld.Keeper, 5, 4, false);
        Assert.True(captured.AreAllied(4));
        Assert.True(captured.IsUpgraded(5, 4));
        var current = new KeeperView(db, world, KeeperWorld.Keeper);
        Assert.False(current.AreAllied(4));
        Assert.False(current.IsUpgraded(5, 4));
    }

    [Fact]
    public void EarlierObservationSchemaIsRejectedExplicitly()
    {
        GameDatabase db = KeeperWorld.Database();
        byte[] packet = KeeperViewSnapshot.ToBytes(new KeeperView(db, KeeperWorld.World(db), KeeperWorld.Keeper));
        packet[4] = 5;
        Assert.Throws<InvalidDataException>(() => KeeperViewSnapshot.FromBytes(packet, db));
    }

    [Fact]
    public void CombatMessagesSurviveTheObservationWindowAndDoNotLeakAcrossPlayers()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        var sim = new GameSimulation(db, world, 1, [new CombatMessageProducer()]);
        sim.Tick();
        var captured = new KeeperView(db, world, KeeperWorld.Keeper);
        PlayerMessage message = Assert.Single(captured.Messages);
        Assert.Equal(PlayerMessageKind.CombatStarted, message.Kind);
        Assert.Equal(0u, message.Timeout);
        Assert.Equal(captured.Messages,
            KeeperViewSnapshot.FromBytes(KeeperViewSnapshot.ToBytes(captured), db).Messages);
        sim.Tick();
        Assert.Empty(new KeeperView(db, world, KeeperWorld.Keeper).Messages);
        Assert.Single(captured.Messages);
    }

    private sealed class CombatMessageProducer : ISimulationSystem
    {
        public string Name => "test combat messages";
        public void Execute(SimulationContext context)
        {
            if (context.Tick != GameTick.Zero) return;
            context.World.Messages.Post(context.World, KeeperWorld.Keeper, PlayerMessageKind.CombatStarted, 4, 5);
            context.World.Messages.Post(context.World, 2, PlayerMessageKind.CombatStarted, 6, 7);
        }
    }

    [Fact]
    public void ObservationPacketRoundTripPreservesTheDeterministicCommandStream()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 5000;
        world.Tile(new TilePos(10, 10)).SetTaggedBy(KeeperWorld.Keeper, true);
        var captured = new KeeperView(db, world, KeeperWorld.Keeper);
        byte[] packet = KeeperViewSnapshot.ToBytes(captured);
        KeeperView restored = KeeperViewSnapshot.FromBytes(packet, db);
        Assert.Equal(packet, KeeperViewSnapshot.ToBytes(restored));
        Assert.True(restored.IsTaggedByMe(new TilePos(10, 10)));

        var a = new KeeperAI(KeeperWorld.Keeper, KeeperWorld.Parameters(), new SimRandom(31));
        var b = new KeeperAI(KeeperWorld.Keeper, KeeperWorld.Parameters(), new SimRandom(31));
        Hash64 first = Hash64.Start(), second = Hash64.Start();
        for (int i = 0; i < 12; i++)
        {
            new InputFrame(a.Think(captured)).HashInto(ref first);
            new InputFrame(b.Think(restored)).HashInto(ref second);
        }
        Assert.Equal(first.Value, second.Value);
    }

    [Fact]
    public void CapturedTickKeepsTerrainHoldingsAndAvailabilityAfterWorldChanges()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 900;
        var old = new KeeperView(db, world, KeeperWorld.Keeper);
        var sim = new GameSimulation(db, world, 1, []);
        sim.Tick();
        world.Tile(KeeperWorld.HeartAt).GoldHeld = 17;
        world.SetAvailability(KeeperWorld.Keeper, 5, 4, 1);
        world.Player(KeeperWorld.Keeper).Status = PlayerStatus.Lost;
        var changed = new TilePos(13, 13);
        KeeperWorld.Set(world, db, changed, KeeperWorld.TreasuryFloor, KeeperWorld.Keeper);
        var current = new KeeperView(db, world, KeeperWorld.Keeper);

        Assert.Equal(GameTick.Zero, old.Tick);
        Assert.Equal(GameTick.Zero.Next, current.Tick);
        Assert.Equal(900, old.Gold);
        Assert.Equal(17, current.Gold);
        Assert.Equal(3, old.Availability(5, 4));
        Assert.Equal(1, current.Availability(5, 4));
        Assert.Equal(PlayerStatus.Playing, old.Status);
        Assert.Equal(PlayerStatus.Lost, current.Status);
        Assert.False(old.HasRoom(changed));
        Assert.True(current.HasRoom(changed));
        Assert.Null(old.OwnRoomOfType((byte)RoomTypeId.Treasury));
        Assert.NotNull(current.OwnRoomOfType((byte)RoomTypeId.Treasury));
    }

    [Fact]
    public void DecisionsUseTheSuppliedObservationWithoutChangingTheWorld()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        var captured = new KeeperView(db, world, KeeperWorld.Keeper);
        world.SetAvailability(KeeperWorld.Keeper, 5, 4, 1);
        var sim = new GameSimulation(db, world, 1, []);
        ulong before = sim.StateHash();
        KeeperAIParameters p = KeeperWorld.Parameters(maxImps: 4) with { TilesPerImpRatio = 10 };
        var fromCapture = new KeeperAI(KeeperWorld.Keeper, p, new SimRandom(7));
        var fromCurrent = new KeeperAI(KeeperWorld.Keeper, p, new SimRandom(7));

        Assert.Contains(fromCapture.Think(captured), c => c is CreateImpCommand);
        Assert.DoesNotContain(fromCurrent.Think(db, world), c => c is CreateImpCommand);
        Assert.Equal(before, sim.StateHash());
        Assert.Throws<ArgumentException>(() => fromCapture.Think(new KeeperView(db, world, 2)));
    }

    [Fact]
    public void ReturnedTilesCannotMutateTheObservationOrWrapAcrossRows()
    {
        GameDatabase db = KeeperWorld.Database();
        WorldState world = KeeperWorld.World(db);
        var view = new KeeperView(db, world, KeeperWorld.Keeper);
        KeeperTileView copy = view.Tile(KeeperWorld.HeartAt) with { OwnerPlayerId = 2 };

        Assert.Equal(2, copy.OwnerPlayerId);
        Assert.True(view.IsMine(KeeperWorld.HeartAt));
        Assert.Equal(KeeperWorld.Keeper, world.Tile(KeeperWorld.HeartAt).OwnerPlayerId);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Tile(new TilePos(view.Width, 0)));
    }
}
