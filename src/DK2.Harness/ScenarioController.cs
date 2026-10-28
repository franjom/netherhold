// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Harness;

/// <summary>
/// A test controller for one player: it reads a <see cref="KeeperView"/> after a tick and returns
/// the commands for the next one, the way a computer keeper does (KEEPER_AI.md §12.1). It is an
/// input source and nothing else, so its own state stays outside <c>StateHash()</c> and a run is
/// reproduced by the commands it emitted (ADR-0552).
///
/// <para>The view is privileged: <see cref="KeeperView"/> applies no fog of war. That is acceptable
/// for testing and is why a controller is not a competitive player.</para>
/// </summary>
/// <param name="Name">Names the controller in the report and the command log.</param>
/// <param name="PlayerId">The only player this controller may command.</param>
/// <param name="Decide">Called with a view captured after the tick; may return no commands.</param>
public sealed record ScenarioController(
    string Name, PlayerId PlayerId, Func<KeeperView, IReadOnlyList<PlayerCommand>> Decide)
{
    /// <summary>Decide after every this-many ticks, starting after tick 0. Building a view is not
    /// cheap (navigation bytes, room index, spell-placement checks), so a controller that does not
    /// need every tick should say so.</summary>
    public int IntervalTicks { get; init; } = 1;
}

/// <summary>A command a test controller emitted, with the tick whose input frame carries it.</summary>
public sealed record ControllerCommandEmission(int InputTick, string Controller, PlayerCommand Command);
