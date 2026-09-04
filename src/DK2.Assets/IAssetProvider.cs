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

using System;

namespace DK2.Assets;

/// <summary>
/// Where a resolved asset actually comes from.
/// </summary>
public enum AssetSource
{
    /// <summary>A file on disk, produced by the converter from the user's own copy.</summary>
    ConvertedFile,

    /// <summary>A file on disk from a replacement art pack.</summary>
    ReplacementFile,

    /// <summary>Nothing on disk: a coloured box, a tone, a flat colour.</summary>
    Placeholder,
}

/// <summary>
/// What a provider hands back: enough for a renderer to load the thing, and
/// nothing that assumes an engine.
///
/// <para><b>Deliberately not a Unity object.</b> This assembly is loadable by
/// Unity and must not depend on it (ADR-0002), so the seam is a description —
/// a file path, or the shape and colour of a placeholder — and turning that into
/// a mesh is the renderer's job. The same handle drives a Unity view, the text
/// view in the harness, and any future renderer.</para>
/// </summary>
public readonly struct ResolvedAsset
{
    public ResolvedAsset(AssetRef reference, AssetSource source, string? filePath, PlaceholderLook look, string providerName)
    {
        Reference = reference;
        Source = source;
        FilePath = filePath;
        Look = look;
        ProviderName = providerName;
    }

    /// <summary>The key that was asked for.</summary>
    public AssetRef Reference { get; }

    /// <summary>Where it came from.</summary>
    public AssetSource Source { get; }

    /// <summary>
    /// The file to load, for a file-backed source; null for a placeholder.
    /// <b>The only place in the project where an asset path exists at all</b> —
    /// producing one is a provider's job and no caller may construct one.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>How to draw it when there is no file. Meaningful for placeholders.</summary>
    public PlaceholderLook Look { get; }

    /// <summary>Which provider answered. Diagnostics only; never a switch.</summary>
    public string ProviderName { get; }

    /// <summary>True when a renderer must improvise rather than load a file.</summary>
    public bool IsPlaceholder => Source == AssetSource.Placeholder;

    public override string ToString() =>
        Source == AssetSource.Placeholder
            ? $"{Reference} → placeholder {Look} ({ProviderName})"
            : $"{Reference} → {FilePath} ({ProviderName})";
}

/// <summary>
/// How to draw an asset nobody has supplied: a shape, a size and a colour.
///
/// <para>Enough to make a dungeon legible as boxes and no more. The colour is
/// derived from the asset key rather than chosen, so a terrain is the same
/// colour every run and on every machine — see
/// <see cref="PlaceholderProvider"/>.</para>
/// </summary>
public readonly struct PlaceholderLook
{
    public PlaceholderLook(PlaceholderShape shape, byte red, byte green, byte blue, ushort heightPermille)
    {
        Shape = shape;
        Red = red;
        Green = green;
        Blue = blue;
        HeightPermille = heightPermille;
    }

    public PlaceholderShape Shape { get; }
    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }

    /// <summary>
    /// Height in thousandths of a tile width. An integer on purpose: this
    /// assembly follows ADR-0001 like the rest, and a renderer converts.
    /// </summary>
    public ushort HeightPermille { get; }

    public override string ToString() =>
        $"{Shape} #{Red:X2}{Green:X2}{Blue:X2} h={HeightPermille}‰";
}

/// <summary>The shapes a placeholder renderer has to be able to draw.</summary>
public enum PlaceholderShape
{
    /// <summary>Nothing is drawn. For assets whose absence is not a hole (music, effects).</summary>
    None,

    /// <summary>A tile-sized cube. Walls, rock, room floors at full height.</summary>
    Block,

    /// <summary>A flat quad on the ground. Floors, water, lava, textures.</summary>
    Slab,

    /// <summary>A small upright box. Creatures and objects.</summary>
    Figure,
}

/// <summary>
/// The <c>Asset Provider</c> arrow of goal 4. One rule: given a key, either
/// answer or say you cannot, and never throw for a key you do not have.
///
/// <para>Implementations are chained (see <see cref="AssetProviderChain"/>), so
/// "cannot" is an ordinary answer and the last link in the chain is
/// <see cref="PlaceholderProvider"/>, which always answers.</para>
/// </summary>
public interface IAssetProvider
{
    /// <summary>A short name, for diagnostics and for the chain's reporting.</summary>
    string Name { get; }

    /// <summary>
    /// Resolves a key, or returns false. Must not throw for an unknown key —
    /// a missing asset is a normal condition in a project that has to run
    /// without any assets at all.
    /// </summary>
    bool TryResolve(AssetRef reference, out ResolvedAsset asset);
}
