// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>Job 22 (Wait, state 176) is reachable in shipped data, so the dispatcher's hold counter
/// <c>+0x44</c> that <c>0x00486CC0</c> arms is not dead code (Q499). Our <c>HeroJobRules.Wait</c> has no
/// hold; this pins the population that would show it, so a change to that rule is made knowingly.</summary>
public class HeroWaitJobOnRealDataTests
{
    private const byte WaitJob = 22;
    private const byte LeaderBit = 2;

    [InstallFact]
    public void JobWaitIsSetByShippedPartiesPlacedCreaturesAndScripts_Q499HoldCounterIsUnbuilt()
    {
        string install = Dk2InstallGate.Path;
        string maps = Path.Combine(install, "Data", "editor", "maps");
        int leaders = 0, followers = 0, placed = 0, scripted = 0;

        foreach (string file in Directory.EnumerateFiles(maps, "*Things.kld").OrderBy(f => f))
        {
            string level = Path.GetFileName(file)[..^"Things.kld".Length];
            foreach (ThingRecord record in GameDatabaseLoader.LoadCampaignThings(install, level) ?? [])
            {
                if (record is GoodCreatureThing good && good.Objective == WaitJob) placed++;
                if (record is not HeroPartyThing party) continue;
                foreach (GoodCreatureThing m in party.Members.Where(m => m.CreatureId != 0 && m.Objective == WaitJob))
                {
                    if ((m.Flags & LeaderBit) != 0) leaders++;
                    else followers++;
                }
            }

            foreach (TriggerRecord t in GameDatabaseLoader.LoadCampaignTriggers(install, level) ?? [])
                if (t is ActionTriggerRecord && t.Type == 15 && t.Parameters["ObjectiveType"] == WaitJob) scripted++;
        }

        Assert.Equal((6, 14, 127, 7), (leaders, followers, placed, scripted));
    }
}
