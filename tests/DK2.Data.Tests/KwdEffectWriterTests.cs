// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdEffectWriterTests
{
    [Fact]
    public void EveryDecodedFieldRoundTripsWithoutChangingOtherBytes() =>
        KwdScalarOverlayChecks.Check(KwdRecordType.Effects, 246, Read, KwdAdditionalRecords.WriteEffectRecord,
            "Name:0:32 ArtResource:20:84 Light:74:24 Mass:8C:4 AirFriction:90:4 Elasticity:94:4 Radius:98:4 " +
            "MinSpeedXy:9C:4 MaxSpeedXy:A0:4 MinSpeedYz:A4:4 MaxSpeedYz:A8:4 MinScale:AC:4 MaxScale:B0:4 Flags:B4:4 " +
            "EffectId:B8:2 MinHp:BA:2 MaxHp:BC:2 FadeDuration:BE:2 NextEffectId:C0:2 DeathEffectId:C2:2 " +
            "HitSolidEffectId:C4:2 HitWaterEffectId:C6:2 HitLavaEffectId:C8:2 GenerateIds:CA:16 OuterOriginRange:DA:2 " +
            "LowerHeightLimit:DC:2 UpperHeightLimit:DE:2 OrientationRange:E0:2 SpriteSpinRateRange:E2:2 WhirlpoolRate:E4:2 " +
            "DirectionalSpread:E6:2 CircularPathRate:E8:2 InnerOriginRange:EA:2 GenerateRandomness:EC:2 Unknown_00EE:EE:2 " +
            "Unknown_00F0:F0:2 GenerationType:F2:1 ElementsPerTurn:F3:1 Unknown_00F4:F4:2");

    [Fact]
    public void EveryNestedArtFieldRoundTripsWithoutChangingOtherBytes() =>
        KwdNestedOverlayChecks.Art(KwdRecordType.Effects, 246, 32, Read, KwdAdditionalRecords.WriteEffectRecord,
            c => c.ArtResource, (c, art) => c with { ArtResource = art });

    [Fact]
    public void EveryNestedLightFieldRoundTripsWithoutChangingOtherBytes() =>
        KwdNestedOverlayChecks.Light(KwdRecordType.Effects, 246, 116, Read, KwdAdditionalRecords.WriteEffectRecord,
            c => c.Light, (c, light) => c with { Light = light });

    [Fact]
    public void GenerateIdsRequireExactlyEightSlots()
    {
        var image = new KwdRecordImage(KwdRecordType.Effects, new byte[246]);
        Assert.Throws<InvalidDataException>(() => KwdAdditionalRecords.WriteEffectRecord(Read(image.ToBytes()) with { GenerateIds = new ushort[7] }, image));
    }

    [InstallFact]
    public void EveryShippedEffectTypedOverlayIsByteIdentical()
    {
        KwdChunk chunk = KwdReader.ReadFile(Path.Combine(Dk2InstallGate.Path, "Data", "editor", "Effects.kwd")).Single();
        var rows = KwdAdditionalRecords.ParseEffects([chunk]); var images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(370, rows.Count);
        for (int i = 0; i < rows.Count; i++) Assert.Equal(images[i].ToBytes(), KwdAdditionalRecords.WriteEffectRecord(rows[i], images[i]));
    }

    private static EffectDef Read(byte[] source) => KwdAdditionalRecords.ParseEffects([
        new KwdChunk { Type = KwdRecordType.Effects, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
