// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Harness;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Players;
using static DK2.Behavior.Tests.PlayerObservationTests;

namespace DK2.Behavior.Tests;

/// <summary>Action admission, deduplication and receipts (campaign player plan, task 3), on the
/// fixtures of <see cref="PlayerObservationTests"/>.</summary>
public class PlayerSessionTests
{
    private sealed class Harness
    {
        public Harness(Action<GameDatabase, WorldState>? setup = null, PlayerSessionLimits? limits = null)
        {
            (Database, World) = PlayerObservationTests.World();
            setup?.Invoke(Database, World);
            Builder = new PlayerObservationBuilder(Database, Viewer);
            Session = new PlayerSession(Run, Viewer, new PlayerActionAdapter(Database, Viewer), limits);
        }

        public GameDatabase Database { get; }
        public WorldState World { get; }
        public PlayerObservationBuilder Builder { get; }
        public PlayerSession Session { get; }
        public PlayerObservationCapture Current { get; private set; } = null!;
        private long _revision;

        public PlayerObservationCapture Observe()
        {
            Current = Builder.Capture(World, Stamp(World, ++_revision), Session.TakeReceipts());
            Session.Observe(Current);
            return Current;
        }

        public PlayerRequest Request(long id, PlayerAction action, PlayerStamp? basedOn = null) =>
            new(basedOn ?? Current.Observation.Stamp, new PlayerActionId(id), action);

        public PlayerReceipt Submit(long id, PlayerAction action) => Session.Submit(Request(id, action));
    }

    private static readonly PlayerAction Tag = new PlayerAction.TagArea(new TilePos(4, 0), new TilePos(5, 1));

    private static ulong HashOf(IReadOnlyList<PlayerCommand> commands)
    {
        Hash64 hash = Hash64.Start();
        new InputFrame(commands).HashInto(ref hash);
        return hash.Value;
    }

    /// <summary>The viewer sees its own creature on <see cref="PlayerObservationTests.OwnFloor"/>,
    /// holds another, has mana, and may build a Treasury and cast Create Imp.</summary>
    private static void Equipped(GameDatabase db, WorldState world)
    {
        world.SpawnCreature(db, Fighter, Viewer, OwnFloor);
        CreatureState held = world.SpawnCreature(db, Fighter, Viewer, new TilePos(2, 2));
        world.Player(Viewer).Hand.Add(HandItem.Of(held));
        held.InHand = true;
        world.Player(Viewer).Mana = 100000;
        world.Player(Viewer).Availabilities.Add(new AvailabilityState(AvailabilityKind.Room, (int)RoomTypeId.Treasury, 3));
        world.Player(Viewer).Availabilities.Add(new AvailabilityState(AvailabilityKind.Spell, BottomPanelView.CreateImpSpellId, 3));
    }

    [Fact]
    public void DuplicateRequestRunsOnce()
    {
        var h = new Harness();
        h.Observe();
        PlayerRequest request = h.Request(1, Tag);

        PlayerReceipt first = h.Session.Submit(request);
        Assert.Equal(first, h.Session.Submit(request));
        Assert.Equal(PlayerReceiptStatus.Queued, first.Status);

        Assert.IsType<TagTilesCommand>(Assert.Single(h.Session.Drain(new GameTick(5))));
        Assert.Empty(h.Session.Drain(new GameTick(6)));

        // A retry after a later observation gets the Submitted receipt and no second command.
        h.Observe();
        PlayerReceipt retried = h.Session.Submit(request);
        Assert.Equal((PlayerReceiptStatus.Submitted, (GameTick?)new GameTick(5)), (retried.Status, retried.ScheduledTick));
        Assert.Empty(h.Session.Drain(new GameTick(7)));
        Assert.Equal(1, h.Session.AdmittedCount);
    }

    [Fact]
    public void ConflictingIdIsRefused()
    {
        var h = new Harness();
        h.Observe();
        h.Submit(1, Tag);

        PlayerReceipt other = h.Submit(1, new PlayerAction.TagArea(new TilePos(0, 0), new TilePos(1, 1)));
        Assert.Equal((PlayerReceiptStatus.Refused, PlayerRefusalReason.ConflictingId), (other.Status, other.Reason));
        // The original stays queued and is the only command.
        Assert.Single(h.Session.Drain(new GameTick(3)));
        h.Observe();
        Assert.Equal(PlayerRefusalReason.ConflictingId, h.Session.Submit(h.Request(1, Tag)).Reason);
        Assert.Empty(h.Session.Drain(new GameTick(4)));
    }

