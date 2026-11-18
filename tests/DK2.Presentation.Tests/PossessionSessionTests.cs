// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The possessing client a host drives with its input (possession-client.md, ADR-0805).</summary>
public sealed class PossessionSessionTests
{
    [InstallFact]
    public void ObjectDeathRequestRunsTheSharedDeathMoveAndHoldWithoutABody()
    {
        Rig rig = Level1(withOrdinaryCamera: true);
        rig.Frame();
        Assert.False(rig.Session.RequestObjectDeathView(rig.World, rig.Clock));
        rig.Frame(PossessionFlight.BetweenClasses);
        rig.World.Player(Viewer).PossessedCreatureId = 0;
        Assert.True(rig.Session.RequestObjectDeathView(rig.World, rig.Clock));
        Assert.Equal(0, rig.Session.HiddenCreatureId);
        Assert.Equal(200, rig.Session.Flight.Target.Roll);
        Assert.Equal(400, rig.Session.Flight.Target.Pitch);
        rig.Frame(PossessionSession.DeathMove);
        Assert.True(rig.Session.SettledPossessionCamera);
        rig.Frame(PossessionSession.DeathHold + 1);
        Assert.True(rig.Session.Flight.Active);
    }

    [InstallFact]
    public void AReplacementWorldWithTheSameCreatureIdRebindsColourEvents()
    {
        Rig rig = Level1(withOrdinaryCamera: false);
        rig.Frame();
        rig.Frame(256);
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        CreatureState previous = rig.Me;
        rig.World = WorldState.CopyOf(rig.World);
        rig.Me = rig.World.CreatureById(previous.Id)!;
        Assert.NotSame(previous, rig.Me);
        Assert.False(rig.Frame().Entered);
        CreatureDef definition = database.CreatureById(rig.Me.CreatureTypeId);
        CreatureHealthRules.Apply(database, definition, rig.Me, -10, 0);
        CreatureHealthRules.Apply(database, definition, rig.Me, 10, 0);
        rig.Frame();
        rig.Frame(125);
        Assert.Equal(65535, rig.Session.Colours.Change.Current[128]);
        rig.Frame(250);
        CreatureHealthRules.Apply(database, definition, previous, -10, 0);
        rig.Frame();
        rig.Frame(125);
        Assert.Equal(32767, rig.Session.Colours.Change.Current[128]);
    }
    [InstallFact]
    public void AHitTruncatedToZeroStillFlashesRed()
    {
        Rig rig = Level1(withOrdinaryCamera: false);
        rig.Frame();
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        int before = rig.Me.Health;
        int landed = DK2.Simulation.Combat.StatusEffectRules.Hurt(rig.World, db,
            db.CreatureById(rig.Me.CreatureTypeId), rig.Me, 1, 0);
        Assert.Equal(0, landed);
        Assert.Equal(before, rig.Me.Health);
        rig.Frame();
        rig.Frame(125);
        Assert.Equal(65535, rig.Session.Colours.Change.Current[128]);
    }

    [InstallFact]
    public void SessionFeedsDamageThawAndLeaveToTheColourDriver()
    {
        Rig rig = Level1(withOrdinaryCamera: true);
        rig.Frame();
        rig.Frame(1744);
        Assert.True(rig.Session.Colours.Change.Running);
        rig.Frame(256);
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        CreatureDef definition = database.CreatureById(rig.Me.CreatureTypeId);
        // Both changes happen between presentation frames: net-health polling would miss the flash.
        CreatureHealthRules.Apply(database, definition, rig.Me, -10, 0);
        CreatureHealthRules.Apply(database, definition, rig.Me, 10, 0);
        rig.Frame();
        rig.Frame(125);
        Assert.Equal(65535, rig.Session.Colours.Change.Current[128]);
        rig.Frame(250);
        Assert.Equal(32767, rig.Session.Colours.Change.Current[128]);
        rig.Me.FrozenTicks = 1;
        var simulation = new GameSimulation(database, rig.World, 1,
            [new DK2.Simulation.Systems.CreatureStatusSystem()]);
        simulation.Tick();
        Assert.Equal(0, rig.Me.FrozenTicks);
        // A second freeze before the frame must not erase the thaw notification.
        rig.Me.FrozenTicks = 1;
        rig.Frame();
        Assert.True(rig.Session.Colours.Change.Running);
        rig.Frame(500);
        Assert.False(rig.Session.Colours.Change.Running);
        rig.World.Player(Viewer).PossessedCreatureId = 0;
        rig.Me.Possessed = false;
        rig.Frame();
        Assert.True(rig.Session.Colours.Change.Running);
        rig.Frame(256);
        Assert.False(rig.Session.Colours.Change.Running);
    }
    [InstallFact]
    public void ColourSubscriptionChangesNoHashAndResetStopsObserving()
    {
        Rig rig = Level1(withOrdinaryCamera: false);
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var simulation = new GameSimulation(database, rig.World, 1, []);
        ulong before = simulation.StateHash();
        rig.Frame();
        Assert.Equal(before, simulation.StateHash());
        rig.Session.Reset();
        CreatureHealthRules.Apply(database, database.CreatureById(rig.Me.CreatureTypeId), rig.Me, -1, 0);
        rig.World.Player(Viewer).PossessedCreatureId = 0;
        rig.Frame(3000);
        Assert.NotEqual(65535, rig.Session.Colours.Change.Current[128]);
    }
    private static readonly PlayerId Viewer = new(3);
    private static readonly CameraViewport Screen = new(1920, 1920, 1080);

