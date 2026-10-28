// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;

namespace DK2.Data.Model;

/// <summary>A player's 0x44-byte level-end statistics block as the profile keeps it (profile
/// <c>+0x8DD + 0x44 × Number</c>): twelve dwords, a byte, four dwords and three bytes. Field
/// meanings and the scores: docs/research/level-statistics.md.</summary>
public sealed record LevelStatisticsBlock
{
    public const int Dwords = 17;
    private static readonly uint[] EvilRatingWeights = { 1000, 40, 50, 5, 0, 5, 1, 2, 5, 20, 40, 50 };

    public uint DungeonHeartsDestroyed { get; init; }
    public uint EnemyCreaturesKilled { get; init; }
    public uint HeroesDestroyed { get; init; }
    public uint RoomsTakenOver { get; init; }
    public uint RoomExtraPoints { get; init; }
    public uint LandOwned { get; init; }
    public uint GoldMined { get; init; }
    public int ManaStored { get; init; }
    public uint ItemsManufactured { get; init; }
    public uint CreaturesCommanded { get; init; }
    public uint CreaturesConverted { get; init; }
    /// <summary>Mean creature level, 20.12.</summary>
    public uint CreatureTraining { get; init; }
    public bool LevelWon { get; init; }
    public uint OwnCreaturesKilled { get; init; }
    public uint MoneySpent { get; init; }
    public uint ThingsResearched { get; init; }
    /// <summary>Whole seconds.</summary>
    public uint TimeTaken { get; init; }
    public byte SpecialsFound { get; init; }
    public byte SpecialsMissed { get; init; }
    public byte SecretLevelsMissed { get; init; }

    /// <summary>The training value as the statistics row and the score read it: the integer part
    /// and <c>trunc(fraction × 100 / 4096)</c>, which the row prints unpadded ("2.5" for 2.05).</summary>
    public (uint Whole, uint Hundredths) TrainingParts => (CreatureTraining >> 12, (CreatureTraining & 0xFFF) * 100 / 4096);

    /// <summary>The modes 1 and 4 level-end sum (<c>0x004BF211</c>–<c>0x004BF2EA</c>, table <c>0x0069F300</c>):
    /// <c>RoomExtraPoints</c> unweighted, training as <c>whole × 50 + trunc(hundredths × 50 / 100)</c>.</summary>
    public uint TotalEvilRating
    {
        get
        {
            uint[] fields = { DungeonHeartsDestroyed, EnemyCreaturesKilled, HeroesDestroyed, RoomsTakenOver, 0,
                LandOwned, GoldMined, (uint)ManaStored, ItemsManufactured, CreaturesCommanded, CreaturesConverted };
            uint sum = RoomExtraPoints;
            for (int i = 0; i < fields.Length; i++) sum += fields[i] * EvilRatingWeights[i];
            (uint whole, uint hundredths) = TrainingParts;
            return sum + whole * EvilRatingWeights[11] + hundredths * EvilRatingWeights[11] / 100;
        }
    }

    public uint[] ToDwords()
    {
        var bytes = new byte[Dwords * 4];
        Span<byte> span = bytes;
        uint[] head = { DungeonHeartsDestroyed, EnemyCreaturesKilled, HeroesDestroyed, RoomsTakenOver, RoomExtraPoints,
            LandOwned, GoldMined, (uint)ManaStored, ItemsManufactured, CreaturesCommanded, CreaturesConverted, CreatureTraining };
        for (int i = 0; i < head.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(span[(i * 4)..], head[i]);
        bytes[0x30] = LevelWon ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x31..], OwnCreaturesKilled);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x35..], MoneySpent);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x39..], ThingsResearched);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x3D..], TimeTaken);
        bytes[0x41] = SpecialsFound; bytes[0x42] = SpecialsMissed; bytes[0x43] = SecretLevelsMissed;
        var dwords = new uint[Dwords];
        for (int i = 0; i < Dwords; i++) dwords[i] = BinaryPrimitives.ReadUInt32LittleEndian(span[(i * 4)..]);
        return dwords;
    }

    public static LevelStatisticsBlock FromDwords(IReadOnlyList<uint> source, int row)
    {
        var bytes = new byte[Dwords * 4];
        Span<byte> span = bytes;
        for (int i = 0; i < Dwords; i++) BinaryPrimitives.WriteUInt32LittleEndian(span[(i * 4)..], source[row * Dwords + i]);
        uint U(int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
        return new LevelStatisticsBlock
        {
            DungeonHeartsDestroyed = U(0), EnemyCreaturesKilled = U(4), HeroesDestroyed = U(8), RoomsTakenOver = U(12),
            RoomExtraPoints = U(16), LandOwned = U(20), GoldMined = U(24), ManaStored = (int)U(28), ItemsManufactured = U(32),
            CreaturesCommanded = U(36), CreaturesConverted = U(40), CreatureTraining = U(44), LevelWon = bytes[0x30] != 0,
            OwnCreaturesKilled = U(0x31), MoneySpent = U(0x35), ThingsResearched = U(0x39), TimeTaken = U(0x3D),
            SpecialsFound = bytes[0x41], SpecialsMissed = bytes[0x42], SecretLevelsMissed = bytes[0x43],
        };
    }
}
