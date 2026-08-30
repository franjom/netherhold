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
            Advance(creature, context.Database.CreatureById(creature.CreatureTypeId));
        }
    }

    private static void Advance(CreatureState creature, CreatureDef definition)
    {
        int step = StepThisTick(creature, definition);

        while (step > 0 && creature.PathIndex < creature.Path.Count)
        {
            TilePos waypoint = creature.Path[creature.PathIndex];
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

            if (dx != 0 && dy != 0)
            {
                // A diagonal leg advances both axes together, and the budget
                // buys less of each than it would on a straight one: covering
                // one tile of axis costs DiagonalCost/StraightCost of path.
                int axis = step * GridPathfinder.StraightCost / GridPathfinder.DiagonalCost;
                creature.PosX += Math.Sign(dx) * axis;
                creature.PosY += Math.Sign(dy) * axis;
            }
            else if (dx != 0)
            {
                creature.PosX += Math.Sign(dx) * step;
            }
            else
            {
                creature.PosY += Math.Sign(dy) * step;
            }

            step = 0;
        }
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
    /// </summary>
    private static int LegLength(int dx, int dy)
    {
        int ax = Math.Abs(dx);
        int ay = Math.Abs(dy);
        if (ax == 0 || ay == 0) return ax + ay;

        // Legs between tile centres are square, so either axis gives the same
        // answer; taking the larger keeps a mid-leg position (where rounding may
        // have left the axes a unit apart) from under-charging.
        return Math.Max(ax, ay) * GridPathfinder.DiagonalCost / GridPathfinder.StraightCost;
    }

    /// <summary>
    /// Distance to cover this tick, in position units.
    ///
    /// Integer division alone would discard the remainder every tick and leave
    /// every creature permanently slower than its own data says; the carried
    /// remainder makes the speed exact over any whole number of seconds while
    /// keeping the arithmetic in integers (ADR-0001).
    /// </summary>
    private static int StepThisTick(CreatureState creature, CreatureDef definition)
    {
        int perSecond = definition.Speed.Raw;
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
