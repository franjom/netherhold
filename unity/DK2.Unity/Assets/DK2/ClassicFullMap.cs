// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Core;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
namespace DK2.Unity
{
    public sealed class ClassicFullMap : IDisposable
    {
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly Image _map;
        private readonly Material _material;
        private readonly RenderTexture _target;
        private readonly DungeonMinimap _source;
        private bool _visible;
        public PanelSettings Panel => _settings;
        public ClassicFullMap(Transform parent, DungeonMinimap source)
        {
            _source = source;
            Shader shader = Resources.Load<Shader>("DK2FullMap");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Full-map shader unavailable.");
            _material = new Material(shader);
            _material.SetTexture("_Classes",source.ClassTexture); _material.SetTexture("_Palette",source.PaletteTexture);
            _target = new RenderTexture(source.MapWidth,source.MapHeight,0,RenderTextureFormat.ARGB32)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _target.Create();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.sortingOrder = 20;
            _owner = new GameObject("Dungeon full map"); _owner.transform.SetParent(parent,false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            _root.style.backgroundColor = Color.black;
            _map = new Image { image = _target, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            _map.style.position = Position.Absolute; _root.Add(_map);
            SetVisible(false);
        }
        public void SetVisible(bool visible)
        {
            _visible=visible; _root.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
        }
        public void Draw(Vector2Int viewport=default)
        {
            if (!_visible) return;
            int width=viewport.x>0?viewport.x:Screen.width, height=viewport.y>0?viewport.y:Screen.height;
            ClassicFullMapLayout layout = ClassicFullMapLayout.Fit(width,height,_source.MapWidth,_source.MapHeight);
            _map.style.left=layout.Origin.X; _map.style.top=layout.Origin.Y;
            _map.style.width=layout.Size.X; _map.style.height=layout.Size.Y;
            Graphics.Blit(null,_target,_material);
        }
        public TilePos ResolvePointer(Vector3 screenPosition) =>
            ClassicFullMapLayout.Fit(Screen.width,Screen.height,_source.MapWidth,_source.MapHeight)
                .ResolveTile(new System.Numerics.Vector2(screenPosition.x,Screen.height-screenPosition.y));
        public void Dispose()
        {
            _target.Release(); UnityEngine.Object.Destroy(_target); UnityEngine.Object.Destroy(_material);
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}
