// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Text;
using DK2.Assets;
using DK2.Core;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>
/// Draws a world as text, through the same <see cref="WorldView"/> and the same
/// <see cref="IAssetProvider"/> chain a Unity view would use.
///
/// <para><b>Why a text renderer is worth having when the goal is Unity.</b>
/// Goal 11 asks for the simulation to be presentable in more than one way, and a
/// second renderer is the only thing that actually proves the boundary is a
/// boundary: if the view types were secretly Unity-shaped, this would not
/// compile. It also gives the project a renderer that <b>runs in CI</b> — the
/// Unity view will not — so "the presentation layer still works" becomes a
/// test result rather than something somebody notices by opening the editor.</para>
///
/// <para><b>And it is honest about missing art.</b> Every glyph here comes from
/// <see cref="PlaceholderProvider.GlyphFor"/> when nothing resolved, so a run
/// with no converted assets looks like what it is. The same is true of the Unity
/// view by construction, because both ask the same chain.</para>
///
/// <para>This is not a game interface. There is no input, no camera and no
/// scrolling: it renders a rectangle of a world once, for a scenario report or a
/// test. Making it interactive would make it a second front end to maintain, and
/// the front end is Unity's job.</para>
/// </summary>
public sealed class TextRenderer
{
    private readonly WorldView _view;
    private readonly IAssetProvider _assets;

    public TextRenderer(WorldView view, IAssetProvider assets)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    }

    /// <summary>
    /// The reserved glyphs — one meaning each, and <b>none of them appears in
    /// the wall or room alphabets</b>.
    ///
    /// <para><b>That disjointness is not tidiness; the first version of this
    /// renderer did not have it and drew a lie.</b> The wall alphabet included
    /// <c>@</c> and <c>$</c>, so rock variants on campaign level 1 read as
    /// creatures and as stored gold — and the dungeon heart, which really does
    /// hold gold, became impossible to tell from the rock beside it.
    /// <c>GlyphsAreDisjoint</c> now fails if any alphabet ever overlaps
    /// again.</para>
    /// </summary>
    public const char OutsideGlyph = ' ';
    public const char UnownedFloorGlyph = '.';
    public const char GoldGlyph = '$';
    public const char TaggedGlyph = 'x';
    public const char CreatureGlyph = '@';
    public const char CarryingCreatureGlyph = 'c';
    public const char UnknownPlayerGlyph = '?';

    /// <summary>
    /// Wall symbols, one per solid terrain type, picked by the asset key's
    /// stable hash. Punctuation only, so walls never collide with the reserved
    /// glyphs or with a room's letter.
    /// </summary>
    public const string WallAlphabet = "#%&*+=~-";

    /// <summary>
    /// Room furniture, one capital per room type. Capitals because a room is
    /// the thing a reader is usually looking for, and a letter stands out from a
    /// field of punctuation in a way another symbol would not.
    /// </summary>
    public const string RoomAlphabet = "ABDEFGHIJKLMNOPQRSTUVWYZ";

    /// <summary>
    /// Renders the whole world.
    /// </summary>
    public string Render(WorldState world) =>
        Render(world, 0, 0, world?.Width ?? 0, world?.Height ?? 0);

    /// <summary>
    /// Renders a rectangle of the world, clipped to it.
    ///
    /// <para>Precedence, highest first: a creature, then a dig mark, then gold,
    /// then the terrain. That order is the same judgement a real renderer makes
    /// about what occludes what — a creature standing on a marked tile hides the
    /// mark, not the reverse — and putting it here rather than in each renderer
    /// is the point of the shared view.</para>
    /// </summary>
    public string Render(WorldState world, int originX, int originY, int width, int height)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        var creatures = new Dictionary<int, CreatureView>();
        foreach (CreatureView creature in _view.Creatures(world))
        {
            TilePos at = creature.Tile;
            if (!world.Contains(at)) continue;
            // Later creatures overwrite earlier ones on the same tile, which
            // matches "spawn order is behaviour" without pretending a character
            // cell can show two things.
            creatures[(at.Y * world.Width) + at.X] = creature;
        }

        var sb = new StringBuilder();

        for (int y = originY; y < originY + height; y++)
        {
            for (int x = originX; x < originX + width; x++)
            {
                if (!world.Contains(x, y)) { sb.Append(OutsideGlyph); continue; }

                if (creatures.TryGetValue((y * world.Width) + x, out CreatureView creature))
                {
                    sb.Append(creature.GoldHeld > 0 ? CarryingCreatureGlyph : CreatureGlyph);
                    continue;
                }

                sb.Append(GlyphFor(_view.TileAt(world, x, y)));
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// The glyph for one tile.
    ///
    /// <para>A wall takes its symbol and a room its letter from the asset key's
    /// stable hash, so each type is visually distinct and stays the same
    /// character across runs and machines — the text equivalent of the
    /// placeholder colours, and derived from the same hash for the same
    /// reason.</para>
    /// </summary>
    public char GlyphFor(TileView tile)
    {
        if (tile.Tagged) return TaggedGlyph;
        if (tile.GoldHeld > 0) return GoldGlyph;

        if (tile.Solid)
        {
            // Ask the chain even though the answer is thrown away: a renderer
            // that skips resolution when it can guess is a renderer that stops
            // reporting missing art, and the chain's counts are how a run says
            // "everything was a box".
            _assets.TryResolve(tile.Ground, out _);
            return WallAlphabet[PlaceholderProvider.SymbolIndex(tile.Ground, WallAlphabet.Length)];
        }

        if (tile.IsRoom)
        {
            _assets.TryResolve(tile.Furniture, out _);
            return RoomAlphabet[PlaceholderProvider.SymbolIndex(tile.Furniture, RoomAlphabet.Length)];
        }

        _assets.TryResolve(tile.Ground, out _);
        return PlayerGlyph(tile.OwnerPlayerId);
    }

    /// <summary>
    /// A single character for a player id.
    ///
    /// <para>Digits only, and <c>?</c> past nine. DK2 allows a handful of
    /// keepers, so extending this into letters would buy nothing and would cost
    /// the disjointness the room alphabet depends on.</para>
    /// </summary>
    public static char PlayerGlyph(byte playerId) => playerId switch
    {
        0 => UnownedFloorGlyph,
        <= 9 => (char)('0' + playerId),
        _ => UnknownPlayerGlyph,
    };

    /// <summary>
    /// The legend for a rendering, so a scenario log is readable without this
    /// file open beside it.
    /// </summary>
    public static string Legend() =>
        $"'{OutsideGlyph}' outside   '{UnownedFloorGlyph}' unowned floor   " +
        $"1-9 floor owned by that keeper   '{TaggedGlyph}' marked for digging   " +
        $"'{GoldGlyph}' gold stored here   '{CreatureGlyph}' creature   " +
        $"'{CarryingCreatureGlyph}' creature carrying gold   " +
        $"{WallAlphabet} walls (one per terrain type)   " +
        $"A-Z room furniture (one per room type). " +
        "Wall and room characters are derived from the asset key's stable hash, " +
        "so a given type is always the same character.";
}
