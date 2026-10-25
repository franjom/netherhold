// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Data.Model;

namespace DK2.Data.Campaign;

/// <summary>Our versioned campaign file, distinct from original saves and the Windows registry.</summary>
public static class CampaignProfileFile
{
    private const uint Magic = 0x50433244; // D2CP
    private const uint Version = 1;
    private const int FileSize = 8 + 4 + 4 + 4 + 4 + (40 * 3 + 16 * 3 + 7 + 136 + 8 + 8) * 4;

    public static byte[] Write(CampaignProfile profile)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        using var stream = new MemoryStream(FileSize);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic); writer.Write(Version);
        writer.Write(profile.LevelNumber); writer.Write(profile.PetDungeonLevelNumber);
        writer.Write(profile.TransferCreatureId); writer.Write(profile.TransferCreatureLevel);
        writer.Write(profile.SecretLevelNumber); writer.Write(profile.IsUserPetDungeon ? 1u : 0u);
        writer.Write(profile.LastPetDungeonEvilRating);
        WriteArray(writer, profile.PlayerLevelStatus?.Select(value => (uint)value).ToArray(), 40);
        foreach ((IReadOnlyList<uint> values, int count) in Arrays(profile)) WriteArray(writer, values, count);
        return stream.ToArray();
    }

    public static CampaignProfile Read(ReadOnlySpan<byte> data)
    {
        if (data.Length != FileSize) throw new InvalidDataException("Invalid campaign profile length.");
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != Magic || reader.ReadUInt32() != Version)
            throw new InvalidDataException("Unsupported campaign profile signature or version.");
        return new CampaignProfile
        {
            LevelNumber = reader.ReadByte(), PetDungeonLevelNumber = reader.ReadByte(),
            TransferCreatureId = reader.ReadByte(), TransferCreatureLevel = reader.ReadByte(),
            SecretLevelNumber = reader.ReadUInt32(), IsUserPetDungeon = reader.ReadUInt32() != 0,
            LastPetDungeonEvilRating = reader.ReadUInt32(),
            PlayerLevelStatus = Array.AsReadOnly(ReadArray(reader, 40).Select(value => (CampaignLevelStatus)value).ToArray()),
            TotalEvilRating = ReadArray(reader, 40), LevelAttempts = ReadArray(reader, 40),
            SecretLevels = ReadArray(reader, 16), SecretLevelsCompleted = ReadArray(reader, 16),
            SpecialLevelsCompleted = ReadArray(reader, 16), PetDungeonLevelsCompleted = ReadArray(reader, 7),
            PlayerStatistics = ReadArray(reader, 136), PlayerStatisticsValid = ReadArray(reader, 8),
            PlayerValues = ReadArray(reader, 8),
        };
    }

    public static CampaignProfile LoadOrCreate(string path, Func<CampaignProfile?>? import = null)
    {
        if (File.Exists(path)) return Read(File.ReadAllBytes(path));
        CampaignProfile profile = import?.Invoke() ?? new CampaignProfile();
        byte[] data = Write(profile);
        ProfileFileStorage.Save(path, data, replaceExisting: false);
        return profile;
    }

    public static void Save(string path, CampaignProfile profile) => ProfileFileStorage.Save(path, Write(profile));

    private static IEnumerable<(IReadOnlyList<uint>, int)> Arrays(CampaignProfile p)
    {
        yield return (p.TotalEvilRating, 40); yield return (p.LevelAttempts, 40);
        yield return (p.SecretLevels, 16); yield return (p.SecretLevelsCompleted, 16);
        yield return (p.SpecialLevelsCompleted, 16); yield return (p.PetDungeonLevelsCompleted, 7);
        yield return (p.PlayerStatistics, 136); yield return (p.PlayerStatisticsValid, 8);
        yield return (p.PlayerValues, 8);
    }

    private static void WriteArray(BinaryWriter writer, IReadOnlyList<uint>? values, int count)
    {
        if (values is null || values.Count != count) throw new InvalidDataException($"Expected {count} profile values.");
        foreach (uint value in values) writer.Write(value);
    }

    private static IReadOnlyList<uint> ReadArray(BinaryReader reader, int count)
    {
        var values = new uint[count];
        for (int i = 0; i < values.Length; i++) values[i] = reader.ReadUInt32();
        return Array.AsReadOnly(values);
    }
}
