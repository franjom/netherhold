// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public class HeroOpeningOnRealDataTests
{
    [InstallTheory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(21UL)]
    public void TheOpeningDwarvesChooseBesideTheGateBeforeTheirLeaderLeaves(ulong seed)
    {
        string install = Dk2InstallGate.Path;
        const string level = "Level2";
        var db = GameDatabaseLoader.LoadForCampaignLevel(install, level);
        var sim = CampaignSimulationFactory.Create(db,
            GameDatabaseLoader.LoadCampaignMap(install, level)!,
            GameDatabaseLoader.LoadCampaignLevel(install, level)!,
            GameDatabaseLoader.LoadCampaignPlayers(install, level)!,
            GameDatabaseLoader.LoadCampaignThings(install, level)!,
            GameDatabaseLoader.LoadCampaignTriggers(install, level)!, seed);

        sim.Run(4);

        CreatureState[] party = sim.World.Creatures.Where(c => c.PartyId == 1).ToArray();
        Assert.Equal(4, party.Length);
        Assert.All(party, c => Assert.Equal(HeroPartyRules.DwarfCreatureId, c.CreatureTypeId));
        CreatureState leader = Assert.Single(party, c => (c.Flags & 2) != 0);
        Assert.Equal(new TilePos(13, 13), leader.Destination);
        CreatureState[] followers = party.Where(HeroJobRules.IsFollower).ToArray();
        Assert.Equal(new byte[] { 3, 2, 1 }, followers.Select(c => c.PositionInParty));
        Assert.All(followers, c => Assert.Equal(new TilePos(14, 7), c.Destination));
    }
}
