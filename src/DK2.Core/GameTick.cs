// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

/// <summary>
/// A point in simulation time, measured in fixed integer ticks since level start.
///
/// DK2 v1.70 runs its simulation on a fixed integer game tick
/// (STRONGLY INFERRED: Flame's `limit_tps` patch and tick-stamped fields such as
/// `CRoom.f45_sData_DiscoModeStartedOnGameTick`). The exact ticks-per-second value
/// is UNKNOWN until measured against the original — see docs/research/open-questions.md.
/// </summary>
public readonly record struct GameTick(long Value) : IComparable<GameTick>
{
    public static readonly GameTick Zero = new(0);

    public GameTick Next => new(Value + 1);

    public int CompareTo(GameTick other) => Value.CompareTo(other.Value);

    public static bool operator <(GameTick a, GameTick b) => a.Value < b.Value;
    public static bool operator >(GameTick a, GameTick b) => a.Value > b.Value;
    public static bool operator <=(GameTick a, GameTick b) => a.Value <= b.Value;
    public static bool operator >=(GameTick a, GameTick b) => a.Value >= b.Value;

    public override string ToString() => $"t{Value}";
}
