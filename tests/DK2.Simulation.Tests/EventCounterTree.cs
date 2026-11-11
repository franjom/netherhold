// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>A creature tree whose one event record adds 1 to flag 1 each time the event fires, so
/// <c>world.Triggers.Read(1)</c> counts the firings. Give the creature <see cref="Root"/> as its
/// <c>TriggerId</c>.</summary>
internal static class EventCounterTree
{
    public const ushort Root = 5;
    public const byte CountFlag = 1;

    public static TriggerTree For(CreatureTriggerEvent type) => new(
    [
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = Root, Type = (byte)type, RepeatCount = 255,
            ChildTriggerId = 0xFFFF, NextTriggerId = 0, Parameters = new Dictionary<string, long>(),
        },
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = 0xFFFF, Type = 7, RepeatCount = 0, ChildTriggerId = 0, NextTriggerId = 0,
            Parameters = new Dictionary<string, long> { ["FlagId"] = CountFlag, ["Flag"] = 0x12, ["Value"] = 1 },
        },
    ], 0);
}
