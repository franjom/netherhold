// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>The names and strings the bottom panel draws with; the adapter's asset source supplies them.</summary>
public interface IBottomPanelArt
{
    string? Icon(BottomPanelChoiceKey key);
    /// <summary>An upgraded spell's icon, the record's second art name (file <c>+0x120</c>), or null.</summary>
    string? UpgradedIcon(BottomPanelChoiceKey key) => null;
    string Text(uint id);
}

/// <summary>The keeper's bottom panel, engine-free: what it draws (<see cref="Build"/>, in native virtual
/// units, 1920 high) and how it answers the pointer. The adapter only renders the frame and forwards input.
/// Elements are a fixed table in back-to-front order; like retained UI widgets, each keeps its image,
/// place and tint until the next frame changes them.</summary>
public sealed class BottomPanelHud
{
    private const int Tall = 1920;
    // At most two category panels open at once (0x00416C20 pins one tab), each with up to 26 slots a
    // page (13 x 2 128-unit cells, 0x00417470). Slot action p * PanelSlots + k is panel p's page slot k.
    private const int MaxPanels = 2, PanelSlots = 26, SlotCount = MaxPanels * PanelSlots;
    private const int TabAction = 100, PageAction = 200, ReaperAction = 300, ResizeAction = 400;
    private const string Hilight = "GUI\\Icons\\hilight";

    private struct Element
    {
        public bool Show, Enabled;
        public HudDrawKind Kind;
        public HudImage Image;
        public HudRect Rect;
        public HudColor Tint;
        public HudBlend Blend;
        public int Percent, Action;
    }

    private readonly IBottomPanelArt _art;
    private readonly BottomPanelView _view;
    private readonly Element[] _elements;
    private readonly int[] _fills = new int[64], _rail = new int[16], _tabs = new int[4], _tabSelected = new int[4],
        _tabLock = new int[4], _tabHover = new int[4], _icons = new int[SlotCount], _progress = new int[SlotCount],
        _selection = new int[SlotCount], _slotHover = new int[SlotCount], _research = new int[SlotCount],
        _previous = new int[MaxPanels], _next = new int[MaxPanels], _timerImages = new int[2],
        _sparkles = new int[BottomPanelSparkles.Count];
    private readonly int _railMetal, _railEnd, _resize, _reaperBackground, _reaper, _reaperBlink;
    private readonly BottomPanelChoice?[] _slots = new BottomPanelChoice?[SlotCount];
    private readonly BottomPanelCategory[] _panelCategory = new BottomPanelCategory[MaxPanels];
    private readonly BottomPanelRect[] _panelArea = new BottomPanelRect[MaxPanels];
    private readonly int[] _page = new int[4], _pages = { 1, 1, 1, 1 };
    private readonly GuiSoundSlot[] _panelSlot = new GuiSoundSlot[MaxPanels];
    private readonly BottomPanelTabs _tabState = new();
    private readonly FlashChime _chime = new();
    private readonly BottomPanelSparkles _sparkleState = new();
    private int _panelCount, _count;
    private bool _captured, _pointerKnown;
    private float _pointerX, _pointerY;
    private int _pressed = -1, _pressedButton;
    private bool _visible = true, _allowInput = true;
    private float _width;
    private string _descriptionText = "";
    private int _hovered = -1, _hoverCost;
    private BottomPanelChoice? _hoverChoice;
    private bool _hoverUnknown;
    private HoverTextSnapshot _widgetHover = HoverTextSnapshot.Empty;

