// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Pathfinding;

namespace DK2.Simulation.Tests;

public class NativeSearchNeighbourOrderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EqualHeapKeysTakeTheNativeLeftHandCorridor(bool seededAtDestination)
    {
        // 004CD000/004CD490 visit neighbour words +6,+8,+A in that order.
        // Executing both native functions on this synthetic fan returns bottom,left,top.
        var mesh = NavMesh.Build(new FloorGrid());
        mesh.InsertPoint(2, 2);
        int top = mesh.Locate(2 * 256, 128);
        int bottom = mesh.Locate(2 * 256, 3 * 256);
        int left = mesh.Locate(128, 2 * 256);
        var corridor = new List<int>();

        Assert.True(new MeshRouter(mesh).TrySearch(top, bottom, new NavPoint(2 * 256, 3 * 256),
            MoverProfile.Walker, corridor, seededAtDestination));
        Assert.Equal([bottom, left, top], corridor);
    }

    private sealed class FloorGrid : INavByteGrid
    {
        public int Width => 4;
        public int Height => 4;
        public byte NavByte(int x, int y) => NavBytes.Floor;
    }
}
