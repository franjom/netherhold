// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

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
