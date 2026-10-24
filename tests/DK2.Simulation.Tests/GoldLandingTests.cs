// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;
using System.Text.Json;

namespace DK2.Simulation.Tests;

public class GoldLandingTests
{
    private static readonly PlayerId Keeper = PlayerIds.FirstKeeper;
    private static readonly TilePos Destination = new(2, 2);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APointerDropMergesIntoAnOffCentreMiningPileAndSurvivesRestoration(bool restore)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Neutral, Keeper.Value]);
        int index = Destination.Y * world.Width + Destination.X;
        ObjectState target = FloorGold.PutDown(world, database, index, 2, 1800)!;
        ObjectState incoming = FloorGold.Drop(world, database, index + 1, 500)!;
        var simulation = new GameSimulation(database, world, 42, [new HandSystem()]);
        simulation.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, incoming.Id)));
        if (restore)
            simulation = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(simulation), database, [new HandSystem()]);
        var point = new FpPosition2(new Fp32(Destination.X * Fp32.One + 0xD54),
            new Fp32(Destination.Y * Fp32.One + 0x2AA));
        var command = new DropCommand(Keeper, Destination, Position: point);
        command = JsonSerializer.Deserialize<DropCommand>(JsonSerializer.Serialize(command))!;

        simulation.Tick(InputFrame.Of(command));

        ObjectState merged = Assert.Single(simulation.World.Objects);
        Assert.Equal(target.Id, merged.Id);
        Assert.Equal(2300, merged.GoldHeld);
    }

    [Fact]
    public void APointOutsideTheCommandTileCannotReleaseTheHeldPile()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Neutral, Keeper.Value]);
        ObjectState pile = FloorGold.Drop(world, database, 0, 500)!;
        var simulation = new GameSimulation(database, world, 42, [new HandSystem()]);
        simulation.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, pile.Id)));
        simulation.Tick(InputFrame.Of(new DropCommand(Keeper, Destination,
            Position: new FpPosition2(new Fp32(0), new Fp32(0)))));
        Assert.True(pile.InHand);
        Assert.Single(world.Player(Keeper).Hand);
    }

    [Fact]
    public void OptionalDropCoordinatesRoundTripAndContributeToTheCommandHash()
    {
        var first = new DropCommand(Keeper, Destination,
            Position: new FpPosition2(new Fp32(9000), new Fp32(9500)));
        var second = first with { Position = new FpPosition2(new Fp32(9500), new Fp32(9000)) };
        var absent = first with { Position = null };
        foreach (DropCommand command in new[] { first, second, absent })
            Assert.Equal(command, JsonSerializer.Deserialize<DropCommand>(JsonSerializer.Serialize(command)));
        var a = Hash64.Start(); first.HashInto(ref a);
        var b = Hash64.Start(); second.HashInto(ref b);
        var c = Hash64.Start(); absent.HashInto(ref c);
        Assert.NotEqual(a.Value, b.Value);
        Assert.NotEqual(a.Value, c.Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LegacyGoldFromTheHandMergesIntoTheFloorPile_StronglyInferredFromBinary(byte kind)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Neutral, Keeper.Value]);
        int destination = Destination.Y * world.Width + Destination.X;
        ObjectState existing = FloorGold.Drop(world, database, destination, 1800)!;
        var simulation = new GameSimulation(database, world, 42, [new HandSystem()]);

        foreach (int amount in new[] { 100, 300, 700 })
        {
            ObjectState incoming = world.SpawnObject(database, kind, PlayerIds.Neutral, destination + 1);
            incoming.GoldHeld = amount;
            Assert.Equal(0, incoming.ProcessState);
            simulation.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, incoming.Id)));
            Assert.True(incoming.InHand);
            simulation.Tick(InputFrame.Of(new DropCommand(Keeper, Destination)));
        }

        Assert.Same(existing, Assert.Single(world.Objects));
        Assert.Equal(2900, existing.GoldHeld);
        Assert.Empty(world.Player(Keeper).Hand);
    }

    [Fact]
    public void AFirstHandDroppedPileReceivesLaterDrops_StronglyInferredFromBinary()
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Neutral, Keeper.Value]);
        int destination = Destination.Y * world.Width + Destination.X;
        ObjectState first = FloorGold.Drop(world, database, destination + 1, 100)!;
        ObjectState second = FloorGold.Drop(world, database, destination + 1, 200)!;
        var simulation = new GameSimulation(database, world, 42, [new HandSystem()]);
        foreach (ObjectState pile in new[] { first, second })
        {
            simulation.Tick(InputFrame.Of(new PickUpObjectCommand(Keeper, pile.Id)));
            simulation.Tick(InputFrame.Of(new DropCommand(Keeper, Destination)));
        }

        Assert.Same(first, Assert.Single(world.Objects));
        Assert.Equal(300, first.GoldHeld);
        Assert.Equal(CreatureState.CentreOf(Destination.X), first.PosX);
        Assert.Equal(CreatureState.CentreOf(Destination.Y), first.PosY);
    }
}
