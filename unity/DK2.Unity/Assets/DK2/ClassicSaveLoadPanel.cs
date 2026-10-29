// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Data.Save;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>Original-font named-save browser; its native destinations are Options23/24 and front-end8.</summary>
    public sealed class ClassicSaveLoadPanel : IDisposable
    {
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly IClassicOptionsAssets _assets;
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private SaveSlotsView _view;
        private int _height, _revision = -1, _first;
        private bool _save, _frontEnd, _visible;
        private string _error = "";
        public event Action Submit;
        public event Action Back;
        public event Action Continue;

        public ClassicSaveLoadPanel(Transform parent, IClassicOptionsAssets assets)
        {
            _assets = assets;
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _settings.referenceResolution = new Vector2Int(1024,768);
            _settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; _settings.match = 1;
            _settings.sortingOrder = 16;
            _owner = new GameObject("Saved games"); _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            _root.style.display = DisplayStyle.None;
        }

        public void Show(SaveSlotsView view, bool visible, bool save, bool frontEnd = false)
        {
            bool changed = _view != view || _save != save || _frontEnd != frontEnd || visible && !_visible;
            _view = view; _visible = visible; _save = save; _frontEnd = frontEnd;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (changed) { _revision = -1; _first = 0; _error = ""; }
            if (!visible || view == null) return;
            if (!_frontEnd && _save)
            {
                foreach (char c in Input.inputString)
                {
                    if (c == '\b') { if (view.Name.Length > 0) view.SetName(view.Name.Substring(0, view.Name.Length - 1)); }
                    else if (c == '\n' || c == '\r') Submit?.Invoke();
                    else if (!char.IsControl(c)) view.SetName(view.Name + c);
                }
            }
            if (Input.mouseScrollDelta.y != 0)
            {
                int next = Mathf.Clamp(_first - (int)Input.mouseScrollDelta.y, 0, Math.Max(0, view.Entries.Count - 7));
                if (next != _first) { _first = next; _revision = -1; }
            }
            if (_height != Screen.height || _revision != view.Revision) Rebuild();
        }

        public void Error(string message) { _error = message; _revision = -1; }

        private void Rebuild()
        {
            _height = Screen.height; _revision = _view.Revision;
            _root.Clear();
            foreach (Texture2D texture in _textures) UnityEngine.Object.Destroy(texture);
            _textures.Clear();
            float x = _frontEnd ? 112 : 8, y = _frontEnd ? 188.8f : 112;
            if (!_frontEnd) Text(_assets.Text(_save ? 201u : 143u), 8, 72, null);
            int shown = Math.Min(_frontEnd ? 11 : 7, _view.Entries.Count - _first);
            for (int i = 0; i < shown; i++)
            {
                int index = _first + i;
                SavedSessionEntry entry = _view.Entries[index];
                string caption = entry.Header.DisplayName;
                if (_frontEnd) caption += "   " + entry.Header.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                Image image = Text(caption, x, y + i * 32, () => { _view.Select(index); _revision = -1; });
                image.tintColor = index == _view.SelectedIndex ? Color.white : new Color(200/255f,200/255f,200/255f);
            }
            if (!_frontEnd)
            {
                if (_save) Text(_view.Name + "_", 8, 440, () => Submit?.Invoke());
                else if (_view.Selected != null) Text(_assets.Text(143), 8, 440, () => Submit?.Invoke());
                Text(_assets.Text(142), 8, 512, () => Continue?.Invoke());
                Text(_assets.Text(20), 512, 512, () => Back?.Invoke());
            }
            if (!string.IsNullOrEmpty(_error)) Text(_error, x, _frontEnd ? 600 : 480, null);
        }

        private Image Text(string value, float x, float y, Action press)
        {
            Texture2D texture = ClassicOptionsPanel.TextTexture(_assets.Font(_height), value);
            _textures.Add(texture);
            var image = new Image { image = texture, pickingMode = press == null ? PickingMode.Ignore : PickingMode.Position };
            image.style.position = Position.Absolute; image.style.left = x; image.style.top = y;
            image.style.width = texture.width * 768f / _height;
            image.style.height = texture.height * 768f / _height;
            if (press != null) image.RegisterCallback<PointerDownEvent>(evt =>
            { if (evt.button == 0) { evt.StopPropagation(); press(); } });
            _root.Add(image); return image;
        }

        public void Dispose()
        {
            foreach (Texture2D texture in _textures) UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(_settings);
        }
    }
}
