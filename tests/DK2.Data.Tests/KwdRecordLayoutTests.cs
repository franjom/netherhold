// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Data.Tests;

/// <summary>
/// Pins the on-disk record size of every KWD record type against the reference
/// install, and pins the shape of the two types that have no fixed size.
///
/// <para><b>Why this file exists.</b> Five record types are parsed; thirteen are
/// not, and they are needed at M6. Their sizes were derived on 2026-09-04 by
/// summing OpenKeeper's field lists (facts only — GPLv3; see
/// <c>docs/research/external-resources.md</c>) and then checking each total
/// against <c>contentSize / itemCount</c> from the shipped files. <b>Eight new
/// sizes matched to the byte; one disagreed and one was not answerable that
/// way.</b> Recording the numbers here means a future parser cannot quietly
/// disagree with the install, and the disagreement cannot be forgotten:
/// OpenKeeper's CREATURES field list accounts for <b>5377</b> of the record's
/// 5449 bytes and silently skips the last 72, so their layout is not a complete
/// description of 1.70 data. Ours reads nothing past 0x1081 and is unaffected —
/// but anybody extending it past there must measure rather than borrow.</para>
///
/// <para>This is the same structural validation that caught the WAD entry
/// stride and confirmed terrain 552 and rooms 1055 before a parser existed. It
/// is deliberately a test rather than a document: a document cannot fail.</para>
/// </summary>
public class KwdRecordLayoutTests
{
    private static string? FindInstall() => Dk2Install.Find();

    private static string Editor(string dir, string file) =>
        Path.Combine(dir, "Data", "editor", file);

    /// <summary>
    /// Every fixed-stride record type, and the stride the shipped files carry.
    ///
    /// <para>Each number is <c>contentSize / itemCount</c> from the install and
    /// is independently reproduced by summing OpenKeeper's field list for the
    /// same record — two routes, one number, on all of these.</para>
    /// </summary>
    public static TheoryData<string, KwdRecordType, int, int> FixedStrides() => new()
    {
        // file, type, item count, record size
        { "Terrain.kwd",        KwdRecordType.Terrain,         41,  552 },  // parsed
        { "Rooms.kwd",          KwdRecordType.Rooms,           26, 1055 },  // parsed
        { "Creatures.kwd",      KwdRecordType.Creatures,       46, 5449 },  // parsed
        { "KeeperSpells.kwd",   KwdRecordType.KeeperSpells,    13,  406 },  // two fields read
        { "Traps.kwd",          KwdRecordType.Traps,           16,  579 },  // one field read (Q59)
        { "Doors.kwd",          KwdRecordType.Doors,            8,  616 },  // unparsed, M6
        { "Objects.kwd",        KwdRecordType.Objects,        145,  894 },  // unparsed, M6
        { "Shots.kwd",          KwdRecordType.Shots,           65,  239 },  // unparsed, M6
        { "CreatureSpells.kwd", KwdRecordType.CreatureSpells,  41,  266 },  // unparsed, M7
        { "Effects.kwd",        KwdRecordType.Effects,        370,  246 },  // unparsed, M7
        { "EffectElements.kwd", KwdRecordType.EffectElements, 596,  182 },  // unparsed, M7
    };

    [Theory]
    [MemberData(nameof(FixedStrides))]
    public void AFixedStrideRecordTypeHasTheSizeItsFieldListPredicts(
        string file, KwdRecordType type, int expectedItems, int expectedSize)
    {
        string? dir = FindInstall();
        if (dir is null) return; // skipped: no DK2 install on this machine (e.g. CI)

        KwdChunk chunk = KwdReader.ReadFile(Editor(dir, file)).Single(c => c.Type == type);

        Assert.Equal(expectedItems, chunk.ItemCount);
        Assert.Equal(expectedItems * expectedSize, chunk.Content.Length);
    }

    /// <summary>
    /// VARIABLES is a tagged union in a fixed 16-byte cell: <c>u32 id</c> and 12
    /// bytes whose meaning the id picks. Pinned here because it is the one type
    /// where the field-list-summing method gives a nonsense answer (100 bytes,
    /// being every branch of the union added together) and the install gives the
    /// truth. A reminder that the method needs the cross-check, not the reverse.
    /// </summary>
    [Fact]
    public void AVariableIsSixteenBytesWhicheverKindItIs()
    {
        string? dir = FindInstall();
        if (dir is null) return;

        List<KwdChunk> chunks = KwdReader.ReadFile(Editor(dir, "GlobalVariables.kwd"));
        KwdChunk variables = chunks.Single(c => c.Type == KwdRecordType.Variables);

        Assert.Equal(621, variables.ItemCount);
        Assert.Equal(621 * 16, variables.Content.Length);
    }