    public BottomPanelHud(IBottomPanelArt art, BottomPanelView view)
    {
        _art = art ?? throw new ArgumentNullException(nameof(art));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _elements = new Element[64 + 16 + 2 + 4 * 4 + SlotCount * 5 + MaxPanels * 2 + 4 + 2 + BottomPanelSparkles.Count];
        for (int i = 0; i < _fills.Length; i++) _fills[i] = Picture("GUI\\Windows\\Panel-BG");
        // The tab strip's background 0x00418EE0: Bar-00 (resource +0x798) tiled across, then
        // Bar-02 (+0x16E3) and Bar-03 (+0x16FE) at the strip's top; under the tabs.
        for (int i = 0; i < _rail.Length; i++) _rail[i] = Picture("GUI\\Windows\\Bar-00");
        _railMetal = Picture("GUI\\Windows\\Bar-02");
        _railEnd = Picture("GUI\\Windows\\Bar-03");
        string[] tabs = { "t-creatures", "t-rooms", "t-spells", "t-workshop" };
        for (int i = 0; i < 4; i++) _tabs[i] = Interactive("GUI\\Tabs\\" + tabs[i], TabAction + i);
        // The selected tab draws its widget's alternate art (+0x40: catalogue 173/174/175/178)
        // over the icon; unselected tabs are not dimmed.
        string[] selected = { "selected-creature", "selected-room", "selected-spell", "selected-workshop" };
        for (int i = 0; i < 4; i++) _tabSelected[i] = Picture("GUI\\Icons\\" + selected[i]);
        // A pinned tab draws catalogue 8, gui\icons\tab-lock, at layer 3: over the selected overlay
        // (layer 4), under the hover (layer 2) (0x00412260).
        for (int i = 0; i < 4; i++) _tabLock[i] = Picture("GUI\\Icons\\tab-lock");
        // A hovered tab gets catalogue 121, gui\icons\hilight, drawn last with render flag 8
        // (0x00412260 at 0x00412900..0x0041294F), read as additive.
        for (int i = 0; i < 4; i++) _tabHover[i] = Picture(Hilight, HudBlend.Additive);
        // Order is draw order: recharge/manufacture sector (depth 0.7/0.6) over the icon, the selection
        // (layer 5) over it, the hover (layer 4) over that, and the research sector (depth 0.4) in front
        // (panel-indicators.md §5).
        for (int i = 0; i < SlotCount; i++)
        {
            _icons[i] = Interactive(default, i);
            _progress[i] = Sector(); _selection[i] = Picture(default); _slotHover[i] = Picture(Hilight, HudBlend.Additive);
            _research[i] = Sector();
        }
        for (int p = 0; p < MaxPanels; p++)
        {
            _previous[p] = Interactive("GUI\\Pages\\page_back", PageAction + 2 * p);
            _next[p] = Interactive("GUI\\Pages\\page_forward", PageAction + 1 + 2 * p);
        }
        // The strip's fifth widget 0x00690D98: catalogue 44 i-mise, local (544,0), 128x128.
        _resize = Interactive("GUI\\Icons\\i-mise", ResizeAction);
        _reaperBackground = Picture("GUI\\Spells\\reaper_talisman-00");
        _reaper = Interactive("GUI\\Spells\\reaper_talisman-01", ReaperAction);
        _reaperBlink = Picture(default);
        for (int i = 0; i < 2; i++) _timerImages[i] = Picture(default);
        // The flash's sparkles draw last, in front of the flashing widget (tab-alerts.md section 2).
        for (int i = 0; i < _sparkles.Length; i++) _sparkles[i] = Picture(default, HudBlend.Additive);
    }

    public event Action<BottomPanelSelection>? Selected;
    /// <summary>A stored special's slot was clicked (SpellPanel_ClickSlot <c>0x00411140</c>); the
    /// panel's own selection is left as it is.</summary>
    public event Action<BottomPanelChoice>? SpecialChosen;
    public event Action<RoomTypeId>? NavigateRoom;
    /// <summary>A door or trap slot's right click (<c>0x00416070</c>), with the door or trap id.</summary>
    public event Action<byte>? NavigateDoor, NavigateTrap;
    /// <summary>A click DK2 posts as action 0x72, with its target (<see cref="GuiButtonPairs"/>, ADR-0670).</summary>
    public event Action<GuiButtonTarget>? GuiButton;
    /// <summary>Raised with the new state when the Resize Panel button collapses or expands.</summary>
    public event Action<bool>? CollapsedChanged;

    /// <summary>The game is suspended (Options open): the flash chime tests world <c>+0x284</c> (<c>0x00412584</c>).</summary>
    public bool GamePaused { get; set; }
    /// <summary>The level script's FLASH_BUTTON target, set by the host each frame.</summary>
    public HudFlashTarget? FlashTarget { get; set; }
    public SoundRequests Sounds => _tabState.Sounds;
    /// <summary>HUD <c>+0x109</c> == 0 (0x004167D0): the strip at the bottom, categories,
    /// creature panels and the Reaper hidden.</summary>
    public bool Collapsed => _tabState.Collapsed;
    /// <summary>The panels showing (two with a pinned tab), none while collapsed ("closed" for the flash rules).</summary>
    public BottomPanelOpenSet OpenPanel => _tabState.Open;
    public bool HasPointerCapture => _captured;
    public HoverTextSnapshot HoverText => !string.IsNullOrEmpty(_widgetHover.TooltipName) ? _widgetHover
        : new HoverTextSnapshot(_descriptionText, "", default);

