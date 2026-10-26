// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Campaign profile rules over immutable snapshots; score writes are separate.</summary>
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

    /// <summary>The map being played: Level Number, or 25 + Secret Level Number (0x0053C3C0 case 4).</summary>
    public static CampaignSlot PlayedSlot(CampaignProfile profile)
    {
        int value = profile.SecretLevelNumber is >= 1 and <= 5 ? 25 + (int)profile.SecretLevelNumber : profile.LevelNumber;
        return value is >= 1 and <= 30 && value != 25 ? new CampaignSlot(value) : CampaignSlot.None;
    }

    /// <summary>The local player's mode-1 arms of 0x004BE630: a loss reopens the map, counts an
    /// attempt and drops the transfer creature; a win runs the branch table or marks the secret.
    /// Not here: the win tail's LEVEL-flag 0x20/0x40 completions and the finale (0x004BEDC0..).</summary>
    public static CampaignProfile RecordResult(CampaignProfile profile, CampaignSlot played, bool won)
    {
        if (!played.IsCampaign && !played.IsSecret) throw new ArgumentOutOfRangeException(nameof(played));
        if (won)
        {
            if (played.IsCampaign) return CompleteMap(profile, played);
            var completed = profile.SecretLevelsCompleted.ToArray(); completed[played.Value - 26] = 1;
            return profile with { SecretLevelsCompleted = Array.AsReadOnly(completed) };
        }
        var attempts = profile.LevelAttempts.ToArray(); attempts[played.Value - 1]++;
        if (played.IsSecret) return profile with { LevelAttempts = Array.AsReadOnly(attempts) };
        var statuses = profile.PlayerLevelStatus.ToArray();
        if (statuses[played.Value - 1] is not (CampaignLevelStatus.Won or CampaignLevelStatus.ClosedBranch))
            statuses[played.Value - 1] = CampaignLevelStatus.Open;
        return profile with { PlayerLevelStatus = Array.AsReadOnly(statuses), LevelAttempts = Array.AsReadOnly(attempts),
            TransferCreatureId = 0 };
    }

    /// <summary>0x00534F70 on re-entering the front end: a won campaign map re-applies its branch
    /// row (slot 24 has none), repairing a status lost between the level and the menu.</summary>
    public static CampaignProfile RepairAfterLevel(CampaignProfile profile)
    {
        CampaignSlot played = PlayedSlot(profile);
        return played.IsCampaign && played.Value != 24 && profile.PlayerLevelStatus[played.Value - 1] == CampaignLevelStatus.Won
            ? CompleteMap(profile, played) : profile;
    }

    /// <summary>The statistics page's accept (dispatcher 0x55): after a won campaign map, Level Number
    /// moves to the map after it (6a/6b to 8, 11a-c to 15, 15a/b to 20) unless that one is won or closed.</summary>
    public static CampaignProfile AdvanceAfterResults(CampaignProfile profile)
    {
        CampaignSlot played = PlayedSlot(profile);
        if (!played.IsCampaign || profile.PlayerLevelStatus[played.Value - 1] != CampaignLevelStatus.Won) return profile;
        int next = played.Value switch { 6 or 7 => 8, 12 or 13 or 14 => 15, 18 or 19 => 20, _ => played.Value + 1 };
        return profile.PlayerLevelStatus[next - 1] is CampaignLevelStatus.Won or CampaignLevelStatus.ClosedBranch
            ? profile : profile with { LevelNumber = (byte)next };
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
