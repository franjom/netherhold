// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Per-player priority for foreign casualties on a worker's scanned tile.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/computer-keeper-threat.md")]
public static class RescuePriorityRules
{
    public static bool IsMarked(CreatureState creature, PlayerId playerId) =>
        (creature.RescuePriorityMask & PlayerBit(playerId)) != 0;

    public static void Set(CreatureState creature, PlayerId playerId, bool enabled)
    {
        ushort bit = PlayerBit(playerId);
        creature.RescuePriorityMask = enabled
            ? (ushort)(creature.RescuePriorityMask | bit)
            : (ushort)(creature.RescuePriorityMask & ~bit);
    }

    public static int Rank(CreatureState creature, byte rescuer) =>
        creature.OwnerPlayerId == rescuer ? 1 : IsMarked(creature, rescuer) ? 0 : 2;

    private static ushort PlayerBit(PlayerId playerId) =>
        playerId.Value is >= 1 and <= 7 ? (ushort)(1 << (playerId.Value - 1)) : (ushort)0;
}
