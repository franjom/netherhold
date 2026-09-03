using System.Buffers.Binary;
using System.Text;
using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Behavior.Tests;

/// <summary>
/// One field of TRAPS, found from measurements and pinned before anything reads
/// it.
///
/// <para><b>Nothing in the simulation uses this yet</b>, and that is the point of
/// the test. A trap is an object, and objects arrive at M6 — but the number was
/// established now, by the owner measuring five traps in the original, and a
/// finding that is not asserted anywhere is a finding that quietly rots. There
/// is deliberately <b>no <c>KwdTraps</c> parser</b>: parsing a 579-byte record
/// on the strength of one understood field would be parsing it badly. This reads
/// the bytes directly, the way <c>KeeperSpellsOnRealDataTests</c> did before
/// spells needed a real parser.</para>
///
/// <para>Skips when no installation is configured (DK2_DIR). See Q59 and
/// docs/research/imps-and-mana.md.</para>
/// </summary>
public class TrapsOnRealDataTests
{
    /// <summary>16 traps over 9264 bytes of content — exactly 579 each.</summary>
    private const int RecordSize = 579;

    /// <summary>
    /// Mana per second a built trap costs its keeper.
    ///
    /// <b>CONFIRMED against five independent measurements</b> taken in the
    /// original by the owner (2026-08-30): Spike 5, Sentry 5, Fireburst 10,
    /// Freeze 10, Lightning 25 — five anchors across three distinct values.
    ///
    /// <para><b>And now confirmed a second way, from the other direction</b>
    /// (2026-09-03). OpenKeeper parses this record in full, and its field order
    /// puts <c>manaUsage</c> — an unsigned short — at exactly <b>0x0202</b>, with
    /// a record size of 516 to match. Its own offset comments elsewhere in the
    /// same reader act as checkpoints on that arithmetic. The offset here was
    /// found by scanning for a measured value; the name and width came from a
    /// source that never saw our measurements. See
    /// docs/research/external-resources.md.</para>
    ///
    /// <para><b>It also drew a distinction we had missed.</b> A trap has <i>two</i>
    /// mana fields: this standing <c>manaUsage</c>, and a separate 4-byte
    /// <c>manaCostToFire</c> at 0x01D4 — what one shot costs. The owner measured a
    /// standing upkeep that rose as traps were built, which is this one. Nothing
    /// reads the per-shot cost yet.</para>
    /// </summary>
    private const int OffsetManaUpkeepPerSecond = 0x0202;

    private static IReadOnlyDictionary<string, int>? UpkeepByName()
    {
        string? install = Dk2Install.Find();
        if (install is null) return null; // skipped: no DK2 install

        string path = Path.Combine(install, "Data", "editor", "Traps.kwd");
        if (!File.Exists(path)) return null;

        KwdChunk chunk = KwdReader.ReadFile(path).Single(c => c.Type == KwdRecordType.Traps);

        // The stride has to divide the content exactly, or the offset below is
        // being read from the wrong place in every record after the first.
        Assert.Equal(0, chunk.Content.Length % chunk.ItemCount);
        Assert.Equal(RecordSize, chunk.Content.Length / chunk.ItemCount);

        var byName = new Dictionary<string, int>();
        for (int i = 0; i < chunk.ItemCount; i++)
        {
            int at = i * RecordSize;
            ReadOnlySpan<byte> name = chunk.Content.AsSpan(at, 32);
            int end = name.IndexOf((byte)0);

            byName[Encoding.ASCII.GetString(end < 0 ? name : name[..end])] =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    chunk.Content.AsSpan(at + OffsetManaUpkeepPerSecond));
        }
        return byName;
    }

    [Fact]
    public void TheTrapRecordDividesExactly_AndNamesSixteenTraps()
    {
        if (UpkeepByName() is not { } upkeep) return;

        Assert.Equal(16, upkeep.Count);
        foreach (string name in new[]
                 {
                     "Spike Trap", "Sentry Trap", "Fireburst Trap", "Freeze Trap",
                     "Lightning Trap", "Guard Post", "Magic Door",
                 })
        {
            Assert.True(upkeep.ContainsKey(name), $"TRAPS does not name '{name}'");
        }
    }

    /// <summary>
    /// <b>Every trap the owner measured, read back out of the file.</b> Five
    /// anchors across three distinct values is a far stronger case than the
    /// spell upkeep had, and it took a refutation to get there: with only Spike
    /// and Sentry measured, both read 5, and a second identical value cannot
    /// separate a real column from a coincidence. The three-valued spread is
    /// what makes this an identification rather than a fit.
    /// </summary>
    [Theory]
    [InlineData("Spike Trap", 5)]
    [InlineData("Sentry Trap", 5)]
    [InlineData("Fireburst Trap", 10)]
    [InlineData("Freeze Trap", 10)]
    [InlineData("Lightning Trap", 25)]
    public void EachMeasuredTrapDrainsWhatTheOwnerSaw(string name, int manaPerSecond)
    {
        if (UpkeepByName() is not { } upkeep) return;

        Assert.Equal(manaPerSecond, upkeep[name]);
    }

    /// <summary>
    /// <b>A guard post costs nothing, and that is the column's own argument.</b>
    /// It is the only zero among the sixteen, and a guard post is the one entry
    /// in TRAPS that is not a trap at all — it is a spot for a creature to stand
    /// on. A cost field would read zero there and an arbitrary column would not.
    ///
    /// <para><b>The door rows are not live data, and the owner proved it.</b>
    /// This column says Magic Door 30; the owner built two magic doors and the
    /// drain did not move at all. Doors have their own record - `Doors.kwd`, 8
    /// of them, Magic Door among them - so the door rows in TRAPS are
    /// vestigial, which "Spare Door 1" at 250 corroborates: no cost in this
    /// game is 250 a second. The five values that matter are all traps and all
    /// measured. See Q59.</para>
    /// </summary>
    [Fact]
    public void AGuardPostIsTheOnlyFreeEntry_BecauseItIsNotATrap()
    {
        if (UpkeepByName() is not { } upkeep) return;

        Assert.Equal(0, upkeep["Guard Post"]);
        Assert.Equal(30, upkeep["Magic Door"]);

        List<string> free = upkeep.Where(e => e.Value == 0).Select(e => e.Key).ToList();
        Assert.Equal(["Guard Post"], free);
    }

    /// <summary>
    /// The whole column, so that a change to the offset has to be a deliberate
    /// edit rather than a silent drift. Read as u16; every record's high byte is
    /// zero, so a u8 reading agrees and the width is not actually pinned by the
    /// data — noted rather than hidden.
    /// </summary>
    [Fact]
    public void TheWholeUpkeepColumnIsPlausible()
    {
        if (UpkeepByName() is not { } upkeep) return;

        var expected = new Dictionary<string, int>
        {
            ["Gas Trap"] = 5,
            ["Boulder Trap"] = 5,
            ["Sentry Trap"] = 5,
            ["Alarm Trap"] = 5,
            ["Fireburst Trap"] = 10,
            ["Lightning Trap"] = 25,
            ["Freeze Trap"] = 10,
            ["Fear Trap"] = 5,
            ["Trigger Trap"] = 5,
            ["Spike Trap"] = 5,
            ["Guard Post"] = 0,
            ["Magic Door"] = 30,
            ["Spare Door 1"] = 250,
            ["Hero Sentry Trap"] = 50,
            ["Hero Alarm Trap"] = 5,
            ["Jack In The Box"] = 5,
        };

        Assert.Equal(16, expected.Count);
        foreach ((string name, int mana) in expected)
            Assert.Equal(mana, upkeep[name]);
    }
}
