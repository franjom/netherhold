// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.AI.Players;

/// <summary>How the campaign player marks rock to dig.</summary>
public enum DigMode
{
    /// <summary>No new marks; standing ones are taken back.</summary>
    Off,

    /// <summary>The frontier, gold seams first, then nearest home (the default).</summary>
    GoldFirst,

    /// <summary>The frontier nearest home, gold or not.</summary>
    Frontier,

    /// <summary>The frontier nearest <see cref="PlayerStrategy.DigTarget"/>.</summary>
    Toward,
}

/// <summary>What the campaign player does about visible enemies.</summary>
public enum FightMode
{
    /// <summary>Lift free fighters and drop them at an attack on the dungeon (an enemy within
    /// <see cref="PlayerStrategy.EnemyNearTiles"/> of own ground), the one nearest home first (the default).</summary>
    Engage,

    /// <summary>Answer only an enemy within <see cref="PlayerStrategy.HomeGuardTiles"/> of home;
    /// anything held with no such enemy goes down near home.</summary>
    HoldHome,

    /// <summary>No lifting at all; anything held goes down near home.</summary>
    Off,
}

/// <summary>Which rooms get a square dug out for them when no free square is big enough (Task 3a).</summary>
public enum RoomSites
{
    None,

    /// <summary>Only a room that must be larger than <see cref="PlayerStrategy.RoomSide"/>: a Library sized for its
    /// Warlocks, a Temple. Sites for every room lost Level 4's two diagnosis wins (2026-11-14).</summary>
    Oversized,

    /// <summary>Every room; won Level 3 seed 3 and held Levels 6b and 7 longer on the diagnosis seeds.</summary>
    All,
}

/// <summary>When the frontier may open onto others' ground (campaign bot stage 3 plan, Task 2).</summary>
public enum BreachMode
{
    /// <summary>Never: a wall between the player and another side's open ground, or near where an enemy
    /// was seen, stays standing.</summary>
    Never,

    /// <summary>Only once no other tile is left to mark: develop first, then dig on toward the rest of the map.</summary>
    WhenDeveloped,

    /// <summary>Any frontier tile, nearest first, as before Task 2.</summary>
    Always,
}

/// <summary>
/// The values the campaign player's routines read (campaign bot stage 3 plan, Task 1). Test policy for
/// this controller, not a claim about DK2's own keepers. The defaults are the controller's behaviour
/// before strategies existed; <see cref="CampaignPlayer.Apply"/> changes them during a run.
/// </summary>
public sealed record PlayerStrategy
{
    /// <summary>The harness's default bound on a decision's advance (<c>--player-interval-ticks</c> takes
    /// 1 to 40); a strategy keeps within it whatever the run's own bound.</summary>
    public const int MaxAdvanceTicks = 40;

    /// <summary>Rooms to keep, in the order they are built.</summary>
    public IReadOnlyList<RoomTypeId> Rooms { get; init; } = [RoomTypeId.Treasury, RoomTypeId.Lair, RoomTypeId.Hatchery];

    /// <summary>A room is built as a square of at least this side; <see cref="KeeperKnowledge"/> and the targets raise it (Task 3a).</summary>
    public int RoomSide { get; init; } = 3;

    public DigMode Dig { get; init; } = DigMode.GoldFirst;

    /// <summary>The tile <see cref="DigMode.Toward"/> digs toward; null for every other mode.</summary>
    public TilePos? DigTarget { get; init; }

    /// <summary>At most this many of the player's own dig marks stand at once.</summary>
    public int MaxOutstandingTags { get; init; } = 8;

    public FightMode Fight { get; init; } = FightMode.Engage;

    /// <summary>Under <see cref="FightMode.HoldHome"/>, how near home (tiles) an enemy must be to be answered.</summary>
    public int HomeGuardTiles { get; init; } = 8;

    /// <summary>Activate offered specials (within the options' allowed types).</summary>
    public bool UseSpecials { get; init; } = true;

    /// <summary>Ticks between ordinary decisions, 1 to <see cref="MaxAdvanceTicks"/>.</summary>
    public int AdvanceTicks { get; init; } = 4;

