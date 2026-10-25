// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Text;

namespace DK2.Data.Campaign;

/// <summary>Selected level metadata and briefing strings, read from the install without converting files.</summary>
public static class CampaignBriefingLoader
{
    public static CampaignBriefing Load(string install, CampaignSlot slot)
    {
        if (slot.LevelName is not string name) throw new ArgumentOutOfRangeException(nameof(slot));
        var definition = GameDatabaseLoader.LoadCampaignLevel(install, name)
            ?? throw new FileNotFoundException("Campaign level metadata was not found: " + name);
        string file = BriefingTextTable.FileName(definition.TextTableId)
            ?? throw new InvalidDataException("Campaign level has no briefing table: " + name);
        var strings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", file));
        string Line(uint id) => strings.TryResolve(id, out string? text) ? text : "";
        return new(slot, Line(0), Line(1), Line(2), Array.AsReadOnly(new[] { Line(3), Line(4), Line(5) }));
    }
}
