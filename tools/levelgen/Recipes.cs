// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.LevelGen;

/// <summary>One test level: the shipped level it starts from, the name it is saved under, and the edits.
/// A recipe stays here once played, so the level behind a capture can be rebuilt byte for byte.</summary>
public sealed record Recipe(string Name, string Base, string Purpose, Action<LevelBuilder> Build);

public static class Recipes
{
    public static readonly IReadOnlyList<Recipe> All =
    [
        new("Q520Boulder", "Secret4",
            "Q520 first trial (2026-11-01): Secret4 unchanged but three Dwarves re-levelled. Open lava, hard to aim; superseded by Q520Corridors.",
            b => RelevelDwarves(b, move: false)),
        new("Q520Corridors", "Secret4",
            "Q520 as played 2026-11-01 (F:/DK2Evidence/studies/q520-corridors--2026-11-01--001/payload): straight dead-end corridors north of the trap row, "
            + "one Dwarf case per corridor. Keeps Secret4's boulder overrides (health 10000, creature damage 100%).",
            b => { Corridors(b); RelevelDwarves(b, move: true); }),
        new("Q520CorridorsGlobal", "Secret4",
            "Q520Corridors with Secret4's boulder overrides set back to GlobalVariables.kwd, so the numbers are campaign play's.",
            b =>
            {
                Corridors(b);
                RelevelDwarves(b, move: true);
                foreach (var id in new[] { VariableTypeId.BoulderInitialHealth, VariableTypeId.BoulderSpeedTilesPerSecond,
                             VariableTypeId.BoulderToCreatureDamagePercentage, VariableTypeId.BoulderSlapDamage })
                    if (!b.SetVariable(id, b.GlobalValue(id))) throw new InvalidOperationException($"{BaseOf(b)} has no {id} override");
            }),
        new("M7Traps", "Secret4",
            "M7 doors and traps in sealed zones, at campaign boulder values: boulder corridors through water and lava (Q520d), "
            + "two hero doors (Q520c) and two Sentries (Q520c); a Goblin four tiles from a Hero Sentry (Q362, Q513b/c) and one six "
            + "tiles from another (Q452); a Jack In The Box on the Imps' way to gold (Q524).",
            M7Traps),
        new("M7TrapFights", "Secret4",
            "Owner checks, 2026-11-02: three brave creatures each four tiles from a Hero Sentry, a level-10 Black Knight, a "
            + "level-10 Bile Demon and a fearless level-10 Vampire (Q362: does it swing at the revealed trap?); two hero "
            + "Boulder traps three tiles from a Troll, one on a Neutral Dirt Path like Level 20's (12,5), one on hero "
            + "ground (Q513a: only the second should roll); a Prison whose captive is carried away from a fight, for the "
            + "owner to sell during the carry (Q729, state 44's exit 4). Ended on its second world frame (2026-11-02): lost to Secret4's trigger 4, see M7TrapFights5.",
            b => M7TrapFights(b, Zone.All)),
        new("M7TF_Brave", "Secret4", "M7TrapFights' Q362 chambers alone (bisecting what ended M7TrapFights, 2026-11-02).", b => M7TrapFights(b, Zone.Brave)),
        new("M7TF_Boulders", "Secret4", "M7TrapFights' Q513a corridors alone (bisecting what ended M7TrapFights, 2026-11-02).", b => M7TrapFights(b, Zone.Boulders)),
        new("M7TF_Prison", "Secret4", "M7TrapFights' Q729 Prison and fight alone (bisecting what ended M7TrapFights, 2026-11-02).", b => M7TrapFights(b, Zone.Prison)),
        new("M7TrapFights2", "Secret4",
            "M7TrapFights with M7Traps' dead-end way out of the Heart (x 19, rows 23..27): each zone variant ended too, and a "
            + "Heart sealed on every side is what all three shared and M7Traps, which ran, did not.",
            b => M7TrapFights(b, Zone.All | Zone.HeartWay)),
        new("M7TrapFights3", "Secret4",
            "M7TrapFights2 ended the same way. Its Dwarves now keep Secret4's 5% health as M7Traps' did: at 100% the "
            + "Dwarf starts its SendToActionPoint job (objective 23) at once, which M7Traps' never did.",
            b => M7TrapFights(b, Zone.All | Zone.HeartWay | Zone.PlacedHealth)),
        new("M7TrapFights4", "Secret4",
            "M7TrapFights3 without the Boulder trap on the Neutral Dirt Path at (3,4), which no capture ever showed in the game.",
            b => M7TrapFights(b, Zone.All | Zone.HeartWay | Zone.PlacedHealth | Zone.NoDirtBoulder)),
        new("M7TrapFights5", "Secret4",
            "M7TrapFights as designed, plus a keeper Boulder trap sealed at (37,35). None of the builds above crashed: Secret4's "
            + "trigger 4 (type 28, comparison 3, TargetId 2) loses the level when the keeper owns no Boulder trap, and they "
            + "removed all of them, so DK2 ended each one on its second world frame. M7Traps kept five.",
            b => M7TrapFights(b, Zone.All | Zone.KeeperBoulder)),
        new("M7TrapFights6", "Secret4",
            "M7TrapFights5 with one of Secret4's own keeper Boulder traps moved to (37,35), not a copy of Alcatraz's record, "
            + "so the trap trigger 4 counts is the kind M7Traps kept.",
            b => M7TrapFights(b, Zone.All | Zone.KeeperBoulder | Zone.OwnBoulder)),
        // one owner check per level, so whatever ends a run names its scenario; each keeps M7TrapFights6's keeper trap
        new("M7Q362", "Secret4", "M7TrapFights6's Q362 chambers alone.",
            b => M7TrapFights(b, Zone.Brave | Zone.KeeperBoulder | Zone.OwnBoulder)),
        new("M7Q513a", "Secret4", "M7TrapFights6's Q513a Boulder corridors alone.",
            b => M7TrapFights(b, Zone.Boulders | Zone.KeeperBoulder | Zone.OwnBoulder)),
        new("M7Q729", "Secret4", "M7TrapFights6's Q729 Prison and fight alone.",
            b => M7TrapFights(b, Zone.Prison | Zone.KeeperBoulder | Zone.OwnBoulder)),
        new("M7Q729b", "Secret4",
            "M7Q729 with the captive's Flags2 bit 2 (DieInstantly, +0x3EA bit 11) cleared: Secret4 places its Dwarves with "
            + "Flags2 4, so M7Q729's was killed outright by its first lethal blow (2026-11-02) and never collapsed.",
            b => M7TrapFights(b, Zone.Prison | Zone.KeeperBoulder | Zone.OwnBoulder | Zone.Capturable)),
        new("M7Q729c", "Secret4",
            "M7Q729b selling its own Prison: the owner's Sell did nothing there (2026-11-02), so a type-39 area trigger "
            + "on the fight chamber (Good Dwarves in it == 0, once) runs action 22 on each Prison tile, which sells the "
            + "tile (0x004C5DB0, pay flag 0) and lays Claimed Path. The Dwarf leaves the chamber only in an Imp's arms.",
            b => M7TrapFights(b, Zone.Prison | Zone.KeeperBoulder | Zone.OwnBoulder | Zone.Capturable | Zone.AutoSell)),
        new("Q716FreeImps", "Small",
            "Q716 (2026-11-02): Small without its eight placed Imps, so keepers 3 and 4 are both below MinimumImpThreshold "
            + "when the Heart opening arms their deadlines on the same turn; native turns 38, 59, 80 and 101 should each "
            + "cast a free Imp for both. The capture's world seed shows whether each heading draw follows its own "
            + "keeper's angle draw or both angle draws.",
            WithoutImps),
        new("Q750CostNumbersBank", "Small",
            "Floating cost digits (floating-cost-numbers.md, Q744, Q750), played 2026-11-04: the CostNumbers field, "
            + "250000 starting gold and a 9x9 keeper Treasury painted at x 36..44, rows 22..30, so the Heart's 16000 and "
            + "81 x 3000 hold all of it.",
            b =>
            {
                CostNumbers(b);
                for (int y = 22; y <= 30; y++) for (int x = 36; x <= 44; x++) b.Paint(x, y, new MapTile(TreasuryTerrain, Keeper, 1, 0));
                b.SetStartingGold(Keeper, 250000);
            }),
        new("MPT21Dungeon", "mpd1",
            "The owner's My Pet Dungeon save MPT21.SAV (Data/Save, 2026-09-26) as a level, for Unity (2026-11-05): mpd1's map "
            + "repainted tile by tile from the save's grid, placed objects left in solid rock removed, and 250000 starting "
            + "gold (the Heart's 16000 and the save's 115 Treasury tiles x 3000 hold it). The save's creatures and objects "
            + "are undecoded (Q182), so mpd1's own placed things stay.",
            b => FromSave(b, "MPT21.SAV", 250000)),
        new("Q498Spells", "Small",
            "Q498 (2026-11-11): Chicken and Inferno, cast by the owner with the mana cheat. Keeper 3's sealed Heart and four "
            + "sealed chambers of its Claimed Path (both spells need the target on the caster's land): A, a keeper-4 Goblin "
            + "with a corridor to a keeper-4 Hatchery (the chickened walk); B, a keeper-4 Goblin with no way out (the 60 s "
            + "without a fight); C, Prince Felix (immune: the mana is still spent); D, a Knight to target with two Dwarves "
            + "one tile off and one three tiles off (Inferno's one-tile zone, 100 a tick for 30 ticks, and its recast timeout).",
            SpellChambers),
        new("Q498Spells2", "Small",
            "Q498Spells without the cheat, which crashed DK2 when typed there (2026-11-11): Small's LevelRating and "
            + "AverageTime overrides become DungeonHeartManaGenerationIncreasePerSecond and MaximumManaGainPerSecond, "
            + "60000 each (u16 words Level_InitTriggersAndStart adds to player +0x10E6 and +0x10DE, 0x0050BBBC and "
            + "0x0050BA91), so the Heart refills mana to the 200000 ceiling within seconds; keeper 4's AI flag is off "
            + "so it does not spend the same mana near the chambers.",
            b =>
            {
                SpellChambers(b);
                b.SetAiFlag(Rival, false);
                if (!b.RepointVariable(VariableTypeId.LevelRating, VariableTypeId.DungeonHeartManaGenerationIncreasePerSecond, 60000)
                    || !b.RepointVariable(VariableTypeId.AverageTime, VariableTypeId.MaximumManaGainPerSecond, 60000))
                    throw new InvalidOperationException("Small no longer carries LevelRating and AverageTime overrides");
            }),
        new("Q498Spells3", "Small",
            "Q498Spells2 with the spells: Small makes only Possession (2) and Create Imp (4) available (VARIABLES kind 5), "
            + "so the owner had neither Chicken nor Inferno (2026-11-11). Keeper 4's two spell rows, unused with its AI "
            + "off, become keeper 3's Inferno (9) and Chicken (11) at availability 3.",
            b =>
            {
                Recipes.All.Single(r => r.Name == "Q498Spells2").Build(b);
                if (!b.RepointAvailability(AvailabilityKind.Spell, Rival, 2, Keeper, 9, 3)
                    || !b.RepointAvailability(AvailabilityKind.Spell, Rival, 4, Keeper, 11, 3))
                    throw new InvalidOperationException("Small no longer carries keeper 4's spell rows 2 and 4");
            }),
        new("Q498Spells4", "Small",
            "Q498Spells3 with chamber A's Goblin held: in the Q498Spells3 run (2026-11-11) it walked into keeper 4's "
            + "Hatchery (room 1127, under the owner's fog) by turn 50, before any cast. A locked keeper-3 Wooden Door "
            + "(DoorThing.Flags bit 0, doors-and-traps.md) at (23,4) holds it in the chamber until the owner unlocks it "
            + "after the Chicken.",
            b =>
            {
                Recipes.All.Single(r => r.Name == "Q498Spells3").Build(b);
                var (door, doorImage) = b.Template<DoorThing>(d => d.TriggerId == 0 && d.DoorId == WoodenDoor);
                b.InsertThing(door with { X = 23, Y = 4, DoorId = WoodenDoor, PlayerId = Keeper, Flags = 1 }, doorImage);
            }),
    ];