    /// <summary>Rooms worth taking where the map holds them (rule R66): one in view that another player or nobody owns
    /// is dug toward before the rest of the frontier, held-back ground included, until own floor touches it.</summary>
    public IReadOnlyList<RoomTypeId> TakeRooms { get; init; } = [RoomTypeId.Portal, RoomTypeId.Prison, RoomTypeId.TortureChamber];

    /// <summary>Tiles of rooms to take that a player who knows the level knows of before seeing them (a level's
    /// playbook, R66), dug toward the same way until own floor touches them.</summary>
    public IReadOnlyList<TilePos> TakeAt { get; init; } = [];

    /// <summary>Rooms the panel offers that are not in <see cref="Rooms"/>, built once each in this
    /// order after every listed room stands and none has outgrown itself (stage 3 plan, Task 2).</summary>
    public IReadOnlyList<RoomTypeId> ExtraRooms { get; init; } =
    [
        RoomTypeId.TrainingRoom, RoomTypeId.Library, RoomTypeId.Workshop, RoomTypeId.Prison, RoomTypeId.Temple,
        RoomTypeId.Graveyard, RoomTypeId.TortureChamber, RoomTypeId.Casino, RoomTypeId.CombatPit,
    ];

    /// <summary>A listed Lair grows while it has fewer tiles than this many per own non-worker creature.</summary>
    public int LairTilesPerCreature { get; init; } = 1;

    /// <summary>A listed Hatchery grows while it has fewer tiles than one per this many own non-workers.</summary>
    public int CreaturesPerHatcheryTile { get; init; } = 2;

    /// <summary>A listed Treasury grows once the player's gold reaches this share (percent) of what its
    /// heart and treasuries hold; only when the options know the per-tile capacities.</summary>
    public int TreasuryFullPercent { get; init; } = 80;

    /// <summary>Rooms the hand keeps working, in order: an idle own non-worker is carried to one
    /// that has fewer creatures on it than its tiles allow (<see cref="TilesPerWorkingCreature"/>).
    /// None by default: carrying fighters to the Training Room lost Level 3 accept seeds 12 and 13
    /// that the player wins without it (stage 3 plan, Task 2), so a level's strategy names it.</summary>
    public IReadOnlyList<RoomTypeId> UseRooms { get; init; } = [];

    /// <summary>A used room wants one creature on it per this many of its tiles.</summary>
    public int TilesPerWorkingCreature { get; init; } = 3;

    /// <summary>At most one creature is carried to a used room per this many ticks.</summary>
    public int UseRoomEveryTicks { get; init; } = 200;

    /// <summary>No creature is carried to a used room while gold is below this.</summary>
    public int UseRoomMinGold { get; init; } = 1000;

    /// <summary>When digging may open onto ground another player owns or near where an enemy was seen.</summary>
    public BreachMode Breach { get; init; } = BreachMode.WhenDeveloped;

    /// <summary>Per room type, the attraction a new room of that type should reach (Task 3a): it is built at the
    /// smallest side whose attraction does, from <see cref="KeeperKnowledge"/>; e.g. a Library for four
    /// Warlocks. Without knowledge of the room, ignored.</summary>
    public IReadOnlyDictionary<RoomTypeId, int> AttractionTargets { get; init; } = new Dictionary<RoomTypeId, int>();

    /// <summary>Per room type, the floor slots a new room should have (Task 3a): a Library holds two research
    /// books per slot and needs a free one for each upgrade, so its target follows the level's research.</summary>
    public IReadOnlyDictionary<RoomTypeId, int> SlotTargets { get; init; } = new Dictionary<RoomTypeId, int>();

    /// <summary>Per listed room type, the tiles it should grow to (Task 3a); e.g. a Hatchery of 25 tiles,
    /// which Bile Demons and Giants need.</summary>
    public IReadOnlyDictionary<RoomTypeId, int> TileTargets { get; init; } = new Dictionary<RoomTypeId, int>();

    /// <summary>Which rooms get a square dug out when none is free (Task 3a).</summary>
    public RoomSites Sites { get; init; } = RoomSites.Oversized;

