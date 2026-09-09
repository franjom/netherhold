// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation;

/// <summary>The world as one particular creature can walk it: a tile is walkable when its terrain is
/// not <see cref="TerrainFlags.Solid"/> and every hazard flag it carries is matched by a creature
/// flag. Immutable and cheap — one instance answers for one creature type against one snapshot of
/// the world, which is what makes it safe to hand to a search; the structure mask is the one thing
/// that costs a pass over the tiles, so build it once for a tick and pass it in.</summary>
/// <remarks>CONFIRMED, both halves (owner, 2026-09-04). Nothing in DK2 passes through rock —
/// <see cref="CreatureFlags.CanFly"/> buys height over hazards, not passage through mass — and imps
/// waded across water to mine a gem island cut off with no bridge. The original's tile tooltip
/// states the rule: "Water. Impassable to some, others are merely hindered by it."
/// <see cref="CreatureFlags.CanWalkOnWater"/> is on 44 of the 46 creatures, the two without being
/// the Vampire and the Unique Vampire (the owner named them from play before the column was read);
/// seven creatures cross lava (<see cref="CreatureFlags.CanWalkOnLava"/>), which is why lava is what a hazard rule mostly acts on (Q117).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-tasks.md")]
public sealed class WorldPathGrid : IPathGrid
{
    private readonly GameDatabase _database;
    private readonly WorldState _world;
    private readonly StructureBlocks _structures;
    private readonly bool _canCrossWater;
    private readonly bool _canCrossLava;

    public WorldPathGrid(GameDatabase database, WorldState world, CreatureDef mover)
        : this(database, world, mover, null) { }

    /// <param name="structures">A structure mask already computed for this world, or null to
    /// compute one. Pass one in when several grids are built for the same tick: the mask does not
    /// depend on the mover.</param>
    public WorldPathGrid(
        GameDatabase database, WorldState world, CreatureDef mover,
        StructureBlocks? structures)
    {
        if (mover is null) throw new ArgumentNullException(nameof(mover));

        _database = database ?? throw new ArgumentNullException(nameof(database));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _structures = structures ?? StructureBlocks.For(database: database, world: world);
        _canCrossWater = mover.Flags.HasFlag(CreatureFlags.CanWalkOnWater);
        _canCrossLava = mover.Flags.HasFlag(CreatureFlags.CanWalkOnLava);
    }

    public int Width => _world.Width;

    public int Height => _world.Height;

    /// <summary>Whether this creature may stand on the tile; <see cref="Systems.CreatureMovementSystem"/>
    /// applies the rule again when stepping, because a route is planned once and the world moves.</summary>
    /// <remarks>
    /// <para>Water and lava carry <c>Impenetrable</c> in the shipped record and that is deliberately
    /// not what is tested: impenetrable means "cannot be tunnelled through" and says nothing about
    /// wading — only <c>IsSolid</c> blocks a walker. Cost is charged elsewhere: <c>TerrainDef.Damage</c>
    /// is 100 for water and lava, and <see cref="Systems.CreatureHealthSystem"/> charges only a
    /// creature the hazard would stop (<see cref="CreatureHealthRules.TerrainHealthPerSecond"/>, CONFIRMED). A bridge tile carries its own terrain, so it needs no case here; the MAP bridge byte is unread (Q16).</para>
    /// <para>Not implemented: "merely hindered" — water visibly slows a crossing creature (CONFIRMED,
    /// owner) and no variable carries it (all 214 balance variables searched; <see cref="TerrainDef"/>
    /// has no speed field), so a crossing runs at full walking speed. Hypothesis: the unread third
    /// speed <c>ShuffleSpeed</c> (Imp 4096 against its 7536 walk, Vampire 1638 against 2867 — not a constant ratio, so per creature). Routing is unaffected, the shortest route winning even through water (Q30, CONFIRMED): the gap costs arrival times, not paths (Q118).</para></remarks>
    public bool IsWalkable(int x, int y)
    {
        if (!_world.Contains(x, y)) return false;

        TerrainDef terrain = _database.TerrainById(_world.Tile(x, y).TerrainId);
        if (terrain.IsSolid) return false;
        if (terrain.IsWater && !_canCrossWater) return false;
        if (terrain.IsLava && !_canCrossLava) return false;

        // A hole in a portal and a heart on its platform. Not a terrain flag,
        // because all nine tiles of a portal are one terrain (Q88).
        return !_structures.Blocks((y * _world.Width) + x);
    }
}
