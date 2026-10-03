// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdShotWriterTests
{
    [Fact]
    public void EveryDecodedFieldRoundTripsWithoutChangingOtherBytes() =>
        KwdScalarOverlayChecks.Check(KwdRecordType.Shots, 239, Read, KwdAdditionalRecords.WriteShotRecord,
            "Name:0:32 MeshResource:20:84 Light:74:24 AirFriction:8C:4 Mass:90:4 Speed:94:4 Unknown_0098:98:4 " +
            "Unknown_009C:9C:4 ShotProcessFlags:A0:4 Radius:A4:4 Flags:A8:4 GeneralEffectId:AC:2 CreationEffectId:AE:2 " +
            "DeathEffectId:B0:2 TimedEffectId:B2:2 HitSolidEffectId:B4:2 HitLavaEffectId:B6:2 HitWaterEffectId:B8:2 " +
            "HitThingEffectId:BA:2 Health:BC:2 ShotId:BE:1 DeathShotId:BF:1 TimedDelay:C0:1 HitSolidShotId:C1:1 " +
            "HitLavaShotId:C2:1 HitWaterShotId:C3:1 HitThingShotId:C4:1 DamageType:C5:1 CollideType:C6:1 ProcessType:C7:1 " +
            "AttackCategory:C8:1 SoundCategory:C9:32 Threat:E9:2 BurnDuration:EB:4");

    [Fact]
    public void EveryNestedArtFieldRoundTripsWithoutChangingOtherBytes() =>
        KwdNestedOverlayChecks.Art(KwdRecordType.Shots, 239, 32, Read, KwdAdditionalRecords.WriteShotRecord,
            c => c.MeshResource, (c, art) => c with { MeshResource = art });

    [Fact]
    public void EveryNestedLightFieldRoundTripsWithoutChangingOtherBytes() =>
        KwdNestedOverlayChecks.Light(KwdRecordType.Shots, 239, 116, Read, KwdAdditionalRecords.WriteShotRecord,
            c => c.Light, (c, light) => c with { Light = light });

    [InstallFact]
    public void EveryShippedShotTypedOverlayIsByteIdentical()
    {
        KwdChunk chunk = KwdReader.ReadFile(Path.Combine(Dk2InstallGate.Path, "Data", "editor", "Shots.kwd")).Single();
        var rows = KwdAdditionalRecords.ParseShots([chunk]); var images = KwdRecordRoundTrip.Split(chunk);
        Assert.Equal(65, rows.Count);
        for (int i = 0; i < rows.Count; i++) Assert.Equal(images[i].ToBytes(), KwdAdditionalRecords.WriteShotRecord(rows[i], images[i]));
    }

    private static ShotDef Read(byte[] source) => KwdAdditionalRecords.ParseShots([
        new KwdChunk { Type = KwdRecordType.Shots, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
