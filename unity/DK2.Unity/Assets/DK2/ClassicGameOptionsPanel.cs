// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using DK2.Data.Settings;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>Native in-game settings layout and GUI art, over the paused level.</summary>
    public sealed class ClassicGameOptionsPanel : IDisposable
    {
        private readonly InstallHudSources _sources;
        private readonly InstallClassicOptionsAssets _assets;
        private readonly GameObject _owner;
        private readonly VisualElement _root;
        private readonly List<Texture2D> _text = new List<Texture2D>();
        private readonly Dictionary<string, Texture2D> _art = new Dictionary<string, Texture2D>();
        private readonly List<VisualElement> _keyRows = new List<VisualElement>();
        private readonly TextScrollBar _keyScroll = new TextScrollBar();
        private readonly Dictionary<ushort, (Image Thumb, FrontEndRect Box)> _sliders = new Dictionary<ushort, (Image, FrontEndRect)>();
        private bool _dragging;
        private VisualElement _keyClip, _keyTrack;
        private Image _keyThumb;
        private float _keyLine;
        private int _barHeld, _height;
        private InGameOptionsPage _shown;
        private GameSettings _shownSettings;
        private GameAction? _shownCapture;
        private bool _visible;
        public InGameOptionsController Model { get; }
        public PanelSettings Panel { get; }
        private int Height => Panel.targetTexture != null ? Panel.targetTexture.height : Screen.height;
        public ClassicGameOptionsPanel(Transform parent, InstallHudSources sources, InGameOptionsController model)
        {
            _sources = sources; _assets = new InstallClassicOptionsAssets(sources); Model = model;
            Panel = ScriptableObject.CreateInstance<PanelSettings>();
            Panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            Panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; Panel.referenceResolution = new Vector2Int(1024, 768);
            Panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; Panel.match = 1; Panel.sortingOrder = 15;
            _owner = new GameObject("Dungeon game options"); _owner.transform.SetParent(parent, false);
            var doc = _owner.AddComponent<UIDocument>(); doc.panelSettings = Panel; _root = doc.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore; _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            _root.focusable = true;
            _root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (!_visible || Model.Settings.CapturingAction == null) return;
                KeyModifiers modifiers = (e.shiftKey ? KeyModifiers.Shift : 0) | (e.ctrlKey ? KeyModifiers.Control : 0) | (e.altKey ? KeyModifiers.Alt : 0);
                Model.Settings.CaptureKey(ClassicKeyBindings.ScanCode(e.keyCode), modifiers); e.StopPropagation();
            });
            Show(false);
        }
        public bool ContainsPointer(Vector3 position) => _visible;
        public void Show(bool visible)
        {
            if (visible && !_visible) Model.Open();
            if (!visible && _visible) Model.Hide();
            if (!visible) _dragging = false;
            _visible = visible; _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
        public void PollKeyboard()
        {
            if (!_visible || Model.Settings.CapturingAction == null) return;
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
                if ((int)key < (int)KeyCode.Mouse0 && Input.GetKeyDown(key))
                    Model.Settings.CaptureKey(ClassicKeyBindings.ScanCode(key), ClassicKeyBindings.Modifiers);
        }
        public void Draw()
        {
            if (!_visible) return;
            if (Model.Page != _shown || _height != Height || !_dragging &&
                (!ReferenceEquals(_shownSettings, Model.Settings.Pending) || Model.Settings.CapturingAction != _shownCapture)) Rebuild();
            foreach (var slider in _sliders)
            {
                (int lo, int hi) = Model.Settings.Range(slider.Key);
                FrontEndRect box = slider.Value.Box;
                slider.Value.Thumb.style.left = box.X + (box.Width - box.Height) * (Model.Settings.Value(slider.Key) - lo) / (hi - lo);
            }
            if (_barHeld != 0) _keyScroll.Step(_barHeld);
            UpdateKeyScroll();
        }
        private void Rebuild()
        {
            bool resetKeys = _shown != InGameOptionsPage.Keyboard || _height != Height;
            foreach (Texture2D image in _text) UnityEngine.Object.Destroy(image);
            _text.Clear(); _keyRows.Clear(); _sliders.Clear(); _dragging = false; _root.Clear(); _keyClip = null; _keyThumb = null; _keyTrack = null; _barHeld = 0;
            _shown = Model.Page; _shownSettings = Model.Settings.Pending; _shownCapture = Model.Settings.CapturingAction; _height = Height;
            if (_shown == InGameOptionsPage.None) return;
            foreach (InGameOptionsWidget widget in ClassicGameOptionsLayout.ForPage(_shown))
            {
                bool setting = widget.Kind == InGameOptionsWidgetKind.Slider || widget.Kind == InGameOptionsWidgetKind.Value;
                bool enabled = setting ? Model.Settings.Supports(widget.Setting.Value)
                    : widget.Kind != InGameOptionsWidgetKind.Navigate || Model.CanNavigate(widget.Destination);
                if (widget.Kind == InGameOptionsWidgetKind.Slider) { Slider(widget, enabled); continue; }
                if (widget.Kind == InGameOptionsWidgetKind.KeyboardList) { Keyboard(widget.Bounds, resetKeys); continue; }
                string value = widget.TextId != 0 ? _assets.Text(widget.TextId)
                    : setting ? SettingText(widget.Setting.Value) : "Unavailable";
                Action pressed = null;
                if (enabled)
                {
                    if (widget.Kind == InGameOptionsWidgetKind.Navigate) pressed = () => Model.Navigate(widget.Destination);
                    if (widget.Kind == InGameOptionsWidgetKind.Back) pressed = Model.Back;
                    if (widget.Kind == InGameOptionsWidgetKind.Continue) pressed = Model.Continue;
                    if (widget.Kind == InGameOptionsWidgetKind.Value) pressed = () =>
                    {
                        ushort id = widget.Setting.Value; int old = Model.Settings.Value(id);
                        Model.Settings.SetWidget(id, id == 133 ? old % 3 + 1 : old == 0 ? 1 : 0);
                    };
                }
                Image label = Text(value, widget.Bounds.X, widget.Bounds.Y, _root, pressed,
                    enabled && widget.Kind != InGameOptionsWidgetKind.Unavailable ? (widget.IsTitle ? 1 : 200 / 255f) : .35f);
                label.name = "option-" + widget.NativeIndex;
            }
        }
        private string SettingText(ushort id) => id == 133
            ? _assets.Text(new uint[] { 0, 1443, 281, 1442 }[Model.Settings.Pending.SoundQuality])
            : id == 135 ? _assets.Text(Model.Settings.Pending.Headphones ? 1465u : 111u)
            : _assets.Text(Model.Settings.Value(id) == 0 ? 110u : 109u);
        private void Slider(InGameOptionsWidget widget, bool enabled)
        {
            FrontEndRect box = widget.Bounds;
            Image track = Art("GUI\\Scroll Bars\\h-scroll_bar_bg", _root, box); track.name = "slider-" + widget.Setting.Value;
            float side = box.Height;
            Image left = Art("GUI\\Scroll Bars\\h-left_arrow", _root, new FrontEndRect(box.X - side, box.Y, side, side));
            Image right = Art("GUI\\Scroll Bars\\h-right_arrow", _root, new FrontEndRect(box.X + box.Width, box.Y, side, side));
            (int lo, int hi) = Model.Settings.Range(widget.Setting.Value);
            float t = (Model.Settings.Value(widget.Setting.Value) - lo) / (float)(hi - lo);
            Image thumb = Art("GUI\\Scroll Bars\\h-scroll_bar", _root, new FrontEndRect(box.X + t * (box.Width - side), box.Y, side, side));
            foreach (Image image in new[] { track, left, right, thumb }) image.tintColor = enabled ? Color.white : new Color(1, 1, 1, .35f);
            _sliders[widget.Setting.Value] = (thumb, box);
            if (!enabled) return;
            Action<float> set = x => Model.Settings.SetWidget(widget.Setting.Value,
                Mathf.RoundToInt(lo + Mathf.Clamp01((x - track.worldBound.x) / track.worldBound.width) * (hi - lo)));
            foreach (Image image in new[] { track, thumb })
            {
                image.pickingMode = PickingMode.Position;
                image.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; e.StopPropagation(); _dragging = true; image.CapturePointer(e.pointerId); set(e.position.x); });
                image.RegisterCallback<PointerMoveEvent>(e => { if (image.HasPointerCapture(e.pointerId)) { e.StopPropagation(); set(e.position.x); } });
                image.RegisterCallback<PointerUpEvent>(e => { _dragging = false; if (image.HasPointerCapture(e.pointerId)) image.ReleasePointer(e.pointerId); });
            }
            left.pickingMode = right.pickingMode = PickingMode.Position;
            left.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { e.StopPropagation(); Model.Settings.SetWidget(widget.Setting.Value, Model.Settings.Value(widget.Setting.Value) - 1); } });
            right.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { e.StopPropagation(); Model.Settings.SetWidget(widget.Setting.Value, Model.Settings.Value(widget.Setting.Value) + 1); } });
        }
        private void Keyboard(FrontEndRect bounds, bool reset)
        {
            _keyClip = new VisualElement { pickingMode = PickingMode.Position }; Place(_keyClip, bounds); _keyClip.style.overflow = Overflow.Hidden; _root.Add(_keyClip);
            _keyLine = _assets.Font(_height).LineHeight * 768f / _height;
            if (reset) _keyScroll.Reset(Mathf.CeilToInt(KeyboardSetup.Actions.Count * _keyLine), (int)bounds.Height);
            for (int i = 0; i < KeyboardSetup.Actions.Count; i++)
            {
                KeyboardAction entry = KeyboardSetup.Actions[i]; bool enabled = KeyboardSetup.IsBuilt(entry.Action);
                var row = new VisualElement { pickingMode = enabled ? PickingMode.Position : PickingMode.Ignore };
                row.name = "key-" + (int)entry.Action;
                Place(row, new FrontEndRect(0, i * _keyLine, bounds.Width, _keyLine)); _keyClip.Add(row); _keyRows.Add(row);
                GameKeyBinding binding = Model.Settings.Pending.Binding(entry.Action);
                float tint = enabled ? 200 / 255f : .35f;
                KeyCell(row, _assets.Text(entry.TextId), 0, 320, tint);
                KeyCell(row, binding.Modifiers == KeyModifiers.None ? "" : binding.Modifiers.ToString(), 320, 160, tint);
                KeyCell(row, Model.Settings.CapturingAction == entry.Action ? _assets.Text(1475)
                    : binding.ScanCode == 0 ? "-" : ClassicKeyBindings.Key(binding.ScanCode).ToString(), 480, bounds.Width - 480, tint);
                if (enabled) row.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { e.StopPropagation(); Model.Settings.BeginKeyCapture(entry.Action); _root.Focus(); } });
            }
            float side = 25.6f; float x = bounds.X + bounds.Width;
            Image up = Art("GUI\\Scroll Bars\\v-up_arrow", _root, new FrontEndRect(x, bounds.Y, side, side));
            Image down = Art("GUI\\Scroll Bars\\v-down_arrow", _root, new FrontEndRect(x, bounds.Y + bounds.Height - side, side, side));
            _keyTrack = Art("GUI\\Scroll Bars\\v-scroll_bar_bg", _root, new FrontEndRect(x, bounds.Y + side, side, bounds.Height - side * 2));
            _keyThumb = Art("GUI\\Scroll Bars\\v-scroll_bar", _root, new FrontEndRect(x, bounds.Y + side, side, 6.4f));
            Hold(up, -1); Hold(down, 1);
            _keyThumb.pickingMode = PickingMode.Position;
            _keyThumb.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { e.StopPropagation(); _keyThumb.CapturePointer(e.pointerId); } });
            _keyThumb.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_keyThumb.HasPointerCapture(e.pointerId)) return;
                e.StopPropagation(); float top = _keyTrack.worldBound.y - side;
                _keyScroll.DragTo((int)((e.position.y - top) * 2.5f), (int)(bounds.Height * 2.5f));
            });
            _keyThumb.RegisterCallback<PointerUpEvent>(e => _keyThumb.ReleasePointer(e.pointerId));
            UpdateKeyScroll();
        }
        private void KeyCell(VisualElement row, string text, float x, float width, float tint)
        {
            var cell = new VisualElement { pickingMode = PickingMode.Ignore }; Place(cell, new FrontEndRect(x, 0, width, _keyLine));
            cell.style.overflow = Overflow.Hidden; row.Add(cell); Text(text, 0, 0, cell, null, tint);
        }
        private void Hold(Image image, int direction)
        {
            image.pickingMode = PickingMode.Position;
            image.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { e.StopPropagation(); image.CapturePointer(e.pointerId); _barHeld = direction; } });
            image.RegisterCallback<PointerUpEvent>(e => { _barHeld = 0; image.ReleasePointer(e.pointerId); });
        }
        private void UpdateKeyScroll()
        {
            if (_keyClip == null) return;
            for (int i = 0; i < _keyRows.Count; i++) _keyRows[i].style.top = i * _keyLine - _keyScroll.Position;
            FrontEndRect box = ClassicGameOptionsLayout.ForPage(InGameOptionsPage.Keyboard).Single(w => w.Kind == InGameOptionsWidgetKind.KeyboardList).Bounds;
            (int top, int height) = _keyScroll.Thumb((int)(box.Height * 2.5f));
            _keyThumb.style.top = box.Y + top * .4f; _keyThumb.style.height = height * .4f;
        }
        private Image Text(string value, float x, float y, VisualElement parent, Action press, float tint)
        {
            Texture2D texture = ClassicOptionsPanel.TextTexture(_assets.Font(_height), value); _text.Add(texture);
            var image = new Image { image = texture, pickingMode = press == null ? PickingMode.Ignore : PickingMode.Position };
            Place(image, new FrontEndRect(x, y, texture.width * 768f / _height, texture.height * 768f / _height)); parent.Add(image);
            image.tintColor = new Color(tint, tint, tint, 1);
            if (press != null)
            {
                image.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { e.StopPropagation(); press(); } });
                image.RegisterCallback<PointerEnterEvent>(_ => image.tintColor = Color.white);
                image.RegisterCallback<PointerLeaveEvent>(_ => image.tintColor = new Color(tint, tint, tint, 1));
            }
            return image;
        }
        private Image Art(string name, VisualElement parent, FrontEndRect rect)
        {
            if (!_art.TryGetValue(name, out Texture2D texture)) { texture = MinimapFrameVisual.Decode(_sources.Texture(name)); _art.Add(name, texture); }
            var image = new Image { image = texture, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill }; Place(image, rect); parent.Add(image); return image;
        }
        private static void Place(VisualElement element, FrontEndRect rect)
        {
            element.style.position = Position.Absolute; element.style.left = rect.X; element.style.top = rect.Y; element.style.width = rect.Width; element.style.height = rect.Height;
        }
        public void Dispose()
        {
            foreach (Texture2D image in _text) UnityEngine.Object.Destroy(image);
            foreach (Texture2D image in _art.Values) UnityEngine.Object.Destroy(image);
            UnityEngine.Object.Destroy(_owner); UnityEngine.Object.Destroy(Panel);
        }
    }
}