    [Fact]
    public void QueuedRequestsDoNotSurviveTheNextObservation()
    {
        var h = new Harness((db, world) => world.SpawnCreature(db, Fighter, Viewer, OwnFloor));
        h.Observe();
        PlayerRequest pickup = h.Request(1, new PlayerAction.PickUp(h.Current.Observation.Things[0].Handle));
        h.Session.Submit(pickup);
        h.Session.TakeReceipts();

        PlayerObservationCapture next = h.Builder.Capture(h.World, Stamp(h.World, 2), h.Session.TakeReceipts());
        PlayerReceipt withdrawn = Assert.Single(h.Session.Observe(next));

        // Its creature id came from the earlier observation: withdrawn, never carried forward.
        Assert.Empty(h.Session.Drain(new GameTick(9)));
        PlayerReceipt stale = Assert.Single(h.Session.TakeReceipts());
        Assert.Equal(withdrawn, stale);
        Assert.Equal((PlayerReceiptStatus.Cancelled, PlayerRefusalReason.StaleObservation), (stale.Status, stale.Reason));
        Assert.Equal(stale, h.Session.Submit(pickup));
    }

    [Fact]
    public void ATransientRefusalMayBeRetriedUnderTheSameId()
    {
        var h = new Harness(limits: new PlayerSessionLimits(maxRequestsPerObservation: 1));
        PlayerStamp first = h.Observe().Observation.Stamp;
        h.Observe();
        Assert.Equal(PlayerRefusalReason.StaleObservation, h.Session.Submit(h.Request(1, Tag, first)).Reason);
        Assert.Equal(PlayerReceiptStatus.Queued, h.Submit(2, Tag).Status);
        Assert.Equal(PlayerRefusalReason.TooManyRequests, h.Submit(1, Tag).Reason);
        h.Session.Drain(new GameTick(1));
        h.Observe();
        Assert.Equal(PlayerReceiptStatus.Queued, h.Submit(1, Tag).Status);
        Assert.Throws<ArgumentOutOfRangeException>(() => h.Session.Drain(new GameTick(0)));
    }

    [Fact]
    public void StaleFutureOrOtherRunRequestsAreRefusedBeforeAnyHandleIsResolved()
    {
        var h = new Harness((db, world) => world.SpawnCreature(db, Fighter, Viewer, OwnFloor));
        Assert.Equal(PlayerRefusalReason.FutureObservation,
            h.Session.Submit(new PlayerRequest(Stamp(h.World), new PlayerActionId(1), Tag)).Reason);
        PlayerStamp first = h.Observe().Observation.Stamp;
        h.Observe();
        var unknown = new PlayerAction.PickUp(new PlayerThingHandle(new PlayerRevision(99), 99));

        Assert.Equal(PlayerRefusalReason.StaleObservation, h.Session.Submit(h.Request(2, unknown, first)).Reason);
        Assert.Equal(PlayerRefusalReason.FutureObservation,
            h.Session.Submit(h.Request(3, unknown, first with { Revision = new PlayerRevision(9) })).Reason);
        Assert.Equal(PlayerRefusalReason.WrongRun,
            h.Session.Submit(h.Request(4, unknown, h.Current.Observation.Stamp with { RunId = new PlayerRunId("other") })).Reason);
        Assert.Equal(PlayerRefusalReason.WrongRun, h.Session.Submit(new PlayerRequest(default, new PlayerActionId(5), unknown)).Reason);
        Assert.Equal(PlayerRefusalReason.OutOfBounds, h.Session.Submit(new PlayerRequest(h.Current.Observation.Stamp, default, unknown)).Reason);
        // A same-revision stamp with another tick is not the observation it names.
        Assert.Equal(PlayerRefusalReason.StaleObservation,
            h.Session.Submit(h.Request(6, unknown, h.Current.Observation.Stamp with { Tick = new GameTick(77) })).Reason);
        // Only a current request reaches the handle.
        Assert.Equal(PlayerRefusalReason.UnknownHandle, h.Submit(7, unknown).Reason);
        Assert.Empty(h.Session.Drain(new GameTick(1)));
    }

    [Fact]
    public void AHandleFromAnEarlierRevisionNeverResolves()
    {
        var h = new Harness((db, world) => world.SpawnCreature(db, Fighter, Viewer, OwnFloor));
        PlayerThingHandle old = Assert.Single(h.Observe().Observation.Things).Handle;
        h.Observe();
        // The thing still sits at ordinal 0, but handles never name slots or survive their revision.
        Assert.Equal(new PlayerThingHandle(h.Current.Observation.Stamp.Revision, 0), Assert.Single(h.Current.Observation.Things).Handle);
        Assert.Equal(PlayerRefusalReason.UnknownHandle, h.Submit(1, new PlayerAction.PickUp(old)).Reason);
        Assert.Equal(PlayerReceiptStatus.Queued,
            h.Submit(2, new PlayerAction.PickUp(h.Current.Observation.Things[0].Handle)).Status);
    }