    /// <summary>The Combat Pit routine (stage 3 plan, Task 4; owner direction): at most this many own creatures
    /// fighting in an owned Combat Pit at once; 0 (the default) leaves the Pit alone. Two pair off; 1 is refused.</summary>
    public int PitFighters { get; init; }

    /// <summary>The Imp scouting routine (stage 3 plan, Task 4; owner direction): 1 sends one own Imp at a time to
    /// look at unknown ground from unowned or neutral floor at the edge of what the player knows; 0 (the default) does not.</summary>
    public int Scouts { get; init; }

    /// <summary>The Imp creation routine (rule R8; owner direction, 2026-11-15): 1 casts Create Imp when the Imps are
    /// fewer than the work wants and the mana income pays the price back (the default; owner, 2026-11-17: "4 imps are never
    /// an optimal number, at least 10"); 0 never casts it.</summary>
    public int CreateImps { get; init; } = 1;

    /// <summary>R8: never more own Imps than this (owner: 15 to 20, so the bot does not spend everything on Imps).</summary>
    public int MaxImps { get; init; } = 16;

    /// <summary>R8: the Imps kept whatever the dig backlog, and the most bought while the Library is still to upgrade
    /// Create Imp (an upgraded cast makes a level 4 Imp, a plain one level 1). Never above <see cref="MaxImps"/>. Ten by the
    /// owner's rule (2026-11-17).</summary>
    public int MinImps { get; init; } = 10;

    /// <summary>R8: mana never spent on an Imp, kept for the spells that attack and defend (Heal 5000, Thunderbolt 6000,
    /// Call To Arms 10000).</summary>
    public int ManaReserve { get; init; } = 15000;

    /// <summary>R8: an Imp is bought only when its price is at most this many seconds of net mana income (orb income
    /// less upkeep). A heuristic: the price climbs 1500 with each Imp, so this sets how many the income carries.</summary>
    public int ImpPriceSeconds { get; init; } = 60;

    /// <summary>How near (tiles) an enemy in sight must be to home (or the Combat Pit) to stop the peaceful routines:
    /// sending creatures to rooms, the Pit, scouting. Further off it is ignored (Level 15a's hero Guards stand in their
    /// Prison nine tiles and more from the Heart all game, Q843).</summary>
    public int EnemyNearTiles { get; init; } = 8;

    /// <summary>The trap routine (defence plan D4): at most this many own traps and trap blueprints on the corridors
    /// attackers take to the Heart, bought only with gold above <see cref="UseRoomMinGold"/>. 0 places none.</summary>
    public int MaxTraps { get; init; } = 8;

    /// <summary>While breaching is held back, no tile within this many tiles of an enemy sighting is marked.</summary>
    public int EnemyClearanceTiles { get; init; } = 3;

    /// <summary>1 keeps the gold budget (economy and assault plan E2, owner R11: "enough for payday and training"); 0 spends
    /// as before the plan.</summary>
    public int GoldBudget { get; init; } = 1;

    /// <summary>The gold budget's margin: the gold kept is the coming payday's wage bill plus this percent. Below it the
    /// player grows no room, builds no extra room, buys no trap and trains nobody; growth and extra rooms must also leave
    /// it standing once paid for.</summary>
    public int GoldMarginPercent { get; init; } = 20;

    /// <summary>The Lair grows while fewer than this many beds would be left over for the non-workers (E3, owner R12:
    /// "keep track of available slots in the lair"); its hover shows one bed per tile (<c>LairRules.Capacity</c>). 0 grows
    /// it by <see cref="LairTilesPerCreature"/> as before the plan.</summary>
    public int FreeBeds { get; init; } = 2;

    /// <summary>The Hatchery grows once at least this percent of the non-workers (and two) show the hunger chicken on
    /// their flower (E3, owner R12). 100 never grows it for hunger.</summary>
    public int HungryPercent { get; init; } = 25;

    /// <summary>1 builds a second Lair or Hatchery when an outgrown one cannot grow beside itself (E3, owner R12: "expand
    /// it, create a second one"); 0 as before the plan.</summary>
    public int SecondRooms { get; init; } = 1;

