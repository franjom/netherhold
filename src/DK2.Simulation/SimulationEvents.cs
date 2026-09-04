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

namespace DK2.Simulation;

/// <summary>
/// Something the simulation did, recorded for observation only.
///
/// Events are the harness's and the oracle comparison's window into the
/// simulation. They are strictly write-only from the simulation's point of
/// view: nothing in the simulation ever reads an event back, so attaching a
/// recorder cannot change behaviour. That property is asserted by
/// <c>DeterminismTests.EventRecording_DoesNotAffectState</c>.
/// </summary>
/// <param name="Tick">The tick during which it happened.</param>
/// <param name="System">Which system emitted it.</param>
/// <param name="Kind">Stable machine-readable event name.</param>
/// <param name="Detail">Human-readable specifics; never parsed by the simulation.</param>
public readonly record struct SimEvent(GameTick Tick, string System, string Kind, string Detail)
{
    public override string ToString() => $"{Tick} [{System}] {Kind}: {Detail}";
}

public interface ISimulationEventSink
{
    void Emit(in SimEvent simEvent);
}

/// <summary>Discards everything. The default, so events cost nothing when unobserved.</summary>
public sealed class NullEventSink : ISimulationEventSink
{
    public static readonly NullEventSink Instance = new();

    private NullEventSink() { }

    public void Emit(in SimEvent simEvent) { }
}

/// <summary>Keeps every event in memory. Used by tests and the scenario harness.</summary>
public sealed class ListEventSink : ISimulationEventSink
{
    private readonly List<SimEvent> _events = [];

    public IReadOnlyList<SimEvent> Events => _events;

    public void Emit(in SimEvent simEvent) => _events.Add(simEvent);

    public void Clear() => _events.Clear();
}
