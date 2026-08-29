using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

/// <summary>
/// Validates the CREATURES(170) record parser against the reference install.
/// These assertions are the guard against silent offset drift: if a field
/// offset is ever wrong, the semantic checks below fail loudly.
/// </summary>
public class KwdCreaturesTests
{
    private static List<CreatureDef>? Load()
    {
        string? env = Environment.GetEnvironmentVariable("DK2_DIR");
        string dir = env ?? @"E:\GogGames\Dungeon Keeper 2";
        string path = Path.Combine(dir, "Data", "editor", "Creatures.kwd");
        return File.Exists(path) ? KwdCreatures.Parse(KwdReader.ReadFile(path)) : null;
    }

    private static CreatureDef By(List<CreatureDef> all, string name) =>
        all.Single(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void AllCreatures_Parse_WithContiguousIds()
    {
        List<CreatureDef>? all = Load();
        if (all is null) return;

        Assert.Equal(46, all.Count);
        // Ids are 1..46 with no gaps or duplicates.
        Assert.Equal(
            Enumerable.Range(1, 46).ToList(),
            all.Select(c => (int)c.CreatureId).OrderBy(i => i).ToList());
        Assert.All(all, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
    }

    [Fact]
    public void CreatureIds_MatchFlameIndependentReverseEngineering()
    {
        List<CreatureDef>? all = Load();
        if (all is null) return;

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

    [Fact]
    public void Imp_HasWorkerSemantics()
    {
        List<CreatureDef>? all = Load();
        if (all is null) return;

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

    [Fact]
    public void JobPreferences_MatchKnownCreatureRoles()
    {
        List<CreatureDef>? all = Load();
        if (all is null) return;

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

    [Fact]
    public void HappyJobChances_SumTo100_WhereJobsExist()
    {
        List<CreatureDef>? all = Load();
        if (all is null) return;

        // A strong structural check on the JobPreference offsets: DK2 authors
        // these as percentage tables, so populated tables should total 100.
        foreach (CreatureDef c in all.Where(c => c.HappyJobs.Count > 0))
        {
            int total = c.HappyJobs.Sum(j => j.ChancePercent);
            Assert.True(total == 100,
                $"{c.Name}: happy job chances total {total}, expected 100");
        }
    }

    [Fact]
    public void Attractions_PointAtTheCreaturesOwnRoom()
    {
        List<CreatureDef>? all = Load();
        if (all is null) return;

        // Room ids per Flame's room_type.h (independent source): Library=6, Workshop=10.
        Assert.Contains(By(all, "Warlock").Attractions, a => a.RoomId == (int)RoomTypeId.Library);
        Assert.Contains(By(all, "Troll").Attractions, a => a.RoomId == (int)RoomTypeId.Workshop);
    }

    [Fact]
    public void StatRanges_AreSane()
    {
        List<CreatureDef>? all = Load();
        if (all is null) return;

        foreach (CreatureDef c in all)
        {
            Assert.InRange(c.Hp, 1, 20000);
            Assert.InRange(c.Pay, 0, 5000);
            Assert.InRange(c.MeleeDamage, 0, 2000);
            Assert.InRange(c.Speed.ToDouble(), 0.1, 5.0);
            Assert.InRange(c.RunSpeed.ToDouble(), 0.1, 5.0);
            Assert.InRange(c.DistanceCanSee.ToDouble(), 0.0, 64.0);
            Assert.True(Enum.IsDefined(c.JobClass), $"{c.Name}: bad JobClass {c.JobClass}");
            Assert.True(Enum.IsDefined(c.FightStyle), $"{c.Name}: bad FightStyle {c.FightStyle}");
            Assert.True(Enum.IsDefined(c.MeleeAttackType), $"{c.Name}: bad AttackType {c.MeleeAttackType}");
        }
    }
}
