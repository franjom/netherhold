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
    [InstallTheory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void PickLockRequiresBothTheCameraPointAndCreatureTileToBePrison(bool cameraInPrison, bool creatureInPrison, bool opens)
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        byte prison = db.Rooms.Values.Single(r => r.TypeId == RoomTypeId.Prison).TerrainId;
        byte ordinary = db.Terrain.Values.First(t => db.RoomByTerrain(t.TerrainId) == null).TerrainId;
        TilePos cameraTile = new(4, 4), creatureTile = new(6, 4);
        world.Tile(cameraTile).TerrainId = cameraInPrison ? prison : ordinary;
        world.Tile(creatureTile).TerrainId = creatureInPrison ? prison : ordinary;
        CreatureDef warlock = db.Creatures.Values.Single(c => c.Name == "Warlock");
        CreatureState creature = world.SpawnCreature(db, warlock.CreatureId, Keeper, creatureTile);
        var view = new PossessionView();
        view.Enter((cameraTile.X << 12) + 2048, (cameraTile.Y << 12) + 2048, 0,
            new PossessedBody(0, 0, 0, false, false, false, false, 0, 0, 0, 0, 0, false, 0));
        OpenPossessedPrisonCommand? command = PossessionControls.PickLock(world, db, Keeper, view, creature);
        Assert.Equal(opens, command != null);
        if (command != null) Assert.Equal(creatureTile, command.PrisonAt);
    }
    [InstallFact]
    public void SpellAndFollowerKeysClearHypnosisBeforeFireAsTheKeyCallersMaskF9()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        CreatureDef vampire = db.Creatures.Values.Single(c => c.Name == "Vampire");
        CreatureDef warlock = db.Creatures.Values.Single(c => c.Name == "Warlock");
        int slot = Enumerable.Range(0, warlock.Spells.Count).First(i => warlock.Spells[i].CreatureSpellId != 0);
        var controls = new PossessionControls();
        controls.SelectSecondary(vampire);
        controls.SelectSpell(slot, warlock, 10);
        Assert.Equal(0, controls.Selection & 6);
        Assert.Equal(slot, Assert.IsType<ActPossessedCreatureCommand>(controls.SubmitFire(Keeper, new PossessionView(), vampire)).Slot.Value);
        controls.SelectSecondary(vampire);
        controls.SelectFollowers();
        Assert.Equal(0, controls.Selection & 6);
        Assert.Equal(PossessedSlot.FollowerArm, Assert.IsType<ActPossessedCreatureCommand>(controls.SubmitFire(Keeper, new PossessionView(), vampire)).Slot.Value);
    }
    [InstallFact]
    public void SniperRequiresItsDefinitionMultiplierAndAnUnlockedSpell()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        CreatureDef elf = db.Creatures.Values.Single(c => c.Name == "Dark Elf");
        Assert.True(PossessionControls.CanToggleSniper(elf, 10));
        Assert.False(PossessionControls.CanToggleSniper(elf with { SniperZoom = new Fp32(0x1FFF) }, 10));
        Assert.False(PossessionControls.CanToggleSniper(elf with { Spells = [] }, 10));
        Assert.False(PossessionControls.CanToggleSniper(elf with
        {
            Spells = elf.Spells.Select(s => s with { LevelAvailable = 10 }).ToArray(),
        }, 9));
    }
    [InstallFact]
    public void SecondaryAbilitySelectsHypnosisAndPrimarySneakUsesTheCameraTile()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        CreatureDef vampire = db.Creatures.Values.Single(c => c.Name == "Vampire");
        var controls = new PossessionControls();
        controls.SelectSecondary(vampire);
        Assert.False(controls.Melee);
        Assert.Equal(4, controls.Selection);
        Assert.IsType<HypnotiseCommand>(controls.SubmitFire(Keeper, new PossessionView(), vampire));
        CreatureDef rogue = db.Creatures.Values.Single(c => c.Name == "Rogue");
        var view = new PossessionView();
        var body = new PossessedBody(100, 200, 50, false, false, false, false, 0x200, 0x600, 0, 0, 4, false, 0);
        view.Enter(0x5800, 0x6800, 0x1000, body);
        ToggleSneakCommand sneak = Assert.IsType<ToggleSneakCommand>(controls.UsePrimary(Keeper, view, rogue));
        Assert.Equal((5, 6), (sneak.TileX, sneak.TileY));
    }
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
        controls.SelectMelee();

        ActPossessedCreatureCommand fire = controls.Fire(Keeper, view);
        MovePossessedCreatureCommand pose = PossessionControls.Pose(Keeper, view, body);

        Assert.Equal((0x5800, 0x6800, 0x1600), (fire.Aim.Origin.X.Raw, fire.Aim.Origin.Y.Raw, fire.Aim.Origin.Z.Raw));
        Assert.Equal((0x155, PossessionView.Horizon, true, 0), (fire.Aim.Yaw.Value, fire.Aim.Pitch.Value, fire.Melee, fire.Slot.Value));
        Assert.Equal((0x5800, 0x6800, 0x600, 0x155),
            (pose.Pose.Position.X.Raw, pose.Pose.Position.Y.Raw, pose.Pose.Position.Z.Raw, pose.Pose.Heading.Value));
    }
}
