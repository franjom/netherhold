// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI.Players;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Tests.Players;

/// <summary>The Imp creation routine, rule R8 (owner direction, 2026-11-15): Imps to the work and the mana income,
/// never above the cap, never into the spell reserve, and few while the Library can still upgrade Create Imp.</summary>
public partial class CampaignPlayerTests
{
    /// <summary>A 40-tile ring of rock round own floor, the heart at (7,5) and a Library at x 2-4, y 3-4.</summary>
    private static readonly string[] ImpMap =
    [
        "RRRRRRRRRRRRRRR",
        "R#############R",
        "R#...........#R",
        "R#.BBB.......#R",
        "R#.BBB.......#R",
        "R#.....H.....#R",
        "R#...........#R",
        "R#...........#R",
        "R#...........#R",
        "R#############R",
        "RRRRRRRRRRRRRRR",
    ];

    /// <summary><see cref="ImpMap"/> with the first <paramref name="marks"/> rock tiles marked to dig.</summary>
    private static string[] Marked(int marks)
    {
        string[] rows = new string[ImpMap.Length];
        for (int y = 0; y < ImpMap.Length; y++)
        {
            char[] row = ImpMap[y].ToCharArray();
            for (int x = 0; x < row.Length && marks > 0; x++)
            {
                if (row[x] == '#') { row[x] = 'x'; marks--; }
            }

            rows[y] = new string(row);
        }
        return rows;
    }

    private static PlayerCreatureSummary[] Imps(int count, PlayerCreatureSummary[]? more = null)
    {
        return [.. Enumerable.Range(0, count).Select(i => new PlayerCreatureSummary(Imp, new TilePos(8 + i % 4, 6 + i / 4 % 3), false)), .. more ?? []];
    }

    private static PlayerSpellOffer CreateImpOffer(bool upgraded = false, bool ready = true)
    {
        return new(4, "Create Imp", new ManaAmount(1500), true, false, ready, upgraded);
    }

    /// <summary>A keeper with <paramref name="imps"/> Imps, 50000 mana, +300 income and 28 upkeep, Create Imp at 1500.</summary>
    private static PlayerObservation ImpView(int imps, int marks = 0, long tick = 1000, int mana = 50_000, int income = 300, int upkeep = 28,
        bool upgraded = false, PlayerCreatureSummary[]? more = null)
    {
        return Observe(tick / 4, map: Marked(marks), rooms: [], tick: tick, creatures: Imps(imps, more), spells: [CreateImpOffer(upgraded)],
            mana: mana, income: income, upkeep: upkeep, impCost: 1500);
    }

    private static CampaignPlayer ImpMaker(Func<PlayerStrategy, PlayerStrategy>? tune = null)
    {
        // A long stall window: these timelines pass minutes with dig marks standing and nothing dug. Four free Imps, as shipped.
        return Player(o => o with { StallWindowTicks = 100_000, FreeImps = 4 }).Also(p => p.Apply((tune ?? (s => s))(p.Strategy with { CreateImps = 1 })));
    }

    private static PlayerAction.CreateImpAt? Cast(CampaignPlayer player, PlayerObservation view)
    {
        return player.Decide(view).Actions.OfType<PlayerAction.CreateImpAt>().SingleOrDefault();
    }

    [Fact]
    public void AnImpIsCreatedOnOwnFloorWhileFewerThanTheMinimumAndOnByDefault()
    {
        PlayerAction.CreateImpAt cast = Assert.IsType<PlayerAction.CreateImpAt>(Cast(ImpMaker(), ImpView(imps: 4)));
        PlayerTile floor = ImpView(imps: 4).Tiles[cast.Tile];
        Assert.Equal(Me, floor.Owner);
        Assert.Equal(default, floor.Room);
        // On by default, ten the minimum (owner, 2026-11-17: "4 imps are never an optimal number, at least 10").
        Assert.Equal(1, new PlayerStrategy().CreateImps);
        Assert.Equal(10, new PlayerStrategy().MinImps);
        Assert.Null(Cast(Player().Also(p => p.Apply(p.Strategy with { CreateImps = 0 })), ImpView(imps: 4)));
        // At the minimum with no dig marks: enough.
        Assert.Null(Cast(ImpMaker(), ImpView(imps: 10)));
    }

