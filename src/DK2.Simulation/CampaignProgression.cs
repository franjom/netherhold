// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Campaign profile rules; world result handling and score writes are separate.</summary>
public static class CampaignProgression
{
    public static bool CanSelect(CampaignProfile profile, CampaignSlot slot, bool secretFiveAvailable)
        => slot.IsCampaign ? profile.PlayerLevelStatus[slot.Value - 1] is CampaignLevelStatus.Open or CampaignLevelStatus.Won
            : slot.IsSecret && (slot.Value == 30 ? secretFiveAvailable : profile.SecretLevels[slot.Value - 26] != 0);

    public static bool NeedsResetConfirmation(CampaignProfile profile)
        => profile.PlayerLevelStatus[0] != CampaignLevelStatus.Open || profile.LevelAttempts[0] != 0;

    public static CampaignProfile NewCampaign(CampaignProfile profile)
    {
        var statuses = profile.PlayerLevelStatus.ToArray();
        var ratings = profile.TotalEvilRating.ToArray();
        Array.Clear(statuses, 0, 25); Array.Clear(ratings, 0, 25);
        statuses[0] = CampaignLevelStatus.Open;
        return profile with { PlayerLevelStatus = Array.AsReadOnly(statuses), TotalEvilRating = Array.AsReadOnly(ratings),
            LevelAttempts = new uint[40], SecretLevels = new uint[16], SecretLevelsCompleted = new uint[16],
            LevelNumber = 1, PetDungeonLevelNumber = 1, IsUserPetDungeon = false };
    }

    public static CampaignProfile LocateHiddenLand(CampaignProfile profile, CampaignSlot playedMap)
    {
        int index = playedMap.Value switch { 5 => 0, 8 => 1, 11 => 2, 16 => 3, _ => -1 };
        if (index < 0) return profile;
        var secrets = profile.SecretLevels.ToArray(); secrets[index] = 1;
        return profile with { SecretLevels = Array.AsReadOnly(secrets) };
    }

    public static CampaignProfile EnsureOpenMap(CampaignProfile profile)
    {
        for (int i = 0; i < 24; i++)
        {
            if (profile.PlayerLevelStatus[i] == CampaignLevelStatus.Open) return profile;
            if (profile.PlayerLevelStatus[i] != CampaignLevelStatus.Locked) continue;
            var statuses = profile.PlayerLevelStatus.ToArray(); statuses[i] = CampaignLevelStatus.Open;
            return profile with { PlayerLevelStatus = Array.AsReadOnly(statuses) };
        }
        return profile;
    }

    public static CampaignProfile CompleteMap(CampaignProfile profile, CampaignSlot slot)
    {
        if (!slot.IsCampaign) throw new ArgumentOutOfRangeException(nameof(slot));
        var statuses = profile.PlayerLevelStatus.ToArray();
        int won = slot.Value;
        Set(won, CampaignLevelStatus.Won);
        switch (won)
        {
            case 5: Open(6); Open(7); break;
            case 6: Close(7); Open(8); break;
            case 7: Close(6); Open(8); break;
            case 11: Open(12); Open(13); Open(14); break;
            case 12: Close(13); Close(14); Open(15); break;
            case 13: Close(12); Close(14); Open(15); break;
            case 14: Close(12); Close(13); Open(15); break;
            case 17: Open(18); Open(19); break;
            case 18: Close(19); Open(20); break;
            case 19: Close(18); Open(20); break;
            default: Open(won + 1); break;
        }
        return profile with { PlayerLevelStatus = Array.AsReadOnly(statuses) };
        void Open(int value) => Set(value, CampaignLevelStatus.Open);
        void Close(int value) => Set(value, CampaignLevelStatus.ClosedBranch);
        void Set(int value, CampaignLevelStatus status)
        {
            if (statuses[value - 1] is not (CampaignLevelStatus.Won or CampaignLevelStatus.ClosedBranch))
                statuses[value - 1] = status;
        }
    }
}
