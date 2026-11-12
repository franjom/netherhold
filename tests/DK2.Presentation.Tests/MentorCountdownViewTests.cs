// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>The time-limit countdown reaches every viewer as a Mentor-bank line, never as a level
/// line, and with no subtitle (ADR-0767).</summary>
public sealed class MentorCountdownViewTests
{
    [Fact]
    public void ACountdownLineReachesAnyViewerAsAMentorLine()
    {
        var director = new ScriptedViewDirector(PlayerIds.FirstKeeper + 1, _ => null, _ => "level line");
        var mentor = new List<(int Id, bool Clear)>();
        int levelLines = 0;
        director.MentorSpeechRequested += (id, clear) => mentor.Add((id, clear));
        director.SpeechRequested += (_, _) => levelLines++;

        director.Emit(new SimEvent(default, "script", "countdown", "")
        {
            ScriptedView = new ScriptedViewRequest(ScriptedViewKind.MentorSpeech, PlayerIds.Unowned, 9, default, true),
        });
        director.Update(1000, 0);

        Assert.Equal([(9, true)], mentor);
        Assert.Equal(0, levelLines);
        Assert.Null(director.Subtitle);
    }
}
