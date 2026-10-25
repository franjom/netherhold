// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

public sealed record CampaignBriefing(CampaignSlot Slot, string Name, string Tagline,
    string MainObjective, IReadOnlyList<string> SubObjectives)
{
    public string Heading => string.Concat((char)34, Name, (char)34, " - ", Tagline);
}

public enum CampaignObjectiveShot : byte { First, Second }
