// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

public class SaveLoadingBoundsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(55)]
    [InlineData(80)]
    [InlineData(1000)]
    public void TruncationHasOneSaveException(int length)
    {
        byte[] bytes = Snapshot();
        var error = Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(bytes[..length], TestWorld.Database()));
        Assert.Equal(SaveIncompatibleReason.Truncated, error.Reason);
        Assert.NotNull(error.Position);
    }

    [Theory]
    [InlineData(-1, SaveIncompatibleReason.OutOfRange)]
    [InlineData(int.MaxValue, SaveIncompatibleReason.OutOfRange)]
    [InlineData(256, SaveIncompatibleReason.Truncated)]
    public void ActivePlayerCountsAreBoundedBeforeAllocation(int count, SaveIncompatibleReason reason)
    {
        byte[] bytes = Snapshot();
        BitConverter.GetBytes(count).CopyTo(bytes, 59);
        // Leave only the header when testing the remaining-bytes bound.
        if (count == 256) bytes = bytes[..63];
        var error = Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(bytes, TestWorld.Database()));
        Assert.Equal(reason, error.Reason);
    }

    [Theory]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(65536, 65536)]
    [InlineData(-1, 12)]
    [InlineData(12, 0)]
    public void DimensionsAreCheckedBeforeMultiplication(int width, int height)
    {
        byte[] bytes = Snapshot();
        BitConverter.GetBytes(width).CopyTo(bytes, 49);
        BitConverter.GetBytes(height).CopyTo(bytes, 53);
        var error = Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(bytes, TestWorld.Database()));
        Assert.Equal(SaveIncompatibleReason.OutOfRange, error.Reason);
    }

    [Fact]
    public void InflatedTileCountIsBoundedByRemainingBytes()
    {
        byte[] bytes = Snapshot();
        int tileCountOffset = 63 + BitConverter.ToInt32(bytes, 59);
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, tileCountOffset);
        var error = Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(bytes, TestWorld.Database()));
        Assert.Equal(SaveIncompatibleReason.Truncated, error.Reason);
    }

    [Fact]
    public void OversizedPartyStringIsRefusedBeforeAllocation()
    {
        byte[] bytes = PartySnapshot(out int prefix);
        new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 7 }.CopyTo(bytes, prefix);
        var error = Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(bytes, TestWorld.Database()));
        Assert.Equal(SaveIncompatibleReason.Truncated, error.Reason);
    }

    [Fact]
    public void InvalidStringPrefixHasAReason()
    {
        byte[] bytes = PartySnapshot(out int prefix);
        bytes.AsSpan(prefix, 5).Fill(0xFF);
        var error = Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(bytes, TestWorld.Database()));
        Assert.Equal(SaveIncompatibleReason.OutOfRange, error.Reason);
    }

    [Fact]
    public void InvalidPartyUtf8KeepsDecoderErrorAndPosition()
    {
        byte[] bytes = PartySnapshot(out int prefix);
        bytes[prefix + 1] = 0xFF;
        var error = Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(bytes, TestWorld.Database()));
        Assert.Equal(SaveIncompatibleReason.Corrupt, error.Reason);
        Assert.IsType<DecoderFallbackException>(error.InnerException);
        Assert.True(error.Position > prefix);
    }

    [Fact]
    public void SampledCorruptionAndTruncationNeverEscapeTheSaveBoundary()
    {
        var simulation = TestWorld.Simulation();
        simulation.Run(50);
        simulation.Navigation.Refresh();
        ThingProjection.Place(simulation.World, simulation.Database, [new HeroPartyThing(new byte[555]) { Name = "fuzz party", Members = [] }]);
        byte[] original = WorldSnapshot.ToBytes(simulation);
        var database = TestWorld.Database();
        // Header bytes plus evenly spaced samples include the populated mesh suffix.
        var offsets = Enumerable.Range(0, 100)
            .Concat(Enumerable.Range(0, 512).Select(i => (int)((long)i * (original.Length - 1) / 511))).Distinct();
        foreach (int offset in offsets)
        foreach (byte value in new byte[] { 0xFF, 0x7F, 0x80 })
        {
            byte[] bytes = (byte[])original.Clone();
            bytes[offset] = value;
            Exception? error = Record.Exception(() => WorldSnapshot.FromBytes(bytes, database));
            Assert.True(error is null or SaveIncompatibleException,
                $"Offset {offset}, value {value:X2}: {error}");
        }
        foreach (int length in Enumerable.Range(0, 128).Select(i => (int)((long)i * (original.Length - 1) / 127)))
        {
            var error = Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(original[..length], database));
            Assert.Equal(SaveIncompatibleReason.Truncated, error.Reason);
        }
    }

    [Fact]
    public void HandCountCannotExceedTheDatabaseCapacity()
    {
        var simulation = TestWorld.Simulation();
        var database = simulation.Database;
        database.Variables.Misc[VariableTypeId.MaxNumberOfThingsInHand] =
            new MiscVariable(VariableTypeId.MaxNumberOfThingsInHand, 0, 0, 0);
        simulation.World.Player(1).Hand.Add(new HandItem(HandItemKind.Creature, simulation.World.Creatures[0].Id));
        byte[] bytes = WorldSnapshot.ToBytes(simulation);
        var error = Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(bytes, database));
        Assert.Equal(SaveIncompatibleReason.OutOfRange, error.Reason);
    }

    [Fact]
    public void DuplicateSecretDoorKeysAreWrappedWithPosition()
    {
        byte[] bytes = Snapshot();
        int tileCountOffset = 63 + BitConverter.ToInt32(bytes, 59);
        int countOffset = tileCountOffset + 4 + BitConverter.ToInt32(bytes, tileCountOffset) * 109 + 32;
        Assert.Equal(0, BitConverter.ToInt32(bytes, countOffset));
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(bytes, 0, countOffset);
        writer.Write(2);
        for (int i = 0; i < 2; i++)
        {
            writer.Write((byte)1); writer.Write(2); writer.Write(2); writer.Write(1);
            for (int side = 0; side < 4; side++) writer.Write(0);
        }
        writer.Write(bytes, countOffset + 4, bytes.Length - countOffset - 4);
        var error = Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(stream.ToArray(), TestWorld.Database()));
        Assert.Equal(SaveIncompatibleReason.Corrupt, error.Reason);
        Assert.IsType<ArgumentException>(error.InnerException);
        Assert.Equal(countOffset + 4 + 58, error.Position);
    }

    [Theory]
    [InlineData(-1, SaveIncompatibleReason.OutOfRange)]
    [InlineData(int.MaxValue, SaveIncompatibleReason.Truncated)]
    public void NestedMeshPointCountsAreBounded(int count, SaveIncompatibleReason reason)
    {
        var simulation = TestWorld.Simulation();
        simulation.Navigation.Refresh();
        byte[] bytes = WorldSnapshot.ToBytes(simulation);
        using var navStream = new MemoryStream();
        simulation.Navigation.Write(new BinaryWriter(navStream));
        int navOffset = bytes.Length - (int)navStream.Length;
        int centresOffset = navOffset + 1 + 4 + simulation.World.TileCount + 4 + simulation.World.TileCount;
        int pointsOffset = centresOffset + 4 + BitConverter.ToInt32(bytes, centresOffset) * 4 + 16;
        BitConverter.GetBytes(count).CopyTo(bytes, pointsOffset);
        var error = Assert.Throws<SaveIncompatibleException>(() => WorldSnapshot.FromBytes(bytes, TestWorld.Database()));
        Assert.Equal(reason, error.Reason);
    }

    private static byte[] Snapshot() => WorldSnapshot.ToBytes(TestWorld.Simulation());

    private static byte[] PartySnapshot(out int prefix)
    {
        var simulation = TestWorld.Simulation();
        const string name = "unique party marker";
        ThingProjection.Place(simulation.World, simulation.Database, [new HeroPartyThing(new byte[555]) { Name = name, Members = [] }]);
        byte[] bytes = WorldSnapshot.ToBytes(simulation);
        prefix = bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(name)) - 1;
        Assert.True(prefix >= 0);
        return bytes;
    }
}
