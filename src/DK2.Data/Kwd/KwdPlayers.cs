// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>
/// Parser for PLAYERS(180) records: one per player slot, 205 bytes each, in a
/// level's <c>&lt;name&gt;Players.kld</c>.
///
/// <para><b>Stride CONFIRMED from data:</b> every one of the 66 shipped files
/// divides exactly (<c>contentSize / itemCount = 205</c>, item counts 3–7, 274
/// records in all). <b>Field boundaries STRONGLY INFERRED</b> from the retail
/// loader that unpacks this record: gold at 0, the AI flag at 4, the 158-byte
/// parameter block at 8, then trigger id, player id, camera and a 32-byte name.
/// The block's byte→field map is the one the loader applies, listed with
/// consumers and shipped ranges in <c>KEEPER_AI.md</c> §2.2 on the
/// <c>claude/keeper-ai</c> branch.</para>
///
/// <para><b>Reads the way the game reads.</b> Where the editor wrote a 16-bit
/// value and the game consumes one byte, this parser takes the byte; where the
/// editor wrote a 32-bit boolean, non-zero is true. The bytes the game never
/// reads (<c>0x05–0x07</c>, <c>0x3A–0x41</c>, <c>0x84–0x85</c> and the high bytes
/// of the three u16s) hold uninitialised editor memory in most shipped records
/// and are left alone; they remain reachable through
/// <see cref="KeeperAIParameters.RawBlock"/>.</para>
/// </summary>
public static class KwdPlayers
{
    /// <summary>Bytes per player record.</summary>
    public const int RecordSize = 205;

    /// <summary>Bytes in the AI parameter block at record offset 8.</summary>
    public const int BlockSize = 158;

    private const int OffsetGold = 0x00;
    private const int OffsetAiFlag = 0x04;
    private const int OffsetBlock = 0x08;
    private const int OffsetTriggerId = 0xA6;
    private const int OffsetPlayerId = 0xA8;
    private const int OffsetCameraX = 0xA9;
    private const int OffsetCameraY = 0xAB;
    private const int OffsetName = 0xAD;
    private const int NameSize = 32;

    public static List<PlayerDef> Parse(IEnumerable<KwdChunk> chunks)
    {
        var result = new List<PlayerDef>();
        foreach (KwdChunk chunk in chunks)
        {
            if (chunk.Type != KwdRecordType.Players) continue;

            if (chunk.ItemCount == 0 || chunk.Content.Length != RecordSize * chunk.ItemCount)
                throw new InvalidDataException(
                    $"PLAYERS content of {chunk.Content.Length} bytes does not divide into " +
                    $"{chunk.ItemCount} records of {RecordSize}.");

            for (int i = 0; i < chunk.ItemCount; i++)
                result.Add(ParseRecord(chunk.Content, i * RecordSize));
        }
        return result;
    }