    /// <summary>With no <see cref="UseRooms"/> named, an owned Training Room is used once gold is at least this and
    /// three wage bills (E2: idle gold trains the army; Level 4 ended with 92k unspent). 0 never.</summary>
    public int TrainGold { get; init; } = 30000;

    /// <summary>The trap assault (economy and assault plan A2; owner on Level 4: "drop several creatures near ... form a
    /// party of offensive creatures, not imps, and attack together those sentry traps"): this many fighters, lifted
    /// together and dropped together beside an enemy trap whose reach covers own ground or a dig mark. 0 never; at most
    /// the actions one decision may send (<see cref="CampaignPlayerOptions.MaxActionsPerDecision"/>), or it never goes.</summary>
    public int AssaultParty { get; init; } = 4;

    /// <summary>1 keeps drops, answers and (while other ground is left) dig marks out of enemy traps' reach (A2); 0 ignores
    /// enemy traps, as before the plan.</summary>
    public int AvoidTraps { get; init; } = 1;

    /// <summary>The whole-army push (economy and assault plan A4; owner on Level 4: "drop all my creatures to rush the
    /// lord"): an attack that holds its ground while the army shrinks is no longer answered one fighter at a time; the
    /// player gathers this many fit fighters (or two thirds of its fighters, never fewer than four) and drops them
    /// together. 0 answers every attack one at a time, as before the plan.</summary>
    public int RallyParty { get; init; } = 8;

    /// <summary>Recruiting (Level 5, 2026-11-17: the Portal turned every creature away for a full Workshop and Library while
    /// 50-90k gold lay idle): with a Portal owned, no new non-worker for a while and at least this much gold over the
    /// payday budget (with strategy.gold-budget 0 there is no budget, and only this floor and the paydays the gold must cover stand),
    /// the offered room that caps portal creatures with the fewest tiles is built or grown. 0 never does.</summary>
    public int RecruitGold { get; init; } = 20000;

