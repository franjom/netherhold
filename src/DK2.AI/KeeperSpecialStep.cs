// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>The keeper's specials step (<c>0x00500520</c>, step 8, computer-keeper-management.md §3):
/// walk the keeper's own objects, keep the last one with <c>TypeSpecial</c> on a tile it owns that no
/// creature holds, and only then test that one's id: 8, 117–126, 128 or 129 posts action 88 for it.
/// It runs every tick, after the retreat and before the training roll, and spends no allowance.</summary>
/// <remarks>DK2's owner list is pushed at its head (<c>0x004B4C30</c>), so the walk ends at the
/// object owned longest; ours keeps no ownership order and takes the lowest object id for it. A last
/// object of an excluded id (7, 127, 140) suppresses an earlier usable one, as in DK2.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-management.md")]
public static class KeeperSpecialStep
{
    public static void Run(KeeperView view, List<PlayerCommand> commands)
    {
        // The view lists the specials by ascending id; the walk runs newest-owned first.
        KeeperSpecialView? kept = null;
        IReadOnlyList<KeeperSpecialView> specials = view.Specials;
        for (int i = specials.Count - 1; i >= 0; i--)
        {
            KeeperSpecialView s = specials[i];
            if (view.IsMine(new TilePos(s.X, s.Y)) && !s.Carried) kept = s;
        }
        if (kept is { } chosen && Usable(chosen.ObjectTypeId))
            commands.Add(new ActivateSpecialCommand(view.PlayerId, chosen.ObjectId));
    }

    /// <summary>The switch at <c>0x005005C2</c>: 8 (the Torture Wheel, which carries no
    /// <c>TypeSpecial</c> and so never reaches it), 117–126, 128 and 129.</summary>
    public static bool Usable(byte objectTypeId) =>
        objectTypeId is 8 or (>= 117 and <= 126) or 128 or 129;
}
