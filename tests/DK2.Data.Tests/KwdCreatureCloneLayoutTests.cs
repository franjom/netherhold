// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdCreatureCloneLayoutTests
{
    [Fact]
    public void CloneIdReadsTheByteFollowingShuffleSpeed()
    {
        byte[] source = new byte[5449];
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(0xD37), 0x0012ABCD);
        source[0xD3B] = 13;
        CreatureDef creature = Read(source);
        Assert.Equal(0x0012ABCD, creature.ShuffleSpeed.Raw);
        Assert.Equal((byte)13, creature.CloneCreatureId);
        Assert.Equal(creature.UniqueBaseCreatureId, creature.CloneCreatureId);
    }

    [Fact]
    public void EditingEitherNameForTheBaseCreatureByteKeepsBothViewsConsistent()
    {
        CreatureDef creature = Read(new byte[5449]);
        CreatureDef cloneEdit = creature with { CloneCreatureId = 13 };
        Assert.Equal((byte)13, cloneEdit.UniqueBaseCreatureId);
        CreatureDef baseEdit = creature with { UniqueBaseCreatureId = 7 };
        Assert.Equal((byte)7, baseEdit.CloneCreatureId);
    }

    [InstallFact]
    public void EveryShippedCloneProjectionMatchesItsSourceByteAndBaseCreatureView()
    {
        KwdChunk chunk = KwdReader.ReadFile(Path.Combine(Dk2InstallGate.Path, "Data", "editor", "Creatures.kwd")).Single();
        var creatures = KwdCreatures.Parse([chunk]);
        var images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(46, creatures.Count);
        for (int i = 0; i < creatures.Count; i++)
        {
            Assert.Equal(images[i].ToBytes()[0xD3B], creatures[i].CloneCreatureId);
            Assert.Equal(creatures[i].UniqueBaseCreatureId, creatures[i].CloneCreatureId);
        }
    }

    private static CreatureDef Read(byte[] source) => KwdCreatures.Parse([
        new KwdChunk { Type = KwdRecordType.Creatures, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
