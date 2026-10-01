// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Tests;

/// <summary>The hook context for a test whose creature belongs to no world: the gate's own
/// tests and the record-level ones make a bare <see cref="CreatureState"/>. It is a real world
/// and database, the synthetic pair CI runs on, but not the creature's, so a hook that reads the
/// map under the creature sees an empty map there. A test whose creature is in a world passes
/// that world instead.</summary>
internal static class DetachedHooks
{
    public static readonly StateHookContext Context = Make();

    private static StateHookContext Make()
    {
        var database = TestWorld.Database();
        return new StateHookContext(TestWorld.World(database), database);
    }
}
