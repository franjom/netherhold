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
        private Texture2D _panelTexture, _objectiveTexture;
        private uint _shownId;
        private int _shownHeight;
        private bool _objectiveHovered, _objectiveOpen, _visible = true, _allowInput = true;
        private int Height => Screen.height;
        private float V(int value) => value * Height / 1920f;

        /// <param name="resolve">A message string reference to its text (Speech.str, level table, Text.str).</param>
        /// <param name="briefing">A line of the level's briefing table, or null.</param>
        /// <param name="locate">A message's goto location in tiles, or null to hide the button.</param>
        /// <param name="goTo">Moves the view to a location in tiles.</param>
        public ClassicMessageTabs(Transform parent, IClassicBottomPanelAssets assets, IClassicOptionsAssets textAssets,
            MessageTabsView view, Func<int, string> resolve, Func<int, string> briefing,
            Func<PlayerMessage, (float X, float Y)?> locate = null, Action<float, float> goTo = null)
        {
            _locate = locate; _goTo = goTo;
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

            _panel = Box(); _panelText = Text(_panel);
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
            _objectivePanel = Box(); _objectiveText = Text(_objectivePanel);
            _objectivePanel.pickingMode = PickingMode.Position;
            _objectivePanel.RegisterCallback<PointerDownEvent>(evt => { evt.StopPropagation(); _objectiveOpen = false; });
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

        public void Draw(HudVisibility visibility, bool collapsed)
        {
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
            DrawPanel(top);
            DrawObjective();
        }

        /// <summary>Menu40 in message mode (0x00423F50 sizes it): the text in a 1096-wide box, 4 * its
        /// height + 172 tall when it fits 288, else 440. The owner's DK2 shot (2026-10-22, expanded
        /// panel, 1604x1202) puts it 40 above the strip, (1012..2228) wide (fitted to that one shot), the text at its top and the
        /// exit and tick buttons under it at x 88 and 264, 24 from the bottom.</summary>
        private void DrawPanel(int stripTop)
        {
            PlayerMessage? open = _objectiveOpen ? null : _view.Open;
            _panel.style.display = open.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (open is not PlayerMessage message) return;
            const int left = 1012, width = 1096, boxWidth = 1216;
            if (_shownId != message.Id || _shownHeight != Height || _panelTexture == null)
            {
                _shownId = message.Id; _shownHeight = Height;
                _panelTexture = Replace(_panelTexture, ClassicOptionsPanel.TextTexture(
                    _textAssets.Font(Height), MessageText(message.Id) ?? "", Mathf.Max(1, (int)V(width))));
                _panelText.image = _panelTexture;
            }
            // DK2: 4 * h + 172 when h fits the box strictly, h read as quarter units (0x0052C5A0 unread),
            // so virtual height + 172 under 288; 40 above the strip, or 168 while +0x10C2 is set, which
            // 0x00427BA0 sets while any indicator widget 0x2D..0x32 shows (their row at y 1332 is 128
            // tall). Those widgets are not built, so 40, as the owner's DK2 shot shows.
            float textHeight = _panelTexture.height * 1920f / Height;
            int height = textHeight < 288 ? (int)(textHeight + 172) : 440;
            int y = stripTop - height - 40;
            Place(_panel, left, y, boxWidth, height);
            Place(_exit, 88, height - 112, 88, 88); Place(_tick, 264, height - 112, 88, 88);
            Place(_goto, 440, height - 112, 88, 88);
            _goto.style.display = _locate?.Invoke(message) is not null ? DisplayStyle.Flex : DisplayStyle.None;
            _panelText.style.left = V(42); _panelText.style.top = V(32);
            _panelText.style.width = _panelTexture.width; _panelText.style.height = _panelTexture.height;
        }

        /// <summary>Menu33 (0x004207E0): "Main Objective" (Text.str 1408), briefing line 2, "Sub
        /// Objective" (1409), briefing lines 3 to 5, over the play area (0,180) 2560x1280.</summary>
        private void DrawObjective()
        {
            _objectivePanel.style.display = _objectiveOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!_objectiveOpen) return;
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
            // DK2's menu40 is a translucent dark pane with a faint light rim (owner's shot); its frame art is unread.
            box.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.6f);
            box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = 1;
            box.style.borderTopColor = box.style.borderBottomColor = box.style.borderLeftColor = box.style.borderRightColor =
                new Color(0.55f, 0.55f, 0.55f, 0.6f);
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
            Replace(_panelTexture, null); Replace(_objectiveTexture, null);
            foreach (Texture2D texture in _textures.Values) UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(_settings); UnityEngine.Object.Destroy(_owner);
        }
    }
}
