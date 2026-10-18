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
    HudGaugeFraction HeartHealth = default, HudGaugeFraction Payday = default,
    GoldAmount WageBill = default, GameTick Tick = default, GameTick LastPaydayTick = default,
    GameTick NextPaydayTick = default, long PaydayCutoffTicks = 0, GameTick HeartAlarmUntil = default,
    TilePos? HeartTile = null);

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
    private int _lastHealth;
    private GameTick _alarmUntil;
    private bool _healthDisplayed;

    public KeeperResourceView(GameDatabase database, PlayerId viewingPlayer)
    { _database = database; _viewer = viewingPlayer; }

    public KeeperResourceSnapshot Capture(WorldState world, bool displayHealth = true)
    {
        if (ReferenceEquals(_world, world) && _tick == world.CurrentTick)
        {
            if (displayHealth && !_healthDisplayed && world.HasPlayer(_viewer))
            {
                ObserveHealth(_snapshot.HeartHealth.Value);
                _snapshot = _snapshot with { HeartAlarmUntil = _alarmUntil };
            }
            return _snapshot;
        }
        if (!ReferenceEquals(_world, world))
        {
            _lastHealth = _database[VariableTypeId.DungeonHeartObjectHealth];
            _alarmUntil = default;
        }
        _world = world; _tick = world.CurrentTick;
        _healthDisplayed = false;
        _snapshot = default;
        if (!world.HasPlayer(_viewer)) return _snapshot;
        int index = 0;
        while (world.PlayerAtIndex(index).PlayerId != _viewer) index++;
        int[] income = ManaRules.GainPerSecondByPlayerIndex(world, _database);
        int[] upkeep = ManaRules.UpkeepPerSecondByPlayerIndex(world, _database, world.CurrentTick);
        byte? heartTerrain = ManaRules.DungeonHeartTerrainId(_database);
        int minX = world.Width, minY = world.Height, maxX = -1, maxY = -1;
        for (int i = 0; i < world.TileCount; i++)
            if (world.TileAtIndex(i).OwnerPlayerId == _viewer && world.TileAtIndex(i).TerrainId == heartTerrain)
            {
                int x = i % world.Width, y = i / world.Width;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        bool hasHeart = maxX >= 0;
        TilePos? heartTile = hasHeart ? new TilePos((minX + maxX) / 2, (minY + maxY) / 2) : null;
        ref PlayerState player = ref world.Player(_viewer);
        int health = hasHeart ? player.HeartHealth : 0;
        if (displayHealth) ObserveHealth(health);
        long remaining = player.NextPaydayTick.Value - _tick.Value;
        long interval = player.NextPaydayTick.Value - player.LastPaydayTick.Value;
        long cutoff = _database[VariableTypeId.PayDayCutOffTimeSeconds];
        if (cutoff == 0) cutoff = 100;
        _snapshot = new KeeperResourceSnapshot(new(GoldStore.Total(world, _viewer)),
            new(player.Mana), new(income[index]), new(upkeep[index]),
            new(hasHeart ? player.HeartHealth : 0, _database[VariableTypeId.DungeonHeartObjectHealth]),
            new((int)Math.Clamp(remaining, 0, int.MaxValue), (int)Math.Clamp(interval, 0, int.MaxValue)),
            new(CasinoJackpotRules.WageBill(world, _database, _viewer)), _tick, player.LastPaydayTick,
            player.NextPaydayTick, cutoff * SimulationTiming.OriginalTickPeriodTicks, _alarmUntil, heartTile);
        return _snapshot;
    }

    private void ObserveHealth(int health)
    {
        if (health < _lastHealth)
            _alarmUntil = new GameTick(_tick.Value + 3 * SimulationTiming.TicksPerSecond);
        _lastHealth = health;
        _healthDisplayed = true;
    }
}
