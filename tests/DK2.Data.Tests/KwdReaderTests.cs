// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdReaderTests
{
    private static string EditorDir()
    {
        string editor = Path.Combine(Dk2InstallGate.Path, "Data", "editor");
        Assert.True(Directory.Exists(editor), $"the install has no {editor}");
        return editor;
    }

    [InstallFact]
    public void EveryEditorKwd_ParsesToChunks_WithConsistentSizes()
    {
        string editor = EditorDir();

        foreach (string file in Directory.GetFiles(editor, "*.kwd"))
        {
            List<KwdChunk> chunks = KwdReader.ReadFile(file);
            Assert.NotEmpty(chunks);
        }
    }

    [InstallFact]
    public void EveryCampaignMap_ParsesToChunks()
    {
        string editor = EditorDir();

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

    [InstallFact]
    public void EveryKldFile_ParsesToChunks()
    {
        string editor = EditorDir();

        int parsed = 0;
        foreach (string file in Directory.GetFiles(Path.Combine(editor, "maps"), "*.kld"))
        {
            List<KwdChunk> chunks = KwdReader.ReadFile(file);
            Assert.NotEmpty(chunks);
            parsed++;
        }
        Assert.True(parsed > 300, $"expected ~330 KLD files, parsed {parsed}");
    }

    [InstallFact]
    public void GlobalVariables_ParsesToBalanceTable_WithDocumentedValues()
    {
        string editor = EditorDir();

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

    /// <summary>VARIABLES id 77 (variables-corpus-sweep.md §Q84, ADR-0491): several records per level,
    /// each one field of one creature type. Level 1 gives its Lord of the Land (29) MeleeDamage
    /// (selector 4) 200, then Threat (selector 3) 125.</summary>
    [InstallFact]
    public void Level1CarriesTwoCreatureFieldOverridesForItsLord()
    {
        string maps = Path.Combine(EditorDir(), "maps");
        GlobalVariables level1 = KwdVariables.Parse(KwdReader.ReadFile(Path.Combine(maps, "Level1Variables.kld")));
        Assert.Equal(
            [new CreatureFieldOverrideEntry(29, (int)CreatureStatId.MeleeDamage, 200), new CreatureFieldOverrideEntry(29, (int)CreatureStatId.Threat, 125)],
            level1.CreatureFieldOverrides);

        // The campaign loader applies them: the Lord's file Threat is 320.
        CreatureDef lord1 = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "level1").CreatureById(29);
        Assert.Equal(((ushort)125, (ushort)200), (lord1.Threat, lord1.MeleeDamage));
        Assert.Equal((ushort)150, GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "level4").CreatureById(29).Threat);
        Assert.Equal((ushort)320, GameDatabaseLoader.Load(Dk2InstallGate.Path).CreatureById(29).Threat);
    }
}
