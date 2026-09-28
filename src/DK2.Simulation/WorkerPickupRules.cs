// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>The two worker bits changed together by prison delivery and task release.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-pickup.md")]
internal static class WorkerPickupRules
{
    public static void SetEnabled(CreatureState creature, bool enabled) =>
        creature.Flags = enabled ? (byte)(creature.Flags | 9) : (byte)(creature.Flags & ~9);

    public static void OnTaskReleased(CreatureState creature)
    {
        // The dispatcher chooses the hook from the saved caller, but this hook reads the resume.
        int state = creature.Dk2StateId is 6 or 7 ? creature.UtilityNewState : creature.Dk2StateId;
        if (state is 43 or 44 or 46 or 49) SetEnabled(creature, true);
    }
}