    private const byte HatcheryTerrain = 13;
    private const byte Knight = 13, PrinceFelix = 26;

    /// <summary>The Q498 layout on Small: every tile impenetrable rock but keeper 3's walled Heart, keeper 4's, and
    /// four 3-tile-high chambers of keeper 3's Claimed Path along rows 3..5 and 9..11, none joined to a Heart.</summary>
    private static void SpellChambers(LevelBuilder b)
    {
        LevelMap map = b.Map;
        var rock = new MapTile(ImpenetrableRock, Neutral, 1, 0);
        var path = new MapTile(ClaimedPath, Keeper, 1, 0);
        var hatchery = new MapTile(HatcheryTerrain, Rival, 1, 0);
        // A: chamber x 20..22 with a corridor along row 4 to keeper 4's Hatchery at x 28..30, rows 3..5.
        // B: x 34..36, rows 3..5.  C: x 40..42, rows 3..5.  D: x 20..26, rows 9..11.
        static bool Floor(int x, int y) =>
            (x is >= 20 and <= 22 && y is >= 3 and <= 5) || (x is >= 23 and <= 27 && y == 4)
            || (x is >= 34 and <= 36 && y is >= 3 and <= 5)
            || (x is >= 40 and <= 42 && y is >= 3 and <= 5)
            || (x is >= 20 and <= 26 && y is >= 9 and <= 11);
        static bool Hatchery(int x, int y) => x is >= 28 and <= 30 && y is >= 3 and <= 5;
        for (int y = 1; y < map.Height - 1; y++)
            for (int x = 1; x < map.Width - 1; x++)
                if (Floor(x, y)) b.Paint(x, y, path);
                else if (Hatchery(x, y)) b.Paint(x, y, hatchery);
                else if (map[x, y].TerrainId is not (9 or 14)) b.Paint(x, y, rock);
        // One sealed tile of the heroes' own, so the remake's harness makes player 1 too (levelgen README).
        b.Paint(44, 28, new MapTile(ClaimedPath, Heroes, 1, 0));

        var things = b.Things;
        for (int i = things.Count - 1; i >= 0; i--)
            if (things[i] is ObjectThing o && map[o.X, o.Y].TerrainId is not (9 or 14) && !Floor(o.X, o.Y)) b.RemoveThing(i);

        var (keeper, keeperImage) = b.Template<KeeperCreatureThing>(c => c.TriggerId == 0 && c.PlayerId == Rival && c.CreatureId == Imp);
        void Goblin4(int x, int y) => b.InsertThing(keeper with
        {
            X = x, Y = y, CreatureId = Goblin, PlayerId = Rival, Level = 4, InitialHealth = 100, ObjectiveTargetActionPointId = 0,
        }, keeperImage);
        Goblin4(21, 4);
        Goblin4(35, 4);

        var (hero, heroImage) = b.Template<GoodCreatureThing>(g => g.CreatureId == Dwarf);
        void Hero(int x, int y, byte id, byte level) => b.InsertThing(hero with
        {
            X = x, Y = y, CreatureId = id, Level = level, InitialHealth = 100, ObjectiveTargetActionPointId = 0,
            TriggerId = 0, PartyId = 0, Flags2 = (byte)(hero.Flags2 & ~4),
        }, heroImage);
        Hero(41, 4, PrinceFelix, 4);
        Hero(21, 10, Knight, 10);
        Hero(22, 10, Dwarf, 4);
        Hero(21, 11, Dwarf, 4);
        Hero(24, 10, Dwarf, 4);
    }