    [Fact]
    public void WrongPlayerWrongPhaseAndUnofferedActionsAreRejected()
    {
        var h = new Harness(limits: new PlayerSessionLimits(maxRequestsPerObservation: 8));
        h.Observe();
        Assert.Equal(PlayerRefusalReason.WrongPlayer,
            h.Session.Submit(h.Request(1, Tag, h.Current.Observation.Stamp with { Player = Rival })).Reason);
        Assert.Equal(PlayerRefusalReason.WrongPhase, h.Submit(2, new PlayerAction.SkipIntro()).Reason);
        Assert.Equal(PlayerRefusalReason.NotOffered, h.Submit(3, new PlayerAction.DropAt(OwnFloor)).Reason);
        Assert.Equal(PlayerRefusalReason.NotOffered,
            h.Submit(4, new PlayerAction.BuildRoom(RoomTypeId.Treasury, new TilePos(0, 0), new TilePos(1, 1))).Reason);
        Assert.Equal(PlayerRefusalReason.OutOfBounds, h.Submit(5, new PlayerAction.TagArea(new TilePos(0, 0), new TilePos(Width, 0))).Reason);

        h.World.Player(Viewer).CameraPathEndTick = h.World.CurrentTick.Value + 10;
        h.Observe();
        Assert.Equal(PlayerRefusalReason.WrongPhase, h.Submit(6, Tag).Reason);
        Assert.Equal(PlayerReceiptStatus.Queued, h.Submit(7, new PlayerAction.SkipIntro()).Status);
        Assert.IsType<SkipCameraPathCommand>(Assert.Single(h.Session.Drain(new GameTick(2))));
    }

    [Fact]
    public void OfferedFamiliesStillRefuseUnofferedTargetsAndBounds()
    {
        var h = new Harness(Equipped, new PlayerSessionLimits(maxRequestsPerObservation: 8));
        h.Observe();
        Assert.Equal(PlayerRefusalReason.NotOffered,
            h.Submit(1, new PlayerAction.BuildRoom(RoomTypeId.Lair, new TilePos(0, 0), new TilePos(1, 1))).Reason);
        Assert.Equal(PlayerRefusalReason.OutOfBounds,
            h.Submit(2, new PlayerAction.BuildRoom(RoomTypeId.Treasury, new TilePos(0, 0), new TilePos(0, Height))).Reason);
        Assert.Equal(PlayerRefusalReason.OutOfBounds, h.Submit(3, new PlayerAction.DropAt(new TilePos(-1, 0))).Reason);
        Assert.Equal(PlayerRefusalReason.OutOfBounds, h.Submit(4, new PlayerAction.CreateImpAt(new TilePos(Width, 0))).Reason);
        Assert.Equal(PlayerRefusalReason.UnknownHandle,
            h.Submit(5, new PlayerAction.PickUp(new PlayerThingHandle(h.Current.Observation.Stamp.Revision, 1))).Reason);
        Assert.Empty(h.Session.Drain(new GameTick(1)));
    }

    [Fact]
    public void SelectPanelEmitsOrdinaryCommand()
    {
        var h = new Harness((_, world) =>
            world.Player(Viewer).Availabilities.Add(new AvailabilityState(AvailabilityKind.Room, (int)RoomTypeId.Treasury, 3)));
        h.Observe();
        GuiButtonTarget treasury = GuiButtonTarget.Room(RoomTypeId.Treasury);

        h.Submit(1, new PlayerAction.SelectPanel(treasury));

        Assert.Equal(new GuiButtonCommand(Viewer, treasury), Assert.Single(h.Session.Drain(new GameTick(1))));
        Assert.Equal(PlayerRefusalReason.NotOffered, h.Submit(2, new PlayerAction.SelectPanel(GuiButtonTarget.Room(RoomTypeId.Lair))).Reason);
    }

