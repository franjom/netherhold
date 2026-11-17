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
public partial class CampaignPlayerTests
{
    private static readonly PlayerId Me = new(3), Heroes = new(PlayerIds.Good);
    private static readonly PlayerRunId Run = new("campaign-player-test");
    private static readonly CreatureTypeId Imp = new(1), Goblin = new(7);

    /// <summary>'.' own floor, 'H' own heart, '#' diggable rock, '$' gold seam, 'R' impenetrable rock,
    /// '?' unseen, 't' an own dig mark on unseen ground, 'x' an own dig mark on rock, 'e' enemy floor, 'T' own Treasury, 'L' own Lair,
    /// 'A' own Hatchery, 'G' own Training Room, 'W' own Workshop, 'P' own Prison, 'B' own Library, 'k' own floor with an own trap,
    /// 'w' water, 'l' lava.</summary>
    private static PlayerTileGrid Map(params string[] rows)
    {
        var tiles = new PlayerTile[rows.Length * rows[0].Length];
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[0].Length; x++)
                tiles[y * rows[0].Length + x] = rows[y][x] switch
                {
                    '.' => new PlayerTile(PlayerTileFlags.Known, Me, default),
                    'T' => new PlayerTile(PlayerTileFlags.Known, Me, RoomTypeId.Treasury),
                    'L' => new PlayerTile(PlayerTileFlags.Known, Me, RoomTypeId.Lair),
                    'A' => new PlayerTile(PlayerTileFlags.Known, Me, RoomTypeId.Hatchery),
                    'G' => new PlayerTile(PlayerTileFlags.Known, Me, RoomTypeId.TrainingRoom),
                    'W' => new PlayerTile(PlayerTileFlags.Known, Me, RoomTypeId.Workshop),
                    'P' => new PlayerTile(PlayerTileFlags.Known, Me, RoomTypeId.Prison),
                    'B' => new PlayerTile(PlayerTileFlags.Known, Me, RoomTypeId.Library),
                    'C' => new PlayerTile(PlayerTileFlags.Known, Me, RoomTypeId.CombatPit),
                    'n' => new PlayerTile(PlayerTileFlags.Known, new PlayerId(PlayerIds.Neutral), default),
                    'd' => new PlayerTile(PlayerTileFlags.Known, default, default),   // dirt path: not ownable, so no owner shows
                    'h' => new PlayerTile(PlayerTileFlags.Known, Heroes, default),   // hero-owned floor
                    'H' => new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Solid, Me, RoomTypeId.DungeonHeart),
                    '#' => new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Solid | PlayerTileFlags.Diggable, default, default),
                    '$' => new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Solid | PlayerTileFlags.Diggable | PlayerTileFlags.GoldSeam, default, default),
                    'R' => new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Solid, default, default),
                    't' => new PlayerTile(PlayerTileFlags.Tagged, default, default),
                    'x' => new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Solid | PlayerTileFlags.Diggable | PlayerTileFlags.Tagged, default, default),
                    'e' => new PlayerTile(PlayerTileFlags.Known, Heroes, default),
                    'k' => new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.OwnTrap, Me, default),   // own floor holding an own trap
                    'w' => new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Water, default, default),
                    'l' => new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Lava, default, default),
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
        IReadOnlyList<PlayerRoomSummary>? owned = null, IReadOnlyList<PlayerReceipt>? receipts = null, long tick = -1,
        IReadOnlyList<PlayerCreatureSummary>? creatures = null, IReadOnlyList<PlayerSpellOffer>? spells = null,
        int mana = 0, int income = 0, int upkeep = 0, int? impCost = null, IReadOnlyList<PlayerWorkshopOffer>? workshop = null,
        IReadOnlyList<PlayerVisibleTrap>? enemyTraps = null, int wageBill = 0)
    {
        rooms ??= [new PlayerRoomOffer(RoomTypeId.Treasury, new GoldAmount(50), true), new PlayerRoomOffer(RoomTypeId.Lair, new GoldAmount(50), true)];
        things ??= [];
        PlayerActionKinds actions = phase switch
        {
            PlayerPhase.Ended => PlayerActionKinds.None,
            PlayerPhase.Intro => PlayerActionKinds.SkipIntro,
            _ => PlayerActionKinds.TagArea | (rooms.Count > 0 ? PlayerActionKinds.BuildRoom | PlayerActionKinds.SelectPanel : 0)
                 | (hand > 0 ? PlayerActionKinds.DropAt : 0) | (spells is { Count: > 0 } ? PlayerActionKinds.CastSpell : 0)
                 | (impCost is not null ? PlayerActionKinds.CreateImpAt : 0) | (workshop is { Count: > 0 } ? PlayerActionKinds.PlaceTrap : 0),
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
            Mana = new ManaAmount(mana),
            ManaIncome = new ManaPerSecond(income),
            ManaUpkeep = new ManaPerSecond(upkeep),
            HandCount = hand,
            WageBill = new GoldAmount(wageBill),
            Tiles = Map(map ?? Dungeon),
            Things = things,
            EnemyTraps = enemyTraps ?? [],
            Rooms = owned ?? [],
            Creatures = creatures ?? [],
            Receipts = receipts ?? [],
            Offers = new PlayerOffers
            {
                Actions = actions,
                Rooms = rooms,
                Panels = [.. rooms.Select(r => GuiButtonTarget.Room(r.Room))],
                Spells = spells ?? [],
                Workshop = workshop ?? [],
                CreateImpCost = impCost is int cost ? new ManaAmount(cost) : null,
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
        // The hero stands 8 tiles beyond own floor (an attack: EnemyNearTiles is 8); the goblin already stands where a
        // drop would put it.
        string[] wide = [.. Dungeon.Select(r => r + "##########")];
        PlayerVisibleThing hero = new(Handle(0), PlayerThingKind.Creature, new TilePos(16, 5), Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));
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

    // ---- The strategy the routines read (campaign bot stage 3 plan, Task 1) ----

    [Fact]
    public void HoldHomeAnswersOnlyAnEnemyNearHomeAndOffAnswersNone()
    {
        PlayerVisibleThing far = new(Handle(0), PlayerThingKind.Creature, new TilePos(9, 9), Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));
        PlayerVisibleThing near = new(Handle(0), PlayerThingKind.Creature, new TilePos(6, 7), Heroes, PlayerThingActions.None, creatureType: new CreatureTypeId(20));
        PlayerVisibleThing goblin = new(Handle(2), PlayerThingKind.Creature, new TilePos(3, 2), Me, PlayerThingActions.PickUp | PlayerThingActions.Slap, creatureType: Goblin);

        CampaignPlayer hold = Player();
        hold.Apply(hold.Strategy with { Fight = FightMode.HoldHome, HomeGuardTiles = 3 });
        Assert.DoesNotContain(hold.Decide(Observe(things: [far, goblin], gold: 0)).Actions, a => a is PlayerAction.PickUp);
        Assert.Equal(new PlayerAction.PickUp(Handle(2)), hold.Decide(Observe(revision: 2, things: [near, goblin], gold: 0)).Actions[0]);

        CampaignPlayer off = Player();
        off.Apply(off.Strategy with { Fight = FightMode.Off });
        Assert.DoesNotContain(off.Decide(Observe(things: [near, goblin], gold: 0)).Actions, a => a is PlayerAction.PickUp);

        // Holding a fighter with no enemy it answers, either drops it near home, not beside the far enemy.
        foreach (CampaignPlayer player in new[] { hold, off })
        {
            PlayerObservation holding = Observe(revision: 3, things: [far], hand: 1, gold: 0);
            TilePos nearHome = PlayerActionRoutines.NearestOwnFloor(holding, PlayerActionRoutines.Home(holding), [])!.Value;
            Assert.Equal(new PlayerAction.DropAt(nearHome), player.Decide(holding).Actions[0]);
            Assert.NotEqual(new TilePos(8, 8), nearHome);
        }
    }

    [Fact]
    public void FrontierDigsNearestHomeGoldOrNot()
    {
        CampaignPlayer player = Player();
        player.Apply(player.Strategy with { Dig = DigMode.Frontier });
        TilePos first = Assert.IsType<PlayerAction.TagArea>(player.Decide(Observe(gold: 0, rooms: [])).Actions[0]).From;
        Assert.NotEqual(new TilePos(5, 9), first);                          // gold first would take the seam
        Assert.Equal(PlayerActionRoutines.Frontier(Observe(gold: 0, rooms: []), new TilePos(5, 5), preferGold: false, [])[0], first);
    }

    [Fact]
    public void UntaggingOutlastsARecoveryThatFillsTheDecisionAndStopsWhenDiggingResumes()
    {
        string[] marked = [.. Dungeon];
        marked[1] = "Rtttttt###R";
        CampaignPlayer player = Player(o => o with { StallWindowTicks = 8 });
        player.Apply(player.Strategy with { Dig = DigMode.Off });
        Assert.Equal(4, player.Decide(Observe(map: marked, gold: 0, rooms: [], tick: 4)).Actions.Count);
        // No gain for the window: the recovery's untags fill the decision, and the step must not end.
        PlayerDecision recovery = player.Decide(Observe(revision: 2, map: marked, gold: 0, rooms: [], tick: 20));
        Assert.Equal(4, recovery.Actions.Count);
        Assert.Equal(recovery.Actions.Count, recovery.Actions.Distinct().Count());
        marked[1] = "R####tt###R";
        Assert.Equal(2, player.Decide(Observe(revision: 3, map: marked, gold: 0, rooms: [], tick: 24)).Actions.Count);

        // Turning digging back on mid-untag leaves the remaining marks to be dug.
        CampaignPlayer resumed = Player();
        resumed.Apply(resumed.Strategy with { Dig = DigMode.Off });
        resumed.Apply(resumed.Strategy with { Dig = DigMode.GoldFirst });
        Assert.DoesNotContain(resumed.Decide(Observe(map: marked, gold: 0, rooms: [])).Actions, a => a is PlayerAction.TagArea { Untag: true });
    }

    [Fact]
    public void AnIdlePlayerWithDiggingOffIsNotALimitation()
    {
        static CampaignPlayer Idle(DigMode dig)
        {
            CampaignPlayer player = Player(o => o with { StallWindowTicks = 8, RecoveryAttempts = 0 });
            player.Apply(player.Strategy with { Dig = dig });
            return player;
        }
        foreach (DigMode dig in new[] { DigMode.Off, DigMode.GoldFirst })
        {
            CampaignPlayer player = Idle(dig);
            player.Decide(Observe(gold: 0, rooms: [], tick: 4));
            PlayerDecision later = player.Decide(Observe(revision: 2, gold: 0, rooms: [], tick: 40));
            // With a frontier still to dig, no gain is a stall; with digging off it is not.
            Assert.Equal(dig == DigMode.GoldFirst, later.IsLimitation);
        }
    }

    [Fact]
    public void DigOffTagsNothingAndTakesItsMarksBackAcrossDecisions()
    {
        string[] marked = [.. Dungeon];
        marked[1] = "Rtttttt###R";
        CampaignPlayer player = Player();
        player.Apply(player.Strategy with { Dig = DigMode.Off });

        PlayerDecision first = player.Decide(Observe(map: marked, gold: 0, rooms: []));
        Assert.Equal(4, first.Actions.Count);
        Assert.All(first.Actions, a => Assert.True(a is PlayerAction.TagArea { Untag: true }));

        marked[1] = "R####tt###R";
        PlayerDecision second = player.Decide(Observe(revision: 2, map: marked, gold: 0, rooms: []));
        Assert.Equal([new PlayerAction.TagArea(new TilePos(5, 1), new TilePos(5, 1), Untag: true),
                      new PlayerAction.TagArea(new TilePos(6, 1), new TilePos(6, 1), Untag: true)], second.Actions);

        Assert.Empty(player.Decide(Observe(revision: 3, gold: 0, rooms: [])).Actions);
    }

    [Fact]
    public void ARemovedRoomsPendingSquareIsGivenUp()
    {
        CampaignPlayer player = Player();
        Assert.Contains(player.Decide(Observe()).Actions, a => a is PlayerAction.BuildRoom { Room: RoomTypeId.Treasury });
        // The Treasury is pending, which holds every other build until it lands or is given up.
        Assert.DoesNotContain(player.Decide(Observe(revision: 2)).Actions, a => a is PlayerAction.BuildRoom);

        player.Apply(player.Strategy with { Rooms = [RoomTypeId.Lair] });
        Assert.Contains(player.Decide(Observe(revision: 3)).Actions, a => a is PlayerAction.BuildRoom { Room: RoomTypeId.Lair });
    }

    [Fact]
    public void TowardOrdersTheFrontierByDistanceToItsTarget()
    {
        // Gold first would mark the seam at (5,9) first; toward (9,5) marks (9,5).
        Assert.Equal(new PlayerAction.TagArea(new TilePos(5, 9), new TilePos(5, 9)), Player().Decide(Observe(gold: 0, rooms: [])).Actions[0]);
        CampaignPlayer player = Player();
        player.Apply(player.Strategy with { Dig = DigMode.Toward, DigTarget = new TilePos(9, 5) });
        Assert.Equal(new PlayerAction.TagArea(new TilePos(9, 5), new TilePos(9, 5)), player.Decide(Observe(gold: 0, rooms: [])).Actions[0]);
    }

    [Fact]
    public void SpecialsOffLeavesAnOfferedSpecialAndABadStrategyIsRefused()
    {
        PlayerVisibleThing special = new(Handle(0), PlayerThingKind.Special, new TilePos(3, 3), Me, PlayerThingActions.Activate, objectType: new ObjectTypeId(88));
        CampaignPlayer player = Player();
        player.Apply(player.Strategy with { UseSpecials = false });
        Assert.DoesNotContain(player.Decide(Observe(things: [special], gold: 0)).Actions, a => a is PlayerAction.ActivateSpecial);

        var refused = Assert.Throws<ArgumentException>(() => player.Apply(player.Strategy with { RoomSide = 0, AdvanceTicks = 0 }));
        Assert.Contains("RoomSide", refused.Message, StringComparison.Ordinal);
        Assert.Contains("AdvanceTicks", refused.Message, StringComparison.Ordinal);
        Assert.False(player.Strategy.UseSpecials); // the refused strategy left the current one in place
        Assert.Throws<ArgumentException>(() => player.Apply(player.Strategy with { Dig = DigMode.Toward, DigTarget = new TilePos(30, 30) }));
    }
}
