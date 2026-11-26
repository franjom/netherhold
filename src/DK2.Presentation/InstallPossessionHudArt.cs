// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Art;
using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The three creature GUI resources installed by 0x00420D10, read from the
/// shipped definitions. Textures remain behind the shared install art abstraction.</summary>
public sealed class InstallPossessionHudArt
{
    private readonly Dictionary<CreatureOverviewType, (string Icon, string Portrait, string Melee)> _creatures = new();
    public string GoldIcon { get; }

    public InstallPossessionHudArt(string install)
    {
        GoldIcon = Dk2ArtCatalogue.Load(install).Rooms[(byte)RoomTypeId.Treasury][0].Name;
        const int size = 5449;
        foreach (KwdChunk chunk in KwdReader.ReadFile(Path.Combine(install, "Data", "editor", "Creatures.kwd")))
        {
            if (chunk.Type != KwdRecordType.Creatures) continue;
            if (chunk.Content.Length != chunk.ItemCount * size) throw new InvalidDataException("Unexpected creature HUD record size.");
            for (int at = 0; at < chunk.Content.Length; at += size)
            {
                string Name(int offset) => Encoding.ASCII.GetString(chunk.Content, at + offset, 64).TrimEnd('\0');
                _creatures[new(chunk.Content[at + 0xF4B])] = (Name(0xC44), Name(0xD40), Name(0x1027));
            }
        }
    }

    public string Icon(CreatureOverviewType type) => _creatures.TryGetValue(type, out (string Icon, string Portrait, string Melee) art) ? art.Icon : "";
    public string Portrait(CreatureOverviewType type) => _creatures.TryGetValue(type, out (string Icon, string Portrait, string Melee) art) ? art.Portrait : "";
    public string Melee(CreatureOverviewType type) => _creatures.TryGetValue(type, out (string Icon, string Portrait, string Melee) art) ? art.Melee : "";
}
