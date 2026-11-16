// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>The camera rules moved out of Unity's DungeonRunner in migration plan step 2i: <see cref="CameraGlide"/>,
/// <see cref="CameraFocus"/>, <see cref="ClassicCameraInput"/>, <see cref="ClassicCameraSetup"/>, the query follow and
/// turn, the front end's field of view, <see cref="TileTopPlane"/> and <see cref="GameSessionStarter"/>.</summary>
public class CameraRulesTests
{
    private const float RawUnit = 1f / Fp32.One;
    private static readonly CameraViewport Viewport = new(1600, 1600, 1200);

    private static CameraThing Profile() => new(new byte[84])
    {
        CameraId = 3, Z = 4096, Zoom = 40960, MinZoom = 16384, MaxZoom = 53248,
        Lens = 8192, AnglePitch = 220, Flags = 0x26,
    };

    // --- CameraFocus and CameraGlide (action 26, 0x00442650) ---

    [Fact]
    public void AGlideTargetsTheTileCentreAtTheFocusHeight()
    {
        Vector3 target = CameraFocus.TileCentre(new TilePos(4, 7), 1.25f, 30, 38);
        Assert.Equal(new Vector3(4.5f, 1.25f, -7.5f), target);
    }

    [Theory]
    [InlineData(-3, -2, 0f, 0f)]
    [InlineData(40, 50, 30f - RawUnit, -38f + RawUnit)]
    [InlineData(29, 37, 29.5f, -37.5f)]
    public void AGlideTargetIsKeptInsideTheMapOneRawUnitShortOfTheFarEdges(int x, int y, float expectedX, float expectedZ)
    {
        var glide = new CameraGlide();
        glide.Start(new Vector3(10, 1, -10), new TilePos(x, y), 30, 38, nowMilliseconds: 0);
        Assert.Equal(expectedX, glide.Target.X);
        Assert.Equal(1f, glide.Target.Y);
        Assert.Equal(expectedZ, glide.Target.Z);
    }

    [Fact]
    public void AnEmptyMapDoesNotThrow()
    {
        Vector3 focus = CameraFocus.TileCentre(new TilePos(3, 3), 1, 0, 0);
        Assert.Equal(-RawUnit, focus.X);
        Assert.Equal(RawUnit, focus.Z);
    }

    [Theory]
    [InlineData(0.5f, 1f, 0f, 1f)]
    [InlineData(-1f, 1f, 0f, 1f)]
    [InlineData(2f, 1f, 0f, 0f)]
    [InlineData(-1f, 0f, 1f, 0f)]
    [InlineData(2f, 0f, 1f, 1f)]
    [InlineData(0.5f, 0f, 1f, 0.5f)]
    public void TheClampTestsTheMinimumFirstAsUnitysMathfClampDoes(float value, float minimum, float maximum, float expected) =>
        Assert.Equal(expected, CameraFocus.Clamp(value, minimum, maximum));

    [Fact]
    public void AGlideLastsTheDirectorsTimeAndMovesInAStraightLine()
    {
        var glide = new CameraGlide();
        var from = new Vector3(1.5f, 1, -1.5f);
        glide.Start(from, new TilePos(3, 1), 30, 38, nowMilliseconds: 1000);
        double length = ScriptedViewDirector.GlideMilliseconds(2, 0);
        Assert.Equal(102.4, length, 9);
        Assert.True(glide.Active);
        Vector3 half = glide.Advance(from, 1000 + length / 2);
        Assert.Equal(2.5f, half.X, 5);
        Assert.Equal(-1.5f, half.Z);
        Assert.True(glide.Active);
        Vector3 end = glide.Advance(half, 1000 + length);
        Assert.Equal(glide.Target, end);
        Assert.False(glide.Active);
        Assert.Equal(new Vector3(9, 9, 9), glide.Advance(new Vector3(9, 9, 9), 5000));
    }

    [Fact]
    public void ALongGlideTakesAtMostHalfASecondAndStopEndsItWhereTheFocusIs()
    {
        var glide = new CameraGlide();
        glide.Start(new Vector3(0.5f, 1, -0.5f), new TilePos(20, 20), 30, 38, nowMilliseconds: 0);
        Vector3 midway = glide.Advance(default, 250);
        Assert.Equal(10.5f, midway.X, 4);
        glide.Stop();
        Assert.False(glide.Active);
        Assert.Equal(midway, glide.Advance(midway, 400));
    }

