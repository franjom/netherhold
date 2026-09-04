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

public class Hash64Tests
{
    [Fact]
    public void SameInput_ProducesSameHash()
    {
        Assert.Equal(HashOf("dungeon", 42), HashOf("dungeon", 42));
    }

    [Fact]
    public void DifferentInput_ProducesDifferentHash()
    {
        Assert.NotEqual(HashOf("dungeon", 42), HashOf("dungeon", 43));
        Assert.NotEqual(HashOf("dungeon", 42), HashOf("keeper", 42));
    }

    [Fact]
    public void OrderMatters()
    {
        Hash64 a = Hash64.Start();
        a.Add(1);
        a.Add(2);

        Hash64 b = Hash64.Start();
        b.Add(2);
        b.Add(1);

        Assert.NotEqual(a.Value, b.Value);
    }

    [Fact]
    public void KnownVector_IsPinned()
    {
        // FNV-1a over the ASCII bytes of "a". Pinning it proves this is the
        // published algorithm rather than something that merely looks stable
        // in one process. https://en.wikipedia.org/wiki/Fowler-Noll-Vo_hash_function
        Hash64 h = Hash64.Start();
        h.Add((byte)'a');
        Assert.Equal(0xaf63dc4c8601ec8cUL, h.Value);
    }

    [Fact]
    public void EmptyHash_IsTheOffsetBasis()
    {
        Assert.Equal(14695981039346656037UL, Hash64.Start().Value);
    }

    [Fact]
    public void NullString_DiffersFromEmptyString()
    {
        Hash64 a = Hash64.Start();
        a.Add((string?)null);

        Hash64 b = Hash64.Start();
        b.Add(string.Empty);

        Assert.NotEqual(a.Value, b.Value);
    }

    private static ulong HashOf(string s, int n)
    {
        Hash64 h = Hash64.Start();
        h.Add(s);
        h.Add(n);
        return h.Value;
    }
}
