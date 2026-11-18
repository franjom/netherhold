// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class PossessionHudViewTests
{
    [InstallFact]
    public void HypnosisHighlightRequiresAHeldTargetRatherThanSelectionAlone()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var viewer = new PlayerId(3);
        WorldState world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        CreatureDef definition = db.Creatures.Values.First(d => (byte)d.SecondaryAbility == 5);
        CreatureState creature = world.SpawnCreature(db, definition.CreatureId, viewer, new TilePos(4, 4));
        world.Player(viewer).PossessedCreatureId = creature.Id;
        var controls = new PossessionControls();
        controls.SelectSecondary(definition);
        var view = new PossessionHudView(db, new InstallPossessionHudArt(Dk2InstallGate.Path));
        Assert.False(view.Capture(world, viewer, controls)!.Actions.Single(a => a.Image.EndsWith("hypnotise-mode")).Selected);
        creature.HypnotisedCreatureId = 77;
        Assert.True(view.Capture(world, viewer, controls)!.Actions.Single(a => a.Image.EndsWith("hypnotise-mode")).Selected);
    }

    [InstallTheory]
    [InlineData(3)]
    [InlineData(7)]
    public void PersistentPrimaryAbilityStateHighlightsItsIcon(int kind)
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var viewer = new PlayerId(3);
        WorldState world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        CreatureDef definition = db.Creatures.Values.First(d => (byte)d.PrimaryAbility == kind);
        CreatureState creature = world.SpawnCreature(db, definition.CreatureId, viewer, new TilePos(4, 4));
        world.Player(viewer).PossessedCreatureId = creature.Id;
        creature.PossessionFlightForm = kind == 3;
        creature.PossessionPraying = kind == 7;
        var view = new PossessionHudView(db, new InstallPossessionHudArt(Dk2InstallGate.Path));
        string suffix = kind == 3 ? "bat-mode" : "pray-mode";
        Assert.True(view.Capture(world, viewer, new PossessionControls())!.Actions.Single(a => a.Image.EndsWith(suffix)).Selected);
    }

    [InstallFact]
    public void SniperCreatureAndFrozenFiltersRemainIndependentAndOrdered()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var viewer = new PlayerId(3);
        WorldState world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        CreatureState creature = world.SpawnCreature(db, 13, viewer, new TilePos(4, 4));
        world.Player(viewer).PossessedCreatureId = creature.Id;
        creature.SniperMode = true;
        creature.FrozenTicks = 10;
        var view = new PossessionHudView(db, new InstallPossessionHudArt(Dk2InstallGate.Path));
        Assert.Equal(new[] { "GUI\\Filters\\F-Sniper", "GUI\\Filters\\F-Knight", "GUI\\Filters\\F-Frozen" },
            view.Capture(world, viewer, new PossessionControls())!.Overlays);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(5, 10, 50)]
    [InlineData(10, 10, 100)]
    [InlineData(30, 10, 100)]
    [InlineData(0, 0, 100)]
    public void RechargeUsesHudClockWithoutTheWorkRateDivisor(int elapsed, int interval, int expected) =>
        Assert.Equal(expected, PossessionHudView.Recharge(new GameTick(100 + elapsed), 100, interval));

    [InstallFact]
    public void SnapshotHasCarriedGoldAndOnlyUnlockedSpellsAndOwnFollowers()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var viewer = new PlayerId(3);
        WorldState world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        CreatureDef warlock = db.Creatures.Values.Single(d => d.Name == "Warlock");
        CreatureState me = world.SpawnCreature(db, warlock.CreatureId, viewer, new TilePos(4, 4));
        me.GoldHeld = 1350;
        me.Possessed = true;
        world.Player(viewer).PossessedCreatureId = me.Id;
        var art = new InstallPossessionHudArt(Dk2InstallGate.Path);
        var hud = new PossessionHudView(db, art);
        var controls = new PossessionControls();
        PossessionHudSnapshot initial = Assert.IsType<PossessionHudSnapshot>(hud.Capture(world, viewer, controls));
        Assert.Equal(new GoldAmount(1350), initial.Gold);
        Assert.Equal(100, initial.HealthPercent);
        Assert.Equal(warlock.Spells.Take(3).Count(s => s.CreatureSpellId != 0 && s.LevelAvailable <= me.Level),
            initial.Actions.Count(a => a.Spell));
        Assert.Empty(initial.Followers);
        CreatureState follower = world.SpawnCreature(db, warlock.CreatureId, viewer, new TilePos(5, 4));
        follower.FollowLeaderId = me.Id;
        Assert.Single(hud.Capture(world, viewer, controls)!.Followers);
        world.Player(viewer).PossessedCreatureId = 0;
        Assert.Null(hud.Capture(world, viewer, controls));
    }
}
