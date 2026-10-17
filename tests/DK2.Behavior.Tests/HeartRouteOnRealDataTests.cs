// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public sealed class HeartRouteOnRealDataTests
{
    [InstallFact]
    public void FirstLevel4DigWalkUsesTheNativeHeartRoute()
    {
        string install = Dk2InstallGate.Path;
        var database = GameDatabaseLoader.LoadForCampaignLevel(install, "Level4");
        var map = GameDatabaseLoader.LoadCampaignMap(install, "Level4")!;
        var players = GameDatabaseLoader.LoadCampaignPlayers(install, "Level4")!;
        var simulation = CampaignSimulationFactory.Create(database, map,
            GameDatabaseLoader.LoadCampaignLevel(install, "Level4")!, players,
            GameDatabaseLoader.LoadCampaignThings(install, "Level4")!,
            GameDatabaseLoader.LoadCampaignTriggers(install, "Level4")!, 1);
        var points = new List<Fixed16Vector>();
        var profile = WorldSteering.ProfileOf(database.CreatureById(1),
            new PlayerId(3), MoveOptions.Plain);

        // Coordinates measured at chooser return; see imp-boundary-route.md.
        Assert.True(simulation.Navigation.Route(new Fixed16Vector(88182 << 4, 223342 << 4),
            new Fixed16Vector(1824064, 3549856), profile, points));
        Assert.Equal(new Fixed16Vector[]
        {
            new(1605632, 3473408), new(1703936, 3538944), new(1824000, 3549696),
        }, points);
    }
}