    private const byte Rival = 4;

    /// <summary>Repaints the base level from a DK2 save of that level (terrain, owner and flags per tile)
    /// and drops placed objects now inside solid rock or wall.</summary>
    private static void FromSave(LevelBuilder b, string saveFile, int startingGold)
    {
        string path = Path.Combine(DK2.Data.Save.Dk2SaveReader.SaveDirectory(b.Install), saveFile);
        DK2.Data.Save.SavedGame save = DK2.Data.Save.Dk2SaveReader.Load(b.Install, path)
            ?? throw new InvalidOperationException($"{saveFile} names a level this install has no map for");
        if (!save.Header.LevelName.Equals(b.BaseName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{saveFile} is level {save.Header.LevelName}, not {b.BaseName}");
        LevelMap from = save.Map, to = b.Map;
        if (from.Width != to.Width || from.Height != to.Height) throw new InvalidOperationException("grid size differs");
        for (int y = 0; y < from.Height; y++)
            for (int x = 0; x < from.Width; x++)
                if (from[x, y] != to[x, y]) b.Paint(x, y, from[x, y]);
        LevelMap painted = b.Map;
        var things = b.Things;
        for (int i = things.Count - 1; i >= 0; i--)
            if (things[i] is ObjectThing o && painted[o.X, o.Y].TerrainId is ImpenetrableRock or 2 or 9) b.RemoveThing(i);
        b.SetStartingGold(Keeper, startingGold);
    }

    private const byte TreasuryTerrain = 10;

    /// <summary>The Q750 layout: keeper 3's Heart opens east at (15,11..13) onto a 28x29 Claimed Path field
    /// (x 17..44, rows 2..30) for room builds and drag sales, with four level-1 Goblins for a Temple. All other
    /// rock is impenetrable, which seals keeper 4 in its corner.</summary>
    private static void CostNumbers(LevelBuilder b)
    {
        LevelMap map = b.Map;
        var rock = new MapTile(ImpenetrableRock, Neutral, 1, 0);
        var path = new MapTile(ClaimedPath, Keeper, 1, 0);
        static bool Field(int x, int y) => (x is >= 17 and <= 44 && y is >= 2 and <= 30) || (x is 15 or 16 && y is >= 11 and <= 13);
        for (int y = 1; y < map.Height - 1; y++)
            for (int x = 1; x < map.Width - 1; x++)
                if (Field(x, y)) b.Paint(x, y, path);
                else if (map[x, y].TerrainId is not (9 or 14)) b.Paint(x, y, rock);

        var things = b.Things;
        for (int i = things.Count - 1; i >= 0; i--)
            if (things[i] is ObjectThing o && Field(o.X, o.Y)) b.RemoveThing(i);

        var (creature, creatureImage) = b.Template<KeeperCreatureThing>(c => c.TriggerId == 0 && c.PlayerId == Keeper && c.CreatureId == Imp);
        foreach (int y in new[] { 10, 12, 14, 16 })
            b.InsertThing(creature with
            {
                X = 20, Y = y, CreatureId = Goblin, PlayerId = Keeper, Level = 1, InitialHealth = 100, ObjectiveTargetActionPointId = 0,
            }, creatureImage);
    }

    private static void WithoutImps(LevelBuilder b)
    {
        var things = b.Things;
        for (int i = things.Count - 1; i >= 0; i--)
            if (things[i] is KeeperCreatureThing { CreatureId: 1 }) b.RemoveThing(i);
    }

    [Flags]
    private enum Zone { Brave = 1, Boulders = 2, Prison = 4, All = 7, HeartWay = 8, PlacedHealth = 16, NoDirtBoulder = 32, KeeperBoulder = 64, OwnBoulder = 128, Capturable = 256, AutoSell = 512 }

    private static string BaseOf(LevelBuilder b) => b.BaseName;

    private const byte Keeper = 3, Heroes = 1, Neutral = 2;
    private const byte ImpenetrableRock = 1, Water = 4, Lava = 5, Gold = 6, ClaimedPath = 8;
    private const byte Imp = 1, Goblin = 7, Dwarf = 14;
    private const byte BoulderTrap = 2, SentryTrap = 3, HeroSentry = 14, JackInTheBox = 16;
    private const byte WoodenDoor = 1, SteelDoor = 2;

    /// <summary>The M7Traps layout; every zone is sealed from the others by impenetrable rock.<list type="bullet">
    /// <item>Boulders (not joined to the Heart, so none can roll into it): Boulder traps at x 9..17 odd on
    /// row 22, the cross path on row 20 (x 9..17), corridors north to row 4 at x 9, 13 and 17.
    /// x 9: water rows 14..16, lava rows 6..7. x 13: a hero Wooden Door at row 15 and a hero Steel Door
    /// at row 8. x 17: the keeper's own Sentry at row 15 and a Hero Sentry at row 9.</item>
    /// <item>Goblins: chamber x 24..36, rows 3..12, Hero Sentry at (30,5), Goblin at (30,9). Chamber x 24..36,
    /// rows 15..19, Hero Sentry at (25,17), Goblin at (31,17).</item>
    /// <item>Jack: the Heart's way out (x 19, rows 23..27) joins row 25 west to x 3; Jack In The Box (heroes')
    /// at (10,25); gold at x 1..2, rows 24..26; two Imps at (19,24) and (18,25).</item>
    /// <item>One Dwarf sealed in a one-tile pocket at (37,37), so Secret4's "no Dwarves left" win never fires;
    /// Secret4's 400 s limit (action 23 of trigger 65534) becomes 3600 s.</item>
    /// </list></summary>
    private static void M7Traps(LevelBuilder b)
    {
        LevelMap map = b.Map;
        var tiles = new MapTile?[map.Width, map.Height];
        void Set(int x, int y, byte terrain, byte owner) => tiles[x, y] = new MapTile(terrain, owner, 1, 0);
        void Path(int x, int y, byte owner = Keeper) => Set(x, y, ClaimedPath, owner);

        // boulders
        for (int x = 9; x <= 17; x++) Path(x, 20);
        for (int x = 9; x <= 17; x += 2) { Path(x, 21); Path(x, 22); }
        foreach (int cx in new[] { 9, 13, 17 }) for (int y = 4; y <= 19; y++) Path(cx, y);
        for (int y = 14; y <= 16; y++) Set(9, y, Water, Neutral);
        for (int y = 6; y <= 7; y++) Set(9, y, Lava, Neutral);
        Path(13, 15, Heroes); Path(13, 8, Heroes); Path(17, 9, Heroes);
        // goblins
        for (int x = 24; x <= 36; x++) { for (int y = 3; y <= 12; y++) Path(x, y); for (int y = 15; y <= 19; y++) Path(x, y); }
        Path(30, 5, Heroes); Path(25, 17, Heroes);
        // jack
        for (int y = 23; y <= 27; y++) Path(19, y);
        for (int x = 3; x <= 18; x++) Path(x, 25);
        Path(10, 25, Heroes);
        for (int y = 24; y <= 26; y++) { Set(1, y, Gold, Neutral); Set(2, y, Gold, Neutral); }
        // the Dwarf's pocket
        Path(37, 37, Heroes);

        var rock = new MapTile(ImpenetrableRock, Neutral, 1, 0);
        for (int y = 1; y < map.Height - 1; y++)
            for (int x = 1; x < map.Width - 1; x++)
                if (map[x, y].TerrainId is not (9 or 14)) b.Paint(x, y, tiles[x, y] ?? rock);

        // things: keep the five western Boulder traps and one Dwarf, drop the rest of Secret4's
        var things = b.Things;
        bool keptDwarf = false;
        int dwarfPoint = -1;
        for (int i = things.Count - 1; i >= 0; i--)
        {
            switch (things[i])
            {
                case TrapThing t when t.TrapId == BoulderTrap && t.X <= 17: break;
                case TrapThing: b.RemoveThing(i); break;
                case GoodCreatureThing g when !keptDwarf:
                    keptDwarf = true;
                    dwarfPoint = g.ObjectiveTargetActionPointId;
                    b.EditThing<GoodCreatureThing>(i, r => r with { X = 37, Y = 37 });
                    break;
                case GoodCreatureThing: b.RemoveThing(i); break;
            }
        }
        things = b.Things;
        for (int i = 0; i < things.Count; i++)
            if (things[i] is ActionPointThing ap && ap.ActionPointId == dwarfPoint)
                b.EditThing<ActionPointThing>(i, r => r with { StartX = 37, StartY = 37, EndX = 37, EndY = 37 });

        var (trap, trapImage) = b.Template<TrapThing>(t => t.TrapId == HeroSentry);
        void Trap(int x, int y, byte id, byte owner) => b.InsertThing(trap with { X = x, Y = y, TrapId = id, PlayerId = owner }, trapImage);
        Trap(17, 15, SentryTrap, Keeper);
        Trap(17, 9, HeroSentry, Heroes);
        Trap(30, 5, HeroSentry, Heroes);
        Trap(25, 17, HeroSentry, Heroes);
        Trap(10, 25, JackInTheBox, Heroes);

        var (door, doorImage) = b.Template<DoorThing>(d => d.TriggerId == 0 && d.DoorId == WoodenDoor);
        b.InsertThing(door with { X = 13, Y = 15, DoorId = WoodenDoor, PlayerId = Heroes }, doorImage);
        b.InsertThing(door with { X = 13, Y = 8, DoorId = SteelDoor, PlayerId = Heroes }, doorImage);

        var (creature, creatureImage) = b.Template<KeeperCreatureThing>(c => c.TriggerId == 0 && c.PlayerId == Keeper && c.CreatureId == Imp);
        void Creature(int x, int y, byte id) => b.InsertThing(creature with
        {
            X = x, Y = y, CreatureId = id, PlayerId = Keeper, Level = 1, InitialHealth = 100, ObjectiveTargetActionPointId = 0,
        }, creatureImage);
        Creature(30, 9, Goblin);
        Creature(31, 17, Goblin);
        Creature(19, 24, Imp);
        Creature(18, 25, Imp);

        b.SetTriggerParameter(65534, "Value", 3600);
        foreach (var id in new[] { VariableTypeId.BoulderInitialHealth, VariableTypeId.BoulderSpeedTilesPerSecond,
                     VariableTypeId.BoulderToCreatureDamagePercentage, VariableTypeId.BoulderSlapDamage })
            b.SetVariable(id, b.GlobalValue(id));
    }

    private const byte DirtPath = 3, PrisonTerrain = 20;
    private const byte Troll = 10, BileDemon = 3, Vampire = 8, BlackKnight = 22;

    /// <summary>The M7TrapFights layout; every zone is sealed from the others and from the Heart.<list type="bullet">
    /// <item>Q362: three chambers x 24..36, rows 3..7, 9..13 and 15..19; a Hero Sentry at x 25 and a level-10
    /// creature at x 29 on the middle row (5, 11, 17): Black Knight, Bile Demon, Vampire.</item>
    /// <item>Q513a: corridors x 3 and x 7, rows 3..11; a hero Boulder trap on row 4 of each, a Troll on row 7.
    /// (3,4) is a Neutral Dirt Path as Level 20's (12,5) is; (7,4) is hero Claimed Path.</item>
    /// <item>Q729: a 3x3 keeper Prison at x 2..4, rows 30..32, its door row 31 joined east by a corridor to a
    /// chamber x 11..14, rows 29..33, with a level-4 Bile Demon at (12,31), a level-1 Dwarf at (14,31) and two
    /// Imps at (6,31) and (7,31).</item>
    /// <item>One Dwarf sealed at (37,37) so Secret4's "no Dwarves left" win never fires; its 400 s limit is 3600 s.</item>
    /// </list></summary>
    private static void M7TrapFights(LevelBuilder b, Zone zones)
    {
        LevelMap map = b.Map;
        var tiles = new MapTile?[map.Width, map.Height];
        void Set(int x, int y, byte terrain, byte owner) => tiles[x, y] = new MapTile(terrain, owner, 1, 0);
        void Path(int x, int y, byte owner = Keeper) => Set(x, y, ClaimedPath, owner);

        bool brave = zones.HasFlag(Zone.Brave), boulders = zones.HasFlag(Zone.Boulders), prison = zones.HasFlag(Zone.Prison);
        // Q362 chambers
        if (brave)
        {
            foreach (int top in new[] { 3, 9, 15 })
                for (int y = top; y <= top + 4; y++)
                    for (int x = 24; x <= 36; x++) Path(x, y);
            foreach (int row in new[] { 5, 11, 17 }) Path(25, row, Heroes);
        }
        // Q513a corridors
        if (boulders)
        {
            foreach (int cx in new[] { 3, 7 }) for (int y = 3; y <= 11; y++) Path(cx, y);
            if (!zones.HasFlag(Zone.NoDirtBoulder)) Set(3, 4, DirtPath, Neutral);
            Path(7, 4, Heroes);
        }
        // Q729: the Prison, its corridor and the fight
        if (prison)
        {
            for (int y = 30; y <= 32; y++) for (int x = 2; x <= 4; x++) Set(x, y, PrisonTerrain, Keeper);
            for (int x = 5; x <= 10; x++) Path(x, 31);
            for (int y = 29; y <= 33; y++) for (int x = 11; x <= 14; x++) Path(x, y);
        }
        // the Heart's gap at (19,27) and a dead end north to row 23, as M7Traps has
        if (zones.HasFlag(Zone.HeartWay)) for (int y = 23; y <= 27; y++) Path(19, y);
        // the Dwarf's pocket
        Path(37, 37, Heroes);
        // the keeper Boulder trap's pocket: Secret4's trigger 4 loses the level when the keeper owns none
        if (zones.HasFlag(Zone.KeeperBoulder)) Path(37, 35);

        var rock = new MapTile(ImpenetrableRock, Neutral, 1, 0);
        for (int y = 1; y < map.Height - 1; y++)
            for (int x = 1; x < map.Width - 1; x++)
                if (map[x, y].TerrainId is not (9 or 14)) b.Paint(x, y, tiles[x, y] ?? rock);

        // things: two of Secret4's Dwarves (the pocket's and the captive's); of its traps, with OwnBoulder, one
        // keeper Boulder trap moved into its pocket
        bool keeperBoulder = zones.HasFlag(Zone.OwnBoulder);
        var dwarves = new Queue<(int X, int Y, byte Level)>(prison ? [(37, 37, 1), (14, 31, 1)] : [(37, 37, 1)]);
        var points = new Dictionary<int, (int X, int Y)>();
        var (boulder, boulderImage) = b.Template<TrapThing>(t => t.TrapId == BoulderTrap);
        var (sentry, sentryImage) = b.Template<TrapThing>(t => t.TrapId == HeroSentry);
        var things = b.Things;
        for (int i = things.Count - 1; i >= 0; i--)
        {
            switch (things[i])
            {
                case TrapThing t when keeperBoulder && t.TrapId == BoulderTrap && t.PlayerId == Keeper:
                    keeperBoulder = false;
                    b.EditThing<TrapThing>(i, r => r with { X = 37, Y = 35 });
                    break;
                case TrapThing: b.RemoveThing(i); break;
                case GoodCreatureThing g when dwarves.Count > 0:
                    var (x, y, level) = dwarves.Dequeue();
                    b.EditThing<GoodCreatureThing>(i, r => zones.HasFlag(Zone.PlacedHealth)
                        ? r with { X = x, Y = y, Level = level }
                        : r with { X = x, Y = y, Level = level, InitialHealth = 100 });
                    // Flags2 bit 2 is DieInstantly (creature-death.md): the captive must collapse to be carried
                    if (zones.HasFlag(Zone.Capturable) && (x, y) == (14, 31))
                        b.EditThing<GoodCreatureThing>(i, r => r with { Flags2 = (byte)(r.Flags2 & ~4) });
                    points[g.ObjectiveTargetActionPointId] = (x, y);
                    break;
                case GoodCreatureThing: b.RemoveThing(i); break;
            }
        }
        things = b.Things;
        for (int i = 0; i < things.Count; i++)
            if (things[i] is ActionPointThing ap && points.TryGetValue(ap.ActionPointId, out var to))
                b.EditThing<ActionPointThing>(i, r => r with { StartX = to.X, StartY = to.Y, EndX = to.X, EndY = to.Y });

        if (brave)
            foreach (int row in new[] { 5, 11, 17 })
                b.InsertThing(sentry with { X = 25, Y = row, TrapId = HeroSentry, PlayerId = Heroes }, sentryImage);
        if (zones.HasFlag(Zone.KeeperBoulder) && !zones.HasFlag(Zone.OwnBoulder))
            b.InsertThing(boulder with { X = 37, Y = 35, TrapId = BoulderTrap, PlayerId = Keeper }, boulderImage);
        if (boulders)
            foreach (int cx in zones.HasFlag(Zone.NoDirtBoulder) ? new[] { 7 } : new[] { 3, 7 })
                b.InsertThing(boulder with { X = cx, Y = 4, TrapId = BoulderTrap, PlayerId = Heroes }, boulderImage);

        var (creature, creatureImage) = b.Template<KeeperCreatureThing>(c => c.TriggerId == 0 && c.PlayerId == Keeper && c.CreatureId == Imp);
        void Creature(int x, int y, byte id, byte level) => b.InsertThing(creature with
        {
            X = x, Y = y, CreatureId = id, PlayerId = Keeper, Level = level, InitialHealth = 100, ObjectiveTargetActionPointId = 0,
        }, creatureImage);
        if (brave)
        {
            Creature(29, 5, BlackKnight, 10);
            Creature(29, 11, BileDemon, 10);
            Creature(29, 17, Vampire, 10);
        }
        if (boulders)
        {
            Creature(3, 7, Troll, 1);
            Creature(7, 7, Troll, 1);
        }
        if (prison)
        {
            Creature(12, 31, BileDemon, 4);
            Creature(6, 31, Imp, 1);
            Creature(7, 31, Imp, 1);
        }

        b.SetTriggerParameter(65534, "Value", 3600);
        if (zones.HasFlag(Zone.AutoSell)) SellPrisonOnceCarriedOut(b);
    }

    /// <summary>A type-39 watch on Secret4's spare action point 9, moved over the fight chamber (x 11..14,
    /// rows 29..33): once no Good Dwarf stands in it, action 22 turns each Prison tile to the keeper's
    /// Claimed Path, selling it first (trigger-action-arms.md, action 22).</summary>
    private static void SellPrisonOnceCarriedOut(LevelBuilder b)
    {
        // a negative id (0x8000 and up as a short) is an action node, a positive one a condition
        const ushort Watch = 100, FirstSale = 65000;
        const byte AreaCount = 39, AlterTerrain = 22, Equal = 3, Creatures = 0;
        static Dictionary<string, long> With(IReadOnlyDictionary<string, long> p, params (string Name, long Value)[] set)
        {
            var d = p.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var (name, value) in set)
                if (!d.ContainsKey(name)) throw new ArgumentException($"no trigger parameter {name}");
                else d[name] = value;
            return d;
        }

        var (watch, watchImage) = b.TriggerTemplate(213, AreaCount);
        b.InsertTrigger(watch with
        {
            TriggerId = Watch, NextTriggerId = 0, ChildTriggerId = 0, RepeatCount = 1,
            Parameters = With(watch.Parameters, ("Comparison", Equal), ("PlayerId", Heroes), ("TypeId", Dwarf),
                ("ThingType", Creatures), ("Value", 0)),
        }, watchImage);

        var (sale, saleImage) = b.TriggerTemplate(214, AlterTerrain);
        ushort id = FirstSale;
        for (int y = 30; y <= 32; y++)
            for (int x = 2; x <= 4; x++)
                b.InsertTrigger(sale with
                {
                    TriggerId = id++, NextTriggerId = 0, ChildTriggerId = 0, RepeatCount = 0,
                    // action 22's position is one-based
                    Parameters = With(sale.Parameters, ("PosX", x + 1), ("PosY", y + 1), ("TerrainId", ClaimedPath), ("PlayerId", Keeper)),
                }, saleImage);
        for (ushort k = FirstSale; k < id - 1; k++) b.LinkTrigger(k, (ushort)(k + 1), 0);
        b.LinkTrigger(Watch, 0, FirstSale);

        var things = b.Things;
        int point = Enumerable.Range(0, things.Count).Single(i => things[i] is ActionPointThing { ActionPointId: 9 });
        b.EditThing<ActionPointThing>(point, r => r with { StartX = 11, StartY = 29, EndX = 14, EndY = 33 });
        b.SetActionPointRoot(point, Watch);
    }

    /// <summary>Secret4's lava becomes impenetrable rock; open: the cross path at row 20, the trap stubs at
    /// odd x on rows 21-22, the way to the Heart at x 19, and six corridors north (x 9, 13, 17, 21 to row 4;
    /// 25 to row 8, then east to x 35; 29 to row 15). The Heart room keeps its walls.</summary>
    private static void Corridors(LevelBuilder b)
    {
        LevelMap map = b.Map;
        var rock = new MapTile(1, 2, 1, 0);   // Impenetrable Rock, neutral
        var path = new MapTile(8, 3, 1, 0);   // Claimed Path, player 3 (the human keeper)
        var open = new bool[map.Width, map.Height];
        for (int x = 9; x <= 29; x++) open[x, 20] = true;
        for (int x = 9; x <= 29; x += 2) open[x, 21] = open[x, 22] = true;
        for (int y = 23; y <= 27; y++) open[19, y] = true;
        foreach (var (cx, top) in new[] { (9, 4), (13, 4), (17, 4), (21, 4), (25, 8), (29, 15) })
            for (int y = top; y <= 19; y++) open[cx, y] = true;
        for (int x = 26; x <= 35; x++) open[x, 8] = true;
        for (int y = 1; y < map.Height - 1; y++)
            for (int x = 1; x < map.Width - 1; x++)
                if (map[x, y].TerrainId is not (9 or 14)) b.Paint(x, y, open[x, y] ? path : rock);
    }

    /// <summary>Three of Secret4's nine level-1, 5%-health Dwarves get the Q520 cases (health is a
    /// percentage of the level-scaled maximum, placed-things.md section 3); with <paramref name="move"/>
    /// they go to corridors 13, 17 and 21 with their action points, and the other six are removed.</summary>
    private static void RelevelDwarves(LevelBuilder b, bool move)
    {
        var cases = new Dictionary<(int, int), (int X, int Y, byte Level, int Health)>
        {
            [(36, 16)] = (13, 5, 1, 100),   // 1000 Hp
            [(3, 13)] = (17, 5, 10, 63),    // 3780 of 6000 Hp
            [(33, 10)] = (21, 5, 10, 100),  // 6000 Hp
        };
        var points = new Dictionary<int, (int X, int Y)>();
        var things = b.Things;
        for (int i = things.Count - 1; i >= 0; i--)
        {
            if (things[i] is not GoodCreatureThing g) continue;
            if (!cases.TryGetValue((g.X, g.Y), out var c))
            {
                if (move) b.RemoveThing(i);
                continue;
            }
            var (x, y) = move ? (c.X, c.Y) : (g.X, g.Y);
            b.EditThing<GoodCreatureThing>(i, r => r with { X = x, Y = y, Level = c.Level, InitialHealth = c.Health });
            points[g.ObjectiveTargetActionPointId] = (x, y);
        }
        if (!move) return;
        things = b.Things;
        for (int i = 0; i < things.Count; i++)
            if (things[i] is ActionPointThing ap && points.TryGetValue(ap.ActionPointId, out var to))
                b.EditThing<ActionPointThing>(i, r => r with { StartX = to.X, StartY = to.Y, EndX = to.X, EndY = to.Y });
    }
}
