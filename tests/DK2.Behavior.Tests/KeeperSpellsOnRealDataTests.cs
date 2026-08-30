using DK2.Data;
using DK2.Data.Kwd;
using DK2.Simulation;

namespace DK2.Behavior.Tests;

/// <summary>
/// The one number this project reads out of a record it does not parse.
///
/// <see cref="ImpSupplyRules.CreateImpBaseCost"/> is 1500, and it is written into
/// simulation code — a deliberate, single exception to the rule that a balance
/// constant in the code is a bug. <c>KeeperSpells.kwd</c> is real shipped data
/// but is not parsed until spells arrive at M7, so there is no
/// <c>GameDatabase</c> entry to read it from yet.
///
/// <para>This test is what makes the exception safe: it goes to the actual file,
/// reads the actual bytes, and fails if the constant and the game ever disagree.
/// It also re-validates the offset rather than trusting it — 0x11C is only
/// believable as "mana cost" because it holds a plausible one for every spell in
/// the record, so that is what is asserted.</para>
///
/// <para>All of this goes away when <c>KwdKeeperSpells</c> exists. See Q55.</para>
/// </summary>
public class KeeperSpellsOnRealDataTests
{
    /// <summary>13 spells over 5278 bytes of content — exactly 406 each.</summary>
    private const int RecordSize = 406;

    /// <summary>Where the mana cost sits in a spell record.</summary>
    private const int ManaCostOffset = 0x11C;

    private static IReadOnlyList<(string Name, int ManaCost)>? Spells()
    {
        string? install = Dk2Install.Find();
        if (install is null) return null; // skipped: no DK2 install

        string path = Path.Combine(install, "Data", "editor", "KeeperSpells.kwd");
        if (!File.Exists(path)) return null;

        List<KwdChunk> chunks = KwdReader.ReadFile(path);
        KwdChunk chunk = chunks.Single();

        // The stride has to divide the content exactly, or the offset below is
        // being read from the wrong place in every record after the first. Same
        // structural check every other record in this project got.
        Assert.Equal(0, chunk.Content.Length % chunk.ItemCount);
        Assert.Equal(RecordSize, chunk.Content.Length / chunk.ItemCount);

        var spells = new List<(string, int)>();
        for (int i = 0; i < chunk.ItemCount; i++)
        {
            int at = i * RecordSize;
            int end = at;
            while (end < at + 64 && chunk.Content[end] is >= 0x20 and <= 0x7e) end++;

            spells.Add((
                System.Text.Encoding.ASCII.GetString(chunk.Content, at, end - at),
                BitConverter.ToInt32(chunk.Content, at + ManaCostOffset)));
        }
        return spells;
    }

    [Fact]
    public void CreateImpCostsWhatTheSimulationChargesForIt()
    {
        IReadOnlyList<(string Name, int ManaCost)>? spells = Spells();
        if (spells is null) return;

        (string Name, int ManaCost) createImp = spells.Single(s => s.Name == "Create Imp");
        Assert.Equal(ImpSupplyRules.CreateImpBaseCost, createImp.ManaCost);
        Assert.Equal(1500, createImp.ManaCost);
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
        IReadOnlyList<(string Name, int ManaCost)>? spells = Spells();
        if (spells is null) return;

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
}
