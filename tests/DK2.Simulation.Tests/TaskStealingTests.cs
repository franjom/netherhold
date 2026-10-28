// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>ADR-0289: a nearer imp takes a held task, an attacker claims what it broke under the
/// same job, and workers are updated newest first (imp-task-stealing.md, imp-claim-path.md).</summary>
public class TaskStealingTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    private sealed record Run(GameSimulation Sim, ListEventSink Events)
    {
        public WorldState World => Sim.World;
        public GameDatabase Db => Sim.Database;

        public void Ticks(int count)
        {
            for (int i = 0; i < count; i++) Sim.Tick(InputFrame.Empty);
        }

        public CreatureState Imp(TilePos at) =>
            World.SpawnCreature(Db, Dk2TerrainFixture.ImpCreatureId, Keeper, at);

        public IEnumerable<SimEvent> Of(string kind) => Events.Events.Where(e => e.Kind == kind);

        public int Index(TilePos at) => (at.Y * World.Width) + at.X;
    }

    /// <summary>The keeper's claimed 3×3 room at (1..3, 1..3) in rock, and a dirt path from
    /// (4,2) east to <paramref name="pathEndX"/>: only (4,2) touches the keeper's floor, so it is
    /// the one claimable tile.</summary>
    private static Run Start(int pathEndX = 4)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        for (int x = 4; x <= pathEndX; x++)
        {
            ref TileState path = ref world.Tile(x, 2);
            path.TerrainId = Dk2TerrainFixture.DirtPath;
            path.OwnerPlayerId = PlayerIds.Unowned;
            path.Health = database.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        }
        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink);
    }

    /// <summary>The start side and turn of <c>0x004805F0</c>, from the offset of the imp to the
    /// task tile, for each of its branches. Sides N, E, S, W are 0 to 3.</summary>
    [Theory]
    [InlineData(0, 5, 2, 1)]
    [InlineData(0, -5, 0, 1)]
    [InlineData(-3, 0, 3, 1)]
    [InlineData(-1, 2, 2, 1)]
    [InlineData(-2, 1, 3, -1)]
    [InlineData(-1, -2, 0, -1)]
    [InlineData(-2, -2, 3, 1)]
    [InlineData(3, 0, 1, 1)]
    [InlineData(1, 2, 2, -1)]
    [InlineData(2, 1, 1, 1)]
    [InlineData(1, -2, 0, 1)]
    [InlineData(2, -1, 1, -1)]
    public void TheDigWalkStartsOnTheSideTheBinaryPicks(int dx, int dy, int side, int step) =>
        Assert.Equal((side, step), MiningSlots.StartSide(dx, dy));

    /// <summary>
    /// <b>A nearer imp takes a claim from a farther one that is walking to it</b>, and not from
    /// one nearer than itself (<c>0x0046AB50</c>: strictly nearer the tile's centre). The first
    /// imp takes (4,2) from the far end of the path, or from beside it; the second, added
    /// beside the room two ticks later, is nearer only in the first case.
    /// </summary>
    [Theory]
    [InlineData(10, 2, true)]
    [InlineData(5, 1, false)]
    public void ANearerImpTakesAClaimFromAFartherOneAndOnlyThen(int firstX, int secondX, bool steals)
    {
        Run run = Start(pathEndX: 10);
        CreatureState first = run.Imp(new TilePos(firstX, 2));
        run.Ticks(3);
        Assert.Equal(run.Index(new TilePos(4, 2)), first.TargetTileIndex);

        CreatureState second = run.Imp(new TilePos(secondX, 2));
        run.Ticks(4);

        Assert.Equal(steals, run.Of("task_stolen").Any(e => e.Detail.Contains($"imp={second.Id} from={first.Id} (4,2)")));
        Assert.Equal(steals ? second.Id : first.Id,
            run.World.Creatures.Single(c => c.TargetTileIndex == run.Index(new TilePos(4, 2))).Id);
    }

    /// <summary>
    /// <b>A holder already at work is measured from where it stands.</b> The claim handler
    /// (<c>0x0046AB50</c>) reads the holder's <c>+0x1A/+0x1E</c> whatever its state, as do the
    /// reinforce handler and both arms of the dig walk. The first imp is at work on (4,2), 1600
    /// right of the centre; the second stands on the tile, 800, 1600 or 2000 left of it, and takes
    /// the claim on its choosing tick (the second) only when strictly nearer the centre: at the same
    /// distance it does not (<c>JNC</c> at <c>0x0046AE07</c>).
    /// </summary>
    [Theory]
    [InlineData(800, true)]
    [InlineData(1600, false)]
    [InlineData(2000, false)]
    public void AHolderAtWorkIsMeasuredFromWhereItStands(int secondOffset, bool steals)
    {
        Run run = Start();
        CreatureState first = run.Imp(new TilePos(3, 2));
        for (int i = 0; i < 200 && first.Dk2StateId is not (15 or 16); i++) run.Ticks(1);
        Assert.True(first.Dk2StateId is 15 or 16);
        Assert.Equal(run.Index(new TilePos(4, 2)), first.TargetTileIndex);
        int centreX = CreatureState.CentreOf(4), centreY = CreatureState.CentreOf(2);
        first.PosX = first.LastPosX = centreX + 1600;
        first.PosY = first.LastPosY = centreY;

        CreatureState second = run.Imp(new TilePos(4, 2));
        second.PosX = second.LastPosX = centreX - secondOffset;
        second.PosY = second.LastPosY = centreY;
        run.Ticks(2);

        Assert.Equal(steals, run.Of("task_stolen").Any(e => e.Detail.Contains($"imp={second.Id} from={first.Id} (4,2)")));
    }

    /// <summary>
    /// <b>A miner at work is measured from where it stands, not from its slot.</b> The dig walk's
    /// mine arm (<c>0x00469AF0</c>, compare at <c>0x0046A2CE</c>) takes a held slot when the
    /// chooser, from where it stands, is strictly nearer the side tile's corner than the holder is
    /// from its own <c>+0x1A/+0x1E</c>. Three imps hold the seam's three slots on its one open
    /// side (4,5), all at work (26). The one in the far slot is moved to the far corner of its
    /// stance tile (4,5): 7882 from that tile's corner by DK2's measure, against 5687 from its
    /// slot. A fourth in (4,4), 6335 from the corner, takes that slot.
    /// </summary>
    [Fact]
    public void AMinerAtWorkIsMeasuredFromWhereItStands()
    {
        Run run = Start();
        foreach (TilePos p in new[] { new TilePos(4, 3), new TilePos(4, 4), new TilePos(4, 5) })
        {
            ref TileState tile = ref run.World.Tile(p);
            tile.TerrainId = Dk2TerrainFixture.DirtPath;
            tile.OwnerPlayerId = PlayerIds.Unowned;
            tile.Health = run.Db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        }
        CreatureState[] miners = [run.Imp(new TilePos(2, 2)), run.Imp(new TilePos(2, 3)), run.Imp(new TilePos(3, 2))];
        run.Sim.Tick(InputFrame.Of(TagTilesCommand.Tag(Keeper, new TilePos(5, 5))));
        int seam = run.Index(new TilePos(5, 5));
        for (int i = 0; i < 400 && !miners.All(m => m.TargetTileIndex == seam && m.Dk2StateId == 26); i++) run.Ticks(1);
        Assert.All(miners, m => Assert.True(m.TargetTileIndex == seam && m.Dk2StateId == 26));

        CreatureState moved = miners.Single(m => m.WorkSlot == miners.Max(n => n.WorkSlot));
        moved.PosX = moved.LastPosX = 20400;
        moved.PosY = moved.LastPosY = 24500;
        CreatureState chooser = run.Imp(new TilePos(4, 4));
        chooser.PosX = chooser.LastPosX = 19500;
        chooser.PosY = chooser.LastPosY = 16450;
        run.Ticks(2);

        Assert.Contains(run.Of("task_stolen"), e => e.Detail.StartsWith($"imp={chooser.Id} from={moved.Id} (5,5)"));
    }

    /// <summary>
    /// <b>An attacker claims the floor it broke without choosing again.</b> Enemy floor is a
    /// kind-3 task with subkind 2; state 20 (<c>0x00465F20</c>) sets the subkind to 0 and goes
    /// to 15 with the task kept. So the tile is taken once, and never given up in between.
    /// </summary>
    [Fact]
    public void AnAttackerClaimsTheFloorItBrokeWithoutChoosingAgain()
    {
        Run run = Start();
        ref TileState enemy = ref run.World.Tile(4, 2);
        enemy.TerrainId = Dk2TerrainFixture.ClaimedPath;
        enemy.OwnerPlayerId = Rival;
        enemy.Health = run.Db.TerrainById(Dk2TerrainFixture.ClaimedPath).MaxHealth;
        run.Imp(new TilePos(3, 2));

        for (int i = 0; i < 3000 && run.World.Tile(4, 2).OwnerPlayerId != Keeper; i++) run.Ticks(1);

        Assert.Equal(Keeper, run.World.Tile(4, 2).OwnerPlayerId);
        Assert.Single(run.Of("attack_to_claim"));
        Assert.Single(run.Of("job_taken"), e => e.Detail.Contains("(4,2)"));
        Assert.DoesNotContain(run.Of("job_abandoned"), e => e.Detail.Contains("(4,2)"));
    }

    /// <summary>
    /// <b>Of two equally near imps, the newer takes the claim.</b> DK2 links a new creature at
    /// the head of its list (<c>0x004B4A10</c>) and the world turn walks it from the head, so the
    /// newer imp chooses first; the older, no nearer, cannot take it. Either spawn order must
    /// give the tile to the newer imp, or the rule is position in disguise.
    /// </summary>
    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    public void OfTwoEquallyNearImpsTheNewerTakesTheClaim(int olderY, int newerY)
    {
        Run run = Start();
        run.Imp(new TilePos(3, olderY));
        CreatureState newer = run.Imp(new TilePos(3, newerY));

        run.Ticks(4);

        SimEvent taken = run.Of("job_taken").First(e => e.Detail.Contains("(4,2)"));
        Assert.StartsWith($"imp={newer.Id} ", taken.Detail);
    }

    /// <summary>
    /// <b>Nothing protects a task taken this tick.</b> The handlers read no state stamp
    /// (imp-task-stealing.md §6). The newer imp, far down the path, chooses first and takes
    /// (4,2); the older one beside the room chooses after it in the same tick's walk, is
    /// nearer, and takes the tile the tick it was chosen.
    /// </summary>
    [Fact]
    public void ATaskTakenThisTickCanBeTakenThisTick()
    {
        Run run = Start(pathEndX: 10);
        CreatureState older = run.Imp(new TilePos(2, 2));
        CreatureState newer = run.Imp(new TilePos(10, 2));

        run.Ticks(4);

        SimEvent stolen = Assert.Single(run.Of("task_stolen"), e => e.Detail.Contains("(4,2)"));
        Assert.StartsWith($"imp={older.Id} from={newer.Id} ", stolen.Detail);
        GameTick tookAt = run.Of("job_taken").First(e => e.Detail.StartsWith($"imp={newer.Id} ")).Tick;
        Assert.Equal(tookAt, stolen.Tick);
    }
}
