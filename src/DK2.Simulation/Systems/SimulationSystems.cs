namespace DK2.Simulation.Systems;

/// <summary>
/// The systems the game runs, in the order it runs them.
///
/// System order is part of the simulation's definition, not a convenience: two
/// systems that touch the same state in a different order produce a different
/// game. Keeping the order in one place means the harness, the tests and any
/// future front end all run the same game — a scenario that disagreed with the
/// test suite about ordering would produce evidence about neither.
///
/// <para>The order, and why:</para>
/// <list type="number">
/// <item><see cref="TileTagSystem"/> — the player's marks land first, so a tile
/// marked on tick N is marked for the whole of tick N.</item>
/// <item><see cref="RoomBuildSystem"/> — the player's other command. After
/// tagging rather than before it, so that a tile both marked and built on in one
/// tick ends up a room: building is the later, more specific intent, and a mark
/// on room terrain is dropped by the conversion anyway.</item>
/// <item><see cref="CreatureMovementSystem"/> — creatures move, so a worker that
/// arrives beside its job is noticed on the tick it arrives rather than the
/// next.</item>
/// <item><see cref="ImpTaskSystem"/> — workers decide what to do, and queue the
/// work they are in position for.</item>
/// <item><see cref="TileWorkSystem"/> — the work queued this tick resolves this
/// tick, so nothing survives a tick boundary half-done.</item>
/// </list>
///
/// <para><b>UNKNOWN: DK2's own per-tick subsystem ordering.</b> Flame's
/// disassembly names the subsystems but not their sequence. This order is ours,
/// chosen to be explicable rather than to match something we have not
/// measured.</para>
/// </summary>
public static class SimulationSystems
{
    public static ISimulationSystem[] Default() =>
    [
        new TileTagSystem(),
        new RoomBuildSystem(),
        new CreatureMovementSystem(),
        new ImpTaskSystem(),
        new TileWorkSystem(),
    ];
}
