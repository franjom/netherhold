// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The personality setter's room list (<c>0x004F8C60</c>) on the shipped rooms: the walk
/// by index then by id needs the definitions sorted by id, and DK2 faults with fewer than fifteen
/// that qualify, so the shipped data must have both.</summary>
public class PersonalityOnRealDataTests
{
    [InstallFact]
    public void TheShippedRoomsAreSortedByIdAndFillTheList()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);

        byte[] ids = db.Rooms.Values.Select(r => r.RoomId).ToArray();
        Assert.Equal(ids.OrderBy(id => id), ids);
        byte[] list = KeeperAIPersonality.RoomList(db);
        Assert.DoesNotContain((byte)0, list);
        Assert.Contains((byte)RoomTypeId.Portal, list);
    }
}
