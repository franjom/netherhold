// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public readonly record struct KeeperResourceSnapshot(
    GoldAmount Gold, ManaAmount Mana, ManaPerSecond Income, ManaPerSecond Upkeep,
    HudGaugeFraction HeartHealth = default);

public readonly record struct HudGaugeFraction(int Value, int Maximum)
{
    public bool Contains(Vector2 fromCentre)
    {
        if (Maximum <= 0 || Value <= 0) return false;
        if (Value >= Maximum) return true;
        double angle = Math.Atan2(fromCentre.X, -fromCentre.Y);
        if (angle < 0) angle += Math.PI * 2;
        return angle <= Math.PI * 2 * Value / Maximum;
    }
}

/// <summary>Settled resource facts; the renderer never reads a running world.</summary>
public sealed class KeeperResourceView
{
    private readonly GameDatabase _database;
    private readonly PlayerId _viewer;
    private WorldState? _world;
    private GameTick _tick;
    private KeeperResourceSnapshot _snapshot;

    public KeeperResourceView(GameDatabase database, PlayerId viewingPlayer)
    { _database = database; _viewer = viewingPlayer; }

    public KeeperResourceSnapshot Capture(WorldState world)
    {
        if (ReferenceEquals(_world, world) && _tick == world.CurrentTick) return _snapshot;
        _world = world; _tick = world.CurrentTick;
        _snapshot = default;
        if (!world.HasPlayer(_viewer)) return _snapshot;
        int index = 0;
        while (world.PlayerAtIndex(index).PlayerId != _viewer) index++;
        int[] income = ManaRules.GainPerSecondByPlayerIndex(world, _database);
        int[] upkeep = ManaRules.UpkeepPerSecondByPlayerIndex(world, _database, world.CurrentTick);
        byte? heartTerrain = ManaRules.DungeonHeartTerrainId(_database);
        bool hasHeart = false;
        for (int i = 0; i < world.TileCount && !hasHeart; i++)
            hasHeart = world.TileAtIndex(i).OwnerPlayerId == _viewer && world.TileAtIndex(i).TerrainId == heartTerrain;
        _snapshot = new KeeperResourceSnapshot(new(GoldStore.Total(world, _viewer)),
            new(world.Player(_viewer).Mana), new(income[index]), new(upkeep[index]),
            new(hasHeart ? world.Player(_viewer).HeartHealth : 0, _database[VariableTypeId.DungeonHeartObjectHealth]));
        return _snapshot;
    }
}
