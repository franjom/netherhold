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

using DK2.Core;

namespace DK2.Core.Tests;

public class TilePosTests
{
    [Fact]
    public void ChebyshevDistance_CountsDiagonalAsOne()
    {
        Assert.Equal(1, new TilePos(0, 0).ChebyshevDistance(new TilePos(1, 1)));
        Assert.Equal(5, new TilePos(2, 3).ChebyshevDistance(new TilePos(7, 1)));
    }

    [Fact]
    public void ManhattanDistance_SumsAxes()
    {
        Assert.Equal(2, new TilePos(0, 0).ManhattanDistance(new TilePos(1, 1)));
    }
}