    /// <summary>
    /// A PLAYERS record is 205 bytes. The odd one out among the sizes above: it
    /// lives in a level's own <c>*Players.kld</c> rather than in the editor
    /// database, so it had to be measured against a campaign level.
    /// </summary>
    [Fact]
    public void APlayerRecordIsTwoHundredAndFiveBytes()
    {
        string? dir = FindInstall();
        if (dir is null) return;

        string[] files = Directory.GetFiles(
            Path.Combine(dir, "Data", "editor", "maps"), "*Players.kld");
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            KwdChunk chunk = KwdReader.ReadFile(file).Single(c => c.Type == KwdRecordType.Players);
            Assert.True(chunk.ItemCount > 0, $"{Path.GetFileName(file)} declares no players.");
            Assert.Equal(chunk.ItemCount * 205, chunk.Content.Length);
        }
    }

    /// <summary>
    /// The finding this file was written for: THINGS and TRIGGERS records carry
    /// their own size, so the chunk splits with no layout knowledge at all — and
    /// the split adds up exactly, on every level DK2 ships.
    ///
    /// <para>"Exactly" is the whole assertion. A self-describing walk that
    /// merely does not crash proves nothing; one that consumes precisely
    /// <c>itemCount</c> records and precisely the whole chunk, 133 times over,
    /// cannot be a coincidence of alignment.</para>
    /// </summary>
    [Fact]
    public void EveryThingsAndTriggersChunkSplitsExactly()
    {
        string? dir = FindInstall();
        if (dir is null) return;

        string editor = Path.Combine(dir, "Data", "editor");
        string maps = Path.Combine(editor, "maps");

        // Every KWD and KLD, not just the obviously named ones. Narrowing this
        // to maps/*Things.kld is what hid the camera thing (tag 205) on the
        // first pass: its only 39 records live in GlobalVariables.kwd.
        var files = Directory.GetFiles(editor, "*.kwd")
            .Concat(Directory.GetFiles(maps, "*.kwd"))
            .Concat(Directory.GetFiles(maps, "*.kld"))
            .ToList();
        Assert.True(files.Count >= 400, $"expected the whole editor corpus, found {files.Count}.");

        int chunks = 0, things = 0, triggers = 0;
        var payloadSizes = new Dictionary<(KwdRecordType, int), SortedSet<int>>();

        foreach (string file in files)
        {
            foreach (KwdChunk chunk in KwdReader.ReadFile(file))
            {
                if (!KwdTaggedRecords.IsTagged(chunk.Type)) continue;

                // Split throws unless itemCount records consume the chunk to the byte.
                List<KwdTaggedRecords.TaggedRecord> records = KwdTaggedRecords.Split(chunk);
                Assert.Equal(chunk.ItemCount, records.Count);

                chunks++;
                if (chunk.Type == KwdRecordType.Things) things += records.Count;
                else triggers += records.Count;

                foreach (KwdTaggedRecords.TaggedRecord record in records)
                {
                    if (!payloadSizes.TryGetValue((chunk.Type, record.Tag), out SortedSet<int>? seen))
                        payloadSizes[(chunk.Type, record.Tag)] = seen = [];
                    seen.Add(record.Payload.Length);
                }
            }
        }

        Assert.Equal(133, chunks);
        Assert.Equal(6318, things);
        Assert.Equal(9161, triggers);

        // Every tag carries exactly one payload size, campaign-wide: these are
        // fixed-size record types multiplexed, not variable-length records.
        foreach (((KwdRecordType type, int tag), SortedSet<int> sizes) in payloadSizes)
        {
            Assert.True(sizes.Count == 1,
                $"{type} tag {tag} carries {sizes.Count} payload sizes: " +
                string.Join(", ", sizes) + " — so it is not fixed-size after all.");
        }

        // And the sizes are the ones recorded, for exactly the tags recorded.
        foreach ((int tag, (string name, int size, bool inMaps)) in KwdTaggedRecords.ThingPayloadSizes)
        {
            bool present = payloadSizes.TryGetValue((KwdRecordType.Things, tag), out SortedSet<int>? sizes);
            Assert.Equal(inMaps, present);
            if (present) Assert.Equal(size, sizes!.Single());
            else Assert.False(inMaps, $"thing {name} ({tag}) was expected in the shipped maps.");
        }

        foreach ((int tag, (string _, int size)) in KwdTaggedRecords.TriggerPayloadSizes)
            Assert.Equal(size, payloadSizes[(KwdRecordType.Triggers, tag)].Single());

        // Nothing turned up that is not in those two tables.
        Assert.DoesNotContain(payloadSizes.Keys, k =>
            k.Item1 == KwdRecordType.Things
                ? !KwdTaggedRecords.ThingPayloadSizes.ContainsKey(k.Item2)
                : !KwdTaggedRecords.TriggerPayloadSizes.ContainsKey(k.Item2));
    }
}
