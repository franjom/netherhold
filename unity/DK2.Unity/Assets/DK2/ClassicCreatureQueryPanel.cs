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
        private Texture2D _texture;
        private string _previous;
        private int _height;
        public PanelSettings Panel => _settings;
        public ClassicCreatureQueryPanel(Transform parent,IClassicOptionsAssets assets)
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
            _texture=ClassicOptionsPanel.TextTexture(_assets.Font(height),snapshot.Text,Mathf.Max(1,(int)(438.4f*scale)));
            _text.image=_texture; _text.style.width=_texture.width/scale; _text.style.height=_texture.height/scale;
        }
        public bool ContainsPointer(Vector3 screenPosition,HudVisibility visibility) =>
            visibility.ShowNormalHud && visibility.AllowOrdinaryInput && _root.style.display.value!=DisplayStyle.None
                && new Rect(408,392,480,176).Contains(new Vector2(screenPosition.x*768/Screen.height,768-screenPosition.y*768/Screen.height));
        public void Dispose()
        {
            if(_texture!=null) UnityEngine.Object.Destroy(_texture);
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}
