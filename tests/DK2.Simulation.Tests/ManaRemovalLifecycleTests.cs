// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The live mana-pressure lifecycle (ADR-0277): warnings and scans on the payment,
/// the countdown before it, expiry through the death bookkeeping. The pure arithmetic is
/// <see cref="ManaPressureTests"/>; these run the systems.</summary>
public class ManaRemovalLifecycleTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    /// <summary>The removal-enabled state a keeper reaches after its fourth warning, with a scan
    /// due at once: what the fourth-warning payment leaves behind, minus the wait.</summary>
    private static readonly ManaPressureState Ready = new(
        WarningStage: 5, WarningsSuppressed: true, RemovalEnabled: true, ScanDeadline: 0);

    /// <summary>The fixture map: nine claimed tiles worth 9 mana a second, no heart, so five
    /// imps (35 a second) run the keeper dry and one removal restores the floor of four.</summary>
    private static (GameDatabase Db, WorldState World, List<CreatureState> Imps) Setup(
        int imps = 5, int claimedTiles = 9, byte owner = Keeper)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        int claimed = 9;
        for (int y = 1; y < world.Height - 1 && claimed < claimedTiles; y++)
            for (int x = 1; x < world.Width - 1 && claimed < claimedTiles; x++)
            {
                if (x <= 3 && y <= 3) continue;
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                world.Tile(x, y).OwnerPlayerId = Keeper;
                claimed++;
            }
        var spawned = new List<CreatureState>();
        for (int i = 0; i < imps; i++)
            spawned.Add(world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, owner, new TilePos(1 + i % 3, 1 + i / 3)));
        return (db, world, spawned);
    }

    private static (GameSimulation Sim, ListEventSink Events) Run(
        GameDatabase db, WorldState world, params ISimulationSystem[] systems)
    {
        var sim = new GameSimulation(db, world, 7, systems.Length == 0 ? Dk2TerrainFixture.DefaultSystems() : systems);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    private static int Field(SimEvent e, string key)
    {
        int start = e.Detail.IndexOf(key + "=", StringComparison.Ordinal) + key.Length + 1;
        int end = e.Detail.IndexOfAny([' ', '/'], start);
        return int.Parse(end < 0 ? e.Detail[start..] : e.Detail[start..end]);
    }

    private static IEnumerable<SimEvent> Of(ListEventSink sink, string kind) => sink.Events.Where(e => e.Kind == kind);

    // ---- the end-to-end scenario ---------------------------------------------------------

    [Fact]
    public void FiveImpsOnNineTilesLoseOneAfterFourWarningsAndFiveMoreSeconds()
    {
        var (db, world, imps) = Setup();
        var (sim, events) = Run(db, world);
        sim.Run(183);
        Assert.Equal(5, world.Creatures.Count);
        sim.Tick();
        Assert.Equal(4, world.Creatures.Count);
        sim.Run(26);

        // Warnings 1–4 every ten seconds from the first low-mana payment at turn 4; the fourth
        // enables the scan on the same payment and the countdown expires twenty ticks later.
        Assert.Equal([(1, 44), (2, 84), (3, 124), (4, 164)],
            Of(events, "mana_warning").Select(e => (Field(e, "stage"), Field(e, "turn"))));
        SimEvent scan = Assert.Single(Of(events, "mana_removal_scan"), e => Field(e, "turn") == 164);
        int chosen = Field(scan, "chosen");
        Assert.Contains(chosen, imps.Select(i => i.Id));
        SimEvent expired = Assert.Single(Of(events, "mana_removal_expired"));
        Assert.Equal((chosen, 164, 184), (Field(expired, "creature"), Field(expired, "stamped"), Field(expired, "turn")));
        Assert.Equal(183L, expired.Tick.Value);
        Assert.Null(world.CreatureById(chosen));
        Assert.DoesNotContain(world.Creatures, c => c.Dk2StateId == CreatureStateMachine.NoState);

        // The next scan, at 208, finds the floor of four and chooses nobody.
        Assert.Equal(-1, Field(Assert.Single(Of(events, "mana_removal_scan"), e => Field(e, "turn") == 208), "chosen"));
        Assert.Equal(4, world.Creatures.Count);
        Assert.Contains(world.Messages.Entries, m => m.Kind == PlayerMessageKind.ManaShortfall && m.Data1 == 3);
    }

    [Fact]
    public void OnlyAKeeperIsWarnedOrScanned()
    {
        var (db, world, _) = Setup(owner: PlayerIds.Good);
        var (sim, events) = Run(db, world);
        sim.Run(220);
        Assert.Empty(Of(events, "mana_warning"));
        Assert.Empty(Of(events, "mana_removal_scan"));
        Assert.Equal(5, world.Creatures.Count);
    }

    // ---- selection --------------------------------------------------------------------------

    [Fact]
    public void LevelTieTakesTheNewestIdleImpElseTheFarthestFromTheHeartPositionAndStampsIt()
    {
        var (db, world, imps) = Setup();
        world.Player(Keeper).ManaPressure = Ready;
        imps[1].WorkerIdle = true;
        imps[3].WorkerIdle = true;
        var (sim, events) = Run(db, world, new ManaRemovalSystem(), new ManaSystem());
        sim.Run(4);

        // Acquisition order is newest first, so the fourth-spawned idle imp beats the second.
        CreatureState chosen = imps[3];
        Assert.Equal(chosen.Id, Field(Assert.Single(Of(events, "mana_removal_scan")), "chosen"));
        Assert.Equal(4u, chosen.ManaRemovalStamp);
        Assert.True(chosen.CorpseSuppressed);
        Assert.All(imps.Where(i => i != chosen), i => Assert.Equal(0u, i.ManaRemovalStamp));
        PlayerMessage message = Assert.Single(world.Messages.Entries);
        Assert.Equal((PlayerMessageKind.ManaShortfall, 3, 0, Keeper), (message.Kind, message.Data1, message.Data2, message.PlayerId));

        // No idle imp and no heart: the farthest from the initialiser's (1, 1), which is the
        // one on (3, 1), two tiles off, ahead of the diagonal neighbour at root two.
        var (db2, world2, imps2) = Setup();
        world2.Player(Keeper).ManaPressure = Ready;
        var (sim2, events2) = Run(db2, world2, new ManaRemovalSystem(), new ManaSystem());
        sim2.Run(4);
        Assert.Equal(imps2[2].Id, Field(Assert.Single(Of(events2, "mana_removal_scan")), "chosen"));
        Assert.Equal((3, 1), (imps2[2].Tile.X, imps2[2].Tile.Y));
    }

    [Fact]
    public void AHeldWorkerCountsTowardTheFloorButIsNeverChosen()
    {
        var (db, world, imps) = Setup();
        world.Player(Keeper).ManaPressure = Ready;
        imps[4].WorkerIdle = true;
        var (sim, events) = Run(db, world, new HandSystem(), new ManaRemovalSystem(), new ManaSystem());
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imps[4].Id)));
        Assert.True(imps[4].InHand);
        sim.Run(3);
        int chosen = Field(Assert.Single(Of(events, "mana_removal_scan")), "chosen");
        Assert.NotEqual(imps[4].Id, chosen);
        Assert.NotEqual(-1, chosen);

        var (db2, world2, imps2) = Setup(imps: 4);
        world2.Player(Keeper).ManaPressure = Ready;
        var (sim2, events2) = Run(db2, world2, new HandSystem(), new ManaRemovalSystem(), new ManaSystem());
        sim2.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imps2[0].Id)));
        sim2.Run(3);
        Assert.Equal(-1, Field(Assert.Single(Of(events2, "mana_removal_scan")), "chosen"));
    }

    [Fact]
    public void AUniqueLowestLevelNonImpIsStampedNeverExpiresAndLeavesNoCorpseWhenItDies()
    {
        var (db, world, imps) = Setup();
        foreach (CreatureState imp in imps) imp.Level = 2;
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(3, 3));
        Assert.True(CreatureDeathRules.LeavesBody(db.CreatureById(goblin.CreatureTypeId)));
        world.Player(Keeper).ManaPressure = Ready;
        var (sim, _) = Run(db, world, new ManaRemovalSystem(), new ManaSystem(), new CreatureHealthSystem());
        sim.Run(4);
        Assert.Equal(4u, goblin.ManaRemovalStamp);
        Assert.True(goblin.CorpseSuppressed);
        Assert.False(HandRules.HasPickupPermission(world, db, goblin, Keeper));

        // Type 1 only: a hundred ticks of zero mana neither clear the stamp nor kill it, and
        // the scans at 48 and 92 (deadline strictly below the turn) refresh it each time.
        sim.Run(100);
        Assert.Equal(0, world.Player(Keeper).Mana);
        Assert.Equal(92u, goblin.ManaRemovalStamp);
        Assert.NotNull(world.CreatureById(goblin.Id));
        Assert.False(HandRules.HasPickupPermission(world, db, goblin, Keeper));

        // Killed by other means it collapses, waits its window and then leaves nothing.
        goblin.Health = 0;
        sim.Run(4);
        Assert.True(CreatureDeathRules.IsDying(goblin));
        sim.Run(CreatureDeathRules.DyingWindowTicks(db) + 4);
        Assert.Null(world.CreatureById(goblin.Id));

        // Control: the same Goblin never selected leaves a corpse.
        var (dbC, worldC, _) = Setup();
        CreatureState control = worldC.SpawnCreature(dbC, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(3, 3));
        var (simC, _) = Run(dbC, worldC, new CreatureHealthSystem());
        control.Health = 0;
        simC.Run(CreatureDeathRules.DyingWindowTicks(dbC) + 8);
        Assert.True(CreatureDeathRules.IsCorpse(worldC.CreatureById(control.Id)!));
    }

    // ---- the countdown ------------------------------------------------------------------------

    [Fact]
    public void AStampRefusesPickupUntilTheKeeperCanAffordExactlyOneUpkeep()
    {
        var (db, world, imps) = Setup(imps: 1);
        CreatureState imp = imps[0];
        imp.ManaRemovalStamp = 1;
        world.Player(Keeper).Mana = 6;
        var (sim, events) = Run(db, world, new HandSystem(), new ManaRemovalSystem());
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        Assert.False(imp.InHand);
        Assert.Single(Of(events, "pickup_refused"));
        Assert.Equal(1u, imp.ManaRemovalStamp);

        world.Player(Keeper).Mana = 7;
        sim.Tick();
        Assert.Equal(0u, imp.ManaRemovalStamp);
        Assert.Single(Of(events, "mana_removal_cancelled"));
        sim.Tick(InputFrame.Of(new PickUpCreatureCommand(Keeper, imp.Id)));
        Assert.True(imp.InHand);
    }

    [Fact]
    public void ExpiryRunsTheDeathBookkeepingBeforeThePaymentAndSpillsWhatWasCarried()
    {
        // Six mana and nine a second against two imps' fourteen: 1, then 0, never seven.
        var (db, world, imps) = Setup(imps: 2);
        world.Player(Keeper).Mana = 6;
        CreatureState imp = imps[0];
        imp.ManaRemovalStamp = 4;
        imp.GoldHeld = 1001;
        ObjectState crate = world.SpawnObject(db, Dk2TerrainFixture.AlarmCrateObjectId, Keeper, 0);
        ObjectCarryRules.PickUp(imp, crate);
        var (sim, events) = Run(db, world, new ManaRemovalSystem(), new ManaSystem());

        // Turns 1–23 leave it alone: 4 + 20 <= turn first holds at 24, which is tick 23.
        sim.Run(23);
        Assert.NotNull(world.CreatureById(imp.Id));
        Assert.Empty(Of(events, "mana_removal_expired"));
        sim.Tick();

        Assert.Null(world.CreatureById(imp.Id));
        Assert.Equal([imps[1].Id], world.OwnedCreatures(Keeper).Select(c => c.Id));
        Assert.Equal(1000, FloorGold.Total(world, db));
        Assert.Equal(-1, crate.CarriedByCreatureId);
        Assert.Contains(crate, world.Objects);
        Assert.Single(Of(events, "creature_died"), e => e.System == "ManaRemoval");
        Assert.DoesNotContain(events.Events, e => e.Kind == "creature_collapsed");

        // The payment on the expiry tick already charges one imp: the cleanup ran before it.
        SimEvent payment = Assert.Single(events.Events, e => e.Tick.Value == 23 && e.System == "Mana");
        Assert.Equal("mana_gained", payment.Kind);
        Assert.Contains("upkeep=7/s", payment.Detail);
    }

    [Fact]
    public void PossessionSkipsBothRecoveryAndExpiry()
    {
        var (db, world, imps) = Setup(imps: 1);
        CreatureState imp = imps[0];
        imp.ManaRemovalStamp = 1;
        world.Player(Keeper).Mana = 100;
        world.Player(Keeper).PossessedCreatureId = imp.Id;
        var (sim, _) = Run(db, world, new ManaRemovalSystem());
        sim.Run(40);
        Assert.Equal(1u, imp.ManaRemovalStamp);
        Assert.NotNull(world.CreatureById(imp.Id));
        world.Player(Keeper).Mana = 0;
        sim.Run(40);
        Assert.NotNull(world.CreatureById(imp.Id));
    }

    [Fact]
    public void RepeatedScansRefreshTheStampAndTheRemovalMessageIsDuplicateForTwentySeconds()
    {
        // 37 claimed tiles against five imps: two mana a second, so the first stamp is
        // reprieved once the keeper reaches seven, and every later scan finds it affordable.
        var (db, world, imps) = Setup(claimedTiles: 37);
        world.Player(Keeper).ManaPressure = Ready;
        var (sim, events) = Run(db, world, new ManaRemovalSystem(), new ManaSystem());
        sim.Run(4);
        SimEvent first = Assert.Single(Of(events, "mana_removal_scan"));
        CreatureState chosen = world.CreatureById(Field(first, "chosen"))!;
        Assert.Equal(4u, chosen.ManaRemovalStamp);
        uint messageId = Assert.Single(world.Messages.Entries).Id;

        sim.Run(13);
        Assert.Equal(0u, chosen.ManaRemovalStamp);
        Assert.Equal(8, world.Player(Keeper).Mana);
        SimEvent cancelled = Assert.Single(Of(events, "mana_removal_cancelled"));
        Assert.Equal(17, Field(cancelled, "turn"));

        sim.Run(31);
        Assert.Equal(2, Of(events, "mana_removal_scan").Count());
        CreatureState again = world.CreatureById(Field(Of(events, "mana_removal_scan").Last(), "chosen"))!;
        Assert.Equal(48u, again.ManaRemovalStamp);
        Assert.Equal(messageId, Assert.Single(world.Messages.Entries).Id);

        // The deadline set at 48 is 88, and 88 < 88 fails: the third scan waits for turn 92.
        sim.Run(40);
        Assert.Equal(2, Of(events, "mana_removal_scan").Count());
        sim.Run(4);
        Assert.Equal(3, Of(events, "mana_removal_scan").Count());
        Assert.NotEqual(messageId, Assert.Single(world.Messages.Entries).Id);
        Assert.Equal(5, world.Creatures.Count);
    }

    // ---- persistence ------------------------------------------------------------------------

    [Fact]
    public void SaveAndLoadContinueTheClocksTheStampAndTheExpiry()
    {
        var (db, world, _) = Setup();
        var (sim, _) = Run(db, world);
        sim.Run(170);
        Assert.Contains(world.Creatures, c => c.ManaRemovalStamp == 164 && c.CorpseSuppressed);
        Assert.Equal(5, world.Player(Keeper).ManaPressure.WarningStage);

        GameSimulation copy = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, Dk2TerrainFixture.DefaultSystems());
        Assert.Equal(world.Player(Keeper).ManaPressure, copy.World.Player(Keeper).ManaPressure);
        for (int i = 0; i < 40; i++)
        {
            Assert.Equal(sim.StateHash(), copy.StateHash());
            sim.Tick();
            copy.Tick();
        }
        Assert.Equal(sim.StateHash(), copy.StateHash());
        Assert.Equal(4, copy.World.Creatures.Count);
    }

    [Fact]
    public void TheHashSeesTheClocksTheStampAndTheCorpseBit()
    {
        ulong Hash(Action<WorldState> mutate)
        {
            var (db, world, _) = Setup(imps: 1);
            mutate(world);
            return new GameSimulation(db, world, 1, []).StateHash();
        }
        ulong baseline = Hash(_ => { });
        Assert.NotEqual(baseline, Hash(w => w.Player(Keeper).ManaPressure = new ManaPressureState(WarningStage: 1)));
        Assert.NotEqual(baseline, Hash(w => w.Player(Keeper).ManaPressure = new ManaPressureState(ScanDeadline: 1)));
        Assert.NotEqual(baseline, Hash(w => w.Player(Keeper).ManaPressure = new ManaPressureState(RemovalEnabled: true)));
        Assert.NotEqual(baseline, Hash(w => w.Creatures[0].ManaRemovalStamp = 1));
        Assert.NotEqual(baseline, Hash(w => w.Creatures[0].CorpseSuppressed = true));
    }

    // ---- the messages -----------------------------------------------------------------------

    [Fact]
    public void WarningSubtypesAreSeparateRecordsOfTwentySecondsAndStageZeroIsDiscarded()
    {
        var (db, world, _) = Setup();
        var sim = new GameSimulation(db, world, 1, []);
        Assert.Null(world.Messages.Post(world, Keeper, PlayerMessageKind.LowManaWarning, 0, 0));
        PlayerMessage one = world.Messages.Post(world, Keeper, PlayerMessageKind.LowManaWarning, 1, 0)!.Value;
        PlayerMessage two = world.Messages.Post(world, Keeper, PlayerMessageKind.LowManaWarning, 2, 0)!.Value;
        Assert.NotEqual(one.Id, two.Id);
        Assert.Equal((80u, true), (one.Timeout, one.NeedsTab));
        Assert.Equal(one.Id, world.Messages.Post(world, Keeper, PlayerMessageKind.LowManaWarning, 1, 0)!.Value.Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Messages.Post(world, Keeper, PlayerMessageKind.LowManaWarning, 5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Messages.Post(world, Keeper, PlayerMessageKind.ManaShortfall, 6, 0));
        sim.Run(79);
        Assert.Equal(2, world.Messages.Entries.Count);
        sim.Tick();
        Assert.Empty(world.Messages.Entries);
    }

    // ---- other writers of the corpse bit ---------------------------------------------------

    /// <summary>A Prison at its Skeleton cap sets the corpse bit back (<c>0x004B3240</c>), so a
    /// captive that mana-removal selection had suppressed still leaves a body there.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePrisonSkeletonCapRestoresTheCorpseBitSelectionCleared(bool suppressed)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)1, (byte)2, Keeper]);
        for (int y = 3; y <= 5; y++)
            for (int x = 4; x <= 6; x++)
                RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.Prison), Keeper);
        int cap = PrisonRules.SkeletonCap(world, db, Keeper);
        for (int i = 0; i < cap; i++)
            world.SpawnCreature(db, Dk2TerrainFixture.SkeletonCreatureId, Keeper, new TilePos(1, 1));
        CreatureState captive = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, 1, new TilePos(5, 4));
        Assert.True(PrisonRules.Imprison(new StateHookContext(world, db), captive, Keeper, (4 * world.Width) + 5));
        captive.CorpseSuppressed = suppressed;
        captive.Health = 1;
        var sim = new GameSimulation(db, world, 1, [new CreatureHealthSystem(), new PrisonSystem()]);
        sim.Run(16);
        Assert.NotNull(world.CreatureById(captive.Id));
        Assert.True(CreatureDeathRules.IsCorpse(captive));
        Assert.False(captive.CorpseSuppressed);
    }
}
