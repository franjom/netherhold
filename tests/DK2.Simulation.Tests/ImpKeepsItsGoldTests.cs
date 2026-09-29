// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>ADR-0288: where DK2's state 10 puts gold down and where it does not, and which of
/// two equally near marks it digs. Every rule here was a difference the decision checker
/// (<c>dk2tools decide</c>) found between a Level 1 capture's choices and ours.</summary>
public class ImpKeepsItsGoldTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private sealed record Run(GameSimulation Sim, ListEventSink Events)
    {
        public WorldState World => Sim.World;
        public GameDatabase Db => Sim.Database;
        public CreatureState Imp => Sim.World.Creatures[0];

        public void Tick(params PlayerCommand[] commands) =>
            Sim.Tick(commands.Length == 0 ? InputFrame.Empty : InputFrame.Of(commands));

        public void Ticks(int count)
        {
            for (int i = 0; i < count; i++) Tick();
        }

        public IEnumerable<SimEvent> Of(string kind) => Events.Events.Where(e => e.Kind == kind);
    }

    private static Run Start(TilePos impAt)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper]);
        world.SpawnCreature(database, Dk2TerrainFixture.ImpCreatureId, Keeper, impAt);
        var sim = new GameSimulation(database, world, 99, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return new Run(sim, sink);
    }

    private static void Treasury(Run run, TilePos at)
    {
        ref TileState tile = ref run.World.Tile(at);
        RoomRules.Place(run.Db, ref tile, run.Db.Room(RoomTypeId.Treasury), Keeper);
    }

    /// <summary>
    /// <b>A claimer with gold in its hands claims on.</b> DK2's state 10 has no deposit
    /// before the choice: the Level 1 capture's imp carried 1092 gold through 41 claims
    /// over 680 ticks with room in the heart, and put it down only after its next dig.
    /// Our old pre-choice deposit sent it home instead.
    /// </summary>
    [Fact]
    public void AClaimerWithGoldInItsHandsClaimsOnRatherThanGoingToTheTreasury()
    {
        Run run = Start(new TilePos(3, 2));
        Treasury(run, new TilePos(1, 1));
        ref TileState path = ref run.World.Tile(4, 2);
        path.TerrainId = Dk2TerrainFixture.DirtPath;
        path.Health = run.Db.TerrainById(Dk2TerrainFixture.DirtPath).StartingHealth;
        run.Imp.GoldHeld = 500;
        run.Imp.LastTaskKind = ImpTaskKind.Claim;
        run.Imp.LastTaskTileIndex = (2 * run.World.Width) + 3;

        run.Ticks(3);

        Assert.Contains(run.Of("job_taken"), e => e.Detail.Contains("(4,2)") && e.Detail.Contains("priority=Claim"));
        Assert.NotEqual(CreatureActivity.DeliveringGold, run.Imp.Activity);
        Assert.Equal(500, run.Imp.GoldHeld);
    }

    /// <summary>
    /// <b>The aimed gold retry deposits a full load; nothing else in state 10 does.</b> The
    /// kind dispatcher's kind 7 (<c>0x00468520</c>) sends full hands to the store chooser;
    /// the per-tile gold handler of Tier B only declines them. So a full imp aimed at gold
    /// walks to the treasury, and the same imp without the aim stays put.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnImpWithFullHandsGoesToTheStoreOnlyWhenAimedAtGold(bool aimed)
    {
        Run run = Start(new TilePos(2, 2));
        Treasury(run, new TilePos(1, 1));
        run.Imp.GoldHeld = run.Db.CreatureById(Dk2TerrainFixture.ImpCreatureId).MaxGoldHeld;
        run.Imp.LastTaskKind = ImpTaskKind.FetchGold;
        run.Imp.DroppedTask = aimed ? ImpTaskKind.FetchGold : ImpTaskKind.None;

        run.Ticks(3);

        Assert.Equal(aimed, run.Of("gold_delivered").Any() || run.Imp.Activity == CreatureActivity.DeliveringGold);
    }

    /// <summary>
    /// <b>Of two equally near marks, the later marked is dug first.</b> DK2 files each mark
    /// at the head of its task list (<c>0x00507bb0</c>) and its dig walk keeps the first of
    /// equally near tasks, so the newest wins the tie. Its diggers stand off the tile's
    /// centre, which makes ties real: two of the Level 1 capture's choices were decided so.
    /// The walls are one diagonal step from an imp at the centre of (3,2); either marking
    /// order must win, or the rule is tile order in disguise.
    /// </summary>
    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    public void OfTwoEquallyNearMarksTheLaterMarkedIsDugFirst(int firstY, int laterY)
    {
        Run run = Start(new TilePos(3, 2));

        run.Tick(TagTilesCommand.Tag(Keeper, new TilePos(4, firstY)),
                 TagTilesCommand.Tag(Keeper, new TilePos(4, laterY)));
        run.Ticks(3);

        SimEvent taken = run.Of("job_taken").First();
        Assert.Contains($"(4,{laterY})", taken.Detail);
    }
}
