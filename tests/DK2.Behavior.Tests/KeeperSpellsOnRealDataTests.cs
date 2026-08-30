using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Behavior.Tests;

/// <summary>
/// Two fields of a record nobody has a template for, checked against the file
/// they came out of.
///
/// <para><c>KwdKeeperSpells</c> reads exactly two numbers from the 406-byte
/// KEEPERSPELLS record — a spell's mana cost at 0x11C and its result's per-second
/// upkeep at 0xE2 — and both were found by scanning for a value the owner had
/// measured in the original. That is a powerful technique and a dangerous one:
/// any 406-byte stride has plenty of offsets that happen to hold 1500 or 7.</para>
///
/// <para><b>So neither offset is trusted, and this is where they are made to
/// earn it.</b> The project's rule for an unparsed record: the stride must
/// divide the content exactly, the reading must be plausible for <i>every</i>
/// record and not just the anchor, and the column must mean something. Both
/// columns pass, and the upkeep one passes twice over — it is non-zero for
/// precisely the five spells whose effect persists and zero for all eight
/// instants, which is a shape a coincidence does not have.</para>
///
/// <para>Skips when no installation is configured (DK2_DIR), so CI stays green
/// without the game. See docs/research/imps-and-mana.md, Q53 and Q55.</para>
/// </summary>
public class KeeperSpellsOnRealDataTests
{
    /// <summary>13 spells over 5278 bytes of content — exactly 406 each.</summary>
    private const int RecordSize = 406;

    private static IReadOnlyList<KeeperSpellDef>? Spells()
    {
        string? install = Dk2Install.Find();
        if (install is null) return null; // skipped: no DK2 install

        string path = Path.Combine(install, "Data", "editor", "KeeperSpells.kwd");
        if (!File.Exists(path)) return null;

        List<KwdChunk> chunks = KwdReader.ReadFile(path);
        KwdChunk chunk = chunks.Single();

        // The stride has to divide the content exactly, or every offset the
        // parser reads is being read from the wrong place in every record after
        // the first. Same structural check every other record here got.
        Assert.Equal(0, chunk.Content.Length % chunk.ItemCount);
        Assert.Equal(RecordSize, chunk.Content.Length / chunk.ItemCount);

        return KwdKeeperSpells.Parse(chunks);
    }

    private static GameDatabase? Database() =>
        Dk2Install.Find() is { } dir ? GameDatabaseLoader.Load(dir) : null;

    // ---- the cast cost ----------------------------------------------------

    /// <summary>
    /// The simulation charges for an imp what the game's own file says, and it
    /// gets there by lookup rather than by a constant. This assertion used to
    /// guard a hardcoded 1500; it now guards a parser, which is a better thing
    /// to be guarding.
    /// </summary>
    [Fact]
    public void CreateImpCostsWhatTheSimulationChargesForIt()
    {
        if (Database() is not { } database) return;

        Assert.Equal(1500, ImpSupplyRules.CreateImpBaseCost(database));
    }

    /// <summary>
    /// The offset is believable because every spell has a sensible price there,
    /// in an order that matches how the original plays: possession is the
    /// cheapest thing a keeper does and summoning the Horned Reaper the dearest.
    /// One value being right could be luck; thirteen cannot be.
    /// </summary>
    [Fact]
    public void EverySpellHasAPlausibleManaCostAtTheSameOffset()
    {
        if (Spells() is not { } spells) return;

        Assert.Equal(13, spells.Count);

        var expected = new Dictionary<string, int>
        {
            ["Possession"] = 500,
            ["Create Imp"] = 1500,
            ["Sight Of Evil"] = 5000,
            ["Heal"] = 5000,
            ["Thunderbolt"] = 6000,
            ["Call To Arms"] = 10000,
            ["Chicken"] = 10000,
            ["Create Gold"] = 15000,
            ["Turncoat"] = 20000,
            ["Tremor"] = 30000,
            ["Inferno"] = 50000,
            ["Summon Horny"] = 100000,
        };

        foreach ((string name, int cost) in expected)
            Assert.Equal(cost, spells.Single(s => s.Name == name).ManaCost);
    }

