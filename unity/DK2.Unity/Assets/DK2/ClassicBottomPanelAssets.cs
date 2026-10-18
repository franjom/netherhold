// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DK2.Art;
using DK2.Art.Textures;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Presentation;

namespace DK2.Unity
{
    public interface IClassicBottomPanelAssets
    {
        RgbaImage Image(string name);
        string Icon(BottomPanelChoiceKey key);
        string Name(BottomPanelChoiceKey key, string fallback);
        int Order(BottomPanelChoiceKey key);
        string Text(uint id);
        BitmapFont Font(int screenHeight);
    }

    /// <summary>Native interface assets remain local to the install.</summary>
    public sealed class InstallClassicBottomPanelAssets : IClassicBottomPanelAssets, IDisposable
    {
        private readonly EngineTextureArchive _textures;
        private readonly Dk2StringTable _strings;
        private readonly string _textDirectory;
        private readonly Dictionary<BottomPanelChoiceKey, string> _names = new Dictionary<BottomPanelChoiceKey, string>();
        private readonly Dictionary<BottomPanelChoiceKey, string> _icons = new Dictionary<BottomPanelChoiceKey, string>();
        private readonly Dictionary<BottomPanelChoiceKey, int> _order = new Dictionary<BottomPanelChoiceKey, int>();
        private readonly Dictionary<string, BitmapFont> _fonts = new Dictionary<string, BitmapFont>();

        public InstallClassicBottomPanelAssets(string install, GameDatabase database)
        {
            _textures = EngineTextureArchive.OpenInstall(install);
            _textDirectory = Path.Combine(install, "Data", "Text", "Default");
            _strings = Dk2StringTable.Open(Path.Combine(_textDirectory, "Text.str"));
            var art = Dk2ArtCatalogue.Load(install);
            foreach (RoomDef room in database.Rooms.Values)
            {
                var key = new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, room.RoomId);
                _names[key] = _strings.Resolve(room.NameStringId);
                if (art.Rooms.TryGetValue(room.RoomId, out var resources)) _icons[key] = resources[0].Name;
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
                }
            }
        }
        public string Icon(BottomPanelChoiceKey key) => _icons.TryGetValue(key, out string name) ? name : null;
        public int Order(BottomPanelChoiceKey key) => _order.TryGetValue(key, out int order) ? order : key.Value;
        public string Name(BottomPanelChoiceKey key, string fallback) => _names.TryGetValue(key, out string name) ? name : fallback;
        public string Text(uint id) => _strings.Resolve(id);
        public RgbaImage Image(string name) => _textures.Decode(name);
        public BitmapFont Font(int screenHeight)
        {
            string file = InterfaceFonts.LegibleFileName(screenHeight);
            if (!_fonts.TryGetValue(file, out BitmapFont font))
            { font = Bf4FontReader.Open(Path.Combine(_textDirectory, file)); _fonts.Add(file, font); }
            return font;
        }
        public void Dispose() => _textures.Dispose();
    }
}
