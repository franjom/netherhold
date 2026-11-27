// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Art.Textures;
using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The creature overview's install art, read in place and shared by every engine adapter (migration plan
/// step 1b): each creature type's portrait and overview order from its Creatures.kwd record, its name from Text.str,
/// and the HUD's textures through the caller's loader.</summary>
public sealed class InstallCreatureOverviewArt
{
    private const int RecordSize = 5449;
    private readonly Func<string, RgbaImage> _image;
    private readonly Func<uint, string> _text;
    private readonly GameDatabase _database;
    private readonly Dictionary<byte, string> _portraits = new Dictionary<byte, string>();
    private readonly Dictionary<byte, int> _order = new Dictionary<byte, int>();

    /// <param name="install">The install directory.</param>
    /// <param name="image">A HUD catalogue image by name.</param>
    /// <param name="text">A <c>Text.str</c> string by id.</param>
    public InstallCreatureOverviewArt(string install, GameDatabase database, Func<string, RgbaImage> image, Func<uint, string> text)
    {
        if (install is null)
        {
            throw new ArgumentNullException(nameof(install));
        }

        _database = database ?? throw new ArgumentNullException(nameof(database));
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        foreach (KwdChunk chunk in KwdReader.ReadFile(Path.Combine(install, "Data", "Editor", "Creatures.kwd")))
        {
            if (chunk.Type != KwdRecordType.Creatures || chunk.ItemCount == 0)
            {
                continue;
            }

            if (chunk.Content.Length != chunk.ItemCount * RecordSize)
            {
                throw new InvalidDataException("Unexpected creature portrait record size.");
            }

            for (int i = 0; i < chunk.ItemCount; i++)
            {
                int at = i * RecordSize; byte id = chunk.Content[at + 0xF4B];
                string name = Encoding.ASCII.GetString(chunk.Content, at + 0xD40, 64).TrimEnd('\0');
                _portraits[id] = name; _order[id] = chunk.Content[at + 0xCF6];
            }
        }
    }

    public RgbaImage Image(string path)
    {
        return _image(path);
    }

    /// <summary>The type's portrait catalogue image (record <c>+0xD40</c>).</summary>
    public string Portrait(CreatureOverviewType type)
    {
        return _portraits[type.Value];
    }

    /// <summary>The type's <c>Text.str</c> name.</summary>
    public string Name(CreatureOverviewType type, string fallback)
    {
        return _text(_database.CreatureById(type.Value).NameStringId);
    }

    /// <summary>The type's overview order (record byte <c>+0xCF6</c>).</summary>
    public int Order(CreatureOverviewType type)
    {
        return _order[type.Value];
    }
}
