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
    ];

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
