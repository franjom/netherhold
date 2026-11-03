// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The terrain ambience <c>EffectGrid_UpdateAroundListener</c> (<c>0x0044E2A0</c>) feeds the sound service: an 11-by-11 grid of the tiles
/// around the listener, each cell the sound category of its tile's terrain (definition <c>+0x195</c>) when the tile is explored and none when not.
/// The service's ambient pass (<c>0x00609480</c> builds an emitter per category the grid holds whose map has group <c>0x2EA</c>; <c>0x00609A30</c> asks
/// that group, and <c>0x2EB</c> where the sound quality allows two, with the emitter's handle and replays it at the emitter's point) is the player's
/// affair; this type is the grid and the emitters it implies. Presentation only: it reads the world. Deviations (docs/research/sound-effects-continuous.md
/// section 4): the emitter's point is the mean of its cells' centres, where DK2 weights each cell by tables (<c>0x006C6F98</c>) read here only as far as
/// their use; the Temple's waterless category (<c>+0x199</c>, chosen when the room's <c>+0x61</c> is zero, unread) and the first-person category
/// (<c>+0x19D</c>) are not asked.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects-continuous.md#4-terrain-ambience-grid-and-first-person")]
public static class AmbienceGrid
{
    public const int Side = 11;
    private const int Reach = 5;
    private const int MaxEmitters = 2;

    /// <summary>An ambient emitter: the category it plays in, and its point in the view's space, <c>DungeonScene.Ground</c>'s (x, height, -y) in tiles.</summary>
    public readonly record struct Emitter(string Category, Vector3 Position);

    /// <summary>The 121 cells, row by row from the listener's tile less five in y and in x; an empty string is a cell with none.</summary>
    public static string[] Cells(WorldState world, GameDatabase database, PlayerId viewer, TilePos listener)
    {
        var cells = new string[Side * Side];
        for (int row = 0; row < Side; row++)
            for (int column = 0; column < Side; column++)
            {
                int x = listener.X - Reach + column, y = listener.Y - Reach + row;
                cells[(row * Side) + column] = world.Contains(new TilePos(x, y)) && world.Tile(x, y).IsExploredBy(viewer)
                    ? database.TerrainById(world.Tile(x, y).TerrainId).SoundCategory : "";
            }
        return cells;
    }

    /// <summary>An emitter for each of the two heaviest categories in <paramref name="cells"/> (by cell count) among those <paramref name="hasAmbience"/> says have the ambient group.</summary>
    public static List<Emitter> Emitters(string[] cells, TilePos listener, Func<string, bool> hasAmbience)
    {
        var emitters = new List<Emitter>();
        var seen = new HashSet<string>();
        var counts = new List<int>();
        for (int i = 0; i < cells.Length; i++)
        {
            string category = cells[i];
            if (category.Length == 0 || !seen.Add(category) || !hasAmbience(category)) continue;
            float x = 0, y = 0;
            int count = 0;
            for (int j = i; j < cells.Length; j++)
            {
                if (cells[j] != category) continue;
                x += listener.X - Reach + (j % Side) + .5f;
                y += listener.Y - Reach + (j / Side) + .5f;
                count++;
            }
            emitters.Add(new Emitter(category, new Vector3(x / count, 0f, -y / count)));
            counts.Add(count);
        }
        // 0x00609CE0 pops the best two records from a heap ordered by tile count and weight, and the pass keeps those categories only (the loop bound
        // at +0x28 is unread but at most two). The comparator and its tie-break are unread: ours ranks by cell count, the first seen winning a tie (INFERRED).
        if (emitters.Count <= MaxEmitters) return emitters;
        return emitters.Select((emitter, i) => (emitter, i)).OrderByDescending(p => counts[p.i]).ThenBy(p => p.i).Take(MaxEmitters)
            .Select(p => p.emitter).ToList();
    }
}
