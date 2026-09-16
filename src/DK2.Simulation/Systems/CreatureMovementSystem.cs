// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>Advances every creature that has a route one tick along it.</summary>
/// <remarks>No creature ever stands in terrain it could not walk into —
/// CONFIRMED (owner, 2026-09-04). A route from <see cref="WorldPathGrid"/> is
/// planned once while the world moves on, so every waypoint is re-checked as it
/// is stepped onto and a blocked one ends the walk (the route is dropped, not
/// re-planned, as in <see cref="ImpTaskSystem"/>); the shipped cause is
/// <see cref="BridgeBurnSystem"/>, a bridge over lava expiring after thirty
/// seconds (Q67). The tile a creature stands on is not tested, as in
/// <see cref="GridPathfinder"/>, so it can walk off ground that turned
/// impassable. UNKNOWN: whether the original stops dead or re-routes. Deviates:
/// routing is ours (Q5) — DK2 uses a navigation mesh and steering (ADR-0155) —
/// and its turning, acceleration and mutual avoidance are not reconstructed.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-tasks.md")]
public sealed class CreatureMovementSystem : ISimulationSystem
{
    private const string SystemName = "CreatureMovement";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        // One mask for the whole tick. The structure mask (Q88 - a portal's
        // centre and a heart's platform) does not depend on the mover, and this
        // is the one place that builds a grid per creature per tick, so
        // computing it here keeps the cost at one pass over the tiles a tick
        // instead of one per creature.
        StructureBlocks structures = StructureBlocks.For(context.World, context.Database);

