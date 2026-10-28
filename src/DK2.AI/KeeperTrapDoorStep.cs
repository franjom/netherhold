// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>The keeper's traps and doors (<c>0x005000A0</c>), run at step 10 on a tenth of the ticks
/// with allowance: with a Workshop and <c>random(100) &lt; TrapAndDoorUsage</c>, the four edges of
/// the own rooms' stored positions, grown by <c>random(10) - 2</c>, are walked for corridor tiles
/// (<c>0x004FFA50</c>) three tiles clear of the keeper's doors and traps (<c>0x004FFC40</c>); each
/// gets a door or trap (<c>0x004FFD80</c>) and one allowance once its tile test passes
/// (computer-keeper-management.md §9).</summary>
/// <remarks>Deviates: the blueprint is a command executed a tick later. What DK2 lays at once,
/// this pass's spacing and tile tests see through <see cref="_pendingDoors"/> and
/// <see cref="_pendingTraps"/>, held only when the gold is there, as DK2 lays nothing short of
/// gold; the next tick's pass does not see them. The dig-tag test reads the tile's tag for the
/// keeper, not its task pool's kinds 1 and 2 (<c>0x00508290</c>, ADR-0408).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-management.md")]
public sealed class KeeperTrapDoorStep
{
    /// <summary><c>0x005000A0</c>'s third argument to the spacing test: a site at a squared distance
    /// below nine tiles from the keeper's own door or trap is refused.</summary>
    public const int SpacingTiles = 3;

    /// <summary>The boulder's trap id: it takes its own branch when available.</summary>
    public const byte BoulderTrapId = 2;

    /// <summary>A trap whose definition byte <c>+0xBD</c> (<see cref="TrapDef.TriggerType"/>) is this
    /// stops on <c>random(5) &gt; preference</c>, any other on <c>random(5) &lt; preference</c>.</summary>
    public const byte OppositeTriggerType = 1;

    /// <summary>The table at <c>0x006BC350</c>: north, east, south and west.</summary>
    private static readonly (int Dx, int Dy)[] Sides = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>A long corridor runs this many valid tiles beyond the site.</summary>
    private const int LongCorridorTiles = 4;

    private readonly KeeperView _view;
    private readonly KeeperAIParameters _p;
    private readonly SimRandom _rng;
    private readonly List<PlayerCommand> _commands;
    private readonly List<TilePos> _pendingDoors = [];
    private readonly List<TilePos> _pendingTraps = [];
    private readonly List<TilePos> _pendingAll = [];
    private int _gold;

    public KeeperTrapDoorStep(KeeperView view, KeeperAIParameters parameters, SimRandom rng, List<PlayerCommand> commands)
    {
        _view = view;
        _p = parameters;
        _rng = rng;
        _commands = commands;
        _gold = view.Gold;
    }

    /// <summary><c>0x005000A0</c>: returns the allowance left.</summary>
    public int Run(int allowance)
    {
        if (_view.RandomRoomOfType(_view.PlayerId, RoomTypeId.Workshop, _rng) is null) return allowance;
        if ((uint)_rng.NextInt(100) >= _p.TrapAndDoorUsage) return allowance;

        // The rooms' stored positions (+0x27), compared unsigned.
        int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
        foreach (KeeperRoomView room in _view.RoomsOf(_view.PlayerId))
        {
            TilePos at = _view.PositionOf(room);
            minX = Math.Min(minX, at.X);
            minY = Math.Min(minY, at.Y);
            maxX = Math.Max(maxX, at.X);
            maxY = Math.Max(maxY, at.Y);
        }
        int margin = _rng.NextInt(10) - 2;
        minX -= margin; minY -= margin; maxX += margin; maxY += margin;
        // The clamps in 0x005000A0's order.
        int w = _view.Width, h = _view.Height;
        if (minX < 1) minX = 1;
        if (minY < 1) minY = 1;
        if (maxX >= w - 1) maxX = w - 2;
        if (maxY >= h - 1) maxY = h - 2;
        if (maxX < 1) maxX = 1;
        if (maxY < 1) maxY = 1;
        if (minX >= w - 1) minX = w - 2;
        if (minY >= h - 1) minY = h - 2;

        // West tests only its first tile (0x0050025F compares with the same minimum); then east,
        // north and south in full, each while allowance remains.
        if (allowance > 0) allowance = Try(allowance, minX, minY);
        for (int y = minY; y <= maxY && allowance > 0; y++) allowance = Try(allowance, maxX, y);
        for (int x = minX; x <= maxX && allowance > 0; x++) allowance = Try(allowance, x, minY);
        for (int x = minX; x <= maxX && allowance > 0; x++) allowance = Try(allowance, x, maxY);
        return allowance;
    }

    private int Try(int allowance, int x, int y) =>
        ValidSite(x, y) && Spaced(x, y) ? Place(allowance, new TilePos(x, y)) : allowance;

    /// <summary><c>0x004FFA50</c>: an interior tile, open, between two solid tiles west and east (or
    /// failing that north and south), neither of which this keeper has tagged.</summary>
    public bool ValidSite(int x, int y)
    {
        if (x <= 0 || y <= 0 || x >= _view.Width - 1 || y >= _view.Height - 1) return false;
        if (_view.IsNavSolid(new TilePos(x, y))) return false;
        if (_view.IsNavSolid(new TilePos(x - 1, y)) && _view.IsNavSolid(new TilePos(x + 1, y)))
            return !_view.IsTaggedByMe(new TilePos(x - 1, y)) && !_view.IsTaggedByMe(new TilePos(x + 1, y));
        if (_view.IsNavSolid(new TilePos(x, y - 1)) && _view.IsNavSolid(new TilePos(x, y + 1)))
            return !_view.IsTaggedByMe(new TilePos(x, y - 1)) && !_view.IsTaggedByMe(new TilePos(x, y + 1));
        return false;
    }

