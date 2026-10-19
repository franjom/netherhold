// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// Every shipped level carries its own VARIABLES, and they change mechanics that
/// are already built.
///
/// <para>Found 2026-09-04 while working out what "what rooms do" would need:
/// <c>GameDatabaseLoader.Load</c> read only <c>GlobalVariables.kwd</c> and
/// ignored the sixty-six <c>&lt;name&gt;Variables.kld</c> files entirely. That is
/// not a cosmetic gap — <b>26 distinct variable ids are overridden by at least
/// one shipped level</b>, and one of them is read by a system that ships:
/// <c>MinimumImpThreshold</c> is <b>0</b> on four of the five secret levels and
/// on the front end, so a keeper there gets no free imps at all where
/// <see cref="Simulation.ImpSupplyRules"/> would hand out four (ADR-0021).</para>
///
/// <para>These tests pin the numbers against the install, so the merge cannot
/// drift from what the files say. The merge <i>rules</i> are pinned separately
/// and without an install in
/// <c>DK2.Data.Tests.GlobalVariablesMergeTests</c>.</para>
///
/// <para>Gated with [InstallFact], so it reports SKIPPED without an installation rather than passing
/// without proprietary assets.</para>
/// </summary>
public class LevelVariablesOnRealDataTests
{
    private static string Install() => Dk2InstallGate.Path;

    /// <summary>
    /// The defect this work exists to fix, stated as the two numbers that
    /// differ: four free imps globally, none on a secret level.
    /// </summary>
    [InstallFact]
    public void TheSecretLevelsGiveAKeeperNoFreeImps()
    {
        string install = Install();

        GameDatabase global = GameDatabaseLoader.Load(install);
        Assert.Equal(4, global[VariableTypeId.MinimumImpThreshold]);

        foreach (string level in new[] { "Secret1", "Secret2", "Secret4", "Secret5" })
        {
            GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, level);
            Assert.Equal(0, db[VariableTypeId.MinimumImpThreshold]);
        }