    /// <summary>Where <paramref name="category"/>'s panel sits (virtual units, 0x00417310), null while it is
    /// closed or the panel is collapsed. Creatures' area is its overview's (menu 5).</summary>
    public BottomPanelRect? PanelArea(BottomPanelCategory category) =>
        BottomPanelLayout.PanelOf(category, _tabState.Open, _tabState.Pinned);

    // 0x00417170 moves the strip from y 0x5B4 to 0x6EC.
    private int Drop => Collapsed ? ClassicCamera.CollapsedPanelTop - ClassicCamera.ExpandedPanelTop : 0;
    public HudRect Bounds => new(472, 1460 + Drop, Math.Max(0, _width - 760), Tall - (1460 + Drop));
    public HudRect ReaperBounds => new(_width - 288, 1612, 264, 264);

    public void SetVisibility(HudVisibility visibility)
    {
        _visible = visibility.ShowNormalHud; _allowInput = visibility.AllowOrdinaryInput;
        if (!_visible || !_allowInput) CancelPointerGesture();
        if (!_visible || !_allowInput) { _hovered = -1; SetDescription(""); }
    }

    /// <summary>True when a pointer at <paramref name="x"/>, <paramref name="y"/> (virtual units) is the panel's.</summary>
    public bool ContainsPointer(float x, float y) => _visible && _allowInput &&
        (_captured || Bounds.Contains(x, y) || !Collapsed && ReaperBounds.Contains(x, y));

    public void CancelPointerGesture()
    {
        _view.ReleaseChoice();
        _captured = false; _pressed = -1;
    }

    /// <summary>The adapter lost the pointer capture without a release.</summary>
    public void CaptureLost() { _captured = false; _pressed = -1; }

    /// <summary>An ordinary (unshifted) click on <paramref name="category"/>'s tab.</summary>
    public void SelectCategory(BottomPanelCategory category) => ClickTab(category, false);

    /// <summary>The panel as a level starts: Creatures open. Nobody clicked its tab, so no GUI-button action is raised
    /// and the tab's press sound is dropped.</summary>
    public void OpenAtLevelStart()
    {
        ClickTab(BottomPanelCategory.Creatures, false, clicked: false);
        _tabState.Sounds.Clear();
    }

    /// <summary>0x00416C20: expand if collapsed, then open (or, with Shift alone, pin or unpin) the
    /// category. Resetting the page of a panel that opens is ours (the page index field is unread).</summary>
    private void ClickTab(BottomPanelCategory category, bool shiftOnly, bool clicked = true)
    {
        CancelPointerGesture();
        if (clicked) GuiButton?.Invoke(GuiButtonPairs.ForTab(category));   // 0x00416C36, before any expansion
        bool wasOpen = _tabState.Open.Contains(category);
        if (_tabState.Click(category, shiftOnly)) CollapsedChanged?.Invoke(false);
        if (!wasOpen) _page[(int)category] = 0;
    }

    /// <summary>Lays the panel out for a screen <paramref name="virtualWidth"/> units wide and writes its
    /// draws into <paramref name="frame"/>, which the caller cleared. Nothing is drawn while hidden.</summary>
    public void Build(float virtualWidth, long realMilliseconds, HudFrame frame)
    {
        _width = virtualWidth;
        if (!_visible) return;
        Layout(realMilliseconds);
        for (int i = 0; i < _count; i++)
        {
            ref Element e = ref _elements[i];
            if (e.Show) frame.Add(new HudDraw(e.Kind, e.Image, e.Rect, e.Tint, e.Blend, e.Percent));
        }
    }

