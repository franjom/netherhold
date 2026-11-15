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
    /// <summary>The in-game options menu's Unity side: <see cref="OptionsHud"/> (ADR-0805) drawn by a
    /// <see cref="HudCanvas"/>. The runner reads presses through <see cref="ActionAt"/>.</summary>
    public sealed class ClassicOptionsPanel : IDisposable
    {
        private static readonly RgbaImage Blank = new RgbaImage(1, 1, new byte[4]);
        private readonly OptionsHud _hud;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly HudCanvas _canvas;
        private readonly HudFrame _frame = new HudFrame();
        private Vector2Int _viewport;
        public PanelSettings Panel => _settings;
        private int Width => _viewport.x > 0 ? _viewport.x : Screen.width;
        private int Height => _viewport.y > 0 ? _viewport.y : Screen.height;
        public ClassicOptionsPanel(Transform parent, IClassicOptionsAssets assets)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            _hud = new OptionsHud(assets.Text, assets.Font);
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.scale = 1;
            _settings.sortingOrder = 15;
            _owner = new GameObject("Dungeon options"); _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            // Set text only: nothing to load from the catalogue.
            _canvas = new HudCanvas(_ => Blank, null);
            _root.Add(_canvas);
            SetVisible(false);
        }
        public void SetSupported(ClassicOptionsAction action, bool supported) => _hud.SetSupported(action, supported);
        public void SetVisible(bool visible)
        { _hud.SetVisible(visible); _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None; }
        public void SetPage(ClassicOptionsPage page, ClassicSessionAction confirmation) => _hud.SetPage(page, confirmation);
        public bool ContainsPointer(Vector3 screenPosition) => _hud.Visible;
        public ClassicOptionsAction ActionAt(Vector3 screenPosition)
        {
            if (Height <= 0) return ClassicOptionsAction.None;
            Vector2 at = Virtual(screenPosition);
            return _hud.ActionAt(at.x, at.y);
        }
        /// <summary>A screen position (bottom-up) in virtual units.</summary>
        private Vector2 Virtual(Vector3 screen)
        {
            float toVirtual = 1920f / Height;
            return new Vector2(screen.x * toVirtual, 1920 - screen.y * toVirtual);
        }
        public void Draw(Vector3 pointer, Vector2Int viewport = default)
        {
            _viewport = viewport;
            if (!_hud.Visible) return;
            var screen = new HudScreen(Width, Height);
            Vector2 at = Virtual(pointer);
            _frame.Clear();
            _hud.Build(screen, (at.x, at.y), _frame);
            _canvas.Present(_frame, screen.Scale, 1);
        }
        /// <summary>Text set by <see cref="HudTextPixels"/>, the one rasterizer every panel and engine shares.</summary>
        internal static Texture2D TextTexture(BitmapFont font,string text,int wrapWidth=0)
        {
            byte[] set=HudTextPixels.Render(new HudText(font,text,wrapWidth),out int width,out int height);
            // Unity textures run bottom-up.
            var rows=new byte[set.Length]; int row=width*4;
            for(int y=0;y<height;y++) Buffer.BlockCopy(set,y*row,rows,(height-1-y)*row,row);
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false)
                { filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
            texture.LoadRawTextureData(rows); texture.Apply(false,true); return texture;
        }
        public void Dispose()
        {
            _canvas.Dispose();
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}