        // Secret3 is the exception and is worth pinning as one, so that "all the
        // secret levels" is not quietly assumed by a later reader.
        Assert.Equal(
            4,
            GameDatabaseLoader.LoadForCampaignLevel(install, "Secret3")[
                VariableTypeId.MinimumImpThreshold]);
    }

    /// <summary>
    /// Level 1 overrides values, adds ids the global table does not have at all,
    /// and leaves the rest alone — all three halves of the merge, on one level.
    /// </summary>
    [InstallFact]
    public void LevelOneOverridesReplacesAddsAndKeeps()
    {
        string install = Install();

        GameDatabase global = GameDatabaseLoader.Load(install);
        GameDatabase level1 = GameDatabaseLoader.LoadForCampaignLevel(install, "level1");

        // Replaced: both payday variables double on level 1.
        Assert.Equal(600, global[VariableTypeId.PayDayFrequencySeconds]);
        Assert.Equal(1200, level1[VariableTypeId.PayDayFrequencySeconds]);
        Assert.Equal(600, global[VariableTypeId.TimeBeforeFirstPayDaySeconds]);
        Assert.Equal(1200, level1[VariableTypeId.TimeBeforeFirstPayDaySeconds]);

        // Replaced: the portal's spawn interval, and a mood rate.
        Assert.Equal(25, global[VariableTypeId.EntranceGenerationSpeedSeconds]);
        Assert.Equal(18, level1[VariableTypeId.EntranceGenerationSpeedSeconds]);
        Assert.Equal(
            150, global[VariableTypeId.ModifyAngerInCompanyOfHatedCreaturesPerSecond]);
        Assert.Equal(
            100, level1[VariableTypeId.ModifyAngerInCompanyOfHatedCreaturesPerSecond]);

        // Kept: level 1 says nothing about these, so the global value stands.
        Assert.Equal(
            global[VariableTypeId.MaxGoldPerTreasuryTile],
            level1[VariableTypeId.MaxGoldPerTreasuryTile]);
        Assert.Equal(
            global[VariableTypeId.MinimumImpThreshold],
            level1[VariableTypeId.MinimumImpThreshold]);

        // Added: ids the global table does not carry at all.
        Assert.False(global.Variables.TryGet(VariableTypeId.GameTicks, out _));
        Assert.True(level1.Variables.TryGet(VariableTypeId.GameTicks, out int gameTicks));
        Assert.Equal(340, gameTicks);

        // The merge adds and never loses: level 1 contributes four plain ids the global
        // table lacks, so the merged count is exactly four higher. Its fifth, id 77, is a
        // structured record list kept apart from Misc since ADR-0491.
        Assert.Equal(global.Variables.Misc.Count + 4, level1.Variables.Misc.Count);
        Assert.Empty(global.Variables.CreatureFieldOverrides);
        Assert.Equal(2, level1.Variables.CreatureFieldOverrides.Count);
    }

    /// <summary>
    /// <c>GameTicks</c> (id 74) is carried by <b>every</b> shipped level, at 340
    /// in all of them.
    ///
    /// <para><b>This corrects a recorded negative result.</b> Q1 said the id was
    /// carried by "no shipped file — not GlobalVariables.kwd, not any of the 71
    /// map KWDs". The sweep that produced that looked at the <c>*.kwd</c> map
    /// files; the value lives in the <c>*Variables.kld</c> siblings.</para>
    ///
    /// <para><b>What 340 means is UNKNOWN and this test claims nothing about
    /// it.</b> 340 ticks a second is implausible for a 1999 simulation, the id
    /// shares its "no trailing fields" shape with <c>LevelRating</c> and
    /// <c>AverageTime</c> (which are front-end metadata), and OpenKeeper's own
    /// comment on the same id records <c>value=4</c>, which matches nothing in
    /// this install — so even the community name may be unreliable. What is
    /// pinned here is only that the number exists, is the same everywhere, and
    /// is therefore a candidate rather than a coincidence.</para>
    /// </summary>
    [InstallFact]
    public void EveryLevelCarriesGameTicksAndTheyAllAgree()
    {
        string install = Install();

        string maps = Path.Combine(install, "Data", "editor", "maps");
        int levels = 0;

        foreach (string file in Directory.GetFiles(maps, "*Variables.kld"))
        {
            string level = Path.GetFileNameWithoutExtension(file);
            level = level[..^"Variables".Length];

            GlobalVariables? vars = GameDatabaseLoader.LoadCampaignVariables(install, level);
            Assert.NotNull(vars);
            Assert.True(
                vars.TryGet(VariableTypeId.GameTicks, out int ticks),
                $"{level} carries no GameTicks");
            Assert.Equal(340, ticks);
            levels++;
        }

        Assert.True(levels > 60, $"expected the whole campaign, swept {levels} levels");
    }

    /// <summary>
    /// A level's availability rows follow the global template's, and override it
    /// type by type (DK2's handler 0x0051CA60 writes rows as read, ADR-0281).
    ///
    /// <para>The global template is player 0's. Level 1 marks the Treasury and the
    /// Library unavailable and says nothing of the Lair or the Hatchery, which its
    /// keeper then has from the template: the two rooms the level is played with.</para>
    ///
    /// <para>Kind and value ids follow OpenKeeper's ROOM/CREATURE/TRAP/DOOR/SPELL and
    /// UNAVAILABLE/RESEARCHABLE/AVAILABLE/UNAVAILABLE_ROOM, reached from the data first.</para>
    /// </summary>
    [InstallFact]
    public void ALevelsAvailabilityRowsOverrideTheGlobalTemplateTypeByType()
    {
        string install = Install();

        GameDatabase global = GameDatabaseLoader.Load(install);
        GameDatabase level1 = GameDatabaseLoader.LoadForCampaignLevel(install, "level1");
        GameDatabase level20 = GameDatabaseLoader.LoadForCampaignLevel(install, "level20");

        // The global template is player 0 throughout; a level's own rows are not.
        Assert.All(global.Variables.Availabilities, a => Assert.Equal(0, a.PlayerId));
        Assert.Equal(32, global.Variables.Availabilities.Count);
        Assert.Equal(32 + 31, level1.Variables.Availabilities.Count);
        Assert.Equal(31, level1.Variables.Availabilities.Count(a => a.PlayerId != 0));

        // Keeper 3's rooms: the level's two, the template's for the rest.
        const ushort room = 1;
        Dictionary<int, int> rooms = level1.Variables.AvailabilitiesOf(3)
            .Where(a => a.Kind == room).ToDictionary(a => a.TypeId, a => a.Value);
        Assert.Equal(1, rooms[(int)RoomTypeId.Treasury]);
        Assert.Equal(1, rooms[(int)RoomTypeId.Library]);
        Assert.Equal(3, rooms[(int)RoomTypeId.Lair]);
        Assert.Equal(3, rooms[(int)RoomTypeId.Hatchery]);

        // And the level's own room rows grow through the campaign, which is what
        // makes them a per-level fact rather than a constant.
        int roomsAtLevel1 = level1.Variables.Availabilities.Count(a => a.Kind == room && a.PlayerId != 0);
        int roomsAtLevel20 = level20.Variables.Availabilities.Count(a => a.Kind == room && a.PlayerId != 0);
        Assert.True(
            roomsAtLevel20 > roomsAtLevel1,
            $"level 20 offered {roomsAtLevel20} rooms, level 1 offered {roomsAtLevel1}");
    }

    /// <summary>
    /// Lists the level is silent about are kept, not cleared.
    ///
    /// The temple's sacrifice recipes and the per-level creature stat rows are
    /// plainly game-wide, and no level ships either — so "the level provided
    /// none" has to mean "keep the global set". Pinned because the opposite
    /// reading would silently empty two tables nothing reads yet.
    /// </summary>
    [InstallFact]
    public void GameWideListsSurviveALevelThatCarriesNone()
    {
        string install = Install();

        GameDatabase global = GameDatabaseLoader.Load(install);
        GameDatabase level1 = GameDatabaseLoader.LoadForCampaignLevel(install, "level1");

        Assert.NotEmpty(global.Variables.Sacrifices);
        Assert.NotEmpty(global.Variables.CreatureStats);
        Assert.Equal(global.Variables.Sacrifices.Count, level1.Variables.Sacrifices.Count);
        Assert.Equal(global.Variables.CreatureStats.Count, level1.Variables.CreatureStats.Count);
    }

    /// <summary>
    /// Level 1's creature pool is fifteen Goblins, and its creature availability
    /// names the same creature.
    ///
    /// The two halves of "which creatures may enter" agreeing on one level is
    /// what makes either readable, and it is the data a portal will need.
    /// </summary>
    [InstallFact]
    public void LevelOnesCreaturePoolAndAvailabilityAgree()
    {
        string install = Install();

        GameDatabase level1 = GameDatabaseLoader.LoadForCampaignLevel(install, "level1");

        CreaturePoolEntry pooled = Assert.Single(level1.Variables.CreaturePool);
        Assert.Equal("Goblin", level1.CreatureById((byte)pooled.CreatureId).Name);
        Assert.Equal(15, pooled.Value);

        const ushort creature = 2;
        AvailabilityEntry offered = Assert.Single(
            level1.Variables.Availabilities, a => a.Kind == creature);
        Assert.Equal(pooled.CreatureId, offered.TypeId);
        Assert.Equal(pooled.PlayerId, offered.PlayerId);
    }

    /// <summary>
    /// Every level that overrides the portal's base interval, and there are
    /// <b>five</b> — not the one this project had recorded.
    ///
    /// <para>Found 2026-09-07 while looking for a level with two claimable
    /// portals for the Q86 timing. `kwd-variables.md` listed level 1's 18 in its
    /// table of live overrides and nobody had swept the other 65 files for the
    /// same id, so "25 game-wide, 18 on level 1" had been repeated in four
    /// places and asserted to the owner as *"the only one of 66"*. It is not.
    /// </para>
    ///
    /// <para><b>The first three levels ramp</b> — 18, 20, 22, then the global 25
    /// from level 4 on. That is a deliberate difficulty gradient across the
    /// tutorial levels and it was invisible while only level 1 was known.</para>
    ///
    /// <para><b>And Secret1's portal is switched off, not slowed</b>: 10000
    /// seconds is two hours and forty-seven minutes, so no creature arrives in
    /// any real play session. Secret1 also sets `MinimumImpThreshold` to 0
    /// (ADR-0040), so it is a level with a fixed roster and no way to add to it —
    /// the same "disabled rather than cut" shape as ADR-0085's ten mechanisms,
    /// this time in a per-level file rather than the global table.</para>
    /// </summary>
    [InstallFact]
    public void FiveLevelsOverrideThePortalInterval_NotOnlyLevelOne()
    {
        string install = Install();

        string maps = Path.Combine(install, "Data", "editor", "maps");
        var found = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.GetFiles(maps, "*Variables.kld"))
        {
            GlobalVariables own = KwdVariables.Parse(KwdReader.ReadFile(file));
            if (own.TryGet(VariableTypeId.EntranceGenerationSpeedSeconds, out int seconds))
            {
                found[Path.GetFileName(file)[..^"Variables.kld".Length]] = seconds;
            }
        }

        Assert.Equal(
            new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["level1"] = 18,
                ["level2"] = 20,
                ["level3"] = 22,
                ["Level10"] = 30,
                ["Secret1"] = 10000,
            },
            found);

        // 61 of the 66 shipped levels take the global value, so a level with
        // base 25 is any of them — which is what identifies the level behind an
        // observed 25-second first gap, and it is not "not level 1".
        Assert.Equal(66, Directory.GetFiles(maps, "*Variables.kld").Length);
        Assert.Equal(25, GameDatabaseLoader.Load(install)
            [VariableTypeId.EntranceGenerationSpeedSeconds]);
    }

}
