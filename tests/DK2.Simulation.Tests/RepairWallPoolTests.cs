// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// The kind-16 (wall repair) pool entry and its side holders (q811). DK2's leave hook
/// <c>0x004647D0</c> has no case for states 23 and 24 and state 24's removal tests the entry's type
/// against 6 (<c>0x0046680E</c>), so a repairer keeps its side of the wall after it fled or finished;
/// a later repairer reads that slot (<c>0x00508110</c>), steals it only when strictly nearer the
/// wall's centre than the holder and otherwise takes the next side. Built as DK2 does (ADR-0783).
/// </summary>
public class RepairWallPoolTests
{
    private const byte Keeper = 3;
    private const int North = 0, East = 1;
    private static readonly TilePos Wall = new(3, 3);

    private static GameSimulation Start(out ListEventSink sink, TilePos firstImp)
    {
        var db = Dk2TerrainFixture.Database();
        var tiles = new MapTile[49];
        for (int y = 0; y < 7; y++)
        for (int x = 0; x < 7; x++)
            tiles[(y * 7) + x] = x is >= 1 and <= 5 && y is >= 1 and <= 5
                ? new MapTile(Dk2TerrainFixture.ClaimedPath, Keeper, 0, 0)
                : new MapTile(Dk2TerrainFixture.ImpenetrableRock, 2, 0, 0);
        var world = WorldState.FromMap(db, new LevelMap(7, 7, tiles), [(byte)2, Keeper, (byte)4]);
        ref TileState wall = ref world.Tile(Wall);
        wall.TerrainId = Dk2TerrainFixture.ReinforcedWall;
        wall.OwnerPlayerId = Keeper;
        wall.Health = 500; // above half, below full: repair applies, reinforce (kind 6) does not
        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, firstImp);
        var sim = new GameSimulation(db, world, 42, Dk2TerrainFixture.DefaultSystems());
        sink = new ListEventSink();
        sim.ObserveWith(sink);
        return sim;
    }

    private static CreatureState Spawn(GameSimulation sim, TilePos at)
    {
        sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.ImpCreatureId, Keeper, at);
        return sim.World.Creatures[^1];
    }

    private static void TickUntil(GameSimulation sim, Func<bool> done, int limit = 300)
    {
        for (int i = 0; i < limit && !done(); i++) sim.Tick();
        Assert.True(done(), "the condition was not reached");
    }

    private static int Holder(GameSimulation sim, int side) =>
        sim.World.RepairTaskEntries.TryGetValue((Keeper, Wall), out RepairTaskEntry? entry) ? entry.Holders[side] : 0;

    private static bool Repairs(CreatureState imp) =>
        imp.TargetWork == TileWorkKind.RepairWall && imp.TargetTileIndex == (Wall.Y * 7) + Wall.X;

    [Fact]
    public void ARepairerThatFledStillHoldsItsSide_SoAFartherRepairerTakesTheNextOne()
    {
        // Imp A takes the north side, then flees (state 203; nothing in the pool changes: 0x004647D0
        // has no case for 23 and 24). Imp B starts north too (north first, step +1) and finds the side
        // still held by A (the slot outlives A's job) and may not steal it, so it takes east.
        GameSimulation sim = Start(out ListEventSink sink, new TilePos(3, 2));
        CreatureState a = sim.World.Creatures[0];
        TickUntil(sim, () => Repairs(a));
        Assert.Equal(a.Id, Holder(sim, North));

        a.ClearJob();
        a.Dk2StateId = 203; // combat fear (Fleeing): gives up no pool slot and refuses state 10
        CreatureState b = Spawn(sim, new TilePos(3, 1));
        TickUntil(sim, () => Repairs(b));

        Assert.Equal(a.Id, Holder(sim, North));
        Assert.Equal(b.Id, Holder(sim, East));
        Assert.Equal(new TilePos(4, 3), b.IsRouted ? b.Destination : b.Tile);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "task_stolen");
    }

    /// <summary>The distance rule alone (0x0046B426-0x0046B4B4): A still repairs the north side and may
    /// be sent to 10, B comes from farther off, is not strictly nearer the wall's centre and takes east.</summary>
    [Fact]
    public void AFartherRepairerDoesNotStealAHeldSide_BinaryInferred()
    {
        GameSimulation sim = Start(out ListEventSink sink, new TilePos(3, 2));
        CreatureState a = sim.World.Creatures[0];
        TickUntil(sim, () => Repairs(a));
        CreatureState b = Spawn(sim, new TilePos(3, 1));
        TickUntil(sim, () => Repairs(b));

        Assert.Equal(a.Id, Holder(sim, North));
        Assert.Equal(b.Id, Holder(sim, East));
        Assert.DoesNotContain(sink.Events, e => e.Kind == "task_stolen");
    }

    [Fact]
    public void ANearerRepairerStealsTheStaleSideAndSendsItsHolderToState10()
    {
        // Imp A (two tiles north of the wall) takes the north side and drifts off.
        // Imp B stands on the north side tile, nearer the wall's centre: it takes the slot and A
        // goes to 10 wherever it is, as for any steal (0x0046B576 -> 0x0048AD30(10)).
        GameSimulation sim = Start(out ListEventSink sink, new TilePos(3, 1));
        CreatureState a = sim.World.Creatures[0];
        TickUntil(sim, () => Repairs(a));
        Assert.Equal(a.Id, Holder(sim, North));
        a.ClearJob();
        a.Dk2StateId = ImpStates.Resting;

        CreatureState b = Spawn(sim, new TilePos(3, 2));
        TickUntil(sim, () => Repairs(b));

        Assert.Equal(b.Id, Holder(sim, North));
        SimEvent stolen = Assert.Single(sink.Events, e => e.Kind == "task_stolen");
        Assert.Contains($"imp={b.Id} from={a.Id}", stolen.Detail);
    }

    [Fact]
    public void TheSideSlotGoesWithTheEntry_ADigTagRemovesIt()
    {
        // The pool adder raising kind 1 removes the tile's kind-16 entry and with it every holder
        // (0x00507BB0 at 0x00507D08-0x00507D6C).
        GameSimulation sim = Start(out _, new TilePos(3, 2));
        CreatureState a = sim.World.Creatures[0];
        TickUntil(sim, () => Repairs(a));
        Assert.Equal(a.Id, Holder(sim, North));

        sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, Wall)));

        Assert.True(sim.World.Tile(Wall).IsTaggedBy(Keeper), "the wall was not tagged");
        Assert.Empty(sim.World.RepairTaskEntries);
    }

    [Fact]
    public void AFinishedRepairLeavesItsHolderInTheEntry()
    {
        // State 24 removes the entry only when its type is 6 (0x0046680E), and a repair entry's is 16,
        // so the wall's entry and the repairer's slot outlive the repair.
        GameSimulation sim = Start(out _, new TilePos(3, 2));
        CreatureState a = sim.World.Creatures[0];
        TickUntil(sim, () => Repairs(a));
        TickUntil(sim, () => sim.World.Tile(Wall).Health >= 900, 2000);
        sim.Run(20);

        Assert.Equal(a.Id, Holder(sim, North));
    }

    [Fact]
    public void TheEntryRoundTripsThroughASnapshotAndChangesTheHash()
    {
        GameSimulation sim = Start(out _, new TilePos(3, 2));
        CreatureState a = sim.World.Creatures[0];
        TickUntil(sim, () => Repairs(a));
        Assert.Equal(a.Id, Holder(sim, North));

        ulong before = sim.StateHash();
        sim.World.RepairTaskEntries[(Keeper, Wall)].Holders[2] = 17;
        Assert.NotEqual(before, sim.StateHash());
        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database);

        Assert.Equal(sim.StateHash(), restored.StateHash());
        Assert.Equal(a.Id, restored.World.RepairTaskEntries[(Keeper, Wall)].Holders[North]);
        Assert.Equal(17, restored.World.RepairTaskEntries[(Keeper, Wall)].Holders[2]);
    }

    /// <summary>A step that knocks one creature off its job into state 203 when armed, placed where
    /// DK2's fight scan acts: after the action poll, before the state dispatcher (0x00488930).</summary>
    private sealed class KnockOffAfterThePoll : Systems.ICreatureUpdateStep
    {
        public CreatureState? Target;
        public bool Armed;
        public string Name => "KnockOff";
        public void Execute(SimulationContext context) => Systems.CreatureUpdateSystem.RunAlone(context, this);

        public void Update(SimulationContext context, CreatureState creature)
        {
            if (!Armed || !ReferenceEquals(creature, Target)) return;
            Armed = false;
            creature.ClearJob();
            creature.Dk2StateId = 203;
        }
    }

    /// <summary>Action 8's handler applies the repair strike (0x00497AC0) from the action poll that
    /// precedes the fight scan (0x00489A68), not from state 24's handler (0x00466710): a repairer the
    /// fight scan moves off the wall in a turn its strike is due has still struck (Level 5, 1693: the
    /// fleeing imp's last +4 lands; Q811).</summary>
    [Fact]
    public void ARepairerMovedOffTheWallAfterTheActionPollHasStillStruckThatTurn_BinaryInferred()
    {
        var db = Dk2TerrainFixture.Database();
        GameSimulation probe = Start(out _, new TilePos(3, 2));
        var knock = new KnockOffAfterThePoll();
        ISimulationSystem[] systems = Dk2TerrainFixture.DefaultSystems();
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] is not Systems.CreatureUpdateSystem update) continue;
            var steps = new List<Systems.ICreatureUpdateStep>();
            foreach (Systems.ICreatureUpdateStep step in update.Steps)
            {
                steps.Add(step);
                if (step is Systems.CombatSystem) steps.Add(knock);
            }
            Assert.Contains(knock, steps);
            systems[i] = new Systems.CreatureUpdateSystem([.. steps]);
        }
        var sim = new GameSimulation(db, probe.World, 42, systems);
        CreatureState imp = sim.World.Creatures[0];
        knock.Target = imp;

        // Two strikes landed: the strike is now due every turn.
        TickUntil(sim, () => sim.World.Tile(Wall).Health >= 506);
        int before = sim.World.Tile(Wall).Health;
        knock.Armed = true;
        sim.Tick();

        Assert.False(knock.Armed);   // knocked off this turn, before its job step
        Assert.True(sim.World.Tile(Wall).Health > before, $"{sim.World.Tile(Wall).Health} vs {before}");
    }
}
