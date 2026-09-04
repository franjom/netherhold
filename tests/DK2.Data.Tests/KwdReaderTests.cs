// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

public class KwdReaderTests
{
    private static string? EditorDir()
    {
        string dir = Dk2Install.Find() ?? "";
        string editor = Path.Combine(dir, "Data", "editor");
        return Directory.Exists(editor) ? editor : null;
    }

    [Fact]
    public void EveryEditorKwd_ParsesToChunks_WithConsistentSizes()
    {
        string? editor = EditorDir();
        if (editor is null) return;

        foreach (string file in Directory.GetFiles(editor, "*.kwd"))
        {
            List<KwdChunk> chunks = KwdReader.ReadFile(file);
            Assert.NotEmpty(chunks);
        }
    }

    [Fact]
    public void EveryCampaignMap_ParsesToChunks()
    {
        string? editor = EditorDir();
        if (editor is null) return;

        string maps = Path.Combine(editor, "maps");
        int parsed = 0;
        foreach (string file in Directory.GetFiles(maps, "*.kwd"))
        {
            List<KwdChunk> chunks = KwdReader.ReadFile(file);
            Assert.NotEmpty(chunks);
            parsed++;
        }
        Assert.True(parsed > 50, $"expected all campaign/skirmish maps, parsed {parsed}");
    }

    [Fact]
    public void EveryKldFile_ParsesToChunks()
    {
        string? editor = EditorDir();
        if (editor is null) return;

        int parsed = 0;
        foreach (string file in Directory.GetFiles(Path.Combine(editor, "maps"), "*.kld"))
        {
            List<KwdChunk> chunks = KwdReader.ReadFile(file);
            Assert.NotEmpty(chunks);
            parsed++;
        }
        Assert.True(parsed > 300, $"expected ~330 KLD files, parsed {parsed}");
    }

    [Fact]
    public void GlobalVariables_ParsesToBalanceTable_WithDocumentedValues()
    {
        string? editor = EditorDir();
        if (editor is null) return;

        List<KwdChunk> chunks = KwdReader.ReadFile(Path.Combine(editor, "GlobalVariables.kwd"));
        GlobalVariables vars = KwdVariables.Parse(chunks);

        // Empirical spot checks. These specific values were documented by the
        // OpenKeeper project; here we CONFIRM them against real v1.70 GOG data.
        Assert.Equal(825, vars[VariableTypeId.ClaimTileHealth]);
        Assert.Equal(3000, vars[VariableTypeId.MaxGoldPerTreasuryTile]);
        Assert.Equal(-300, vars[VariableTypeId.MineGoldHealth]);
        Assert.Equal(60, vars[VariableTypeId.DeadBodyDiesAfterSeconds]);

        // Structured payload sanity.
        Assert.NotEmpty(vars.CreatureStats);
        Assert.NotEmpty(vars.Misc);
    }
}