    private sealed class Rig
    {
        public required WorldState World;
        public required CreatureState Me;
        public required PossessionSession Session;
        public ClassicCamera? Ordinary;
        public Vector3 Focus = new(4.5f, 1, -4.5f);
        public readonly List<PlayerCommand> Commands = [];
        public readonly List<SoundRequest> Sounds = [];
        public long Clock = 1000;

        public PossessionUpdate Frame(int milliseconds = 20, bool allowed = true, bool primary = false, bool secondary = false,
            Vector2 pointer = default, params GameAction[] held)
        {
            Clock += milliseconds;
            var keys = new GameBindingInput(new GameSettings(), KeyModifiers.None,
                code => held.Any(action => DefaultKeyBindings.Get(action).ScanCode == code), _ => false);
            PossessionUpdate update = Session.Update(World, Viewer,
                new PossessionInput(allowed, Clock, milliseconds, pointer, primary, secondary, keys, 10, false),
                Ordinary, Focus, Commands, Sounds);
            if (update.OrdinaryFocus is Vector3 focus) Focus = focus;
            return update;
        }
    }

    private static Rig Level1(bool withOrdinaryCamera)
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        // A plain open tile with plain open ground on all four sides.
        TilePos open = default;
        for (int y = 2; y < world.Height - 2 && open == default; y++)
            for (int x = 2; x < world.Width - 2; x++)
                if (Plain(x, y) && Plain(x - 1, y) && Plain(x + 1, y) && Plain(x, y - 1) && Plain(x, y + 1))
                {
                    open = new TilePos(x, y);
                    break;
                }
        byte goblin = db.Creatures.Values.Single(c => c.Name == "Goblin").CreatureId;
        CreatureState me = world.SpawnCreature(db, goblin, Viewer, open);
        me.Body.Radius = SteeringMover.CrowdWordOf(db.CreatureById(goblin).FootprintRadius.Raw);
        WorldSteering.SyncBody(me);
        me.Possessed = true;
        world.Player(Viewer).PossessedCreatureId = me.Id;
        IReadOnlyList<CameraThing> presets = GameDatabaseLoader.LoadCameraPresets(Dk2InstallGate.Path, null);
        return new Rig
        {
            World = world, Me = me,
            Session = new PossessionSession(db, presets.FirstOrDefault(c => c.CameraId == PossessionCamera.PresetId)),
            Ordinary = withOrdinaryCamera ? new ClassicCamera(presets.First(c => c.CameraId == 3)) { ReferenceScreenWidth = 1600 } : null,
        };

