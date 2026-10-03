// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

public static partial class KwdPlayers
{
    /// <summary>Overlays decoded player and AI fields, retaining unknown bytes and unchanged
    /// boolean encodings. RawBlock must match the explicit source image.</summary>
    public static byte[] WriteRecord(PlayerDef player, KwdRecordImage template)
    {
        byte[] image = KwdOverlay.Image(template, KwdRecordType.Players, RecordSize);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(OffsetGold), player.StartingGold);
        WriteBoolean(image, OffsetAiFlag, player.HasAiFlag, 4);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(OffsetTriggerId), player.TriggerId);
        image[OffsetPlayerId] = player.PlayerId;
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(OffsetCameraX), player.CameraX);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(OffsetCameraY), player.CameraY);
        KwdOverlay.Ascii(image, OffsetName, NameSize, player.Name, Name(image, OffsetName), trimmed: false);
        WriteBlock(image, player.Ai);
        return image;
    }

    private static void WriteBlock(byte[] image, KeeperAIParameters row)
    {
        if (row is null || !row.RawBlock.Span.SequenceEqual(image.AsSpan(OffsetBlock, BlockSize)))
            throw new InvalidDataException("PLAYERS AI RawBlock must match its explicit template; edit typed fields instead.");
        if (row.RoomList is null || row.RoomList.Count != 15)
            throw new InvalidDataException("PLAYERS AI RoomList requires exactly 15 bytes.");
        void U8(int offset, byte value) => image[OffsetBlock + offset] = value;
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(OffsetBlock + offset), value);
        void I32(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(OffsetBlock + offset), value);
        void B32(int offset, bool value) => WriteBoolean(image, OffsetBlock + offset, value, 4);
        U8(0x00, row.EditorAiType);
        U8(0x01, row.TendancySpeed);
        U8(0x02, row.Openness);
        U8(0x03, row.RemoveCallToArmsOwnCreatures);
        U8(0x04, row.BuildLostRoomAfterMinimumTime);
        B32(0x08, row.CreateEmptyRoomShapeAreasWhenIdle);
        B32(0x0C, row.AutomaticallyBuildBiggerLairWhenPortalTakenOver);
        B32(0x10, row.SellRecentlyTakenOverRoom);
        U8(0x14, row.MinimumRandomTimeBeforePlacingReseachedRoom);
        U8(0x15, row.DefaultRoomSize);
        U8(0x16, row.TilesLeftBetweenRooms);
        WriteBoolean(image, OffsetBlock + 0x17, row.PlaceRoomsNextToIdeal, 1);
        U8(0x18, row.CorridorStyle);
        U8(0x19, row.BuildNewRoomWhenMoreSpaceRequired);
        U8(0x1A, row.NeutralRoomRangeFromHeart);
        for (int i = 0; i < 15; i++) U8(0x1B + i, row.RoomList[i]);
        U8(0x2A, row.Flexibility);
        U8(0x2B, row.NeutralRoomRangeFromClaimedArea);
        U16(0x2C, row.RemoveCallToArmsAfter);
        B32(0x2E, row.BoulderTrapsOnLongCorridors);
        B32(0x32, row.BoulderTrapOnBreachRoute);
        U8(0x36, row.AggressiveVsDefensiveTrapPreference);
        U8(0x37, row.TrapVsDoorPreference);
        U8(0x38, row.DoorPreference);
        U8(0x39, row.TrapAndDoorUsage);
        B32(0x42, row.IAAllRoomsPlaced);
        U8(0x46, row.IAAllSpellsResearched);
        B32(0x4A, row.IAOnlyAttackAttackers);
        B32(0x4E, row.NeverAttack);
        U8(0x52, row.IACreatureSkillLevel);
        U8(0x53, row.IAThreatSuperiority);
        U8(0x54, row.IAFirstEnd);
        U8(0x55, row.IAFirstStart);
        U8(0x56, row.BreachAtXPoints);
        U8(0x57, row.IAUseCreatures);
        U16(0x58, row.ManaValue);
        U16(0x5A, row.BACallToArmsValue);
        U8(0x5C, row.RemoveCallToArmsEnemyCreatures);
        U8(0x5D, row.RemoveCallToArmsWithinRange);
        B32(0x5E, row.BARemoveIfCantReinforce);
        U8(0x62, row.BAThreatSuperiority);
        U8(0x63, row.AttackingVsDefensiveSpellPreference);
        U8(0x64, row.ImprisonPercentage);
        U8(0x65, row.BARetreatHealth);
        U16(0x66, row.GoldValue);
        B32(0x68, row.UnhappyCreaturesHappy);
        B32(0x6C, row.AngryCreaturesHappy);
        B32(0x70, row.AngryCreaturesDisposeOf);
        B32(0x74, row.UnwantedCreatureDisposeOf);
        U8(0x78, row.AngryDisposalMethod);
        U8(0x79, row.MaximumImps);
        U8(0x7A, row.Flags);
        U8(0x7B, row.IAMinimumCreatures);
        B32(0x7C, row.LightningInWater);
        U8(0x80, row.UseSightOfEvil);
        U8(0x81, row.UseKeeperSpells);
        U8(0x82, row.SpellPreference);
        U8(0x83, row.CallToArmsUsage);
        U16(0x86, row.TendancyMoney);
        U8(0x88, row.WaitBeforeAttackingAgain);
        I32(0x8A, row.StartingMana);
        U8(0x8E, row.SpecialRange);
        U8(0x90, row.TilesPerImpRatio);
        U16(0x92, row.MaxBuildAreaMinX);
        U16(0x94, row.MaxBuildAreaMinY);
        U16(0x96, row.MaxBuildAreaMaxX);
        U16(0x98, row.MaxBuildAreaMaxY);
        U8(0x9A, row.ProbabilityOfMovingCreatureForResearch);
        U8(0x9B, row.SpecialSearchChance);
        U8(0x9C, row.SpecialFindChance);
        U8(0x9D, row.ImprisonedCreatureAction);
    }

    private static void WriteBoolean(byte[] image, int offset, bool value, int width)
    {
        bool original = width == 1 ? image[offset] != 0
            : BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(offset)) != 0;
        if (value == original) return;
        if (width == 1) image[offset] = value ? (byte)1 : (byte)0;
        else BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(offset), value ? 1 : 0);
    }
}
