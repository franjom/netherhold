// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>Player <c>+0xEEA</c>, the name the HUD shows for a player (the Current Holder box, the full map's legend),
/// shared by every engine adapter.</summary>
public static class PlayerNames
{
    /// <summary>The level's Players record name, or DK2's own "Good Player" / "Neutral Player" (0x006A6D50, 0x006A6D68)
    /// when that is empty, by the record's kind (0x0051EFE0: 1 good, 2 neutral), else "Keeper %d" with its Number
    /// (0x0051F0B0, 0x006A6D88), which a level load sets to the id. STRONGLY INFERRED. <paramref name="players"/> is
    /// null outside a campaign level.</summary>
    public static Func<byte, string> From(IEnumerable<PlayerDef>? players)
    {
        var names = new Dictionary<byte, string>();
        if (players != null)
        {
            foreach (PlayerDef player in players)
            {
                if (!string.IsNullOrEmpty(player.Name))
                {
                    names[player.PlayerId] = player.Name;
                }
            }
        }

        return id => names.TryGetValue(id, out string? name) ? name
            : id == PlayerIds.Good ? "Good Player" : id == PlayerIds.Neutral ? "Neutral Player" : "Keeper " + id;
    }
}
