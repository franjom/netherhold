// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary><see cref="KeeperSpellRules.PlaceAllowedInRoom"/> is <see cref="KeeperSpellRules.PlaceAllowed"/>
/// with the tile's room handed in rather than flood-filled again, so the two must agree on every own
/// room tile of a real map, for every keeper spell, every keeper.</summary>
public class SpellPlaceInRoomTests
{
    [InstallFact]
    public void TheRoomAwareCheckAgreesWithThePlainOneOnEveryOwnRoomTile()
    {
        string install = Dk2InstallGate.Path;
        foreach (string level in new[] { "Level17", "Level11a", "Level1" })
        {
            GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, level);
            LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, level)!;
            byte[] owners = map.Tiles.Select(t => t.PlayerId).Distinct().OrderBy(p => p).ToArray();
            WorldState world = WorldState.FromMap(database, map, owners);
            int compared = 0, refused = 0;
            foreach (byte owner in owners.Where(PlayerIds.IsKeeper))
            {
                var view = new KeeperView(database, world, owner);
                foreach (RoomInstance room in RoomIndex.Build(world, database).Where(r => r.OwnerPlayerId == owner))
                    foreach (int index in room.TileIndices)
                        foreach (KeeperSpellDef spell in database.KeeperSpells.Values)
                        {
                            var at = new Core.TilePos(index % world.Width, index / world.Width);
                            bool plain = KeeperSpellRules.PlaceAllowed(world, database, view, owner, spell, at);
                            Assert.Equal(plain, KeeperSpellRules.PlaceAllowedInRoom(world, database, view, owner, spell, at, room));
                            compared++;
                            if (!plain) refused++;
                        }
            }
            Assert.True(compared > 0, $"{level} has no keeper room tiles to compare");
            Assert.True(refused > 0 && refused < compared, $"{level}: {refused} of {compared} refused, so nothing was told apart");
        }
    }
}
