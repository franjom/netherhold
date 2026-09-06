// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
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

        var expected = new Dictionary<string, byte>
        {
            ["Gas Trap"] = 58,       // SHOTS 58 is Gas Trap Cloud
            ["Boulder Trap"] = 26,   // SHOTS 26 is Boulder
            ["Sentry Trap"] = 44,    // SHOTS 44 is Cannon Ball
            ["Alarm Trap"] = 13,     // SHOTS 13 is Alarm
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
}
