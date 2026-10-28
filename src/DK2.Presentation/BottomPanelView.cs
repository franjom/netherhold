// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public enum BottomPanelCategory : byte { Creatures, Rooms, Spells, Workshop }
/// <summary><see cref="Special"/> is a special stored in a Library, keyed by its object id
/// (<see cref="BottomPanelChoice.ObjectId"/> names the box).</summary>
public enum BottomPanelChoiceKind : byte { Room, Spell, Door, Trap, Special }
public readonly record struct BottomPanelChoiceKey(BottomPanelChoiceKind Kind, byte Value);
public readonly record struct BottomPanelSelection(BottomPanelChoiceKey? Choice, PlayerToolMode Tool, RoomTypeId Room);
public readonly record struct BottomPanelRect(int X, int Y, int Width, int Height);

/// <summary>Native virtual units, converted by the adapter at height / 1920.</summary>
public static class BottomPanelLayout
{
    public const int Left = 472, Top = 1600, Span = 1800, Height = 300;
    public static BottomPanelRect Tab(int index) => new(472 + index * 136, 1460, 128, 128);
    public static BottomPanelRect Panel => new(Left, Top, Span, Height);

    /// <summary><c>0x00417310</c>: the <c>n</c> open category menus share the span, each <c>W = 1800 / n</c>
    /// wide at y 1600, 300 high. An unpinned menu sits at x 472; the pinned one (rank 1) at
    /// <c>472 + W</c> when an unpinned one is open, else at 472. Creatures places its overview (menu 5).
    /// Null when <paramref name="category"/> is not open.</summary>
    public static BottomPanelRect? PanelOf(BottomPanelCategory category, BottomPanelOpenSet open, BottomPanelCategory? pinned)
    {
        if (!open.Contains(category)) return null;
        int width = Span / open.Count;
        bool unpinnedOpen = (pinned is BottomPanelCategory p ? open.Without(p) : open).Count > 0;
        int x = pinned == category && unpinnedOpen ? Left + width : Left;
        return new BottomPanelRect(x, Top, width, Height);
    }

    /// <summary><c>0x00417470</c>, expanded (<c>+0x109 == 1</c>): 264-unit cells while
    /// <c>count ≤ 300·(W−64) / 73984 − 1</c>, else 128-unit cells; <c>(W−64)/cell</c> columns and
    /// <c>300/cell</c> rows. One full-width panel holds 6 large or 26 small slots a page; a half one 3 or 12.</summary>
    public static BottomPanelGrid Grid(int panelWidth, int count)
    {
        int area = panelWidth - 64;
        int cell = count <= Height * area / 73984 - 1 ? BottomPanelGrid.LargeCell : BottomPanelGrid.SmallCell;
        return new BottomPanelGrid(cell, Math.Max(0, area / cell), Height / cell);
    }

    /// <summary>The page arrows keep their local x (0) and width (64); 0x00417470 sets y 12 and 144,
    /// height 132 (<c>0x84</c>), so each panel carries its own pair at its left edge.</summary>
    public static BottomPanelRect PreviousPage(BottomPanelRect panel) => new(panel.X, panel.Y + 12, 64, 132);
    public static BottomPanelRect NextPage(BottomPanelRect panel) => new(panel.X, panel.Y + 144, 64, 132);

    /// <summary>0x00417470 for menu 5, the creature overview: <c>(W − 432) / 116</c> columns, one row.</summary>
    public static int OverviewColumns(int panelWidth) => Math.Max(0, (panelWidth - 432) / 116);
}

