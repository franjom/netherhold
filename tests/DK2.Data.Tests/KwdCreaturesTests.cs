// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

/// <summary>
/// Validates the CREATURES(170) record parser against the reference install.
/// These assertions are the guard against silent offset drift: if a field
/// offset is ever wrong, the semantic checks below fail loudly.
/// </summary>
public class KwdCreaturesTests
{
    private static List<CreatureDef> Load()
    {
        string path = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "Creatures.kwd");
        Assert.True(File.Exists(path), $"the install has no {path}");
        return KwdCreatures.Parse(KwdReader.ReadFile(path));
    }

    private static CreatureDef By(List<CreatureDef> all, string name) =>
        all.Single(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    [InstallFact]
    public void AllCreatures_Parse_WithContiguousIds()
    {
        List<CreatureDef> all = Load();

        Assert.Equal(46, all.Count);
        // Ids are 1..46 with no gaps or duplicates.
        Assert.Equal(
            Enumerable.Range(1, 46).ToList(),
            all.Select(c => (int)c.CreatureId).OrderBy(i => i).ToList());
        Assert.All(all, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
    }

    [InstallFact]
    public void CreatureIds_MatchFlameIndependentReverseEngineering()
    {
        List<CreatureDef> all = Load();

        // Cross-source check: these id->name mappings come from Flame's
        // src/dk2/entities/entities_type.h, derived by disassembling DKII.EXE
        // v1.70 — an entirely separate method from the file-format templates.
        // Agreement between the two is strong evidence both are correct.
        Assert.Equal("Imp", By(all, "Imp").Name);
        Assert.Equal(1, By(all, "Imp").CreatureId);
        Assert.Equal(3, By(all, "Bile Demon").CreatureId);
        Assert.Equal(5, By(all, "Warlock").CreatureId);
        Assert.Equal(8, By(all, "Vampire").CreatureId);
        Assert.Equal(10, By(all, "Troll").CreatureId);
        Assert.Equal(27, By(all, "Horny").CreatureId);
    }

    /// <summary>A spell slot's shot origin, the slot record's first twelve bytes (memory <c>+6</c>,
    /// <c>+0xA</c>, <c>+0xE</c>, read by <c>0x00498990</c>; combat-spells.md §3): the Warlock's is
    /// (0, −1761, 1433) and the Dark Elf's (0, −2048, 2662), the same in every slot of one creature.</summary>
    [InstallFact]
    public void ASpellSlotCarriesItsShotOrigin()
    {
        List<CreatureDef> all = Load();
        CreatureDef warlock = By(all, "Warlock");
        Assert.All(warlock.Spells, s => Assert.Equal((0, -1761, 1433), (s.OriginX, s.OriginY, s.OriginZ)));
        Assert.All(By(all, "Dark Elf").Spells, s => Assert.Equal((0, -2048, 2662), (s.OriginX, s.OriginY, s.OriginZ)));
    }

    /// <summary>The Portal Entrance effect at file byte 3894 (loader 0x00492320, staging +0x1326
    /// to def+0x776; portal-creature-action.md), ADR-0476.</summary>
    [InstallFact]
    public void EntranceEffectIds_AreTheShippedPortalEntranceEffects()
    {
        List<CreatureDef> all = Load();
        Assert.Equal(181, By(all, "Goblin").EntranceEffectId);
        Assert.Equal(178, By(all, "Dark Elf").EntranceEffectId);
        Assert.Equal(180, By(all, "Troll").EntranceEffectId);
        Assert.Equal(0, By(all, "Imp").EntranceEffectId);
    }

    [InstallFact]
    public void UniqueBaseCreatureIds_MapToOrdinaryArchetypes()
    {
        List<CreatureDef> all = Load();
        var expected = new Dictionary<string, byte>
        {
            ["Unique Firefly"] = 12,
            ["Unique Goblin"] = 7,
            ["Unique Warlock"] = 5,
            ["Unique Troll"] = 10,
            ["Unique Dark Elf"] = 6,
            ["Unique Skeleton"] = 9,
            ["Unique Mistress"] = 4,
            ["Unique Salamander"] = 11,
            ["Unique Rogue"] = 24,
            ["Unique Bile Demon"] = 3,
            ["Unique Vampire"] = 8,
            ["Unique Black Knight"] = 22,
            ["Unique Dark Angel"] = 23,
        };

        Assert.Equal(expected.Count, all.Count(c => c.Flags.HasFlag(CreatureFlags.IsUnique)));
        foreach ((string name, byte baseId) in expected)
            Assert.Equal(baseId, By(all, name).UniqueBaseCreatureId);
        Assert.All(all.Where(c => !c.Flags.HasFlag(CreatureFlags.IsUnique)),
            c => Assert.Equal(0, c.UniqueBaseCreatureId));
    }

    [InstallFact]
    public void Imp_HasWorkerSemantics()
    {
        List<CreatureDef> all = Load();

        CreatureDef imp = By(all, "Imp");
        Assert.True(imp.Flags.HasFlag(CreatureFlags.IsWorker));
        Assert.True(imp.Flags.HasFlag(CreatureFlags.IsEvil));
        Assert.Equal(0, imp.Pay);                 // imps are never paid
        Assert.Equal(500, imp.Hp);
        // Sentinel values: imps neither eat nor sleep in DK2.
        Assert.Equal(short.MaxValue, imp.TimeAwake);
        Assert.Equal(0, imp.TimeSleep);
        Assert.Equal(JobClass.Scout, imp.JobClass);
    }

    [InstallFact]
    public void JobPreferences_MatchKnownCreatureRoles()
    {
        List<CreatureDef> all = Load();

        // Troll is the workshop creature: manufactures when happy, hates research.
        CreatureDef troll = By(all, "Troll");
        Assert.Equal(JobClass.Artisan, troll.JobClass);
        Assert.Contains(troll.HappyJobs, j => j.Job == JobType.Manufacture);
        Assert.Contains(JobType.Research, troll.HateJobs);
        Assert.True(troll.ManufacturePerSecond > 0);
        Assert.Equal(0, troll.ResearchPerSecond);

        // Warlock is the library creature: the mirror image.
        CreatureDef warlock = By(all, "Warlock");
        Assert.Equal(JobClass.Thinker, warlock.JobClass);
        Assert.Contains(warlock.HappyJobs, j => j.Job == JobType.Research);
        Assert.Contains(JobType.Manufacture, warlock.HateJobs);
        Assert.True(warlock.ResearchPerSecond > 0);
        Assert.Equal(0, warlock.ManufacturePerSecond);
    }

    [InstallFact]
    public void HappyJobChances_SumTo100_WhereJobsExist()
    {
        List<CreatureDef> all = Load();

        // A strong structural check on the JobPreference offsets: DK2 authors
        // these as percentage tables, so populated tables should total 100.
        foreach (CreatureDef c in all.Where(c => c.HappyJobs.Count > 0))
        {
            int total = c.HappyJobs.Sum(j => j.ChancePercent);
            Assert.True(total == 100,
                $"{c.Name}: happy job chances total {total}, expected 100");
        }
    }

    [InstallFact]
    public void Attractions_PointAtTheCreaturesOwnRoom()
    {
        List<CreatureDef> all = Load();

        // Room ids per Flame's room_type.h (independent source): Library=6, Workshop=10.
        Assert.Contains(By(all, "Warlock").Attractions, a => a.RoomId == (int)RoomTypeId.Library);
        Assert.Contains(By(all, "Troll").Attractions, a => a.RoomId == (int)RoomTypeId.Workshop);
    }

    [InstallFact]
    public void StatRanges_AreSane()
    {
        List<CreatureDef> all = Load();

        foreach (CreatureDef c in all)
        {
            Assert.InRange(c.Hp, 1, 20000);
            Assert.InRange(c.Pay, 0, 5000);
            Assert.InRange(c.MeleeDamage, 0, 2000);
            Assert.InRange(c.Speed.ToDouble(), 0.1, 5.0);
            Assert.InRange(c.RunSpeed.ToDouble(), 0.1, 5.0);
            Assert.InRange(c.DistanceCanSee.ToDouble(), 0.0, 64.0);
            Assert.InRange(c.SizeClass, 1, 5);
            Assert.InRange(c.TurnRate, 1, 4096);
            Assert.True(Enum.IsDefined(c.JobClass), $"{c.Name}: bad JobClass {c.JobClass}");
            Assert.True(Enum.IsDefined(c.FightStyle), $"{c.Name}: bad FightStyle {c.FightStyle}");
            Assert.True(Enum.IsDefined(c.MeleeAttackType), $"{c.Name}: bad AttackType {c.MeleeAttackType}");
        }
    }
}