    [Fact]
    public void TheDigMarksSetTheTargetUpToTheCapAndTheImpGoesToTheNearestMark()
    {
        // 36 marks want 12 Imps, above the minimum of ten; 11 are short of it.
        PlayerAction.CreateImpAt cast = Assert.IsType<PlayerAction.CreateImpAt>(Cast(ImpMaker(), ImpView(imps: 11, marks: 36)));
        Assert.Null(Cast(ImpMaker(), ImpView(imps: 12, marks: 36)));
        // The first marks are on the top row: the Imp lands on the floor beside them, not at the heart.
        Assert.Equal(2, cast.Tile.Y);
        // A minimum above the cap: the cap wins.
        Assert.Null(Cast(ImpMaker(s => s with { MaxImps = 10, MinImps = 20 }), ImpView(imps: 10)));
        Assert.NotNull(Cast(ImpMaker(s => s with { MaxImps = 10, MinImps = 20 }), ImpView(imps: 9)));
        // 40 marks want 14, but the cap holds at 10.
        Assert.Null(Cast(ImpMaker(s => s with { MaxImps = 10 }), ImpView(imps: 10, marks: 40)));
        Assert.NotNull(Cast(ImpMaker(s => s with { MaxImps = 10 }), ImpView(imps: 9, marks: 40)));
        Assert.Equal(16, new PlayerStrategy().MaxImps);
    }

    [Fact]
    public void ManaForSpellsIsKeptAndTheIncomeMustPayThePriceBack()
    {
        // 16000 less the 1500 price leaves 14500, under the 15000 reserve.
        Assert.Null(Cast(ImpMaker(), ImpView(imps: 4, mana: 16_000)));
        Assert.NotNull(Cast(ImpMaker(), ImpView(imps: 4, mana: 16_500)));
        // 1500 is 75 s of a net income of 20 a second, over the 60 s allowed; 60 s of 25 a second.
        Assert.Null(Cast(ImpMaker(), ImpView(imps: 4, income: 48, upkeep: 28)));
        Assert.NotNull(Cast(ImpMaker(), ImpView(imps: 4, income: 53, upkeep: 28)));
        Assert.Null(Cast(ImpMaker(), ImpView(imps: 4, income: 20, upkeep: 28)));
    }

    [Fact]
    public void WhileTheLibraryCanUpgradeCreateImpOnlyTheMinimumIsBoughtUnlessTheWorkIsPressing()
    {
        PlayerCreatureSummary[] researcher = [new(Goblin, new TilePos(3, 3), false)];
        Func<PlayerStrategy, PlayerStrategy> four = s => s with { MinImps = 4 };
        // 24 marks want 8 Imps; 6 are short of it, so without research one is bought...
        Assert.NotNull(Cast(ImpMaker(four), ImpView(imps: 6, marks: 24)));
        // ...but with a creature at work in the Library, the bot waits for the upgraded spell.
        Assert.Null(Cast(ImpMaker(four), ImpView(imps: 6, marks: 24, more: researcher)));
        // Upgraded: no more waiting.
        Assert.NotNull(Cast(ImpMaker(four), ImpView(imps: 6, marks: 24, more: researcher, upgraded: true)));
        // Pressing work (36 marks want 12, twice the 6 there are) does not wait.
        Assert.NotNull(Cast(ImpMaker(four), ImpView(imps: 6, marks: 36, more: researcher)));
        // Below the minimum is always bought.
        Assert.NotNull(Cast(ImpMaker(s => s with { MinImps = 5 }), ImpView(imps: 4, more: researcher)));

        // 33 marks want 11, short of twice the 6 there are: still waiting.
        Assert.Null(Cast(ImpMaker(four), ImpView(imps: 6, marks: 33, more: researcher)));
        // A worker in the Library, or a creature outside it, is no research.
        Assert.NotNull(Cast(ImpMaker(four), ImpView(imps: 6, marks: 24, more: [new(Imp, new TilePos(3, 3), false)])));
        Assert.NotNull(Cast(ImpMaker(four), ImpView(imps: 6, marks: 24, more: [new(Goblin, new TilePos(9, 3), false)])));

        // The wait ends after 20 game minutes even while research goes on.
        CampaignPlayer patient = ImpMaker(four);
        Assert.Null(Cast(patient, ImpView(imps: 6, marks: 24, tick: 1000, more: researcher)));
        Assert.Null(Cast(patient, ImpView(imps: 6, marks: 24, tick: 5700, more: researcher)));
        Assert.NotNull(Cast(patient, ImpView(imps: 6, marks: 24, tick: 5800, more: researcher)));

        // The research is remembered for a while after the creature leaves the Library, then forgotten.
        CampaignPlayer player = ImpMaker(four);
        Assert.Null(Cast(player, ImpView(imps: 6, marks: 24, tick: 1000, more: researcher)));
        Assert.Null(Cast(player, ImpView(imps: 6, marks: 24, tick: 1500)));
        Assert.NotNull(Cast(player, ImpView(imps: 6, marks: 24, tick: 2200)));
    }