        foreach (CreatureState creature in context.World.Creatures)
        {
            if (creature.PathIndex >= creature.Path.Count) continue;

            // A creature in the keeper's hand is out of the dungeon: it does
            // not act until it is put down (HandRules.IsHeld, ADR-0062).
            if (HandRules.IsHeld(creature)) continue;

            // Belt and braces: collapsing already clears the path (ADR-0051), so
            // this only bites if a future mechanic sets a path on a creature
            // that then drops. A prisoner does walk (ADR-0073, Q172): the only
            // thing that ever gives one a path is PrisonerWanderSystem and every
            // path it gives is a single step inside the prisoner's own cell, so
            // there is deliberately no "prisoners may not leave" test here.
            if (CreatureDeathRules.IsDown(creature)) continue;

            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            Advance(context.Database, creature, definition,
                new WorldPathGrid(context.Database, context.World, definition, structures));
        }
    }

    private static void Advance(
        GameDatabase database, CreatureState creature, CreatureDef definition, WorldPathGrid grid)
    {
        int step = StepThisTick(database, creature, definition);

        while (step > 0 && creature.PathIndex < creature.Path.Count)
        {
            TilePos waypoint = creature.Path[creature.PathIndex];

            // The route was legal when it was planned; the world has had ticks
            // to change since — a bridge burning is the shipped case. A blocked
            // waypoint ends the walk here.
            if (!grid.IsWalkable(waypoint.X, waypoint.Y))
            {
                creature.Path.Clear();
                creature.PathIndex = 0;
                return;
            }

            int targetX = CreatureState.CentreOf(waypoint.X);
            int targetY = CreatureState.CentreOf(waypoint.Y);

            int dx = targetX - creature.PosX;
            int dy = targetY - creature.PosY;
            int remaining = LegLength(dx, dy);

            if (remaining == 0)
            {
                creature.PathIndex++;
                continue;
            }

            if (step >= remaining)
            {
                creature.PosX = targetX;
                creature.PosY = targetY;
                creature.PathIndex++;
                step -= remaining;
                continue;
            }

            // Two phases, and the order matters: the surplus of the longer axis
            // straight, then both axes at 45 degrees. A creature stops where it
            // crossed into its tile, so diagonal-first would sweep it through the
            // side tile for most of a tile-width and could stand it in rock;
            // surplus-first runs the diagonal phase through the shared corner
            // only - "passes diagonally between two solid tiles" (Q28, ADR-0025).
            int ax = Math.Abs(dx);
            int ay = Math.Abs(dy);
            int surplus = Math.Abs(ax - ay);
            int straight = Math.Min(step, surplus);
            if (ax > ay) creature.PosX += Math.Sign(dx) * straight;
            else creature.PosY += Math.Sign(dy) * straight;
            step -= straight;

            if (step > 0)
            {
                // Both axes together, and the budget buys less of each than it
                // would on a straight run: one tile of axis costs
                // DiagonalCost/StraightCost of path. Capped at what is left of
                // the shorter axis so the snap-to-target case above stays the
                // only way a leg ends.
                int axis = Math.Min(
                    step * GridPathfinder.StraightCost / GridPathfinder.DiagonalCost,
                    Math.Min(ax, ay));
                creature.PosX += Math.Sign(dx) * axis;
                creature.PosY += Math.Sign(dy) * axis;
            }

            step = 0;
        }

        KeepOffTheCorner(creature, grid);
    }

    /// <summary>Nudges a creature off the shared corner, the one place the
    /// two-phase walk can still leave its tile solid: a tick's budget can run
    /// out with the position exactly on a corner, and integer division hands a
    /// corner to the tile with the larger coordinates — between two solid tiles,
    /// a solid one. One position unit toward the waypoint is inside the
    /// destination tile, and a unit is 1/4096 of a tile.</summary>
    private static void KeepOffTheCorner(CreatureState creature, WorldPathGrid grid)
    {
        if (creature.PathIndex >= creature.Path.Count) return;

        TilePos here = creature.Tile;
        if (grid.IsWalkable(here.X, here.Y)) return;

        TilePos waypoint = creature.Path[creature.PathIndex];
        creature.PosX += Math.Sign(CreatureState.CentreOf(waypoint.X) - creature.PosX);
        creature.PosY += Math.Sign(CreatureState.CentreOf(waypoint.Y) - creature.PosY);
    }

    /// <summary>How far a creature must travel to reach a waypoint, in position
    /// units, on the same scale <see cref="StepThisTick"/> spends.</summary>
    /// <remarks>Octile, not square: the surplus of the longer axis at 10/10 plus
    /// the shorter at 14/10, which is the walk <see cref="Advance"/> actually
    /// makes. Movement is continuous at a constant speed (Q29 - speed is in tiles
    /// per second), so a diagonal neighbour is root-2 tile-widths away and must
    /// cost 14/10 - not the 2x of <c>|dx| + |dy|</c>, the 1x of <c>max</c>, or
    /// <c>max x 14/10</c> on a leg that is not square. All three would disagree
    /// with <see cref="GridPathfinder"/>, which costed the route at 14/10, and a
    /// router and a mover that disagree make creatures arrive early or late for
    /// no visible reason. Integer arithmetic on the same 14/10 stand-in for
    /// root-2: no square root, no floating point, no cross-machine drift (Q48).</remarks>
    private static int LegLength(int dx, int dy)
    {
        int ax = Math.Abs(dx);
        int ay = Math.Abs(dy);
        int shorter = Math.Min(ax, ay);
        int surplus = Math.Max(ax, ay) - shorter;
        return surplus + (shorter * GridPathfinder.DiagonalCost / GridPathfinder.StraightCost);
    }

    /// <summary>Distance to cover this tick, in position units.</summary>
    /// <remarks>Speed is tiles per second on a 4096 denominator — STRONGLY
    /// INFERRED: the Imp's 7536 is 1.84, and the globals that name the unit share
    /// the scale (<c>BoulderSpeedTilesPerSecond</c> 4915 = 1.2,
    /// <c>TriggerTrapTriggerSpeedTilesPerSecond</c> 4096 = 1.0,
    /// <c>DefaultTorchLightRadiusTiles</c> 6144 = 1.5). Ticks per second is
    /// UNKNOWN (Q1). It is the creature's level speed, not its record speed
    /// (Q119) — CONFIRMED by the owner, "imps gain speed as they gain more
    /// levels": 105% per level to level 5, up to 250% at level 10, so a level-2
    /// imp covers 7913 raw units a second (<see cref="CreatureLevelRules"/>;
    /// nothing raises a level yet). The remainder is carried because integer
    /// division alone would leave every creature slower than its data (ADR-0001).</remarks>
    private static int StepThisTick(
        GameDatabase database, CreatureState creature, CreatureDef definition)
    {
        int perSecond = CreatureLevelRules.SpeedRaw(database, definition, creature.Level);
        int ticksPerSecond = SimulationTiming.TicksPerSecond;

        int step = perSecond / ticksPerSecond;
        creature.MoveRemainder += perSecond % ticksPerSecond;
        if (creature.MoveRemainder >= ticksPerSecond)
        {
            step++;
            creature.MoveRemainder -= ticksPerSecond;
        }

        return step;
    }
}