    [Fact]
    public void ACreatureFocusConvertsFixedPointToTilesWithZNegated()
    {
        Vector3 focus = CameraFocus.Over(new Fp32(3 * Fp32.One + 2048), new Fp32(5 * Fp32.One + 1024), 1);
        Assert.Equal(new Vector3(3.5f, 1, -5.25f), focus);
    }

    [Fact]
    public void TheFocusBoundsFixTheHeightAtRaw4095AndExcludeTheFarEdges()
    {
        CameraFocusBounds bounds = CameraFocus.Bounds(30, 38);
        Assert.Equal(new Vector3(0, 4095f / Fp32.One, -38 + RawUnit), bounds.Minimum);
        Assert.Equal(new Vector3(30 - RawUnit, 4095f / Fp32.One, 0), bounds.Maximum);
    }

    // --- ClassicCameraInput ---

    private static CameraPointerSample Pointer(float x, float y, bool focused = true, bool overHud = false, float wheel = 0) =>
        new(new Vector2(x, y), 1600, 1200, focused, overHud, wheel);

    [Theory]
    [InlineData(4, 600, ClassicCameraKeys.Left)]
    [InlineData(5, 600, ClassicCameraKeys.None)]
    [InlineData(1595, 600, ClassicCameraKeys.None)]
    [InlineData(1596, 600, ClassicCameraKeys.Right)]
    [InlineData(800, 1196, ClassicCameraKeys.Up)]
    [InlineData(800, 1195, ClassicCameraKeys.None)]
    [InlineData(800, 4, ClassicCameraKeys.Down)]
    [InlineData(800, 5, ClassicCameraKeys.None)]
    [InlineData(0, 0, ClassicCameraKeys.Left | ClassicCameraKeys.Down)]
    [InlineData(1599, 1199, ClassicCameraKeys.Right | ClassicCameraKeys.Up)]
    public void TheEdgeScrollBandIsFivePixelsStrict(float x, float y, ClassicCameraKeys expected) =>
        Assert.Equal(expected, ClassicCameraInput.EdgeScroll(Pointer(x, y)));

    [Theory]
    [InlineData(-1, 600)]
    [InlineData(1600, 600)]
    [InlineData(800, 1200)]
    [InlineData(800, -1)]
    public void APointerOffTheScreenDoesNotScroll(float x, float y) =>
        Assert.Equal(ClassicCameraKeys.None, ClassicCameraInput.EdgeScroll(Pointer(x, y)));

    [Fact]
    public void AnUnfocusedWindowDoesNotScroll() =>
        Assert.Equal(ClassicCameraKeys.None, ClassicCameraInput.EdgeScroll(Pointer(0, 600, focused: false)));

    [Fact]
    public void TheFrameTimeRoundsToTheNearestTimeSpanTick()
    {
        Assert.Equal(TimeSpan.FromTicks(166667), ClassicCameraInput.FrameTime(1d / 60));
        Assert.Equal(TimeSpan.FromTicks(4), ClassicCameraInput.FrameTime(0.00000037));
        Assert.Equal(TimeSpan.FromTicks((long)Math.Round(0.016f * (double)TimeSpan.TicksPerSecond)), ClassicCameraInput.FrameTime(0.016f));
    }

    [Fact]
    public void ThePointerDeltaIsRightAndDownScaledBySensitivity()
    {
        Assert.Equal(new Vector2(6, 9), ClassicCameraInput.PointerDelta(new Vector2(10, 20), new Vector2(14, 14), 15));
        Assert.Equal(new Vector2(2, 1), ClassicCameraInput.PointerDelta(default, new Vector2(4, -2), 1));
    }

    private static GameBindingInput Keys(params GameAction[] held)
    {
        var settings = new GameSettings();
        KeyModifiers modifiers = held.Length == 0 ? KeyModifiers.None : settings.Binding(held[0]).Modifiers;
        return new GameBindingInput(settings, modifiers,
            scan => held.Any(action => settings.Binding(action).ScanCode == scan), _ => false);
    }

    private static (ClassicCamera Camera, ClassicCameraControls Controls) Camera()
    {
        var camera = new ClassicCamera(Profile());
        return (camera, new ClassicCameraControls(camera, bounds: CameraFocus.Bounds(30, 38)));
    }

