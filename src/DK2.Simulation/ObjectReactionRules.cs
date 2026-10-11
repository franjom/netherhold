// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

[Dk2Behavior(Confidence.Confirmed, "docs/research/object-drawing.md#8-furniture-eggs-and-chickens")]
public static class ObjectReactionRules
{
    public const byte StruckProcess = 18;

    public static void StartStruck(SimulationContext context, ObjectState target)
    {
        if (target.AutoPlaced && target.ObjectTypeId == 14 && target.TimerTicks != 0x200)
            target.Heading = AutoFurnitureRules.Choice(target.TileIndex % context.World.Width,
                target.TileIndex / context.World.Width, 0x800);
        target.ProcessState = StruckProcess;
        target.ProcessAgeTicks = 0;
    }

    public static void Step(SimulationContext context, ObjectState target)
    {
        target.ProcessAgeTicks++;
        if (target.ProcessAgeTicks == 1)
        {
            target.ProcessDeadline = context.Tick.Value;
            target.ProcessEnding = context.Rng.NextInt(2) == 0;
            target.TimerTicks = 0x200;
            target.ProcessCount = 0;
        }
        var definition = context.Database.ObjectById(target.ObjectTypeId);
        if (definition is not null && target.ProcessAgeTicks > AnimationTimingRules.FirstCycleHoldTicks(definition.ArtSlot(5)))
            target.ProcessCount = 1;
        int sign = target.ProcessEnding ? 1 : -1;
        int spin = (int)((long)target.TimerTicks * sign * 9 / ((context.Tick.Value + 3 - target.ProcessDeadline) * 3));
        if (Math.Abs(spin) > 0x40)
        {
            target.Heading = (target.Heading + spin - sign * 0x40) & 0x7FF;
            return;
        }
        target.ProcessState = 0;
        target.ProcessAgeTicks = 0;
    }
}
