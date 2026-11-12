// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The route is decided on the point asked for (ADR-0755): a live Heart's centre carries the
/// builder's lava byte (Q601), so a walker that cannot cross lava gets no route there, as DK2's
/// Level 5 Dwarf 435 got none on turn 1817 and tunnelled. A platform tile beside it still routes.</summary>
public class HeartCentreRouteOnRealDataTests
{
    private const byte DwarfCreatureId = 14;

    [InstallFact]
    public void AWalkerGetsNoRouteToALiveHeartsLavaCentre_ButOneToItsPlatform()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "level5");
        List<PlayerDef> players = GameDatabaseLoader.LoadCampaignPlayers(install, "level5")!;
        WorldState world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "level5")!, players.Select(p => p.PlayerId));
        ThingProjection.Place(world, db, GameDatabaseLoader.LoadCampaignThings(install, "level5")!);
        NavStamp centre = Assert.Single(world.NavStamps, s => s.Byte == 3);
        int cx = centre.Tile % world.Width, cy = centre.Tile / world.Width;
        Assert.False((db.CreatureById(DwarfCreatureId).Flags & CreatureFlags.CanWalkOnLava) == CreatureFlags.CanWalkOnLava);

        // Due west: ADR-0439's edge point for the centre lies on the platform tile beside it.
        CreatureState dwarf = world.SpawnCreature(db, DwarfCreatureId, new PlayerId(PlayerIds.Good), new TilePos(cx - 2, cy));
        var navigation = new WorldSteering(db, world);
        var raw = new Fixed16Vector(CreatureState.CentreOf(cx) << 4, CreatureState.CentreOf(cy) << 4);
        var from = new Fixed16Vector(dwarf.PosX << 4, dwarf.PosY << 4);
        Assert.Equal(cx - 1, navigation.ConstrainTarget(from, raw).Tile().X);
        Assert.False(navigation.TryRouteTo(dwarf, CreatureState.CentreOf(cx), CreatureState.CentreOf(cy), MoveOptions.Plain));
        Assert.True(navigation.TryRouteTo(dwarf, CreatureState.CentreOf(cx + 1), CreatureState.CentreOf(cy), MoveOptions.Plain));
    }
}
