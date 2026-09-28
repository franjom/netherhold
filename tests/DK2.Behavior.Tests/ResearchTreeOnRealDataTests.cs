// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The research tree's data, read from the install: the Variables id-17 list and the
/// keeper spells' shots. docs/research/research-tree.md.</summary>
public class ResearchTreeOnRealDataTests
{
    private static GameDatabase Database() => GameDatabaseLoader.Load(Dk2InstallGate.Path);

    /// <summary>29 entries, all in GlobalVariables.kwd, in one fixed order: twelve spells, then
    /// six doors, then eleven traps, and no room at all. Create Imp is first.</summary>
    [InstallFact]
    public void TheResearchListIsTwelveSpellsSixDoorsElevenTraps_CreateImpFirst()
    {
        List<ResearchEntry> list = Database().Variables.Research;

        Assert.Equal(29, list.Count);
        Assert.Equal(
            [4, 6, 2, 10, 1, 3, 8, 7, 5, 13, 9, 11],
            list.Take(12).Select(e => { Assert.Equal(ResearchTreeRules.KindSpell, e.Kind); return e.TypeId; }));
        Assert.Equal(
            [1, 3, 6, 2, 4, 5],
            list.Skip(12).Take(6).Select(e => { Assert.Equal(ResearchTreeRules.KindDoor, e.Kind); return e.TypeId; }));
        Assert.Equal(
            [3, 1, 11, 8, 7, 4, 10, 2, 9, 6, 5],
            list.Skip(18).Select(e => { Assert.Equal(ResearchTreeRules.KindTrap, e.Kind); return e.TypeId; }));
    }

    /// <summary>Every shipped entry costs the same 1000 points. The spell records' own research
    /// times (500 and 1500) are not what the adder compares against.</summary>
    [InstallFact]
    public void EveryResearchEntryCostsAThousandPoints() =>
        Assert.All(Database().Variables.Research, e => Assert.Equal(1000, e.Cost));

    /// <summary>No level file carries an id-17 record, so no level changes the list: the merged
    /// database of every level is the global one.</summary>
    [InstallFact]
    public void NoLevelChangesTheResearchList()
    {
        string editor = Path.Combine(Dk2InstallGate.Path!, "Data", "editor");
        GlobalVariables global = Database().Variables;
        int levels = 0;
        foreach (string file in Directory.EnumerateFiles(editor, "*Variables.kld", SearchOption.AllDirectories))
        {
            GlobalVariables level = KwdVariables.Parse(KwdReader.ReadFile(file));
            Assert.Empty(level.Research);
            levels++;
        }

        Assert.True(levels > 60, $"only {levels} level Variables files found");
        Assert.Equal(29, global.Research.Count);
    }

    /// <summary>Create Imp fires shot 2 with (Imp, level 1), and upgraded shot 60 with (Imp,
    /// level 4) — the level-4 imp the owner saw. The drift guard for the Create Imp record in
    /// <c>Dk2TerrainFixture</c>: the values are repeated rather than compared, as this project
    /// does not reference the simulation fixture.</summary>
    [InstallFact]
    public void CreateImpsBonusShotMakesALevelFourImp()
    {
        KeeperSpellDef spell = Database().RequireKeeperSpell(KeeperSpellName.CreateImp);
        CreatureDef imp = Database().CreatureByName("Imp")!;

        Assert.Equal((byte)4, spell.KeeperSpellId);
        Assert.Equal(((byte)2, 1, 1), spell.Shot(upgraded: false));
        Assert.Equal(((byte)60, 1, 4), spell.Shot(upgraded: true));
        Assert.Equal(imp.CreatureId, spell.ShotData1);
    }

    /// <summary>Research upgrades a spell only if its record has a bonus shot. Summon Horny and
    /// OneShotReaper have none — the owner confirms Horny cannot be upgraded — and every other
    /// shipped spell can be.</summary>
    [InstallFact]
    public void EverySpellButSummonHornyAndTheReaperCanBeUpgraded()
    {
        GameDatabase db = Database();
        string[] notUpgradable = db.KeeperSpells.Values
            .Where(s => !s.CanBeUpgraded).Select(s => s.Name).Order().ToArray();

        Assert.Equal(["OneShotReaper", "Summon Horny"], notUpgradable);
        Assert.All(db.Variables.Research.Where(e => e.Kind == ResearchTreeRules.KindSpell),
            e => Assert.NotNull(db.KeeperSpellById(e.TypeId)));
    }
}
