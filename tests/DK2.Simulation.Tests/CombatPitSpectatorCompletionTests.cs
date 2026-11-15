// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;
namespace DK2.Simulation.Tests;
public sealed class CombatPitSpectatorCompletionTests
{
    private sealed class JobProbe(CreatureState watcher, Action<bool> result) : ISimulationSystem
    {
        public string Name => "SpectatorJobProbe";
        public void Execute(SimulationContext context) => result(CombatPitSpectatorRules.TakeTheJob(context, Name, watcher));
    }

    private sealed class SpectatorStep : ICreatureUpdateStep
    {
        public string Name => "SpectatorProbe";
        public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);
        public void Update(SimulationContext context, CreatureState creature)
        {
            if (CombatPitSpectatorRules.IsSpectatorState(creature.Dk2StateId)
                && !CombatPitSpectatorRules.Turn(context, Name, creature))
                CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle);
        }
    }

    private static (GameSimulation Sim, CreatureState Creature, GameDatabase Db, Dk2Lcg Rng, RoomInstance Room) Start(
        uint seed = 3, bool artPresent = true, bool irregular = false)
    {
        var art = new CreatureAnimationTiming[CreatureDef.ArtSlotCount];
        if (artPresent)
            art[8] = art[20] = new CreatureAnimationTiming { ResourceType = 6, FrameCount = 100, FramesPerSecond = 20 };
        CreatureDef watcher = Dk2TerrainFixture.Goblin() with { CreatureId = 54, ArtSlots = art };
        GameDatabase db = Dk2TerrainFixture.Database(watcher);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 2; y <= 6; y++)
        for (int x = 4; x <= 8; x++)
            if (!irregular || x < 6 || y > 4)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.CombatPit), PlayerIds.FirstKeeper);
        RoomInstance room = RoomIndex.RoomAt(world, db, (4 * world.Width) + 4)!;
        TilePos at = new(4, irregular ? 3 : 4);
        CreatureState c = world.SpawnCreature(db, watcher.CreatureId, PlayerIds.FirstKeeper, at);
        StatePlacement.Place(c, CombatPitSpectatorRules.FacingState);
        c.HeldPlace = new RoomPlace((at.Y * world.Width) + at.X, 1);
        c.PitStateEnteredTick = -1;
        c.Heading = global::DK2.Simulation.Steering.Bearing.Of(CreatureState.CentreOf(room.CentreFieldX) - c.PosX,
            CreatureState.CentreOf(room.CentreFieldY) - c.PosY);
        var rng = new Dk2Lcg(seed);
        var sim = new GameSimulation(db, world, new SimRandom(rng), [new SpectatorStep()]);
        rng.Reseed(seed);
        return (sim, c, db, rng, room);
    }

    [Fact]
    public void MissingResolvedCheerArtConsumesNoChoiceDrawAndStartsNoAction()
    {
        var (sim, c, db, rng, _) = Start(artPresent: false);
        sim.Tick();
        Assert.Equal(144, c.Dk2StateId);
        Assert.Equal(3U, rng.Seed);
        Assert.Equal(0, c.ActionUntilTick);
        Assert.Equal(0, CreatureCurrentAction.IndexOf(c, db.CreatureById(c.CreatureTypeId), sim.CurrentTick));
    }

    [Theory]
    [InlineData(3U, 70, 2, 20)]
    [InlineData(1U, 69, 2, 8)]
    [InlineData(2U, 69, 3, 8)]
    public void CheerStartsItsNativeCountedAction(uint seed, int action, byte storedCount, byte artSlot)
    {
        var (sim, c, db, _, _) = Start(seed);
        sim.Tick();
        Assert.Equal(144, c.Dk2StateId);
        Assert.Equal(action, CreatureCurrentAction.IndexOf(c, db.CreatureById(c.CreatureTypeId), sim.CurrentTick));
        Assert.Equal(storedCount, c.ActionCount);
        Assert.Equal(artSlot, c.ActionArtSlot);
        Assert.Equal(0, c.ActionUntilTick);
    }

    [Fact]
    public void CheerWaitsForTheAuthoredClipBeyondEightTicks()
    {
        var (sim, c, db, _, _) = Start();
        for (int i = 0; i < 11; i++) sim.Tick();
        Assert.Equal(144, c.Dk2StateId);
        Assert.Equal(70, CreatureCurrentAction.IndexOf(c, db.CreatureById(c.CreatureTypeId), sim.CurrentTick));
    }

    [Fact]
    public void FacingUsesTheNativeRoomCentreForAnIrregularPit()
    {
        var (sim, c, _, _, _) = Start(irregular: true);
        int heading = c.Heading;
        sim.Tick();
        Assert.Equal(heading, c.Heading);
    }

    [Fact]
    public void QuietAgeUsesTheNativeUnsignedTurnDifference()
    {
        var (sim, c, _, _, room) = Start(artPresent: false);
        StatePlacement.Place(c, CombatPitSpectatorRules.CheeringState);
        sim.World.TileAtIndex(room.TileIndices[0]).PitFightSeenTick = -((1L << 32) + 16);
        sim.Tick();
        Assert.Equal(CombatPitSpectatorRules.ChoosingState, c.Dk2StateId);
    }

    [Fact]
    public void JobRefusesAtTheConfiguredQuietBoundary()
    {
        var (basis, c, db, _, room) = Start(artPresent: false);
        db.Variables.Misc[VariableTypeId.TicksPerSecond] = new MiscVariable(VariableTypeId.TicksPerSecond, 1, 0, 0);
        StatePlacement.Place(c, CreatureStateMachine.Idle);
        basis.World.TileAtIndex(room.TileIndices[0]).PitFightSeenTick = -4;
        bool? accepted = null;
        var sim = new GameSimulation(db, basis.World, seed: 1, [new JobProbe(c, value => accepted = value)]);
        sim.Tick();
        Assert.False(accepted);
    }

    [Fact]
    public void FinishedCheerLeavesAfterTheConfiguredQuietLimit()
    {
        var (sim, c, db, _, room) = Start(artPresent: false);
        db.Variables.Misc[VariableTypeId.TicksPerSecond] = new MiscVariable(VariableTypeId.TicksPerSecond, 1, 0, 0);
        StatePlacement.Place(c, CombatPitSpectatorRules.CheeringState);
        sim.World.TileAtIndex(room.TileIndices[0]).PitFightSeenTick = -5;
        sim.Tick();
        Assert.Equal(CreatureStateMachine.Idle, c.Dk2StateId);
    }

    [Fact]
    public void FinderProbesTheFirstSlabChainPointEvenWhenAnotherRimTileIsReachable()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [0, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < world.Width; x++)
            world.Tile(x, y).TerrainId = Dk2TerrainFixture.ImpenetrableRock;
        TilePos[] order = [new(4, 6), new(5, 6), new(6, 6), new(4, 5), new(5, 5), new(6, 5),
            new(4, 4), new(5, 4), new(6, 4), new(4, 3), new(4, 2)];
        foreach (TilePos tile in order)
        {
            byte terrain = world.Tile(tile).TerrainId;
            byte owner = world.Tile(tile).OwnerPlayerId;
            RoomRules.Place(db, ref world.Tile(tile), db.Room(RoomTypeId.CombatPit), PlayerIds.FirstKeeper);
            SlabChain.Changed(world, db, world.TileIndex(tile), terrain, owner);
        }
        world.Tile(4, 1).TerrainId = Dk2TerrainFixture.ClaimedPath;
        world.Tile(4, 1).OwnerPlayerId = PlayerIds.FirstKeeper;
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
        { X = 4, Y = 3, PlayerId = PlayerIds.FirstKeeper, Flags = 0, DoorId = 1, Unknown_0011 = new byte[3] }]);
        world.Doors[0].LockMode = 1;
        CreatureState fighter = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new TilePos(5, 5));
        StatePlacement.Place(fighter, 199);
        CreatureState watcher = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new TilePos(4, 1));
        RoomInstance room = RoomIndex.RoomAt(world, db, world.TileIndex(new TilePos(4, 6)))!;
        int[] chain = SlabChain.Order(world, room);
        Assert.Equal(world.TileIndex(new TilePos(4, 6)), chain[0]);
        int reachableIndex = Array.IndexOf(chain, world.TileIndex(new TilePos(4, 2)));
        Assert.True(reachableIndex >= 0);
        uint seed = 1;
        while (new Dk2Lcg(seed).NextInt(chain.Length) != reachableIndex) seed++;
        var rng = new Dk2Lcg(seed);
        bool? accepted = null;
        var sim = new GameSimulation(db, world, new SimRandom(rng), [new JobProbe(watcher, value => accepted = value)]);
        Assert.False(sim.Navigation.TryRoute(watcher, new TilePos(4, 6), Navigation.MoveOptions.Loose));
        Assert.True(sim.Navigation.TryRoute(watcher, new TilePos(4, 2), Navigation.MoveOptions.Loose));
        rng.Reseed(seed);
        sim.Tick();
        Assert.False(accepted);
    }
}
