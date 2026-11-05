// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>Offers a keeper the Lair (availability 3), as a level that lets it build one does. The
/// lair charge needs that or a Lair owned (<c>0x00478DB7-0x00478DD5</c>, ADR-0666), so a test of the
/// lairless ladder runs in a level that offers the room.</summary>
internal static class LairOffer
{
    public static void Offer(WorldState world, byte player) =>
        world.Player(player).Availabilities.Add(
            new AvailabilityState(AvailabilityKind.Room, (int)RoomTypeId.Lair, RoomNeedRules.Available));
}
