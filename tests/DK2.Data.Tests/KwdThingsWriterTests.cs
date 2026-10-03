// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdThingsWriterTests
{
    public static TheoryData<int, int, string> Layouts => new()
    {
        {194,24,"X:0:4 Y:4:4 Unknown_0008:8:4 KeeperSpellId:C:4 MoneyAmount:10:4 TriggerId:14:2 ObjectId:16:1 PlayerId:17:1"},
        {195,16,"X:0:4 Y:4:4 Unknown_0008:8:4 NumberOfShots:C:1 TrapId:D:1 PlayerId:E:1 Unknown_000F:F:1"},
        {196,20,"X:0:4 Y:4:4 Unknown_0008:8:4 TriggerId:C:2 DoorId:E:1 PlayerId:F:1 Flags:10:1 Unknown_0011:11:3"},
        {197,56,"StartX:0:4 StartY:4:4 EndX:8:4 EndY:C:4 WaitDelay:10:2 Flags:12:2 TriggerId:14:2 ActionPointId:16:1 NextWaypointId:17:1 Name:18:32"},
        {198,24,"X:0:4 Y:4:4 Z:8:4 GoldHeld:C:2 Level:E:1 Flags:F:1 InitialHealth:10:4 TriggerId:14:2 CreatureId:16:1 Unknown_0017:17:1"},
        {199,32,"X:0:4 Y:4:4 Z:8:4 GoldHeld:C:2 Level:E:1 Flags:F:1 ObjectiveTargetActionPointId:10:4 InitialHealth:14:4 TriggerId:18:2 ObjectiveTargetPlayerId:1A:1 Objective:1B:1 CreatureId:1C:1 WanderRadius:1D:1 PartyId:1E:1 Flags2:1F:1"},
        {200,28,"X:0:4 Y:4:4 Z:8:4 GoldHeld:C:2 Level:E:1 Flags:F:1 InitialHealth:10:4 ObjectiveTargetActionPointId:14:4 TriggerId:18:2 CreatureId:1A:1 PlayerId:1B:1"},
        {201,555,"Name:0:32 TriggerId:20:2 PartyId:22:1 Unknown_0023:23:4 Unknown_0027:27:4"},
        {203,36,"X:0:4 Y:4:4 Unknown_0008:8:4 Unknown_000C:C:4 Unknown_0010:10:2 Unknown_0012:12:2 EffectIds:14:8 Frequency:1C:1 GeneratorId:1D:1 Unknown_001E:1E:6"},
        {204,20,"X:0:4 Y:4:4 Unknown_0008:8:4 Unknown_000C:C:2 Direction:E:1 Unknown_000F:F:1 InitialHealth:10:2 RoomType:12:1 PlayerId:13:1"},
        {205,84,"X:0:4 Y:4:4 Z:8:4 MinClipX:C:4 MinClipY:10:4 MinClipZ:14:4 MaxClipX:18:4 MaxClipY:1C:4 MaxClipZ:20:4 ViewDistance:24:4 MinViewDistance:28:4 MaxViewDistance:2C:4 Zoom:30:4 MinZoom:34:4 MaxZoom:38:4 Lens:3C:4 MinLens:40:4 MaxLens:44:4 Flags:48:4 AngleYaw:4C:2 AngleRoll:4E:2 AnglePitch:50:2 CameraId:52:2"},
    };

    [Theory, MemberData(nameof(Layouts))]
    public void EveryDecodedFieldRoundTripsWithoutChangingOtherBytes(int tag, int size, string fields) =>
        Check(tag, size, fields, tag == 201 ? ["Members"] : null);

    [Fact]
    public void HypothesizedDeadBodyLayoutRoundTripsSyntheticFields() =>
        Check(202,16,"X:0:4 Y:4:4 Z:8:4 GoldHeld:C:2 CreatureId:E:1 PlayerId:F:1");

    [Fact]
    public void EveryHeroPartyMemberFieldUsesItsOwnSlot()
    {
        string fields = Layouts.Single(r => (int)r[0] == 199)[2].ToString()!;
        for (int slot = 0; slot < 16; slot++)
        {
            int at = 43 + slot * 32;
            KwdScalarOverlayChecks.Check(KwdRecordType.Things,32, bytes => KwdThings.Parse(199,bytes), (row, image) =>
            {
                byte[] source = Enumerable.Repeat((byte)0x5A,555).ToArray();
                image.ToBytes().CopyTo(source, at);
                var party = (HeroPartyThing)KwdThings.Parse(201,source);
                GoodCreatureThing[] members = party.Members.ToArray(); members[slot] = (GoodCreatureThing)row;
                byte[] result = KwdThings.WriteRecord(party with { Members = members }, Image(201,source));
                Assert.Equal(Image(201,source).ToBytes()[..8],result[..8]);
                for (int i=0;i<555;i++) if (i<at || i>=at+32) Assert.Equal(source[i],result[i+8]);
                return result[(at+8)..(at+40)];
            },fields);
        }
    }

    [Fact]
    public void InvalidTemplatesAndVariableLengthPayloadsAreRefused()
    {
        var obj = KwdThings.Parse(194,new byte[24]);
        Assert.Throws<InvalidDataException>(() => KwdThings.WriteRecord(obj,Image(195,new byte[16])));
        Assert.Throws<InvalidDataException>(() => KwdThings.WriteRecord((ObjectThing)obj with { Unknown_0008 = new byte[3] },Image(194,new byte[24])));
        var party=(HeroPartyThing)KwdThings.Parse(201,new byte[555]);
        Assert.Throws<InvalidDataException>(() => KwdThings.WriteRecord(party with { Members = [] },Image(201,new byte[555])));
        var generator=(EffectGeneratorThing)KwdThings.Parse(203,new byte[36]);
        Assert.Throws<InvalidDataException>(() => KwdThings.WriteRecord(generator with { EffectIds = [] },Image(203,new byte[36])));
        Assert.Throws<InvalidDataException>(() => KwdThings.WriteRecord(obj,new KwdRecordImage(KwdRecordType.Things,new byte[32],194)));
    }

    [InstallFact]
    public void EveryShippedThingTypedOverlayIsByteIdentical()
    {
        int count=0;
        foreach(string file in Directory.EnumerateFiles(Path.Combine(Dk2InstallGate.Path,"Data"),"*",SearchOption.AllDirectories)
            .Where(p=>p.EndsWith(".kwd",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".kld",StringComparison.OrdinalIgnoreCase)))
        foreach(KwdChunk chunk in KwdReader.ReadFile(file).Where(c=>c.Type==KwdRecordType.Things))
        {
            var rows=KwdThings.Parse([chunk]); var images=KwdRecordRoundTrip.Split(chunk);
            for(int i=0;i<rows.Count;i++) Assert.Equal(images[i].ToBytes(),KwdThings.WriteRecord(rows[i],images[i]));
            count+=rows.Count;
        }
        Assert.Equal(6318,count);
    }

    private static void Check(int tag,int size,string fields,string[]? excluded=null) =>
        KwdScalarOverlayChecks.Check(KwdRecordType.Things,size,bytes=>KwdThings.Parse(tag,bytes),
            (row,image)=>KwdThings.WriteRecord(row,Image(tag,image.ToBytes()))[8..],fields,excluded);

    internal static KwdRecordImage Image(int tag,byte[] payload)
    {
        byte[] bytes=new byte[payload.Length+8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes,tag); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),payload.Length);
        payload.CopyTo(bytes,8); return new KwdRecordImage(KwdRecordType.Things,bytes,tag);
    }
}
