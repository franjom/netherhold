// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

public sealed class CreatureSoundCategoryTests
{
    [Fact]
    public void SoundBankReadsAndWritesItsAuthoredField()
    {
        byte[] source = new byte[5449];
        Encoding.ASCII.GetBytes("CREATURE_GOBLIN").CopyTo(source, 0xF56);
        var chunk = new KwdChunk { Type = KwdRecordType.Creatures, ItemCount = 1, Content = source, HeaderBlock = [] };
        var creature = Assert.Single(KwdCreatures.Parse([chunk]));
        Assert.Equal("CREATURE_GOBLIN", creature.SoundCategory);
        var template = Assert.Single(KwdRecordRoundTrip.Split(chunk));
        byte[] edited = KwdCreatures.WriteRecord(creature with { SoundCategory = "CREATURE_TROLL" }, template);
        Assert.Equal("CREATURE_TROLL", Assert.Single(KwdCreatures.Parse([chunk with { Content = edited }])).SoundCategory);
    }
}
