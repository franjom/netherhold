// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

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
/// <para><b>The hazard half is now CONFIRMED</b> (owner observation,
/// 2026-09-04) after being inferred from the existence of
/// <see cref="CreatureFlags.CanWalkOnWater"/> and
/// <see cref="CreatureFlags.CanWalkOnLava"/> alone. The owner marked a gem tile
/// on an island cut off by water, with no bridge anywhere, and <b>imps waded
/// across, mined it and carried the gold home</b>. And the original states the
/// rule itself in its own tile tooltip:</para>
///
/// <para><i>"Water. Impassable to some, others are merely hindered by it."</i></para>
///
/// <para>Which is as close to a specification as this project has found in the
/// game's own text — a per-creature passability flag and a penalty for those who
/// do cross, in one sentence. Both hazard terrains carry
/// <c>Impenetrable</c> in the shipped record and that is deliberately <i>not</i>
/// what is tested here: impenetrable means "cannot be tunnelled through", which
/// is true of water and says nothing about wading. Only <c>IsSolid</c> blocks a
/// walker, and water is not solid.</para>
///
/// <para><b>What "merely hindered" implies is NOT implemented, and there are two
/// halves of it</b> (Q117):</para>
///
/// <list type="bullet">
/// <item><b>The slowdown.</b> "Hindered" reads as a movement penalty, and no
/// variable for one was found — 214 balance variables were searched and there is
/// no water speed modifier. A creature crosses water here at its normal
/// speed.</item>
/// <item><b>The health cost.</b> <c>TerrainDef.Damage</c> is <b>100</b> for
/// Water and Lava, and it is applied by
/// <see cref="Systems.CreatureHealthSystem"/> — but only since the owner's
/// observation prompted a look, because before that it was parsed and read by
/// nothing but <c>AssetCatalogue</c>. The DK2 wiki corroborates that it is real:
/// its Vampire page warns against patrols routed through water because they
/// "may get damaged, resurrect, and even die".</item>
/// </list>
///
/// <para><b>And checking that claim found a third terrain.</b> The assertion
/// first written here was that <c>Damage</c> is non-zero on Water and Lava
/// alone. It is not: <b>Hero Lair</b> (terrain 35) carries <b>−67</b>. So the
/// field is <i>signed</i> and is not damage at all but a <b>health delta per
/// second</b> — the hero side's answer to a keeper's lair, healing whatever
/// stands on it. Three of 41 terrains, which is the same shape as mana
/// (three of 41 generate any, ADR-0017) and own-land regeneration (two of 46),
/// and the third time this project has been caught claiming a column was
/// simpler than it is.</para>
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
