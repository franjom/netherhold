// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
namespace DK2.Unity
{
    public sealed class ClassicCreatureQueryPanel : IDisposable
    {
        private readonly IClassicOptionsAssets _assets;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root,_clip;
        private readonly Image _text;
        private readonly Image[] _bar=new Image[4];
        private Texture2D _texture;
        private string _previous;
        private int _height;
        public PanelSettings Panel => _settings;
        public ClassicCreatureQueryPanel(Transform parent,IClassicOptionsAssets assets,Func<string,DK2.Art.Textures.RgbaImage> image=null)
        {
            _assets=assets;
            _settings=ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet=Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode=PanelScaleMode.ScaleWithScreenSize;
            _settings.referenceResolution=new Vector2Int(1024,768);
            _settings.screenMatchMode=PanelScreenMatchMode.MatchWidthOrHeight; _settings.match=1;
            _settings.sortingOrder=13;
            _owner=new GameObject("Creature query details"); _owner.transform.SetParent(parent,false);
            var document=_owner.AddComponent<UIDocument>(); document.panelSettings=_settings;
            _root=document.rootVisualElement; _root.pickingMode=PickingMode.Ignore;
            _clip=new VisualElement { pickingMode=PickingMode.Ignore };
            _clip.style.position=Position.Absolute;
            _clip.style.left=416; _clip.style.top=400; _clip.style.width=438.4f; _clip.style.height=115.2f;
            _clip.style.overflow=Overflow.Hidden; _root.Add(_clip);
            _text=new Image { pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.StretchToFill };
            _text.style.position=Position.Absolute; _text.style.left=0; _text.style.top=0;
            _clip.Add(_text); _root.style.display=DisplayStyle.None;
            // Menu40's text widget owns a bar (0x0052A0D0) at menu-local (1116,20) 64x288, here in
            // 1024x768 units: up arrow, track, thumb, down arrow (0x00414400).
            string[] art={"v-up_arrow","v-scroll_bar_bg","v-scroll_bar","v-down_arrow"};
            for(int i=0;i<4;i++)
            {
                _bar[i]=new Image { pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.StretchToFill };
                _bar[i].style.position=Position.Absolute; _bar[i].style.display=DisplayStyle.None;
                if(image!=null) _bar[i].image=MinimapFrameVisual.Decode(image("GUI\\Scroll Bars\\"+art[i])); _root.Add(_bar[i]);
            }
            const float x=854.4f,y=400,w=25.6f,h=115.2f,unit=0.4f;
            Place(_bar[0],x,y,w,64*unit); Place(_bar[1],x,y+64*unit,w,h-128*unit);
            // Query mode resets the position to the top every draw (0x004242F0), so the thumb stays there.
            Place(_bar[2],x+20*unit,y+(64+4)*unit,28*unit,(TextScrollBar.MinimumThumb-8)*unit); Place(_bar[3],x,y+h-64*unit,w,64*unit);
        }
        public void Draw(CreatureQuerySnapshot snapshot,HudVisibility visibility,bool otherPanelOpen=false,Vector2Int viewport=default)
        {
            bool visible=visibility.ShowNormalHud && !otherPanelOpen && !string.IsNullOrEmpty(snapshot.Text);
            _root.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            if(!visible) return;
            int height=viewport.y>0?viewport.y:Screen.height;
            if(_previous==snapshot.Text && _height==height) return;
            _previous=snapshot.Text; _height=height;
            if(_texture!=null) UnityEngine.Object.Destroy(_texture);
            float scale=height/768f;
            // 0x0052A330 measures 64 narrower than the widget: text taller than it shows the bar, which
            // in Query mode never scrolls (0x004242F0 draws the text at offset 0).
            _texture=ClassicOptionsPanel.TextTexture(_assets.Font(height),snapshot.Text,Mathf.Max(1,(int)(412.8f*scale)));
            bool overflow=_texture.height>115.2f*scale;
            if(!overflow) { UnityEngine.Object.Destroy(_texture); _texture=ClassicOptionsPanel.TextTexture(_assets.Font(height),snapshot.Text,Mathf.Max(1,(int)(438.4f*scale))); }
            foreach(Image part in _bar) part.style.display=overflow && part.image!=null?DisplayStyle.Flex:DisplayStyle.None;
            _text.image=_texture; _text.style.width=_texture.width/scale; _text.style.height=_texture.height/scale;
        }
        private static void Place(VisualElement e,float x,float y,float w,float h)
        { e.style.left=x; e.style.top=y; e.style.width=w; e.style.height=h; }
        public bool ContainsPointer(Vector3 screenPosition,HudVisibility visibility) =>
            visibility.ShowNormalHud && visibility.AllowOrdinaryInput && _root.style.display.value!=DisplayStyle.None
                && new Rect(408,392,480,176).Contains(new Vector2(screenPosition.x*768/Screen.height,768-screenPosition.y*768/Screen.height));
        public void Dispose()
        {
            if(_texture!=null) UnityEngine.Object.Destroy(_texture);
            foreach(Image part in _bar) if(part.image!=null) UnityEngine.Object.Destroy(part.image);
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}
