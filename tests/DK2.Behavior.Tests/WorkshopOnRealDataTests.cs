// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>The Workshop's states against the shipped v1.70 data (workshop-states.md, ADR-0399).
/// Gated with [InstallFact].</summary>
public class WorkshopOnRealDataTests
{
    private static GameDatabase Db() => GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "level3");

    /// <summary>Every piece the shipped Workshop places is a machine the finder looks for (15, 16, 46).</summary>
    [InstallFact]
    public void EveryShippedWorkshopPieceIsAMachine()
    {
        IReadOnlyList<byte> ids = Db().Room(RoomTypeId.Workshop).ObjectIds;
        Assert.All(ids.Where(id => id != 0), id => Assert.True(WorkshopStep.IsMachine(id), $"object {id}"));
    }

    /// <summary>Action 32 plays art 22, or its fallback 5; every creature that manufactures has one.</summary>
    [InstallFact]
    public void EveryManufacturerHasTheArtItsWorkPlays()
    {
        GameDatabase db = Db();
        var makers = db.Creatures.Values.Where(c =>
            c.HappyJobs.Concat(c.UnhappyJobs).Concat(c.AngryJobs).Any(j => j.Job == JobType.Manufacture)).ToList();
        Assert.NotEmpty(makers);
        Assert.All(makers, c => Assert.True(AnimationTimingRules.ResolvesPresent(c, WorkshopStep.WorkArtSlot), c.Name));
    }
}
