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
    ];
}