    [Fact]
    public void AdmittedActionsBuildTheHumanPathsCommands()
    {
        var h = new Harness((db, world) =>
        {
            world.Player(Viewer).Availabilities.Add(new AvailabilityState(AvailabilityKind.Room, (int)RoomTypeId.Treasury, 3));
            world.SpawnCreature(db, Fighter, Viewer, OwnFloor);
        });
        PlayerObservationCapture capture = h.Observe();
        var human = new PlayerInputTranslator { RubberBandLimit = PlacementCursor.RubberBandLimit(h.Database) };
        PlayerInputSnapshot snapshot = PlayerInputSnapshot.Capture(new WorldView(h.Database, Viewer), h.World, Viewer, h.Database);
        InputFrame Gesture(PlayerToolMode tool, TilePos from, TilePos to, RoomTypeId room = default)
        {
            human.Translate(new PlayerIntent(Viewer, tool, from, PointerButtonTransition.Pressed, room), snapshot);
            return human.Translate(new PlayerIntent(Viewer, tool, to, PointerButtonTransition.Released, room), snapshot);
        }

        // Tag drag, a room drag past the rubber band (5), a hand click on the creature, an untag drag.
        h.Submit(1, new PlayerAction.TagArea(new TilePos(0, 0), new TilePos(3, 2)));
        h.Submit(2, new PlayerAction.BuildRoom(RoomTypeId.Treasury, new TilePos(0, 0), new TilePos(7, 2)));
        h.Submit(3, new PlayerAction.PickUp(capture.Observation.Things[0].Handle));
        h.Submit(4, new PlayerAction.TagArea(new TilePos(3, 2), new TilePos(0, 0), Untag: true));
        IReadOnlyList<PlayerCommand> ours = h.Session.Drain(new GameTick(1));

        Assert.Equal(4, ours.Count);
        Assert.Equal(HashOf(Gesture(PlayerToolMode.TagTiles, new TilePos(0, 0), new TilePos(3, 2)).All), HashOf([ours[0]]));
        Assert.Equal(HashOf(Gesture(PlayerToolMode.BuildRoom, new TilePos(0, 0), new TilePos(7, 2), RoomTypeId.Treasury).All), HashOf([ours[1]]));
        Assert.Equal(new PickUpCreatureCommand(Viewer, h.World.Creatures[0].Id), ours[2]);
        Assert.Equal(HashOf(human.Translate(new PlayerIntent(Viewer, PlayerToolMode.Hand, OwnFloor, PointerButtonTransition.Pressed), snapshot).All),
            HashOf([ours[2]]));
        Assert.Equal(HashOf(Gesture(PlayerToolMode.UntagTiles, new TilePos(3, 2), new TilePos(0, 0)).All), HashOf([ours[3]]));
    }

    [Fact]
    public void ADropNamesTheCreatureUnderItAsTheHandDoes()
    {
        var h = new Harness(Equipped);
        h.Observe();
        PlayerInputSnapshot snapshot = PlayerInputSnapshot.Capture(new WorldView(h.Database, Viewer), h.World, Viewer, h.Database);
        var human = new PlayerInputTranslator();
        PlayerCommand Click(TilePos tile) => Assert.Single(human.Translate(new PlayerIntent(Viewer, PlayerToolMode.Hand, tile,
            PointerButtonTransition.None, SecondaryButton: PointerButtonTransition.Pressed), snapshot).All);

        h.Submit(1, new PlayerAction.DropAt(OwnFloor));
        h.Submit(2, new PlayerAction.DropAt(new TilePos(0, 2)));
        IReadOnlyList<PlayerCommand> ours = h.Session.Drain(new GameTick(1));

        Assert.Equal(Click(OwnFloor), ours[0]);
        Assert.NotEqual(0, ((DropCommand)ours[0]).TargetCreatureId);
        Assert.Equal(Click(new TilePos(0, 2)), ours[1]);
    }

    [Fact]
    public void DrainIsOnceAndCancelWithdrawsOnlyWhatIsQueued()
    {
        var h = new Harness();
        h.Observe();
        h.Submit(1, Tag);
        Assert.Single(h.Session.Drain(new GameTick(4)));
        h.Submit(2, new PlayerAction.TagArea(new TilePos(0, 0), new TilePos(1, 0)));
        h.Submit(3, new PlayerAction.TagArea(new TilePos(2, 0), new TilePos(3, 0)));

        IReadOnlyList<PlayerReceipt> cancelled = h.Session.Cancel();

        Assert.Equal([2L, 3L], cancelled.Select(r => r.Id.Value));
        Assert.All(cancelled, r => Assert.Equal((PlayerReceiptStatus.Cancelled, PlayerRefusalReason.RunCancelled), (r.Status, r.Reason)));
        Assert.Empty(h.Session.Cancel());
        Assert.Empty(h.Session.Drain(new GameTick(5)));
        Assert.Equal(PlayerReceiptStatus.Submitted, h.Session.Submit(h.Request(1, Tag)).Status);
        Assert.Equal(PlayerRefusalReason.RunCancelled, h.Submit(4, Tag).Reason);
        IReadOnlyList<PlayerReceipt> receipts = h.Session.TakeReceipts();
        Assert.Equal([PlayerReceiptStatus.Queued, PlayerReceiptStatus.Submitted, PlayerReceiptStatus.Queued, PlayerReceiptStatus.Queued,
            PlayerReceiptStatus.Cancelled, PlayerReceiptStatus.Cancelled, PlayerReceiptStatus.Refused], receipts.Select(r => r.Status));
        Assert.Empty(h.Session.TakeReceipts());
    }

