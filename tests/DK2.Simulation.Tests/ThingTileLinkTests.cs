// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The mixed-class tile chain order (<see cref="ThingTileLink"/>): a thing that changes
/// tile goes to the head of its new tile's chain, whatever moved it; a thing in a hand or carried
/// is off the chain (keeper-spell-possession.md, Tile chain order).</summary>
public sealed class ThingTileLinkTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    private static (GameDatabase Db, WorldState World) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20),
            [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    [Fact]
    public void ATileChangeLinksAtTheHeadAndAMoveWithinTheTileKeepsThePlace_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState c = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        ObjectState pile = world.SpawnObject(db, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, (5 * world.Width) + 6);
        Assert.Equal((5 * world.Width) + 5, c.TileLink.TileIndex);
        Assert.True(pile.TileLink.Order > c.TileLink.Order);
        ThingTileLink first = c.TileLink;
        c.PosX += 1000;
        Assert.Equal(first, c.TileLink);
        c.PosX += 4096;
        Assert.Equal((5 * world.Width) + 6, c.TileLink.TileIndex);
        Assert.True(c.TileLink.Order > pile.TileLink.Order);
    }

    [Fact]
    public void AHandOrACarrierTakesAThingOffTheChainAndPuttingItDownLinksItAtTheHead_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState c = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        ObjectState pile = world.SpawnObject(db, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, (5 * world.Width) + 5);
        int before = c.TileLink.Order;
        c.InHand = true;
        Assert.Equal(ThingTileLink.None, c.TileLink);
        c.InHand = false;
        Assert.True(c.TileLink.Order > pile.TileLink.Order && c.TileLink.Order > before);
        pile.CarriedByCreatureId = c.Id;
        Assert.Equal(ThingTileLink.None, pile.TileLink);
        pile.CarriedByCreatureId = -1;
        Assert.True(pile.TileLink.Order > c.TileLink.Order);
        Assert.True(world.RemoveObject(pile.Id));
        Assert.Equal(ThingTileLink.None, pile.TileLink);
    }

    [Fact]
    public void ShotsTrapsAndDoorsAreLinkedWhenMade_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        ShotState shot = world.SpawnShot(Dk2TerrainFixture.FireballShotId, Keeper, CreatureState.CentreOf(7), CreatureState.CentreOf(7));
        Assert.Equal((7 * world.Width) + 7, shot.TileLink.TileIndex);
        TrapState trap = PlaceTrap(db, world, new TilePos(9, 7));
        Assert.Equal((7 * world.Width) + 9, trap.TileLink.TileIndex);
        Assert.True(trap.TileLink.Order > shot.TileLink.Order);
        DoorState door = PlaceDoor(db, world, new TilePos(11, 7));
        Assert.Equal((7 * world.Width) + 11, door.TileLink.TileIndex);
        Assert.True(door.TileLink.Order > trap.TileLink.Order);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AShotRelinksWhenEitherAxisCrossesATile_StronglyInferredFromBinary(bool alongX)
    {
        var (db, world) = Open();
        ShotState shot = world.SpawnShot(Dk2TerrainFixture.FireballShotId, Keeper, CreatureState.CentreOf(7), CreatureState.CentreOf(7));
        ObjectState pile = world.SpawnObject(db, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, (7 * world.Width) + 7);
        if (alongX) shot.PosX += 4096;
        else shot.PosY += 4096;
        Assert.Equal(alongX ? (7 * world.Width) + 8 : (8 * world.Width) + 7, shot.TileLink.TileIndex);
        Assert.True(shot.TileLink.Order > pile.TileLink.Order);
    }

    [Fact]
    public void AnObjectRelinksWhenItsTileOrItsHandChanges_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        ObjectState pile = world.SpawnObject(db, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, (5 * world.Width) + 5);
        CreatureState c = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 6));
        pile.TileIndex = (6 * world.Width) + 5;
        Assert.Equal((6 * world.Width) + 5, pile.TileLink.TileIndex);
        Assert.True(pile.TileLink.Order > c.TileLink.Order);
        int before = pile.TileLink.Order;
        pile.InHand = true;
        Assert.Equal(ThingTileLink.None, pile.TileLink);
        pile.InHand = false;
        Assert.True(pile.TileLink.Order > before);
    }

    [Fact]
    public void ACreatureThatDiesIntoABodyIsLinkedAfreshAtTheHead_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(5, 5));
        ObjectState pile = world.SpawnObject(db, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, (5 * world.Width) + 5);
        Assert.True(pile.TileLink.Order > goblin.TileLink.Order);
        goblin.Health = 0;
        Assert.True(CreatureDeathRules.Collapse(new StateHookContext(world, db), goblin));
        goblin.DyingTicks = CreatureDeathRules.DyingWindowTicks(db);
        new GameSimulation(db, world, 42, [new CreatureHealthSystem()]).Run(SimulationTiming.TicksPerSecond);
        Assert.True(CreatureDeathRules.IsCorpse(goblin));
        Assert.Equal((5 * world.Width) + 5, goblin.TileLink.TileIndex);
        Assert.True(goblin.TileLink.Order > pile.TileLink.Order);
    }

    private static TrapState PlaceTrap(GameDatabase db, WorldState world, TilePos at)
    {
        ref TileState tile = ref world.Tile(at);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = Rival;
        ThingProjection.Place(world, db, [new TrapThing(new byte[16]) { X = at.X, Y = at.Y, TrapId = Dk2TerrainFixture.SentryTrapId, PlayerId = Rival, NumberOfShots = 1 }]);
        return world.Traps[^1];
    }

    private static DoorState PlaceDoor(GameDatabase db, WorldState world, TilePos at)
    {
        ref TileState tile = ref world.Tile(at);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = Rival;
        byte doorId = db.Doors.Values.First(d => (d.Flags & DoorFlags.Barricade) == 0).DoorId;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = at.X, Y = at.Y, PlayerId = Rival, DoorId = doorId, Unknown_0011 = new byte[3] }]);
        return world.Doors[^1];
    }

    [Fact]
    public void OrdinaryWalkingRelinksAtEveryTileChange_NotOnlyPossession_StronglyInferredFromBinary()
    {
        var (db, world) = Open();
        CreatureState walker = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(3, 10));
        var navigation = new WorldSteering(db, world);
        navigation.Refresh();
        Assert.True(navigation.TryRouteTo(walker, CreatureState.CentreOf(15), CreatureState.CentreOf(10), MoveOptions.Plain));
        var sim = new GameSimulation(db, world, 1, [new CreatureMovementSystem()]);
        int changes = 0;
        for (int i = 0; i < 400 && walker.Tile.X < 15; i++)
        {
            ThingTileLink before = walker.TileLink;
            sim.Tick();
            Assert.Equal((walker.PosY >> 12) * world.Width + (walker.PosX >> 12), walker.TileLink.TileIndex);
            if (walker.TileLink.TileIndex != before.TileIndex)
            {
                Assert.True(walker.TileLink.Order > before.Order);
                changes++;
            }
        }
        Assert.True(changes >= 10, $"the walk crossed {changes} tiles");
    }

    [Fact]
    public void EveryThingsLinkMatchesWhereItIsAfterEveryTickOfTheFullSystemList()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, Rival, PlayerIds.Neutral, Keeper]);
        ref TileState graveyard = ref world.Tile(1, 1);
        RoomRules.Place(db, ref graveyard, db.Room(RoomTypeId.Graveyard), Keeper);
        CreatureState corpse = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(3, 3));
        CreatureDeathRules.BecomeCorpse(world, db, corpse);
        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3, 3));
        world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(4, 4));
        world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, Rival, new TilePos(6, 4));
        world.SpawnObject(db, FloorGold.FloorGoldObjectId, PlayerIds.Neutral, (4 * world.Width) + 5).GoldHeld = 300;
        var sim = new GameSimulation(db, world, 42, SimulationSystems.Default());
        int relinks = 0, last = 0;
        for (int i = 0; i < 1200; i++)
        {
            sim.Tick();
            AssertCurrent(world);
            int top = MaxOrder(world);
            if (top != last) relinks++;
            last = top;
        }
        Assert.True(relinks > 20, $"only {relinks} ticks moved anything");
    }

    private static int MaxOrder(WorldState world) =>
        world.Creatures.Select(c => c.TileLink.Order).Concat(world.Objects.Select(o => o.TileLink.Order))
            .Concat(world.Shots.Select(s => s.TileLink.Order)).DefaultIfEmpty(0).Max();

    private static void AssertCurrent(WorldState world)
    {
        int Tile(int x, int y) => world.Contains(x >> 12, y >> 12) ? ((y >> 12) * world.Width) + (x >> 12) : -1;
        foreach (CreatureState c in world.Creatures)
            Assert.Equal(c.InHand || c.CarriedByCreatureId >= 0 ? -1 : Tile(c.PosX, c.PosY), c.TileLink.TileIndex);
        foreach (ObjectState o in world.Objects)
            Assert.Equal(o.InHand || o.CarriedByCreatureId >= 0 ? -1 : o.TileIndex, o.TileLink.TileIndex);
        foreach (ShotState s in world.Shots)
            Assert.Equal(Tile(s.PosX, s.PosY), s.TileLink.TileIndex);
        foreach (TrapState t in world.Traps)
            Assert.Equal((t.Y * world.Width) + t.X, t.TileLink.TileIndex);
        foreach (DoorState d in world.Doors)
            Assert.Equal((d.Y * world.Width) + d.X, d.TileLink.TileIndex);
    }
}
