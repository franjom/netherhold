// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

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
/// <item><see cref="RoomSellSystem"/> — before building rather than after, so a
/// keeper who sells one room to afford another can do both on the same tick. The
/// friendlier of the two orderings, and the same reasoning as
/// <see cref="ImpSupplySystem"/> running after the mana it spends.</item>
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
/// <item><see cref="BridgeBurnSystem"/> — a wooden bridge over lava runs out
/// (Q67). After the work, so a bridge somebody was repairing on the tick it
/// burned gets the repair and then burns anyway, which is the order that makes
/// the fire final rather than something a keeper can outrun by a tick.</item>
/// <item><see cref="ManaSystem"/> — mana is a consequence of what a keeper owns
/// and everything that can change that has now run. A tile claimed on tick N is
/// worth mana from tick N.</item>
/// <item><see cref="ImpSupplySystem"/> — last, and after the mana it spends. A
/// keeper who earns the price of an imp on tick N can cast for it on tick N,
/// which is the friendlier of the two orderings and the one a player would
/// expect. It also means a new imp starts looking for work on tick N+1 rather
/// than immediately, which keeps <see cref="ImpTaskSystem"/>'s "spawn order
/// decides who gets the job" rule honest for creatures created mid-tick.</item>
/// <item><see cref="PortalSystem"/> — after it, so the two things that make
/// creatures sit together at the end of the tick and a creature born on tick N
/// starts acting on tick N+1 either way. After building and selling for a
/// concrete reason: a lair built on tick N attracts on tick N, which is the
/// answer a player watching their first goblin arrive would expect. Nothing it
/// does can affect a system above it in the same tick, so its position is a
/// statement about what a new creature can see rather than a constraint.</item>
/// <item><see cref="CreatureSleepSystem"/> — the driver for creatures that are
/// not workers, and the first one there has ever been: it claims a bed, walks
/// the creature to it and puts it to sleep. Placed here for the same reason
/// <see cref="ImpTaskSystem"/> sits after movement — it sets a path that
/// movement walks on the following tick — and <i>before</i> the health system,
/// so a creature that falls asleep this tick is healed on the same second
/// rather than a second late.</item>
/// <item><see cref="CreatureHealthSystem"/> — last, so a creature that arrives
/// on tick N is not regenerated or aged on the tick it arrives, and its hunger
/// clock starts from a whole second. It also means anything that damages a
/// creature earlier in the tick is resolved before death is checked, so a
/// creature killed and healed in the same second nets out rather than dying on
/// the intermediate value. Per-second, on the same phase as
/// <see cref="ManaSystem"/>.</item>
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
        new RoomSellSystem(),
        new RoomBuildSystem(),
        new CreatureMovementSystem(),
        new ImpTaskSystem(),
        new TileWorkSystem(),
        new BridgeBurnSystem(),
        new ManaSystem(),
        new ImpSupplySystem(),
        new PortalSystem(),
        new CreatureSleepSystem(),
        new CreatureHealthSystem(),
    ];
}
