// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>
/// The codec will decode bytes from other players (ADR-0724 R1). Whatever the bytes, a decode
/// returns a command or throws <see cref="InvalidDataException"/> (malformed) or
/// <see cref="EndOfStreamException"/> (cut short, which the journal reader treats as its end),
/// and it never allocates far beyond what the input can hold.
/// </summary>
public class CommandCodecHostileInputTests
{
    /// <summary>Generous: the largest legitimate decode is a sell wave at the codec's tile limit.</summary>
    private const long MaxBytesPerDecode = 32L * 1024 * 1024;

    [Theory]
    [InlineData(-1)]
    [InlineData(CommandCodec.MaxTiles + 1)]
    [InlineData(int.MaxValue)]
    public void ATileCountOutsideTheLimit_IsRefused(int count)
    {
        byte[] input = Bytes(w => { Head(w, tag: 1); w.Write(count); w.Write(5); });

        Assert.Throws<InvalidDataException>(() => Decode(input));
    }

    [Fact]
    public void ATileCountPastTheEnd_EndsTheStream_WithoutAllocatingIt()
    {
        byte[] input = Bytes(w => { Head(w, tag: 1); w.Write(CommandCodec.MaxTiles); w.Write(5); });
        long before = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<EndOfStreamException>(() => Decode(input));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 64 * 1024);
    }

    [Theory]
    [InlineData(0, 0, 4_000, 4_000)]
    [InlineData(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue)]
    [InlineData(0, int.MinValue, 0, int.MaxValue)]
    public void AnOversizedSellWave_IsRefused(int x0, int y0, int x1, int y1)
    {
        byte[] input = Bytes(w => { Head(w, tag: 34); w.Write(x0); w.Write(y0); w.Write(x1); w.Write(y1); });

        Assert.Throws<InvalidDataException>(() => Decode(input));
    }

    [Fact]
    public void RandomBytes_DecodeOrFailInAControlledWay()
    {
        var random = new Random(724);
        for (int i = 0; i < 20_000; i++)
        {
            var input = new byte[random.Next(0, 64)];
            random.NextBytes(input);
            // Most random tags are unknown; aim half the cases at real ones.
            if (input.Length >= 2 && i % 2 == 0) input[0] = (byte)random.Next(0, 40);
            if (input.Length >= 2) input[1] = 0;
            AssertControlled(input);
        }
    }

    [Fact]
    public void MutatedSamples_DecodeOrFailInAControlledWay()
    {
        var random = new Random(7240);
        byte[][] samples = CommandCodecTests.Samples().Select(Encode).ToArray();
        for (int i = 0; i < 20_000; i++)
        {
            byte[] input = (byte[])samples[i % samples.Length].Clone();
            switch (random.Next(3))
            {
                case 0:
                    for (int f = random.Next(1, 4); f > 0; f--)
                        input[random.Next(input.Length)] ^= (byte)(1 << random.Next(8));
                    break;
                case 1:
                    input = input[..random.Next(input.Length)];
                    break;
                default:
                    int at = random.Next(input.Length);
                    input[at] = (byte)(random.Next(2) == 0 ? 0xFF : 0x80);
                    break;
            }
            AssertControlled(input);
        }
    }

    private static void AssertControlled(byte[] input)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            Decode(input);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
        }
        catch (Exception e)
        {
            Assert.Fail($"{e.GetType().Name} decoding {Convert.ToHexString(input)}: {e.Message}");
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < MaxBytesPerDecode,
            $"decoding {Convert.ToHexString(input)} allocated {allocated} bytes");
    }

    private static PlayerCommand Decode(byte[] input) =>
        CommandCodec.Read(new BinaryReader(new MemoryStream(input)));

    private static byte[] Encode(PlayerCommand command) => Bytes(w => CommandCodec.Write(w, command));

    private static void Head(BinaryWriter w, ushort tag)
    {
        w.Write(tag);
        w.Write((byte)1);
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream)) write(w);
        return stream.ToArray();
    }
}
