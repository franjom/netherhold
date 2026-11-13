// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Tools.Tests;

public class SnapshotFieldsTests
{
    [Fact]
    public void NullAndTheLiteralWordNullAreDifferentValues()
    {
        var absent = SnapshotFields.Capture(new TextField(null), "item");
        var literal = SnapshotFields.Capture(new TextField("null"), "item");

        Assert.Null(absent["item.Text"]);
        Assert.Equal("null", literal["item.Text"]);
    }

    [Fact]
    public void DictionaryInsertionOrderAndSetOrderDoNotChangeTheProjection()
    {
        var first = new { Entries = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 }, Set = new HashSet<int> { 3, 1 } };
        var second = new { Entries = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }, Set = new HashSet<int> { 1, 3 } };

        Assert.Equal(SnapshotFields.Capture(first, "item"), SnapshotFields.Capture(second, "item"));
    }

    [Fact]
    public void AComputedGetterIsNeverEvaluated()
    {
        var fields = SnapshotFields.Capture(new ThrowingGetter(), "item");

        Assert.Equal("7", fields["item.Stored"]);
        Assert.DoesNotContain("item.Computed", fields.Keys);
    }

    private sealed record TextField(string? Text);

    private sealed class ThrowingGetter
    {
        public int Stored = 7;
        public int Computed => throw new InvalidOperationException("Must not be evaluated.");
    }
}
