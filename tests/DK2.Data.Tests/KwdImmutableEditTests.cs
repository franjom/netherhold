// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

public class KwdImmutableEditTests
{
    [Fact]
    public void TypedCopyEditsLeaveTheOriginalAndItsRetainedSourceUntouched()
    {
        var chunk = new KwdChunk { Type = KwdRecordType.CreatureSpells, ItemCount = 1,
            Content = new byte[266], HeaderBlock = [] };
        CreatureSpellDef original = KwdAdditionalRecords.ParseCreatureSpells([chunk]).Single();
        CreatureSpellDef changed = original with { Range = 12345 };
        Assert.Equal(0, original.Range); Assert.Equal(12345, changed.Range);
        Assert.Equal(chunk.Content, changed.ToBytes());
        byte[] exported = changed.ToBytes(); exported[0] = 0xDA;
        Assert.Equal(chunk.Content, original.ToBytes());
        Assert.Equal(chunk.Content, changed.ToBytes());
    }

    [Fact]
    public void PolymorphicCopiesPreserveTheConcretePayloadAndSourceImage()
    {
        ThingRecord original = new ObjectThing(new byte[24]) { X = 12, Y = 34, Unknown_0008 = new byte[4] };
        ThingRecord copy = original with { };
        var typed = Assert.IsType<ObjectThing>(copy);
        Assert.NotSame(original, copy);
        Assert.Equal(12, typed.X); Assert.Equal(34, typed.Y);
        Assert.Equal(original.ToBytes(), copy.ToBytes());
        var image = new KwdRecordImage(KwdRecordType.Objects, new byte[894]);
        Assert.Equal(image.ToBytes(), (image with { }).ToBytes());
    }
}
