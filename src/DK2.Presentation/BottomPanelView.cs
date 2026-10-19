// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public enum BottomPanelCategory : byte { Creatures, Rooms, Spells, Workshop }
public enum BottomPanelChoiceKind : byte { Room, Spell, Door, Trap }
public readonly record struct BottomPanelChoiceKey(BottomPanelChoiceKind Kind, byte Value);
public readonly record struct BottomPanelSelection(BottomPanelChoiceKey? Choice, PlayerToolMode Tool, RoomTypeId Room);
public readonly record struct BottomPanelRect(int X, int Y, int Width, int Height);

/// <summary>Native virtual units, converted by the adapter at height / 1920.</summary>
public static class BottomPanelLayout
{
    public static BottomPanelRect Tab(int index) => new(472 + index * 136, 1460, 128, 128);
    public static BottomPanelRect Cell(int index) => new(548 + index * 272, 1612, 264, 264);
    public static BottomPanelRect Panel => new(472, 1600, 1800, 300);
    public const int SlotsPerPage = 6;
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
    public bool Selected { get; internal set; }
    public bool Unknown { get; internal set; }
    public bool ClearsToolOnClick { get; internal set; }
    public BottomPanelCategory Category => Kind switch
    {
        BottomPanelChoiceKind.Room => BottomPanelCategory.Rooms,
        BottomPanelChoiceKind.Spell => BottomPanelCategory.Spells,
        _ => BottomPanelCategory.Workshop,
    };
}

/// <summary>Bottom-panel facts captured before worker dispatch. Research: bottom-panel-controls.md.</summary>
public sealed class BottomPanelView
{
    private readonly GameDatabase _database;
    private readonly PlayerId _player;
    private readonly BottomPanelChoice[] _choices;
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
            if ((trap.Flags & 0x2000) == 0) Add(BottomPanelChoiceKind.Trap, trap.TrapId, trap.Name, trap.GoldCost);
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
        }
        int gold = _gold;
        int mana = player ? world.Player(_player).Mana : 0;
        _spellsTab = _workshopTab = false;
        _workshopDim = !player || !OwnsRoomTile(world, RoomTypeId.Workshop);
        _creaturesDim = true;
        for (int i = 0; i < world.Creatures.Count && _creaturesDim; i++)
            if (world.Creatures[i].OwnerPlayerId == _player && CreatureStateMachine.HasState(world.Creatures[i]))
                _creaturesDim = false;
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
            choice.Selectable = choice.Visible && available != 2 && available != 4;
            if (choice.Kind == BottomPanelChoiceKind.Spell && choice.Key.Value == 4 && player)
                choice.Cost = ImpSupplyRules.CreateImpCost(world, _database, _player);
            choice.Affordable = choice.Cost <= (choice.Kind == BottomPanelChoiceKind.Spell ? mana : gold);
            choice.Selected = Selection.Choice == choice.Key;
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
