// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>States 5, 71, 86, 113 and 126 (refused-states.md, ADR-0400).</summary>
public class RefusedStateTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Goblin = Dk2TerrainFixture.GoblinCreatureId;
    private const byte Imp = Dk2TerrainFixture.ImpCreatureId;

    /// <summary>A 20×20 map of the keeper's claimed path inside its border.</summary>
    private static WorldState Open(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < 19; y++)
        for (int x = 1; x < 19; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = Keeper;
        }

        return world;
    }

    private static void Room(GameDatabase db, WorldState world, RoomTypeId type, int x0, int y0, int size)
    {
        for (int y = y0; y < y0 + size; y++)
        for (int x = x0; x < x0 + size; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), Keeper);
    }

    /// <summary>A tile joins the room at (x, y), which stamps the rebuilt room (<c>0x004E4480</c>).</summary>
    private static void Grow(GameDatabase db, WorldState world, RoomTypeId type, int x, int y)
    {
        RoomRules.Place(db, ref world.Tile(x, y), db.Room(type), Keeper);
        SlabChain.Changed(world, db, (y * world.Width) + x, Dk2TerrainFixture.ClaimedPath, Keeper);
    }

    // ------------------------------------------------------------------------------- 5

    [Fact]
    public void EveryNeutralCreatureInStateOneTakesFive_NotOnlyAFleeingOne()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = Open(db);
        CreatureState neutral = world.SpawnCreature(db, Goblin, PlayerIds.Neutral, new TilePos(9, 9));
        var sim = new GameSimulation(db, world, seed: 7, [new CreatureMovementSystem(), new CreatureJobSystem()]);
        var seen = new HashSet<int>();

        for (int i = 0; i < 20 * SimulationTiming.TicksPerSecond; i++)
        {
            sim.Tick();
            seen.Add(neutral.Dk2StateId);
        }

        Assert.False(neutral.Fleeing);
        Assert.Contains(CreatureJobSystem.NeutralIdleState, seen);
        Assert.DoesNotContain(CreatureJobSystem.IdleChooserState, seen);
        Assert.Contains(6, seen);   // 5 wanders through 87
    }

    [Fact]
    public void Five_UnlikeThree_NeitherWaitsForARunningActionNorIdlesAtTheDeadline()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = Open(db);
        CreatureState neutral = world.SpawnCreature(db, Goblin, PlayerIds.Neutral, new TilePos(5, 5));
        CreatureState keepers = world.SpawnCreature(db, Goblin, Keeper, new TilePos(12, 12));
        StatePlacement.Place(neutral, CreatureJobSystem.NeutralIdleState);
        StatePlacement.Place(keepers, CreatureJobSystem.DoingNothingState);
        neutral.ActionUntilTick = keepers.ActionUntilTick = 1000;   // an action still playing
        neutral.StateDeadlineTick = 0;                              // 3's deadline long past
        keepers.StateDeadlineTick = 1000;
        var sim = new GameSimulation(db, world, seed: 7, [new CreatureJobSystem()]);

        sim.Tick();

        Assert.Equal(ImpStates.Wandering, neutral.Dk2StateId);   // 0x004776B0 jumps straight to the body
        Assert.Equal(CreatureJobSystem.DoingNothingState, keepers.Dk2StateId);
    }

    [Fact]
    public void ABileDemonsIdleActionMakesTheHealInstantShotOneTimeInTen_AGoblinsNever()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = Open(db);
        CreatureState demon = world.SpawnCreature(db, Dk2TerrainFixture.BileDemonCreatureId, PlayerIds.Neutral, new TilePos(5, 5));
        CreatureState goblin = world.SpawnCreature(db, Goblin, PlayerIds.Neutral, new TilePos(14, 14));
        var sink = new ListEventSink();
        // The shots move first, so a shot made by an idle action is still standing after its tick.
        var sim = new GameSimulation(db, world, seed: 7, [new ShotSystem(), new CreatureMovementSystem(), new CreatureJobSystem()]);
        sim.ObserveWith(sink);
        var shots = new HashSet<int>();

        for (int i = 0; i < 600 * SimulationTiming.TicksPerSecond; i++)
        {
            sim.Tick();
            foreach (ShotState shot in world.Shots)
            {
                // Only the shot and its death shot, Modify Health with the parent's words of 0.
                Assert.True(shot.ShotTypeId is Dk2TerrainFixture.HealInstantShotId or Dk2TerrainFixture.ModifyHealthShotId, $"shot type {shot.ShotTypeId}");
                if (shot.ShotTypeId == Dk2TerrainFixture.HealInstantShotId)
                {
                    Assert.Equal(demon.PosX, shot.PosX);
                    shots.Add(shot.Id);
                }
            }
        }

        int actions = sink.Events.Count(e => e.Kind == "idle_action" && e.Detail.Contains($"creature={demon.Id} "));
        Assert.Contains(sink.Events, e => e.Kind == "idle_action" && e.Detail.Contains($"creature={goblin.Id} "));
        Assert.True(actions >= 20, $"{actions} idle actions");
        Assert.InRange(shots.Count, 1, actions / 3);
        Assert.Equal(demon.Health, CreatureHealthRules.MaxHealth(db, db.CreatureById(demon.CreatureTypeId), demon));
    }

    // ------------------------------------------------------------------------------ 113

    [Fact]
    public void ARebuiltTrainingRoomSendsItsTraineeTo113AndBackTo108()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = Open(db);
        Room(db, world, RoomTypeId.TrainingRoom, 3, 3, 3);
        Dk2TerrainFixture.Furnish(db, world, RoomTypeId.TrainingRoom, new TilePos(3, 3), Keeper);   // a place to claim again
        CreatureState trainee = world.SpawnCreature(db, Goblin, Keeper, new TilePos(4, 4));
        trainee.CurrentJob = JobType.Train;
        StatePlacement.As(trainee, CreatureActivity.AtWork);
        trainee.HeldPlace = new RoomPlace((4 * world.Width) + 4, RoomCapacityRules.Units);   // the enter hook's claim
        trainee.JobTileIndex = (4 * world.Width) + 4;
        world.Tile(1, 1).GoldHeld = 100_000;   // training is paid for
        var sim = new GameSimulation(db, world, seed: 7, [new CreatureJobSystem()]);
        sim.Tick();

        Grow(db, world, RoomTypeId.TrainingRoom, 6, 4);
        var seen = new List<(int State, int Resume)>();
        for (int i = 0; i < 8; i++)
        {
            sim.Tick();
            seen.Add((trainee.Dk2StateId, trainee.UtilityNewState));
        }

        // 113 waits a turn, then 108 on its own Training Room; 108 walks to a machine at once, so the
        // tick after 113 shows 108 or a walk that resumes it.
        int returned = seen.FindIndex(s => s.State == TrainingRules.ReturningState);
        Assert.True(returned >= 0, string.Join(" ", seen));
        Assert.Contains(seen.Skip(returned + 1), s => s.State == TrainingRules.WalkToMachineState || s.Resume == TrainingRules.WalkToMachineState);
        Assert.Equal(JobType.Train, trainee.CurrentJob);
    }

    // ------------------------------------------------------------------------------ 126

    [Fact]
    public void ARebuiltPrisonSendsItsCaptiveTo126AndBackIn()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = Open(db);
        Room(db, world, RoomTypeId.Prison, 3, 3, 3);
        CreatureState captive = world.SpawnCreature(db, Goblin, PlayerIds.Good, new TilePos(4, 4));
        Assert.True(PrisonRules.Imprison(new StateHookContext(world, db), captive, Keeper, (4 * world.Width) + 4));
        var sink = new ListEventSink();
        var sim = new GameSimulation(db, world, seed: 7, [new PrisonerWanderSystem()]);
        sim.ObserveWith(sink);
        sim.Tick();

        Grow(db, world, RoomTypeId.Prison, 6, 4);
        var seen = new HashSet<int>();
        for (int i = 0; i < 8; i++)
        {
            sim.Tick();
            seen.Add(captive.Dk2StateId);
        }

        Assert.Contains(PrisonRules.ReturningState, seen);
        Assert.Contains(sink.Events, e => e.Kind == "prisoner_returned");
        Assert.True(PrisonRules.IsImprisoned(captive));
    }

    // ------------------------------------------------------------------------------- 71

    /// <summary>A 5×5 Dungeon Heart of the keeper's at (3..7, 3..7).</summary>
    private static WorldState WithAHeart(GameDatabase db)
    {
        WorldState world = Open(db);
        Room(db, world, RoomTypeId.DungeonHeart, 3, 3, 5);
        return world;
    }

    private static CreatureState GettingUp(GameDatabase db, WorldState world, byte type, TilePos at)
    {
        CreatureState it = world.SpawnCreature(db, type, Keeper, at);
        StatePlacement.Place(it, 257);
        it.ActionUntilTick = 0;
        return it;
    }

    [Fact]
    public void AWorkerGettingUpOnTheHeartsInnerTiles_AnchorStronglyInferred_IsGivenBackForCreateImpsMana()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WithAHeart(db);
        world.Player(Keeper).Mana = 1000;
        CreatureState imp = GettingUp(db, world, Imp, new TilePos(5, 5));
        imp.GoldHeld = 50;
        var sim = new GameSimulation(db, world, seed: 7, [new CreatureStatusSystem()]);

        sim.Tick();

        Assert.Null(world.CreatureById(imp.Id));
        Assert.Equal(1000 + db.RequireKeeperSpell(KeeperSpellName.CreateImp).ManaCost, world.Player(Keeper).Mana);
    }

    [Fact]
    public void AWorkerOnTheHeartsRim_AnchorStronglyInferred_IsSentTo11_ANonWorkerTo188ForItsWages()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WithAHeart(db);
        CreatureState imp = GettingUp(db, world, Imp, new TilePos(3, 3));
        CreatureState goblin = GettingUp(db, world, Goblin, new TilePos(5, 5));
        var sim = new GameSimulation(db, world, seed: 7, [new CreatureStatusSystem()]);

        sim.Tick();

        Assert.Equal(ImpStates.AfterDrop, imp.Dk2StateId);
        Assert.Equal(188, goblin.Dk2StateId);
    }

    // ------------------------------------------------------------------------------- 86

    [Fact]
    public void ATunnellerIn86WalksToOneOfNinePointsOfANeighbourAndWandersOn()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = Open(db);
        CreatureState dwarf = world.SpawnCreature(db, Imp, PlayerIds.Good, new TilePos(9, 9));
        StatePlacement.Place(dwarf, TunnelRules.WanderState);
        // 186's fields as 0x00485B80 leaves them: positions, which 86 reads as a range and a turn.
        dwarf.Tunnel.StandX = dwarf.PosX;
        dwarf.Tunnel.StandY = dwarf.PosY;
        dwarf.Tunnel.DigX = CreatureState.CentreOf(10);
        dwarf.Tunnel.DigY = CreatureState.CentreOf(10);
        var sim = new GameSimulation(db, world, seed: 7, [new CreatureMovementSystem(), new HeroSystem()]);

        var tiles = new HashSet<TilePos>();
        for (int i = 0; i < 30 * SimulationTiming.TicksPerSecond; i++)
        {
            sim.Tick();
            tiles.Add(dwarf.Tile);
        }

        Assert.True(tiles.Count > 1, "it never left its tile");
        Assert.True(dwarf.Dk2StateId is TunnelRules.WanderState or 6, $"state {dwarf.Dk2StateId}");
    }

    [Theory]
    [InlineData(0, false)]   // every neighbour is a tile from (9, 9): out of a range of 0
    [InlineData(1, true)]
    public void A86TakesANeighbourWithinTheDigsXOfTheStandPoint_ReadAsATileIndex_AtOneOfTheNinePoints(int range, bool walks)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = Open(db);
        CreatureState dwarf = world.SpawnCreature(db, Imp, PlayerIds.Good, new TilePos(9, 9));
        StatePlacement.Place(dwarf, TunnelRules.WanderState);
        // The stand point compared as a tile: 9, 9 against the neighbours' indices.
        (dwarf.Tunnel.StandX, dwarf.Tunnel.StandY, dwarf.Tunnel.DigX, dwarf.Tunnel.DigY) = (9, 9, range, 100_000);
        var sim = new GameSimulation(db, world, seed: 7, [new HeroSystem()]);

        sim.Tick();

        if (!walks)
        {
            Assert.NotEqual(TunnelRules.WanderState, dwarf.Dk2StateId);
            Assert.NotEqual(TunnelRules.WanderState, dwarf.UtilityNewState);
            return;
        }
        Assert.Equal(TunnelRules.WanderState, dwarf.UtilityNewState);
        int x = dwarf.Body.PathTargetPos.X >> 4, y = dwarf.Body.PathTargetPos.Y >> 4;
        Assert.Equal(1, Math.Abs((x >> 12) - 9) + Math.Abs((y >> 12) - 9));
        int[] points = [0x2AA, 0x7FF, 0xD54];
        Assert.Contains(x & 0xFFF, points);
        Assert.Contains(y & 0xFFF, points);
    }

    [Fact]
    public void A86WhoseDeadlineTheDigsYHasPassedGoesIdle()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = Open(db);
        CreatureState dwarf = world.SpawnCreature(db, Imp, PlayerIds.Good, new TilePos(9, 9));
        StatePlacement.Place(dwarf, TunnelRules.WanderState);
        (dwarf.Tunnel.StandX, dwarf.Tunnel.StandY, dwarf.Tunnel.DigX, dwarf.Tunnel.DigY) = (dwarf.PosX, dwarf.PosY, CreatureState.CentreOf(10), 0);
        var sim = new GameSimulation(db, world, seed: 7, [new CreatureMovementSystem(), new HeroSystem()]);

        sim.Tick();

        Assert.NotEqual(TunnelRules.WanderState, dwarf.Dk2StateId);
    }
}
