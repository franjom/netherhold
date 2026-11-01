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
            "Q520 as played 2026-11-01 (D:/DK2Evidence/runs/q520-corridors-2026-11-01): straight dead-end corridors north of the trap row, "
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
    ];

    private static string BaseOf(LevelBuilder b) => b.BaseName;

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
