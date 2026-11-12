// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The Mentor's countdown in a timed level: the time-limit block of <c>0x004B9740</c>, which
/// <c>Player_Tick</c> runs for every player each turn after the trigger walk. The gates, the
/// eighteen marks and the single shared speech queue are in that function's record and ADR-0767.
/// The marks multiply the level's tick rate (variable 19), 4 in every shipped level.
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/functions/004B9740.md")]
public static class TimeLimitCountdown
{
    private static readonly int[] MinuteMarksSeconds = [1800, 1200, 600, 300, 240, 180, 120, 60];

    /// <summary>The countdown line for <paramref name="timeLeftTicks"/>, as the Mentor-bank sound
    /// event id (1 to 18) and whether it clears the queue first; null when no line is due.</summary>
    public static (int SoundId, bool ClearsQueue)? LineFor(int timeLeftTicks)
    {
        if (timeLeftTicks == 0) return null;
        for (int i = 0; i < MinuteMarksSeconds.Length; i++)
            if (timeLeftTicks == MinuteMarksSeconds[i] * SimulationTiming.TicksPerSecond)
                return (i + 1, false);
        for (int seconds = 10; seconds >= 1; seconds--)
            if (timeLeftTicks == seconds * SimulationTiming.TicksPerSecond)
                return (19 - seconds, true);
        return null;
    }

    /// <summary>One turn's ask. Every player still playing enqueues the line into DK2's one speech
    /// queue, which keeps a single copy, so ours asks once, for every viewer, when any player in the
    /// world is still playing; there is no local-player test.</summary>
    internal static void Announce(WorldState world, TriggerState triggers, Action<ScriptedViewRequest>? requested)
    {
        if (requested is null || !triggers.TimerSpeechEnabled || world.GameMode == GameMode.MyPetDungeon) return;
        if (LineFor(triggers.LevelTimeLimit) is not { } line) return;
        foreach (PlayerId player in world.ActivePlayerOrder)
        {
            if (!world.HasPlayer(player) || world.Player(player).Status != 0) continue;
            requested(new ScriptedViewRequest(ScriptedViewKind.MentorSpeech, PlayerIds.Unowned, line.SoundId, default, line.ClearsQueue));
            return;
        }
    }
}
