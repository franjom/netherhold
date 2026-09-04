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