    /// <summary>Every problem with this strategy, empty when it may be applied. With the map's size,
    /// a dig target outside it is one.</summary>
    public IReadOnlyList<string> Validate(int? mapWidth = null, int? mapHeight = null)
    {
        var problems = new List<string>();
        // One fighter has no one to pair with and would be lifted straight out again (the lone-victor rule).
        if (PitFighters is < 0 or 1) problems.Add($"PitFighters {PitFighters} is neither 0 nor 2 or more");
        if (Scouts is < 0 or > 1) problems.Add($"Scouts {Scouts} is not 0 or 1");
        if (CreateImps is < 0 or > 1) problems.Add($"CreateImps {CreateImps} is not 0 or 1");
        if (MaxImps <= 0) problems.Add($"MaxImps {MaxImps} is not positive");
        if (MinImps < 0) problems.Add($"MinImps {MinImps} is negative");   // above MaxImps, the cap wins
        if (ManaReserve < 0) problems.Add($"ManaReserve {ManaReserve} is negative");
        if (MaxTraps < 0) problems.Add($"MaxTraps {MaxTraps} is negative");
        if (ImpPriceSeconds <= 0) problems.Add($"ImpPriceSeconds {ImpPriceSeconds} is not positive");
        if (Rooms is null) problems.Add("Rooms is null");
        else if (Rooms.Distinct().Count() != Rooms.Count) problems.Add("Rooms names a room twice");
        if (RoomSide <= 0) problems.Add($"RoomSide {RoomSide} is not positive");
        if (MaxOutstandingTags <= 0) problems.Add($"MaxOutstandingTags {MaxOutstandingTags} is not positive");
        if (HomeGuardTiles < 0) problems.Add($"HomeGuardTiles {HomeGuardTiles} is negative");
        if (AdvanceTicks is < 1 or > MaxAdvanceTicks) problems.Add($"AdvanceTicks {AdvanceTicks} is outside 1 to {MaxAdvanceTicks}");
        if (TakeRooms is null) problems.Add("TakeRooms is null");
        if (TakeAt is null) problems.Add("TakeAt is null");
        else if (mapWidth is int takeWidth && mapHeight is int takeHeight
                 && TakeAt.Any(t => t.X < 0 || t.Y < 0 || t.X >= takeWidth || t.Y >= takeHeight)) problems.Add("TakeAt names a tile outside the map");
        if (ExtraRooms is null) problems.Add("ExtraRooms is null");
        else if (ExtraRooms.Distinct().Count() != ExtraRooms.Count) problems.Add("ExtraRooms names a room twice");
        if (UseRooms is null) problems.Add("UseRooms is null");
        else if (UseRooms.Distinct().Count() != UseRooms.Count) problems.Add("UseRooms names a room twice");
        if (LairTilesPerCreature < 0) problems.Add($"LairTilesPerCreature {LairTilesPerCreature} is negative");
        if (CreaturesPerHatcheryTile <= 0) problems.Add($"CreaturesPerHatcheryTile {CreaturesPerHatcheryTile} is not positive");
        if (TreasuryFullPercent is < 1 or > 100) problems.Add($"TreasuryFullPercent {TreasuryFullPercent} is outside 1 to 100");
        if (TilesPerWorkingCreature <= 0) problems.Add($"TilesPerWorkingCreature {TilesPerWorkingCreature} is not positive");
        if (UseRoomEveryTicks < 0) problems.Add($"UseRoomEveryTicks {UseRoomEveryTicks} is negative");
        if (UseRoomMinGold < 0) problems.Add($"UseRoomMinGold {UseRoomMinGold} is negative");
        if (EnemyClearanceTiles < 0) problems.Add($"EnemyClearanceTiles {EnemyClearanceTiles} is negative");
        if (EnemyNearTiles < 0) problems.Add($"EnemyNearTiles {EnemyNearTiles} is negative");
        if (GoldMarginPercent < 0) problems.Add($"GoldMarginPercent {GoldMarginPercent} is negative");
        if (GoldBudget is < 0 or > 1) problems.Add($"GoldBudget {GoldBudget} is not 0 or 1");
        if (SecondRooms is < 0 or > 1) problems.Add($"SecondRooms {SecondRooms} is not 0 or 1");
        if (FreeBeds < 0) problems.Add($"FreeBeds {FreeBeds} is negative");
        if (HungryPercent is < 1 or > 100) problems.Add($"HungryPercent {HungryPercent} is outside 1 to 100");
        if (TrainGold < 0) problems.Add($"TrainGold {TrainGold} is negative");
        if (AssaultParty < 0) problems.Add($"AssaultParty {AssaultParty} is negative");
        if (AvoidTraps is < 0 or > 1) problems.Add($"AvoidTraps {AvoidTraps} is not 0 or 1");
        if (RallyParty < 0) problems.Add($"RallyParty {RallyParty} is negative");
        if (RecruitGold < 0) problems.Add($"RecruitGold {RecruitGold} is negative");
        if (AttractionTargets is null) problems.Add("AttractionTargets is null");
        else foreach (KeyValuePair<RoomTypeId, int> t in AttractionTargets.Where(t => t.Value <= 0))
            problems.Add($"AttractionTargets[{t.Key}] {t.Value} is not positive");
        if (SlotTargets is null) problems.Add("SlotTargets is null");
        else foreach (KeyValuePair<RoomTypeId, int> t in SlotTargets.Where(t => t.Value <= 0))
            problems.Add($"SlotTargets[{t.Key}] {t.Value} is not positive");
        if (TileTargets is null) problems.Add("TileTargets is null");
        else foreach (KeyValuePair<RoomTypeId, int> t in TileTargets.Where(t => t.Value <= 0))
            problems.Add($"TileTargets[{t.Key}] {t.Value} is not positive");
        if (Dig == DigMode.Toward && DigTarget is null) problems.Add("Dig Toward needs a DigTarget");
        if (Dig != DigMode.Toward && DigTarget is not null) problems.Add($"DigTarget is set but Dig is {Dig}");
        if (DigTarget is TilePos target && mapWidth is int width && mapHeight is int height
            && (target.X < 0 || target.Y < 0 || target.X >= width || target.Y >= height))
            problems.Add($"DigTarget ({target.X},{target.Y}) is outside the {width}x{height} map");
        return problems;
    }
}
