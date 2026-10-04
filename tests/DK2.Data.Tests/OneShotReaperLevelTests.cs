// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class OneShotReaperLevelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(255)]
    public void ExtendedMetadataExposesTheUnsignedLevelByteAndPreservesTheRecord(byte value)
    {
        KwdChunk chunk = Chunk(25683);
        chunk.Content[25603] = value;
        chunk.Content[25604] = 0xE7;
        LevelDef level = KwdLevel.Parse([chunk])!;

        Assert.Equal(value, level.OneShotReaperLevel);
        Assert.Equal(chunk.Content, KwdLevel.WriteRecord(level, chunk));

        byte[] metadata = level.Unknown_6403.ToArray();
        metadata[0] = unchecked((byte)(value + 1));
        LevelDef changed = level with { Unknown_6403 = metadata };
        byte[] written = KwdLevel.WriteRecord(changed, chunk);
        Assert.Equal(metadata[0], KwdLevel.Parse([chunk with { Content = written }])!.OneShotReaperLevel);
        for (int i = 0; i < written.Length; i++)
            if (i != 25603) Assert.Equal(chunk.Content[i], written[i]);
    }

    [Fact]
    public void BaseMetadataHasNoChosenLevel()
    {
        LevelDef level = KwdLevel.Parse([Chunk(25603)])!;
        Assert.Null(level.OneShotReaperLevel);
    }

    [Fact]
    public void LevelMetadataSurvivesLaterVariableMergesWithoutInventingDefaults()
    {
        var variables = new GlobalVariables();
        variables.Misc[VariableTypeId.MineGoldHealth] = new(VariableTypeId.MineGoldHealth, -300, 0, 0);
        var original = new GameDatabase([], [], [], variables);
        Assert.Null(original.Level);
        Assert.Same(original, original.ForLevel(null));

        KwdChunk chunk = Chunk(25683);
        chunk.Content[25603] = 3;
        LevelDef level = KwdLevel.Parse([chunk])!;
        GameDatabase attached = original.ForLevel(null, level);
        Assert.Same(level, attached.Level);
        Assert.Same(variables, attached.Variables);
        Assert.Null(original.Level);

        var overrides = new GlobalVariables();
        overrides.Misc[VariableTypeId.MineGoldHealth] = new(VariableTypeId.MineGoldHealth, -400, 0, 0);
        GameDatabase merged = attached.ForLevel(overrides);
        Assert.Same(level, merged.Level);
        Assert.Equal((byte)3, merged.Level!.OneShotReaperLevel);
        Assert.Equal(-400, merged[VariableTypeId.MineGoldHealth]);
        Assert.Equal(-300, attached[VariableTypeId.MineGoldHealth]);

        LevelDef withoutMetadata = KwdLevel.Parse([Chunk(25603)])!;
        GameDatabase replaced = merged.ForLevel(null, withoutMetadata);
        Assert.Same(withoutMetadata, replaced.Level);
        Assert.Null(replaced.Level!.OneShotReaperLevel);
        Assert.Same(merged.Variables, replaced.Variables);
        Assert.Same(level, new GameDatabase([], [], [], variables, level: level).Level);
    }

    [InstallFact]
    public void CampaignLoaderCarriesTheManifestAlongsideItsVariables()
    {
        LevelDef expected = GameDatabaseLoader.LoadCampaignLevel(Dk2InstallGate.Path, "level1")!;
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "level1");
        Assert.NotNull(database.Level);
        Assert.Equal(expected.Name, database.Level.Name);
        Assert.Equal(expected.OneShotReaperLevel, database.Level.OneShotReaperLevel);
        Assert.Equal(expected.Unknown_6403.ToArray(), database.Level.Unknown_6403.ToArray());
    }

    private static KwdChunk Chunk(int metadata)
    {
        byte[] bytes = new byte[metadata + 8 + 72 + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(metadata), 222);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(metadata + 4), 80);
        byte[] header = new byte[28];
        BinaryPrimitives.WriteUInt16LittleEndian(header, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 2);
        return new KwdChunk { Type = KwdRecordType.Level, ItemCount = 1, Content = bytes, HeaderBlock = header };
    }
}
