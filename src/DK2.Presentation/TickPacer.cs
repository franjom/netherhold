// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation;

/// <summary>How many simulation ticks a rendered frame runs, shared by every engine adapter: real time accrues
/// <see cref="SimulationTiming.TicksPerSecond"/> ticks a second, scaled by the run's speed and the game-speed setting
/// (4 is normal), and each frame takes whole ticks only, up to a cap, carrying the remainder to the next frame.</summary>
public sealed class TickPacer
{
    /// <summary>The whole ticks one frame may run, so a stall is paid back at this rate rather than fast-forwarding the
    /// game in one frame.</summary>
    public const int DefaultMaxTicksPerFrame = 2;

    /// <summary>The most real time one frame may accrue, 1/3 s: Unity's maximum timestep in this project
    /// (<c>ProjectSettings/TimeManager.asset</c>, which must change with it), applied by the host before <see cref="Accrue"/>.</summary>
    public const double MaximumFrameSeconds = 1.0 / 3.0;

    /// <summary>Ticks owed and not yet run; below 1 after <see cref="Take"/> unless the cap held some back.</summary>
    public double Debt { get; private set; }

    /// <summary>Adds a frame's real time and returns the ticks it added.</summary>
    public double Accrue(double elapsedSeconds, float speed, int gameSpeed)
    {
        double advance = elapsedSeconds * SimulationTiming.TicksPerSecond * speed * (gameSpeed / 4.0);
        Debt += advance;
        return advance;
    }

    /// <summary>The frame's whole ticks, at most <paramref name="maxTicksPerFrame"/> (at least one); the rest stays owed. The
    /// host clamps each frame's elapsed time too, or a long stall is paid back at the cap.</summary>
    public int Take(int maxTicksPerFrame)
    {
        int ticks = (int)Math.Min(Debt, Math.Max(1, maxTicksPerFrame));
        Debt -= ticks;
        return ticks;
    }

    /// <summary>The input of the frame's first tick: every pending command when the frame runs a tick, else
    /// <see cref="InputFrame.Empty"/>. The caller clears <paramref name="pending"/> when the result carries commands.</summary>
    public static InputFrame FirstInput(int ticks, IReadOnlyList<PlayerCommand> pending)
    {
        if (pending is null)
        {
            throw new ArgumentNullException(nameof(pending));
        }

        if (ticks <= 0 || pending.Count == 0)
        {
            return InputFrame.Empty;
        }

        var commands = new PlayerCommand[pending.Count];
        for (int i = 0; i < commands.Length; i++)
        {
            commands[i] = pending[i];
        }

        return new InputFrame(commands);
    }

    /// <summary>Tick <paramref name="index"/> of a frame: the first carries the frame's commands, the rest none.</summary>
    public static InputFrame InputFor(int index, in InputFrame first)
    {
        return index == 0 ? first : InputFrame.Empty;
    }
}
