// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>ADR-0580: a worker that raised the alarm holds state 252 while action 79 plays, one play of
/// art slot 29 (<c>0x004969D0(0x4F, 1, 0, 0, 0)</c>, combat-alarm.md §3). DK2's Level 4 captures over
/// eight seeds hold an Imp in 252 four ticks in 436 of 444 stand-downs.</summary>
public class AlarmActionOnRealDataTests
{
    [InstallFact]
    public void AnImpsAlarmActionPlaysFourTurns_AsDk2HoldsItsImpsIn252()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        CreatureDef imp = db.Creatures.Values.First(c => c.Name == "Imp");

        Assert.Equal(4, AnimationTimingRules.CountedHoldTicks(imp, 29, 1));
    }
}
