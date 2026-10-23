// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public enum WorldHoverKind { None, Tile, Creature, Object, Door, Trap, Widget }
public readonly record struct WorldHoverHit(WorldHoverKind Kind, TilePos Tile, int EntityId = 0);
public readonly record struct HoverTextSnapshot(string TopStatus, string TooltipName, WorldHoverHit Hit)
{
    public static HoverTextSnapshot Empty => new("", "", default);
}

/// <summary>Captures localized hover text while the simulation is settled; see world-hover-text.md.</summary>
public sealed class WorldHoverText
{
    private readonly GameDatabase _database;
    private readonly Func<uint, string> _resolve;
    private readonly PlayerId _viewer;
    private readonly Dictionary<int, string> _tokens = new();
    private IReadOnlyList<RoomInstance> _rooms = Array.Empty<RoomInstance>();
    private WorldState? _world;
    private GameTick _tick;
    private WorldHoverHit _hit;
    private string _personalName = "";
    private HoverTextSnapshot _snapshot = HoverTextSnapshot.Empty;

    public WorldHoverText(GameDatabase database, Func<uint, string> resolveString, PlayerId viewingPlayer)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _resolve = resolveString ?? throw new ArgumentNullException(nameof(resolveString));
        _viewer = viewingPlayer;
    }

    public HoverTextSnapshot Capture(WorldState world, WorldHoverHit hit, string personalName = "")
    {
        bool changedTick = !ReferenceEquals(world, _world) || world.CurrentTick != _tick;
        if (!changedTick && hit == _hit && personalName == _personalName) return _snapshot;
        if (changedTick) _rooms = RoomIndex.Build(world, _database);
        _world = world; _tick = world.CurrentTick; _hit = hit; _personalName = personalName;
        _tokens.Clear();
        _snapshot = CaptureCore(world, hit, personalName);
        return _snapshot;
    }

    private HoverTextSnapshot CaptureCore(WorldState world, WorldHoverHit hit, string personalName)
    {
        if (hit.Kind == WorldHoverKind.None || !world.Contains(hit.Tile.X, hit.Tile.Y))
            return HoverTextSnapshot.Empty;
        ref TileState tile = ref world.Tile(hit.Tile);
        if (world.FogOfWarEnabled && !tile.IsExploredBy(_viewer))
            return new(_resolve(2473), _resolve(348), hit);
        if (hit.Kind == WorldHoverKind.Creature && world.CreatureById(hit.EntityId) is { } creature)
            return Creature(creature, hit, personalName);
        if (hit.Kind == WorldHoverKind.Object && world.ObjectById(hit.EntityId) is { } obj
            && _database.ObjectById(obj.ObjectTypeId) is { NameStringId: > 0 } definition)
        {
            string status = definition.TooltipStringId == 0 ? "" : Substitute(_resolve(definition.TooltipStringId), _tokens);
            if (obj.ObjectTypeId is 1 or 2 or 3)
            {
                _tokens[73] = Number(obj.GoldHeld);
                status = Substitute(_resolve(obj.ObjectTypeId == 3 ? 2543u : 2544u), _tokens);
            }
            return new(status, _resolve(definition.NameStringId), hit);
        }
        int tileIndex = world.TileIndex(hit.Tile);
        foreach (RoomInstance room in _rooms)
            if (room.Contains(tileIndex)) return Room(world, room, hit);
        TerrainDef terrain = _database.TerrainById(tile.TerrainId);
        _tokens[37] = Number(terrain.MaxHealth / 100 == 0 ? 0 : tile.Health / (terrain.MaxHealth / 100));
        _tokens[66] = Number(terrain.ManaGain);
        _tokens[67] = Number(terrain.MaxHealth == 0 ? 0 : terrain.GoldValue * tile.Health / terrain.MaxHealth);
        bool own = tile.OwnerPlayerId == _viewer;
        uint terrainTemplate = terrain.TerrainId switch
        {
            1 or 30 => 2477, 2 => 2475, 3 => 2797, 4 => 2489, 5 => 2491, 6 => 2493, 7 => 2495,
            8 => own ? 2798u : 2487u, 9 => own ? 2479u : 2481u,
            26 or 36 => own ? 2465u : 2471u, 27 or 38 => 2473, 28 or 29 or 33 or 34 or 37 => 2468,
            31 => 2497, 32 => own ? 2799u : 2501u, 35 => 2470, _ => terrain.TooltipStringId,
        };
        return new(terrainTemplate == 0 ? "" : Substitute(_resolve(terrainTemplate), _tokens),
            terrain.NameStringId == 0 ? "" : _resolve(terrain.NameStringId), hit);
    }

    /// <summary>Hovering a column of the Creatures panel, its portrait or counts (<c>0x00418DD0</c>, status
    /// case 4, <c>0x00494740</c>): the type's description with %27 its job class and %28 its fight style.
    /// No popup: the status strip clears the case before the popup is checked (STRONGLY INFERRED,
    /// creature-overview.md).</summary>
    public HoverTextSnapshot CreatureType(CreatureDef definition)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (definition.DescriptionStringId == 0) return HoverTextSnapshot.Empty;
        var tokens = new Dictionary<int, string>
        {
            [27] = _resolve(definition.JobClass switch
            {
                JobClass.Thinker => 2419u, JobClass.Fighter => 2417u, JobClass.Scout => 2420u, _ => 2418u,
            }),
            [28] = _resolve(definition.FightStyle switch
            {
                FightStyle.Blitzer => 2424u, FightStyle.Blocker => 2423u, FightStyle.Flanker => 2425u,
                FightStyle.Support => 2426u, _ => 2427u,
            }),
        };
        return new(Substitute(_resolve(definition.DescriptionStringId), tokens), "", default);
    }

    private HoverTextSnapshot Creature(CreatureState creature, WorldHoverHit hit, string personalName)
    {
        CreatureDef definition = _database.CreatureById(creature.CreatureTypeId);
        string kind = _resolve(definition.NameStringId);
        _tokens[29] = personalName;
        _tokens[30] = kind;
        if (creature.OwnerPlayerId == PlayerIds.Good)
            return new(Substitute(_resolve(2412), _tokens), kind, hit);
        if (creature.OwnerPlayerId == PlayerIds.Neutral)
            return new(Substitute(_resolve(2415), _tokens), kind, hit);
        if (creature.OwnerPlayerId != _viewer) return new("", kind, hit);
        if (definition.Flags.HasFlag(CreatureFlags.IsHorny))
            return new(Substitute(_resolve(2793), _tokens), kind, hit);
        _tokens[31] = _resolve((uint)ActionStringId(creature.Dk2StateId, creature.UtilityNewState));
        _tokens[74] = Number(EfficiencyRules.Percent(creature, definition));
        _tokens[33] = _resolve(creature.Mood switch
        {
            CreatureMood.Unhappy => 2342u, CreatureMood.Angry => 2344u, _ => 2340u,
        });
        return new(Substitute(_resolve(2841), _tokens), kind, hit);
    }

    private HoverTextSnapshot Room(WorldState world, RoomInstance room, WorldHoverHit hit)
    {
        hit = new(WorldHoverKind.Tile, world.TilePosition(room.TileIndices[0]));
        RoomDef definition = room.Definition;
        string name = _resolve(definition.NameStringId);
        if (room.OwnerPlayerId != _viewer)
            return new(_resolve(room.OwnerPlayerId == PlayerIds.Good && definition.RoomId >= 18 && definition.RoomId <= 23 ? 2468u : 2471u), name, hit);
        int cap = RoomHealthRules.Cap(_database, room);
        _tokens[37] = Number(cap > 0 ? (int)((long)RoomHealthRules.Health(world, _database, room) * 100 / cap) : 0);
        int roomCount = 0;
        foreach (RoomInstance owned in _rooms)
            if (owned.OwnerPlayerId == _viewer && owned.Definition.RoomId == definition.RoomId) roomCount++;
        uint template;
        switch (definition.TypeId)
        {
            case RoomTypeId.Treasury:
                int held = 0, goldCapacity = 0;
                foreach (int tile in room.TileIndices)
                {
                    held += world.TileAtIndex(tile).GoldHeld;
                    goldCapacity += GoldStore.Capacity(world, _database, tile, room);
                }
                _tokens[75] = Number(held); _tokens[76] = Number(goldCapacity); _tokens[47] = Number(roomCount);
                template = 2581;
                break;
            case RoomTypeId.Lair:
                _tokens[38] = Number(LairRules.Occupied(world, _database, room));
                _tokens[39] = Number(LairRules.Capacity(room)); _tokens[45] = Number(roomCount);
                template = 2435;
                break;
            case RoomTypeId.Hatchery:
                _tokens[46] = Number(roomCount);
                template = 2580;
                break;
            case RoomTypeId.DungeonHeart:
                ref PlayerState player = ref world.Player(_viewer);
                if (player.HeartRuined) return new("", name, hit);
                int maxHealth = _database[VariableTypeId.DungeonHeartObjectHealth];
                _tokens[37] = Number(maxHealth > 0 ? (int)((long)player.HeartHealth * 100 / maxHealth) : 0);
                _tokens[40] = Number(player.Mana);
                _tokens[41] = Number(ManaRules.Threshold(_database));
                template = 2579;
                break;
            case RoomTypeId.Portal:
                _tokens[44] = Number(roomCount);
                _tokens[43] = Number(PortalRules.CreatureCapacity(_database, roomCount));
                template = 2790;
                break;
            case RoomTypeId.Library:
                CapacityTokens(world, room);
                _tokens[77] = _tokens[38]; _tokens[78] = _tokens[39];
                int books = 0, shelves = 0;
                foreach (ObjectState obj in world.Objects)
                {
                    if (!RoomContainsObject(room, obj)) continue;
                    if (_database.ObjectById(obj.ObjectTypeId) is { } objDef && (objDef.IsSpellBook || objDef.IsSpecial)) books++;
                    if (obj.AutoPlaced && obj.ObjectTypeId == 64) shelves++;
                }
                _tokens[38] = Number(books); _tokens[39] = Number(shelves * 2); _tokens[48] = Number(roomCount);
                template = 2582;
                break;
            case RoomTypeId.TrainingRoom:
                CapacityTokens(world, room); _tokens[49] = Number(roomCount);
                template = 2583;
                break;
            case RoomTypeId.Workshop:
                CapacityTokens(world, room); _tokens[50] = Number(roomCount);
                int crates = 0;
                foreach (ObjectState obj in world.Objects)
                    if (CrateRules.IsListedIn(world, _database, room, obj)) crates++;
                _tokens[79] = Number(crates);
                var furnitureSlots = new HashSet<int>();
                AutoFurnitureRules.FurnitureSlots(world, _database, room, furnitureSlots);
                _tokens[80] = Number((room.TileCount - furnitureSlots.Count) * CrateRules.CratesPerTile);
                template = 2584;
                break;
            case RoomTypeId.GuardRoom:
                AutoFurnitureRules.TryTotalCapacity(world, room, out int guardCapacity);
                _tokens[81] = Number(RoomCapacityRules.Occupants(world, room));
                _tokens[82] = Number(guardCapacity); _tokens[51] = Number(roomCount);
                int routes = 0;
                foreach (TrapState trap in world.Traps)
                    if (trap.PlayerId == _viewer && !trap.UnderConstruction && trap.GuardRoomTile >= 0 && room.Contains(trap.GuardRoomTile)
                        && _database.Traps.TryGetValue(trap.TrapId, out TrapDef? trapDef) && (trapDef.Flags & 0x800) != 0) routes++;
                _tokens[52] = Number(routes);
                template = 2585;
                break;
            case RoomTypeId.Prison:
                _tokens[83] = Number(RoomCapacityRules.Occupants(world, room));
                _tokens[84] = Number(PrisonRules.Capacity(room)); _tokens[56] = _tokens[84]; _tokens[85] = Number(roomCount);
                _tokens[57] = _resolve((JailBreakRules.DoorOf(world, room) & JailBreakRules.Activated) != 0 ? 2454u : 2455u);
                template = 2586;
                break;
            case RoomTypeId.TortureChamber:
                CapacityTokens(world, room); _tokens[54] = Number(roomCount);
                template = 2451;
                break;
            case RoomTypeId.Temple:
                CapacityTokens(world, room); _tokens[62] = Number(roomCount);
                template = 2459;
                break;
            case RoomTypeId.Graveyard:
                int graveCapacity = AutoFurnitureRules.FloorSlotCount(world, room);
                _tokens[38] = Number(GraveyardRules.Lying(world, room)); _tokens[39] = Number(graveCapacity);
                _tokens[86] = _tokens[39]; _tokens[61] = Number(roomCount);
                template = 2587;
                break;
            case RoomTypeId.Casino:
                CapacityTokens(world, room); _tokens[63] = Number(roomCount);
                _tokens[87] = CasinoRules.Lever(world, room) is ObjectState lever ? _resolve(lever.Setting == 0 ? 2508u : 2509u) : "?";
                template = 2781;
                break;
            case RoomTypeId.CombatPit:
                HashSet<int> floor = AutoFurnitureRules.FloorSlotSet(world, room);
                int fighters = 0;
                foreach (CreatureState creature in world.Creatures)
                    if (creature.CombatPitFighter && !creature.InHand && world.Contains(creature.Tile) && floor.Contains(world.TileIndex(creature.Tile))) fighters++;
                _tokens[38] = Number(fighters); _tokens[39] = Number(floor.Count); _tokens[53] = Number(roomCount);
                template = 2449;
                break;
            case RoomTypeId.WoodenBridge:
                template = 2463;
                break;
            case RoomTypeId.StoneBridge:
                template = 2465;
                break;
            default:
                return new(name, name, hit);
        }
        return new(Substitute(_resolve(template), _tokens), name, hit);
    }

    private void CapacityTokens(WorldState world, RoomInstance room)
    {
        AutoFurnitureRules.TryTotalCapacity(world, room, out int capacity);
        _tokens[38] = Number(RoomCapacityRules.Occupants(world, room)); _tokens[39] = Number(capacity);
    }

    private static bool RoomContainsObject(RoomInstance room, ObjectState obj) =>
        !obj.InHand && obj.CarriedByCreatureId < 0 && obj.OwnerPlayerId == room.OwnerPlayerId && room.Contains(obj.TileIndex);

    public static int ActionStringId(int stateId, int utilityNewState)
    {
        if (stateId < 0 || stateId >= CreatureStateTable.Count) return 2599;
        ref readonly CreatureStateDef state = ref CreatureStateTable.Get(stateId);
        if (state.IsUtility)
        {
            if (utilityNewState < 0 || utilityNewState >= CreatureStateTable.Count) return 2599;
            state = ref CreatureStateTable.Get(utilityNewState);
        }
        return state.LabelStringId == 0 ? 2599 : state.LabelStringId;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Substitute(string template, IReadOnlyDictionary<int, string> values)
    {
        var result = new StringBuilder(template.Length);
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] != '%' || i + 1 == template.Length || template[i + 1] < '0' || template[i + 1] > '9')
            { result.Append(template[i]); continue; }
            int token = 0;
            while (i + 1 < template.Length && template[i + 1] >= '0' && template[i + 1] <= '9')
                token = token * 10 + template[++i] - '0';
            if (values.TryGetValue(token, out string? replacement)) result.Append(replacement);
        }
        return result.ToString();
    }
}

/// <summary>The original world tooltip waits 1500 milliseconds after changing hover identity.</summary>
public sealed class HoverTooltipDelay
{
    private WorldHoverHit _hit;
    private long _enteredAt;
    public bool Visible(HoverTextSnapshot snapshot, long milliseconds, bool showCreatureTooltip = true)
    {
        bool same = snapshot.Hit.Kind == _hit.Kind && (snapshot.Hit.Kind == WorldHoverKind.Tile
            ? snapshot.Hit.Tile == _hit.Tile : snapshot.Hit.EntityId == _hit.EntityId);
        if (!same) { _hit = snapshot.Hit; _enteredAt = milliseconds; }
        return snapshot.Hit.Kind != WorldHoverKind.None && !string.IsNullOrEmpty(snapshot.TooltipName)
            && (showCreatureTooltip || snapshot.Hit.Kind != WorldHoverKind.Creature)
            && milliseconds > _enteredAt + (snapshot.Hit.Kind == WorldHoverKind.Widget ? 250 : 1500);
    }
}
