// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

public class SnapshotTests
{
    [Fact]
    public void RoundTrip_PreservesStateHash()
    {
        GameSimulation original = TestWorld.Simulation();
        original.Run(250);

        byte[] bytes = WorldSnapshot.ToBytes(original);
        GameSimulation restored = WorldSnapshot.FromBytes(bytes, TestWorld.Database(), [new ChaosSystem()]);

        Assert.Equal(original.CurrentTick, restored.CurrentTick);
        Assert.Equal(original.StateHash(), restored.StateHash());
    }

    [Fact]
    public void RoundTrip_CarriesTheTriggerBanks()
    {
        // RoundTrip_PreservesStateHash would catch this too, since the banks
        // are hashed - but only by failing with "the hash moved", which says
        // nothing about which of two hundred fields was dropped. This names
        // them, so a snapshot that forgets a bank fails where the bank is.
        GameSimulation original = TestWorld.Simulation();
        original.World.Triggers.Write(0, FlagWriteMode.Assign, 200);
        original.World.Triggers.Write(127, FlagWriteMode.Subtract, 1);
        original.World.Triggers.StartTimer(0, now: 11);
        original.World.Triggers.StartTimer(15, now: -4);
        original.World.Triggers.Write(TriggerState.SpecialFlagId, FlagWriteMode.Assign, 0x7FFFFFFF);

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(original), TestWorld.Database(), [new ChaosSystem()]);

        Assert.Equal(200, restored.World.Triggers.Read(0));

        // 0 - 1 as a signed char, read back zero-extended: the wrap has to
        // survive the round trip as the byte it is, not as -1.
        Assert.Equal(255, restored.World.Triggers.Read(127));

        Assert.Equal(11, restored.World.Triggers.ReadTimer(0));
        Assert.Equal(-4, restored.World.Triggers.ReadTimer(15));
        Assert.Equal(0x7FFFFFFF, restored.World.Triggers.SpecialValue);
        Assert.True(restored.World.Triggers.SpecialValueWritten);
    }

    [Fact]
    public void RoundTrip_CarriesRetiredTriggerRecords()
    {
        GameDatabase database = TestWorld.Database();
        TriggerRecord action = new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 0xFFFF, Type = 7,
            Parameters = new Dictionary<string, long>
            {
                ["FlagId"] = 3, ["Flag"] = 0x12, ["Value"] = 1,
            },
        };
        TriggerRecord condition = new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 1, Type = 1, RepeatCount = 1,
            ChildTriggerId = 0xFFFF,
            Parameters = new Dictionary<string, long>
            {
                ["Comparison"] = 3, ["FlagId"] = 0, ["Flag"] = 1,
                ["IndirectFlagId"] = 0, ["Value"] = 0,
            },
        };
        var tree = new TriggerTree([condition, action], 1);
        var original = new GameSimulation(database, TestWorld.World(database), 1, triggers: tree);
        original.Tick();

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(original), database, triggers: tree);
        original.Tick();
        restored.Tick();

        Assert.Equal(1, restored.World.Triggers.Read(3));
        Assert.Equal(original.StateHash(), restored.StateHash());
    }

    [Fact]
    public void RoundTrip_CarriesActionPointRuntimeState()
    {
        GameDatabase database = TestWorld.Database();
        GameSimulation original = TestWorld.Simulation();
        ThingProjection.Place(original.World, database,
        [
            new ActionPointThing(new byte[56])
            {
                StartX = 2, StartY = 3, EndX = 7, EndY = 8,
                WaitDelay = 5, Flags = 0x7D, TriggerId = 44,
                ActionPointId = 9, NextWaypointId = 10, Name = "snapshot",
            },
        ]);
        original.World.ActionPoints[0].PassageCount = 12;
        original.World.ActionPoints[0].WaitTimerTicks = 17;

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(original), database, [new ChaosSystem()]);

        ActionPointState point = Assert.Single(restored.World.ActionPoints);
        Assert.Equal((byte)9, point.Id);
        Assert.Equal((ushort)2, point.StartX);
        Assert.Equal((ushort)3, point.StartY);
        Assert.Equal((ushort)7, point.EndX);
        Assert.Equal((ushort)8, point.EndY);
        Assert.Equal((ushort)44, point.TriggerId);
        Assert.Equal((byte)10, point.NextWaypointId);
        Assert.Equal(ActionPointState.LoadedFlagMask, point.Flags);
        Assert.Equal((ushort)12, point.PassageCount);
        Assert.Equal((ushort)17, point.WaitTimerTicks);
        Assert.Equal(original.StateHash(), restored.StateHash());
    }

    [Fact]
    public void RestoredRun_ContinuesIdentically()
    {
        // The real invariant. Equal hashes at the moment of restore only prove
        // the visible state matches; continuing to agree for hundreds of ticks
        // proves the snapshot captured everything the simulation depends on —
        // RNG position included.
        GameSimulation original = TestWorld.Simulation();
        original.Run(100);

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(original), TestWorld.Database(), [new ChaosSystem()]);

        for (int t = 0; t < 500; t++)
        {
            original.Tick();
            restored.Tick();
            Assert.True(original.StateHash() == restored.StateHash(),
                $"restored run diverged {t} ticks after loading");
        }
    }

    [Fact]
    public void Snapshot_IsByteIdenticalForIdenticalRuns()
    {
        GameSimulation a = TestWorld.Simulation();
        GameSimulation b = TestWorld.Simulation();
        a.Run(75);
        b.Run(75);

        Assert.Equal(WorldSnapshot.ToBytes(a), WorldSnapshot.ToBytes(b));
    }

    [Fact]
    public void Read_RejectsForeignData()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("not a snapshot at all"));
        Assert.Throws<InvalidDataException>(
            () => WorldSnapshot.Read(stream, TestWorld.Database()));
    }

    [Fact]
    public void Read_RejectsUnsupportedVersion()
    {
        byte[] bytes = WorldSnapshot.ToBytes(TestWorld.Simulation());
        bytes[4] = 99; // format version field, immediately after the 4-byte magic

        Assert.Throws<InvalidDataException>(
            () => WorldSnapshot.FromBytes(bytes, TestWorld.Database()));
    }

    [Fact]
    public void RoundTrip_PreservesWorldContents()
    {
        GameSimulation original = TestWorld.Simulation();
        original.Run(30);
        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(original), TestWorld.Database());

        Assert.Equal(original.World.Width, restored.World.Width);
        Assert.Equal(original.World.Height, restored.World.Height);
        Assert.Equal(original.World.PlayerCount, restored.World.PlayerCount);

        for (int i = 0; i < original.World.TileCount; i++)
        {
            Assert.Equal(original.World.TileAtIndex(i).TerrainId, restored.World.TileAtIndex(i).TerrainId);
            Assert.Equal(original.World.TileAtIndex(i).Health, restored.World.TileAtIndex(i).Health);
        }
    }
}
