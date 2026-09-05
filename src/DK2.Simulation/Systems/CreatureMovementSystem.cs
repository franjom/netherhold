// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>
/// Walks every creature that has somewhere to be one tick further along its
/// route.
///
/// <para><b>Speed is read as tiles per second (STRONGLY INFERRED).</b> The
/// CREATURES record stores speed as a 4096-denominator fixed-point number and
/// gives the Imp 7536, i.e. 1.84. The global variables table uses the identical
/// scale for values whose names state their unit —
/// <c>BoulderSpeedTilesPerSecond</c> = 4915 (1.2),
/// <c>TriggerTrapTriggerSpeedTilesPerSecond</c> = 4096 (1.0),
/// <c>DefaultTorchLightRadiusTiles</c> = 6144 (1.5) — so a speed of 1.84 on the
/// same scale is 1.84 tiles per second. Nothing in the creature record itself
/// says so, which is why this is inferred rather than confirmed.</para>
///
/// <para>How many ticks a second is remains UNKNOWN (Q1), so this system says
/// nothing about how long a walk takes in wall-clock terms. What it does fix is
/// the relationship between walking and working: both are driven from
/// <see cref="SimulationTiming"/>, so an imp's travel time and its dig rate stay
/// in the same proportion however Q1 is eventually answered.</para>
///
/// <para>Movement is along the route the pathfinder returned, which is
/// four-connected, so every leg is a straight line along one axis. Ours, not
/// DK2's: the original's creatures visibly turn, accelerate and avoid each
/// other, none of which is reconstructed here.</para>
///
/// <para><b>A creature never enters a tile it could not walk to
/// (CONFIRMED).</b> "Creatures can not pass through walls, they can only move
/// through dug out tiles, not through solid rocks" — the owner, 2026-09-04,
/// made explicit after watching another reimplementation let imps walk through
/// rock. The route came from <see cref="WorldPathGrid"/> and so was legal when
/// it was planned, but a route is planned once and the world keeps moving. The
/// shipped cause is <see cref="BridgeBurnSystem"/>: a wooden bridge over lava
/// runs out after thirty seconds and the tile becomes lava again (Q67), so a
/// creature already walking a route across it would otherwise stroll over the
/// lava. Every waypoint is therefore re-checked at the moment it is stepped
/// onto, and a creature that finds one blocked stops and forgets the route
/// rather than entering it. Dropping the route rather than re-planning here is
/// the same choice <see cref="ImpTaskSystem"/> already makes when the world
/// changes under a walk, and it composes with both users of a path: a worker
/// abandons the job and looks again, a carrier re-routes to somewhere else that
/// will take its gold.</para>
///
/// <para>The mover leaves the tile a creature is <i>standing</i> on alone, the
/// same way <see cref="Pathfinding.GridPathfinder"/> does not test a mover's
/// starting tile: a creature caught on ground that turned impassable has to be
/// able to walk off it.</para>
///
/// <para>UNKNOWN, and deliberately not invented: what the original's creature
/// does at that instant — whether it stops dead or re-routes around. Only the
/// observable rule is reproduced, which is that no creature is ever inside
/// terrain it could not have walked into.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-tasks.md")]
public sealed class CreatureMovementSystem : ISimulationSystem
{
    private const string SystemName = "CreatureMovement";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (CreatureState creature in context.World.Creatures)
        {
            if (creature.PathIndex >= creature.Path.Count) continue;

            // Belt and braces: collapsing clears the path, so a dying creature
            // has nothing to walk anyway (ADR-0051). The guard is here so that a
            // future mechanic which sets a path on a creature that then drops
            // cannot make a corpse-to-be keep walking.
            // A creature in the keeper's hand is out of the dungeon: it does
            // not act until it is put down (HandRules.IsHeld, ADR-0062).
            if (HandRules.IsHeld(creature)) continue;

            // Nor does a prisoner: it stands in its cell and withers, and its
            // whole second belongs to PrisonSystem (ADR-0064).
            if (PrisonRules.IsImprisoned(creature)) continue;
            if (CreatureDeathRules.IsDown(creature)) continue;

            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            Advance(context.Database, creature, definition,
                new WorldPathGrid(context.Database, context.World, definition));
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

            // A leg is walked in two phases: the SURPLUS of the longer axis
            // first, straight, and only then both axes together at 45 degrees.
            // The order is the whole point. A creature stops wherever it
            // crossed into its tile, not at the centre, so a leg to a diagonal
            // neighbour rarely starts square; walked 45 degrees first it sweeps
            // through the side tile for most of a tile-width, and if that side
            // tile is rock the creature is standing in rock. Walked surplus
            // first, the 45-degree phase runs exactly through the corner the two
            // tiles share and touches nothing else -- which is what the owner's
            // "a creature passes diagonally between two solid tiles" (Q28,
            // ADR-0025) looks like once a creature has a width. Routing is ours
            // (Q5); this is the shape of it that keeps ADR-0025 and the
            // no-creature-in-rock invariant true at the same time.
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

    /// <summary>
    /// The one place the two-phase walk above can still leave a creature's
    /// tile solid: the shared corner itself, when a tick's budget runs out with
    /// the position exactly on it. A corner belongs to four tiles and integer
    /// division hands it to the one with the larger coordinates, which between
    /// two solid tiles is one of the solid ones. One position unit toward the
    /// waypoint is inside the destination tile, and a unit is 1/4096 of a tile
    /// -- below anything the game can show or the hash can care about except in
    /// the sign of it being right.
    /// </summary>
    private static void KeepOffTheCorner(CreatureState creature, WorldPathGrid grid)
    {
        if (creature.PathIndex >= creature.Path.Count) return;

        TilePos here = creature.Tile;
        if (grid.IsWalkable(here.X, here.Y)) return;

        TilePos waypoint = creature.Path[creature.PathIndex];
        creature.PosX += Math.Sign(CreatureState.CentreOf(waypoint.X) - creature.PosX);
        creature.PosY += Math.Sign(CreatureState.CentreOf(waypoint.Y) - creature.PosY);
    }

    /// <summary>
    /// How far a creature must travel to reach a waypoint, in position units and
    /// on the same scale <see cref="StepThisTick"/> spends.
    ///
    /// <para><b>A diagonal leg is longer than a straight one, and by exactly the
    /// factor the router charges.</b> Movement is continuous through world space
    /// at a constant speed (Q29 - a creature's speed is in tiles per second), so
    /// crossing to a diagonal neighbour covers root-2 tile-widths and takes
    /// 1.41x as long. Using <c>|dx| + |dy|</c> here would charge 2x, and using
    /// <c>max</c> would charge 1x; both are wrong, and both would put the mover
    /// out of step with <see cref="GridPathfinder"/>, whose route was costed at
    /// 14/10. A router and a mover that disagree produce creatures that arrive
    /// early or late for no reason anybody can see.</para>
    ///
    /// <para>Integer arithmetic throughout, on the same 14/10 stand-in for
    /// root-2 the search uses: no square root, so no floating point, so no
    /// cross-machine drift (Q48).</para>
    ///
    /// <para><b>Octile, not square.</b> A leg between two tile centres is
    /// square and either axis would do; a leg from wherever a creature actually
    /// stopped is not, and this is the length of the walk
    /// <see cref="Advance"/> makes -- the surplus of the longer axis straight,
    /// then the rest at 14/10. Charging <c>max x 14/10</c> for an unsquare leg
    /// would make a creature arrive late for no reason anybody could see.</para>
    /// </summary>
    private static int LegLength(int dx, int dy)
    {
        int ax = Math.Abs(dx);
        int ay = Math.Abs(dy);
        int shorter = Math.Min(ax, ay);
        int surplus = Math.Max(ax, ay) - shorter;
        return surplus + (shorter * GridPathfinder.DiagonalCost / GridPathfinder.StraightCost);
    }

    /// <summary>
    /// Distance to cover this tick, in position units.
    ///
    /// Integer division alone would discard the remainder every tick and leave
    /// every creature permanently slower than its own data says; the carried
    /// remainder makes the speed exact over any whole number of seconds while
    /// keeping the arithmetic in integers (ADR-0001).
    ///
    /// <para><b>Speed is the creature's level speed, not its record speed</b>
    /// (Q119). CONFIRMED by the owner: <i>"imps gain speed as they gain more
    /// levels"</i>, and the shipped stat table says by how much — 105% per level
    /// to level 5, then up to 250% at level 10. So a level-2 imp covers 7913
    /// raw units a second against its record's 7536, which is the difference the
    /// owner could see on screen. See <see cref="CreatureLevelRules"/>; nothing
    /// raises a level yet, so today this reads 100% for everything that was not
    /// placed at a level by hand.</para>
    /// </summary>
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
