// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
namespace DK2.Unity
{
    public readonly struct CreatureOverviewRequest
    {
        public CreatureOverviewRequest(CreatureOverviewType type,CreatureOverviewSelection selection,CreatureOverviewFilter filter=CreatureOverviewFilter.All)
        { Type=type;Selection=selection;Filter=filter; }
        public CreatureOverviewType Type { get; }
        public CreatureOverviewSelection Selection { get; }
        public CreatureOverviewFilter Filter { get; }
    }
    public sealed class ClassicCreatureOverview : IDisposable
    {
        private const int MaximumColumns=32;
        private readonly ICreatureOverviewAssets _assets;
        private readonly CreatureOverview _view;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly Dictionary<string,Texture2D> _textures=new Dictionary<string,Texture2D>();
        private readonly Image[] _portraits=new Image[MaximumColumns],_highlight=new Image[MaximumColumns];
        private readonly Image _workerStrip;
        private readonly Image[] _digits=new Image[(MaximumColumns*4+4)*5];
        private readonly Image _modeStrip,_modeHighlight;
        private readonly Image _modeFrame;
        private readonly Texture2D[] _modeFrames=new Texture2D[4];
        private readonly Image _previous,_next;
        private readonly string[] _numberText=new string[MaximumColumns*4+4];
        private readonly int[] _numberValue=new int[MaximumColumns*4+4];
        private readonly Texture2D[,] _numberTextures=new Texture2D[2,10];
        private int _page,_height,_width,_columns;
        private bool _visible;
        public PanelSettings Panel=>_settings;
        public CreatureOverviewMode Mode { get; private set; }
        public ClassicCreatureOverview(Transform parent,ICreatureOverviewAssets assets,CreatureOverview view)
        {
            _assets=assets;_view=view;
            _settings=ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet=Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode=PanelScaleMode.ConstantPixelSize;_settings.sortingOrder=13;
            _owner=new GameObject("Creature overview");if(parent!=null) _owner.transform.SetParent(parent,false);
            var document=_owner.AddComponent<UIDocument>();document.panelSettings=_settings;
            _root=document.rootVisualElement;_root.pickingMode=PickingMode.Ignore;
            for(int i=0;i<MaximumColumns;i++) { _portraits[i]=Picture(null);_highlight[i]=Picture("GUI\\Tabs\\t-cp-hilight"); }
            // Menu 6's worker sidebar is one strip, catalogue 157 (widget 0x00690488); the
            // GUI\Jobs\Expert icons are the mode-selector widgets' art, not this column.
            _workerStrip=Picture("GUI\\Tabs\\t-cp-imp");
            for(int i=0;i<_digits.Length;i++) _digits[i]=Picture(null);
            _previous=Picture("GUI\\Pages\\page_back");_next=Picture("GUI\\Pages\\page_forward");
            _modeStrip=Picture("GUI\\Tabs\\t-creature_panel");
            _modeHighlight=Picture("GUI\\Tabs\\t-cp-hilight");
            _modeFrame=Picture("GUI\\Tabs\\t-cp-total");
            string[] frames={"total","jobs","fights","moods"};
            for(int i=0;i<4;i++) _modeFrames[i]=Texture("GUI\\Tabs\\t-cp-"+frames[i]);
            for(int family=0;family<2;family++) for(int digit=0;digit<10;digit++)
                _numberTextures[family,digit]=Texture((family==0?"GUI\\Icons\\i-":"GUI\\Icons\\lo-")+digit);
            _root.style.display=DisplayStyle.None;
        }
        private float V(float value)=>value*_height/1920f;
        public void Draw(HudVisibility visibility,bool selectedCategory,Vector3 pointer,Vector2Int viewport=default)
        {
            _width=viewport.x>0?viewport.x:Screen.width;_height=viewport.y>0?viewport.y:Screen.height;
            _visible=visibility.ShowNormalHud && selectedCategory;
            _root.style.display=_visible?DisplayStyle.Flex:DisplayStyle.None;
            if(!_visible) return;
            _columns=Math.Min(MaximumColumns-1,Math.Max(1,((int)(_width*1920f/_height)-760-432)/116));
            int ordinary=Math.Max(0,_view.Entries.Count-1),pages=Math.Max(1,(ordinary+_columns-1)/_columns);
            _page=Math.Min(_page,pages-1);
            for(int i=0;i<MaximumColumns;i++)
            {
                int entryIndex=i==0?0:1+_page*_columns+i-1;
                bool show=i==0?_view.Entries.Count>0:i<=_columns && entryIndex<_view.Entries.Count;
                _portraits[i].style.display=show?DisplayStyle.Flex:DisplayStyle.None;
                _highlight[i].style.display=DisplayStyle.None;
                for(int row=0;row<4;row++) Digits(i*4+4+row,0,0,0,false);
                if(!show) continue;
                CreatureOverviewEntry entry=_view.Entries[entryIndex];
                _portraits[i].image=Texture(_assets.Portrait(entry.Type));
                float x=i==0?544:912+(i-1)*136;
                Place(_portraits[i],V(x),V(i==0?1608:1612),V(128),V(264));
                if(i>0)
                {
                    float countX=924+(i-1)*136;
                    bool hover=HitRect(i).Contains(new Vector2(pointer.x,_height-pointer.y));
                    _highlight[i].style.display=hover?DisplayStyle.Flex:DisplayStyle.None;
                    _highlight[i].tintColor=new Color32(0,255,201,255);
                    int rows=CreatureOverview.Rows(Mode);
                    int step=Mode==CreatureOverviewMode.Jobs?64:84;
                    int hoveredRow=Mode==CreatureOverviewMode.Total?0:Math.Min(rows-1,Math.Max(0,(int)((_height-pointer.y-V(1616))/V(step))));
                    Place(_highlight[i],V(countX),V(1616+hoveredRow*step),V(108),V(Mode==CreatureOverviewMode.Total?244:step));
                    for(int row=0;row<rows;row++)
                    {
                        int count=_view.Count(entry.Type,CreatureOverview.RowFilter(Mode,row));
                        Digits(i*4+4+row,count,V(countX+54),V(Mode==CreatureOverviewMode.Total?1709:1628+row*step),true);
                    }
                }
            }
            CreatureOverviewWorkerCounts workers=_view.WorkerCounts;
            Place(_workerStrip,V(476),V(1608),V(64),V(264));
            for(int i=0;i<4;i++)
            {
                int value=i==0?workers.Total:i==1?workers.Idle:i==2?workers.Busy:workers.Fighting;
                Digits(i,value,V(604),V(1624+i*64),true);
            }
            // Both page strips are always drawn, forward (catalogue 10) above back (11):
            // widgets 0x0068FFD8 and 0x00690028. Which way each pages (0x00417730) is unread.
            Place(_next,V(760),V(1612),V(64),V(132));Place(_previous,V(760),V(1744),V(64),V(132));
            Place(_modeStrip,V(680),V(1608),V(64),V(264));
            Place(_modeHighlight,V(692),V(1616+(int)Mode*64),V(44),V(52));
            _modeHighlight.tintColor=Mode==CreatureOverviewMode.Total?Color.white:Mode==CreatureOverviewMode.Jobs?
                new Color32(20,255,10,255):Mode==CreatureOverviewMode.Fighting?new Color32(255,30,0,255):new Color32(240,165,0,255);
            _modeFrame.image=_modeFrames[(int)Mode];
            Place(_modeFrame,V(840),V(1612),V(64),V(264));
        }
        private Rect HitRect(int column)=>column==0?new Rect(V(544),V(1612),V(120),V(256))
            :new Rect(V(924+(column-1)*136),V(1616),V(108),V(244));
        public bool ContainsPointer(Vector3 pointer)=>_visible && new Rect(V(472),V(1600),V(1800),V(320))
            .Contains(new Vector2(pointer.x,_height-pointer.y));
        public bool TryPress(Vector3 pointer,bool primary,bool secondary,bool lowestLevel,bool highestLevel,out CreatureOverviewRequest request)
        {
            request=default;if(!_visible || !primary && !secondary) return false;
            Vector2 point=new Vector2(pointer.x,_height-pointer.y);
            if(primary)
                for(int i=0;i<4;i++)
                    if(new Rect(V(692),V(1616+i*64),V(44),V(52)).Contains(point))
                    { Mode=(CreatureOverviewMode)i;return false; }
            if(primary && new Rect(V(760),V(1612),V(64),V(264)).Contains(point))
            { _page=Math.Max(0,_page+(point.y<V(1744)?1:-1));return false; }
            if(_view.Entries.Count>0 && new Rect(V(472),V(1600),V(192),V(256)).Contains(point))
            {
                int row=Math.Min(3,Math.Max(0,(int)((point.y-V(1600))/V(64))));
                CreatureOverviewFilter filter=row==0?CreatureOverviewFilter.All:row==1?CreatureOverviewFilter.Idle:row==2?CreatureOverviewFilter.Busy:CreatureOverviewFilter.Fighting;
                request=new CreatureOverviewRequest(_view.Entries[0].Type,secondary?CreatureOverviewSelection.FocusNext:lowestLevel?CreatureOverviewSelection.PickupLowestLevel:highestLevel?CreatureOverviewSelection.PickupHighestLevel:CreatureOverviewSelection.PickupNext,filter);return true;
            }
            for(int i=0;i<=_columns;i++)
            {
                int at=i==0?0:1+_page*_columns+i-1;
                if(at>=_view.Entries.Count || !HitRect(i).Contains(point)) continue;
                CreatureOverviewSelection selection=secondary?CreatureOverviewSelection.FocusNext:lowestLevel?CreatureOverviewSelection.PickupLowestLevel
                    :highestLevel?CreatureOverviewSelection.PickupHighestLevel:CreatureOverviewSelection.PickupNext;
                int row=Mode==CreatureOverviewMode.Total?0:Math.Min(CreatureOverview.Rows(Mode)-1,
                    Math.Max(0,(int)((point.y-V(1616))/V(Mode==CreatureOverviewMode.Jobs?64:84))));
                request=new CreatureOverviewRequest(_view.Entries[at].Type,selection,i==0?CreatureOverviewFilter.All:CreatureOverview.RowFilter(Mode,row));return true;
            }
            return false;
        }
        private void Digits(int row,int value,float centre,float top,bool show)
        {
            if(_numberText[row]==null || _numberValue[row]!=value) { _numberValue[row]=value;_numberText[row]=value.ToString(CultureInfo.InvariantCulture); }
            string text=_numberText[row];
            float advance=V(40),left=centre-text.Length*advance/2;
            for(int i=0;i<5;i++)
            {
                Image glyph=_digits[row*5+i];bool visible=show && i<text.Length;
                glyph.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
                if(!visible) continue;
                glyph.image=_numberTextures[_width>=640?0:1,text[i]-'0'];
                Place(glyph,left+i*advance,top,V(48),V(48));
            }
        }
        private Texture2D Texture(string path)
        { if(!_textures.TryGetValue(path,out Texture2D texture)) { texture=MinimapFrameVisual.Decode(_assets.Image(path));_textures.Add(path,texture); }return texture; }
        private Image Picture(string path)
        {
            var image=new Image { image=path==null?null:Texture(path),pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.StretchToFill };
            image.style.position=Position.Absolute;_root.Add(image);return image;
        }
        private static void Place(VisualElement element,float x,float y,float width,float height)
        { element.style.left=x;element.style.top=y;element.style.width=width;element.style.height=height; }
        public void Dispose()
        { foreach(Texture2D texture in _textures.Values) UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(_owner);UnityEngine.Object.Destroy(_settings); }
    }
}