    [Fact]
    public void NothingIsAdmittedAfterTheGameEnds()
    {
        var h = new Harness();
        h.World.Player(Viewer).Status = PlayerStatus.Won;
        h.Observe();
        Assert.Equal(PlayerRefusalReason.Terminal, h.Submit(1, Tag).Reason);
        Assert.Equal(PlayerRefusalReason.Terminal, h.Submit(2, new PlayerAction.SkipIntro()).Reason);
    }

    [Fact]
    public void RequestsPerObservationAndAdmittedActionsAreBounded()
    {
        var h = new Harness(limits: new PlayerSessionLimits(maxRequestsPerObservation: 2, maxAdmittedActions: 3));
        h.Observe();
        Assert.Equal(PlayerReceiptStatus.Queued, h.Submit(1, Tag).Status);
        Assert.Equal(PlayerRefusalReason.NotOffered, h.Submit(2, new PlayerAction.DropAt(OwnFloor)).Reason);
        Assert.Equal(PlayerRefusalReason.TooManyRequests, h.Submit(3, Tag).Reason);
        h.Session.Drain(new GameTick(1));
        h.Observe();
        Assert.Equal(PlayerReceiptStatus.Queued, h.Submit(4, Tag).Status);
        Assert.Equal(PlayerReceiptStatus.Queued, h.Submit(5, Tag).Status);
        h.Session.Drain(new GameTick(2));
        h.Observe();
        Assert.Equal(PlayerRefusalReason.ActionBudgetExhausted, h.Submit(6, Tag).Reason);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerSessionLimits(maxRequestsPerObservation: 0));
    }

    [Fact]
    public void ReceiptsDoNotDistinguishHiddenStates()
    {
        PlayerThingHandle Own(int ordinal) => new(new PlayerRevision(1), ordinal);
        PlayerAction[] probes =
        [
            new PlayerAction.TagArea(new TilePos(5, 0), new TilePos(7, 2)),
            new PlayerAction.PickUp(Own(0)),
            new PlayerAction.Slap(Own(0)),
            new PlayerAction.PickUp(Own(1)),
            new PlayerAction.DropAt(HiddenFloor),
            new PlayerAction.CreateImpAt(HiddenFloor),
            new PlayerAction.BuildRoom(RoomTypeId.Treasury, HiddenRoom, HiddenRoom),
        ];
        (PlayerReceipt[] Receipts, ulong Commands) Probe(Action<GameDatabase, WorldState> setup)
        {
            var h = new Harness(setup, new PlayerSessionLimits(maxRequestsPerObservation: probes.Length));
            h.Observe();
            PlayerReceipt[] receipts = probes.Select((p, i) => h.Submit(i + 1, p)).ToArray();
            return (receipts, HashOf(h.Session.Drain(new GameTick(1))));
        }

        var plain = Probe(Equipped);
        var hidden = Probe((db, world) => { Equipped(db, world); AddHiddenDifferences(db, world); });

        Assert.Equal(plain.Receipts, hidden.Receipts);
        Assert.Equal(plain.Commands, hidden.Commands);
        // Not vacuous: every probe but the past-the-end handle reached a command.
        Assert.Equal(6, plain.Receipts.Count(r => r.Status == PlayerReceiptStatus.Queued));
        Assert.Equal(PlayerRefusalReason.UnknownHandle, plain.Receipts[3].Reason);
    }

    [Fact]
    public void ASessionRefusesAnotherRunsObservationOrARepeatedRevision()
    {
        var h = new Harness();
        PlayerObservationCapture capture = h.Observe();
        Assert.Throws<ArgumentException>(() => h.Session.Observe(capture));
        var otherRun = new PlayerObservationBuilder(h.Database, Viewer)
            .Capture(h.World, new PlayerStamp(new PlayerRunId("other"), Viewer, h.World.CurrentTick, new PlayerRevision(5)), []);
        Assert.Throws<ArgumentException>(() => h.Session.Observe(otherRun));
    }
}
