// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The room tick's health step (<c>0x004E4B00</c>): every room under its cap gains its
/// definition's <c>HealthGain</c>. That is 0 for all 26 shipped rooms, so this sweeps nothing on
/// the shipped data and costs one scan of the room table a run.</summary>
/// <remarks>Deviation, for data that sets a gain only: DK2 skips the step on a tick the room
/// type's slot <c>+0x15</c> handler returns 0, which is not read for every type.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/room-health.md")]
public sealed class RoomHealthSystem : ISimulationSystem
{
    private GameDatabase? _checked;
    private bool _anyGain;

    public string Name => "RoomHealth";

    public void Execute(SimulationContext context)
    {
        GameDatabase database = context.Database;
        if (!ReferenceEquals(_checked, database))
        {
            _checked = database;
            _anyGain = database.Rooms.Values.Any(r => r.HealthGain != 0);
        }
        if (!_anyGain) return;

        foreach (RoomInstance room in RoomIndex.Build(context.World, database))
            RoomHealthRules.Regenerate(context.World, database, room);
    }
}
