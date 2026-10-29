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
    private readonly CampaignSlot _playedSlot;
    private readonly Dictionary<int, string> _tokens = new();
    private IReadOnlyList<RoomInstance> _rooms = Array.Empty<RoomInstance>();
    private WorldState? _world;
    private GameTick _tick;
    private WorldHoverHit _hit;
    private string _personalName = "";
    private HoverTextSnapshot _snapshot = HoverTextSnapshot.Empty;

    public WorldHoverText(GameDatabase database, Func<uint, string> resolveString, PlayerId viewingPlayer,
        CampaignSlot playedSlot = default)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _resolve = resolveString ?? throw new ArgumentNullException(nameof(resolveString));
        _viewer = viewingPlayer;
        _playedSlot = playedSlot;
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
            return Creature(creature, hit, personalName, world.GameMode);
        if (hit.Kind == WorldHoverKind.Door && world.DoorById(hit.EntityId) is { } door
            && _database.Doors.TryGetValue(door.DoorId, out DoorDef? doorDef))
            return Door(door, doorDef, hit);
        if (hit.Kind == WorldHoverKind.Trap && world.TrapById(hit.EntityId) is { } trap
            && _database.Traps.TryGetValue(trap.TrapId, out TrapDef? trapDef))
            return Trap(trap, trapDef, hit);
        if (hit.Kind == WorldHoverKind.Object && world.ObjectById(hit.EntityId) is { } obj
            && _database.ObjectById(obj.ObjectTypeId) is { NameStringId: > 0 } definition)
            return new(ObjectStatus(world, obj, definition), _resolve(definition.NameStringId), hit);
        // Ours keeps a store tile's gold in the tile and draws DK2's Gold Pile 1 as a decoration
        // (WorldView.StorePiles); hovered, it reads as that object would: "%73 Gold" (SHOT0228).
        if (hit.Kind == WorldHoverKind.Object && WorldView.IsStorePile(hit.EntityId)
            && WorldView.DecorationTileIndex(hit.EntityId) is int pileTile && pileTile < world.Width * world.Height
            && world.TileAtIndex(pileTile).GoldHeld > 0
            && _database.ObjectById(ObjectHoverHighlight.StorePileObjectId) is { NameStringId: > 0 } pile)
        {
            _tokens[73] = Number(world.TileAtIndex(pileTile).GoldHeld);
            return new(Substitute(_resolve(2543u), _tokens), _resolve(pile.NameStringId), hit);
        }
        int tileIndex = world.TileIndex(hit.Tile);
        foreach (RoomInstance room in _rooms)
            if (room.Contains(tileIndex)) return Room(world, room, hit);
        if (tile.SecretDoor) return new(_resolve(2481), _resolve(2480), hit);
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

    /// <summary><c>0x0049C8C0</c>: the definition's tooltip, except loose gold (1/2: 2544, 3: 2543 with
    /// %73), the Casino lever 95 (2508 generous, 2509 rigged, from its room's payout setting) and the
    /// Prison bolt 116 (2503 closed, 2504 taking captives, from its room's activation). A fixture
    /// with no linked room has empty status; ours links the room the object stands in.</summary>
    private string ObjectStatus(WorldState world, ObjectState obj, ObjectDef definition)
    {
        switch (obj.ObjectTypeId)
        {
            case 1 or 2 or 3:
                _tokens[73] = Number(obj.GoldHeld);
                return Substitute(_resolve(obj.ObjectTypeId == 3 ? 2543u : 2544u), _tokens);
            case AutoFurnitureRules.CasinoLeverObjectId:
                return RoomAt(obj.TileIndex) is null ? "" : _resolve(obj.Setting == 0 ? 2508u : 2509u);
            case PrisonBoltObjectId:
                return RoomAt(obj.TileIndex) is { } prison
                    ? _resolve((JailBreakRules.DoorOf(world, prison) & JailBreakRules.Activated) != 0 ? 2504u : 2503u) : "";
            default:
                return definition.TooltipStringId == 0 ? "" : Substitute(_resolve(definition.TooltipStringId), _tokens);
        }
    }

    /// <summary>The Prison door's bolt object, the second fixture arm of <c>0x0049C8C0</c>.</summary>
    public const int PrisonBoltObjectId = 116;

    private RoomInstance? RoomAt(int tileIndex)
    {
        foreach (RoomInstance room in _rooms)
            if (room.Contains(tileIndex)) return room;
        return null;
    }

    /// <summary><c>0x004A68C0</c> (door vtable <c>0x0066D6EC</c> +0x4C): a blueprint reads 2512 with %68 the
    /// name; another keeper's door its bare name (unreachable through the Hand pick, which resolves
    /// only the viewer's own door: <see cref="HoverPickRules.Resolves"/>); an owned barricade 2536; any other owned door 2532 with
    /// %68 name, %37 health and %72 its lock mode (2515 open to yours, 2516 locked).</summary>
    private HoverTextSnapshot Door(DoorState door, DoorDef definition, WorldHoverHit hit)
    {
        string name = definition.NameStringId == 0 ? "" : _resolve(definition.NameStringId);
        _tokens[68] = name;
        _tokens[37] = Number(HealthPercent(door.Health, definition.Health));
        if (door.UnderConstruction) return new(Substitute(_resolve(2512), _tokens), name, hit);
        if (door.PlayerId != _viewer.Value) return new(name, name, hit);
        if (definition.Flags.HasFlag(DoorFlags.Barricade)) return new(Substitute(_resolve(2536), _tokens), name, hit);
        _tokens[72] = _resolve(door.LockMode != 0 ? 2516u : 2515u);
        return new(Substitute(_resolve(2532), _tokens), name, hit);
    }

    /// <summary><c>0x004A9B70</c> (trap vtable <c>0x0066D754</c> +0x4C): a blueprint reads 2512; another
    /// keeper's trap its bare name; an owned Guard Post (flag 0x800) 2538; any other owned trap 2527 with
    /// %68 name, %37 health, %25 mana drain (<c>def+0x96</c>), %26 mana to fire (<c>def+0x9A</c>) and
    /// %71 always 2514.</summary>
    private HoverTextSnapshot Trap(TrapState trap, TrapDef definition, WorldHoverHit hit)
    {
        string name = definition.NameStringId == 0 ? "" : _resolve(definition.NameStringId);
        _tokens[68] = name;
        _tokens[37] = Number(HealthPercent(trap.Health, definition.Health));
        _tokens[25] = Number((short)definition.ManaUpkeepPerSecond);
        _tokens[26] = Number(definition.ManaCostToFire);
        if (trap.UnderConstruction) return new(Substitute(_resolve(2512), _tokens), name, hit);
        if (trap.PlayerId != _viewer.Value) return new(name, name, hit);
        if ((definition.Flags & TrapRules.GuardPostBranch) != 0) return new(_resolve(2538), name, hit);
        _tokens[71] = _resolve(2514);
        return new(Substitute(_resolve(2527), _tokens), name, hit);
    }

    /// <summary>The structure health percentage of <c>0x004A68C0</c>/<c>0x004A9B70</c>: 12.12 fixed
    /// 100 / definition health (100 when that is 0, <c>0x00441990</c>), times health
    /// (<c>0x004363F0</c>), truncated toward zero.</summary>
    public static int HealthPercent(int health, int definitionHealth)
    {
        Fp32 ratio = definitionHealth == 0 ? new Fp32(100 << 12)
            : new Fp32((int)(((long)(100 << 12) << 12) / ((long)definitionHealth << 12)));
        int product = (new Fp32(health << 12) * ratio).Raw;
        return (product + (product < 0 ? 0xFFF : 0)) >> 12;
    }

    private HoverTextSnapshot Creature(CreatureState creature, WorldHoverHit hit, string personalName, GameMode mode)
    {
        CreatureDef definition = _database.CreatureById(creature.CreatureTypeId);
        string kind = _resolve(definition.NameStringId);
        ushort special = definition.SpecialNameStringId;
        if (CreatureDeathRules.IsCorpse(creature))
        {
            // DK2's dead body (class 6, 0x004B1740): 2791 with %30 the type name and %88 its level.
            // %29 is written only when def+0x895 is non-zero, and then with the type name of the body's
            // creature id +0x128 (the id +0x129 came from, 0x004B0F20), not the special name. Otherwise
            // DK2 leaves that buffer unwritten; ours printing it empty is a GUESS (Q634). The popup is
            // the type name (0x0042D4A0 kind 6). Unreachable: no Hand pick returns a corpse (0x005992B0).
            if (special != 0) _tokens[29] = kind;
            _tokens[30] = kind;
            _tokens[88] = Number(creature.Level);
            return new(Substitute(_resolve(2791), _tokens), kind, hit);
        }
        string popup = special != 0 ? _resolve(special) : kind;
        _tokens[29] = creature.CreatureTypeId == LordOfTheLandCreatureId ? _resolve(LordNameTextId(mode, _playedSlot))
            : special != 0 ? popup : personalName;
        _tokens[30] = kind;
        if (creature.OwnerPlayerId == PlayerIds.Good)
            return new(Substitute(_resolve(2412), _tokens), popup, hit);
        if (creature.OwnerPlayerId == PlayerIds.Neutral)
            return new(Substitute(_resolve(2415), _tokens), popup, hit);
        if (creature.OwnerPlayerId != _viewer) return new("", popup, hit);
        if (definition.Flags.HasFlag(CreatureFlags.IsHorny))
            return new(Substitute(_resolve(2793), _tokens), popup, hit);
        _tokens[31] = _resolve((uint)ActionStringId(creature.Dk2StateId, creature.UtilityNewState));
        _tokens[74] = Number(EfficiencyRules.Percent(creature, definition));
        _tokens[33] = _resolve(creature.Mood switch
        {
            CreatureMood.Unhappy => 2342u, CreatureMood.Angry => 2344u, _ => 2340u,
        });
        return new(Substitute(_resolve(2841), _tokens), popup, hit);
    }

    /// <summary>The Lord of the Land, creature 29 (0x1D), whose status name <c>0x0048D880</c> takes from
    /// level strings 2866..2879 or 3079 before it tests <c>def+0x895</c>.</summary>
    private const int LordOfTheLandCreatureId = 29;
    public static uint LordNameTextId(GameMode mode, CampaignSlot playedSlot) => mode != GameMode.Campaign ? 3079u : playedSlot.Value switch
    {
        >= 1 and <= 5 => 2865u + playedSlot.Value,
        6 or 7 => 2871, 8 => 2872, 9 => 2873, 10 => 2874, 11 => 2875,
        15 => 2876, 17 => 2877, 18 or 19 => 2878, 20 => 2879, _ => 3079,
    };

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
