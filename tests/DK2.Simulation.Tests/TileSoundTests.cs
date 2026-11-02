// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The sounds of tile work. <c>Tile_ApplyWork</c> (<c>0x00452F60</c>) requests, with handle 0 in
/// the category of the terrain the tile had, <c>0xF4</c> when health reaches zero and the tile becomes a
/// different terrain (<c>0x0045327C</c>) and <c>0x10C</c> when health reaches the maximum and the tile
/// converts to its max-health type (<c>0x00453359</c>), both at the tile's centre, height 0. The imp's claim
/// action (<c>0x004974C0</c>) then requests <c>0x10A</c> in its own category with its own voice.</summary>
public sealed class TileSoundTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte ImpId = Dk2TerrainFixture.ImpCreatureId;
    private static readonly TilePos At = new(5, 5);

    private static (GameDatabase Db, WorldState World, GameSimulation Sim, ListEventSink Events) Setup(byte terrain,
        int health, byte owner = PlayerIds.Neutral)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        ref TileState tile = ref world.Tile(At);
        tile.TerrainId = terrain;
        tile.OwnerPlayerId = owner;
        tile.Health = health;
        var sim = new GameSimulation(db, world, seed: 42, [new TileWorkSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (db, world, sim, events);
    }

    private static ThingSoundRequest[] Sounds(ListEventSink events) =>
        events.Events.Where(e => e.ThingSound.HasValue).Select(e => e.ThingSound!.Value).ToArray();

    private static FpPosition3 Centre(TilePos tile) =>
        new(new Fp32(CreatureState.CentreOf(tile.X)), new Fp32(CreatureState.CentreOf(tile.Y)), new Fp32(0));

    private static void Work(WorldState world, TileWorkKind kind, int worker = -1) =>
        world.QueueWork(new TileWorkOrder((At.Y * world.Width) + At.X, kind, Keeper, worker));

    private static void AssertTileSound(ThingSoundRequest sound, WorldState world, string category, ThingSoundEvent expected)
    {
        Assert.Equal(expected, sound.Event);
        Assert.Equal(category, sound.Category);
        Assert.Equal(new SoundSourceId(SoundSourceKind.Terrain, (At.Y * world.Width) + At.X), sound.Source);
        Assert.Equal(Centre(At), sound.Position);
        Assert.False(sound.Voiced);   // handle 0
    }

    [Fact]
    public void DiggingRockOutRequestsF4InTerrainRockAtTheTileCentreWithoutAVoice()
    {
        var (db, world, sim, events) = Setup(Dk2TerrainFixture.Rock, 1);
        Work(world, TileWorkKind.Dig);
        sim.Tick(InputFrame.Empty);

        Assert.Equal(Dk2TerrainFixture.DirtPath, world.Tile(At).TerrainId);
        AssertTileSound(Assert.Single(Sounds(events)), world, "TERRAIN_ROCK", ThingSoundEvent.TileBroken);
        Assert.Equal((ushort)0xF4, (ushort)ThingSoundEvent.TileBroken);
    }

    [Fact]
    public void WorkThatLeavesTheTileItsTerrainOrDoesNotApplyIsSilent()
    {
        var (db, world, sim, events) = Setup(Dk2TerrainFixture.Rock, 5000);
        Work(world, TileWorkKind.Dig);                // health falls, rock stays: no event
        Work(world, TileWorkKind.Claim);              // rejected: rock is solid
        sim.Tick(InputFrame.Empty);

        Assert.Equal(Dk2TerrainFixture.Rock, world.Tile(At).TerrainId);
        Assert.Empty(Sounds(events));
    }

    [Fact]
    public void ReinforcingRockToItsMaximumRequests10CInTerrainRockAtTheTileCentre()
    {
        var (db, world, sim, events) = Setup(Dk2TerrainFixture.Rock, 659);
        Work(world, TileWorkKind.Reinforce);
        sim.Tick(InputFrame.Empty);

        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, world.Tile(At).TerrainId);
        AssertTileSound(Assert.Single(Sounds(events)), world, "TERRAIN_ROCK", ThingSoundEvent.TileConverted);
        Assert.Equal((ushort)0x10C, (ushort)ThingSoundEvent.TileConverted);
    }

    [Fact]
    public void ReinforcingShortOfTheMaximumIsSilent()
    {
        var (db, world, sim, events) = Setup(Dk2TerrainFixture.Rock, 165);
        Work(world, TileWorkKind.Reinforce);
        sim.Tick(InputFrame.Empty);

        Assert.Equal(Dk2TerrainFixture.Rock, world.Tile(At).TerrainId);
        Assert.Empty(Sounds(events));
    }

    [Fact]
    public void AClaimThatConvertsTheTileSoundsTheTerrainsConversionThenTheImps10AWithItsOwnVoice()
    {
        var (db, world, sim, events) = Setup(Dk2TerrainFixture.DirtPath, 659);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(4, 5));
        Work(world, TileWorkKind.Claim, imp.Id);
        sim.Tick(InputFrame.Empty);

        Assert.Equal(Dk2TerrainFixture.ClaimedPath, world.Tile(At).TerrainId);
        ThingSoundRequest[] heard = Sounds(events);
        Assert.Equal(2, heard.Length);
        AssertTileSound(heard[0], world, "TERRAIN_DIRT_PATH", ThingSoundEvent.TileConverted);

        Assert.Equal(ThingSoundEvent.ImpClaimedFloor, heard[1].Event);
        Assert.Equal((ushort)0x10A, (ushort)heard[1].Event);
        Assert.Equal("CREATURE_IMP", heard[1].Category);
        Assert.Equal(SoundSourceId.Of(new CreatureId(imp.Id)), heard[1].Source);
        Assert.Equal(new FpPosition3(new Fp32(imp.PosX), new Fp32(imp.PosY), new Fp32(imp.PosZ)), heard[1].Position);
        Assert.True(heard[1].Voiced);
    }

    [Fact]
    public void AClaimStillInProgressOrWithNoImpBehindItMakesNoImpSound()
    {
        var (db, world, sim, events) = Setup(Dk2TerrainFixture.DirtPath, 1);
        CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(4, 5));
        Work(world, TileWorkKind.Claim, imp.Id);
        sim.Tick(InputFrame.Empty);
        Assert.Equal(Dk2TerrainFixture.DirtPath, world.Tile(At).TerrainId);
        Assert.Empty(Sounds(events));

        world.Tile(At).Health = 659;
        Work(world, TileWorkKind.Claim);   // a test's order, no worker
        sim.Tick(InputFrame.Empty);
        ThingSoundRequest only = Assert.Single(Sounds(events));
        Assert.Equal(ThingSoundEvent.TileConverted, only.Event);
    }

    [Fact]
    public void ARoomTileWorkedDownRequestsF4InItsRoomTerrainsCategory()
    {
        var (db, world, sim, events) = Setup(Dk2TerrainFixture.TreasuryTerrain, 1, Keeper);
        Work(world, TileWorkKind.AttackRoom);
        sim.Tick(InputFrame.Empty);

        Assert.NotEqual(Dk2TerrainFixture.TreasuryTerrain, world.Tile(At).TerrainId);
        ThingSoundRequest broken = Sounds(events).Single(s => s.Event == ThingSoundEvent.TileBroken);
        Assert.Equal("ROOM_TREASURE", broken.Category);
    }

    [Fact]
    public void ACornerFillRequests10CInTerrainRockAtTheFilledTile()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)2, (byte)3, (byte)4]);
        void Set(int x, int y, byte terrain, byte owner, int health)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = terrain; tile.OwnerPlayerId = owner; tile.Health = health;
        }
        Set(5, 5, Dk2TerrainFixture.ReinforcedWall, 3, 900);
        Set(6, 6, Dk2TerrainFixture.ReinforcedWall, 4, 900);
        Set(5, 6, Dk2TerrainFixture.Rock, 2, 165);
        Set(6, 5, Dk2TerrainFixture.DirtPath, 2, 1);
        var sim = new GameSimulation(db, world, 1, [new FillSystem()]);
        var events = new ListEventSink();
        sim.ObserveWith(events);
        sim.Tick(InputFrame.Empty);

        Assert.Equal(Dk2TerrainFixture.ReinforcedWall, world.Tile(5, 6).TerrainId);
        ThingSoundRequest sound = Assert.Single(Sounds(events));
        Assert.Equal(ThingSoundEvent.TileConverted, sound.Event);
        Assert.Equal("TERRAIN_ROCK", sound.Category);
        Assert.Equal(Centre(new TilePos(5, 6)), sound.Position);
    }

    private sealed class FillSystem : ISimulationSystem
    {
        public string Name => "Corner test";
        public void Execute(SimulationContext context) => ReinforcementRules.FillCorners(context, new TilePos(5, 5), 3);
    }

    /// <summary>A sink that hears a sound does not change what the simulation does: the run with a sink and
    /// the run without end in one state hash. It shows the sink does not mutate state, not that a sound
    /// could not.</summary>
    [Fact]
    public void AttachingASoundSinkDoesNotChangeTheStateHashOfADigAndClaimRun()
    {
        static GameSimulation Run(ListEventSink? sink)
        {
            GameDatabase db = Dk2TerrainFixture.Database();
            WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
            world.Tile(At).TerrainId = Dk2TerrainFixture.DirtPath;
            world.Tile(At).Health = 659;
            world.Tile(4, 5).TerrainId = Dk2TerrainFixture.Rock;
            world.Tile(4, 5).Health = 1;
            CreatureState imp = world.SpawnCreature(db, ImpId, Keeper, new TilePos(6, 5));
            var sim = new GameSimulation(db, world, seed: 42, [new TileWorkSystem()]);
            if (sink is not null) sim.ObserveWith(sink);
            world.QueueWork(new TileWorkOrder((At.Y * world.Width) + At.X, TileWorkKind.Claim, Keeper, imp.Id));
            world.QueueWork(new TileWorkOrder((5 * world.Width) + 4, TileWorkKind.Dig, Keeper, imp.Id));
            sim.Tick(InputFrame.Empty);
            return sim;
        }
        var events = new ListEventSink();
        GameSimulation heard = Run(events);
        Assert.Equal(3, Sounds(events).Length);
        Assert.Equal(Run(null).StateHash(), heard.StateHash());
    }
}
