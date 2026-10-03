// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdEffectElementWriterTests
{
    [Fact]
    public void EveryDecodedFieldRoundTripsWithoutChangingOtherBytes() =>
        KwdScalarOverlayChecks.Check(KwdRecordType.EffectElements, 182, Read, KwdAdditionalRecords.WriteEffectElementRecord,
            "Name:0:32 ArtResource:20:84 Mass:74:4 AirFriction:78:4 Elasticity:7C:4 MinSpeedXy:80:4 MaxSpeedXy:84:4 " +
            "MinSpeedYz:88:4 MaxSpeedYz:8C:4 MinScale:90:4 MaxScale:94:4 ScaleRatio:98:4 Flags:9C:4 " +
            "EffectElementId:A0:2 MinHp:A2:2 MaxHp:A4:2 DeathElementId:A6:2 HitSolidElementId:A8:2 HitWaterElementId:AA:2 " +
            "HitLavaElementId:AC:2 Red:AE:1 Green:AF:1 Blue:B0:1 RandomColorIndex:B1:1 TableColorIndex:B2:1 " +
            "FadePercentage:B3:1 NextEffectId:B4:2");

    [Fact]
    public void EveryNestedArtFieldRoundTripsWithoutChangingOtherBytes() =>
        KwdNestedOverlayChecks.Art(KwdRecordType.EffectElements, 182, 32, Read, KwdAdditionalRecords.WriteEffectElementRecord,
            c => c.ArtResource, (c, art) => c with { ArtResource = art });

    [InstallFact]
    public void EveryShippedEffectElementTypedOverlayIsByteIdentical()
    {
        KwdChunk chunk = KwdReader.ReadFile(Path.Combine(Dk2InstallGate.Path, "Data", "editor", "EffectElements.kwd")).Single();
        var rows = KwdAdditionalRecords.ParseEffectElements([chunk]); var images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(596, rows.Count);
        for (int i = 0; i < rows.Count; i++) Assert.Equal(images[i].ToBytes(), KwdAdditionalRecords.WriteEffectElementRecord(rows[i], images[i]));
    }

    private static EffectElementDef Read(byte[] source) => KwdAdditionalRecords.ParseEffectElements([
        new KwdChunk { Type = KwdRecordType.EffectElements, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
