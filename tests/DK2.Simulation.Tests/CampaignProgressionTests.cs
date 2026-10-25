// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public class CampaignProgressionTests
{
    [Fact]
    public void BranchesOpenTogetherAndWinningOneClosesOnlyItsUnsettledSiblings()
    {
        var p = CampaignProgression.NewCampaign(new CampaignProfile());
        p = CampaignProgression.CompleteMap(p, new CampaignSlot(5));
        Assert.Equal(CampaignLevelStatus.Open, p.PlayerLevelStatus[5]);
        Assert.Equal(CampaignLevelStatus.Open, p.PlayerLevelStatus[6]);
        p = CampaignProgression.CompleteMap(p, new CampaignSlot(6));
        Assert.Equal(CampaignLevelStatus.Won, p.PlayerLevelStatus[5]);
        Assert.Equal(CampaignLevelStatus.ClosedBranch, p.PlayerLevelStatus[6]);
        Assert.Equal(CampaignLevelStatus.Open, p.PlayerLevelStatus[7]);
        Assert.True(CampaignProgression.CanSelect(p, new CampaignSlot(6), false));
        Assert.False(CampaignProgression.CanSelect(p, new CampaignSlot(7), false));
        p = CampaignProgression.CompleteMap(p, new CampaignSlot(5));
        Assert.Equal(CampaignLevelStatus.ClosedBranch, p.PlayerLevelStatus[6]);
        foreach (var (winner, siblings, next) in new[] { (12, new[] { 13, 14 }, 15), (13, new[] { 12, 14 }, 15),
            (14, new[] { 12, 13 }, 15), (18, new[] { 19 }, 20), (19, new[] { 18 }, 20) })
        {
            var branch = CampaignProgression.CompleteMap(new CampaignProfile(), new CampaignSlot(winner));
            Assert.Equal(CampaignLevelStatus.Open, branch.PlayerLevelStatus[next - 1]);
            foreach (int sibling in siblings) Assert.Equal(CampaignLevelStatus.ClosedBranch, branch.PlayerLevelStatus[sibling - 1]);
        }
    }

    [Fact]
    public void ResetClearsNativeRangesAndPreservesMpdSpecialTransferAndTailSlots()
    {
        var old = new CampaignProfile { PlayerLevelStatus = Enumerable.Repeat(CampaignLevelStatus.Won, 40).ToArray(),
            TotalEvilRating = Enumerable.Repeat(9u, 40).ToArray(), LevelAttempts = Enumerable.Repeat(7u, 40).ToArray(),
            SecretLevels = Enumerable.Repeat(1u, 16).ToArray(), SecretLevelsCompleted = Enumerable.Repeat(1u, 16).ToArray(),
            TransferCreatureId = 4, TransferCreatureLevel = 8, LevelNumber = 20, PetDungeonLevelNumber = 6,
            SpecialLevelsCompleted = Enumerable.Repeat(1u, 16).ToArray(), PetDungeonLevelsCompleted = Enumerable.Repeat(1u, 7).ToArray() };
        var p = CampaignProgression.NewCampaign(old);
        Assert.Equal(CampaignLevelStatus.Open, p.PlayerLevelStatus[0]);
        Assert.All(p.PlayerLevelStatus.Skip(1).Take(24), s => Assert.Equal(CampaignLevelStatus.Locked, s));
        Assert.All(p.PlayerLevelStatus.Skip(25), s => Assert.Equal(CampaignLevelStatus.Won, s));
        Assert.All(p.TotalEvilRating.Take(25), n => Assert.Equal(0u, n));
        Assert.All(p.TotalEvilRating.Skip(25), n => Assert.Equal(9u, n));
        Assert.All(p.LevelAttempts.Concat(p.SecretLevels).Concat(p.SecretLevelsCompleted), n => Assert.Equal(0u, n));
        Assert.Equal(old.SpecialLevelsCompleted, p.SpecialLevelsCompleted);
        Assert.Equal(old.PetDungeonLevelsCompleted, p.PetDungeonLevelsCompleted);
        Assert.Equal(old.TransferCreatureId, p.TransferCreatureId);
        Assert.Equal(old.TransferCreatureLevel, p.TransferCreatureLevel);
        Assert.Equal(1, p.LevelNumber); Assert.Equal(1, p.PetDungeonLevelNumber);
        Assert.False(CampaignProgression.NeedsResetConfirmation(p));
        Assert.True(CampaignProgression.NeedsResetConfirmation(old));
        Assert.Equal(CampaignLevelStatus.Won, old.PlayerLevelStatus[0]);
    }

    [Fact]
    public void SlotIdentityKeepsTheReservedSlotAndSecretMoonGateDistinct()
    {
        var p = new CampaignProfile { SecretLevels = Enumerable.Repeat(1u, 16).ToArray() };
        Assert.Equal("level6a", new CampaignSlot(6).LevelName);
        Assert.Equal("level11c", new CampaignSlot(14).LevelName);
        Assert.Equal("secret5", new CampaignSlot(30).LevelName);
        Assert.Null(new CampaignSlot(25).LevelName);
        Assert.False(CampaignProgression.CanSelect(p, new CampaignSlot(25), true));
        Assert.False(CampaignProgression.CanSelect(p, new CampaignSlot(1), true));
        Assert.True(CampaignProgression.CanSelect(p, new CampaignSlot(26), false));
        Assert.False(CampaignProgression.CanSelect(p, new CampaignSlot(30), false));
        Assert.True(CampaignProgression.CanSelect(p, new CampaignSlot(30), true));
        Assert.Equal(new CampaignSlot(6), CampaignSlot.FromPickTag(156));
        Assert.Equal(new CampaignSlot(30), CampaignSlot.FromPickTag(30));
        Assert.Equal(CampaignSlot.None, CampaignSlot.FromPickTag(150));
        Assert.Equal(CampaignSlot.None, CampaignSlot.FromPickTag(175));
    }
    [Fact]
    public void StartupRepairOpensTheFirstUnsettledSlotAndNeverReopensAClosedBranch()
    {
        var statuses = Enumerable.Repeat(CampaignLevelStatus.Won, 40).ToArray();
        statuses[6] = CampaignLevelStatus.ClosedBranch; statuses[7] = CampaignLevelStatus.Locked;
        statuses[8] = CampaignLevelStatus.Open;
        var profile = new CampaignProfile { PlayerLevelStatus = statuses };
        var repaired = CampaignProgression.EnsureOpenMap(profile);
        Assert.Equal(CampaignLevelStatus.ClosedBranch, repaired.PlayerLevelStatus[6]);
        Assert.Equal(CampaignLevelStatus.Open, repaired.PlayerLevelStatus[7]);
        Assert.Equal(CampaignLevelStatus.Locked, profile.PlayerLevelStatus[7]);
        Assert.Same(repaired, CampaignProgression.EnsureOpenMap(repaired));
    }

    [Fact]
    public void HiddenLandSpecialUsesTheNativeCampaignSlotsAndNeverUnlocksMoonSecret()
    {
        var profile = new CampaignProfile();
        foreach (int slot in new[] { 5, 8, 11, 16 }) profile = CampaignProgression.LocateHiddenLand(profile, new CampaignSlot(slot));
        Assert.All(profile.SecretLevels.Take(4), n => Assert.Equal(1u, n));
        Assert.All(profile.SecretLevels.Skip(4), n => Assert.Equal(0u, n));
        Assert.Same(profile, CampaignProgression.LocateHiddenLand(profile, new CampaignSlot(6)));
    }

}
