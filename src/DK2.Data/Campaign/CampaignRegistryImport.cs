// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Model;

namespace DK2.Data.Campaign;

/// <summary>Reads campaign values only. Missing values retain defaults; malformed present values fail.</summary>
public static class CampaignRegistryImport
{
    public static CampaignProfile Read(IReadOnlyDictionary<string, object> values)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));
        if (!values.TryGetValue("Version Number", out object? version)
            || (version is not int signed || signed != 1) && (version is not uint unsigned || unsigned != 1))
            return new CampaignProfile();
        var defaults = new CampaignProfile();
        return defaults with
        {
            LevelNumber = Byte(values, "Level Number", 1),
            PetDungeonLevelNumber = Byte(values, "MPD Level Number", 1),
            TransferCreatureId = Byte(values, "Transfer Creature ID", 0),
            TransferCreatureLevel = Byte(values, "Transfer Creature Level", 0),
            SecretLevelNumber = Dword(values, "Secret Level Number", 0),
            PlayerLevelStatus = System.Array.AsReadOnly(Array(values, "Player Level Status", new uint[40])
                .Select(value => (CampaignLevelStatus)value).ToArray()),
            TotalEvilRating = Array(values, "Total Evil Rating", defaults.TotalEvilRating),
            LevelAttempts = Array(values, "Level Attempts", defaults.LevelAttempts),
            SecretLevels = Array(values, "Secret Levels", defaults.SecretLevels),
            SecretLevelsCompleted = Array(values, "Secret Levels Completed", defaults.SecretLevelsCompleted),
            SpecialLevelsCompleted = Array(values, "Special Levels Completed", defaults.SpecialLevelsCompleted),
            PetDungeonLevelsCompleted = Array(values, "Pet Dungeon Levels Completed", defaults.PetDungeonLevelsCompleted),
        };
    }

    private static byte Byte(IReadOnlyDictionary<string, object> values, string name, byte fallback)
    {
        uint value = Dword(values, name, fallback);
        if (value > byte.MaxValue) throw new InvalidDataException($"Invalid registry byte: {name}");
        return (byte)value;
    }

    private static uint Dword(IReadOnlyDictionary<string, object> values, string name, uint fallback)
    {
        if (!values.TryGetValue(name, out object? value)) return fallback;
        if (value is int signed) return unchecked((uint)signed);
        if (value is uint unsigned) return unsigned;
        throw new InvalidDataException($"Expected a registry DWORD: {name}");
    }

    private static IReadOnlyList<uint> Array(IReadOnlyDictionary<string, object> values, string name, IReadOnlyList<uint> fallback)
    {
        if (!values.TryGetValue(name, out object? value)) return fallback;
        if (value is not byte[] bytes || bytes.Length < fallback.Count * 4)
            throw new InvalidDataException($"Invalid registry array: {name}");
        var result = new uint[fallback.Count];
        for (int i = 0; i < result.Length; i++) result[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4));
        return System.Array.AsReadOnly(result);
    }
}