    [Fact]
    public void OneImpIsCreatedAtATimeAndARechargingSpellIsLeftAlone()
    {
        CampaignPlayer player = ImpMaker();
        Assert.NotNull(Cast(player, ImpView(imps: 4, tick: 1000)));
        Assert.Null(Cast(player, ImpView(imps: 4, tick: 1004)));
        Assert.NotNull(Cast(player, ImpView(imps: 5, tick: 1008)));

        PlayerObservation recharging = Observe(250, map: Marked(0), rooms: [], tick: 1000, creatures: Imps(4),
            spells: [CreateImpOffer(ready: false)], mana: 50_000, income: 300, upkeep: 28, impCost: 1500);
        Assert.Null(Cast(ImpMaker(), recharging));
    }

    [Fact]
    public void NoImpIsCreatedIntoAFight()
    {
        TilePos birth = Assert.IsType<PlayerAction.CreateImpAt>(Cast(ImpMaker(), ImpView(imps: 4))).Tile;
        PlayerObservation With(int dx)
        {
            return Observe(250, map: Marked(0), rooms: [], tick: 1000, creatures: Imps(4),
            things: [new(Handle(1), PlayerThingKind.Creature, new TilePos(birth.X + dx, birth.Y), Heroes, PlayerThingActions.None,
                creatureType: new CreatureTypeId(20))],
            spells: [CreateImpOffer()], mana: 50_000, income: 300, upkeep: 28, impCost: 1500);
        }

        Assert.Null(Cast(ImpMaker(), With(2)));
        Assert.Null(Cast(ImpMaker(), With(10)));
        Assert.NotNull(Cast(ImpMaker(), With(11)));
    }

    [Fact]
    public void ATileTheCastWasRefusedAtIsNotChosenAgain()
    {
        CampaignPlayer player = ImpMaker();
        TilePos first = Assert.IsType<PlayerAction.CreateImpAt>(Cast(player, ImpView(imps: 4, tick: 1000))).Tile;
        PlayerReceipt refused = new(new PlayerActionId(1), PlayerReceiptStatus.Refused, PlayerRefusalReason.NotOffered);
        PlayerObservation next = Observe(252, map: Marked(0), rooms: [], tick: 1008, creatures: Imps(4), receipts: [refused],
            spells: [CreateImpOffer()], mana: 50_000, income: 300, upkeep: 28, impCost: 1500);
        Assert.NotEqual(first, Assert.IsType<PlayerAction.CreateImpAt>(Cast(player, next)).Tile);
    }

    [Fact]
    public void NoImpIsBoughtWhileAFreeOneIsOnItsWay()
    {
        // Below the four free Imps the game makes one every few seconds: the bot waits for it.
        Assert.Null(Cast(ImpMaker(), ImpView(imps: 3)));
        Assert.NotNull(Cast(ImpMaker(), ImpView(imps: 4)));
        // An Imp in the hand counts, as it does in the game's tally (Q148).
        Assert.NotNull(Cast(ImpMaker(), ImpView(imps: 3, more: [new(Imp, null, true)])));
        // Unknown (0, the default): never waits.
        Assert.Equal(0, new CampaignPlayerOptions(Run, Me, [Imp]).FreeImps);
        CampaignPlayer noFree = new CampaignPlayer(new CampaignPlayerOptions(Run, Me, [Imp]) { StallWindowTicks = 100_000 })
            .Also(p => p.Apply(p.Strategy with { CreateImps = 1 }));
        Assert.NotNull(Cast(noFree, ImpView(imps: 3)));
    }

    [Fact]
    public void TheImpKnobsAreChecked()
    {
        Assert.Contains("CreateImps 2 is not 0 or 1", new PlayerStrategy { CreateImps = 2 }.Validate());
        Assert.Contains("MinImps -1 is negative", new PlayerStrategy { MinImps = -1 }.Validate());
        Assert.Empty(new PlayerStrategy { MinImps = 20 }.Validate());   // the cap wins
        Assert.Contains("ManaReserve -1 is negative", new PlayerStrategy { ManaReserve = -1 }.Validate());
        Assert.Contains("ImpPriceSeconds 0 is not positive", new PlayerStrategy { ImpPriceSeconds = 0 }.Validate());
        Assert.Contains("MaxImps 0 is not positive", new PlayerStrategy { MaxImps = 0 }.Validate());
    }
}
