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
/// <para><b>And the roster says water is barely a barrier at all.</b>
/// <see cref="CreatureFlags.CanWalkOnWater"/> is on <b>44 of the 46</b>
/// creatures; the two without it are the <b>Vampire</b> and the <b>Unique
/// Vampire</b>. The owner named that from play before the column had been read
/// — <i>"I think vampires can't cross through water, they need bridges"</i> —
/// which is a two-element set picked out of 46 from memory, in a record whose
/// flag layout this project reconstructed rather than read anywhere, and is the
/// strongest single confirmation the bit has. So the tooltip's two clauses split
/// the roster 2 / 44: <i>impassable to some</i> is the vampires, literally, and
/// everybody else is merely hindered. <b>Lava is the opposite shape</b> — seven
/// creatures cross it — which is why lava, not water, is what a hazard rule
/// mostly acts on.</para>
///
/// <para><b>What "merely hindered" means is CONFIRMED and NOT implemented</b>
/// (Q118). The owner: <i>"water visibly slows imps and other creatures passing
/// through it"</i>. There is no variable for it — all 214 balance variables were
/// searched, and <see cref="TerrainDef"/> carries no speed field — so a creature
/// crosses water here at full walking speed. The standing hypothesis is the
/// unread third speed every creature record carries, <c>ShuffleSpeed</c> (Imp
/// 4096 against its 7536 walk, Vampire 1638 against 2867): not a constant ratio,
/// so a per-creature speed rather than a global multiplier, which is the shape a
/// hindrance needs. Timing an imp over a known run of water against the same run
/// of floor would implicate it or rule it out; until then nothing is invented,
/// because a made-up multiplier here is a made-up number inside a determinism
/// hash. Routing is unaffected either way — the shortest route wins even through
/// water (Q30, CONFIRMED) — so the gap costs arrival times, not paths.</para>
///
/// <para><b>The health cost is implemented, and it is not paid by anyone who can
/// cross</b> (CONFIRMED, owner: <i>"no creature that can traverse through water
/// looses health"</i>). <c>TerrainDef.Damage</c> is 100 for Water and Lava and
/// is applied by <see cref="Systems.CreatureHealthSystem"/> only to a creature
/// the hazard would stop — which for water means the two vampires and nothing
/// else. See <see cref="CreatureHealthRules.TerrainHealthPerSecond"/>, including
/// the wiki's warning about water, which sits on the Vampire page and so agrees
/// with both the owner and the data about exactly one creature.</para>
///
/// <para><b>Bridges are what the flagless creature uses instead</b>, and the
/// owner said so in the same breath as the vampires: <i>"they need
/// bridges"</i>. A bridge over water or lava is a room built on a hazard tile,
/// and a bridge tile carries its own terrain — so a bridged crossing is walkable
/// to everybody by the ordinary rule here, with no bridge-specific case. What is
/// still unread is the MAP record's bridge byte (Q16); until rooms are placed
/// from a map, this grid says a lava tile is impassable to a creature that
/// cannot cross lava whether or not the original would have a bridge there.</para>
///
/// <para><b>And a room's own structure blocks its tiles</b> (Q88, CONFIRMED by
/// the owner 2026-09-07): a portal's centre tile and a dungeon heart's centre
/// 3x3 are not walkable, because there is a hole in one and a heart standing on
/// the other. That is <see cref="StructureBlocks"/>, and it is the one part of
/// walkability here that is <i>not</i> derived from terrain flags — which is why
/// it lives in its own type with its own account of what is known and what is
/// not. This paragraph used to say the opposite, and said it in
/// <see cref="PortalRules.EntranceTileIndex"/> too.</para>
///
/// <para>Immutable, and cheap to construct: one instance answers for one
/// creature type against one snapshot of the world, which is what makes it safe
/// to hand to a search that must not see the world change under it. The
/// structure mask is the one thing that costs a pass over the tiles, so a caller
/// building many grids for one tick should build the mask once and pass it in.</para>
/// </summary>
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

    /// <param name="structures">
    /// A structure mask already computed for this world, or null to compute one.
    /// Pass one in when several grids are built for the same tick: the mask does
    /// not depend on the mover.
    /// </param>
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
