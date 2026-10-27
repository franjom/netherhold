// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

public sealed record CampaignBriefing(CampaignSlot Slot, string Name, string Tagline,
    string MainObjective, IReadOnlyList<string> SubObjectives, ushort LevelFlags = 0)
{
    public string Heading => string.Concat((char)34, Name, (char)34, " - ", Tagline);
    /// <summary>Level flags 0x20 (secret) or 0x40 (special), which 0x00535E10 copies to the front end's
    /// <c>+0x311B6</c> and <c>+0x311BA</c>: such a level shows one objective shot.</summary>
    public bool IsSecretOrSpecial => (LevelFlags & 0x60) != 0;
}

public enum CampaignObjectiveShot : byte { First, Second }
