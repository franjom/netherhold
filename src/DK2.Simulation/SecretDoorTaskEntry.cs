// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation;

/// <summary>One keeper's kind-11 entry: insertion order and the four middle-slot holders.
/// Holders outlive a creature's reassignment: the native leave hook omits state 38 and
/// state 39 clears slot 0 rather than the reserved slot 1 (imp-task-objects-and-walks.md §2).</summary>
public sealed class SecretDoorTaskEntry
{
    public int Order { get; init; }
    public int[] Holders { get; } = new int[4];
}
