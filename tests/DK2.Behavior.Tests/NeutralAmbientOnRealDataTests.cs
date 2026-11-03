// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>NeutralAmbientEffectSystem runs after every keeper's frame. That is the neutral player's
/// place in DK2's active list (the reverse of the PLAYERS records) when no shipped level lists a keeper
/// before the neutral player (ADR-0646).</summary>
public class NeutralAmbientOnRealDataTests
{
    [InstallFact]
    public void NoShippedLevelListsAKeeperBeforeTheNeutralPlayer()
    {
        string install = Dk2InstallGate.Path;
        string maps = Path.Combine(install, "Data", "editor", "maps");
        int checkedLevels = 0;
        foreach (string file in Directory.GetFiles(maps, "*Variables.kld"))
        {
            string level = Path.GetFileNameWithoutExtension(file)[..^"Variables".Length];
            List<PlayerDef>? records = GameDatabaseLoader.LoadCampaignPlayers(install, level);
            if (records is null) continue;
            int neutral = records.FindIndex(p => p.PlayerId == PlayerIds.Neutral);
            if (neutral < 0) continue;
            checkedLevels++;
            Assert.DoesNotContain(records.Take(neutral), p => PlayerIds.IsKeeper(p.PlayerId));
        }
        Assert.True(checkedLevels > 20, $"only {checkedLevels} levels have a neutral record");
    }

    /// <summary>The seed-1 Level 5 draw log has the neutral frame's two draws on each of its 500 turns.</summary>
    [InstallFact]
    public void LevelFiveActivatesTheNeutralPlayer()
    {
        List<PlayerDef> records = GameDatabaseLoader.LoadCampaignPlayers(Dk2InstallGate.Path, "level5")!;
        Assert.Contains(records, p => p.PlayerId == PlayerIds.Neutral);
    }
}
