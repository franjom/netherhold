// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The shipped records the Jack In The Box reads (jack-in-the-box.md), which the simulation
/// fixture and <c>JackInTheBoxTests</c> transcribe.</summary>
public class JackInTheBoxOnRealDataTests
{
    [InstallFact]
    public void TheTrapItsTwoShotsAndTheJackAreTheOnesTheTestsTranscribe()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);

        TrapDef trap = db.Traps[16];
        Assert.Equal((TrapRules.TileSelector, 1, 0, 0x21408u, (ushort)8000, (byte)64, 10000, 2),
            (trap.TriggerType, trap.RangeTiles, trap.ManaCostToFire, trap.Flags, trap.Health, trap.ShotTypeId, trap.ShotData1, trap.ShotData2));
        Assert.Equal(4u, SimulationTiming.LoaderFixedSecondsToTicks(trap.RechargeSecondsRaw));

        ShotDef spring = db.Shots[64];
        Assert.Equal(((ushort)1, 0x24u, 0x40u, (byte)2, (byte)39, (byte)2),
            (spring.Health, spring.Flags, spring.ShotProcessFlags, spring.CollideType, spring.ProcessType, spring.DamageType));
        ShotDef blast = db.Shots[JackInTheBoxRules.ExplosionShotId];
        Assert.Equal(((ushort)5, 0x104u, (byte)2, (byte)0, (byte)2, 20000u, 3u),
            (blast.Health, blast.Flags, blast.CollideType, blast.ProcessType, blast.DamageType, blast.Unknown_0098, blast.Unknown_009C));

        CreatureDef jack = db.CreatureById(JackInTheBoxRules.JackCreatureId);
        Assert.Equal(("Jack", (ushort)240, JobClass.Artisan, false),
            (jack.Name, jack.Hp, jack.JobClass, jack.Flags.HasFlag(CreatureFlags.GenerateDeadBody)));
    }
}
