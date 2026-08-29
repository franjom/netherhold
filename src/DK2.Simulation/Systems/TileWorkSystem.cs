using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>
/// Resolves the work applications queued on the world this tick, in queue
/// order, and clears the queue.
///
/// The system owns no policy. It does not decide who works, what they work on,
/// or how often — those are creature decisions (M3, later) and a timing question
/// (Q19/Q1). All it does is turn "one application of kind K by player P on tile
/// I" into the health change and terrain transition that DK2's data says it is,
/// via <see cref="TileWorkRules"/>.
///
/// <para>
/// Gold released by mining is reported as an event and otherwise dropped. In DK2
/// the imp that freed it picks it up and carries it to a treasury; no carrier
/// and no treasury exist yet, so crediting a player directly would be inventing
/// an economy rather than reproducing one.
/// </para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/tile-work.md")]
public sealed class TileWorkSystem : ISimulationSystem
{
    private const string SystemName = "TileWork";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        if (world.PendingWork.Count == 0) return;

        // Snapshot the count first: resolving an order must not be able to make
        // the loop consume work queued by the same tick's later systems.
        int count = world.PendingWork.Count;

        for (int i = 0; i < count; i++)
        {
            TileWorkOrder order = world.PendingWork[i];
            ref TileState tile = ref world.TileAtIndex(order.TileIndex);
            byte before = tile.TerrainId;

            TileWorkResult result = TileWorkRules.Apply(
                context.Database, ref tile, order.Kind, order.PlayerId);

            Report(context, order, result, before, world);
        }

        world.RawPendingWork.RemoveRange(0, count);
    }

    private static void Report(
        SimulationContext context,
        TileWorkOrder order,
        in TileWorkResult result,
        byte terrainBefore,
        WorldState world)
    {
        int x = order.TileIndex % world.Width;
        int y = order.TileIndex / world.Width;
        string where = $"({x},{y}) player={order.PlayerId} {order.Kind}";

        switch (result.Outcome)
        {
            case TileWorkOutcome.Rejected:
                context.Emit(SystemName, "rejected",
                    $"{where} terrain={terrainBefore}");
                return;

            case TileWorkOutcome.NoChange:
                return;

            case TileWorkOutcome.Destroyed:
                context.Emit(SystemName, "destroyed",
                    $"{where} terrain {terrainBefore}->{result.NewTerrainId}");
                break;

            case TileWorkOutcome.Transformed:
                context.Emit(SystemName, "transformed",
                    $"{where} terrain {terrainBefore}->{result.NewTerrainId}");
                break;

            case TileWorkOutcome.Progressed:
            default:
                break;
        }

        if (result.GoldReleased > 0)
        {
            context.Emit(SystemName, "gold_released",
                $"{where} amount={result.GoldReleased} (uncollected: no carrier yet)");
        }

        if (result.GoldSpilled > 0)
        {
            context.Emit(SystemName, "gold_spilled",
                $"{where} amount={result.GoldSpilled} (lost: gold piles are objects, not implemented)");
        }
    }
}
