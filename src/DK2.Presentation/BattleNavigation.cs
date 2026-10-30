// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

[Dk2Behavior(Confidence.Confirmed, "docs/research/battle-tab.md#4-clicks-and-the-f-key--confirmed")]
public sealed class BattleNavigation(PlayerId viewer)
{
    public BattleViewSlot Last { get; private set; } = BattleViewSlot.None;

    public TilePos? Next(IReadOnlyList<BattleViewSnapshot> battles)
    {
        for (int step = 1; step <= 8; step++)
        {
            var slot = new BattleViewSlot((Last.Value + step) & 7);
            foreach (BattleViewSnapshot battle in battles)
                if (battle.Slot == slot && battle.MemberCount > 0 && battle.Owners.Contains(viewer))
                {
                    Last = slot;
                    return battle.Centre;
                }
        }
        Last = BattleViewSlot.None;
        return null;
    }
}
