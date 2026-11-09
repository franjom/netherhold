// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>The campaign player decides from detached observations alone (campaign player plan,
/// task 5). Every fixture here is an observation drawn as a map; no world or privileged view.</summary>
public class CampaignPlayerTests
{
    private static readonly PlayerId Me = new(3), Heroes = new(PlayerIds.Good);
    private static readonly PlayerRunId Run = new("campaign-player-test");
    private static readonly CreatureTypeId Imp = new(1), Goblin = new(7);

    /// <summary>'.' own floor, 'H' own heart, '#' diggable rock, '$' gold seam, 'R' impenetrable rock,
    /// '?' unseen, 't' an own dig mark on unseen ground, 'e' enemy floor, 'T' own Treasury.</summary>
    private static PlayerTileGrid Map(params string[] rows)
    {
        var tiles = new PlayerTile[rows.Length * rows[0].Length];
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[0].Length; x++)
                tiles[y * rows[0].Length + x] = rows[y][x] switch
                {
                    '.' => new PlayerTile(PlayerTileFlags.Known, Me, default),
                    'T' => new PlayerTile(PlayerTileFlags.Known, Me, RoomTypeId.Treasury),
                    'H' => new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Solid, Me, RoomTypeId.DungeonHeart),
                    '#' => new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Solid | PlayerTileFlags.Diggable, default, default),
                    '$' => new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Solid | PlayerTileFlags.Diggable | PlayerTileFlags.GoldSeam, default, default),
                    'R' => new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Solid, default, default),
                    't' => new PlayerTile(PlayerTileFlags.Tagged, default, default),
                    'e' => new PlayerTile(PlayerTileFlags.Known, Heroes, default),
                    _ => default,
                };
        return new PlayerTileGrid(rows[0].Length, rows.Length, tiles);
    }

    /// <summary>Own floor x and y 2..8 around the heart at (5,5); a gold seam at (5,9).</summary>
    private static readonly string[] Dungeon =
    [
        "RRRRRRRRRRR",
        "R#########R",
        "R#.......#R",
        "R#.......#R",
        "R#.......#R",
        "R#...H...#R",
        "R#.......#R",
        "R#.......#R",
        "R#.......#R",
        "R####$####R",
        "RRRRRRRRRRR",
    ];

    private static PlayerObservation Observe(long revision = 1, string[]? map = null, int gold = 5000, int hand = 0,
        PlayerPhase phase = PlayerPhase.Playing, IReadOnlyList<PlayerRoomOffer>? rooms = null, IReadOnlyList<PlayerVisibleThing>? things = null,
        IReadOnlyList<PlayerRoomSummary>? owned = null, IReadOnlyList<PlayerReceipt>? receipts = null, long tick = -1)
    {
        rooms ??= [new PlayerRoomOffer(RoomTypeId.Treasury, new GoldAmount(50), true), new PlayerRoomOffer(RoomTypeId.Lair, new GoldAmount(50), true)];
        things ??= [];
        PlayerActionKinds actions = phase switch
        {
            PlayerPhase.Ended => PlayerActionKinds.None,
            PlayerPhase.Intro => PlayerActionKinds.SkipIntro,
            _ => PlayerActionKinds.TagArea | (rooms.Count > 0 ? PlayerActionKinds.BuildRoom | PlayerActionKinds.SelectPanel : 0)
                 | (hand > 0 ? PlayerActionKinds.DropAt : 0),
        };
        foreach (PlayerVisibleThing thing in things)
        {
            if ((thing.Actions & PlayerThingActions.PickUp) != 0) actions |= PlayerActionKinds.PickUp;
            if ((thing.Actions & PlayerThingActions.Activate) != 0) actions |= PlayerActionKinds.ActivateSpecial;
        }
        return new PlayerObservation
        {
            Stamp = new PlayerStamp(Run, Me, new GameTick(tick >= 0 ? tick : revision * 4), new PlayerRevision(revision)),
            Phase = phase,
            Gold = new GoldAmount(gold),
            HandCount = hand,
            Tiles = Map(map ?? Dungeon),
            Things = things,
            Rooms = owned ?? [],
            Receipts = receipts ?? [],
            Offers = new PlayerOffers
            {
                Actions = actions,
                Rooms = rooms,
                Panels = [.. rooms.Select(r => GuiButtonTarget.Room(r.Room))],
            },
        };
    }

    private static CampaignPlayer Player(Func<CampaignPlayerOptions, CampaignPlayerOptions>? tune = null) =>
        new((tune ?? (o => o))(new CampaignPlayerOptions(Run, Me, [Imp])));

    private static PlayerThingHandle Handle(int ordinal, long revision = 1) => new(new PlayerRevision(revision), ordinal);

    [Fact]
    public void AnAffordableRoomIsPressedThenBuiltOnTheNearestFreeSquareOfOwnFloor()
    {
        PlayerDecision decision = Player().Decide(Observe());

        Assert.Equal(new PlayerAction.SelectPanel(GuiButtonTarget.Room(RoomTypeId.Treasury)), decision.Actions[0]);
        var build = Assert.IsType<PlayerAction.BuildRoom>(decision.Actions[1]);
        // Free 3x3 squares avoid the heart (5,5): nearest centre to it, then Y, then X.
        Assert.Equal((RoomTypeId.Treasury, new TilePos(4, 2), new TilePos(6, 4)), (build.Room, build.From, build.To));
    }

    [Fact]
    public void RoomSquaresTieBreakOnYThenX()
    {
        PlayerObservation o = Observe();
        TilePos[] corners = [.. PlayerActionRoutines.RoomSquares(o, PlayerActionRoutines.Home(o), 3)];
        // Four squares lie at distance 4 from the heart; Y then X orders them.
        Assert.Equal([new TilePos(4, 2), new TilePos(2, 4), new TilePos(6, 4), new TilePos(4, 6)], corners[..4]);
        Assert.Equal(25 - 9, corners.Length);                            // 5x5 corners, less the nine squares on the heart
    }

    [Fact]
    public void NotEnoughGoldForTheWholeSquareBuildsNothing()
    {
        PlayerDecision decision = Player().Decide(Observe(gold: 449));
        Assert.DoesNotContain(decision.Actions, a => a is PlayerAction.BuildRoom or PlayerAction.SelectPanel);
        Assert.Contains(decision.Actions, a => a is PlayerAction.TagArea);
    }

    [Fact]
    public void APendingBuildIsNotRepeatedAndARefusedOneMovesToAnotherSquare()
    {
        CampaignPlayer player = Player();
        var first = (PlayerAction.BuildRoom)player.Decide(Observe(1)).Actions[1];
        Assert.DoesNotContain(player.Decide(Observe(2)).Actions, a => a is PlayerAction.BuildRoom);

        // Actions 1 and 2 were the press and the build; the build (id 2) was refused.
        PlayerReceipt refused = new(new PlayerActionId(2), PlayerReceiptStatus.Refused, PlayerRefusalReason.NotOffered);
        var retried = Assert.Single(player.Decide(Observe(3, receipts: [refused])).Actions.OfType<PlayerAction.BuildRoom>());
        Assert.NotEqual(first.From, retried.From);
    }

    [Fact]
    public void ARoomThePanelDoesNotOfferIsPassedOverForTheNext()
    {
        // A panel without a Treasury (as Level 1's opening is, in our runs) must not hold up the Lair.
        PlayerDecision decision = Player().Decide(Observe(rooms: [new PlayerRoomOffer(RoomTypeId.Lair, new GoldAmount(50), true)]));
        Assert.Equal(RoomTypeId.Lair, Assert.Single(decision.Actions.OfType<PlayerAction.BuildRoom>()).Room);
    }

    [Fact]
    public void ABuiltRoomIsNotBuiltAgainAndTheNextRoomFollows()
    {
        CampaignPlayer player = Player();
        var build = (PlayerAction.BuildRoom)player.Decide(Observe(1)).Actions[1];
        string[] built = [.. Dungeon];
        for (int y = build.From.Y; y <= build.To.Y; y++)
            built[y] = built[y][..build.From.X] + "TTT" + built[y][(build.To.X + 1)..];
        PlayerDecision next = player.Decide(Observe(2, map: built, owned: [new PlayerRoomSummary(RoomTypeId.Treasury, 9)]));
        Assert.Equal(RoomTypeId.Lair, Assert.Single(next.Actions.OfType<PlayerAction.BuildRoom>()).Room);
    }

    [Fact]
    public void TheFrontierTakesGoldFirstThenTheNearestThenYThenX()
    {
        PlayerObservation o = Observe();
        TilePos home = PlayerActionRoutines.Home(o);
        TilePos[] frontier = [.. PlayerActionRoutines.Frontier(o, home, preferGold: true, [])];
        Assert.Equal(new TilePos(5, 9), frontier[0]);                    // the seam
        Assert.Equal([new TilePos(5, 1), new TilePos(1, 5), new TilePos(9, 5)], frontier[1..4]);
        Assert.DoesNotContain(frontier, t => t.X == 0 || t.Y == 0);      // impenetrable rock is never marked
        Assert.Equal(new TilePos(5, 1), PlayerActionRoutines.Frontier(o, home, preferGold: false, []).First());
        Assert.DoesNotContain(new TilePos(5, 1), PlayerActionRoutines.Frontier(o, home, preferGold: false, [new TilePos(5, 1)]));
    }

    [Fact]
    public void DigMarksStayWithinTheOutstandingBound()
    {
        string[] marked = [.. Dungeon];
        marked[1] = "Rttttttt##R";                                     // seven marks already stand
        PlayerDecision decision = Player(o => o with { MaxOutstandingTags = 8 }).Decide(Observe(map: marked, gold: 0));
        Assert.Single(decision.Actions.OfType<PlayerAction.TagArea>());
    }

    [Fact]
    public void ANearbyEnemyIsAnsweredWithAFighterNeverAWorker()
    {
        PlayerVisibleThing hero = new(Handle(0), PlayerThingKind.Creature, new TilePos(9, 9), Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));
        PlayerVisibleThing imp = new(Handle(1), PlayerThingKind.Creature, new TilePos(2, 2), Me, PlayerThingActions.PickUp | PlayerThingActions.Slap, creatureType: Imp);
        PlayerVisibleThing goblin = new(Handle(2), PlayerThingKind.Creature, new TilePos(3, 2), Me, PlayerThingActions.PickUp | PlayerThingActions.Slap, creatureType: Goblin);

        Assert.Equal(new PlayerAction.PickUp(Handle(2)), Player().Decide(Observe(things: [hero, imp, goblin], gold: 0)).Actions[0]);
        Assert.DoesNotContain(Player().Decide(Observe(things: [hero, imp], gold: 0)).Actions, a => a is PlayerAction.PickUp);
        PlayerVisibleThing engaged = new(Handle(2), PlayerThingKind.Creature, new TilePos(8, 8), Me, goblin.Actions, creatureType: Goblin);
        Assert.DoesNotContain(Player().Decide(Observe(things: [hero, engaged], gold: 0)).Actions, a => a is PlayerAction.PickUp);
        // Holding it, drop it on the own floor nearest the enemy.
        Assert.Equal(new PlayerAction.DropAt(new TilePos(8, 8)), Player().Decide(Observe(things: [hero], hand: 1, gold: 0)).Actions[0]);
    }

    [Fact]
    public void NothingUnseenIsTargeted()
    {
        // No enemy in Things: no pick-up, whatever might lie behind the fog. An unseen tile carries no seam.
        string[] fogged = [.. Dungeon.Select(r => "?" + r[1..^1].Replace('R', '?').Replace('$', '#') + "?")];
        fogged[0] = fogged[^1] = new string('?', 11);
        PlayerDecision decision = Player().Decide(Observe(gold: 0, map: fogged));
        Assert.All(decision.Actions, a => Assert.IsType<PlayerAction.TagArea>(a));
        Assert.Equal(new PlayerAction.TagArea(new TilePos(5, 1), new TilePos(5, 1)), decision.Actions[0]);
    }

    [Fact]
    public void AnOfferedSpecialIsActivatedAndAGoneOneIsNot()
    {
        PlayerVisibleThing special = new(Handle(0), PlayerThingKind.Special, new TilePos(3, 3), Me, PlayerThingActions.Activate, objectType: new ObjectTypeId(88));
        Assert.Contains(new PlayerAction.ActivateSpecial(Handle(0)), Player().Decide(Observe(things: [special], gold: 0)).Actions);
        Assert.DoesNotContain(Player().Decide(Observe(gold: 0)).Actions, a => a is PlayerAction.ActivateSpecial);
    }

    [Fact]
    public void WithNothingOfferedItWaitsAndAtTheEndItIdles()
    {
        PlayerObservation nothing = Observe(rooms: []) is var o
            ? new PlayerObservation { Stamp = o.Stamp, Tiles = o.Tiles, Offers = PlayerOffers.None } : null!;
        PlayerDecision wait = Player().Decide(nothing);
        Assert.Empty(wait.Actions);
        Assert.False(wait.IsLimitation);

        PlayerDecision ended = Player().Decide(Observe(phase: PlayerPhase.Ended));
        Assert.Empty(ended.Actions);
        Assert.StartsWith("await verdict", ended.Reason, StringComparison.Ordinal);

        Assert.Equal([new PlayerAction.SkipIntro()], Player().Decide(Observe(phase: PlayerPhase.Intro)).Actions);
    }

    [Fact]
    public void WithNoProgressItRecoversThenReportsALimitation()
    {
        CampaignPlayer player = Player(o => o with { StallWindowTicks = 100, RecoveryAttempts = 2 });
        PlayerDecision? last = null;
        for (long tick = 0, revision = 1; tick <= 400 && last?.IsLimitation != true; tick += 50, revision++)
            last = player.Decide(Observe(revision, gold: 0, tick: tick));

        Assert.True(last!.IsLimitation);
        Assert.Contains("2 recoveries", last.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void IdenticalObservationsGiveIdenticalDecisionsAndAChangedOneChangesIt()
    {
        PlayerDecision a = Player().Decide(Observe(gold: 0)), b = Player().Decide(Observe(gold: 0));
        Assert.Equal(a.Actions, b.Actions);
        Assert.NotEqual(a.Actions, Player().Decide(Observe(gold: 5000)).Actions);
    }

    [Fact]
    public void OneLiftAndOneDropPerSightingWithinTheCooldown()
    {
        PlayerVisibleThing hero = new(Handle(0), PlayerThingKind.Creature, new TilePos(9, 9), Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));
        PlayerVisibleThing goblin = new(Handle(1), PlayerThingKind.Creature, new TilePos(3, 3), Me, PlayerThingActions.PickUp, creatureType: Goblin);
        CampaignPlayer player = Player(o => o with { DropCooldownTicks = 40 });

        Assert.Contains(new PlayerAction.PickUp(Handle(1)), player.Decide(Observe(1, things: [hero, goblin], gold: 0)).Actions);
        Assert.Contains(player.Decide(Observe(2, things: [hero], hand: 1, gold: 0)).Actions, a => a is PlayerAction.DropAt);
        // Dropped and stunned beside its old place: no new lift until the cooldown has passed.
        Assert.DoesNotContain(player.Decide(Observe(3, things: [hero, goblin], gold: 0)).Actions, a => a is PlayerAction.PickUp);
        Assert.Contains(new PlayerAction.PickUp(Handle(1)), player.Decide(Observe(20, things: [hero, goblin], gold: 0)).Actions);
    }

    [Fact]
    public void AFighterAlreadyOnTheBestDropFloorIsNotLiftedForAnOutOfReachEnemy()
    {
        // The hero stands 8 tiles beyond own floor; the goblin already stands where a drop would put it.
        string[] wide = [.. Dungeon.Select(r => r + "##########")];
        PlayerVisibleThing hero = new(Handle(0), PlayerThingKind.Creature, new TilePos(18, 5), Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));
        PlayerVisibleThing goblin = new(Handle(1), PlayerThingKind.Creature, new TilePos(8, 5), Me, PlayerThingActions.PickUp, creatureType: Goblin);
        Assert.DoesNotContain(Player().Decide(Observe(map: wide, things: [hero, goblin], gold: 0)).Actions, a => a is PlayerAction.PickUp);
        // The same goblin back home is lifted.
        PlayerVisibleThing home = new(Handle(1), PlayerThingKind.Creature, new TilePos(2, 2), Me, PlayerThingActions.PickUp, creatureType: Goblin);
        Assert.Contains(new PlayerAction.PickUp(Handle(1)), Player().Decide(Observe(map: wide, things: [hero, home], gold: 0)).Actions);
    }

    [Fact]
    public void AHeldFighterIsDroppedNearHomeWithNoEnemyAndARefusedDropTileIsLeft()
    {
        CampaignPlayer player = Player();
        PlayerAction first = player.Decide(Observe(1, hand: 1, gold: 0)).Actions[0];
        var drop = Assert.IsType<PlayerAction.DropAt>(first);
        Assert.True(PlayerActionRoutines.DistanceSquared(drop.Tile, new TilePos(5, 5)) <= 2);
        // The hand still holds it a decision later: the engine refused that tile.
        var next = Assert.IsType<PlayerAction.DropAt>(player.Decide(Observe(2, hand: 1, gold: 0)).Actions[0]);
        Assert.NotEqual(drop.Tile, next.Tile);
    }

    [Fact]
    public void APendingRoomExpiresToAnotherSquareAndATransientRefusalKeepsItsSquare()
    {
        CampaignPlayer player = Player(o => o with { PendingRoomTicks = 100 });
        var first = (PlayerAction.BuildRoom)player.Decide(Observe(1, tick: 0)).Actions[1];
        Assert.DoesNotContain(player.Decide(Observe(2, tick: 50)).Actions, a => a is PlayerAction.BuildRoom);
        var expired = Assert.Single(player.Decide(Observe(3, tick: 100)).Actions.OfType<PlayerAction.BuildRoom>());
        Assert.NotEqual(first.From, expired.From);

        CampaignPlayer other = Player();
        var built = (PlayerAction.BuildRoom)other.Decide(Observe(1)).Actions[1];
        PlayerReceipt stale = new(new PlayerActionId(2), PlayerReceiptStatus.Cancelled, PlayerRefusalReason.StaleObservation);
        Assert.Equal(built.From, Assert.Single(other.Decide(Observe(2, receipts: [stale])).Actions.OfType<PlayerAction.BuildRoom>()).From);
    }

    [Fact]
    public void AGainResetsTheStallClockAndRecoveryUntagsTheStandingMarks()
    {
        string[] marked = [.. Dungeon];
        marked[1] = "R#t#######R";
        CampaignPlayer player = Player(o => o with { StallWindowTicks = 100, RecoveryAttempts = 1 });
        player.Decide(Observe(1, map: marked, gold: 0, tick: 0));
        PlayerDecision recovered = player.Decide(Observe(2, map: marked, gold: 0, tick: 100));
        Assert.Contains(new PlayerAction.TagArea(new TilePos(2, 1), new TilePos(2, 1), Untag: true), recovered.Actions);
        Assert.False(recovered.IsLimitation);
        // Gold rose: the recovery count resets, so a later stall recovers again rather than giving up.
        player.Decide(Observe(3, map: marked, gold: 10, tick: 150));
        Assert.False(player.Decide(Observe(4, map: marked, gold: 10, tick: 250)).IsLimitation);
        Assert.True(player.Decide(Observe(5, map: marked, gold: 10, tick: 350)).IsLimitation);
        // A loss is not a gain: gold falling does not reset the clock.
        CampaignPlayer losing = Player(o => o with { StallWindowTicks = 100, RecoveryAttempts = 0 });
        losing.Decide(Observe(1, gold: 10, tick: 0));
        Assert.True(losing.Decide(Observe(2, gold: 5, tick: 100)).IsLimitation);
    }

    [Fact]
    public void OptionsAreValidatedAndSpecialsFollowTheAllowList()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Player(o => o with { RoomSide = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Player(o => o with { AdvanceTicks = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Player(o => o with { RecoveryAttempts = -1 }));
        var options = new CampaignPlayerOptions(Run, Me, [Imp]);
        Assert.False(options.WorkerTypes is HashSet<CreatureTypeId>);

        PlayerVisibleThing special = new(Handle(0), PlayerThingKind.Special, new TilePos(3, 3), Me, PlayerThingActions.Activate, objectType: new ObjectTypeId(88));
        Assert.DoesNotContain(Player(o => o with { SpecialTypes = [new ObjectTypeId(89)] }).Decide(Observe(things: [special], gold: 0)).Actions,
            a => a is PlayerAction.ActivateSpecial);
        Assert.Contains(new PlayerAction.ActivateSpecial(Handle(0)),
            Player(o => o with { SpecialTypes = [new ObjectTypeId(88)] }).Decide(Observe(things: [special], gold: 0)).Actions);
    }

    [Fact]
    public void NeutralAndUnownedCreaturesAreNotEnemiesAndUnseenGroundIsAFrontier()
    {
        PlayerVisibleThing neutral = new(Handle(0), PlayerThingKind.Creature, new TilePos(9, 9), new PlayerId(PlayerIds.Neutral), PlayerThingActions.None, creatureType: Goblin);
        Assert.False(PlayerActionRoutines.IsEnemy(neutral, Me));
        Assert.False(PlayerActionRoutines.IsEnemy(new PlayerVisibleThing(Handle(0), PlayerThingKind.Creature, new TilePos(9, 9),
            new PlayerId(PlayerIds.Unowned), PlayerThingActions.None, creatureType: Goblin), Me));
        Assert.True(PlayerActionRoutines.IsEnemy(new PlayerVisibleThing(Handle(0), PlayerThingKind.Creature, new TilePos(9, 9), Heroes, PlayerThingActions.None, creatureType: Goblin), Me));

        string[] unseen = [.. Dungeon];
        unseen[1] = "R####?####R";
        PlayerObservation o = Observe(map: unseen);
        Assert.Equal(new TilePos(5, 1), PlayerActionRoutines.Frontier(o, PlayerActionRoutines.Home(o), preferGold: false, []).First());
    }

    [Fact]
    public void PlayerControllersNeverNameAWorldADatabaseOrAPrivilegedView()
    {
        // The controller boundary as code: nothing under DK2.AI/Players may reach engine state.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DK2.slnx"))) directory = directory.Parent;
        string players = Path.Combine(directory!.FullName, "src", "DK2.AI", "Players");
        string[] files = Directory.GetFiles(players, "*.cs");
        Assert.Contains(files, f => f.EndsWith("CampaignPlayer.cs", StringComparison.Ordinal));
        string[] forbidden = ["WorldState", "GameDatabase", "KeeperView", "GameSimulation", "CreatureState", "ObjectState",
            "PlayerObservationCapture", "WorldView", "HandRules", "DropLandingRules", "GoldStore", "PlayerSession"];
        IEnumerable<string> Leaks(string name, string text) =>
            forbidden.Where(word => System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{word}\b")).Select(word => $"{name}: {word}");
        Assert.Empty(files.SelectMany(f => Leaks(Path.GetFileName(f), File.ReadAllText(f))));
        // Negative control: the scan does find a leak where one is written.
        Assert.Equal(["sample: WorldState", "sample: HandRules"], Leaks("sample", "var w = (WorldState)x; HandRules.CanSlap(d, c, p);"));
    }

    [Fact]
    public void AnObservationOfAnotherRunIsRefused()
    {
        PlayerObservation other = Observe() is var o ? new PlayerObservation { Stamp = o.Stamp with { RunId = new PlayerRunId("other") } } : null!;
        Assert.Throws<ArgumentException>(() => Player().Decide(other));
    }
}