    private void Layout(long now)
    {
        // The stone and the rail both span the whole screen width, under the minimap at the
        // left and the Reaper talisman at the right (owner's DK2 screenshot, 2026-10-19).
        const float fillSize = 160;
        for (int i = 0; i < _fills.Length; i++)
        {
            float left = i * fillSize;
            ref Element fill = ref _elements[_fills[i]];
            fill.Show = left < _width;
            if (fill.Show) fill.Rect = new HudRect(left, 1600 + Drop, Math.Min(fillSize, _width - left), 300);
        }
        // Wood tiles are U(512,88) from x 0, a quarter-tile below the strip's top (read). The
        // metal pair covers the last 480 virtual units from x 2080 (the 0x820 constant); its
        // split into two 240-unit halves is inferred from the art and the owner's screenshot.
        float railLeft = 0, metalLeft = Math.Max(railLeft, _width - 480), tile = 512;
        for (int i = 0; i < _rail.Length; i++)
        {
            float left = railLeft + i * tile;
            ref Element rail = ref _elements[_rail[i]];
            rail.Show = left < metalLeft;
            if (rail.Show) rail.Rect = new HudRect(left, 1460 + Drop + 22, Math.Min(tile, metalLeft - left), 88);
        }
        Place(_railMetal, new HudRect(metalLeft, 1460 + Drop, 240, 128));
        Place(_railEnd, new HudRect(metalLeft + 240, 1460 + Drop, 240, 128));
        Place(_resize, new HudRect(472 + 544, 1460 + Drop, 128, 128));
        BottomPanelFlashState flash = BottomPanelFlash.Resolve(FlashTarget, OpenPanel, now);
        BottomPanelRect? sparkleOn = flash.Tab is BottomPanelCategory flashTab && _view.TabVisible(flashTab)
            ? Dropped(BottomPanelLayout.Tab((int)flashTab)) : null;
        // Tabs carry widget +0x3C 1..4: the 64-unit sparkle.
        int sparkleSize = BottomPanelSparkles.Size;
        BottomPanelOpenSet open = _tabState.Open;
        for (int i = 0; i < 4; i++)
        {
            var category = (BottomPanelCategory)i;
            HudRect tab = Dropped(BottomPanelLayout.Tab(i));
            bool tabShown = _view.TabVisible(category);
            ref Element tabElement = ref _elements[_tabs[i]];
            tabElement.Rect = tab; tabElement.Show = tabShown;
            // 0x00412579: a flashing tab draws its chime test; the tabs carry sound slots 1..4 (Rooms, Spells, Workshop, Creatures).
            if (tabShown && flash.Tab == category && _chime.Draw(flash.Dark, GamePaused))
                _tabState.Sounds.Add(FlashChime.Request(GuiSoundSlots.OfTab(category)));
            tabElement.Tint = flash.Dark && flash.Tab == category ? Dark
                : _view.TabDimmed(category) ? HudColor.Grey(128) : HudColor.White;
            // The selected overlay draws for every category whose menu is visible (0x00412890).
            Show(_tabSelected[i], tab, tabShown && open.Contains(category));
            // The lock shows whenever the tab is pinned, even with the panel collapsed.
            Show(_tabLock[i], tab, tabShown && _tabState.Pinned == category);
            Show(_tabHover[i], tab, tabShown && _allowInput && _hovered == TabAction + i);
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
            LayoutPanel(p, flash, ref sparkleOn, ref sparkleSize);
        LayoutSparkles(sparkleOn, sparkleSize);
        RefreshHover();
        LayoutReaper(now);
    }

    /// <summary>Panel <paramref name="panel"/>'s page of slots and its page arrows: the grid of 0x00417470 at
    /// the place 0x00417310 gives it. A Creatures panel and an unused panel draw nothing here.</summary>
    private void LayoutPanel(int panel, BottomPanelFlashState flash, ref BottomPanelRect? sparkleOn, ref int sparkleSize)
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
        if (items) _panelSlot[panel] = grid.SoundSlot;
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
            BottomPanelChoice? choice = _slots[i];
            ref Element icon = ref _elements[_icons[i]];
            icon.Show = choice != null;
            _elements[_selection[i]].Show = choice is { Selected: true };
            // A hovered slot in state 1 (Selectable) draws catalogue 121 hilight with render flag 8
            // (0x00412FC0, panel-indicators.md §5), or for an upgraded spell 262 hilight-2 without the flag.
            bool hovered = choice != null && _allowInput && _hovered == i && choice.Selectable;
            ref Element hover = ref _elements[_slotHover[i]];
            hover.Show = hovered;
            if (hovered)
            {
                hover.Image = choice!.Upgraded ? "GUI\\Icons\\hilight-2" : Hilight;
                hover.Blend = choice.Upgraded ? HudBlend.Alpha : HudBlend.Additive;
            }
            BottomPanelSlotIndicators marks = choice?.Indicators ?? BottomPanelSlotIndicators.None;
            int progress = marks.RechargePercent ?? marks.ManufacturePercent ?? 0;
            _elements[_progress[i]].Show = progress != 0;
            _elements[_research[i]].Show = marks.ResearchPercent.HasValue;
            if (choice == null) continue;
            BottomPanelRect cell = grid.Slot(area, k);
            // An upgraded spell draws its record's second icon, the gold-rimmed -2 art (panel-indicators.md §5).
            string? iconName = !choice.Unknown ? (choice.Upgraded ? _art.UpgradedIcon(choice.Key) : null) ?? _art.Icon(choice.Key)
                : choice.Kind == BottomPanelChoiceKind.Room ? "GUI\\Rooms\\tba"
                : choice.Kind == BottomPanelChoiceKind.Spell ? "GUI\\Spells\\s-tba" : "GUI\\Traps\\w-tba";
            HudImage iconImage = string.IsNullOrEmpty(iconName) ? default : iconName;
            icon.Image = iconImage;
            // 0x00412FC0: a recharging spell skips the unaffordable tint and the flash; the
            // manufacture tint overwrites the flash and unaffordable tints, and no-Workshop overwrites it.
            bool recharging = marks.RechargePercent.HasValue;
            bool flashing = choice.Selectable && flash.Slot == choice.Key && !recharging;
            if (flashing)
            {
                sparkleOn = cell; sparkleSize = BottomPanelSparkles.SizeFor(grid.WidgetBank);
                if (_chime.Draw(flash.Dark, GamePaused)) _tabState.Sounds.Add(FlashChime.Request(grid.SoundSlot));
            }
            icon.Tint = recharging ? RechargeTint
                : choice.NeedsWorkshop ? HudColor.Grey(128)
                : marks.ManufacturePercent.HasValue ? ManufactureTint
                : flashing && flash.Dark ? Dark
                : !choice.Selectable || choice.Affordable ? HudColor.White : HudColor.Grey(100);
            icon.Rect = cell;
            if (hovered) hover.Rect = cell;
            if (progress != 0) SetSector(_progress[i], cell, iconImage, HudColor.White, progress);
            if (marks.ResearchPercent is byte studied)
            {
                // State 2 cuts the slot's own icon: "tba" at availability 2, the real icon at 4.
                if (marks.ResearchOnUnknown) SetSector(_research[i], cell, iconImage, ResearchUnknownTint, studied);
                else SetSector(_research[i], cell, "GUI\\Icons\\Gold_Frame", HudColor.White, studied);
            }
            if (choice.Selected)
            {
                ref Element selection = ref _elements[_selection[i]];
                selection.Image = choice.Kind == BottomPanelChoiceKind.Room ? "GUI\\Icons\\selected-room"
                    : choice.Kind == BottomPanelChoiceKind.Spell ? "GUI\\Icons\\selected-spell"
                    : choice.Kind == BottomPanelChoiceKind.Door ? "GUI\\Icons\\selected-door" : "GUI\\Icons\\selected-trap";
                selection.Rect = cell;
            }
            icon.Enabled = _allowInput;
        }
        _elements[_previous[panel]].Show = _elements[_next[panel]].Show = items;
        if (!items) return;
        Place(_previous[panel], BottomPanelLayout.PreviousPage(area));
        Place(_next[panel], BottomPanelLayout.NextPage(area));
        _elements[_previous[panel]].Tint = _page[c] > 0 ? HudColor.White : HudColor.Grey(128);
        _elements[_next[panel]].Tint = _page[c] + 1 < _pages[c] ? HudColor.White : HudColor.Grey(128);
    }

    private void LayoutReaper(long now)
    {
        HudRect reaperBounds = ReaperBounds;
        Place(_reaperBackground, reaperBounds);
        Place(_reaper, reaperBounds);
        Place(_reaperBlink, reaperBounds);
        ref Element blink = ref _elements[_reaperBlink];
        ref Element talisman = ref _elements[_reaper];
        blink.Show = false;
        BottomPanelChoice? reaper = _view.Reaper;
        // Collapsing hides menu41 too (0x004167D0).
        _elements[_reaperBackground].Show = !Collapsed;
        talisman.Show = reaper != null && reaper.Visible && !Collapsed;
        if (reaper != null && reaper.Visible)
        {
            bool nativeSelected = reaper.Selected && reaper.Key.Value == 5;
            if (_view.ReaperNormallyUnlocked)
            {
                talisman.Image = nativeSelected ? "GUI\\Spells\\reaper_talisman-03"
                    : reaper.Selectable ? "GUI\\Spells\\reaper_talisman-02" : "GUI\\Spells\\reaper_talisman-01";
                talisman.Tint = reaper.Selectable ? HudColor.White : HudColor.Grey(170);
            }
            else
            {
                byte pieces = (byte)Math.Min(Math.Max((int)_view.TalismanPieces, 1), 4);
                talisman.Image = new HudImage(nativeSelected ? "GUI\\Spells\\reaper_talisman-03" : "GUI\\Spells\\reaper_talisman-01",
                    HudImageVariant.TalismanArc, pieces);
                talisman.Tint = reaper.Selectable ? HudColor.White : HudColor.Grey(153);
                if (!Collapsed && _view.ReaperOneShotReady && (now / 500 & 1) != 0)
                {
                    blink.Image = new HudImage("GUI\\Spells\\reaper_talisman-03", HudImageVariant.TalismanQuarter, pieces);
                    blink.Tint = talisman.Tint; blink.Show = true;
                }
            }
        }
        int? seconds = _view.ReaperSecondsRemaining;
        bool timer = seconds.HasValue && seconds.Value < 100 && !Collapsed;
        bool twoDigits = timer && seconds!.Value >= 10;
        ref Element first = ref _elements[_timerImages[0]];
        ref Element second = ref _elements[_timerImages[1]];
        first.Show = timer; second.Show = twoDigits;
        if (!timer) return;
        first.Image = Digit(twoDigits ? seconds!.Value / 10 : seconds!.Value);
        first.Rect = new HudRect(reaperBounds.X + (twoDigits ? 0 : 64), reaperBounds.Y + 72, 128, 128);
        if (!twoDigits) return;
        second.Image = Digit(seconds.Value % 10);
        second.Rect = new HudRect(reaperBounds.X + 128, reaperBounds.Y + 72, 128, 128);
    }

    private static readonly HudImage[] Digits = Enumerable.Range(0, 10)
        .Select(n => (HudImage)("GUI\\Spells\\RT-" + n.ToString(CultureInfo.InvariantCulture))).ToArray();
    private static readonly HudImage[] SparkleFrames = Enumerable.Range(0, BottomPanelSparkles.Frames)
        .Select(n => (HudImage)("GSparkle" + n.ToString(CultureInfo.InvariantCulture))).ToArray();

    private static HudImage Digit(int value) => Digits[value];

    /// <summary>0x00419DF0 advances the sparkles each rendered HUD frame; sparkle i draws GSparkle&lt;frame&gt;
    /// (0..15, frame 0 included), a square of side s at (left + X % width - s/2, top + Y % height - s/2):
    /// 88 units on a 264-unit slot (widget +0x3C == 6), else 64 (tabs and 128-unit slots).
    /// DK2 takes the modulo in screen pixels, not virtual units, so the spread differs slightly.</summary>
    private void LayoutSparkles(BottomPanelRect? widget, int size)
    {
        _sparkleState.Advance();
        for (int i = 0; i < _sparkles.Length; i++)
        {
            int frame = _sparkleState.Frame(i);
            ref Element sparkle = ref _elements[_sparkles[i]];
            sparkle.Show = widget.HasValue;
            if (widget is not BottomPanelRect at) continue;
            sparkle.Image = SparkleFrames[frame];
            int half = size / 2;
            sparkle.Rect = new HudRect(at.X + _sparkleState.X(i) % at.Width - half, at.Y + _sparkleState.Y(i) % at.Height - half, size, size);
        }
    }

    // ---- Pointer input, in virtual units. Each method reports whether the panel consumed the event.

    /// <summary>The interactive element under the point, front first; -1 for none.</summary>
    public int HitAction(float x, float y)
    {
        if (!_visible) return -1;
        for (int i = _count - 1; i >= 0; i--)
        {
            ref Element e = ref _elements[i];
            if (e.Action >= 0 && e.Show && e.Enabled && e.Rect.Contains(x, y)) return e.Action;
        }
        return -1;
    }

    /// <summary>A button went down; true when the panel took the pointer and wants it captured.</summary>
    public bool PointerDown(float x, float y, int button)
    {
        int action = HitAction(x, y);
        if (action < 0 || !_allowInput || button != 0 && button != 1) return false;
        bool slot = action < SlotCount && _slots[action] != null;
        bool roomSecondary = button == 1 && slot && _slots[action]!.Kind == BottomPanelChoiceKind.Room;
        if (button == 1 && !roomSecondary) return false;
        if (button == 0 && slot) _view.PressChoice(_slots[action]!.Key, _panelSlot[action / PanelSlots]);
        _pressed = action; _pressedButton = button; _captured = true;
        return true;
    }

    /// <summary>The captured button came up: the press acts when it is released over the widget it began on.</summary>
    public bool PointerUp(float x, float y, int button, bool shift, bool ctrl, bool alt)
    {
        if (!_captured || button != _pressedButton) return false;
        int action = _pressed;
        bool secondary = _pressedButton == 1;
        bool inside = ElementOf(action) is int pressed && _elements[pressed].Rect.Contains(x, y);
        CancelPointerGesture();
        if (!inside || !_allowInput) return true;
        if (secondary)
        {
            BottomPanelChoice? room = _slots[action];
            if (room == null || !room.Visible) return true;
            if (GuiButtonPairs.ForSecondary(room) is GuiButtonTarget target) GuiButton?.Invoke(target);
            if (room.Kind == BottomPanelChoiceKind.Room) NavigateRoom?.Invoke((RoomTypeId)room.Key.Value);
            else if (room.Kind == BottomPanelChoiceKind.Door) NavigateDoor?.Invoke(room.Key.Value);
            else if (room.Kind == BottomPanelChoiceKind.Trap) NavigateTrap?.Invoke(room.Key.Value);
            return true;
        }
        if (action == ResizeAction)
        {
            GuiButton?.Invoke(GuiButtonTarget.ResizePanel);   // 0x004167FF
            // The native pointer jump is not mirrored; the sound request is retained by the model.
            _tabState.Toggle();
            if (Collapsed) { _hoverChoice = null; SetDescription(""); }
            CollapsedChanged?.Invoke(Collapsed);
        }
        else if (action == ReaperAction)
        {
            if (_view.Reaper != null && _view.Select(_view.Reaper.Key)) Selected?.Invoke(_view.Selection);
        }
        // 0x00416C20 pins with the modifier mask 0x005B2A50 == 1: Shift without Ctrl or Alt.
        else if (action >= TabAction && action < TabAction + 4) ClickTab((BottomPanelCategory)(action - TabAction), shift && !ctrl && !alt);
        else if (action >= PageAction && action < PageAction + 2 * MaxPanels) Page((action - PageAction) / 2, (action & 1) == 0 ? -1 : 1);
        else if (_slots[action] is { Kind: BottomPanelChoiceKind.Special } special) SpecialChosen?.Invoke(special);
        else if (_slots[action] is BottomPanelChoice choice && _view.Select(choice.Key))
        {
            if (GuiButtonPairs.ForSelection(choice) is GuiButtonTarget selected) GuiButton?.Invoke(selected);
            Selected?.Invoke(_view.Selection);
        }
        return true;
    }

    /// <summary>The pointer moved over the panel. Hover follows it, except while a press holds the pointer.</summary>
    public void PointerMove(float x, float y)
    {
        _pointerX = x; _pointerY = y; _pointerKnown = true;
        if (_captured) return;
        int action = HitAction(x, y);
        if (action == _hovered) return;
        if (_hovered >= 0) Leave();
        if (action >= 0) Enter(action);
    }

    /// <summary>The pointer left every interactive element.</summary>
    public void PointerLeave()
    {
        _pointerKnown = false;
        if (!_captured && _hovered >= 0) Leave();
    }

    /// <summary>Hover again at the last pointer position after the layout moved under a still pointer
    /// (the Resize button dropping, a page turning), as retained UI re-picks after a layout change.</summary>
    public void Repick()
    {
        if (_pointerKnown) PointerMove(_pointerX, _pointerY);
    }

    /// <summary>The wheel pages the panel under the pointer (ours; DK2's wheel paging is unread).</summary>
    public bool Wheel(float x, float y, float deltaY)
    {
        if (HitAction(x, y) < 0 || !_allowInput || Collapsed || !Bounds.Contains(x, y)) return false;
        for (int p = 0; p < _panelCount; p++)
            if (((HudRect)_panelArea[p]).Contains(x, y)) Page(p, deltaY > 0 ? 1 : -1);
        return true;
    }

    private void Enter(int action)
    {
        _hovered = action; _hoverChoice = null;
        if (action >= TabAction && action < TabAction + 4)
            _widgetHover = HudWidgetText.Hover(HudWidgetText.Tab((BottomPanelCategory)(action - TabAction)), _art.Text);
        else if (action == ResizeAction) _widgetHover = HudWidgetText.Hover(HudWidgetText.ResizePanel, _art.Text);
        else if (action == ReaperAction)
            SetDescription(_art.Text(_view.Reaper?.Selectable != true ? 0x8C4u : _view.ReaperOneShotReady ? 0x8BFu : 0x8BEu));
        else RefreshHover();
    }

    private void Leave()
    {
        _hovered = -1; _hoverChoice = null; _widgetHover = HoverTextSnapshot.Empty; SetDescription("");
    }

    private void Page(int panel, int step)
    {
        if (panel >= _panelCount || _panelCategory[panel] == BottomPanelCategory.Creatures) return;
        int c = (int)_panelCategory[panel];
        _page[c] = Math.Min(Math.Max(_page[c] + step, 0), _pages[c] - 1);
    }

    private void SetDescription(string text) => _descriptionText = text;

    private void RefreshHover()
    {
        if (_hovered < 0 || _hovered >= SlotCount) return;
        BottomPanelChoice? choice = _slots[_hovered];
        if (choice == null) { _hoverChoice = null; SetDescription(""); return; }
        if (_hoverChoice == choice && _hoverCost == choice.Cost && _hoverUnknown == choice.Unknown) return;
        _hoverChoice = choice; _hoverCost = choice.Cost; _hoverUnknown = choice.Unknown;
        // Interface_FormatCostPopup (0x0042EA70): a stored special shows its plain name.
        SetDescription(choice.Unknown ? "?" : choice.Kind == BottomPanelChoiceKind.Special ? choice.Name
            : choice.Name + "  " + choice.Cost.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The element an action belongs to, or null for an action no element carries.</summary>
    private int? ElementOf(int action) => action switch
    {
        ReaperAction => _reaper,
        ResizeAction => _resize,
        >= TabAction and < TabAction + 4 => _tabs[action - TabAction],
        >= PageAction and < PageAction + 2 * MaxPanels => ((action & 1) == 0 ? _previous : _next)[(action - PageAction) / 2],
        >= 0 and < SlotCount => _icons[action],
        _ => null,
    };

    private int Add(HudDrawKind kind, HudImage image, int action, HudBlend blend)
    {
        _elements[_count] = new Element
        {
            Show = kind == HudDrawKind.Quad, Enabled = true, Kind = kind, Image = image, Tint = HudColor.White,
            Blend = blend, Action = action,
        };
        return _count++;
    }

    private int Picture(HudImage image, HudBlend blend = HudBlend.Alpha) => Add(HudDrawKind.Quad, image, -1, blend);
    private int Interactive(HudImage image, int action) => Add(HudDrawKind.Quad, image, action, HudBlend.Alpha);
    private int Sector() => Add(HudDrawKind.Sector, default, -1, HudBlend.Alpha);

    private void Place(int element, HudRect rect) => _elements[element].Rect = rect;

    private void Show(int element, HudRect rect, bool show)
    {
        ref Element e = ref _elements[element];
        e.Rect = rect; e.Show = show;
    }

    private void SetSector(int element, HudRect rect, HudImage image, HudColor tint, int percent)
    {
        ref Element e = ref _elements[element];
        e.Rect = rect; e.Image = image; e.Tint = tint; e.Percent = percent;
    }

    private BottomPanelRect Dropped(BottomPanelRect rectangle) =>
        new(rectangle.X, rectangle.Y + Drop, rectangle.Width, rectangle.Height);

    private static readonly HudColor Dark = HudColor.Grey(BottomPanelFlash.DarkTint);
    private static readonly HudColor RechargeTint = HudColor.Grey(0x66);
    private static readonly HudColor ManufactureTint = new(0x78, 0x00, 0x78);
    // Unity's Color(1, .3f, .3f) as Color32: .3f * 255 is 76.5 in float, rounded half to even.
    private static readonly HudColor ResearchUnknownTint = new(255, 76, 76);
}