    /// <summary>
    /// <b>Summon Horny's 100000 is why the field is 32 bits.</b> Read as a
    /// ushort it comes back 34464, which is the kind of quiet wrongness that
    /// would have survived a long time — every other spell fits in 16 bits.
    /// </summary>
    [Fact]
    public void TheManaCostIsThirtyTwoBitsWide_AndSummonHornyProvesIt()
    {
        if (Spells() is not { } spells) return;

        Assert.Equal(100_000, spells.Single(s => s.Name == "Summon Horny").ManaCost);
        Assert.True(spells.Single(s => s.Name == "Summon Horny").ManaCost > ushort.MaxValue);
    }

    // ---- the upkeep -------------------------------------------------------

    /// <summary>
    /// <b>The number the owner measured, read back out of the file.</b> Four
    /// imps drained 28 mana a second in the original and five drained 35; the
    /// Create Imp record says 7.
    /// </summary>
    [Fact]
    public void AnImpCostsSevenManaASecond_WhichIsWhatTheOwnerMeasured()
    {
        if (Database() is not { } database) return;

        Assert.Equal(7, ManaRules.ImpUpkeepPerSecond(database));
        Assert.Equal(28, 4 * ManaRules.ImpUpkeepPerSecond(database));
        Assert.Equal(35, 5 * ManaRules.ImpUpkeepPerSecond(database));
    }

    /// <summary>
    /// <b>What makes 0xE2 an upkeep rather than a coincidence.</b> A spell whose
    /// effect is over the instant it lands has nothing to keep, and every one of
    /// them reads zero; the five whose result persists all read non-zero. No
    /// offset chosen at random behaves like that.
    ///
    /// <para>The second anchor is here too. 2000 for Summon Horny is the figure
    /// the community reports for a Horned Reaper's drain, arrived at with no
    /// reference to this file — and it is the number whose absence from the
    /// CREATURES record is why this mechanic went a whole session unimplemented.
    /// It was on the spell all along.</para>
    /// </summary>
    [Fact]
    public void OnlySpellsWithSomethingToKeepHaveAnUpkeep()
    {
        if (Spells() is not { } spells) return;

        var expected = new Dictionary<string, int>
        {
            // Persistent: a thing exists afterwards, and costs to keep.
            ["Create Imp"] = 7,
            ["Possession"] = 25,
            ["Call To Arms"] = 90,
            ["OneShotReaper"] = 125,
            ["Summon Horny"] = 2000,

            // Instantaneous: nothing survives the cast, nothing to charge for.
            ["Sight Of Evil"] = 0,
            ["Heal"] = 0,
            ["Thunderbolt"] = 0,
            ["Chicken"] = 0,
            ["Create Gold"] = 0,
            ["Turncoat"] = 0,
            ["Tremor"] = 0,
            ["Inferno"] = 0,
        };

        Assert.Equal(13, expected.Count);
        foreach ((string name, int upkeep) in expected)
            Assert.Equal(upkeep, spells.Single(s => s.Name == name).ManaUpkeepPerSecond);

        // Stated as the property rather than as thirteen numbers, so that the
        // claim being made is visible: eight zeroes and five non-zeroes, split
        // exactly along "does this spell leave anything behind".
        Assert.Equal(8, spells.Count(s => s.ManaUpkeepPerSecond == 0));
        Assert.Equal(5, spells.Count(s => s.ManaUpkeepPerSecond > 0));
    }

    /// <summary>
    /// The upkeep is not the cast cost scaled, nor any other function of it —
    /// worth asserting because "I have found a second copy of a field I already
    /// have" is the most likely way an offset hunt goes wrong.
    /// </summary>
    [Fact]
    public void TheUpkeepIsNotTheCastCostInDisguise()
    {
        if (Spells() is not { } spells) return;

        Assert.DoesNotContain(spells, s => s.ManaCost != 0 && s.ManaCost == s.ManaUpkeepPerSecond);

        // Possession is the cheapest spell to cast and the third dearest to
        // hold; Create Gold is the fourth dearest to cast and free to hold. The
        // two columns are not even correlated in order.
        KeeperSpellDef possession = spells.Single(s => s.Name == "Possession");
        KeeperSpellDef createGold = spells.Single(s => s.Name == "Create Gold");
        Assert.True(possession.ManaCost < createGold.ManaCost);
        Assert.True(possession.ManaUpkeepPerSecond > createGold.ManaUpkeepPerSecond);
    }
}
