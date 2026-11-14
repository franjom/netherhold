// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The possessor's selection byte and what Fire sends (possession-client.md §4).</summary>
public sealed class PossessionControlsTests
{
    private static readonly PlayerId Keeper = new(3);

    [Fact]
    public void MeleeKeepsTheSlotAndFollowersClearMelee_StronglyInferredFromBinary()
    {
        var controls = new PossessionControls();
        Assert.True(controls.Melee);

        controls.SelectFollowers();
        Assert.Equal((false, PossessedSlot.FollowerArm), (controls.Melee, controls.Slot));

        controls.SelectMelee();
        Assert.Equal((true, PossessedSlot.FollowerArm), (controls.Melee, controls.Slot));
    }

    [InstallFact]
    public void ASpellIsSelectedOnlyOnceItsLevelIsReached_StronglyInferredFromBinary()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        CreatureDef warlock = db.Creatures.Values.Single(c => c.Name == "Warlock");
        int slot = Enumerable.Range(0, warlock.Spells.Count).First(i => warlock.Spells[i].CreatureSpellId != 0);
        int level = warlock.Spells[slot].LevelAvailable;
        var controls = new PossessionControls();

        controls.SelectSpell(slot, warlock, level - 1);
        Assert.True(controls.Melee);
        controls.SelectSpell(7, warlock, 10);
        Assert.True(controls.Melee);

        controls.SelectSpell(slot, warlock, level);
        Assert.Equal((false, slot), (controls.Melee, controls.Slot));
    }

    [Fact]
    public void FireAndThePoseCarryTheCamera_StronglyInferredFromBinary()
    {
        var view = new PossessionView();
        var body = new PossessedBody(100, 200, 50, false, false, false, false, 0x200, 0x600, 0, 0, 4, false, 0x155);
        view.Enter(0x5800, 0x6800, 0x1000, body);
        var controls = new PossessionControls();

        ActPossessedCreatureCommand fire = controls.Fire(Keeper, view);
        MovePossessedCreatureCommand pose = PossessionControls.Pose(Keeper, view, body);

        Assert.Equal((0x5800, 0x6800, 0x1600), (fire.Aim.Origin.X.Raw, fire.Aim.Origin.Y.Raw, fire.Aim.Origin.Z.Raw));
        Assert.Equal((0x155, PossessionView.Horizon, true, 0), (fire.Aim.Yaw.Value, fire.Aim.Pitch.Value, fire.Melee, fire.Slot.Value));
        Assert.Equal((0x5800, 0x6800, 0x600, 0x155),
            (pose.Pose.Position.X.Raw, pose.Pose.Position.Y.Raw, pose.Pose.Position.Z.Raw, pose.Pose.Heading.Value));
    }
}
