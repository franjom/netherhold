// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using DK2.Art.Textures;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
namespace DK2.Unity
{
    public interface IClassicOptionsAssets
    {
        string Text(uint id);
        BitmapFont Font(int screenHeight);
    }
    public sealed class InstallClassicOptionsAssets : IClassicOptionsAssets
    {
        private readonly InstallHudSources _sources;

        /// <summary>From the install alone.</summary>
        public InstallClassicOptionsAssets(string install) : this(new InstallHudSources(install)) { }

        public InstallClassicOptionsAssets(InstallHudSources sources) =>
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));

        public string Text(uint id) => _sources.Strings.TryResolve(id,out string value) ? value : string.Empty;
        public BitmapFont Font(int height) => _sources.Font(InterfaceFonts.LegibleFileName(height));
    }
    public sealed class ClassicOptionsPanel : IDisposable
    {
        private readonly IClassicOptionsAssets _assets;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly Image[] _labels = new Image[8];
        private readonly Texture2D[] _textures = new Texture2D[8];
        private readonly bool[] _supported = new bool[13];
        private ClassicOptionsPage _page;
        private ClassicSessionAction _confirmation;
        private IReadOnlyList<ClassicOptionsEntry> Entries => ClassicOptionsLayout.ForPage(_page);
        private bool _visible;
        private int _height;
        private Vector2Int _viewport;
        public PanelSettings Panel => _settings;
        private int Height => _viewport.y > 0 ? _viewport.y : Screen.height;
        public ClassicOptionsPanel(Transform parent,IClassicOptionsAssets assets)
        {
            _assets=assets;
            _settings=ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet=Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode=PanelScaleMode.ScaleWithScreenSize;
            _settings.referenceResolution=new Vector2Int(1024,768);
            _settings.screenMatchMode=PanelScreenMatchMode.MatchWidthOrHeight; _settings.match=1;
            _settings.sortingOrder=15;
            _owner=new GameObject("Dungeon options"); _owner.transform.SetParent(parent,false);
            var document=_owner.AddComponent<UIDocument>(); document.panelSettings=_settings;
            _root=document.rootVisualElement; _root.pickingMode=PickingMode.Ignore;
            _root.style.width=Length.Percent(100); _root.style.height=Length.Percent(100);
            for(int i=0;i<_labels.Length;i++)
            {
                var label=new Image { pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.ScaleToFit };
                label.style.position=Position.Absolute; _root.Add(label); _labels[i]=label;
            }
            SetSupported(ClassicOptionsAction.ContinueGame,true);
            SetSupported(ClassicOptionsAction.LevelObjective,true);
            SetSupported(ClassicOptionsAction.Back,true);
            SetSupported(ClassicOptionsAction.EndGame,true);
            SetSupported(ClassicOptionsAction.RestartLevel,true);
            SetSupported(ClassicOptionsAction.ReturnToMenu,true);
            SetSupported(ClassicOptionsAction.QuitGame,true);
            SetSupported(ClassicOptionsAction.Confirm,true);
            SetSupported(ClassicOptionsAction.Cancel,true);
            SetVisible(false);
        }
        public void SetSupported(ClassicOptionsAction action,bool supported)
        { if(action!=ClassicOptionsAction.None) _supported[(int)action]=supported; }
        public void SetVisible(bool visible)
        { _visible=visible; _root.style.display=visible?DisplayStyle.Flex:DisplayStyle.None; }
        public void SetPage(ClassicOptionsPage page, ClassicSessionAction confirmation)
        {
            if (_page == page && _confirmation == confirmation) return;
            _page = page; _confirmation = confirmation; _height = 0;
        }
        public bool ContainsPointer(Vector3 screenPosition) => _visible;
        public ClassicOptionsAction ActionAt(Vector3 screenPosition)
        {
            if (!_visible || Height<=0) return ClassicOptionsAction.None;
            float scale=Height/768f;
            float x=screenPosition.x/scale,y=768-screenPosition.y/scale;
            foreach(ClassicOptionsEntry entry in Entries)
                if(_supported[(int)entry.Action] && x>=entry.X && x<entry.X+entry.Width && y>=entry.Y && y<entry.Y+38.4f)
                    return entry.Action;
            return ClassicOptionsAction.None;
        }
        public void Draw(Vector3 pointer,Vector2Int viewport=default)
        {
            _viewport=viewport;
            if (!_visible) return;
            if (_height!=Height) { _height=Height; RebuildText(); }
            ClassicOptionsAction hover=ActionAt(pointer);
            for(int i=0;i<Entries.Count;i++)
            {
                var entry=Entries[i];
                float colour=entry.Action == ClassicOptionsAction.None ? 200/255f
                    : !_supported[(int)entry.Action] ? .4f : hover==entry.Action ? 1 : 200/255f;
                _labels[i+1].tintColor=new Color(colour,colour,colour,1);
            }
        }
        private void RebuildText()
        {
            BitmapFont font=_assets.Font(_height);
            for(int i=0;i<_labels.Length;i++)
            {
                if(_textures[i]!=null) UnityEngine.Object.Destroy(_textures[i]);
                bool shown = i == 0 ? _page != ClassicOptionsPage.Objective : i <= Entries.Count;
                _labels[i].style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (!shown) { _textures[i] = null; _labels[i].image = null; continue; }
                uint id=i==0 ? ClassicOptionsLayout.TitleFor(_page, _confirmation) : Entries[i-1].TextId;
                _textures[i]=TextTexture(font,_assets.Text(id)); _labels[i].image=_textures[i];
                float x=i==0?8:Entries[i-1].X;
                float y=i==0?72:Entries[i-1].Y;
                float scale=768f/_height;
                _labels[i].style.left=x; _labels[i].style.top=y;
                _labels[i].style.width=_textures[i].width*scale;
                _labels[i].style.height=_textures[i].height*scale;
            }
        }
        internal static Texture2D TextTexture(BitmapFont font,string text,int wrapWidth=0)
        {
            IReadOnlyList<BitmapTextLine> lines=BitmapTextLayout.Wrap(font,text,wrapWidth>0?wrapWidth:32767);
            int width=1,minY=0,maxY=font.LineHeight;
            foreach(BitmapTextLine line in lines)
            {
                int pen=0;
                foreach(char c in line.Text)
                {
                    BitmapGlyph glyph=font.Find(c); if(glyph==null) continue;
                    width=Math.Max(width,pen+glyph.OffsetX+glyph.Width);
                    minY=Math.Min(minY,glyph.OffsetY); maxY=Math.Max(maxY,glyph.OffsetY+glyph.Height);
                    pen+=glyph.Advance;
                }
            }
            int height=Math.Max(1,(Math.Max(1,lines.Count)-1)*font.LineHeight+maxY-minY);
            var pixels=new Color32[width*height];
            for(int row=0;row<lines.Count;row++)
            {
                int pen=0;
                foreach(char c in lines[row].Text)
                {
                    BitmapGlyph glyph=font.Find(c); if(glyph==null) continue;
                    for(int y=0;y<glyph.Height;y++) for(int x=0;x<glyph.Width;x++)
                    {
                        int px=pen+glyph.OffsetX+x,py=row*font.LineHeight+y+glyph.OffsetY-minY;
                        if((uint)px<(uint)width && (uint)py<(uint)height)
                            pixels[(height-1-py)*width+px]=new Color32(255,255,255,(byte)(glyph.Pixels[y*glyph.Width+x]*17));
                    }
                    pen+=glyph.Advance;
                }
            }
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false)
                { filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false,true); return texture;
        }
        public void Dispose()
        {
            foreach(Texture2D texture in _textures) if(texture!=null) UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}
