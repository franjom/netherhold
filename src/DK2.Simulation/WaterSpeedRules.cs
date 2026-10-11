// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The water speed modifier in creature update 0x0048970B–0x00489749.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/steering-behaviours.md")]
public static class WaterSpeedRules
{
    /// <summary>Halves a non-flying creature's current speed on water, rounding in entity units
    /// before returning the 16.16 body cap. Flying uses the same supported subset as traps;
    /// the unmodelled worker flight bits and animation exceptions remain a deviation.</summary>
    public static int SpeedCap(TerrainDef terrain, CreatureDef definition, CreatureState creature, int cap)
    {
        return terrain.IsWater && !IsFlying(definition, creature) ? ((cap >> 4) / 2) << 4 : cap;
    }

    public static bool IsFlying(CreatureDef definition, CreatureState creature) =>
        !definition.Flags.HasFlag(CreatureFlags.IsWorker)
            && creature.Dk2StateId is not (217 or 218)
            && definition.Flags.HasFlag(CreatureFlags.CanFly);
}