    [Fact]
    public void TheWheelZoomsOnlyAwayFromTheHud()
    {
        (ClassicCamera camera, ClassicCameraControls controls) = Camera();
        Vector3 focus = new(10, 4095f / Fp32.One, -10);
        int start = camera.Zoom.Raw;
        ClassicCameraInput.Apply(controls, focus, Keys(), Pointer(800, 600, overHud: true, wheel: 1), new Vector2(800, 600), 10, 0);
        Assert.Equal(start, camera.Zoom.Raw);
        ClassicCameraInput.Apply(controls, focus, Keys(), Pointer(800, 600, wheel: 1), new Vector2(800, 600), 10, 0);
        Assert.Equal(start * 242 >> 8, camera.Zoom.Raw);
        ClassicCameraInput.Apply(controls, focus, Keys(), Pointer(800, 600, wheel: -1), new Vector2(800, 600), 10, 0);
        Assert.Equal((start * 242 >> 8) * 270 >> 8, camera.Zoom.Raw);
    }

    [Fact]
    public void TheZoomKeysZoomOverTheHudToo()
    {
        (ClassicCamera camera, ClassicCameraControls controls) = Camera();
        int start = camera.Zoom.Raw;
        ClassicCameraInput.Apply(controls, new(10, 1, -10), Keys(GameAction.ZoomOut), Pointer(800, 600, overHud: true, wheel: 1),
            new Vector2(800, 600), 10, 0);
        Assert.Equal(start * 270 >> 8, camera.Zoom.Raw);
    }

    [Fact]
    public void AZoomInStepWinsOverAZoomOutStepInTheSameFrame()
    {
        (ClassicCamera camera, ClassicCameraControls controls) = Camera();
        int start = camera.Zoom.Raw;
        ClassicCameraInput.Apply(controls, new(10, 1, -10), Keys(GameAction.ZoomIn), Pointer(800, 600, wheel: -1),
            new Vector2(800, 600), 10, 0);
        Assert.Equal(start * 242 >> 8, camera.Zoom.Raw);
        (ClassicCamera other, ClassicCameraControls otherControls) = Camera();
        ClassicCameraInput.Apply(otherControls, new(10, 1, -10), Keys(GameAction.ZoomOut), Pointer(800, 600, wheel: 1),
            new Vector2(800, 600), 10, 0);
        Assert.Equal(start * 242 >> 8, other.Zoom.Raw);
    }

    [Fact]
    public void TheFramePansAtTheZoomItStartedWithThenZooms()
    {
        Vector3 focus = new(10, 4095f / Fp32.One, -10);
        (_, ClassicCameraControls reference) = Camera();
        Vector3 expected = reference.Advance(focus, ClassicCameraKeys.None, ClassicCameraInput.FrameTime(0.1), ClassicCameraKeys.Left);
        (ClassicCamera camera, ClassicCameraControls controls) = Camera();
        int start = camera.Zoom.Raw;
        Vector3 actual = ClassicCameraInput.Apply(controls, focus, Keys(), Pointer(0, 600, wheel: 1), new Vector2(0, 600), 10, 0.1);
        Assert.Equal(expected, actual);
        Assert.Equal(start * 242 >> 8, camera.Zoom.Raw);
    }

    [Fact]
    public void AnEdgePansTheFocusWithinTheBounds()
    {
        (_, ClassicCameraControls controls) = Camera();
        Vector3 focus = new(10, 4095f / Fp32.One, -10);
        Vector3 moved = ClassicCameraInput.Apply(controls, focus, Keys(), Pointer(0, 600), new Vector2(0, 600), 10, 0.1);
        Assert.True(moved.X < focus.X);
        Vector3 still = ClassicCameraInput.Apply(Camera().Controls, focus, Keys(), Pointer(800, 600), new Vector2(800, 600), 10, 0.1);
        Assert.Equal(focus, still);
    }

