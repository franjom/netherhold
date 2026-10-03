// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdPlayersWriterTests
{
    [Fact]
    public void EveryDecodedPlayerFieldRoundTripsWithoutChangingOtherBytes() => Check<PlayerDef>(
        "PlayerId:A8:1 Name:AD:32 StartingGold:0:4 HasAiFlag:4:4 TriggerId:A6:2 CameraX:A9:2 CameraY:AB:2",
        p => p, (p, changed) => changed, excluded: "Ai");

    [Fact]
    public void EveryDecodedAiFieldRoundTripsWithoutChangingOtherBytes() => Check<KeeperAIParameters>(
        "EditorAiType:0:1 TendancySpeed:1:1 Openness:2:1 RemoveCallToArmsOwnCreatures:3:1 " +
        "BuildLostRoomAfterMinimumTime:4:1 CreateEmptyRoomShapeAreasWhenIdle:8:4 " +
        "AutomaticallyBuildBiggerLairWhenPortalTakenOver:C:4 SellRecentlyTakenOverRoom:10:4 " +
        "MinimumRandomTimeBeforePlacingReseachedRoom:14:1 DefaultRoomSize:15:1 TilesLeftBetweenRooms:16:1 " +
        "PlaceRoomsNextToIdeal:17:1 CorridorStyle:18:1 BuildNewRoomWhenMoreSpaceRequired:19:1 " +
        "NeutralRoomRangeFromHeart:1A:1 RoomList:1B:15 Flexibility:2A:1 NeutralRoomRangeFromClaimedArea:2B:1 " +
        "RemoveCallToArmsAfter:2C:2 BoulderTrapsOnLongCorridors:2E:4 BoulderTrapOnBreachRoute:32:4 " +
        "AggressiveVsDefensiveTrapPreference:36:1 TrapVsDoorPreference:37:1 DoorPreference:38:1 TrapAndDoorUsage:39:1 " +
        "IAAllRoomsPlaced:42:4 IAAllSpellsResearched:46:1 IAOnlyAttackAttackers:4A:4 NeverAttack:4E:4 " +
        "IACreatureSkillLevel:52:1 IAThreatSuperiority:53:1 IAFirstEnd:54:1 IAFirstStart:55:1 " +
        "BreachAtXPoints:56:1 IAUseCreatures:57:1 ManaValue:58:2 BACallToArmsValue:5A:2 " +
        "RemoveCallToArmsEnemyCreatures:5C:1 RemoveCallToArmsWithinRange:5D:1 BARemoveIfCantReinforce:5E:4 " +
        "BAThreatSuperiority:62:1 AttackingVsDefensiveSpellPreference:63:1 ImprisonPercentage:64:1 BARetreatHealth:65:1 " +
        "GoldValue:66:2 UnhappyCreaturesHappy:68:4 AngryCreaturesHappy:6C:4 AngryCreaturesDisposeOf:70:4 " +
        "UnwantedCreatureDisposeOf:74:4 AngryDisposalMethod:78:1 MaximumImps:79:1 Flags:7A:1 IAMinimumCreatures:7B:1 " +
        "LightningInWater:7C:4 UseSightOfEvil:80:1 UseKeeperSpells:81:1 SpellPreference:82:1 CallToArmsUsage:83:1 " +
        "TendancyMoney:86:2 WaitBeforeAttackingAgain:88:1 StartingMana:8A:4 SpecialRange:8E:1 TilesPerImpRatio:90:1 " +
        "MaxBuildAreaMinX:92:2 MaxBuildAreaMinY:94:2 MaxBuildAreaMaxX:96:2 MaxBuildAreaMaxY:98:2 " +
        "ProbabilityOfMovingCreatureForResearch:9A:1 SpecialSearchChance:9B:1 SpecialFindChance:9C:1 ImprisonedCreatureAction:9D:1",
        p => p.Ai, (p, changed) => p with { Ai = changed }, excluded: "RawBlock", baseOffset: 8);

    [Fact]
    public void RefusesRawAiBlockEditsAndWrongRoomListSize()
    {
        byte[] source = Source();
        PlayerDef player = Read(source);
        var image = new KwdRecordImage(KwdRecordType.Players, source);
        byte[] changed = player.Ai.RawBlock.ToArray(); changed[5]++;
        Assert.Throws<InvalidDataException>(() => KwdPlayers.WriteRecord(player with {
            Ai = player.Ai with { RawBlock = changed } }, image));
        Assert.Throws<InvalidDataException>(() => KwdPlayers.WriteRecord(player with {
            Ai = player.Ai with { RoomList = new byte[14] } }, image));
        Assert.Throws<InvalidDataException>(() => KwdPlayers.WriteRecord(player,
            new KwdRecordImage(KwdRecordType.Players, new byte[204])));
    }

    [InstallFact]
    public void EveryShippedPlayerTypedOverlayIsByteIdentical()
    {
        string maps = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "maps");
        int count = 0;
        foreach (string file in Directory.GetFiles(maps, "*Players.kld"))
        {
            KwdChunk chunk = KwdReader.ReadFile(file).Single();
            var players = KwdPlayers.Parse([chunk]);
            var images = KwdRecordRoundTrip.Split(chunk);
            for (int i = 0; i < players.Count; i++)
                Assert.Equal(images[i].ToBytes(), KwdPlayers.WriteRecord(players[i], images[i]));
            count += players.Count;
        }
        Assert.Equal(274, count);
    }

    private static void Check<T>(string fieldList, Func<PlayerDef, T> select,
        Func<PlayerDef, T, PlayerDef> replace, string excluded, int baseOffset = 0) where T : class
    {
        byte[] source = Source();
        var image = new KwdRecordImage(KwdRecordType.Players, source);
        var fields = fieldList.Split(' ').Select(f => f.Split(':')).ToDictionary(f => f[0],
            f => (Offset: baseOffset + Convert.ToInt32(f[1], 16), Width: int.Parse(f[2])));
        PropertyInfo[] properties = typeof(T).GetProperties().Where(p => p.SetMethod is not null && p.Name != excluded).ToArray();
        Assert.Equal(properties.Length, fields.Count);
        Assert.Equal(source, KwdPlayers.WriteRecord(Read(source), image));
        foreach (PropertyInfo property in properties)
        {
            PlayerDef player = Read(source);
            T changed = select(player);
            object value = property.PropertyType == typeof(string) ? "edited"
                : property.PropertyType == typeof(int) ? -12345
                : property.PropertyType == typeof(ushort) ? (ushort)1234
                : property.PropertyType == typeof(byte) ? (byte)13
                : property.PropertyType == typeof(bool) ? !(bool)property.GetValue(changed)!
                : property.PropertyType == typeof(IReadOnlyList<byte>) ? Enumerable.Range(1, 15).Select(i => (byte)i).ToArray()
                : throw new InvalidOperationException(property.Name);
            property.SetValue(changed, value);
            byte[] written = KwdPlayers.WriteRecord(replace(player, changed), image);
            object? reread = property.GetValue(select(Read(written)));
            if (value is IEnumerable<byte> bytes) Assert.Equal(bytes, Assert.IsAssignableFrom<IEnumerable<byte>>(reread));
            else Assert.Equal(value, reread);
            Assert.False(source.SequenceEqual(written));
            var field = fields[property.Name];
            for (int i = 0; i < source.Length; i++)
                if (i < field.Offset || i >= field.Offset + field.Width) Assert.Equal(source[i], written[i]);
            if (property.PropertyType == typeof(bool))
            {
                PlayerDef falsePlayer = Read(written);
                T trueValue = select(falsePlayer);
                property.SetValue(trueValue, true);
                byte[] trueImage = KwdPlayers.WriteRecord(replace(falsePlayer, trueValue),
                    new KwdRecordImage(KwdRecordType.Players, written));
                Assert.True((bool)property.GetValue(select(Read(trueImage)))!);
                for (int i = 0; i < source.Length; i++)
                    if (i < field.Offset || i >= field.Offset + field.Width) Assert.Equal(written[i], trueImage[i]);
            }
        }
        Assert.Equal(source, image.ToBytes());
    }

    private static byte[] Source()
    {
        byte[] source = Enumerable.Repeat((byte)0x5A, 205).ToArray();
        source[0xAD] = (byte)'a'; source[0xAE] = 0;
        return source;
    }

    private static PlayerDef Read(byte[] source) => KwdPlayers.Parse([
        new KwdChunk { Type = KwdRecordType.Players, ItemCount = 1, Content = source, HeaderBlock = [] }]).Single();
}
