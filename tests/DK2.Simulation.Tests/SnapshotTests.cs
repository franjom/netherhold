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
    public void TutorialPresentationStateChangesTheHashAndSurvivesRestore()
    {
        GameSimulation original = TestWorld.Simulation();
        ulong before = original.StateHash();
        original.World.Creatures[0].StatusFlowerUntilTurn = 123;
        Assert.NotEqual(before, original.StateHash());
        ulong flowerOnly = original.StateHash();
        PlayerId player = original.World.Creatures[0].OwnerPlayerId;
        original.World.Player(player).IntroducedCreatureTypes.Add(7);
        Assert.NotEqual(flowerOnly, original.StateHash());

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(original), TestWorld.Database());
        Assert.Equal(original.StateHash(), restored.StateHash());
        Assert.Equal(123u, restored.World.Creatures[0].StatusFlowerUntilTurn);
        Assert.Equal([7], restored.World.Player(player).IntroducedCreatureTypes);
    }

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
    public void RoundTrip_CarriesTheStateIdAndUtilityPair_FromTheConstructorsValues()
    {
        // The constructor writes the state 1 and both slots 0 (travel-states.md 2.4); only a
        // corpse holds no state (ADR-0206, ADR-0209).
        GameSimulation original = TestWorld.Simulation();
        Assert.NotEmpty(original.World.Creatures);   // Q206: Assert.All passes on an empty population
        Assert.All(original.World.Creatures, c => Assert.Equal(CreatureStateMachine.Idle, c.Dk2StateId));
        Assert.All(original.World.Creatures, c => Assert.Equal(0, c.StateBeforeUtilityState));
        Assert.All(original.World.Creatures, c => Assert.Equal(0, c.UtilityNewState));

        // 91 is "find a hatchery, move to it, resume in 92" (eating.md 2.2): the shape the
        // travelling activities retire into - one en route state, the errand in the slot.
        original.World.Creatures[0].Dk2StateId = 21;   // reinforcing walls
        original.World.Creatures[0].StateBeforeUtilityState = 91;
        original.World.Creatures[0].UtilityNewState = 92;
        byte[] bytes = WorldSnapshot.ToBytes(original);
        GameSimulation restored = WorldSnapshot.FromBytes(bytes, TestWorld.Database(), [new ChaosSystem()]);

        Assert.Equal(21, restored.World.Creatures[0].Dk2StateId);
        Assert.Equal(91, restored.World.Creatures[0].StateBeforeUtilityState);
        Assert.Equal(92, restored.World.Creatures[0].UtilityNewState);
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
        original.World.Triggers.LevelTimeLimit = 777;
        original.World.Triggers.SpecialInfoNextRoomTypeId = 5;
        original.World.Triggers.SpecialInfoPlayerId = 4;
        original.World.Triggers.MaximumSlaps = 9;
        TriggerRecord option = new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 0xFFFF, Type = 45,
            Parameters = new Dictionary<string, long> { ["Available"] = 0 },
        };
        new TriggerExecutor(
            new TriggerTree([option], 0xFFFF), original.World.Triggers).Run(1);

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
        Assert.False(restored.World.Triggers.NotificationsEnabled);
        Assert.Equal(777, restored.World.Triggers.LevelTimeLimit);
        Assert.Equal(5, restored.World.Triggers.SpecialInfoNextRoomTypeId);
        Assert.Equal(4, restored.World.Triggers.SpecialInfoPlayerId);
        Assert.Equal(9, restored.World.Triggers.MaximumSlaps);
    }

    [Fact]
    public void RoundTrip_CarriesMutableAvailability()
    {
        var level = new GlobalVariables();
        level.Availabilities.Add(new AvailabilityEntry(5, 1, 7, 1));
        GameDatabase database = TestWorld.Database().ForLevel(level);
        WorldState world = TestWorld.World(database);
        world.SetAvailability(1, AvailabilityKind.Spell, 7, 3);
        var original = new GameSimulation(database, world, 1);

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(original), database);

        Assert.Equal(3, restored.World.Availability(1, AvailabilityKind.Spell, 7));
        Assert.Equal(original.StateHash(), restored.StateHash());
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
    public void RoundTrip_CarriesDoorsAndTrapsVerbatim()
    {
        GameDatabase database = TestWorld.Database();
        GameSimulation original = TestWorld.Simulation();
        ThingProjection.Place(original.World, database,
        [
            new DoorThing(new byte[20])
            {
                X = -1, Y = 2, Unknown_0008 = 3, TriggerId = 4,
                DoorId = 5, PlayerId = 6, Flags = 0xE7,
                Unknown_0011 = new byte[] { 7, 8, 9 },
            },
            new TrapThing(new byte[16])
            {
                X = -10, Y = 11, Unknown_0008 = 12, NumberOfShots = 13,
                TrapId = 14, PlayerId = 15, Unknown_000F = 16,
            },
        ]);

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(original), database, [new ChaosSystem()]);

        DoorState door = Assert.Single(restored.World.Doors);
        Assert.Equal((-1, 2, 3, (ushort)4, (byte)5, (byte)6, (byte)0xE7),
            (door.X, door.Y, door.Unknown_0008, door.TriggerId,
             door.DoorId, door.PlayerId, door.Flags));
        Assert.Equal(new byte[] { 7, 8, 9 }, door.Unknown_0011.ToArray());
        Assert.True(door.UnderConstruction);

        TrapState trap = Assert.Single(restored.World.Traps);
        Assert.Equal((-10, 11, 12, (byte)13, (byte)14, (byte)15, (byte)16),
            (trap.X, trap.Y, trap.Unknown_0008, trap.NumberOfShots,
             trap.TrapId, trap.PlayerId, trap.Unknown_000F));
        Assert.False(trap.UnderConstruction);
        Assert.Equal(original.StateHash(), restored.StateHash());
    }

    [Fact]
    public void RoundTrip_CarriesHeroPartiesAndEffectGeneratorsVerbatim()
    {
        GameDatabase database = TestWorld.Database();
        GameSimulation original = TestWorld.Simulation();
        ThingProjection.Place(original.World, database,
        [
            new HeroPartyThing(new byte[555])
            {
                Name = "snapshot party", TriggerId = 4, PartyId = 5,
                Unknown_0023 = -6, Unknown_0027 = 7,
                Members =
                [
                    new GoodCreatureThing(new byte[32])
                    {
                        X = 8, Y = 9, Z = 10, GoldHeld = 11, Level = 12, Flags = 13,
                        ObjectiveTargetActionPointId = 14, InitialHealth = 15, TriggerId = 16,
                        ObjectiveTargetPlayerId = 17, Objective = 18, CreatureId = 19,
                        WanderRadius = 20, PartyId = 21, Flags2 = 22,
                    },
                ],
            },
            new EffectGeneratorThing(new byte[36])
            {
                X = 23, Y = 24, Unknown_0008 = 25, Unknown_000C = 26,
                Unknown_0010 = 27, Unknown_0012 = 28, EffectIds = [29, 30, 31],
                Frequency = 32, GeneratorId = 33,
                Unknown_001E = new byte[] { 34, 35, 36, 37, 38, 39 },
            },
        ]);

        GameSimulation restored = WorldSnapshot.FromBytes(
            WorldSnapshot.ToBytes(original), database, [new ChaosSystem()]);

        HeroPartyState party = Assert.Single(restored.World.HeroParties);
        Assert.Equal(("snapshot party", (ushort)4, (byte)5, -6, 7),
            (party.Name, party.TriggerId, party.PartyId, party.Unknown_0023, party.Unknown_0027));
        HeroPartyMemberState member = Assert.Single(party.Members);
        Assert.Equal((8, 9, 10, (ushort)11, (byte)12, (byte)13),
            (member.X, member.Y, member.Z, member.GoldHeld, member.Level, member.Flags));
        Assert.Equal(20, member.WanderRadius); Assert.Equal(21, member.PartyId);

        EffectGeneratorState generator = Assert.Single(restored.World.EffectGenerators);
        Assert.Equal(new ushort[] { 29, 30, 31 }, generator.EffectIds);
        Assert.Equal(new byte[] { 34, 35, 36, 37, 38, 39 }, generator.Unknown_001E.ToArray());
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
        bytes[4] = 250; // format version field, immediately after the 4-byte magic

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
