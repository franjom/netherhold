// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

/// <summary>The 48 footstep mark pairs at CREATURES file +0x1323 (definition +0x7A3, <c>0x00492320</c>, <c>0x00568E30</c>), one pair
/// of unsigned words per art slot. Docs: sound-effects-continuous.md section 1.</summary>
public sealed class CreatureFootstepMarksTests
{
    private const int MarksOffset = 0x1323;

    private static KwdChunk Chunk(byte[] record) =>
        new() { Type = KwdRecordType.Creatures, ItemCount = 1, Content = record, HeaderBlock = [] };

    private static byte[] Record(Action<byte[]>? edit = null)
    {
        var record = new byte[5449];
        edit?.Invoke(record);
        return record;
    }

    [Fact]
    public void AllFortyEightPairsAreReadAsUnsignedWordsFourBytesApart()
    {
        CreatureDef creature = Assert.Single(KwdCreatures.Parse([Chunk(Record(r =>
        {
            r[MarksOffset + (4 * 1)] = 7; r[MarksOffset + (4 * 1) + 2] = 0xFF; r[MarksOffset + (4 * 1) + 3] = 0xFF;
            r[MarksOffset + (4 * 47)] = 0x34; r[MarksOffset + (4 * 47) + 1] = 0x12;
        }))]));
        Assert.Equal(CreatureDef.FootstepMarkCount, creature.FootstepMarks.Count);
        Assert.Equal(new FootstepMarkPair(7, 0xFFFF), creature.FootstepMarksOf(1));
        Assert.Equal(new FootstepMarkPair(0x1234, 0), creature.FootstepMarksOf(47));
        Assert.Equal(default, creature.FootstepMarksOf(2));
        Assert.Equal(default, creature.FootstepMarksOf(48));
    }

    [Fact]
    public void AnEditedPairRoundTripsAndNothingElseMoves()
    {
        byte[] source = Record(r => { r[MarksOffset + 4] = 3; r[MarksOffset + 6] = 9; });
        KwdChunk chunk = Chunk(source);
        CreatureDef creature = Assert.Single(KwdCreatures.Parse([chunk]));
        KwdRecordImage template = Assert.Single(KwdRecordRoundTrip.Split(chunk));
        Assert.Equal(source, KwdCreatures.WriteRecord(creature, template));

        FootstepMarkPair[] marks = [.. creature.FootstepMarks];
        marks[1] = new FootstepMarkPair(11, 22);
        marks[40] = new FootstepMarkPair(0xABCD, 5);
        byte[] edited = KwdCreatures.WriteRecord(creature with { FootstepMarks = marks }, template);
        CreatureDef reread = Assert.Single(KwdCreatures.Parse([chunk with { Content = edited }]));
        Assert.Equal(new FootstepMarkPair(11, 22), reread.FootstepMarksOf(1));
        Assert.Equal(new FootstepMarkPair(0xABCD, 5), reread.FootstepMarksOf(40));
        for (int i = 0; i < edited.Length; i++)
            if (i < MarksOffset || i >= MarksOffset + (4 * CreatureDef.FootstepMarkCount))
                Assert.Equal(source[i], edited[i]);
    }

    [Fact]
    public void ADefinitionWithoutMarksLeavesTheFileBytesAlone()
    {
        byte[] source = Record(r => { r[MarksOffset + 4] = 3; });
        KwdChunk chunk = Chunk(source);
        CreatureDef creature = Assert.Single(KwdCreatures.Parse([chunk]));
        KwdRecordImage template = Assert.Single(KwdRecordRoundTrip.Split(chunk));
        Assert.Equal(source, KwdCreatures.WriteRecord(creature with { FootstepMarks = [] }, template));
        Assert.Throws<InvalidDataException>(() =>
            KwdCreatures.WriteRecord(creature with { FootstepMarks = [new FootstepMarkPair(1, 2)] }, template));
    }

    [Fact]
    public void AMarkNormalizesByTheClipsFrameCountAsTheInstallDoes()
    {
        Assert.Equal(0, FootstepMarkPair.Normalize(5, 0));
        Assert.Equal(2048, FootstepMarkPair.Normalize(5, 10));
        Assert.Equal(819, FootstepMarkPair.Normalize(1, 5));
        Assert.Equal(1365, FootstepMarkPair.Normalize(1, 3));
        Assert.Equal((int)(0xFFFF * 4096L / 7), FootstepMarkPair.Normalize(0xFFFF, 7));
    }

    [InstallFact]
    public void TheShippedMarksArePresentForTheHumanoidsAndTheImpOnly()
    {
        var creatures = KwdCreatures.Parse(KwdReader.ReadFile(Path.Combine(Dk2InstallGate.Path, "Data", "editor", "Creatures.kwd")))
            .ToDictionary(c => c.Name.Trim());
        Assert.All(creatures.Values, c => Assert.Equal(CreatureDef.FootstepMarkCount, c.FootstepMarks.Count));
        Assert.Equal(new FootstepMarkPair(2, 8), creatures["Imp"].FootstepMarksOf(1));
        Assert.Equal(new FootstepMarkPair(2, 8), creatures["Imp"].FootstepMarksOf(2));
        Assert.Equal(new FootstepMarkPair(7, 0), creatures["Imp"].FootstepMarksOf(4));
        Assert.Equal(new FootstepMarkPair(16, 27), creatures["Knight"].FootstepMarksOf(1));
        Assert.Equal(new FootstepMarkPair(11, 24), creatures["Knight"].FootstepMarksOf(2));
        Assert.Equal(new FootstepMarkPair(5, 27), creatures["Giant"].FootstepMarksOf(1));
        Assert.Equal(default, creatures["Goblin"].FootstepMarksOf(1));
        Assert.Equal(default, creatures["Warlock"].FootstepMarksOf(2));
    }

    [InstallFact]
    public void TheLastTwoPairsHoldAnAsciiResourceNameNotMarks()
    {
        var creatures = KwdCreatures.Parse(KwdReader.ReadFile(Path.Combine(Dk2InstallGate.Path, "Data", "editor", "Creatures.kwd")))
            .ToDictionary(c => c.Name.Trim());
        FootstepMarkPair first = creatures["Goblin"].FootstepMarksOf(46), second = creatures["Goblin"].FootstepMarksOf(47);
        string text = new string(new[] { first.First, first.Second, second.First, second.Second }
            .SelectMany(w => new[] { (char)(w & 0xFF), (char)(w >> 8) }).ToArray());
        Assert.Equal("GOB-Pose", text);
    }
}
