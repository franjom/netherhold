// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The shipped records the Boulder reads (boulder.md), which the simulation fixture
/// transcribes: this project holds no reference to the fixture, so the file gets to contradict it.</summary>
public class BoulderOnRealDataTests
{
    [InstallFact]
    public void TheBouldersTrapShotObjectAndVariablesAreTheOnesTheFixtureTranscribes()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);

        TrapDef trap = db.Traps[2];
        Assert.Equal(("Boulder Trap", (byte)1, 3, 0, 0x31418u, (ushort)8000, (byte)26, 1),
            (trap.Name, trap.TriggerType, trap.RangeTiles, trap.ManaCostToFire, trap.Flags, trap.Health, trap.ShotTypeId, trap.ShotData1));
        Assert.Equal(4u, SimulationTiming.LoaderFixedSecondsToTicks(trap.RechargeSecondsRaw));

        ShotDef shot = db.Shots[26];
        Assert.Equal((2048, (ushort)1, 0x24u, 0x40u, (byte)3, (byte)22, (byte)0),
            (shot.Speed, shot.Health, shot.Flags, shot.ShotProcessFlags, shot.CollideType, shot.ProcessType, shot.DamageType));

        ObjectDef boulder = db.ObjectById(BoulderRules.ObjectId)!;
        Assert.Equal(((ObjectFlags)0x238200u, (ushort)4000, 4096, BoulderRules.ProcessState),
            (boulder.Flags, boulder.Hp, boulder.Width.Raw, boulder.StartState));
        ObjectDef heart = db.ObjectById(13)!;
        Assert.Equal(((ObjectFlags)0x234000u, (ushort)34464, 4096), (heart.Flags, heart.Hp, heart.Width.Raw));

        Assert.Equal([4000, 4915, 50, 50, 701, 4],
            new[] { VariableTypeId.BoulderInitialHealth, VariableTypeId.BoulderSpeedTilesPerSecond, VariableTypeId.BoulderToCreatureDamagePercentage,
                VariableTypeId.BoulderSpeedToHealthRatio, VariableTypeId.BoulderFearAmount, VariableTypeId.BoulderFearRange }.Select(v => db[v]));
    }
}
