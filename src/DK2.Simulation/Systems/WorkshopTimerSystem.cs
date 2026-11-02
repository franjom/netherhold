// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation.Systems;

/// <summary>The Workshop handler's four effect timers (<c>Workshop_Tick</c> <c>0x004F4B20</c>), run after
/// the hated-company step, which the same handler calls first. Rules and evidence:
/// <see cref="WorkshopTimerRules"/>.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/workshop-timers.md")]
public sealed class WorkshopTimerSystem : IRoomTickStep
{
    public string Name => "workshop timers";

    public void Execute(SimulationContext context) => RoomTickSystem.RunAlone(context, this);

    public bool Applies(SimulationContext context, RoomInstance room) => WorkshopTimerRules.IsWorkshop(room.Definition);

    public void Tick(SimulationContext context, RoomInstance room) => WorkshopTimerRules.Tick(context, room);
}
