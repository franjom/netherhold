// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Data;
using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Behavior.Tests;

/// <summary>
/// Semantic anchors for the DOORS and TRAPS parsers. The prices are printed in
/// the original manual; shot ids independently resolve to matching SHOTS rows.
/// </summary>
public class DoorsAndTrapsDefinitionsOnRealDataTests
{
    private static GameDatabase? Database() =>
        Dk2Install.Find() is { } install ? GameDatabaseLoader.Load(install) : null;

    [Fact]
    public void EveryKeeperDoorPriceMatchesTheOriginalManual()
    {
        if (Database() is not { } database) return;

        var prices = new Dictionary<string, ushort>
        {
            ["Wooden Door"] = 500,
            ["Braced Door"] = 1000,
            ["Steel Door"] = 1500,
            ["Magic Door"] = 6000,
            ["Secret Door"] = 3000,
            ["Barricade"] = 400,
        };

        foreach ((string name, ushort price) in prices)
            Assert.Equal(price, database.Doors.Values.Single(d => d.Name == name).GoldCost);
    }

    [Fact]
    public void EveryManualTrapPriceMatchesTheShippedColumn()
    {
        if (Database() is not { } database) return;

        var prices = new Dictionary<string, ushort>
        {
            ["Sentry Trap"] = 1000,
            ["Fear Trap"] = 750,
            ["Alarm Trap"] = 500,
            ["Gas Trap"] = 600,
            ["Guard Post"] = 300,
            ["Spike Trap"] = 750,
            ["Trigger Trap"] = 300,
            ["Boulder Trap"] = 1500,
            ["Freeze Trap"] = 1500,
            ["Lightning Trap"] = 3000,
            ["Fireburst Trap"] = 6000,
        };

        foreach ((string name, ushort price) in prices)
            Assert.Equal(price, database.Traps.Values.Single(t => t.Name == name).GoldCost);
    }

    [Fact]
    public void TrapShotIdsNameTheEffectEachTrapEmits()
    {
        if (Database() is not { } database) return;

        // The numbers are SHOTS *record ids*, not row indices - see
        // ATrapsShotIdIsAShotsRecordIdAndNotItsRowIndex below.
        var expected = new Dictionary<string, byte>
        {
            ["Gas Trap"] = 58,       // SHOTS id 58 is Gas Trap Cloud
            ["Boulder Trap"] = 26,   // SHOTS id 26 is Boulder
            ["Sentry Trap"] = 44,    // SHOTS id 44 is Cannon Ball
            ["Alarm Trap"] = 13,     // SHOTS id 13 is Alarm
            ["Fireburst Trap"] = 14,
            ["Lightning Trap"] = 59,
            ["Freeze Trap"] = 45,
            ["Fear Trap"] = 25,
            ["Trigger Trap"] = 39,
            ["Spike Trap"] = 27,
            ["Guard Post"] = 0,
        };

        foreach ((string name, byte shotId) in expected)
            Assert.Equal(shotId, database.Traps.Values.Single(t => t.Name == name).ShotTypeId);
    }

    [Fact]
    public void HealthAndManufactureColumnsArePlausibleAcrossEveryRecord()
    {
        if (Database() is not { } database) return;

        Assert.Equal(
            new ushort[] { 2000, 8000, 6000, 12000, 3000, 3000, 3000, 8000 },
            database.Doors.OrderBy(e => e.Key).Select(e => e.Value.Health));
        Assert.Equal(
            new uint[] { 3000, 9000, 6000, 12000, 9000, 1500, 1500, 9000 },
            database.Doors.OrderBy(e => e.Key).Select(e => e.Value.ManufactureRequired));

        Assert.Equal(
            new ushort[]
            {
                1500, 6000, 1500, 1500, 12000, 9000, 9000, 1500,
                1500, 6000, 1500, 9000, 24000, 1500, 1500, 20000,
            },
            database.Traps.OrderBy(e => e.Key).Select(e => e.Value.ManufactureRequired));
    }

    /// <summary>
    /// <b>A trap's shot id is a SHOTS record id, not a row index</b>, and the
    /// two differ by one.
    ///
    /// <para>SHOTS carries its own id byte at <c>+0xBE</c> — the only offset in
    /// the 239-byte record whose value equals the row number plus one for all
    /// 65 rows — and every trap's <c>ShotTypeId</c> resolves through it. Read
    /// the table by row instead and Gas Trap fires <i>Lightning Trap Shot</i>,
    /// Boulder Trap fires <i>Spike</i>, and Sentry Trap fires
    /// <i>Freeze Trap Shot</i>: all plausible, all wrong, and nothing would have
    /// failed.</para>
    ///
    /// <para>Nothing parses SHOTS yet — it arrives at M6 — so this walks the
    /// bytes the way <c>TrapsOnRealDataTests</c> did before TRAPS had a parser.
    /// It is here because a convention that lives only in a comment is one that
    /// gets broken by the first person to index an array with it.</para>
    /// </summary>
    [Fact]
    public void ATrapsShotIdIsAShotsRecordIdAndNotItsRowIndex()
    {
        if (Database() is not { } database) return;
        string install = Dk2Install.Find()!;

        const int recordSize = 239;
        const int offsetShotId = 0xBE;

        KwdChunk shots = KwdReader
            .ReadFile(Path.Combine(install, "Data", "editor", "Shots.kwd"))
            .Single(c => c.Type == KwdRecordType.Shots);

        Assert.Equal(0, shots.Content.Length % recordSize);

        var nameById = new Dictionary<byte, string>();
        for (int row = 0; row < shots.ItemCount; row++)
        {
            int at = row * recordSize;
            ReadOnlySpan<byte> name = shots.Content.AsSpan(at, 32);
            int end = name.IndexOf((byte)0);

            byte id = shots.Content[at + offsetShotId];

            // The id is the row plus one. Stated as an assertion because it is
            // the whole point of the test.
            Assert.Equal(row + 1, id);

            nameById[id] = Encoding.ASCII.GetString(end < 0 ? name : name[..end]).Trim();
        }

        var expected = new Dictionary<string, string>
        {
            ["Gas Trap"] = "Gas Trap Cloud",
            ["Boulder Trap"] = "Boulder",
            ["Sentry Trap"] = "Cannon Ball",
            ["Alarm Trap"] = "Alarm",
            ["Fireburst Trap"] = "Fireburst",
            ["Lightning Trap"] = "Lightning Trap Shot",
            ["Freeze Trap"] = "Freeze Trap Shot",
            ["Fear Trap"] = "Fear",
            ["Trigger Trap"] = "Trigger Trap",
            ["Spike Trap"] = "Spike",
            ["Jack In The Box"] = "Jack In The Box",
        };

        foreach ((string trapName, string shotName) in expected)
        {
            TrapDef trap = database.Traps.Values.Single(t => t.Name == trapName);
            Assert.Equal(shotName, nameById[trap.ShotTypeId]);
        }

        // Guard Post is the one that fires nothing, and zero is not an id.
        Assert.Equal(0, database.Traps.Values.Single(t => t.Name == "Guard Post").ShotTypeId);
        Assert.DoesNotContain((byte)0, nameById.Keys);
    }
}