    [Fact]
    public void AHeldRotateTurnsByTheScaledPointerMoveFromTheSecondFrame()
    {
        (ClassicCamera camera, ClassicCameraControls controls) = Camera();
        GameBindingInput rotate = Keys(GameAction.MouseRotate);
        Assert.True(rotate.Held(GameAction.MouseRotate, exactModifiers: true));
        Vector3 focus = new(10, 1, -10);
        ClassicCameraInput.Apply(controls, focus, rotate, Pointer(802, 600), new Vector2(800, 600), 20, 0);
        Assert.Equal(0, camera.Yaw.Value);
        ClassicCameraInput.Apply(controls, focus, rotate, Pointer(804, 600), new Vector2(802, 600), 20, 0);
        Assert.Equal(2048 - 32, camera.Yaw.Value);
    }

    // --- ClassicCameraSetup ---

    [Fact]
    public void ThePresetsAreFoundByCameraId()
    {
        CameraThing dungeon = Profile(), second = Profile() with { Zoom = 20000 };
        CameraThing possession = Profile() with { CameraId = PossessionCamera.PresetId };
        CameraThing[] presets = [possession, dungeon, second];
        Assert.Same(dungeon, ClassicCameraSetup.Preset(presets, ClassicCameraMode.Dungeon));
        Assert.Null(ClassicCameraSetup.Preset(presets, ClassicCameraMode.TopDown));
        Assert.Same(possession, ClassicCameraSetup.PossessionPreset(presets));
        Assert.Null(ClassicCameraSetup.PossessionPreset([dungeon]));
    }

    [Theory]
    [InlineData(53248, 1.2f, 63898)]
    [InlineData(53248, 1f, 53248)]
    [InlineData(53248, 0.5f, 53248)]
    [InlineData(53248, 3f, 106496)]
    [InlineData(5, 1.5f, 8)]
    [InlineData(7, 1.5f, 10)]
    public void TheZoomOutLimitIsScaledWithinOneToTwoAndRoundedHalfToEven(int maxZoom, float scale, int expected) =>
        Assert.Equal(expected, ClassicCameraSetup.ZoomOutLimit(maxZoom, scale));

    [Fact]
    public void TheSetupStartsAtTheViewersCameraTileWithTheReferenceWidthAndStartYaw()
    {
        var players = new List<PlayerDef> { Player(1, 2, 2), Player(3, 12, 20) };
        ClassicCameraSetup setup = ClassicCameraSetup.Create(Profile(), 1.2f, 4, 10, players, new PlayerId(3), 30, 38);
        Assert.Equal(new Vector3(12.5f, 1, -20.5f), setup.StartFocus);
        Assert.Equal(1600, setup.Camera.ReferenceScreenWidth);
        Assert.Equal(ClassicCamera.LevelStartYaw, setup.Camera.Yaw);
        for (int i = 0; i < 20; i++) setup.Camera.ZoomOut();
        Assert.Equal(ClassicCameraSetup.ZoomOutLimit(53248, 1.2f) - 1, setup.Camera.Zoom.Raw);
        Vector3 clamped = setup.Controls.Advance(new Vector3(29.9f, 1, -10), ClassicCameraKeys.Right, TimeSpan.FromSeconds(1));
        Assert.Equal(30 - RawUnit, clamped.X);
        Assert.Equal(4095f / Fp32.One, clamped.Y);
    }

    [Fact]
    public void ALevelWithoutPlayerRecordsStartsAtTheMapCentre() =>
        Assert.Equal(new Vector3(15, 1, -19),
            ClassicCameraSetup.Create(Profile(), 1, 4, 10, null, new PlayerId(3), 30, 38).StartFocus);

    private static PlayerDef Player(byte id, ushort cameraX, ushort cameraY) => new()
    {
        PlayerId = id, Name = "test", StartingGold = 0, HasAiFlag = false, TriggerId = 0, CameraX = cameraX, CameraY = cameraY,
        Ai = null!,
    };

    // --- The query follow and turn ---

    [Theory]
    [InlineData(true, 7, false, false, true)]
    [InlineData(false, 7, false, false, false)]
    [InlineData(true, 0, false, false, false)]
    [InlineData(true, 7, true, false, false)]
    [InlineData(true, 7, false, true, false)]
    public void TheCameraFollowsTheQueriedCreatureOnlyWithTheToolATargetNoPathAndNoModal(bool queryTool, int target,
        bool pathPlaying, bool modal, bool expected) =>
        Assert.Equal(expected, ClassicCreatureQueryRules.Follows(queryTool, new CreatureQueryTarget(target), pathPlaying, modal));

