// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class CampaignBriefingTests
{
    [InstallFact]
    public void AllPlayableCampaignSlotsUseTheirOwnInstalledBriefingTable()
    {
        string install = Dk2InstallGate.Path;
        foreach (int value in Enumerable.Range(1, 30).Where(i => i != 25))
        {
            var slot = new CampaignSlot(value);
            var briefing = CampaignBriefingLoader.Load(install, slot);
            Assert.Equal(slot, briefing.Slot);
            Assert.False(string.IsNullOrWhiteSpace(briefing.Name + briefing.Tagline), slot.LevelName);
            var definition = GameDatabaseLoader.LoadCampaignLevel(install, slot.LevelName!)!;
            string file = BriefingTextTable.FileName(definition.TextTableId)!;
            var strings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", file));
            uint nativeResource = slot.IsSecret ? (uint)(1005 + slot.Value) : (uint)(999 + slot.Value);
            Assert.Equal(nativeResource, strings.FileId);
            Assert.Equal(strings.Entries.Single(e => e.Id == 2).Value, briefing.MainObjective);
        }
    }
}