        bool Plain(int x, int y)
        {
            TerrainDef terrain = db.TerrainById(world.Tile(x, y).TerrainId);
            return !terrain.IsSolid && !terrain.IsWater && !terrain.IsLava && db.RoomByTerrain(world.Tile(x, y).TerrainId) is null;
        }
    }

    [InstallFact]
    public void TheMoveInHoldsTheStepThePoseAndTheModelUntilItLands_StronglyInferredFromBinary()
    {
        Rig rig = Level1(withOrdinaryCamera: true);

        PossessionUpdate first = rig.Frame();

        Assert.True(first.Entered);
        Assert.True(rig.Session.CameraHeld);
        Assert.True(PossessionScene.RoofShown(rig.Session));
        Assert.False(rig.Session.SettledPossessionCamera);
        Assert.True(rig.Session.PointerLockWanted);
        Assert.Equal(0, rig.Session.HiddenCreatureId);
        Assert.Null(rig.Session.TurnPose(Viewer));
        Assert.True(rig.Session.TryCamera(rig.Ordinary, Screen, out ClassicCameraPose start));
        Assert.Equal(rig.Ordinary!.Pose(rig.Focus, Screen), start);

        int x = rig.Session.View.X;
        rig.Frame(1000, held: [GameAction.ScrollUp, GameAction.Fire]);
        Assert.Equal(x, rig.Session.View.X);
        Assert.Empty(rig.Commands);

        rig.Frame(1000, held: [GameAction.Fire]);

        Assert.False(rig.Session.Flight.Active);
        Assert.True(rig.Session.SettledPossessionCamera);
        Assert.Equal(12f, rig.Session.PossessionViewDistance);
        Assert.Equal(rig.Me.Id, rig.Session.HiddenCreatureId);
        Assert.IsType<MovePossessedCreatureCommand>(rig.Session.TurnPose(Viewer));
        Assert.True(rig.Session.TryCamera(rig.Ordinary, Screen, out ClassicCameraPose eye));
        Assert.True(Vector3.Distance(PossessionCamera.Pose(rig.Session.View).Position, eye.Position) < 1e-5f);
        Assert.Equal(0f, eye.ProjectionCentreShift);
        // Fire was held through the landing: it is not a press.
        rig.Frame(held: [GameAction.Fire]);
        Assert.Empty(rig.Commands);
    }

    [InstallFact]
    public void WithoutAnOrdinaryCameraTheViewCutsAndTheKeysAct()
    {
        Rig rig = Level1(withOrdinaryCamera: false);

        rig.Frame();

        Assert.Equal(rig.Me.Id, rig.Session.HiddenCreatureId);
        Assert.True(rig.Session.TryCamera(null, Screen, out ClassicCameraPose eye));
        Assert.True(Vector3.Distance(PossessionCamera.Forward(rig.Session.View.Yaw, PossessionView.Horizon), eye.Forward) < 1e-5f);
        Assert.True(Vector3.Distance(Vector3.UnitY, eye.Up) < 1e-5f);

        rig.Frame(held: [GameAction.Fire, GameAction.Run]);
        rig.Frame(held: [GameAction.Fire, GameAction.Run]);
        Assert.IsType<ActPossessedCreatureCommand>(Assert.Single(rig.Commands));

        rig.Commands.Clear();
        rig.Frame(primary: true);
        rig.Frame(secondary: true);
        Assert.Equal([typeof(ActPossessedCreatureCommand), typeof(LeavePossessionCommand)], rig.Commands.Select(c => c.GetType()));

        rig.Commands.Clear();
        rig.Frame(allowed: false, primary: true, secondary: true, held: [GameAction.Sniper]);
        Assert.Empty(rig.Commands);
        Assert.False(rig.Session.PointerLockWanted);
    }

    [InstallFact]
    public void CreepIsSentOnceAChangeAndTheLookSkipsTheFrameThePointerIsFirstHeld()
    {
        Rig rig = Level1(withOrdinaryCamera: false);

        rig.Frame(pointer: new Vector2(50, 0));
        int yaw = rig.Session.View.Yaw;
        rig.Frame(pointer: new Vector2(50, 0));
        Assert.NotEqual(yaw, rig.Session.View.Yaw);

        rig.Frame(held: [GameAction.Creep]);
        rig.Frame(held: [GameAction.Creep]);
        Assert.Equal(new SetCreepingCommand(Viewer, true), Assert.Single(rig.Commands));
    }

    /// <summary>Camera mode 11: a possessed creature that dies and leaves its body sends the camera
    /// beside it for 3000 ms with the view running down to nothing, holds 2000 ms, looks straight
    /// down and only then flies back; the body stays out of the picture until that move back.</summary>
    [InstallFact]
    public void ADeathThatLeavesItsBodyPlaysTheDeathViewBeforeFlyingBack_StronglyInferredFromBinary()
    {
        Rig rig = Level1(withOrdinaryCamera: true);
        rig.Frame();
        rig.Frame(2000);
        rig.Me.Health = 0;
        rig.World.Player(Viewer).PossessedCreatureId = 0;

        PossessionUpdate ended = rig.Frame();

        Assert.True(ended.Ended);
        Assert.Null(ended.OrdinaryFocus);
        Assert.True(rig.Session.CameraHeld);
        Assert.True(PossessionScene.RoofShown(rig.Session));
        Assert.False(rig.Session.SettledPossessionCamera);
        Assert.Equal(rig.Me.Id, rig.Session.HiddenCreatureId);
        CameraFlightState target = rig.Session.Flight.Target;
        Assert.Equal(0, target.ViewDistance);
        Assert.InRange(target.Pitch, 0x300, 0x4FF);
        Assert.True(target.Roll is <= 0x7F or >= 0x780);
        Assert.Equal(0x1100, target.Z);
        int eye = rig.Session.Flight.Target.X - rig.Me.PosX, side = rig.Session.Flight.Target.Y - rig.Me.PosY;
        Assert.InRange(Math.Sqrt(((double)eye * eye) + ((double)side * side)), 1, 0x1000);

        rig.Frame(PossessionSession.DeathMove);
        Assert.False(rig.Session.Flight.Active);
        Assert.True(rig.Session.SettledPossessionCamera);
        Assert.Equal(0f, rig.Session.PossessionViewDistance);
        Assert.True(rig.Session.TryCamera(rig.Ordinary, Screen, out ClassicCameraPose dark));
        Assert.Equal(0f, dark.FarClipDistance);
        Assert.Equal(0f, dark.ProjectionCentreShift);

        // The hold: nothing moves, the body stays hidden, the ordinary camera is not asked for yet.
        Assert.Null(rig.Frame(PossessionSession.DeathHold).OrdinaryFocus);
        Assert.Equal(rig.Me.Id, rig.Session.HiddenCreatureId);

        PossessionUpdate back = rig.Frame();
        Assert.NotNull(back.OrdinaryFocus);
        Assert.False(rig.Session.SettledPossessionCamera);
        Assert.Equal(0, rig.Session.HiddenCreatureId);
        Assert.Equal(0, rig.Session.Flight.State.Pitch);
        Assert.True(rig.Session.CameraHeld);
        Assert.True(PossessionScene.RoofShown(rig.Session));

        rig.Frame(2000);
        Assert.False(rig.Session.CameraHeld);
        Assert.False(PossessionScene.RoofShown(rig.Session));
    }

    [InstallFact]
    public void ADeathDuringTheMoveInFliesStraightBack_StronglyInferredFromBinary()
    {
        Rig rig = Level1(withOrdinaryCamera: true);
        rig.Frame();
        rig.Frame(500);
        rig.Me.Health = 0;
        rig.World.Player(Viewer).PossessedCreatureId = 0;

        Assert.NotNull(rig.Frame().OrdinaryFocus);
        Assert.Equal(0, rig.Session.HiddenCreatureId);
    }

    [InstallFact]
    public void AnEndedPossessionFliesBackToTheOrdinaryCameraOverTheEye_StronglyInferredFromBinary()
    {
        Rig rig = Level1(withOrdinaryCamera: true);
        rig.Frame();
        rig.Frame(2000);
        rig.World.Player(Viewer).PossessedCreatureId = 0;

        PossessionUpdate ended = rig.Frame();

        Assert.True(ended.Ended);
        Assert.Equal(new Vector3(rig.Me.PosX / 4096f, 1, -rig.Me.PosY / 4096f), ended.OrdinaryFocus);
        Assert.False(rig.Session.Possessing);
        Assert.False(rig.Session.SettledPossessionCamera);
        Assert.True(rig.Session.CameraHeld);
        Assert.True(PossessionScene.RoofShown(rig.Session));
        Assert.False(rig.Session.PointerLockWanted);
        Assert.Equal(0, rig.Session.HiddenCreatureId);
        Assert.Null(rig.Session.TurnPose(Viewer));
        Assert.True(rig.Session.TryCamera(rig.Ordinary, Screen, out _));

        rig.Frame(2000);

        Assert.False(rig.Session.CameraHeld);
        Assert.False(PossessionScene.RoofShown(rig.Session));
        Assert.False(rig.Session.TryCamera(rig.Ordinary, Screen, out _));
        Assert.Equal((rig.Me.Heading + 4) & 0x7F8, rig.Ordinary!.State(rig.Focus).Yaw);
    }
}