    [Fact]
    public void TheQueryTurnMatchesTheRunnersRoundTripThroughDegreesForEveryYaw()
    {
        for (int value = 0; value < 2048; value++)
        {
            var camera = new ClassicCamera(Profile());
            camera.Face(new Dk2Angle((ushort)value));
            var reference = new ClassicCamera(Profile());
            reference.Face(new Dk2Angle((ushort)value));
            // DungeonRunner's turn before step 2i: the pose's yaw in degrees, rounded back to angle units.
            float degrees = reference.Pose(Vector3.Zero, Viewport).YawDegrees * 2048 / 360f;
            var yaw = new Dk2Angle(unchecked((ushort)(int)Math.Round((double)degrees)));
            reference.Rotate(new Dk2Angle(unchecked((ushort)(ClassicCreatureQueryRules.FollowHeading(yaw).Value - yaw.Value))));
            ClassicCreatureQueryRules.TurnToFollowHeading(camera);
            Assert.Equal(reference.Yaw, camera.Yaw);
            Assert.Equal(value % 64, camera.Yaw.Value);
        }
    }

    [Fact]
    public void ARotationLockedPresetKeepsItsYawOnAQuery()
    {
        var camera = new ClassicCamera(Profile() with { Flags = 0x27 });
        camera.Face(new Dk2Angle(300));
        ClassicCreatureQueryRules.TurnToFollowHeading(camera);
        Assert.Equal(300, camera.Yaw.Value);
    }

    // --- The front end's field of view ---

    [Theory]
    [InlineData(1920, 1080, 1440)]
    [InlineData(1024, 768, 1024)]
    [InlineData(1000, 1000, 1000)]
    public void TheFrontEndKeepsTheFourByThreeVerticalExtentOnAWideScreen(int width, int height, int framedWidth)
    {
        var pose = new CameraPathPose(default, default, default, 1.2f, 50);
        Assert.Equal(pose.VerticalFieldOfView(framedWidth, height), pose.FrontEndVerticalFieldOfView(width, height));
    }

    // --- TileTopPlane ---

    [Fact]
    public void ARayHitsTheTopPlaneAtItsHeight()
    {
        Assert.True(TileTopPlane.TryHit(new Vector3(0, 4, 0), new Vector3(0.6f, -0.8f, 0), TileHeights.Solid, out Vector3 point));
        Assert.Equal(1.5f, point.X, 5);
        Assert.Equal(TileHeights.Solid, point.Y, 5);
        Assert.Equal(0f, point.Z);
    }

    [Fact]
    public void ARayParallelToOrAwayFromThePlaneMissesIt()
    {
        Assert.False(TileTopPlane.TryHit(new Vector3(0, 4, 0), new Vector3(1, 0, 0), 2, out _));
        Assert.False(TileTopPlane.TryHit(new Vector3(0, 4, 0), new Vector3(0, 1, 0), 2, out _));
        Assert.False(TileTopPlane.TryHit(new Vector3(0, 1, 0), new Vector3(0, -1, 0), 2, out _));
    }

    [Fact]
    public void ThePointerPositionIsOnTheTileOrNothing()
    {
        Vector3 down = new(0, -1, 0);
        FpPosition2? position = TileTopPlane.PositionOn(new Vector3(2.25f, 10, -3.5f), down, new TilePos(2, 3), solid: false);
        Assert.Equal(new FpPosition2(new Fp32(9216), new Fp32(14336)), position);
        Assert.Null(TileTopPlane.PositionOn(new Vector3(2.25f, 10, -3.5f), down, new TilePos(2, 2), solid: false));
        Assert.Null(TileTopPlane.PositionOn(new Vector3(2.25f, 1, -3.5f), down, new TilePos(2, 3), solid: true));
    }

    [Fact]
    public void TheSolidAndOpenPlanesHaveTheirOwnHeights()
    {
        Assert.Equal(TileHeights.Solid, TileTopPlane.Height(solid: true));
        Assert.Equal(TileHeights.Open, TileTopPlane.Height(solid: false));
        Vector3 ray = Vector3.Normalize(new Vector3(1, -1, 0));
        Vector2 onSolid = TileTopPlane.HandLightPoint(new Vector3(0, 4, -0.5f), ray, new TilePos(2, 0), solid: true);
        Vector2 onOpen = TileTopPlane.HandLightPoint(new Vector3(0, 4, -0.5f), ray, new TilePos(2, 0), solid: false);
        Assert.Equal(4 - TileHeights.Solid, onSolid.X, 4);
        Assert.Equal(4 - TileHeights.Open, onOpen.X, 4);
        Assert.Equal(0.5f, onSolid.Y);
    }

