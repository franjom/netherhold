// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The room tick's border step (<c>0x004E7CD0</c>): every room with HasFlame and an owner
/// moves its cursor along its contour and, on DK2's odd turns, asks for a Flame Round Room there in
/// its owner's colour. A cursor belongs to the room holding its tile; a room without one is given a
/// new one, and a cursor no room holds is dropped.</summary>
/// <remarks>Deviations: DK2 skips the step on a tick the room type's slot <c>+0x15</c> handler
/// returns 0, which is not read for every type. DK2's cursor is part of the room object and
/// survives the sale of its tile or the loss of HasFlame or owner; ours, kept for derived rooms,
/// goes with its tile and a room re-found without one starts afresh.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/room-border-effects.md")]
public sealed class RoomBorderFlameSystem : ISimulationSystem
{
    private readonly List<RoomBorderCursor> _next = [];

    public string Name => "RoomBorderFlames";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        List<RoomBorderCursor> cursors = world.RoomBorderCursorList;
        bool births = ((context.Tick.Value + 1) & 1) != 0;   // DK2's turn is our tick plus one (ADR-0119)
        _next.Clear();
        foreach (RoomInstance room in RoomIndex.Build(world, context.Database))
        {
            if ((room.Definition.Flags & RoomFlags.HasFlame) == 0 || room.OwnerPlayerId == 0) continue;
            RoomBorderCursor cursor = default;
            bool found = false;
            foreach (RoomBorderCursor held in cursors)
                if (room.Contains(held.Y * world.Width + held.X)) { cursor = held; found = true; break; }
            if (!found) cursor = RoomBorderRules.Start(world, room);
            cursor = RoomBorderRules.Advance(cursor, world, room);
            _next.Add(cursor);
            if (births)
                context.RequestEffect(Name, new ViewEffectRequest(new EffectId(RoomBorderRules.FlameEffectId),
                    new PlayerId(room.OwnerPlayerId), RoomBorderRules.FlamePoint(cursor)));
        }
        cursors.Clear();
        cursors.AddRange(_next);
    }
}
