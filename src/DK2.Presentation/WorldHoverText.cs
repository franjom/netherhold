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

public enum WorldHoverKind { None, Tile, Creature, Object, Door, Trap }
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
        uint template;
        switch (definition.TypeId)
        {
            case RoomTypeId.DungeonHeart:
                ref PlayerState player = ref world.Player(_viewer);
                int maxHealth = _database[VariableTypeId.DungeonHeartObjectHealth];
                _tokens[37] = Number(maxHealth > 0 ? (int)((long)player.HeartHealth * 100 / maxHealth) : 0);
                _tokens[40] = Number(player.Mana);
                _tokens[41] = Number(ManaRules.Threshold(_database));
                template = 2579;
                break;
            case RoomTypeId.Portal:
                int count = 0;
                foreach (RoomInstance owned in _rooms)
                    if (owned.OwnerPlayerId == _viewer && owned.Definition.TypeId == RoomTypeId.Portal) count++;
                _tokens[44] = Number(count);
                _tokens[43] = Number(PortalRules.CreatureCapacity(_database, count));
                template = 2790;
                break;
            default:
                return new(name, name, hit);
        }
        return new(Substitute(_resolve(template), _tokens), name, hit);
    }

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
            && milliseconds > _enteredAt + 1500;
    }
}
