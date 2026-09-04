// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DK2.Core;

namespace DK2.Assets;

/// <summary>
/// Answers every asset key, always, with a coloured box.
///
/// <para><b>This is the load-bearing provider, not the fallback.</b> The plan
/// (§7.2) requires that the game <i>runs</i> with no DK2 install and no art
/// pack — "as untextured boxes" — so that the "shippable without proprietary
/// assets" property is continuously tested rather than aspirational. That
/// promise is only true if this provider is total, which
/// <c>PlaceholderProviderTests</c> checks over every key the whole shipped game
/// database implies.</para>
///
/// <para><b>Colours are derived, not chosen.</b> A key's
/// <see cref="AssetRef.StableHash"/> picks the hue, so rock is the same colour
/// in every run, on every machine and in every renderer — which is the
/// difference between a box view you can read and one you cannot. It also means
/// a new terrain type gets a distinct colour with no table to maintain, and a
/// mod-authored one gets one for free.</para>
///
/// <para>Two deliberate exceptions to "derive everything": the <b>shape</b>
/// follows the asset's kind and category, because a creature drawn as a
/// ground-flat slab is not legible; and colours are pushed into the mid range
/// rather than used raw, because a hash produces near-blacks and near-whites
/// often enough to matter.</para>
/// </summary>
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
    /// What shape stands in for a kind of asset.
    ///
    /// <para>Kind decides first, then category, because a terrain's
    /// <i>texture</i> is a slab whatever the terrain is, while a terrain's
    /// <i>mesh</i> depends on whether the terrain fills its tile — and this
    /// assembly cannot ask, since it does not know the terrain's flags.
    /// <see cref="AssetCatalogue"/> is where that knowledge lives; it requests
    /// the mesh of a solid terrain and the texture of a walkable one, so the
    /// shapes come out right without this method needing the game database.</para>
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
    /// A stable index in <c>0..count-1</c> for an asset key — the same
    /// derivation <see cref="LookFor"/> uses for colour, for renderers that need
    /// to pick from a small set instead (a character, a material, a debug hue).
    ///
    /// <para>Kept here rather than in each renderer so that every view agrees
    /// about which terrain is "the first one": the text view and a Unity debug
    /// view showing different things for the same tile would hide which of them
    /// was wrong. <b>Choosing the set is the renderer's business; being stable
    /// about it is this method's.</b></para>
    /// </summary>
    public static int SymbolIndex(AssetRef reference, int count)
    {
        if (count <= 0) throw new System.ArgumentOutOfRangeException(nameof(count));
        return (int)(reference.StableHash() % (ulong)count);
    }

    /// <summary>
    /// Proof that this provider's derivation is what the project's hash says it
    /// is, rather than something that drifted. Bumping this is a signal that
    /// every placeholder colour changed, which is a visual change and should be
    /// a deliberate one.
    /// </summary>
    public static ulong DerivationVersion()
    {
        Hash64 hash = Hash64.Start();
        hash.Add("PlaceholderProvider/1");
        return hash.Value;
    }
}
