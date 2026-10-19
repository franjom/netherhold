// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using DK2.Data.Model;
using DK2.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>Native bottom-panel artwork, retained choices and captured input.</summary>
    public sealed class ClassicBottomPanel : IDisposable
    {
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly IClassicBottomPanelAssets _assets;
        private readonly BottomPanelView _view;
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private readonly Image[] _tabs = new Image[4], _fills = new Image[64], _rail = new Image[16];
        private readonly Image _railMetal, _railEnd;
        private readonly Image[] _tabSelected = new Image[4];
        private readonly Image[] _icons = new Image[6], _selection = new Image[6];
        private readonly BottomPanelChoice[] _slots = new BottomPanelChoice[6];
        private readonly Image _previous, _next;
        private readonly Image _reaperBackground, _reaper, _reaperBlink;
        private readonly Texture2D[,] _reaperArcs = new Texture2D[4, 2];
        private readonly Texture2D[] _reaperQuarter = new Texture2D[4];
        private readonly Texture2D[] _timerDigits = new Texture2D[10];
        private readonly Image[] _timerImages = new Image[2];
        private BottomPanelCategory _category = BottomPanelCategory.Rooms;
        private int _page, _pages = 1, _captured = -1, _pressed = -1, _pressedButton;
        private bool _visible = true, _allowInput = true;
        private Vector2Int _viewport;
        private string _descriptionText = "";
        private int _hovered = -1, _hoverCost;
        private BottomPanelChoice _hoverChoice;
        private bool _hoverUnknown;
        private HoverTextSnapshot _widgetHover = HoverTextSnapshot.Empty;
        public HoverTextSnapshot HoverText => !string.IsNullOrEmpty(_widgetHover.TooltipName) ? _widgetHover
            : new HoverTextSnapshot(_descriptionText, "", default);
        public event Action<BottomPanelSelection> Selected;
        public event Action<RoomTypeId> NavigateRoom;
        public PanelSettings Panel => _settings;
        public BottomPanelCategory Category => _category;
        public bool HasPointerCapture => _captured >= 0;
        private int Width => _viewport.x > 0 ? _viewport.x : Screen.width;
        private int Height => _viewport.y > 0 ? _viewport.y : Screen.height;
        private float V(int value) => value * Height / 1920f;
        public Rect Bounds => new Rect(V(472), V(1460), Mathf.Max(0, Width - V(760)), Height - V(1460));
        public Rect ReaperBounds => new Rect(Width - V(288), V(1612), V(264), V(264));

        public ClassicBottomPanel(Transform parent, IClassicBottomPanelAssets assets, BottomPanelView view)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.scale = 1;
            _settings.sortingOrder = 12;
            _owner = new GameObject("Keeper bottom panel");
            if (parent != null) _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);
            for (int i = 0; i < _fills.Length; i++) _fills[i] = Picture(Texture("GUI\\Windows\\Panel-BG"));
            // The tab strip's background 0x00418EE0: Bar-00 (resource +0x798) tiled across, then
            // Bar-02 (+0x16E3) and Bar-03 (+0x16FE) at the strip's top; under the tabs.
            for (int i = 0; i < _rail.Length; i++) _rail[i] = Picture(Texture("GUI\\Windows\\Bar-00"));
            _railMetal = Picture(Texture("GUI\\Windows\\Bar-02"));
            _railEnd = Picture(Texture("GUI\\Windows\\Bar-03"));
            string[] tabs = { "t-creatures", "t-rooms", "t-spells", "t-workshop" };
            for (int i = 0; i < 4; i++) _tabs[i] = Interactive(Texture("GUI\\Tabs\\" + tabs[i]), 100 + i);
            // The selected tab draws its widget's alternate art (+0x40: catalogue 173/174/175/178)
            // over the icon; unselected tabs are not dimmed.
            string[] selected = { "selected-creature", "selected-room", "selected-spell", "selected-workshop" };
            for (int i = 0; i < 4; i++) _tabSelected[i] = Picture(Texture("GUI\\Icons\\" + selected[i]));
            for (int i = 0; i < 6; i++)
            { _icons[i] = Interactive(null, i); _selection[i] = Picture(null); }
            _previous = Interactive(Texture("GUI\\Pages\\page_back"), 200);
            _next = Interactive(Texture("GUI\\Pages\\page_forward"), 201);
            _reaperBackground = Picture(Texture("GUI\\Spells\\reaper_talisman-00"));
            _reaper = Interactive(Texture("GUI\\Spells\\reaper_talisman-01"), 300);
            _reaperBlink = Picture(null);
            for (int i = 0; i < 10; i++) _timerDigits[i] = Texture("GUI\\Spells\\RT-" + i.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < 2; i++) _timerImages[i] = Picture(null);
            for (int pieces = 1; pieces <= 4; pieces++)
            {
                _reaperArcs[pieces - 1, 0] = TalismanArc(Texture("GUI\\Spells\\reaper_talisman-01"), pieces, false);
                _reaperArcs[pieces - 1, 1] = TalismanArc(Texture("GUI\\Spells\\reaper_talisman-03"), pieces, false);
                _reaperQuarter[pieces - 1] = TalismanArc(Texture("GUI\\Spells\\reaper_talisman-03"), pieces, true);
            }
            _reaper.RegisterCallback<PointerEnterEvent>(_ => SetDescription(_assets.Text(
                _view.Reaper?.Selectable != true ? 0x8C4u : _view.ReaperOneShotReady ? 0x8BFu : 0x8BEu)));
            _root.RegisterCallback<PointerUpEvent>(PointerUp);
            _root.RegisterCallback<PointerCancelEvent>(_ => CancelPointerGesture());
            _root.RegisterCallback<PointerCaptureOutEvent>(_ => { _captured = -1; _pressed = -1; });
            _root.RegisterCallback<WheelEvent>(Wheel);
        }

        public void SetVisibility(HudVisibility visibility)
        {
            _visible = visibility.ShowNormalHud; _allowInput = visibility.AllowOrdinaryInput;
            if (!_visible || !_allowInput) CancelPointerGesture();
            if (!_visible || !_allowInput) { _hovered = -1; SetDescription(""); }
            _root.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
        public bool ContainsPointer(Vector3 screenPosition) => _visible && _allowInput &&
            (HasPointerCapture || Bounds.Contains(new Vector2(screenPosition.x, Height - screenPosition.y)) ||
                ReaperBounds.Contains(new Vector2(screenPosition.x, Height - screenPosition.y)));
        public void CancelPointerGesture()
        {
            int pointer = _captured; _captured = -1; _pressed = -1;
            if (pointer >= 0 && _root.HasPointerCapture(pointer)) _root.ReleasePointer(pointer);
        }
        public void SelectCategory(BottomPanelCategory category) { _category = category; _page = 0; CancelPointerGesture(); }

        public void Draw(Vector2Int viewport = default)
        {
            _viewport = viewport;
            if (!_visible) return;
            // The stone and the rail both span the whole screen width, under the minimap at the
            // left and the Reaper talisman at the right (owner's DK2 screenshot, 2026-10-19).
            float fillSize = V(160);
            for (int i = 0; i < _fills.Length; i++)
            {
                float left = i * fillSize;
                bool show = left < Width;
                _fills[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show) Place(_fills[i], left, V(1600), Mathf.Min(fillSize, Width - left), V(300));
            }
            // Wood tiles are U(512,88) from x 0, a quarter-tile below the strip's top (read). The
            // metal pair covers the last 480 virtual units from x 2080 (the 0x820 constant); its
            // split into two 240-unit halves is inferred from the art and the owner's screenshot.
            float railLeft = 0, metalLeft = Mathf.Max(railLeft, Width - V(480)), tile = V(512);
            for (int i = 0; i < _rail.Length; i++)
            {
                float left = railLeft + i * tile;
                bool show = left < metalLeft;
                _rail[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show) Place(_rail[i], left, V(1460 + 22), Mathf.Min(tile, metalLeft - left), V(88));
            }
            Place(_railMetal, metalLeft, V(1460), V(240), V(128));
            Place(_railEnd, metalLeft + V(240), V(1460), V(240), V(128));
            for (int i = 0; i < 4; i++)
            {
                Place(_tabs[i], BottomPanelLayout.Tab(i));
                bool tabShown = _view.TabVisible((BottomPanelCategory)i);
                _tabs[i].style.display = tabShown ? DisplayStyle.Flex : DisplayStyle.None;
                _tabs[i].tintColor = _view.TabDimmed((BottomPanelCategory)i) ? new Color32(128, 128, 128, 255) : Color.white;
                Place(_tabSelected[i], BottomPanelLayout.Tab(i));
                _tabSelected[i].style.display = tabShown && i == (int)_category ? DisplayStyle.Flex : DisplayStyle.None;
            }
            int count = 0;
            for (int i = 0; i < _view.Choices.Count; i++)
            { BottomPanelChoice choice = _view.Choices[i]; if (choice.Visible && choice.Category == _category) count++; }
            _pages = Math.Max(1, (count + 5) / 6); _page = Math.Min(_page, _pages - 1);
            int skip = _page * 6, index = 0, visible = 0;
            for (int choiceIndex = 0; choiceIndex < _view.Choices.Count; choiceIndex++)
            {
                BottomPanelChoice choice = _view.Choices[choiceIndex];
                if (!choice.Visible || choice.Category != _category) continue;
                if (visible++ < skip || index >= 6) continue;
                _slots[index++] = choice;
            }
            while (index < 6) _slots[index++] = null;
            for (int i = 0; i < 6; i++)
            {
                BottomPanelChoice choice = _slots[i];
                bool show = choice != null;
                _icons[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                _selection[i].style.display = show && choice.Selected ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                string icon = choice.Unknown ? choice.Kind == BottomPanelChoiceKind.Room ? "GUI\\Rooms\\tba" :
                    choice.Kind == BottomPanelChoiceKind.Spell ? "GUI\\Spells\\s-tba" : "GUI\\Traps\\w-tba" : _assets.Icon(choice.Key);
                _icons[i].image = string.IsNullOrEmpty(icon) ? null : Texture(icon);
                _icons[i].tintColor = choice.Affordable ? Color.white : new Color(100 / 255f, 100 / 255f, 100 / 255f, 1);
                Place(_icons[i], BottomPanelLayout.Cell(i));
                if (choice.Selected)
                {
                    string selected = choice.Kind == BottomPanelChoiceKind.Room ? "GUI\\Icons\\selected-room" :
                        choice.Kind == BottomPanelChoiceKind.Spell ? "GUI\\Icons\\selected-spell" :
                        choice.Kind == BottomPanelChoiceKind.Door ? "GUI\\Icons\\selected-door" : "GUI\\Icons\\selected-trap";
                    _selection[i].image = Texture(selected); Place(_selection[i], BottomPanelLayout.Cell(i));
                }
                _icons[i].SetEnabled(_allowInput);
            }
            Place(_previous, V(472), V(1612), V(64), V(132));
            Place(_next, V(472), V(1744), V(64), V(132));
            _previous.tintColor = _page > 0 ? Color.white : Color.gray;
            _next.tintColor = _page + 1 < _pages ? Color.white : Color.gray;
            RefreshHover();
            Rect reaperBounds = ReaperBounds;
            Place(_reaperBackground, reaperBounds.x, reaperBounds.y, reaperBounds.width, reaperBounds.height);
            Place(_reaper, reaperBounds.x, reaperBounds.y, reaperBounds.width, reaperBounds.height);
            Place(_reaperBlink, reaperBounds.x, reaperBounds.y, reaperBounds.width, reaperBounds.height);
            _reaperBlink.style.display = DisplayStyle.None;
            BottomPanelChoice reaper = _view.Reaper;
            _reaper.style.display = reaper != null && reaper.Visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (reaper != null && reaper.Visible)
            {
                bool nativeSelected = reaper.Selected && reaper.Key.Value == 5;
                if (_view.ReaperNormallyUnlocked)
                {
                    _reaper.image = Texture(nativeSelected ? "GUI\\Spells\\reaper_talisman-03" :
                        reaper.Selectable ? "GUI\\Spells\\reaper_talisman-02" : "GUI\\Spells\\reaper_talisman-01");
                    _reaper.tintColor = reaper.Selectable ? Color.white : new Color(170 / 255f, 170 / 255f, 170 / 255f, 1);
                }
                else
                {
                    int pieces = Mathf.Clamp(_view.TalismanPieces, 1, 4) - 1;
                    _reaper.image = _reaperArcs[pieces, nativeSelected ? 1 : 0];
                    _reaper.tintColor = reaper.Selectable ? Color.white : new Color(.6f, .6f, .6f, 1);
                    if (_view.ReaperOneShotReady && ((long)(Time.unscaledTimeAsDouble * 1000) / 500 & 1) != 0)
                    {
                        _reaperBlink.image = _reaperQuarter[pieces]; _reaperBlink.tintColor = _reaper.tintColor;
                        _reaperBlink.style.display = DisplayStyle.Flex;
                    }
                }
            }
            int? seconds = _view.ReaperSecondsRemaining;
            bool timer = seconds.HasValue && seconds.Value < 100;
            bool twoDigits = timer && seconds.Value >= 10;
            _timerImages[0].style.display = timer ? DisplayStyle.Flex : DisplayStyle.None;
            _timerImages[1].style.display = twoDigits ? DisplayStyle.Flex : DisplayStyle.None;
            if (timer)
            {
                _timerImages[0].image = _timerDigits[twoDigits ? seconds.Value / 10 : seconds.Value];
                Place(_timerImages[0], reaperBounds.x + V(twoDigits ? 0 : 64), reaperBounds.y + V(72), V(128), V(128));
                if (twoDigits)
                {
                    _timerImages[1].image = _timerDigits[seconds.Value % 10];
                    Place(_timerImages[1], reaperBounds.x + V(128), reaperBounds.y + V(72), V(128), V(128));
                }
            }
        }

        private Image Interactive(Texture2D texture, int action)
        {
            Image image = Picture(texture); image.pickingMode = PickingMode.Position;
            image.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (!_allowInput || evt.button != 0 && evt.button != 1) return;
                bool roomSecondary = evt.button == 1 && action >= 0 && action < 6 &&
                    _slots[action] != null && _slots[action].Kind == BottomPanelChoiceKind.Room;
                if (evt.button == 1 && !roomSecondary) return;
                _pressed = action; _pressedButton = evt.button;
                _captured = evt.pointerId; _root.CapturePointer(evt.pointerId); evt.StopPropagation();
            });
            image.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _hovered = action; _hoverChoice = null;
                if (action >= 100 && action < 104)
                    _widgetHover = HudWidgetText.Hover(HudWidgetText.Tab((BottomPanelCategory)(action - 100)), _assets.Text);
                else RefreshHover();
            });
            image.RegisterCallback<PointerLeaveEvent>(_ => { _hovered = -1; _hoverChoice = null; _widgetHover = HoverTextSnapshot.Empty; SetDescription(""); });
            return image;
        }
        private void PointerUp(PointerUpEvent evt)
        {
            if (_captured != evt.pointerId) return;
            if (evt.button != _pressedButton) return;
            int action = _pressed;
            bool secondary = _pressedButton == 1;
            bool inside = action == 300 ? _reaper.worldBound.Contains(evt.position) :
                action >= 100 && action < 104 ? _tabs[action - 100].worldBound.Contains(evt.position) :
                action == 200 ? _previous.worldBound.Contains(evt.position) : action == 201 ? _next.worldBound.Contains(evt.position) :
                action >= 0 && action < 6 && _icons[action].worldBound.Contains(evt.position);
            CancelPointerGesture(); evt.StopPropagation();
            if (!inside || !_allowInput) return;
            if (secondary)
            {
                BottomPanelChoice room = _slots[action];
                if (room != null && room.Visible && room.Kind == BottomPanelChoiceKind.Room)
                    NavigateRoom?.Invoke((RoomTypeId)room.Key.Value);
                return;
            }
            if (action == 300)
            {
                if (_view.Reaper != null && _view.Select(_view.Reaper.Key)) Selected?.Invoke(_view.Selection);
            }
            else if (action >= 100 && action < 104) SelectCategory((BottomPanelCategory)(action - 100));
            else if (action == 200) _page = Math.Max(0, _page - 1);
            else if (action == 201) _page = Math.Min(_pages - 1, _page + 1);
            else if (_slots[action] != null && _view.Select(_slots[action].Key)) Selected?.Invoke(_view.Selection);
        }
        private void Wheel(WheelEvent evt)
        {
            if (!_visible || !_allowInput || !Bounds.Contains(evt.mousePosition)) return;
            _page = Mathf.Clamp(_page + (evt.delta.y > 0 ? 1 : -1), 0, _pages - 1); evt.StopPropagation();
        }
        private void SetDescription(string text)
        {
            _descriptionText = text;
        }
        private void RefreshHover()
        {
            if (_hovered < 0 || _hovered >= 6) return;
            BottomPanelChoice choice = _slots[_hovered];
            if (choice == null) { _hoverChoice = null; SetDescription(""); return; }
            if (_hoverChoice == choice && _hoverCost == choice.Cost && _hoverUnknown == choice.Unknown) return;
            _hoverChoice = choice; _hoverCost = choice.Cost; _hoverUnknown = choice.Unknown;
            SetDescription(choice.Unknown ? "?" : choice.Name + "  " + choice.Cost.ToString(CultureInfo.InvariantCulture));
        }
        private Texture2D Texture(string name)
        {
            if (_textures.TryGetValue(name, out Texture2D texture)) return texture;
            texture = MinimapFrameVisual.Decode(_assets.Image(name)); _textures.Add(name, texture); return texture;
        }
        private static Texture2D TalismanArc(Texture2D source, int pieces, bool lastQuarterOnly)
        {
            Color32[] pixels = source.GetPixels32();
            for (int y = 0; y < source.height; y++)
            for (int x = 0; x < source.width; x++)
            {
                float theta = Mathf.Atan2(x + .5f - source.width / 2f, source.height / 2f - y - .5f) * Mathf.Rad2Deg;
                float sweep = Mathf.Repeat(270 - theta, 360);
                if (sweep > pieces * 90 || lastQuarterOnly && sweep <= (pieces - 1) * 90)
                    pixels[y * source.width + x].a = 0;
            }
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false, true); return texture;
        }
        private Image Picture(Texture2D texture)
        {
            var image = new Image { image = texture, pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute; _root.Add(image); return image;
        }
        private void Place(VisualElement image, BottomPanelRect rectangle) => Place(image, V(rectangle.X), V(rectangle.Y), V(rectangle.Width), V(rectangle.Height));
        private static void Place(VisualElement element, float x, float y, float width, float height)
        { element.style.left = x; element.style.top = y; element.style.width = width; element.style.height = height; }
        public void Dispose()
        {
            CancelPointerGesture();
            foreach (Texture2D texture in _textures.Values) UnityEngine.Object.Destroy(texture);
            foreach (Texture2D texture in _reaperArcs) UnityEngine.Object.Destroy(texture);
            foreach (Texture2D texture in _reaperQuarter) UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(_settings); UnityEngine.Object.Destroy(_owner);
            if (_assets is IDisposable disposable) disposable.Dispose();
        }
    }
}
