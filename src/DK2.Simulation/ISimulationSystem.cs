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

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Everything a system may touch during one tick.
///
/// Deliberately narrow: a system gets the static database, the mutable world,
/// the tick's input, the shared RNG and an event sink — and nothing else. No
/// clock, no file system, no ambient state. That is what makes the run
/// reproducible.
/// </summary>
public sealed class SimulationContext
{
    internal SimulationContext(GameDatabase database, WorldState world, SimRandom rng, ISimulationEventSink events)
    {
        Database = database;
        World = world;
        Rng = rng;
        Events = events;
    }


    public GameDatabase Database { get; }
    public WorldState World { get; }
    public SimRandom Rng { get; }
    public ISimulationEventSink Events { get; internal set; }

    /// <summary>The tick currently being executed.</summary>
    public GameTick Tick { get; internal set; }

    /// <summary>Commands issued for the tick currently being executed.</summary>
    public InputFrame Input { get; internal set; }

    public void Emit(string system, string kind, string detail) =>
        Events.Emit(new SimEvent(Tick, system, kind, detail));
}

/// <summary>
/// One unit of per-tick simulation work.
///
/// Systems run in a fixed registration order, once per tick, single-threaded.
/// Ordering is part of the simulation's definition: two systems that touch the
/// same state in a different order produce a different game, so the order is
/// declared explicitly by whoever builds the <see cref="GameSimulation"/> and
/// is never inferred, sorted or parallelised.
///
/// UNKNOWN: the order in which the original updates its own subsystems. When
/// that starts to matter (M4 onwards) it becomes a research question in its own
/// right rather than something we settle by convenience.
/// </summary>
public interface ISimulationSystem
{
    /// <summary>Stable name, used in event logs and divergence reports.</summary>
    string Name { get; }

    void Execute(SimulationContext context);
}