/// <summary>Retained display state; no simulation object escapes capture.</summary>
public sealed class BottomPanelChoice
{
    internal BottomPanelChoice(BottomPanelChoiceKey key, string name, int cost)
    { Key = key; Name = name; Cost = cost; }
    public BottomPanelChoiceKey Key { get; }
    public BottomPanelChoiceKind Kind => Key.Kind;
    public string Name { get; }
    public int Cost { get; internal set; }
    public bool Visible { get; internal set; }
    public bool Selectable { get; internal set; }
    public bool Affordable { get; internal set; }
    /// <summary>A door or trap while its keeper owns no Workshop: the slot draws 50% grey over any
    /// other tint (<c>0x00412FC0</c>, <c>0x00413734</c>; DK2 skips it while widget <c>+0x41/+0x45</c>
    /// are set, which is unread and not mirrored), the cursor marks every tile invalid
    /// (<c>0x0040B3C0</c>, <c>0x0040B4F0</c>) and a click places nothing (Hand_Click
    /// <c>0x0040849F</c>, <c>0x00408586</c>).</summary>
    public bool NeedsWorkshop { get; internal set; }
    public bool Selected { get; internal set; }
    public bool Unknown { get; internal set; }
    public bool ClearsToolOnClick { get; internal set; }
    /// <summary>The stored box of a <see cref="BottomPanelChoiceKind.Special"/> choice; zero otherwise.</summary>
    public int ObjectId { get; internal set; }
    /// <summary>Recharge, manufacture and research sectors (panel-indicators.md).</summary>
    public BottomPanelSlotIndicators Indicators { get; internal set; }
    public BottomPanelCategory Category => Kind switch
    {
        BottomPanelChoiceKind.Room => BottomPanelCategory.Rooms,
        BottomPanelChoiceKind.Spell or BottomPanelChoiceKind.Special => BottomPanelCategory.Spells,
        _ => BottomPanelCategory.Workshop,
    };
}

/// <summary>Bottom-panel facts captured before worker dispatch. Research: bottom-panel-controls.md.</summary>
public sealed class BottomPanelView
{
    private readonly GameDatabase _database;
    private readonly PlayerId _player;
    private readonly BottomPanelChoice[] _choices;
    private readonly Func<BottomPanelChoiceKey, string, string> _name;
    private readonly List<BottomPanelChoice> _specials = new();
    private readonly bool _oneShotReaper;
    private readonly bool _campaignMode;
    private bool _reaperInitialized;
    private WorldState? _capturedWorld;
    private GameTick _capturedTick;
    private int _gold;
    private bool _ownedRoomsCaptured;
    private bool _spellsTab, _workshopTab, _workshopDim, _creaturesDim;
    private readonly bool[] _ownedRoomTypes = new bool[256];
    public IReadOnlyList<BottomPanelChoice> Choices => _choices;

    /// <summary>The specials stored in the player's Libraries, in the order the spells panel lists them
    /// after its spells (<c>0x00411140</c>, <c>0x00411790</c>): Libraries in room-list order, each
    /// Library's objects newest first. Every one is drawn at full colour with no cost, selection or
    /// recharge (<c>0x00412FC0</c>'s special arm), its icon the object's GUI icon (runtime art slot 0).</summary>
    public IReadOnlyList<BottomPanelChoice> StoredSpecials => _specials;

    /// <summary>Whether a click can select a choice with this availability value: it is shown
    /// (not 1) and neither 2 nor 4. The legal-action fuzzer applies the same rule.</summary>
    public static bool IsSelectable(int availability) => availability is not (1 or 2 or 4);

    /// <summary>Traps flagged <c>0x2000</c> have no panel choice.</summary>
    public static bool IsOnPanel(TrapDef trap) => (trap.Flags & 0x2000) == 0;

    /// <summary>The choice for a key, or null when the panel has none.</summary>
    public BottomPanelChoice? Choice(BottomPanelChoiceKey key)
    {
        foreach (BottomPanelChoice choice in _choices) if (choice.Key == key) return choice;
        return null;
    }
    public BottomPanelChoice? Reaper { get; }
    public byte TalismanPieces { get; }
    public bool ReaperOneShotReady { get; private set; }
    public bool ReaperNormallyUnlocked { get; private set; }
    public int? ReaperSecondsRemaining { get; private set; }
    public BottomPanelSelection Selection { get; private set; } = new(null, PlayerToolMode.Hand, default);