    [Fact]
    public void TheHandLightFallsBackToTheTileCentre() =>
        Assert.Equal(new Vector2(3.5f, 6.5f),
            TileTopPlane.HandLightPoint(new Vector3(0, 4, 0), new Vector3(1, 0, 0), new TilePos(3, 6), solid: false));

    // --- GameSessionStarter ---

    private static LevelMap Map(params byte[] owners) =>
        new(owners.Length, 1, owners.Select(owner => new MapTile(2, owner, 0, 0)).ToArray());

    [Fact]
    public void ASavesPlayersAreTheMapsOwnersAscendingAndDistinct() =>
        Assert.Equal(new byte[] { 0, 1, 3 }, GameSessionStarter.PlayersOf(Map(3, 1, 3, 0, 1)));

    [Fact]
    public void TheCameraScrollsAtTheLevelsTicksPerSecondElseTheOriginalGames()
    {
        Assert.Equal(SimulationTiming.OriginalTicksPerGameSecond, GameSessionStarter.CameraTicksPerSecond(null));
        CampaignLevelData level = Level();
        Assert.Equal(level.Level.TicksPerSecond, GameSessionStarter.CameraTicksPerSecond(level));
    }

    [Fact]
    public void AResumedSimulationWinsOverTheLevel()
    {
        CampaignLevelData level = Level();
        GameSimulation resumed = level.CreateSimulation(1, fogOfWarEnabled: false);
        Assert.Same(resumed, GameSessionStarter.Campaign(level, resumed, frontEnd: true, seed: 1, fogOfWarEnabled: true));
    }

    [Fact]
    public void TheFrontEndWorldHasNoFogAndTheLevelKeepsItsOwn()
    {
        CampaignLevelData level = Level();
        Assert.False(GameSessionStarter.Campaign(level, null, frontEnd: true, seed: 1, fogOfWarEnabled: true).World.FogOfWarEnabled);
        Assert.True(GameSessionStarter.Campaign(level, null, frontEnd: false, seed: 1, fogOfWarEnabled: true).World.FogOfWarEnabled);
    }

    [Fact]
    public void ADk2SavesWorldHasTheMapsOwnersAsPlayers()
    {
        CampaignLevelData level = Level();
        // Good and Neutral only: a keeper's Heart needs the database's Heart health, which this one does not carry.
        GameSimulation simulation = GameSessionStarter.FromDk2Save(level.Database, Map(2, 1, 2, 1), 1, fogOfWarEnabled: true);
        Assert.True(simulation.World.FogOfWarEnabled);
        Assert.Equal(new[] { new PlayerId(1), new PlayerId(2) }, simulation.World.PlayerIds.OrderBy(id => id.Value));
    }

    private static CampaignLevelData Level()
    {
        TerrainDef terrain = new()
        {
            Name = "test", TerrainId = 2, Flags = TerrainFlags.Solid, StartingHealth = 1, MaxHealth = 1, GoldValue = 0,
            ManaGain = 0, MaxManaGain = 0, Damage = 0, MaxHealthTypeTerrainId = 2, DestroyedTypeTerrainId = 2,
            Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        };
        var database = new GameDatabase([terrain], [], [], new GlobalVariables());
        var map = new LevelMap(4, 4, Enumerable.Repeat(new MapTile(2, PlayerIds.Unowned, 0, 0), 16).ToArray());
        var level = new LevelDef([])
        {
            Name = "Level5", Description = "", Author = "", Email = "", Information = "", Unknown_1184 = default, Messages = [],
            SoundCategory = "", PreviousRewards = default, NextRewards = default, IntroductionOverrideObjects = default,
            IntroductionOverrideTextIds = [], TerrainPath = "", Unknown_6403 = default, Paths = [], UnknownPathTail = [],
            TicksPerSecond = 7,
        };
        return new CampaignLevelData("Level5", database, map, level, [], [], []);
    }
}