    /// <summary><c>0x004FFC40</c>: the keeper's own doors, then traps, at tile centres.</summary>
    private bool Spaced(int x, int y)
    {
        const int limit = SpacingTiles * SpacingTiles;
        foreach (KeeperDoorView d in _view.Doors)
            if (d.OwnerPlayerId == _view.PlayerId.Value && Near(d.X, d.Y)) return false;
        foreach (TilePos d in _pendingDoors)
            if (Near(d.X, d.Y)) return false;
        foreach (KeeperTrapView t in _view.Traps)
            if (t.OwnerPlayerId == _view.PlayerId.Value && Near(t.X, t.Y)) return false;
        foreach (TilePos t in _pendingTraps)
            if (Near(t.X, t.Y)) return false;
        return true;

        bool Near(int tx, int ty) => ((tx - x) * (tx - x)) + ((ty - y) * (ty - y)) < limit;
    }

    /// <summary><c>0x004FFD80</c>: <c>random(4) &lt; TrapVsDoorPreference</c> makes it a door.</summary>
    private int Place(int allowance, TilePos at)
    {
        if ((uint)_rng.NextInt(4) < (_p.TrapVsDoorPreference & 7u)) return PlaceDoor(allowance, at);
        return PlaceTrap(allowance, at);
    }

    /// <summary>Door ids in order, Barricades skipped, at availability 3; each remembered before its
    /// stop test <c>random(100) &lt;= stop × 50</c>, so the last eligible one is the fallback.</summary>
    private int PlaceDoor(int allowance, TilePos at)
    {
        // DoorPreference is bits 15-16 of +0x4B2: 1 stops at the first, 2 on half, 0 and 3 on 1 in 100.
        uint stop = (_p.DoorPreference & 3) switch { 1 => 2u, 2 => 1u, _ => 0u };
        DoorDef? chosen = null;
        int count = _view.Database.Doors.Count == 0 ? 0 : _view.Database.Doors.Keys.Max();
        for (int id = 1; id <= count; id++)
        {
            if (!_view.Database.Doors.TryGetValue((byte)id, out DoorDef? door)) continue;
            if (door.Flags.HasFlag(DoorFlags.Barricade)) continue;
            if (_view.Availability(AvailabilityKind.Door, id) != 3) continue;
            chosen = door;
            if ((uint)_rng.NextInt(100) <= stop * 50) break;
        }
        if (chosen is null || !_view.CanPlaceDoor(at, chosen, _pendingAll)) return allowance;
        _commands.Add(new PlaceDoorCommand(_view.PlayerId, at, chosen.DoorId));
        if (chosen.GoldCost <= _gold)
        {
            _gold -= chosen.GoldCost;
            _pendingDoors.Add(at);
            _pendingAll.Add(at);
        }
        return allowance - 1;
    }

    /// <summary>Trap ids in order at availability 3. The Boulder, when it is available: without
    /// <c>BoulderTrapsOnLongCorridors</c> it stops on <c>random(4) &gt; preference</c>; with it, it is
    /// taken only where a long corridor leaves the site, and is no fallback otherwise.</summary>
    private int PlaceTrap(int allowance, TilePos at)
    {
        uint preference = _p.AggressiveVsDefensiveTrapPreference & 7u;
        int boulder = _view.Availability(AvailabilityKind.Trap, BoulderTrapId) == 3 ? BoulderTrapId : 0;
        TrapDef? chosen = null;
        int count = _view.Database.Traps.Count == 0 ? 0 : _view.Database.Traps.Keys.Max();
        for (int id = 1; id <= count; id++)
        {
            if (_view.Availability(AvailabilityKind.Trap, id) != 3) continue;
            if (!_view.Database.Traps.TryGetValue((byte)id, out TrapDef? trap)) continue;
            if (id == boulder)
            {
                if (_p.BoulderTrapsOnLongCorridors)
                {
                    if (!LongCorridor(at)) continue;
                    chosen = trap;
                    break;
                }
                chosen = trap;
                if ((uint)_rng.NextInt(4) > preference) break;
            }
            else if (trap.TriggerType == OppositeTriggerType)
            {
                chosen = trap;
                if ((uint)_rng.NextInt(5) > preference) break;
            }
            else
            {
                chosen = trap;
                if ((uint)_rng.NextInt(5) < preference) break;
            }
        }
        if (chosen is null || !_view.CanPlaceTrap(at, _pendingAll)) return allowance;
        _commands.Add(new PlaceTrapCommand(_view.PlayerId, at, chosen.TrapId));
        if (chosen.GoldCost <= _gold)
        {
            _gold -= chosen.GoldCost;
            _pendingTraps.Add(at);
            _pendingAll.Add(at);
        }
        return allowance - 1;
    }

    /// <summary>Some side along which the next four tiles are all valid sites.</summary>
    private bool LongCorridor(TilePos at)
    {
        foreach ((int dx, int dy) in Sides)
        {
            int run = 0;
            while (run < LongCorridorTiles && ValidSite(at.X + (dx * (run + 1)), at.Y + (dy * (run + 1)))) run++;
            if (run == LongCorridorTiles) return true;
        }
        return false;
    }
}
