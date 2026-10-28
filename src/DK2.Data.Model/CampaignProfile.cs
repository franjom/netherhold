// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

public enum CampaignLevelStatus : uint { Locked = 0, Won = 1, Open = 2, ClosedBranch = 3 }

/// <summary>
/// Campaign storage snapshot, with native field widths and array order. Offsets and meanings:
/// docs/research/campaign-map-and-progression.md §1. Array element zero is map slot one;
/// secret/special/MPD arrays are zero-based. Options are a separate concern.
/// </summary>
public sealed record CampaignProfile
{
    public byte LevelNumber { get; init; } = 1;
    public byte PetDungeonLevelNumber { get; init; } = 1;
    public byte TransferCreatureId { get; init; }
    public byte TransferCreatureLevel { get; init; }
    public uint SecretLevelNumber { get; init; }
    public bool IsUserPetDungeon { get; init; }
    public uint LastPetDungeonEvilRating { get; init; }
    public IReadOnlyList<CampaignLevelStatus> PlayerLevelStatus { get; init; } = new CampaignLevelStatus[40];
    public IReadOnlyList<uint> TotalEvilRating { get; init; } = new uint[40];
    public IReadOnlyList<uint> LevelAttempts { get; init; } = new uint[40];
    public IReadOnlyList<uint> SecretLevels { get; init; } = new uint[16];
    public IReadOnlyList<uint> SecretLevelsCompleted { get; init; } = new uint[16];
    public IReadOnlyList<uint> SpecialLevelsCompleted { get; init; } = new uint[16];
    public IReadOnlyList<uint> PetDungeonLevelsCompleted { get; init; } = new uint[] { 1, 0, 0, 0, 0, 0, 0 };
    public IReadOnlyList<uint> PlayerStatistics { get; init; } = new uint[8 * 17];
    public IReadOnlyList<uint> PlayerStatisticsValid { get; init; } = new uint[8];
    public IReadOnlyList<uint> PlayerValues { get; init; } = new uint[8];
}

public sealed record HiScoreEntry(uint EvilRating, string Name, string LevelLabel);