    private static PlayerDef ParseRecord(byte[] rec, int b) => new()
    {
        StartingGold = BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + OffsetGold)),
        HasAiFlag = BinaryPrimitives.ReadInt32LittleEndian(rec.AsSpan(b + OffsetAiFlag)) != 0,
        Ai = ParseBlock(rec, b + OffsetBlock),
        TriggerId = BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(b + OffsetTriggerId)),
        PlayerId = rec[b + OffsetPlayerId],
        CameraX = BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(b + OffsetCameraX)),
        CameraY = BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(b + OffsetCameraY)),
        Name = Name(rec, b + OffsetName),
    };

    /// <summary>Decodes the 158-byte block exactly as the retail unpacker reads it.</summary>
    public static KeeperAIParameters ParseBlock(byte[] rec, int b)
    {
        if (b < 0 || b + BlockSize > rec.Length)
            throw new ArgumentOutOfRangeException(nameof(b), "AI block runs past the record.");

        byte[] s = rec.AsSpan(b, BlockSize).ToArray();
        byte U8(int off) => s[off];
        ushort U16(int off) => BinaryPrimitives.ReadUInt16LittleEndian(s.AsSpan(off));
        int I32(int off) => BinaryPrimitives.ReadInt32LittleEndian(s.AsSpan(off));
        bool B32(int off) => I32(off) != 0;

        return new KeeperAIParameters
        {
            EditorAiType = U8(0x00),
            TendancySpeed = U8(0x01),
            Openness = U8(0x02),
            RemoveCallToArmsOwnCreatures = U8(0x03),
            BuildLostRoomAfterMinimumTime = U8(0x04),
            CreateEmptyRoomShapeAreasWhenIdle = B32(0x08),
            AutomaticallyBuildBiggerLairWhenPortalTakenOver = B32(0x0C),
            SellRecentlyTakenOverRoom = B32(0x10),
            MinimumRandomTimeBeforePlacingReseachedRoom = U8(0x14),
            DefaultRoomSize = U8(0x15),
            TilesLeftBetweenRooms = U8(0x16),
            PlaceRoomsNextToIdeal = U8(0x17) != 0,
            CorridorStyle = U8(0x18),
            BuildNewRoomWhenMoreSpaceRequired = U8(0x19),
            NeutralRoomRangeFromHeart = U8(0x1A),
            RoomList = s.AsSpan(0x1B, 15).ToArray(),
            Flexibility = U8(0x2A),
            NeutralRoomRangeFromClaimedArea = U8(0x2B),
            RemoveCallToArmsAfter = U16(0x2C),
            BoulderTrapsOnLongCorridors = B32(0x2E),
            BoulderTrapOnBreachRoute = B32(0x32),
            AggressiveVsDefensiveTrapPreference = U8(0x36),
            TrapVsDoorPreference = U8(0x37),
            DoorPreference = U8(0x38),
            TrapAndDoorUsage = U8(0x39),
            IAAllRoomsPlaced = B32(0x42),
            IAAllSpellsResearched = (byte)I32(0x46),
            IAOnlyAttackAttackers = B32(0x4A),
            NeverAttack = B32(0x4E),
            IACreatureSkillLevel = U8(0x52),
            IAThreatSuperiority = U8(0x53),
            IAFirstEnd = U8(0x54),
            IAFirstStart = U8(0x55),
            BreachAtXPoints = U8(0x56),
            IAUseCreatures = U8(0x57),
            ManaValue = U16(0x58),
            BACallToArmsValue = U16(0x5A),
            RemoveCallToArmsEnemyCreatures = U8(0x5C),
            RemoveCallToArmsWithinRange = U8(0x5D),
            BARemoveIfCantReinforce = B32(0x5E),
            BAThreatSuperiority = U8(0x62),
            AttackingVsDefensiveSpellPreference = U8(0x63),
            ImprisonPercentage = U8(0x64),
            BARetreatHealth = U8(0x65),
            GoldValue = U16(0x66),
            UnhappyCreaturesHappy = B32(0x68),
            AngryCreaturesHappy = B32(0x6C),
            AngryCreaturesDisposeOf = B32(0x70),
            UnwantedCreatureDisposeOf = B32(0x74),
            AngryDisposalMethod = U8(0x78),
            MaximumImps = U8(0x79),
            Flags = U8(0x7A),
            IAMinimumCreatures = U8(0x7B),
            LightningInWater = B32(0x7C),
            UseSightOfEvil = U8(0x80),
            UseKeeperSpells = U8(0x81),
            SpellPreference = U8(0x82),
            CallToArmsUsage = U8(0x83),
            TendancyMoney = U16(0x86),
            WaitBeforeAttackingAgain = U8(0x88),   // editor u16, game reads the low byte
            StartingMana = I32(0x8A),
            SpecialRange = U8(0x8E),               // editor u16, game reads the low byte
            TilesPerImpRatio = U8(0x90),           // editor u16, game reads the low byte
            MaxBuildAreaMinX = U16(0x92),
            MaxBuildAreaMinY = U16(0x94),
            MaxBuildAreaMaxX = U16(0x96),
            MaxBuildAreaMaxY = U16(0x98),
            ProbabilityOfMovingCreatureForResearch = U8(0x9A),
            SpecialSearchChance = U8(0x9B),
            SpecialFindChance = U8(0x9C),
            ImprisonedCreatureAction = U8(0x9D),
            RawBlock = s,
        };
    }

    private static string Name(byte[] rec, int b)
    {
        ReadOnlySpan<byte> s = rec.AsSpan(b, NameSize);
        int end = s.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? s : s[..end]);
    }
}
