// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections;
using System.Reflection;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Tests;

/// <summary>
/// The journal's command codec (ADR-0552 Stage B). Every command type must have a sample here,
/// every property of a type must be non-default in at least one of its samples, and every sample
/// must come back equal property by property and by <c>HashInto</c>. Comparing hashes alone would
/// miss a field <c>HashInto</c> forgot; comparing records with <c>==</c> fails on their lists.
/// </summary>
public class CommandCodecTests
{
    private static readonly PlayerId P = new(3);
    private static readonly TilePos T = new(5, 7);
    private static readonly FpPosition2 F2 = new(new Fp32(20_481), new Fp32(-4_097));
    private static readonly FpPosition3 F3 = new(new Fp32(12_345), new Fp32(23_456), new Fp32(-345));
    private static readonly PossessionAim Aim = new(F3, new Dk2Angle(1_234), new Dk2Angle(77));

    public static IEnumerable<PlayerCommand> Samples() =>
    [
        new TagTilesCommand(P, [T, new TilePos(6, 7)], true),
        new BuildRoomCommand(P, RoomTypeId.Treasury, [T, new TilePos(1, 2)])
            { Mode = RoomBuildMode.PlayerWave, Press = new TilePos(9, 9), Release = new TilePos(8, 8) },
        new SellRoomCommand(P, [T]),
        new CreateImpCommand(P, F2),
        new CreateImpCommand(P, T),
        new CreateImpCommand(P),
        new PickUpCreatureCommand(P, 41),
        new PickUpObjectCommand(P, 42),
        new PickUpStoreGoldCommand(P, T),
        new DropCommand(P, T, 43, F2),
        new DropCommand(P, T),
        new SlapCreatureCommand(P, 44),
        new MintWageCommand(P, 45),
        new CastSpellAtPlaceCommand(P, 9, 1_000, 2_000, 0x0102),
        new CastSpellOnCreatureCommand(P, 10, 46, 0x0201),
        new RemoveCallToArmsCommand(P),
        new PlaceDoorCommand(P, T, 4),
        new PlaceTrapCommand(P, T, 5),
        new LockDoorCommand(P, T),
        new PullCasinoLeverCommand(P, T),
        new GrantCreatureLevelCommand(P, 47),
        new SetAllianceCommand(P, new PlayerId(4), true),
        new SkipCameraPathCommand(P),
        new AcknowledgePlayerMessageCommand(P, 0xDEADBEEF),
        new LeavePossessionCommand(P),
        new ActPossessedCreatureCommand(P, Aim, true, new PossessedSlot(-5)),
        new DismissFollowersCommand(P),
        new ToggleSneakCommand(P, 11, 12),
        new HypnotiseCommand(P, Aim),
        new SetSniperModeCommand(P, true),
        new MovePossessedCreatureCommand(P, new PossessionPose(F3, new Dk2Angle(2_000))),
        new SetRescuePriorityCommand(P, 48, true),
        new ContinueAfterResultCommand(P),
        new RevealMapCommand(P),
        new ActivateSpecialCommand(P, 49, 50),
        new WithdrawCounterGoldCommand(P, new GoldAmount(1_234_567)),
    ];

    [Fact]
    public void EveryCommandType_HasASample()
    {
        var sampled = Samples().Select(c => c.GetType()).ToHashSet();
        Type[] missing = CommandTypes().Where(t => !sampled.Contains(t)).ToArray();

        Assert.True(missing.Length == 0,
            $"no codec sample for {string.Join(", ", missing.Select(t => t.Name))}");
    }

    [Fact]
    public void EveryProperty_IsExercisedBySomeSample()
    {
        // A default value round-trips even when the codec drops the field, so each field must
        // carry a distinctive value somewhere.
        foreach (IGrouping<Type, PlayerCommand> type in Samples().GroupBy(c => c.GetType()))
            foreach (PropertyInfo property in Properties(type.Key))
                Assert.True(type.Any(c => !IsDefault(property.GetValue(c))),
                    $"{type.Key.Name}.{property.Name} is default in every sample");
    }

    [Theory]
    [MemberData(nameof(SampleRows))]
    public void ACommand_RoundTrips(PlayerCommand command)
    {
        PlayerCommand back = RoundTrip(command);

        Assert.Equal(command.GetType(), back.GetType());
        foreach (PropertyInfo property in Properties(command.GetType()))
            Assert.True(SameValue(property.GetValue(command), property.GetValue(back)),
                $"{command.GetType().Name}.{property.Name} changed in the round trip");
        Assert.Equal(HashOf(command), HashOf(back));
    }

    [Fact]
    public void AnUnknownTag_IsRefused()
    {
        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((ushort)60_000);
            w.Write((byte)1);
        }
        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => CommandCodec.Read(new BinaryReader(stream)));
    }

    public static TheoryData<PlayerCommand> SampleRows()
    {
        var rows = new TheoryData<PlayerCommand>();
        foreach (PlayerCommand c in Samples()) rows.Add(c);
        return rows;
    }

    internal static IEnumerable<Type> CommandTypes() =>
        typeof(PlayerCommand).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(PlayerCommand)) && !t.IsAbstract);

    private static PlayerCommand RoundTrip(PlayerCommand command)
    {
        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            CommandCodec.Write(w, command);
        stream.Position = 0;
        PlayerCommand back = CommandCodec.Read(new BinaryReader(stream));
        Assert.Equal(stream.Length, stream.Position);
        return back;
    }

    private static IEnumerable<PropertyInfo> Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract" && p.GetIndexParameters().Length == 0);

    private static bool IsDefault(object? value) => value switch
    {
        null => true,
        IEnumerable list and not string => !list.Cast<object>().Any(),
        _ => value.GetType().IsValueType && value.Equals(Activator.CreateInstance(value.GetType())),
    };

    private static bool SameValue(object? a, object? b) =>
        a is IEnumerable left and not string && b is IEnumerable right
            ? left.Cast<object>().SequenceEqual(right.Cast<object>())
            : Equals(a, b);

    private static ulong HashOf(PlayerCommand command)
    {
        var hash = Hash64.Start();
        command.HashInto(ref hash);
        return hash.Value;
    }
}
