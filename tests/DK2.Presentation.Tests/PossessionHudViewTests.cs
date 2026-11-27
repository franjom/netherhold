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
    public void NewCreatureMinusOneRechargeMarkersUseNativeZeroWhenCaptured()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var viewer = new PlayerId(3);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        CreatureDef definition = db.Creatures.Values.Single(d => d.Name == "Warlock");
        CreatureState creature = world.SpawnCreature(db, definition.CreatureId, viewer, new TilePos(4, 4));
        Assert.Equal(-1, creature.TimeLastUsedMelee);
        Assert.All(creature.SpellStamps, stamp => Assert.Equal(-1, stamp));
        world.Player(viewer).PossessedCreatureId = creature.Id;
        PossessionHudSnapshot snapshot = new PossessionHudView(db, new InstallPossessionHudArt(Dk2InstallGate.Path))
            .Capture(world, viewer, new PossessionControls())!;
        Assert.Equal(0, snapshot.Actions[0].RechargePercent);
        Assert.Contains(snapshot.Actions, action => action.Spell);
        Assert.All(snapshot.Actions.Where(action => action.Spell), action => Assert.Equal(0, action.RechargePercent));
    }

    [InstallTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlackKnightVisorAppearsOnlyInSettledFirstPerson(bool settled)
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var viewer = new PlayerId(3);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        CreatureState creature = world.SpawnCreature(db, 22, viewer, new TilePos(4, 4));
        world.Player(viewer).PossessedCreatureId = creature.Id;
        var view = new PossessionHudView(db, new InstallPossessionHudArt(Dk2InstallGate.Path));

        PossessionHudSnapshot snapshot = view.Capture(world, viewer, new PossessionControls(),
            firstPersonCameraSettled: settled)!;

        Assert.Equal(settled, snapshot.Overlays.Contains("GUI\\Filters\\F-Black_Knight"));
        Assert.NotEmpty(snapshot.Portrait);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 50)]
    [InlineData(10, 100)]
    public void NeverUsedRechargeStampStillUsesElapsedLevelTime(int tick, int percent)
    {
        Assert.Equal(percent, PossessionHudView.Recharge(new GameTick(tick), 0, 10));
    }

    [InstallFact]
    public void UnbuiltBookCarryStateDoesNotLightTheThrowIconFromHypnosisSelection()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var viewer = new PlayerId(3);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        CreatureDef carrier = db.Creatures.Values.First(d => (byte)d.PrimaryAbility == 1);
        CreatureState creature = world.SpawnCreature(db, carrier.CreatureId, viewer, new TilePos(4, 4));
        world.Player(viewer).PossessedCreatureId = creature.Id;
        var controls = new PossessionControls();
        controls.SelectSecondary(db.Creatures.Values.Single(d => d.Name == "Vampire"));
        PossessionHudIcon icon = new PossessionHudView(db, new InstallPossessionHudArt(Dk2InstallGate.Path))
            .Capture(world, viewer, controls)!.Actions.Single(a => a.Role == PossessionHudIconRole.PrimaryAbility);
        Assert.EndsWith("throw-mode", icon.Image);
        Assert.False(icon.Selected);
    }
    [InstallTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedLockedOrEmptySpellSlotsKeepLaterSpellsInTheirNativeColumns(bool emptyFirstSlot)
    {
        GameDatabase original = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        CreatureDef warlock = original.Creatures.Values.Single(d => d.Name == "Warlock");
        CreatureSpellSlot first = warlock.Spells.First(s => s.CreatureSpellId != 0);
        CreatureDef definition = warlock with { Spells = [
            first with { CreatureSpellId = emptyFirstSlot ? (byte)0 : first.CreatureSpellId, LevelAvailable = emptyFirstSlot ? (byte)1 : (byte)2 },
            first with { LevelAvailable = 1 }, first with { CreatureSpellId = 0 }] };
        var database = new GameDatabase(original.Terrain.Values, original.Rooms.Values, [definition], original.Variables,
            creatureSpells: original.CreatureSpells.Values);
        var viewer = new PlayerId(3);
        var world = WorldState.FromMap(original, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        CreatureState creature = world.SpawnCreature(original, warlock.CreatureId, viewer, new TilePos(4, 4));
        creature.Level = 1;
        world.Player(viewer).PossessedCreatureId = creature.Id;
        var controls = new PossessionControls();
        controls.SelectSpell(1, definition, creature.Level);
        var view = new PossessionHudView(database, new InstallPossessionHudArt(Dk2InstallGate.Path));
        PossessionHudSnapshot snapshot = view.Capture(world, viewer, controls)!;
        Assert.Equal("", snapshot.Actions[1].Image);
        Assert.False(snapshot.Actions[1].Selected);
        var frame = new HudFrame();
        new PossessionHud().BuildCreaturePanel(new HudScaleLayout(1600, 1200, 100), snapshot, frame);
        string image = original.CreatureSpells[first.CreatureSpellId].GuiIcon.Name;
        HudDraw spell = Assert.Single(frame.Draws.ToArray(), draw => draw.Image.Path == image && draw.Kind == HudDrawKind.Quad);
        Assert.Equal(1684, spell.Rect.X);
        Assert.Equal(8, spell.Rect.Y);
        Assert.Single(snapshot.Actions, action => action.Spell);
    }

    [InstallFact]
    public void HypnosisHighlightRequiresAHeldTargetRatherThanSelectionAlone()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var viewer = new PlayerId(3);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
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
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        CreatureDef definition = db.Creatures.Values.First(d => (byte)d.PrimaryAbility == kind);
        CreatureState creature = world.SpawnCreature(db, definition.CreatureId, viewer, new TilePos(4, 4));
        world.Player(viewer).PossessedCreatureId = creature.Id;
        creature.PossessionFlightForm = kind == 3;
        creature.PossessionPraying = kind == 7;
        var view = new PossessionHudView(db, new InstallPossessionHudArt(Dk2InstallGate.Path));
        string suffix = kind == 3 ? "bat-mode" : "pray-mode";
        PossessionHudIcon icon = view.Capture(world, viewer, new PossessionControls())!.Actions.Single(a => a.Image.EndsWith(suffix));
        Assert.True(icon.Selected);
        Assert.Equal(PossessionHudIconRole.PrimaryAbility, icon.Role);
    }

    [InstallFact]
    public void SniperCreatureAndFrozenFiltersRemainIndependentAndOrdered()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var viewer = new PlayerId(3);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
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
    public void RechargeUsesHudClockWithoutTheWorkRateDivisor(int elapsed, int interval, int expected)
    {
        Assert.Equal(expected, PossessionHudView.Recharge(new GameTick(100 + elapsed), 100, interval));
    }

    [InstallFact]
    public void SnapshotHasCarriedGoldAndOnlyUnlockedSpellsAndOwnFollowers()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var viewer = new PlayerId(3);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
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
        Assert.Equal("gui\\rooms\\Treasury", initial.CarriedGoldIcon, ignoreCase: true);
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