    public BottomPanelView(GameDatabase database, PlayerId player, Func<BottomPanelChoiceKey, string, string> name,
        Func<BottomPanelChoiceKey, int>? order = null, bool campaignMode = true)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _player = player;
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _campaignMode = campaignMode;
        _oneShotReaper = campaignMode && ((database.Level?.Flags ?? 0) & 0x10) != 0;
        TalismanPieces = campaignMode ? database.Level?.TalismanPieces ?? 0 : (byte)0;
        var choices = new List<BottomPanelChoice>();
        void Add(BottomPanelChoiceKind kind, byte id, string fallback, int cost)
        {
            var key = new BottomPanelChoiceKey(kind, id);
            choices.Add(new BottomPanelChoice(key, name(key, fallback), cost));
        }
        foreach (RoomDef room in database.Rooms.Values.OrderBy(r => r.OrderInEditor).ThenBy(r => r.RoomId))
            if (room.IsBuildable) Add(BottomPanelChoiceKind.Room, room.RoomId, room.Name, room.Cost);
        foreach (KeeperSpellDef spell in database.KeeperSpells.Values.OrderBy(s => order?.Invoke(new BottomPanelChoiceKey(BottomPanelChoiceKind.Spell, s.KeeperSpellId)) ?? s.KeeperSpellId))
            if (spell.KeeperSpellId == (_oneShotReaper ? 14 : 5))
            {
                var key = new BottomPanelChoiceKey(BottomPanelChoiceKind.Spell, spell.KeeperSpellId);
                Reaper = new BottomPanelChoice(key, name(key, spell.Name), spell.ManaCost);
            }
            else if (spell.KeeperSpellId != 14 && spell.KeeperSpellId != 5)
                Add(BottomPanelChoiceKind.Spell, spell.KeeperSpellId, spell.Name, spell.ManaCost);
        foreach (DoorDef door in database.Doors.Values.OrderBy(d => order?.Invoke(new BottomPanelChoiceKey(BottomPanelChoiceKind.Door, d.DoorId)) ?? d.DoorId))
            Add(BottomPanelChoiceKind.Door, door.DoorId, door.Name, door.GoldCost);
        foreach (TrapDef trap in database.Traps.Values.OrderBy(t => order?.Invoke(new BottomPanelChoiceKey(BottomPanelChoiceKind.Trap, t.TrapId)) ?? t.TrapId))
            if (IsOnPanel(trap)) Add(BottomPanelChoiceKind.Trap, trap.TrapId, trap.Name, trap.GoldCost);
        _choices = choices.ToArray();
    }

    /// <summary>The tab updater <c>0x00416E20</c>: Creatures and Rooms always show; Spells shows
    /// when a spell other than 5 and 14 has availability other than 1, and Workshop when a door, or
    /// a trap without definition flag <c>0x2000</c>, does. A hidden tab keeps its place.</summary>
    public bool TabVisible(BottomPanelCategory category) => category switch
    {
        BottomPanelCategory.Spells => _spellsTab,
        BottomPanelCategory.Workshop => _workshopTab,
        _ => true,
    };

    /// <summary>The tab draw <c>0x00412260</c> tints a tab 50% grey (<c>0xFF808080</c>): Workshop
    /// while its player owns no Workshop (<c>0x004C5FD0(10)</c>), Creatures while player
    /// <c>+0x10F8</c> (NumberOfCreatureTypes) is 0: the number of creature types with at least one
    /// owned creature, kept by the owner-list link and unlink routines (<c>0x004B4CE2</c>,
    /// <c>0x004B4E0F</c>) with no worker filter. Level-placed creatures link through the same path
    /// (<c>Creature_Construct</c> 0x004872A0), so this is "owns no creature", Imps included. The owner
    /// remembers the tab grey with only Imps (2026-10-19); that memory is unreconciled.</summary>
    public bool TabDimmed(BottomPanelCategory category) => category switch
    {
        BottomPanelCategory.Workshop => _workshopDim,
        BottomPanelCategory.Creatures => _creaturesDim,
        _ => false,
    };

    public void Capture(WorldState world)
    {
        bool player = world.HasPlayer(_player);
        bool changedWorld = !ReferenceEquals(world, _capturedWorld);
        if (changedWorld) { _reaperInitialized = false; ReaperNormallyUnlocked = false; }
        if (changedWorld || world.CurrentTick != _capturedTick)
        {
            _capturedWorld = world; _capturedTick = world.CurrentTick;
            _gold = player ? GoldStore.Total(world, _player) : 0;
            _ownedRoomsCaptured = false;
            CaptureStoredSpecials(world, player);
        }
        int gold = _gold;
        int mana = player ? world.Player(_player).Mana : 0;
        _spellsTab = _workshopTab = false;
        _workshopDim = !player || !OwnsRoomTile(world, RoomTypeId.Workshop);
        _creaturesDim = true;
        for (int i = 0; i < world.Creatures.Count && _creaturesDim; i++)
            if (world.Creatures[i].OwnerPlayerId == _player && CreatureStateMachine.HasState(world.Creatures[i]))
                _creaturesDim = false;
        List<AvailabilityState>? rows = null;
        ManufactureOrder? head = null;
        byte manufacture = 0, research = 0;
        ResearchEntry? target = null;
        if (player)
        {
            ref PlayerState keeper = ref world.Player(_player);
            rows = keeper.Availabilities;
            // DK2 marks the head order's item (0x00506930) but divides by the cost at ring slot 0
            // (0x00506B70), the word the adder pays against too (Q618).
            if (keeper.ManufactureQueue is { Count: > 0 } queue)
            {
                head = queue[0];
                manufacture = BottomPanelProgress.ManufacturePercent(keeper.ManufacturePoints, keeper.ManufactureSlotZeroCost);
            }
            List<ResearchEntry> list = _database.Variables.Research;
            if (keeper.ResearchTarget > 0 && keeper.ResearchTarget <= list.Count)
            {
                target = list[keeper.ResearchTarget - 1];
                research = BottomPanelProgress.ResearchPercent(keeper.ResearchPoints, target.Cost);
            }
        }
        foreach (BottomPanelChoice choice in _choices)
        {
            AvailabilityKind kind = choice.Kind switch
            {
                BottomPanelChoiceKind.Room => AvailabilityKind.Room,
                BottomPanelChoiceKind.Spell => AvailabilityKind.Spell,
                BottomPanelChoiceKind.Door => AvailabilityKind.Door,
                _ => AvailabilityKind.Trap,
            };
            int available = player ? world.Availability(_player, kind, choice.Key.Value) : 1;
            choice.Visible = available != 1;
            if (choice.Kind == BottomPanelChoiceKind.Room && available == 4)
                choice.Visible = OwnsRoomTile(world, (RoomTypeId)choice.Key.Value);
            choice.Unknown = available == 2;
            choice.ClearsToolOnClick = choice.Kind == BottomPanelChoiceKind.Room && available == 4;
            choice.Selectable = IsSelectable(available);
            if (choice.Kind == BottomPanelChoiceKind.Spell && choice.Key.Value == 4 && player)
                choice.Cost = ImpSupplyRules.CreateImpCost(world, _database, _player);
            choice.Affordable = choice.Cost <= (choice.Kind == BottomPanelChoiceKind.Spell ? mana : gold);
            choice.NeedsWorkshop = _workshopDim && choice.Kind is BottomPanelChoiceKind.Door or BottomPanelChoiceKind.Trap;
            choice.Selected = Selection.Choice == choice.Key;
            choice.Indicators = Indicators(world, choice, available, rows, head, manufacture, target, research);
            if (choice.Visible && choice.Kind == BottomPanelChoiceKind.Spell) _spellsTab = true;
            else if (choice.Visible && choice.Kind != BottomPanelChoiceKind.Room) _workshopTab = true;
        }
        if (Reaper is BottomPanelChoice reaper)
        {
            bool ownedReaper = false;
            ReaperSecondsRemaining = null;
            for (int i = 0; i < world.Creatures.Count; i++)
            {
                CreatureState creature = world.Creatures[i];
                if (creature.OwnerPlayerId == _player && CreatureStateMachine.HasState(creature) &&
                    (_database.CreatureById(creature.CreatureTypeId).Flags & CreatureFlags.IsHorny) != 0)
                {
                    ownedReaper = true;
                    if (creature.IsOneShotReaper)
                        ReaperSecondsRemaining = creature.ReaperTicks / SimulationTiming.OriginalTicksPerGameSecond;
                    break;
                }
            }
            int normalAvailability = player ? world.Availability(_player, AvailabilityKind.Spell, 5) : 1;
            if (!_reaperInitialized)
            { ReaperNormallyUnlocked = !_campaignMode && normalAvailability != 1; _reaperInitialized = true; }
            ReaperNormallyUnlocked |= normalAvailability == 3;
            ReaperOneShotReady = player && _oneShotReaper && !world.Player(_player).OneShotReaperUsed;
            reaper.Visible = ReaperNormallyUnlocked || TalismanPieces != 0;
            reaper.Selectable = (ReaperNormallyUnlocked || ReaperOneShotReady) && !ownedReaper;
            reaper.Affordable = reaper.Cost <= mana;
            reaper.Selected = Selection.Choice == reaper.Key;
        }
    }

    /// <summary>The sector selection of 0x00412FC0. Slot state 1 is <see cref="BottomPanelChoice.Selectable"/>
    /// and state 2 is a visible slot at availability 2 or 4 (the update callbacks' <c>+0x61</c>).</summary>
    private BottomPanelSlotIndicators Indicators(WorldState world, BottomPanelChoice choice, int available,
        List<AvailabilityState>? rows, ManufactureOrder? head, byte manufacture, ResearchEntry? target, byte research)
    {
        if (!choice.Visible || rows is null) return BottomPanelSlotIndicators.None;
        bool normal = choice.Selectable, unknown = available is 2 or 4;
        byte? recharge = null, made = null, studied = null;
        if (normal && choice.Kind == BottomPanelChoiceKind.Spell &&
            _database.KeeperSpellById(choice.Key.Value) is KeeperSpellDef spell)
            foreach (AvailabilityState row in rows)
            {
                if (row.Kind != AvailabilityKind.Spell || row.TypeId != choice.Key.Value) continue;
                byte p = BottomPanelProgress.RechargePercent(row.LastCastTick, world.CurrentTick.Value, spell.CooldownRaw);
                if (p != 100) recharge = p;
                break;
            }
        if (normal && manufacture != 0 && head is ManufactureOrder order &&
            (order.Kind == ManufactureRules.DoorKind && choice.Kind == BottomPanelChoiceKind.Door ||
             order.Kind == ManufactureRules.TrapKind && choice.Kind == BottomPanelChoiceKind.Trap) &&
            order.TypeId == choice.Key.Value)
            made = manufacture;
        if ((normal || unknown) && research != 0 && target is not null && target.TypeId == choice.Key.Value &&
            target.Kind == choice.Kind switch
            {
                BottomPanelChoiceKind.Room => ResearchTreeRules.KindRoom,
                BottomPanelChoiceKind.Door => ResearchTreeRules.KindDoor,
                BottomPanelChoiceKind.Trap => ResearchTreeRules.KindTrap,
                _ => ResearchTreeRules.KindSpell,
            })
            studied = research;
        return recharge is null && made is null && studied is null ? BottomPanelSlotIndicators.None
            : new BottomPanelSlotIndicators(recharge, made, studied, studied.HasValue && !normal);
    }

    /// <summary>DK2 walks the player's room list (<c>+0x36</c>, next <c>+0x8</c>), Libraries only (type
    /// byte <c>+0x18</c> 6), and each one's object list (<c>+0xC</c>, next <c>+0x126</c>), counting every
    /// object whose definition has TypeSpecial. <c>Room_AddObject</c> (<c>0x004E5FF0</c>) pushes on the
    /// head, so a Library lists its newest stored first. Ours keeps no room object list: a stored special is
    /// one the keeper owns lying on the Library's tiles, neither held nor carried (the shelf count of
    /// <see cref="ResearchTreeRules"/>), and the newest object stands in for the newest stored.</summary>
    private void CaptureStoredSpecials(WorldState world, bool player)
    {
        int count = 0;
        bool any = false;
        for (int i = 0; i < world.Objects.Count && player && !any; i++)
            any = world.Objects[i].OwnerPlayerId == _player.Value
                && _database.ObjectById(world.Objects[i].ObjectTypeId) is { IsSpecial: true };
        if (any)
            foreach (RoomInstance room in RoomLists.Of(world, _database, _player))
            {
                if (room.Definition.TypeId != RoomTypeId.Library) continue;
                for (int i = world.Objects.Count - 1; i >= 0; i--)
                {
                    ObjectState thing = world.Objects[i];
                    if (thing.OwnerPlayerId != _player.Value || thing.InHand || thing.CarriedByCreatureId >= 0
                        || !room.Contains(thing.TileIndex)
                        || _database.ObjectById(thing.ObjectTypeId) is not { IsSpecial: true } definition) continue;
                    var key = new BottomPanelChoiceKey(BottomPanelChoiceKind.Special, thing.ObjectTypeId);
                    if (count == _specials.Count || _specials[count].Key != key)
                    {
                        var stored = new BottomPanelChoice(key, _name(key, definition.Name), 0)
                        { Visible = true, Selectable = true, Affordable = true };
                        if (count == _specials.Count) _specials.Add(stored); else _specials[count] = stored;
                    }
                    _specials[count++].ObjectId = thing.Id;
                }
            }
        if (count < _specials.Count) _specials.RemoveRange(count, _specials.Count - count);
    }

    private bool OwnsRoomTile(WorldState world, RoomTypeId room)
    {
        if (!_ownedRoomsCaptured)
        {
            Array.Clear(_ownedRoomTypes, 0, _ownedRoomTypes.Length);
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                if (tile.OwnerPlayerId == _player && _database.RoomsByTerrain.TryGetValue(tile.TerrainId, out RoomDef? definition))
                    _ownedRoomTypes[definition.RoomId] = true;
            }
            _ownedRoomsCaptured = true;
        }
        return _ownedRoomTypes[(byte)room];
    }

    public bool Select(BottomPanelChoiceKey key)
    {
        if (Reaper is BottomPanelChoice reaper && reaper.Key == key && reaper.Selectable)
        {
            Selection = new(key, PlayerToolMode.Hand, default);
            foreach (BottomPanelChoice other in _choices) other.Selected = false;
            reaper.Selected = true;
            return true;
        }
        foreach (BottomPanelChoice choice in _choices)
        {
            if (choice.Key == key && choice.Visible && choice.ClearsToolOnClick)
            { SelectTool(PlayerToolMode.Hand); return true; }
            if (choice.Key != key || !choice.Selectable) continue;
            Selection = new(key, key.Kind == BottomPanelChoiceKind.Room ? PlayerToolMode.BuildRoom :
                key.Kind == BottomPanelChoiceKind.Spell && key.Value == 4 ? PlayerToolMode.CreateImp : PlayerToolMode.Hand,
                key.Kind == BottomPanelChoiceKind.Room ? (RoomTypeId)key.Value : default);
            foreach (BottomPanelChoice other in _choices) other.Selected = other.Key == key;
            if (Reaper is BottomPanelChoice special) special.Selected = false;
            return true;
        }
        return false;
    }

    public void SelectTool(PlayerToolMode tool, RoomTypeId room = default)
    {
        Selection = new(tool == PlayerToolMode.BuildRoom ? new BottomPanelChoiceKey(BottomPanelChoiceKind.Room, (byte)room) :
            tool == PlayerToolMode.CreateImp ? new BottomPanelChoiceKey(BottomPanelChoiceKind.Spell, 4) : null, tool, room);
        foreach (BottomPanelChoice choice in _choices) choice.Selected = Selection.Choice == choice.Key;
        if (Reaper is BottomPanelChoice reaper) reaper.Selected = Selection.Choice == reaper.Key;
    }
}
