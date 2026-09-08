// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Assets;

/// <summary>Answers every asset key, always, with a coloured box.</summary>
/// <remarks>
/// <para>Total, not a fallback: the game must run with no DK2 install and no art pack
/// (plan §7.2, ADR-0034), and that only holds if this provider answers everything.
/// <c>ProviderTests</c> checks it over every key the shipped database implies.</para>
/// <para>Colour is derived from the key's <see cref="AssetRef.StableHash"/>, never
/// chosen, so a terrain is the same colour in every run, machine and renderer, and a
/// new or mod-authored terrain gets a distinct one with no table to maintain.</para>
/// <para>Two things are not derived: shape follows the asset's kind and category (a
/// creature drawn as a ground-flat slab is unreadable), and colours are pushed into
/// the mid range because a hash yields near-black and near-white too often.</para>
/// </remarks>
public sealed class PlaceholderProvider : IAssetProvider
{
    /// <summary>The single instance. Stateless, so sharing one is free.</summary>
    public static readonly PlaceholderProvider Instance = new();

    public string Name => "placeholder";

    public bool TryResolve(AssetRef reference, out ResolvedAsset asset)
    {
        if (reference.IsEmpty)
        {
            asset = default;
            return false;
        }

        asset = new ResolvedAsset(
            reference,
            AssetSource.Placeholder,
            filePath: null,
            look: LookFor(reference),
            providerName: Name);

        return true;
    }

    /// <summary>
    /// The look for a key. Public because the text renderer and any future
    /// renderer must agree on it — a placeholder that differs per renderer is
    /// worse than no placeholder, since it hides which one is wrong.
    /// </summary>
    public static PlaceholderLook LookFor(AssetRef reference)
    {
        ulong hash = reference.StableHash();

        PlaceholderShape shape = ShapeFor(reference);
        if (shape == PlaceholderShape.None)
            return new PlaceholderLook(shape, 0, 0, 0, 0);

        // Bytes 0..2 of the hash pick the colour; 3 nudges the height. Using
        // distinct bytes rather than the whole value keeps the three channels
        // independent, which is what stops everything landing on a grey diagonal.
        byte red = MidRange((byte)hash);
        byte green = MidRange((byte)(hash >> 8));
        byte blue = MidRange((byte)(hash >> 16));

        ushort height = shape switch
        {
            PlaceholderShape.Block => 1000,
            PlaceholderShape.Slab => 40,
            // 500..750‰ of a tile: tall enough to read as standing, short
            // enough that a creature does not hide the tile it is on.
            PlaceholderShape.Figure => (ushort)(500 + (byte)(hash >> 24) % 250),
            _ => 0,
        };

        return new PlaceholderLook(shape, red, green, blue, height);
    }

    /// <summary>
    /// What shape stands in for a kind of asset: kind decides first, then category.
    /// A terrain's texture is a slab whatever the terrain, while its mesh depends on
    /// whether the terrain fills its tile — a flag this assembly cannot read.
    /// <see cref="AssetCatalogue"/> holds that knowledge and asks for the mesh of a
    /// solid terrain and the texture of a walkable one, so the shapes come out right
    /// without a game database here.
    /// </summary>
    public static PlaceholderShape ShapeFor(AssetRef reference) => reference.Kind switch
    {
        AssetKind.Texture => PlaceholderShape.Slab,
        AssetKind.Mesh => reference.Key.StartsWith(AssetRef.CreatureCategory + ".", System.StringComparison.Ordinal)
            ? PlaceholderShape.Figure
            : PlaceholderShape.Block,

        // Nothing visual to stand in for: silence and no particles are
        // honest placeholders, and drawing a box for a sound would be a lie.
        AssetKind.Sound or AssetKind.Music or AssetKind.Video or AssetKind.Effect => PlaceholderShape.None,

        // An animation's placeholder is the absence of movement, not a shape.
        AssetKind.Animation => PlaceholderShape.None,

        _ => PlaceholderShape.Block,
    };

    /// <summary>
    /// Pushes a raw hash byte into 64..223, so nothing comes out as
    /// indistinguishable-from-black or washed out.
    /// </summary>
    private static byte MidRange(byte raw) => (byte)(64 + (raw * 160 / 256));

    /// <summary>
    /// A stable index in <c>0..count-1</c> for an asset key, from the same hash
    /// <see cref="LookFor"/> uses, for renderers picking from a small set (a
    /// character, a material, a debug hue). Kept here so every view agrees which
    /// terrain is "the first one" — choosing the set is the renderer's business,
    /// being stable about it is this method's.
    /// </summary>
    public static int SymbolIndex(AssetRef reference, int count)
    {
        if (count <= 0) throw new System.ArgumentOutOfRangeException(nameof(count));
        return (int)(reference.StableHash() % (ulong)count);
    }

}
