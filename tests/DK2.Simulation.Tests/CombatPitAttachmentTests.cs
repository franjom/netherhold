// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public sealed class CombatPitAttachmentTests
{
    [Fact]
    public void KeeperBindingDoesNotAttachAFighterToTheRoomUnderItsFeet()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        var tile = new TilePos(4, 4);
        RoomRules.Place(db, ref world.Tile(tile), db.Room(RoomTypeId.Prison), PlayerIds.FirstKeeper);
        CreatureState fighter = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, tile);
        fighter.PrisonOwnerPlayerId = PlayerIds.FirstKeeper;
        fighter.CombatPitFighter = true;

        Assert.Null(CreatureDeathRules.AttachedRoom(world, db, fighter));
    }

    [Theory]
    [InlineData(RoomTypeId.Prison, 1)]
    [InlineData(RoomTypeId.CombatPit, 0)]
    public void AttachmentUsesTheHeldPlaceEvenWhenTheCreatureHasMovedAway(RoomTypeId type, int units)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        var place = new TilePos(4, 4);
        RoomRules.Place(db, ref world.Tile(place), db.Room(type), PlayerIds.FirstKeeper);
        CreatureState fighter = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new TilePos(1, 1));
        fighter.HeldPlace = new RoomPlace(place.Y * world.Width + place.X, units);

        Assert.Equal(type, CreatureDeathRules.AttachedRoom(world, db, fighter)?.Definition.TypeId);
    }
}
