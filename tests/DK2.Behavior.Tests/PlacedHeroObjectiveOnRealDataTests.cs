// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>A placed Good creature's standing job is its record's Objective triple, as the level
/// loader's <c>0x0048BE20</c> call at <c>0x0052077C</c> sets it (ADR-0344). Level 2 ships eight
/// such creatures, four Wait and four SendToActionPoint; Levels 1 and 3 ship none.</summary>
public class PlacedHeroObjectiveOnRealDataTests
{
    [InstallFact]
    public void PlacedGoodCreaturesTakeTheirRecordsObjectiveAsTheirStandingJob()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "level2");
        LevelMap map = GameDatabaseLoader.LoadCampaignMap(install, "level2")!;
        List<ThingRecord> things = GameDatabaseLoader.LoadCampaignThings(install, "level2")!;
        List<PlayerDef> players = GameDatabaseLoader.LoadCampaignPlayers(install, "level2")!;
        WorldState world = WorldState.FromMap(db, map, [.. players.Select(p => p.PlayerId)]);

        ThingProjection.Place(world, db, things);

        var expected = things.OfType<GoodCreatureThing>()
            .Select(t => ((JobType)t.Objective, t.ObjectiveTargetPlayerId, t.ObjectiveTargetActionPointId))
            .OrderBy(j => j).ToList();
        var actual = world.Creatures.Where(c => c.OwnerPlayerId == PlayerIds.Good)
            .Select(c => (c.GoodJob, c.GoodJobPlayerId, c.GoodJobActionPointId))
            .OrderBy(j => j).ToList();

        Assert.Equal(8, expected.Count);
        Assert.Equal(4, expected.Count(j => j.Item1 == JobType.Wait));
        Assert.Equal(4, expected.Count(j => j.Item1 == JobType.SendToActionPoint));
        Assert.Equal(expected, actual);
    }

    [InstallTheory]
    [InlineData("level1")]
    [InlineData("level3")]
    public void LevelsOneAndThreePlaceNoGoodCreature_SoTheirReplaysCannotMove(string level)
    {
        string install = Dk2InstallGate.Path;
        List<ThingRecord> things = GameDatabaseLoader.LoadCampaignThings(install, level)!;

        Assert.Empty(things.OfType<GoodCreatureThing>());
    }
}
