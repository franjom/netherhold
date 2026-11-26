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
        string[] cells = new string[Side * Side];
        Cells(world, database, viewer, listener, cells);
        return cells;
    }

    /// <summary><see cref="Cells(WorldState, GameDatabase, PlayerId, TilePos)"/> written into <paramref name="cells"/>, which holds
    /// at least <see cref="Side"/> squared, so a frame that rebuilds the grid allocates nothing.</summary>
    public static void Cells(WorldState world, GameDatabase database, PlayerId viewer, TilePos listener, string[] cells)
    {
        if (cells is null || cells.Length < Side * Side)
        {
            throw new ArgumentException("The grid needs 121 cells.", nameof(cells));
        }

        for (int row = 0; row < Side; row++)
        {
            for (int column = 0; column < Side; column++)
            {
                int x = listener.X - Reach + column, y = listener.Y - Reach + row;
                cells[(row * Side) + column] = world.Contains(new TilePos(x, y)) && world.Tile(x, y).IsExploredBy(viewer)
                    ? database.TerrainById(world.Tile(x, y).TerrainId).SoundCategory : "";
            }
        }
    }

    /// <summary>An emitter for each of the two heaviest categories in <paramref name="cells"/> (by cell count) among those <paramref name="hasAmbience"/> says have the ambient group.</summary>
    public static List<Emitter> Emitters(string[] cells, TilePos listener, Func<string, bool> hasAmbience)
    {
        var emitters = new List<Emitter>();
        Emitters(cells, listener, hasAmbience, emitters);
        return emitters;
    }

    /// <summary><see cref="Emitters(string[], TilePos, Func{string, bool})"/> written into <paramref name="into"/> (cleared first), without
    /// allocating: the grid is rebuilt every turn while the camera rests.</summary>
    public static void Emitters(string[] cells, TilePos listener, Func<string, bool> hasAmbience, List<Emitter> into)
    {
        if (cells is null)
        {
            throw new ArgumentNullException(nameof(cells));
        }

        if (hasAmbience is null)
        {
            throw new ArgumentNullException(nameof(hasAmbience));
        }

        if (into is null)
        {
            throw new ArgumentNullException(nameof(into));
        }

        into.Clear();
        Span<int> counts = cells.Length <= Side * Side ? stackalloc int[Side * Side] : new int[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            string category = cells[i];
            if (category.Length == 0 || SeenBefore(cells, i) || !hasAmbience(category))
            {
                continue;
            }

            float x = 0, y = 0;
            int count = 0;
            for (int j = i; j < cells.Length; j++)
            {
                if (cells[j] != category)
                {
                    continue;
                }

                x += listener.X - Reach + (j % Side) + .5f;
                y += listener.Y - Reach + (j / Side) + .5f;
                count++;
            }
            counts[into.Count] = count;
            into.Add(new Emitter(category, new Vector3(x / count, 0f, -y / count)));
        }
        // 0x00609CE0 pops the best two records from a heap ordered by tile count and weight, and the pass keeps those categories only (the loop bound
        // at +0x28 is unread but at most two). The comparator and its tie-break are unread: ours ranks by cell count, the first seen winning a tie (INFERRED).
        if (into.Count <= MaxEmitters)
        {
            return;
        }

        int first = Heaviest(counts.Slice(0, into.Count), -1), second = Heaviest(counts.Slice(0, into.Count), first);
        Emitter best = into[first], next = into[second];
        into.Clear();
        into.Add(best);
        into.Add(next);
    }

    private static bool SeenBefore(string[] cells, int index)
    {
        string category = cells[index];
        for (int i = 0; i < index; i++)
        {
            if (cells[i] == category)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The index of the largest count, the first of equals, skipping <paramref name="skip"/>.</summary>
    private static int Heaviest(ReadOnlySpan<int> counts, int skip)
    {
        int best = -1;
        for (int i = 0; i < counts.Length; i++)
        {
            if (i != skip && (best < 0 || counts[i] > counts[best]))
            {
                best = i;
            }
        }

        return best;
    }
}
