// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using DK2.Data.Wad;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace DK2.Unity
{
    public interface IHandCursorAssets { byte[] Load(HandCursorKind kind); }

    public sealed class InstallHandCursorAssets : IHandCursorAssets, IDisposable
    {
        private readonly WadArchive _archive;
        public InstallHandCursorAssets(string install) => _archive = WadArchive.Open(Path.Combine(install, "Data", "Sprite.WAD"));
        public byte[] Load(HandCursorKind kind) => _archive.Extract(HandCursor.Sprite(kind).FileName);
        public void Dispose() => _archive.Dispose();
    }

    public sealed class ClassicHandCursor : IDisposable
    {
        private readonly Dictionary<HandCursorKind, Texture2D> _textures = new Dictionary<HandCursorKind, Texture2D>();
        private readonly IHandCursorAssets _assets;
        private readonly GameObject _root;
        private readonly PanelSettings _panel;
        private readonly Image _visual;
        private readonly bool _manageSystemPointer;
        public PanelSettings Panel => _panel;
        private HandCursorKind _kind;
        private Texture2D _image;
        private readonly HandCursorAnimation _animation = new HandCursorAnimation();
        private int _frame;
        private bool _ownsPointer;
        private bool _previousVisible;

        public ClassicHandCursor(IHandCursorAssets assets, bool manageSystemPointer = true)
        {
            _assets = assets;
            _manageSystemPointer = manageSystemPointer;
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            _panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panel.referenceResolution = new Vector2Int(1024, 768);
            _panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; _panel.match = 1;
            _panel.sortingOrder = 1000;
            _root = new GameObject("Hand cursor HUD");
            UIDocument document = _root.AddComponent<UIDocument>(); document.panelSettings = _panel;
            document.rootVisualElement.pickingMode = PickingMode.Ignore;
            _visual = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            _visual.style.position = Position.Absolute;
            _visual.style.left = 0; _visual.style.top = 0; _visual.style.display = DisplayStyle.None;
            document.rootVisualElement.Add(_visual);
        }

        public void Update(HandCursorKind kind, bool visible, double nowSeconds)
        {
            if (!visible || kind == HandCursorKind.Default)
            {
                _image = null;
                _visual.style.display = DisplayStyle.None;
                _animation.Reset();
                RestorePointer();
                return;
            }
            _kind = kind;
            if (!_textures.TryGetValue(kind, out _image))
            {
                HandCursorSprite sprite = HandCursor.Sprite(kind);
                _image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!_image.LoadImage(_assets.Load(kind)) || _image.width != (int)sprite.FrameSize.X
                    || _image.height != (int)sprite.FrameSize.Y * sprite.FrameCount)
                {
                    UnityEngine.Object.Destroy(_image); _image = null;
                    throw new InvalidDataException("Cursor sheet does not match its native frame table: " + sprite.FileName);
                }
                _image.filterMode = FilterMode.Bilinear; _image.wrapMode = TextureWrapMode.Clamp;
                Color32[] pixels = _image.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                    if (pixels[i].r == 0 && pixels[i].g == 255 && pixels[i].b == 0) pixels[i].a = 0;
                _image.SetPixels32(pixels); _image.Apply();
                _textures.Add(kind, _image);
            }
            if (_manageSystemPointer && !_ownsPointer) { _previousVisible = Cursor.visible; _ownsPointer = true; }
            _frame = _animation.Advance(kind, (long)(nowSeconds * 1000));
            _visual.image = _image;
            _visual.style.display = DisplayStyle.Flex;
            if (_manageSystemPointer) Cursor.visible = false;
        }

        public void Draw(Vector2 pointerGui)
        {
            if (_image == null) return;
            HandCursorSprite sprite = HandCursor.Sprite(_kind);
            // The frame's claw tip goes on the pointer (HandCursor.Anchor), not DK2's hotspot.
            HandCursorPlacement placed = HandCursor.Place(_kind, _frame,
                new System.Numerics.Vector2(pointerGui.x, pointerGui.y), Screen.height);
            float scale = Screen.height / 768f;
            _visual.style.translate = new Translate(placed.Origin.X / scale, placed.Origin.Y / scale, 0);
            _visual.style.width = placed.Size.X / scale; _visual.style.height = placed.Size.Y / scale;
            _visual.uv = new Rect(0, 1f - (_frame + 1f) / sprite.FrameCount, 1, 1f / sprite.FrameCount);
        }

        private void RestorePointer()
        {
            if (!_ownsPointer) return;
            Cursor.visible = _previousVisible; _ownsPointer = false;
        }

        public void Dispose()
        {
            RestorePointer();
            foreach (Texture2D image in _textures.Values) UnityEngine.Object.Destroy(image);
            _textures.Clear();
            UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_panel);
            (_assets as IDisposable)?.Dispose();
        }
    }
}
