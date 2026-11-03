// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The creature hover sounds of <c>Hand_UpdateHoverTarget</c> (<c>0x0040C290</c>, pick kind 2): the viewer's own hoverable
/// creature, on game turns that are a multiple of three times the tick rate (12), asks the current action record's word when an
/// action runs, else <c>0xCA</c>, <c>0xCB</c> or <c>0xCC</c> by mood while it holds no room place, in its category with its handle at
/// its position. Ours tells the action only for the ones <see cref="CreatureCurrentAction"/> names (deviation). Docs:
/// sound-effects-continuous.md section 2.</summary>
public sealed class CreatureHoverSoundTests
{
    private static readonly PlayerId Viewer = new(PlayerIds.FirstKeeper);

    private static (GameDatabase Database, WorldState World, CreatureState Goblin) Setup(long tick = 12, byte owner = PlayerIds.FirstKeeper)
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        WorldState world = WorldState.FromMap(database, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!, [0, 1]);
        CreatureState goblin = world.SpawnCreature(database, database.CreatureByName("Goblin")!.CreatureId, owner, new TilePos(3, 3));
        typeof(WorldState).GetProperty(nameof(WorldState.CurrentTick))!.SetValue(world, new GameTick(tick));
        return (database, world, goblin);
    }

    private static WorldHoverHit Hit(CreatureState c) => new(WorldHoverKind.Creature, c.Tile, c.Id);

    [InstallFact]
    public void AnOwnContentCreatureOnATurnThatIsAMultipleOfTwelveAsksCAInItsCategoryWithItsHandleAtItsPosition()
    {
        var (database, world, goblin) = Setup(tick: 24);
        SoundRequest request = CreatureHoverSound.For(world, database, Hit(goblin), Viewer)!.Value;
        Assert.Equal(new DK2.Data.Media.SoundCategory("creature_goblin"), request.Category);
        Assert.Equal(new DK2.Data.Media.SoundId(0xCA), request.Id);
        Assert.Equal(WorldSoundDirector.CreatureVoice(goblin.Id), request.Voice);
        Assert.Equal(new Vector3(goblin.PosX / 4096f, goblin.PosZ / 4096f, -goblin.PosY / 4096f), request.World);
    }

    [InstallTheory]
    [InlineData(1)] [InlineData(11)] [InlineData(13)] [InlineData(23)]
    public void OnAnyOtherTurnItAsksNothing(long tick)
    {
        var (database, world, goblin) = Setup(tick);
        Assert.Null(CreatureHoverSound.For(world, database, Hit(goblin), Viewer));
    }

    [InstallTheory]
    [InlineData(0, 0xCAu)] [InlineData(1, 0xCBu)] [InlineData(2, 0xCCu)]
    public void TheMoodBandChoosesTheSound(int band, uint expected)
    {
        var (database, world, goblin) = Setup();
        CreatureDef def = database.CreatureById(goblin.CreatureTypeId);
        goblin.Anger.Hunger = band switch { 0 => 0, 1 => CreatureAngerRules.UnhappyAt(def), _ => CreatureAngerRules.AngryAt(def) + 1 };
        Assert.Equal((CreatureMood)band, CreatureAngerRules.MoodOf(def, goblin));
        Assert.Equal(new DK2.Data.Media.SoundId(expected), CreatureHoverSound.For(world, database, Hit(goblin), Viewer)!.Value.Id);
    }

    [InstallFact]
    public void AnotherKeepersCreatureAndAnythingButACreatureAreSilent()
    {
        var (database, world, rival) = Setup(owner: PlayerIds.FirstKeeper + 1);
        Assert.Null(CreatureHoverSound.For(world, database, Hit(rival), Viewer));
        var (_, _, own) = Setup();
        Assert.Null(CreatureHoverSound.For(world, database, new WorldHoverHit(WorldHoverKind.Tile, own.Tile), Viewer));
        Assert.Null(CreatureHoverSound.For(world, database, new WorldHoverHit(WorldHoverKind.Creature, rival.Tile, 99999), Viewer));
    }

    [InstallFact]
    public void ACreatureInTheHandOrHoldingARoomPlaceAsksNothingFromTheMoodArm()
    {
        var (database, world, goblin) = Setup();
        goblin.HeldPlace = new RoomPlace(5, 1);
        Assert.Null(CreatureHoverSound.For(world, database, Hit(goblin), Viewer));
        goblin.HeldPlace = RoomPlace.None;
        goblin.InHand = true;
        Assert.Null(CreatureHoverSound.For(world, database, Hit(goblin), Viewer));
    }

    [InstallFact]
    public void ARunningActionWithAWordAsksItsWordEvenInARoom_StandInsNameOnlyTheModelledActions()
    {
        var (database, world, goblin) = Setup();
        goblin.HeldPlace = new RoomPlace(5, 1);
        goblin.TrainingActionId = (byte)TrainingActionKind.Melee;
        Assert.Equal(new DK2.Data.Media.SoundId(0xD3), CreatureHoverSound.For(world, database, Hit(goblin), Viewer)!.Value.Id);
        goblin.TrainingActionId = 0;
        goblin.SwingInProgress = true;   // a melee swing: action 17, 0xD7
        Assert.Equal(new DK2.Data.Media.SoundId(0xD7), CreatureHoverSound.For(world, database, Hit(goblin), Viewer)!.Value.Id);
    }

    [InstallFact]
    public void AnImpsDigAsksItsWordAndWithoutTheActionTheMoodSoundReturns()
    {
        var (database, world, goblin) = Setup();
        goblin.TrainingActionId = (byte)TrainingActionKind.Melee;
        goblin.SwingInProgress = false;
        // Action 30's word is 0xD3; a spell trainee is 31, also 0xD3. An imp's dig (1) carries 0x334: in the Imp's category.
        CreatureState imp = world.SpawnCreature(database, database.CreatureByName("Imp")!.CreatureId, PlayerIds.FirstKeeper, new TilePos(4, 3));
        imp.ActionArtSlot = 5;
        imp.TargetTileIndex = 9;
        imp.TargetWork = TileWorkKind.Dig;
        Assert.Equal(new DK2.Data.Media.SoundId(0x334), CreatureHoverSound.For(world, database, Hit(imp), Viewer)!.Value.Id);
        imp.ActionArtSlot = 0;
        Assert.Equal(new DK2.Data.Media.SoundId(0xCA), CreatureHoverSound.For(world, database, Hit(imp), Viewer)!.Value.Id);
    }
}
