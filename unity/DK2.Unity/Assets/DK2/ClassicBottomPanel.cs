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
        private readonly Image[] _tabSelected = new Image[4], _tabLock = new Image[4], _tabHover = new Image[4];
        // At most two category panels open at once (0x00416C20 pins one tab), each with up to 26 slots a
        // page (13 x 2 128-unit cells, 0x00417470). Slot action p * PanelSlots + k is panel p's page slot k.
        private const int MaxPanels = 2, PanelSlots = 26, SlotCount = MaxPanels * PanelSlots;
        private readonly Image[] _icons = new Image[SlotCount], _selection = new Image[SlotCount];
        private readonly SlotSector[] _progress = new SlotSector[SlotCount], _research = new SlotSector[SlotCount];
        private readonly BottomPanelChoice[] _slots = new BottomPanelChoice[SlotCount];
        private readonly Image[] _previous = new Image[MaxPanels], _next = new Image[MaxPanels];
        private readonly BottomPanelCategory[] _panelCategory = new BottomPanelCategory[MaxPanels];
        private readonly BottomPanelRect[] _panelArea = new BottomPanelRect[MaxPanels];
        private readonly int[] _page = new int[4], _pages = { 1, 1, 1, 1 };
        private readonly BottomPanelTabs _tabState = new BottomPanelTabs();
        private int _panelCount;
        private readonly Image _resize;
        private readonly Image[] _sparkles = new Image[BottomPanelSparkles.Count];
        private readonly BottomPanelSparkles _sparkleState = new BottomPanelSparkles();
        private readonly Image _reaperBackground, _reaper, _reaperBlink;
        private readonly Texture2D[,] _reaperArcs = new Texture2D[4, 2];
        private readonly Texture2D[] _reaperQuarter = new Texture2D[4];
        private readonly Texture2D[] _timerDigits = new Texture2D[10];
        private readonly Image[] _timerImages = new Image[2];
        private int _captured = -1, _pressed = -1, _pressedButton;
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
        /// <summary>A stored special's slot was clicked (SpellPanel_ClickSlot <c>0x00411140</c>); the
        /// panel's own selection is left as it is.</summary>
        public event Action<BottomPanelChoice> SpecialChosen;
        public event Action<RoomTypeId> NavigateRoom;
        /// <summary>Raised with the new state when the Resize Panel button collapses or expands.</summary>
        public event Action<bool> CollapsedChanged;
        /// <summary>HUD <c>+0x109</c> == 0 (0x004167D0): the strip at the bottom, categories,
        /// creature panels and the Reaper hidden.</summary>
        public SoundRequests Sounds => _tabState.Sounds;
        public bool Collapsed => _tabState.Collapsed;
        // 0x00417170 moves the strip from y 0x5B4 to 0x6EC.
        private int Drop => Collapsed ? ClassicCamera.CollapsedPanelTop - ClassicCamera.ExpandedPanelTop : 0;
        public PanelSettings Panel => _settings;
        /// <summary>Where <paramref name="category"/>'s panel sits (virtual units, 0x00417310), null while it is
        /// closed or the panel is collapsed. Creatures' area is its overview's (menu 5).</summary>
        public BottomPanelRect? PanelArea(BottomPanelCategory category) =>
            BottomPanelLayout.PanelOf(category, _tabState.Open, _tabState.Pinned);
        public bool HasPointerCapture => _captured >= 0;
        public int UiScale { get; set; } = 100;
        private HudScaleLayout ScaleLayout => ClassicHudScaling.Layout(UiScale, _viewport.x, _viewport.y);
        private int Width => Mathf.RoundToInt(ScaleLayout.LogicalWidth);
        private int Height => _viewport.y > 0 ? _viewport.y : Screen.height;
        private float V(int value) => value * Height / 1920f;
        public Rect Bounds => new Rect(V(472), V(1460 + Drop), Mathf.Max(0, Width - V(760)), Height - V(1460 + Drop));
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
            // A pinned tab draws catalogue 8, gui\icons\tab-lock, at layer 3: over the selected overlay
            // (layer 4), under the hover (layer 2) (0x00412260).
            for (int i = 0; i < 4; i++) _tabLock[i] = Picture(Texture("GUI\\Icons\\tab-lock"));
            // A hovered tab gets catalogue 121, gui\icons\hilight, drawn last with render flag 8
            // (0x00412260 at 0x00412900..0x0041294F). Flag 8 is read as additive: UI Toolkit has no
            // additive images, so the art is baked to white with its brightness as alpha, which
            // lightens the dark tab frame by about what adding it would.
            Texture2D hilight = Lightening(Texture("GUI\\Icons\\hilight"));
            for (int i = 0; i < 4; i++) _tabHover[i] = Picture(hilight);
            // Child order is draw order: recharge/manufacture sector (depth 0.7/0.6) over the icon, the
            // selection (layer 5) over it, and the research sector (depth 0.4) in front (panel-indicators.md).
            for (int i = 0; i < SlotCount; i++)
            {
                _icons[i] = Interactive(null, i);
                _progress[i] = Sector(); _selection[i] = Picture(null); _research[i] = Sector();
            }
            for (int p = 0; p < MaxPanels; p++)
            {
                _previous[p] = Interactive(Texture("GUI\\Pages\\page_back"), 200 + 2 * p);
                _next[p] = Interactive(Texture("GUI\\Pages\\page_forward"), 201 + 2 * p);
            }
            // The strip's fifth widget 0x00690D98: catalogue 44 i-mise, local (544,0), 128x128.
            _resize = Interactive(Texture("GUI\\Icons\\i-mise"), 400);
            _reaperBackground = Picture(Texture("GUI\\Spells\\reaper_talisman-00"));
            _reaper = Interactive(Texture("GUI\\Spells\\reaper_talisman-01"), 300);
            _reaperBlink = Picture(null);
            for (int i = 0; i < 10; i++) _timerDigits[i] = Texture("GUI\\Spells\\RT-" + i.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < 2; i++) _timerImages[i] = Picture(null);
            // The flash's sparkles draw last, in front of the flashing widget (tab-alerts.md section 2).
            for (int i = 0; i < _sparkles.Length; i++) _sparkles[i] = Picture(null);
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
            (HasPointerCapture || Bounds.Contains(ClassicHudScaling.LocalPointer(screenPosition, ScaleLayout, true)) ||
                !Collapsed && ReaperBounds.Contains(ClassicHudScaling.LocalPointer(screenPosition, ScaleLayout, true)));
        public void CancelPointerGesture()
        {
            _view.ReleaseChoice();
            int pointer = _captured; _captured = -1; _pressed = -1;
            if (pointer >= 0 && _root.HasPointerCapture(pointer)) _root.ReleasePointer(pointer);
        }
        /// <summary>An ordinary (unshifted) click on <paramref name="category"/>'s tab.</summary>
        public void SelectCategory(BottomPanelCategory category) => ClickTab(category, false);

        /// <summary>0x00416C20: expand if collapsed, then open (or, with Shift alone, pin or unpin) the
        /// category. Resetting the page of a panel that opens is ours (the page index field is unread).</summary>
        private void ClickTab(BottomPanelCategory category, bool shiftOnly)
        {
            CancelPointerGesture();
            bool wasOpen = _tabState.Open.Contains(category);
            if (_tabState.Click(category, shiftOnly)) CollapsedChanged?.Invoke(false);
            if (!wasOpen) _page[(int)category] = 0;
        }

        /// <summary>The level script's FLASH_BUTTON target, set by the host each frame.</summary>
        public HudFlashTarget? FlashTarget { get; set; }

        /// <summary>The panels showing (two with a pinned tab), none while collapsed ("closed" for the flash rules).</summary>
        public BottomPanelOpenSet OpenPanel => _tabState.Open;

        public void Draw(Vector2Int viewport = default)
        {
            _viewport = viewport;
            ClassicHudScaling.Apply(_settings, _root, ScaleLayout, true);
            if (!_visible) return;
            // The stone and the rail both span the whole screen width, under the minimap at the
            // left and the Reaper talisman at the right (owner's DK2 screenshot, 2026-10-19).
            float fillSize = V(160);
            for (int i = 0; i < _fills.Length; i++)
            {
                float left = i * fillSize;
                bool show = left < Width;
                _fills[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show) Place(_fills[i], left, V(1600 + Drop), Mathf.Min(fillSize, Width - left), V(300));
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
                if (show) Place(_rail[i], left, V(1460 + Drop + 22), Mathf.Min(tile, metalLeft - left), V(88));
            }
            Place(_railMetal, metalLeft, V(1460 + Drop), V(240), V(128));
            Place(_railEnd, metalLeft + V(240), V(1460 + Drop), V(240), V(128));
            Place(_resize, V(472 + 544), V(1460 + Drop), V(128), V(128));
            BottomPanelFlashState flash = BottomPanelFlash.Resolve(FlashTarget, OpenPanel, (long)(Time.unscaledTimeAsDouble * 1000));
            BottomPanelRect? sparkleOn = flash.Tab is BottomPanelCategory flashTab && _view.TabVisible(flashTab)
                ? Dropped(BottomPanelLayout.Tab((int)flashTab)) : (BottomPanelRect?)null;
            // Tabs carry widget +0x3C 1..4: the 64-unit sparkle.
            int sparkleSize = BottomPanelSparkles.Size;
            BottomPanelOpenSet open = _tabState.Open;
            for (int i = 0; i < 4; i++)
            {
                Place(_tabs[i], Dropped(BottomPanelLayout.Tab(i)));
                bool tabShown = _view.TabVisible((BottomPanelCategory)i);
                _tabs[i].style.display = tabShown ? DisplayStyle.Flex : DisplayStyle.None;
                _tabs[i].tintColor = flash.Dark && flash.Tab == (BottomPanelCategory)i ? Dark
                    : _view.TabDimmed((BottomPanelCategory)i) ? new Color32(128, 128, 128, 255) : Color.white;
                // The selected overlay draws for every category whose menu is visible (0x00412890).
                Place(_tabSelected[i], Dropped(BottomPanelLayout.Tab(i)));
                _tabSelected[i].style.display = tabShown && open.Contains((BottomPanelCategory)i) ? DisplayStyle.Flex : DisplayStyle.None;
                // The lock shows whenever the tab is pinned, even with the panel collapsed.
                Place(_tabLock[i], Dropped(BottomPanelLayout.Tab(i)));
                _tabLock[i].style.display = tabShown && _tabState.Pinned == (BottomPanelCategory)i ? DisplayStyle.Flex : DisplayStyle.None;
                Place(_tabHover[i], Dropped(BottomPanelLayout.Tab(i)));
                _tabHover[i].style.display = tabShown && _allowInput && _hovered == 100 + i ? DisplayStyle.Flex : DisplayStyle.None;
            }
            // 0x00416FD0 lays out the open menus 1..3 through 0x00417470; Creatures is its overview's.
            _panelCount = 0;
            for (int c = 0; c < 4 && _panelCount < MaxPanels; c++)
            {
                var category = (BottomPanelCategory)c;
                if (BottomPanelLayout.PanelOf(category, open, _tabState.Pinned) is not BottomPanelRect area) continue;
                _panelCategory[_panelCount] = category; _panelArea[_panelCount] = area; _panelCount++;
            }
            for (int p = 0; p < MaxPanels; p++)
                DrawPanel(p, flash, ref sparkleOn, ref sparkleSize);
            DrawSparkles(sparkleOn, sparkleSize);
            RefreshHover();
            Rect reaperBounds = ReaperBounds;
            Place(_reaperBackground, reaperBounds.x, reaperBounds.y, reaperBounds.width, reaperBounds.height);
            Place(_reaper, reaperBounds.x, reaperBounds.y, reaperBounds.width, reaperBounds.height);
            Place(_reaperBlink, reaperBounds.x, reaperBounds.y, reaperBounds.width, reaperBounds.height);
            _reaperBlink.style.display = DisplayStyle.None;
            BottomPanelChoice reaper = _view.Reaper;
            // Collapsing hides menu41 too (0x004167D0).
            _reaperBackground.style.display = Collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _reaper.style.display = reaper != null && reaper.Visible && !Collapsed ? DisplayStyle.Flex : DisplayStyle.None;
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
                    if (!Collapsed && _view.ReaperOneShotReady && ((long)(Time.unscaledTimeAsDouble * 1000) / 500 & 1) != 0)
                    {
                        _reaperBlink.image = _reaperQuarter[pieces]; _reaperBlink.tintColor = _reaper.tintColor;
                        _reaperBlink.style.display = DisplayStyle.Flex;
                    }
                }
            }
            int? seconds = _view.ReaperSecondsRemaining;
            bool timer = seconds.HasValue && seconds.Value < 100 && !Collapsed;
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

        /// <summary>Panel <paramref name="panel"/>'s page of slots and its page arrows: the grid of 0x00417470 at
        /// the place 0x00417310 gives it. A Creatures panel and an unused panel draw nothing here.</summary>
        private void DrawPanel(int panel, BottomPanelFlashState flash, ref BottomPanelRect? sparkleOn, ref int sparkleSize)
        {
            int first = panel * PanelSlots;
            bool items = panel < _panelCount && _panelCategory[panel] != BottomPanelCategory.Creatures;
            BottomPanelCategory category = _panelCategory[panel];
            BottomPanelRect area = _panelArea[panel];
            int count = 0;
            // The spells panel lists the Libraries' stored specials after its spells (0x00411140).
            IReadOnlyList<BottomPanelChoice> stored = category == BottomPanelCategory.Spells ? _view.StoredSpecials : Array.Empty<BottomPanelChoice>();
            if (items)
            {
                for (int i = 0; i < _view.Choices.Count; i++)
                { BottomPanelChoice choice = _view.Choices[i]; if (choice.Visible && choice.Category == category) count++; }
                count += stored.Count;
            }
            BottomPanelGrid grid = items ? BottomPanelLayout.Grid(area.Width, count) : default;
            int pageSize = Math.Min(PanelSlots, grid.PageSize), c = (int)category;
            if (items)
            {
                _pages[c] = grid.Pages(count); _page[c] = Math.Min(_page[c], _pages[c] - 1);
                int skip = _page[c] * grid.PageSize, index = 0, visible = 0;
                for (int choiceIndex = 0; choiceIndex < _view.Choices.Count && index < pageSize; choiceIndex++)
                {
                    BottomPanelChoice choice = _view.Choices[choiceIndex];
                    if (!choice.Visible || choice.Category != category) continue;
                    if (visible++ < skip) continue;
                    _slots[first + index++] = choice;
                }
                for (int k = 0; k < stored.Count && index < pageSize; k++)
                    if (visible++ >= skip) _slots[first + index++] = stored[k];
                while (index < PanelSlots) _slots[first + index++] = null;
            }
            else for (int k = 0; k < PanelSlots; k++) _slots[first + k] = null;
            for (int k = 0; k < PanelSlots; k++)
            {
                int i = first + k;
                BottomPanelChoice choice = _slots[i];
                bool show = choice != null;
                _icons[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                _selection[i].style.display = show && choice.Selected ? DisplayStyle.Flex : DisplayStyle.None;
                BottomPanelSlotIndicators marks = show ? choice.Indicators : BottomPanelSlotIndicators.None;
                int progress = marks.RechargePercent ?? marks.ManufacturePercent ?? 0;
                _progress[i].style.display = progress != 0 ? DisplayStyle.Flex : DisplayStyle.None;
                _research[i].style.display = marks.ResearchPercent.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                BottomPanelRect cell = grid.Slot(area, k);
                string icon = !choice.Unknown ? _assets.Icon(choice.Key) : choice.Kind == BottomPanelChoiceKind.Room ? "GUI\\Rooms\\tba" :
                    choice.Kind == BottomPanelChoiceKind.Spell ? "GUI\\Spells\\s-tba" : "GUI\\Traps\\w-tba";
                Texture2D iconTexture = string.IsNullOrEmpty(icon) ? null : Texture(icon);
                _icons[i].image = iconTexture;
                // 0x00412FC0: a recharging spell skips the unaffordable tint and the flash; the
                // manufacture tint overwrites the flash and unaffordable tints, and no-Workshop overwrites it.
                bool recharging = marks.RechargePercent.HasValue;
                bool flashing = choice.Selectable && flash.Slot == choice.Key && !recharging;
                if (flashing) { sparkleOn = cell; sparkleSize = BottomPanelSparkles.SizeFor(grid.WidgetBank); }
                _icons[i].tintColor = recharging ? RechargeTint
                    : choice.NeedsWorkshop ? new Color(128 / 255f, 128 / 255f, 128 / 255f, 1)
                    : marks.ManufacturePercent.HasValue ? ManufactureTint
                    : flashing && flash.Dark ? Dark
                    : !choice.Selectable || choice.Affordable ? Color.white : new Color(100 / 255f, 100 / 255f, 100 / 255f, 1);
                Place(_icons[i], cell);
                if (progress != 0)
                {
                    _progress[i].Set(iconTexture, Color.white, progress);
                    Place(_progress[i], cell);
                }
                if (marks.ResearchPercent is byte studied)
                {
                    // State 2 cuts the slot's own icon: "tba" at availability 2, the real icon at 4.
                    if (marks.ResearchOnUnknown) _research[i].Set(iconTexture, ResearchUnknownTint, studied);
                    else _research[i].Set(Texture("GUI\\Icons\\Gold_Frame"), Color.white, studied);
                    Place(_research[i], cell);
                }
                if (choice.Selected)
                {
                    string selected = choice.Kind == BottomPanelChoiceKind.Room ? "GUI\\Icons\\selected-room" :
                        choice.Kind == BottomPanelChoiceKind.Spell ? "GUI\\Icons\\selected-spell" :
                        choice.Kind == BottomPanelChoiceKind.Door ? "GUI\\Icons\\selected-door" : "GUI\\Icons\\selected-trap";
                    _selection[i].image = Texture(selected); Place(_selection[i], cell);
                }
                _icons[i].SetEnabled(_allowInput);
            }
            _previous[panel].style.display = _next[panel].style.display = items ? DisplayStyle.Flex : DisplayStyle.None;
            if (!items) return;
            Place(_previous[panel], BottomPanelLayout.PreviousPage(area));
            Place(_next[panel], BottomPanelLayout.NextPage(area));
            _previous[panel].tintColor = _page[c] > 0 ? Color.white : Color.gray;
            _next[panel].tintColor = _page[c] + 1 < _pages[c] ? Color.white : Color.gray;
        }

        private Image Interactive(Texture2D texture, int action)
        {
            Image image = Picture(texture); image.pickingMode = PickingMode.Position;
            image.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (!_allowInput || evt.button != 0 && evt.button != 1) return;
                bool roomSecondary = evt.button == 1 && action >= 0 && action < SlotCount &&
                    _slots[action] != null && _slots[action].Kind == BottomPanelChoiceKind.Room;
                if (evt.button == 1 && !roomSecondary) return;
                if (evt.button == 0 && action >= 0 && action < SlotCount && _slots[action] != null)
                    _view.PressChoice(_slots[action].Key);
                _pressed = action; _pressedButton = evt.button;
                _captured = evt.pointerId; _root.CapturePointer(evt.pointerId); evt.StopPropagation();
            });
            image.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _hovered = action; _hoverChoice = null;
                if (action >= 100 && action < 104)
                    _widgetHover = HudWidgetText.Hover(HudWidgetText.Tab((BottomPanelCategory)(action - 100)), _assets.Text);
                else if (action == 400) _widgetHover = HudWidgetText.Hover(HudWidgetText.ResizePanel, _assets.Text);
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
                action >= 200 && action < 200 + 2 * MaxPanels ? ((action & 1) == 0 ? _previous : _next)[(action - 200) / 2].worldBound.Contains(evt.position) :
                action == 400 ? _resize.worldBound.Contains(evt.position) :
                action >= 0 && action < SlotCount && _icons[action].worldBound.Contains(evt.position);
            CancelPointerGesture(); evt.StopPropagation();
            if (!inside || !_allowInput) return;
            if (secondary)
            {
                BottomPanelChoice room = _slots[action];
                if (room != null && room.Visible && room.Kind == BottomPanelChoiceKind.Room)
                    NavigateRoom?.Invoke((RoomTypeId)room.Key.Value);
                return;
            }
            if (action == 400)
            {
                // The native pointer jump is not mirrored by legacy input; the sound request is retained by the model.
                _tabState.Toggle();
                if (Collapsed) { _hoverChoice = null; SetDescription(""); }
                CollapsedChanged?.Invoke(Collapsed);
            }
            else if (action == 300)
            {
                if (_view.Reaper != null && _view.Select(_view.Reaper.Key)) Selected?.Invoke(_view.Selection);
            }
            // 0x00416C20 pins with the modifier mask 0x005B2A50 == 1: Shift without Ctrl or Alt.
            else if (action >= 100 && action < 104) ClickTab((BottomPanelCategory)(action - 100), evt.shiftKey && !evt.ctrlKey && !evt.altKey);
            else if (action >= 200 && action < 200 + 2 * MaxPanels) Page((action - 200) / 2, (action & 1) == 0 ? -1 : 1);
            else if (_slots[action] is BottomPanelChoice { Kind: BottomPanelChoiceKind.Special } special) SpecialChosen?.Invoke(special);
            else if (_slots[action] != null && _view.Select(_slots[action].Key)) Selected?.Invoke(_view.Selection);
        }
        private void Wheel(WheelEvent evt)
        {
            if (!_visible || !_allowInput || Collapsed || !Bounds.Contains(_root.WorldToLocal(evt.mousePosition))) return;
            // The wheel pages the panel under the pointer (ours; DK2's wheel paging is unread).
            for (int p = 0; p < _panelCount; p++)
            {
                BottomPanelRect area = _panelArea[p];
                if (new Rect(V(area.X), V(area.Y), V(area.Width), V(area.Height)).Contains(_root.WorldToLocal(evt.mousePosition)))
                    Page(p, evt.delta.y > 0 ? 1 : -1);
            }
            evt.StopPropagation();
        }
        private void Page(int panel, int step)
        {
            if (panel >= _panelCount || _panelCategory[panel] == BottomPanelCategory.Creatures) return;
            int c = (int)_panelCategory[panel];
            _page[c] = Mathf.Clamp(_page[c] + step, 0, _pages[c] - 1);
        }
        private void SetDescription(string text)
        {
            _descriptionText = text;
        }
        private void RefreshHover()
        {
            if (_hovered < 0 || _hovered >= SlotCount) return;
            BottomPanelChoice choice = _slots[_hovered];
            if (choice == null) { _hoverChoice = null; SetDescription(""); return; }
            if (_hoverChoice == choice && _hoverCost == choice.Cost && _hoverUnknown == choice.Unknown) return;
            _hoverChoice = choice; _hoverCost = choice.Cost; _hoverUnknown = choice.Unknown;
            // Interface_FormatCostPopup (0x0042EA70): a stored special shows its plain name.
            SetDescription(choice.Unknown ? "?" : choice.Kind == BottomPanelChoiceKind.Special ? choice.Name
                : choice.Name + "  " + choice.Cost.ToString(CultureInfo.InvariantCulture));
        }
        private Texture2D Texture(string name)
        {
            if (_textures.TryGetValue(name, out Texture2D texture)) return texture;
            texture = MinimapFrameVisual.Decode(_assets.Image(name)); _textures.Add(name, texture); return texture;
        }
        private static readonly Color32 Dark = new Color32(BottomPanelFlash.DarkTint, BottomPanelFlash.DarkTint, BottomPanelFlash.DarkTint, 255);
        private static readonly Color32 RechargeTint = new Color32(0x66, 0x66, 0x66, 255);
        private static readonly Color32 ManufactureTint = new Color32(0x78, 0x00, 0x78, 255);
        private static readonly Color ResearchUnknownTint = new Color(1f, .3f, .3f, 1f);

        private SlotSector Sector()
        {
            var sector = new SlotSector(); sector.style.display = DisplayStyle.None; _root.Add(sector); return sector;
        }

        /// <summary>Hud_DrawTexturedRadialSector 0x0041EF70 as the slot draw uses it: the texture cut to
        /// the wedge from 12 o'clock clockwise over p% (angle 3π − 2π·p/100 to 3π), reaching the square's
        /// corners; like DK2, the width is the diameter on both axes. A fan of at most five triangles, rebuilt only when its texture, tint or percent change.</summary>
        private sealed class SlotSector : VisualElement
        {
            private Texture2D _texture;
            private Color32 _tint;
            private int _percent;

            public SlotSector()
            {
                pickingMode = PickingMode.Ignore; style.position = Position.Absolute;
                generateVisualContent += Draw;
            }

            public void Set(Texture2D texture, Color32 tint, int percent)
            {
                if (texture == _texture && percent == _percent && tint.r == _tint.r && tint.g == _tint.g &&
                    tint.b == _tint.b && tint.a == _tint.a) return;
                _texture = texture; _tint = tint; _percent = percent; MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                Rect rect = contentRect;
                if (_texture == null || _percent <= 0 || _percent >= 100 || rect.width <= 0) return;
                float sweep = 2 * Mathf.PI * _percent / 100f;
                int corners = 0;
                while (corners < 4 && Mathf.PI / 4 + corners * Mathf.PI / 2 < sweep) corners++;
                int vertices = corners + 3;
                MeshWriteData mesh = context.Allocate(vertices, (vertices - 2) * 3, _texture);
                Write(mesh, rect, 0, 0);
                Write(mesh, rect, 0, -1);
                // Corners in sweep order: top-right, bottom-right, bottom-left, top-left (screen y down).
                for (int k = 0; k < corners; k++) Write(mesh, rect, k < 2 ? 1 : -1, k == 0 || k == 3 ? -1 : 1);
                float x = Mathf.Sin(sweep), y = -Mathf.Cos(sweep), reach = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
                Write(mesh, rect, x / reach, y / reach);
                // Centre, then rim points clockwise on screen: UI Toolkit's winding.
                for (int k = 1; k < vertices - 1; k++)
                { mesh.SetNextIndex(0); mesh.SetNextIndex((ushort)k); mesh.SetNextIndex((ushort)(k + 1)); }
            }

            private void Write(MeshWriteData mesh, Rect rect, float x, float y)
            {
                float u = (x + 1) / 2, v = (y + 1) / 2;
                mesh.SetNextVertex(new Vertex
                {
                    position = new Vector3(rect.x + u * rect.width, rect.y + v * rect.width, Vertex.nearZ),
                    tint = _tint, uv = new Vector2(u, 1 - v),
                });
            }
        }

        /// <summary>0x00419DF0 advances the sparkles each rendered HUD frame; sparkle i draws GSparkle&lt;frame&gt;
        /// (0..15, frame 0 included), a square of side s at (left + X % width - s/2, top + Y % height - s/2):
        /// 88 units on a 264-unit slot (widget +0x3C == 6), else 64 (tabs and 128-unit slots).
        /// DK2 takes the modulo in screen pixels, not virtual units, so the spread differs slightly.</summary>
        private void DrawSparkles(BottomPanelRect? widget, int size)
        {
            _sparkleState.Advance();
            for (int i = 0; i < _sparkles.Length; i++)
            {
                int frame = _sparkleState.Frame(i);
                bool show = widget.HasValue;
                _sparkles[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                BottomPanelRect at = widget.Value;
                _sparkles[i].image = Sparkle(frame);
                int half = size / 2;
                Place(_sparkles[i], new BottomPanelRect(at.X + _sparkleState.X(i) % at.Width - half,
                    at.Y + _sparkleState.Y(i) % at.Height - half, size, size));
            }
        }

        /// <summary>Render flag 8 is taken to be additive (unread); UI Toolkit has none, so the sparkle keeps its colour with
        /// its brightness as alpha (as <see cref="Lightening"/> does for the hover art).</summary>
        private Texture2D Sparkle(int frame)
        {
            string name = "GSparkle" + frame.ToString(CultureInfo.InvariantCulture) + " additive";
            if (_textures.TryGetValue(name, out Texture2D baked)) return baked;
            Texture2D source = Texture("GSparkle" + frame.ToString(CultureInfo.InvariantCulture));
            Color32[] pixels = source.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                int peak = Math.Max(p.r, Math.Max(p.g, p.b));
                pixels[i] = peak == 0 ? new Color32(0, 0, 0, 0) : new Color32((byte)(p.r * 255 / peak),
                    (byte)(p.g * 255 / peak), (byte)(p.b * 255 / peak), (byte)(peak * p.a / 255));
            }
            baked = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = name };
            baked.SetPixels32(pixels); baked.Apply(false, true);
            _textures.Add(name, baked);
            return baked;
        }

        private Texture2D Lightening(Texture2D source)
        {
            Color32[] pixels = source.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                int light = (p.r * 299 + p.g * 587 + p.b * 114) / 1000 * p.a / 255;
                pixels[i] = new Color32(255, 255, 255, (byte)light);
            }
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = source.name + " lightening" };
            texture.SetPixels32(pixels); texture.Apply(false, true);
            _textures.Add(texture.name, texture);
            return texture;
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
        private BottomPanelRect Dropped(BottomPanelRect rectangle) =>
            new BottomPanelRect(rectangle.X, rectangle.Y + Drop, rectangle.Width, rectangle.Height);
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
