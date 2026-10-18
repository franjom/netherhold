// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DK2.Art.Textures;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Presentation;
namespace DK2.Unity
{
    public interface ICreatureOverviewAssets
    {
        RgbaImage Image(string path);
        string Portrait(CreatureOverviewType type);
        string Name(CreatureOverviewType type,string fallback);
        int Order(CreatureOverviewType type);
    }
    public sealed class InstallCreatureOverviewAssets : ICreatureOverviewAssets
    {
        private readonly IClassicBottomPanelAssets _shared;
        private readonly GameDatabase _database;
        private readonly Dictionary<byte,string> _portraits=new Dictionary<byte,string>();
        private readonly Dictionary<byte,int> _order=new Dictionary<byte,int>();
        public InstallCreatureOverviewAssets(string install,GameDatabase database,IClassicBottomPanelAssets shared)
        {
            _shared=shared;_database=database;
            foreach(KwdChunk chunk in KwdReader.ReadFile(Path.Combine(install,"Data","Editor","Creatures.kwd")))
            {
                if(chunk.Type!=KwdRecordType.Creatures || chunk.ItemCount==0) continue;
                if(chunk.Content.Length!=chunk.ItemCount*5449) throw new InvalidDataException("Unexpected creature portrait record size.");
                for(int i=0;i<chunk.ItemCount;i++)
                {
                    int at=i*5449;byte id=chunk.Content[at+0xF4B];
                    string name=Encoding.ASCII.GetString(chunk.Content,at+0xD40,64).TrimEnd('\0');
                    _portraits[id]=name;_order[id]=chunk.Content[at+0xCF6];
                }
            }
        }
        public RgbaImage Image(string path)=>_shared.Image(path);
        public string Portrait(CreatureOverviewType type)=>_portraits[type.Value];
        public string Name(CreatureOverviewType type,string fallback)=>_shared.Text(_database.CreatureById(type.Value).NameStringId);
        public int Order(CreatureOverviewType type)=>_order[type.Value];
    }
}
