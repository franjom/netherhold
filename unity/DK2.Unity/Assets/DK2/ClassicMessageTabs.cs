// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity
{
    /// <summary>Menu39's message tabs and Review Objective button, the message panel (menu40 in
    /// message mode) and the objective panel (menu33); docs/research/message-tabs.md. Sounds
    /// 0x320..0x322, the Ally button (mode 3) and name substitution for types 82..87 are not built.</summary>
    public sealed class ClassicMessageTabs : IDisposable
    {
        private const int MenuX = 1112, MenuY = 1460, ButtonWidth = 88, ButtonHeight = 128;
        private const int Slots = MessageTabsView.RecordsExamined;
        private static readonly string[] Families = { "mt-info", "mt-info", "mt-creature", "mt-fight", "mt-objective", "mt-allymsg", "mt-playerexit" };
        private static readonly string[,] FrameNames = Names();

        private static string[,] Names()
        {
            var names = new string[Families.Length, 3];
            for (int family = 0; family < Families.Length; family++)
                for (int frame = 0; frame < 3; frame++) names[family, frame] = $"GUI\\Tabs\\Messages\\{Families[family]}-0{frame}";
            return names;
        }

        private readonly IClassicBottomPanelAssets _assets;
        private readonly IClassicOptionsAssets _textAssets;
        private readonly MessageTabsView _view;
        private readonly Func<int, string> _resolve;
        private readonly Func<int, string> _briefing;
        private readonly GameObject _owner;
        private readonly PanelSettings _settings;
        private readonly VisualElement _root;
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private readonly Image _objective;
        private readonly Image[] _slots = new Image[Slots];
        private readonly VisualElement _panel, _objectivePanel;
        private readonly Image _panelText, _objectiveText, _exit, _tick, _goto;
        private readonly Func<PlayerMessage, (float X, float Y)?> _locate;
        private readonly Action<float, float> _goTo;
        private static readonly string[] FramePieces = { "tl", "t", "tr", "l", "m", "r", "bl", "b", "br" };
        private readonly VisualElement[] _frame = new VisualElement[9];
        private static readonly HudIndicator[] Drawn =
        {
            HudIndicator.TimeRemaining, HudIndicator.Score, HudIndicator.CurrentHolder,
            HudIndicator.HeroesKilled, HudIndicator.SlapsRemaining, HudIndicator.NextRoom,
        };
        private static readonly string[] IndicatorArt =
        {
            "GUI\\Windows\\timer-bg", "GUI\\Windows\\score-bg", "GUI\\Windows\\kingofhill-bg",
            "GUI\\Windows\\kills-bg", "GUI\\Windows\\slaps-bg", "GUI\\Icons\\next_room",
        };
        private static readonly string[] HighDigits = DigitNames("GUI\\Icons\\i-"), LowDigits = DigitNames("GUI\\Icons\\lo-");
        private readonly Image[] _indicatorArt = new Image[Drawn.Length], _indicatorBacking = new Image[Drawn.Length];
        private readonly List<Image>[] _indicatorDigits = new List<Image>[Drawn.Length];
        private HudIndicatorSnapshot _shownIndicators;
        private int _indicatorTop = int.MinValue, _indicatorWidth, _indicatorHeight;
        private (int Width, int Height, int Screen) _shownFrame;
        private readonly Func<byte, string> _playerName;
        private readonly VisualElement _holderWindow;
        private readonly Image _holderText;
        private Texture2D _holderTexture;
        private (byte Player, int Height) _holderShown;
        private int _holderScroll;
        private bool _holderForward = true;
        private readonly TextScrollBar _bar = new TextScrollBar();
        private readonly VisualElement _textWindow;
        private readonly Image _barUp, _barDown, _barTrack, _barThumb;
        private int _barHeld; // 1 up arrow, 2 down arrow, 3 thumb (0x00528B20's states)

        private static string[] DigitNames(string family)
        {
            var names = new string[10];
            for (int d = 0; d < 10; d++) names[d] = family + (char)('0' + d);
            return names;
        }
        private Texture2D _panelTexture, _objectiveTexture;
        private uint _shownId;
        private int _shownHeight;
        private bool _objectiveHovered, _objectiveOpen, _optionsOpen, _visible = true, _allowInput = true;
        private int Height => Screen.height;
        private float V(int value) => value * Height / 1920f;

        /// <param name="resolve">A message string reference to its text (Speech.str, level table, Text.str).</param>
        /// <param name="briefing">A line of the level's briefing table, or null.</param>
        /// <param name="locate">A message's goto location in tiles, or null to hide the button.</param>
        /// <param name="goTo">Moves the view to a location in tiles.</param>
        public ClassicMessageTabs(Transform parent, IClassicBottomPanelAssets assets, IClassicOptionsAssets textAssets,
            MessageTabsView view, Func<int, string> resolve, Func<int, string> briefing,
            Func<PlayerMessage, (float X, float Y)?> locate = null, Action<float, float> goTo = null,
            Func<byte, string> playerName = null)
        {
            _locate = locate; _goTo = goTo; _playerName = playerName;
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _textAssets = textAssets ?? throw new ArgumentNullException(nameof(textAssets));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _resolve = resolve; _briefing = briefing;
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("DK2Hud");
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize; _settings.scale = 1;
            _settings.sortingOrder = 14;
            _owner = new GameObject("Message tabs");
            if (parent != null) _owner.transform.SetParent(parent, false);
            var document = _owner.AddComponent<UIDocument>(); document.panelSettings = _settings;
            _root = document.rootVisualElement; _root.pickingMode = PickingMode.Ignore;
            _root.style.width = Length.Percent(100); _root.style.height = Length.Percent(100);

            _objective = Button(Texture("GUI\\Tabs\\Messages\\mt-objective-00"));
            _objective.RegisterCallback<PointerEnterEvent>(_ => _objectiveHovered = true);
            _objective.RegisterCallback<PointerLeaveEvent>(_ => _objectiveHovered = false);
            _objective.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (!_allowInput || evt.button != 0) return;
                evt.StopPropagation();
                // 0x004288C0: toggles menu33, hiding menu40 while it shows.
                _objectiveOpen = !_objectiveOpen;
                _objectiveTexture = Replace(_objectiveTexture, null);
            });
            for (int i = 0; i < Slots; i++)
            {
                int slot = i;
                _slots[i] = Button(null);
                _slots[i].RegisterCallback<PointerEnterEvent>(_ => _view.Hovered = slot);
                _slots[i].RegisterCallback<PointerLeaveEvent>(_ => { if (_view.Hovered == slot) _view.Hovered = -1; });
                _slots[i].RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (!_allowInput || _objectiveOpen) return;
                    evt.StopPropagation();
                    if (evt.button == 0) _view.OpenTab(slot);        // 0x00423F50
                    else if (evt.button == 1) _view.DismissTab(slot); // 0x00424220
                });
            }

            _panel = Box();
            // 0x00417EA0, menu40's draw callback: the nine gui\windows\text_box pieces (0x00411D00).
            for (int i = 0; i < 9; i++)
            {
                var piece = new VisualElement { pickingMode = PickingMode.Ignore };
                piece.style.position = Position.Absolute;
                piece.style.backgroundImage = new StyleBackground(Texture("GUI\\Windows\\text_box-" + FramePieces[i]));
                piece.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
                piece.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
                piece.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top);
                _panel.Add(piece); _frame[i] = piece;
            }
            // The text widget (20, 20) 1096x288 clips its text; its bar sits right of it (0x0052A0D0).
            _textWindow = new VisualElement { pickingMode = PickingMode.Ignore };
            _textWindow.style.position = Position.Absolute; _textWindow.style.overflow = Overflow.Hidden;
            _panel.Add(_textWindow);
            _panelText = Text(_textWindow);
            _barTrack = Button(Texture("GUI\\Scroll Bars\\v-scroll_bar_bg"), _panel);
            _barThumb = Button(Texture("GUI\\Scroll Bars\\v-scroll_bar"), _panel);
            _barUp = Button(Texture("GUI\\Scroll Bars\\v-up_arrow"), _panel);
            _barDown = Button(Texture("GUI\\Scroll Bars\\v-down_arrow"), _panel);
            _barUp.RegisterCallback<PointerDownEvent>(evt => PressBar(evt, 1));
            _barDown.RegisterCallback<PointerDownEvent>(evt => PressBar(evt, 2));
            _barThumb.RegisterCallback<PointerDownEvent>(evt => PressBar(evt, 3));
            _barTrack.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                evt.StopPropagation();
                // Above or below the thumb: one pixel once per press (state 4).
                float thumbTop = _barThumb.worldBound.yMin;
                _bar.Step(evt.position.y < thumbTop ? -1 : 1);
            });
            _exit = Button(Texture("GUI\\Tabs\\Messages\\mt-exit-00"), _panel);
            _exit.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) { evt.StopPropagation(); _view.DismissOpen(); } });
            _tick = Button(Texture("GUI\\Tabs\\Messages\\mt-tick-00"), _panel);
            _tick.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) { evt.StopPropagation(); _view.CloseOpen(); } });
            // 0x00424720: the view to the message's location; menu40 stays open.
            _goto = Button(Texture("GUI\\Tabs\\Messages\\mt-goto-00"), _panel);
            _goto.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                evt.StopPropagation();
                if (_view.Open is PlayerMessage open && _locate?.Invoke(open) is (float x, float y)) _goTo?.Invoke(x, y);
            });
            for (int i = 0; i < Drawn.Length; i++)
            {
                _indicatorArt[i] = Text(_root); _indicatorBacking[i] = Text(_root);
                _indicatorArt[i].style.display = _indicatorBacking[i].style.display = DisplayStyle.None;
                _indicatorDigits[i] = new List<Image>();
            }
            _holderWindow = new VisualElement { pickingMode = PickingMode.Ignore };
            _holderWindow.style.position = Position.Absolute; _holderWindow.style.overflow = Overflow.Hidden;
            _holderWindow.style.display = DisplayStyle.None; _root.Add(_holderWindow);
            _holderText = Text(_holderWindow);
            _objectivePanel = Box(); _objectiveText = Text(_objectivePanel);
            // Menu33's own background is unread: a translucent dark pane, as before.
            _objectivePanel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.6f);
            _objectivePanel.pickingMode = PickingMode.Position;
            _objectivePanel.RegisterCallback<PointerDownEvent>(evt =>
            { evt.StopPropagation(); if (!_optionsOpen) _objectiveOpen = false; });
        }

        /// <summary>The hovered tab's message text, for the status line (0x0042D820 case 5); the
        /// Review Objective button's own texts 2163/2164.</summary>
        public HoverTextSnapshot HoverText
        {
            get
            {
                if (_objectiveHovered) return new HoverTextSnapshot(_assets.Text(2164), _assets.Text(2163), default);
                int slot = _view.Hovered;
                if (slot < 0 || slot >= _view.Tabs.Count) return HoverTextSnapshot.Empty;
                return new HoverTextSnapshot(MessageText(_view.Tabs[slot].MessageId) ?? "", "", default);
            }
        }

        public bool ContainsPointer(Vector3 screen)
        {
            if (!_visible || !_allowInput) return false;
            var point = new Vector2(screen.x, Height - screen.y);
            if (_objectiveOpen) return true;
            if (_panel.style.display == DisplayStyle.Flex && _panel.worldBound.Contains(point)) return true;
            if (_objective.worldBound.Contains(point)) return true;
            foreach (Image slot in _slots)
                if (slot.style.display == DisplayStyle.Flex && slot.worldBound.Contains(point)) return true;
            return false;
        }

        /// <param name="indicators">0x00427BA0's indicators this frame; any shown lifts menu40 by 128.</param>
        public void Draw(HudVisibility visibility, bool collapsed, in HudIndicatorSnapshot indicators,
            bool optionsOpen = false, bool optionsObjective = false)
        {
            _optionsOpen = optionsOpen;
            _visible = visibility.ShowNormalHud; _allowInput = visibility.AllowOrdinaryInput;
            _root.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!_visible) { _view.Hovered = -1; _objectiveHovered = false; return; }
            int top = MenuY + (collapsed ? ClassicCamera.CollapsedPanelTop - ClassicCamera.ExpandedPanelTop : 0);
            _objective.image = Texture(_objectiveHovered ? "GUI\\Tabs\\Messages\\mt-objective-01" : "GUI\\Tabs\\Messages\\mt-objective-00");
            Place(_objective, MenuX, top, ButtonWidth, ButtonHeight);
            for (int i = 0; i < Slots; i++)
            {
                if (i >= _view.Tabs.Count) { _slots[i].style.display = DisplayStyle.None; continue; }
                MessageTab tab = _view.Tabs[i];
                _slots[i].style.display = DisplayStyle.Flex;
                _slots[i].image = Texture(FrameNames[tab.Graphic, tab.Frame]);
                Place(_slots[i], MenuX + tab.LocalX, top, ButtonWidth, ButtonHeight);
            }
            DrawIndicators(indicators, top);
            ScrollHolder();
            DrawPanel(top, indicators.Any);
            if (optionsOpen) _panel.style.display = DisplayStyle.None;
            DrawObjective(optionsObjective || (!optionsOpen && _objectiveOpen));
        }

        /// <summary>Each indicator (0x00427010): its art 256x128 at the menu's corner, gold-bg-01 188x128
        /// right of it (not for Slaps), then white 0x00415650 digits 40 down, right-aligned to slots
        /// of 40: Time Remaining (0x00427230), Score (0x004273D0, 10 slots), Heroes Killed (0x00427770,
        /// 10 slots), Slaps Remaining (0x00427870, 6 slots). Current Holder (0x004274C0) draws the
        /// player's name instead; Next Room (0x00427980) draws no background, only next_room and the
        /// room's GUI icon as 128-unit squares side by side.</summary>
        private void DrawIndicators(in HudIndicatorSnapshot shown, int panelTop)
        {
            // Rebuilt only when what is shown, the panel top or the screen changes; the time limit
            // changes once a turn, nothing else per frame.
            if (shown == _shownIndicators && panelTop == _indicatorTop && Screen.width == _indicatorWidth
                && Height == _indicatorHeight) return;
            _shownIndicators = shown; _indicatorTop = panelTop; _indicatorWidth = Screen.width; _indicatorHeight = Height;
            IReadOnlyList<(HudIndicator Indicator, int X, int Y)> placed = shown.Any
                ? HudIndicatorLayout.Place(shown, panelTop) : Array.Empty<(HudIndicator, int, int)>();
            _holderWindow.style.display = DisplayStyle.None;
            for (int i = 0; i < Drawn.Length; i++)
            {
                int at = -1;
                for (int p = 0; p < placed.Count; p++) if (placed[p].Indicator == Drawn[i]) at = p;
                Texture2D backing = at < 0 ? null
                    : Drawn[i] == HudIndicator.NextRoom ? RoomIcon(shown.NextRoomTypeId)
                    : Drawn[i] == HudIndicator.SlapsRemaining ? null : Texture("GUI\\Windows\\gold-bg-01");
                _indicatorArt[i].style.display = at >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _indicatorBacking[i].style.display = backing != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (at < 0) { HideDigits(i, 0); continue; }
                int x = placed[at].X, y = placed[at].Y;
                _indicatorArt[i].image = Texture(IndicatorArt[i]);
                if (Drawn[i] == HudIndicator.NextRoom)
                {
                    Place(_indicatorArt[i], x, y, 128, 128);
                    if (backing != null) { _indicatorBacking[i].image = backing; Place(_indicatorBacking[i], x + 128, y, 128, 128); }
                    HideDigits(i, 0);
                    continue;
                }
                Place(_indicatorArt[i], x, y, 256, 128);
                if (backing != null) { _indicatorBacking[i].image = backing; Place(_indicatorBacking[i], x + 256, y, 188, 128); }
                int used = 0;
                // DK2 divides by the LEVEL's turns a second, 0x006F2590 (0x0051E708).
                if (Drawn[i] == HudIndicator.TimeRemaining)
                    foreach ((string digits, int slots) in HudIndicatorLayout.TimeRemainingDigits(
                        shown.TimeLimitTurns, SimulationTiming.OriginalTicksPerGameSecond))
                        used = Digits(i, used, digits, slots, x, y + 40);
                else if (Drawn[i] == HudIndicator.Score)
                    used = Digits(i, used, Invariant(shown.ScoreValue), 10, x, y + 40);
                else if (Drawn[i] == HudIndicator.HeroesKilled)
                    used = Digits(i, used, Invariant(shown.HeroesDestroyed), 10, x, y + 40);
                else if (Drawn[i] == HudIndicator.SlapsRemaining)
                    used = Digits(i, used, Invariant(shown.SlapsLeft), 6, x, y + 40);
                else if (Drawn[i] == HudIndicator.CurrentHolder)
                    PlaceHolder(shown.HolderPlayerId, x, y);
                HideDigits(i, used);
            }
        }

        private static string Invariant(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>0x00427980's second square: the room definition's art 0, the bottom panel's icon.</summary>
        private Texture2D RoomIcon(byte room)
        {
            string name = _assets.Icon(new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, room));
            return name == null ? null : Texture(name);
        }

        /// <summary>0x004274C0: the holder's name (player <c>+0xEEA</c>) in the widget moved 160 in and
        /// 16 down (2 when the screen is 300 high), seen through a window 260 wide (<c>0x104</c>).</summary>
        private void PlaceHolder(byte player, int x, int y)
        {
            if (_holderShown != (player, Height))
            {
                _holderShown = (player, Height);
                _holderTexture = Replace(_holderTexture, TextTextureOrBlank(_playerName?.Invoke(player) ?? "", int.MaxValue / 2));
                // The offset (+0x4112) carries over to a new holder, as in 0x004274C0.
                _holderText.image = _holderTexture;
            }
            int down = Screen.height == 300 ? 2 : 16;
            Place(_holderWindow, x + 160, y + down, 260, 128 - down);
            _holderText.style.top = 0;
            _holderText.style.width = _holderTexture.width; _holderText.style.height = _holderTexture.height;
            _holderWindow.style.display = DisplayStyle.Flex;
        }

        /// <summary>0x004274C0's scroll, once a drawn frame: a name wider than the window moves one
        /// pixel (interface <c>+0x4112</c>) toward its end, then back (direction <c>+0x43B1</c>).</summary>
        private void ScrollHolder()
        {
            if (_holderWindow.style.display != DisplayStyle.Flex || _holderTexture == null) return;
            int overflow = _holderTexture.width - (int)V(260);
            if (overflow <= 0) { _holderScroll = 0; _holderForward = true; }
            else
            {
                _holderScroll += _holderForward ? 1 : -1;
                if (_holderScroll > overflow) _holderForward = false;
                else if (_holderScroll < 0) _holderForward = true;
            }
            _holderText.style.left = -_holderScroll;
        }

        /// <summary>0x00415650 mode 0: 48-unit squares 40 apart, i- digits from 640 wide (lo- below),
        /// catalogue 106 (I--) for a minus; white here. The whole string is drawn, as DK2 does.</summary>
        private int Digits(int indicator, int used, string text, int slots, int x, int y)
        {
            string[] family = Screen.width >= 640 ? HighDigits : LowDigits;
            List<Image> digits = _indicatorDigits[indicator];
            int start = HudIndicatorLayout.AlignedStart(text.Length, slots);
            for (int c = 0; c < text.Length; c++, used++)
            {
                if (used == digits.Count) digits.Add(Text(_root));
                Image digit = digits[used];
                digit.image = Texture(text[c] == '-' ? "GUI\\Icons\\I--" : family[text[c] - '0']);
                digit.style.display = DisplayStyle.Flex;
                Place(digit, x + (start + c) * 40, y, 48, 48);
            }
            return used;
        }

        private void HideDigits(int indicator, int from)
        {
            List<Image> digits = _indicatorDigits[indicator];
            for (int d = from; d < digits.Count; d++) digits[d].style.display = DisplayStyle.None;
        }

        /// <summary>Menu40 in message mode (0x00423F50 sizes it, HudIndicatorLayout.MessagePanel): static
        /// (1020, y) 1200 wide, the text in a 1096-wide box, the exit, tick and goto buttons 88x88 at
        /// x 88, 264 and 440, bottom-anchored 20 up (widget anchor bit 4, 0x00528570).</summary>
        private void DrawPanel(int stripTop, bool indicators)
        {
            PlayerMessage? open = _objectiveOpen ? null : _view.Open;
            _panel.style.display = open.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (open is not PlayerMessage message) { _barHeld = 0; return; }
            if (_shownId != message.Id || _shownHeight != Height || _panelTexture == null)
            {
                _shownId = message.Id; _shownHeight = Height;
                // 0x0052A330 measures 64 narrower than the widget, the bar's width; text that
                // overflows keeps that wrap and gets the bar, text that fits wraps at the full width.
                string text = MessageText(message.Id) ?? "";
                Texture2D measured = PopupTextTexture(text, (int)V(TextScrollBar.MeasuredWidth));
                int box = (int)V(TextScrollBar.Height);
                _bar.Reset(measured.height, box);
                if (!_bar.Enabled) { UnityEngine.Object.Destroy(measured); measured = PopupTextTexture(text, (int)V(TextScrollBar.TextWidget)); }
                _panelTexture = Replace(_panelTexture, measured);
                _panelText.image = _panelTexture;
                _barHeld = 0;
            }
            // The text height in virtual units stands for 4 * DK2's quarter units (0x0052C5A0 unread).
            var (x, y, boxWidth, height) = HudIndicatorLayout.MessagePanel(stripTop, (int)(_panelTexture.height * 1920f / Height), indicators);
            Place(_panel, x, y, boxWidth, height);
            PlaceFrame(boxWidth, height);
            Place(_exit, 88, height - 108, 88, 88); Place(_tick, 264, height - 108, 88, 88);
            Place(_goto, 440, height - 108, 88, 88);
            _goto.style.display = _locate?.Invoke(message) is not null ? DisplayStyle.Flex : DisplayStyle.None;
            // The text widget is (20,20) 1096x288; 0x004242F0's own inset is unread, so the text keeps
            // the place fitted to the owner's DK2 shot (2026-10-22): x 1054, 32 below the top.
            Place(_textWindow, 20, 20, TextScrollBar.TextWidget, TextScrollBar.Height);
            DrawBar();
            // Message mode re-renders the text at y offset -position (0x004242F0).
            _panelText.style.left = V(14); _panelText.style.top = V(12) - _bar.Position;
            _panelText.style.width = _panelTexture.width; _panelText.style.height = _panelTexture.height;
        }

        private void PressBar(PointerDownEvent evt, int state)
        {
            if (evt.button != 0) return;
            evt.StopPropagation();
            _barHeld = state;
        }

        /// <summary>0x00414400 while enabled: arrows 64 square at the ends, the track stretched
        /// between them, the thumb 28 wide 20 in, inset 4 at each end. 0x00528B20 each GUI update:
        /// a held arrow moves one pixel while the pointer stays on it; a held thumb follows the pointer.</summary>
        private void DrawBar()
        {
            DisplayStyle shown = _bar.Enabled ? DisplayStyle.Flex : DisplayStyle.None;
            _barUp.style.display = _barDown.style.display = _barTrack.style.display = _barThumb.style.display = shown;
            if (!_bar.Enabled) { _barHeld = 0; return; }
            if (!Input.GetMouseButton(0)) _barHeld = 0;
            var pointer = new Vector2(Input.mousePosition.x, Height - Input.mousePosition.y);
            if (_barHeld == 1 && _barUp.worldBound.Contains(pointer)) _bar.Step(-1);
            else if (_barHeld == 2 && _barDown.worldBound.Contains(pointer)) _bar.Step(1);
            else if (_barHeld == 3)
                _bar.DragTo((int)((pointer.y - _panel.worldBound.yMin) * 1920f / Height) - TextScrollBar.MenuY, TextScrollBar.Height);
            const int bx = TextScrollBar.MenuX, by = TextScrollBar.MenuY, h = TextScrollBar.Height;
            Place(_barUp, bx, by, TextScrollBar.Width, TextScrollBar.Arrow);
            Place(_barDown, bx, by + h - TextScrollBar.Arrow, TextScrollBar.Width, TextScrollBar.Arrow);
            Place(_barTrack, bx, by + TextScrollBar.Arrow, TextScrollBar.Width, TextScrollBar.Track(h));
            var (top, length) = _bar.Thumb(h);
            Place(_barThumb, bx + 20, by + top + 4, 28, Math.Max(0, length - 8));
        }

        /// <summary>0x00411D00 on menu40's rectangle inset by 10: corners 16 square outside the inset, the
        /// edges tiled in 16s and cropped at the far end, the middle tiled in 32s. DK2 draws these in
        /// device pixels; ours scales them by height / 1200, the owner's 1202-line reference.</summary>
        private void PlaceFrame(int width, int height)
        {
            if (_shownFrame == (width, height, Height)) return;
            _shownFrame = (width, height, Height);
            float pixel = Height / 1200f, border = HudIndicatorLayout.FrameBorder * pixel;
            var (ix, iy, iw, ih) = HudIndicatorLayout.FrameInner(0, 0, V(width), V(height), pixel);
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                VisualElement piece = _frame[row * 3 + column];
                piece.style.left = column == 0 ? ix - border : column == 1 ? ix : ix + iw;
                piece.style.top = row == 0 ? iy - border : row == 1 ? iy : iy + ih;
                piece.style.width = column == 1 ? iw : border;
                piece.style.height = row == 1 ? ih : border;
                float tile = (row == 1 && column == 1 ? HudIndicatorLayout.FrameMiddleTile : HudIndicatorLayout.FrameBorder) * pixel;
                piece.style.backgroundSize = new BackgroundSize(new Length(tile), new Length(tile));
            }
        }

        /// <summary>Menu33 (0x004207E0): "Main Objective" (Text.str 1408), briefing line 2, "Sub
        /// Objective" (1409), briefing lines 3 to 5, over the play area (0,180) 2560x1280.</summary>
        private void DrawObjective(bool visible)
        {
            _objectivePanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            if (_objectiveTexture == null)
            {
                string text = _assets.Text(1408) + "\n" + (_briefing?.Invoke(2) ?? "") + "\n\n" + _assets.Text(1409);
                for (int line = 3; line <= 5; line++)
                    if (!string.IsNullOrEmpty(_briefing?.Invoke(line))) text += "\n" + _briefing(line);
                _objectiveTexture = TextTextureOrBlank(text, (int)V(1600));
                _objectiveText.image = _objectiveTexture;
            }
            float width = Screen.width;
            _objectivePanel.style.left = 0; _objectivePanel.style.top = V(180);
            _objectivePanel.style.width = width; _objectivePanel.style.height = V(1280);
            _objectiveText.style.left = (width - _objectiveTexture.width) / 2f; _objectiveText.style.top = V(250);
            _objectiveText.style.width = _objectiveTexture.width; _objectiveText.style.height = _objectiveTexture.height;
        }

        private Texture2D TextTextureOrBlank(string text, int wrap) =>
            ClassicOptionsPanel.TextTexture(_textAssets.Font(Height), text, Mathf.Max(1, wrap));

        private Texture2D PopupTextTexture(string text, int wrap) =>
            ClassicOptionsPanel.TextTexture(_textAssets.Font(Height * 85 / 100), text, Mathf.Max(1, wrap));

        private string MessageText(uint messageId)
        {
            PlayerMessage? found = _view.Open?.Id == messageId ? _view.Open : _view.MessageById(messageId);
            return found is PlayerMessage message ? _resolve(_view.Reference(message)) : null;
        }

        private Image Button(Texture2D texture, VisualElement parent = null)
        {
            var image = new Image { image = texture, pickingMode = PickingMode.Position, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute; (parent ?? _root).Add(image); return image;
        }

        private VisualElement Box()
        {
            var box = new VisualElement { pickingMode = PickingMode.Position };
            box.style.position = Position.Absolute;
            box.style.display = DisplayStyle.None;
            _root.Add(box); return box;
        }

        private static Image Text(VisualElement parent)
        {
            var image = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            image.style.position = Position.Absolute; parent.Add(image); return image;
        }

        private void Place(VisualElement element, int x, int y, int width, int height)
        { element.style.left = V(x); element.style.top = V(y); element.style.width = V(width); element.style.height = V(height); }

        private Texture2D Texture(string name)
        {
            if (_textures.TryGetValue(name, out Texture2D texture)) return texture;
            texture = MinimapFrameVisual.Decode(_assets.Image(name)); _textures.Add(name, texture); return texture;
        }

        private static Texture2D Replace(Texture2D old, Texture2D next)
        {
            if (old != null) UnityEngine.Object.Destroy(old);
            return next;
        }

        public void Dispose()
        {
            Replace(_panelTexture, null); Replace(_objectiveTexture, null); Replace(_holderTexture, null);
            foreach (Texture2D texture in _textures.Values) UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(_settings); UnityEngine.Object.Destroy(_owner);
        }
    }
}
