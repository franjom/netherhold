// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Art;
using DK2.Art.Textures;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Data.Text;

namespace DK2.Presentation;

/// <summary>The install's bottom-panel art, read in place and shared by every engine adapter (migration plan step 1b):
/// each choice's name, icon, upgraded icon and panel order from the KWD records, plus the HUD's textures and legible
/// BF4 font from <see cref="InstallHudSources"/>.</summary>
public sealed class InstallBottomPanelArt : IBottomPanelArt, IDisposable
{
    private readonly InstallHudSources _sources;
    private readonly bool _owned;
    private readonly Dk2StringTable _strings;
    private readonly Dictionary<BottomPanelChoiceKey, string> _names = new();
    private readonly Dictionary<BottomPanelChoiceKey, string> _icons = new();
    private readonly Dictionary<BottomPanelChoiceKey, string> _upgradedIcons = new();
    private readonly Dictionary<BottomPanelChoiceKey, int> _order = new();

    /// <summary>From the install alone, owning the texture archive it opens.</summary>
    public InstallBottomPanelArt(string install, GameDatabase database)
        : this(new InstallHudSources(install), database ?? throw new ArgumentNullException(nameof(database)))
    {
        _owned = true;
    }

    /// <summary>Over shared sources, which the caller disposes.</summary>
    public InstallBottomPanelArt(InstallHudSources sources, GameDatabase database)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        if (database is null) throw new ArgumentNullException(nameof(database));
        string install = sources.Install;
        _strings = sources.Strings;
        Dk2ArtCatalogue art = sources.Catalogue;
        foreach (RoomDef room in database.Rooms.Values)
        {
            var key = new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, room.RoomId);
            _names[key] = _strings.Resolve(room.NameStringId);
            if (art.Rooms.TryGetValue(room.RoomId, out IReadOnlyList<Dk2ArtResource>? resources)) _icons[key] = resources[0].Name;
        }
        // A stored special's slot draws runtime art slot 0, file art #1, the GuiIconResource
        // (0x00412FC0 installs the definition itself; object-drawing.md §2).
        foreach (ObjectDef special in database.Objects.Values)
        {
            if (!special.IsSpecial) continue;
            var key = new BottomPanelChoiceKey(BottomPanelChoiceKind.Special, special.ObjectId);
            _names[key] = _strings.Resolve(special.NameStringId);
            if (art.Objects.TryGetValue(special.ObjectId, out IReadOnlyList<Dk2ArtResource>? resources) && resources.Count > 1) _icons[key] = resources[1].Name;
        }
        Read(install, "KeeperSpells", KwdRecordType.KeeperSpells, 406, 0xEE, 0xE6, BottomPanelChoiceKind.Spell);
        Read(install, "Doors", KwdRecordType.Doors, 616, 0x244, 0x23C, BottomPanelChoiceKind.Door);
        Read(install, "Traps", KwdRecordType.Traps, 579, 0x20C, 0x208, BottomPanelChoiceKind.Trap);
    }

    private void Read(string install, string file, KwdRecordType type, int size, int idOffset,
        int nameOffset, BottomPanelChoiceKind kind)
    {
        foreach (KwdChunk chunk in KwdReader.ReadFile(Path.Combine(install, "Data", "editor", file + ".kwd")))
        {
            if (chunk.Type != type) continue;
            if (chunk.Content.Length != chunk.ItemCount * size) throw new InvalidDataException("Unexpected interface definition size.");
            for (int at = 0; at < chunk.Content.Length; at += size)
            {
                byte[] data = chunk.Content;
                var key = new BottomPanelChoiceKey(kind, data[at + idOffset]);
                _names[key] = _strings.Resolve((uint)(data[at + nameOffset] | data[at + nameOffset + 1] << 8));
                int orderOffset = kind == BottomPanelChoiceKind.Spell ? 0xDB : kind == BottomPanelChoiceKind.Door ? 0x245 : 0x230;
                _order[key] = data[at + orderOffset];
                int start = at + (kind == BottomPanelChoiceKind.Spell ? 0x20 : 0x74);
                int end = start;
                while (end < start + 64 && data[end] != 0) end++;
                _icons[key] = Encoding.ASCII.GetString(data, start, end - start);
                if (kind == BottomPanelChoiceKind.Spell)
                {
                    int upgraded = at + 0x120, stop = upgraded;
                    while (stop < upgraded + 64 && data[stop] != 0) stop++;
                    if (stop > upgraded) _upgradedIcons[key] = Encoding.ASCII.GetString(data, upgraded, stop - upgraded);
                }
            }
        }
    }

    public string? Icon(BottomPanelChoiceKey key) => _icons.TryGetValue(key, out string? name) ? name : null;
    public string? UpgradedIcon(BottomPanelChoiceKey key) => _upgradedIcons.TryGetValue(key, out string? name) ? name : null;
    /// <summary>The panel order of a spell, door or trap (its record's order byte); otherwise the key's own value.</summary>
    public int Order(BottomPanelChoiceKey key) => _order.TryGetValue(key, out int order) ? order : key.Value;
    public string Name(BottomPanelChoiceKey key, string fallback) => _names.TryGetValue(key, out string? name) ? name : fallback;
    public string Text(uint id) => _strings.Resolve(id);
    public RgbaImage Image(string name) => _sources.Texture(name);
    public BitmapFont Font(int screenHeight) => _sources.Font(InterfaceFonts.LegibleFileName(screenHeight));

    public void Dispose()
    {
        if (_owned) _sources.Dispose();
    }
}
