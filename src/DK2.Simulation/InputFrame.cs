// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>
/// A command issued by a player (or a scripted actor) for one tick.
///
/// DK2 itself is command-driven: Flame's disassembly names a <c>GameAction</c>
/// / <c>GameActionArray</c> pair (STRONGLY INFERRED: the original simulation is
/// advanced by an ordered list of actions per tick, which is also what makes
/// its multiplayer lockstep possible). We mirror that shape because it is the
/// structure that makes replay and the determinism gate possible.
///
/// No concrete commands exist yet — the first ones (tag for digging, claim,
/// build room) arrive with M3.
/// </summary>
public abstract record PlayerCommand(byte PlayerId)
{
    /// <summary>
    /// Contributes this command to a replay hash. Every command must declare
    /// its own contribution so that adding a field cannot silently weaken the
    /// input side of the determinism gate.
    /// </summary>
    public virtual void HashInto(ref Hash64 hash)
    {
        hash.Add(GetType().Name);
        hash.Add(PlayerId);
    }
}

/// <summary>
/// Everything entering the simulation on a single tick. The simulation reads
/// nothing else from the outside world: same seed + same input frames =&gt; same
/// state, by construction.
/// </summary>
public readonly record struct InputFrame(IReadOnlyList<PlayerCommand>? Commands)
{
    public static readonly InputFrame Empty = new((IReadOnlyList<PlayerCommand>?)null);

    private static readonly PlayerCommand[] None = [];

    public IReadOnlyList<PlayerCommand> All => Commands ?? None;

    public static InputFrame Of(params PlayerCommand[] commands) => new(commands);

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(All.Count);
        foreach (PlayerCommand c in All) c.HashInto(ref hash);
    }
}
