// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The Library's states against the shipped v1.70 data (library-states.md, ADR-0397).
/// Gated with [InstallFact].</summary>
public class LibraryOnRealDataTests
{
    private static GameDatabase Db() => GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "level3");

    /// <summary>The Book Case is floor furniture (slot 0) and the Library Book wall furniture (slot 3),
    /// so a researcher reads at a bookcase in the room and at a book against a wall.</summary>
    [InstallFact]
    public void TheShippedLibraryHasItsBookcaseOnTheFloorAndItsBookOnTheWall()
    {
        IReadOnlyList<byte> ids = Db().Room(RoomTypeId.Library).ObjectIds;
        Assert.Equal(new byte[] { ResearchStep.BookCaseObjectId, 0, 0, ResearchStep.BookObjectId }, ids.Take(4));
    }

    /// <summary>A researcher's read is action 29, art 14; every creature that can research has it.</summary>
    [InstallFact]
    public void EveryResearcherHasTheArtItsReadPlays()
    {
        GameDatabase db = Db();
        var researchers = db.Creatures.Values.Where(c =>
            c.HappyJobs.Concat(c.UnhappyJobs).Concat(c.AngryJobs).Any(j => j.Job == JobType.Research)).ToList();
        Assert.NotEmpty(researchers);
        Assert.All(researchers, c => Assert.True(AnimationTimingRules.HasArt(c, 14), c.Name));
    }
}
