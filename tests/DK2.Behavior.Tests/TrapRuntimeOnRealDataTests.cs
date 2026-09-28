// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class TrapRuntimeOnRealDataTests
{
    [InstallFact]
    public void ShippedTrapActivationFieldsMatchTheBinaryMappedColumns()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        TrapDef[] traps = db.Traps.OrderBy(p => p.Key).Select(p => p.Value).ToArray();
        Assert.Equal(16, traps.Length);
        Assert.Equal(new byte[] { 2, 1, 1, 1, 1, 1, 2, 1, 3, 2, 2, 2, 2, 1, 1, 2 }, traps.Select(t => t.TriggerType));
        Assert.Equal(new uint[] { 16, 4, 24, 4, 48, 48, 24, 24, 16, 16, 4, 32, 30, 24, 4, 4 },
            traps.Select(t => SimulationTiming.LoaderFixedSecondsToTicks(t.RechargeSecondsRaw)));
        Assert.Equal(new int[] { 1, 3, 5, 4, 1, 4, 1, 3, 1, 1, 6, 4, 9, 5, 4, 1 }, traps.Select(t => t.RangeTiles));
        Assert.Equal(new int[] { 0, 0, 250, 0, 250, 375, 250, 125, 0, 0, 0, 500, 0, 100, 50, 0 },
            traps.Select(t => t.ManaCostToFire));
        Assert.Equal(new uint[] { 0x3053A, 0x31418, 0x31598, 0x3153A, 0x3053A, 0x31518, 0x3053A, 0x31518,
            0x2043A, 0x53A, 0xC18, 0x35BA, 0x243A, 0x9598, 0x3973A, 0x21408 }, traps.Select(t => t.Flags));
        Assert.All(traps.Where(t => t.TrapId != 13), t => Assert.Equal(0, t.ChargeSecondsRaw));
        Assert.Equal(2048, db.Traps[13].ChargeSecondsRaw);
    }

    [InstallFact]
    public void ShippedShotPayloadOffsetsThreatAndRecoveryMatchMappedColumns()
    {
        TrapDef[] traps = GameDatabaseLoader.Load(Dk2InstallGate.Path).Traps.OrderBy(p => p.Key).Select(p => p.Value).ToArray();
        Assert.Equal(new int[] { 750, 1, 250, 0, 2000, 400, 20, 0, 0, 750, 0, 150, 0, 250, 0, 10000 },
            traps.Select(t => t.ShotData1));
        Assert.Equal(new int[] { 1, 0, 0, 5, 1, 0, 1, 3, 1, 1, 0, 0, 0, 0, 5, 2 }, traps.Select(t => t.ShotData2));
        Assert.All(traps, t => Assert.Equal(0, t.ShotOffsetX));
        Assert.Equal(new int[] { 0, 0, -2048, 0, -1761, -2048, 0, 0, 0, 0, -2457, 0, 0, -2048, 0, 0 },
            traps.Select(t => t.ShotOffsetY));
        Assert.Equal(new int[] { 0, 0, 2170, 3481, 1433, 2662, 0, 2048, 0, 0, 1638, 0, 0, 2170, 3481, 0 },
            traps.Select(t => t.ShotOffsetZ));
        Assert.Equal(new ushort[] { 74, 0, 74, 0, 185, 74, 37, 600, 0, 74, 0, 100, 0, 74, 0, 0 },
            traps.Select(t => t.Threat));
        Assert.Equal(new int[] { 45, 0, 45, 0, 45, 45, 45, 60, 0, 45, 0, 60, 60, 45, 0, 0 },
            traps.Select(t => t.ThreatSecondsRaw / 4096));
        Assert.Equal(new short[] { 6, 16, 6, 4, 16, 12, 12, 12, 4, 8, 4, 40, 0, 30, 4, 16 }, traps.Select(t => t.HealthGain));
    }

    [InstallFact]
    public void ShippedDoorFlagsAndRecoveryMatchMappedColumns()
    {
        DoorDef[] doors = GameDatabaseLoader.Load(Dk2InstallGate.Path).Doors.OrderBy(p => p.Key).Select(p => p.Value).ToArray();
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0x20, 0x40, 0xC0, 0x80 }, doors.Select(d => (byte)d.Flags));
        Assert.Equal(new short[] { 2, 5, 3, 10, 10, 1, 16, 17 }, doors.Select(d => d.HealthGain));
    }
}
