// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation;

/// <summary>
/// The world as one particular creature can walk it.
///
/// Walkability is derived from the data rather than declared here: a tile is
/// walkable when its terrain is not <see cref="TerrainFlags.Solid"/>, and when
/// any hazard flag the terrain carries is matched by a corresponding creature
/// flag.
///
/// <para><b>The solid half is CONFIRMED</b> (owner, 2026-09-04): "creatures can
/// not pass through walls, they can only move through dug out tiles, not
/// through solid rocks". Stated for its own sake rather than in answer to a
/// question, after watching another reimplementation get it wrong. Nothing in
/// DK2 flies through rock — <see cref="CreatureFlags.CanFly"/> buys height over
/// hazards, not passage through mass — so the flag needs no exception here.
/// <see cref="Systems.CreatureMovementSystem"/> enforces the same rule a second
/// time at the moment of stepping, because a route is planned once and the
/// world keeps moving.</para>
///
/// <para>The hazard half is STRONGLY INFERRED — <see cref="CreatureFlags.CanWalkOnWater"/> and
/// <see cref="CreatureFlags.CanWalkOnLava"/> exist for no other purpose, and
/// they fall exactly where DK2's behaviour puts them (imps cross water, most
/// creatures do not; almost nothing crosses lava).</para>
///
/// <para>UNKNOWN, and deliberately not guessed at: bridges. A bridge over water
/// or lava is a room built on a hazard tile, and the MAP record's byte 3 is
/// bridge-related but unread (Q16). Until rooms exist, this grid says a lava
/// tile is impassable to a creature that cannot cross lava, whether or not the
/// original would have a bridge there.</para>
///
/// <para>Immutable, and cheap to construct: one instance answers for one
/// creature type against one snapshot of the world, which is what makes it safe
/// to hand to a search that must not see the world change under it.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-tasks.md")]
public sealed class WorldPathGrid : IPathGrid
{
    private readonly GameDatabase _database;
    private readonly WorldState _world;
    private readonly bool _canCrossWater;
    private readonly bool _canCrossLava;

    public WorldPathGrid(GameDatabase database, WorldState world, CreatureDef mover)
    {
        if (mover is null) throw new ArgumentNullException(nameof(mover));

        _database = database ?? throw new ArgumentNullException(nameof(database));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _canCrossWater = mover.Flags.HasFlag(CreatureFlags.CanWalkOnWater);
        _canCrossLava = mover.Flags.HasFlag(CreatureFlags.CanWalkOnLava);
    }

    public int Width => _world.Width;

    public int Height => _world.Height;

    public bool IsWalkable(int x, int y)
    {
        if (!_world.Contains(x, y)) return false;

        TerrainDef terrain = _database.TerrainById(_world.Tile(x, y).TerrainId);
        if (terrain.IsSolid) return false;
        if (terrain.IsWater && !_canCrossWater) return false;
        if (terrain.IsLava && !_canCrossLava) return false;
        return true;
    }
}
