// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The front end's 3D station behind the menu, as Unity's runner runs its front-end mode
/// (<c>DungeonRunner.FrontEnd</c>'s <c>UpdateFrontEnd</c>): the front-end level's world and scenery
/// (<see cref="FrontEndStation"/>) drawn by a <see cref="WorldScene"/> from the session's camera paths, the campaign map's
/// lands picked under the pointer (<see cref="CampaignMapController.UpdatePointer"/>), the hi-score name typed, and a
/// launched land started in <c>InGameHud.tscn</c> (<see cref="InGameHud.QueueSession"/>).</summary>
public partial class ClassicFrontEndMenu
{
    private const string InGameHudScene = "res://InGameHud.tscn";

    private FrontEndStation? _station;
    private WorldScene? _world;
    /// <summary>The campaign map's launch (dispatcher 0xC), taken once the menu has finished with the frame.</summary>
    private GameSessionRequest? _pendingLaunch;
    private bool _launching;
    private readonly ClassicKeyboard _keyboard = new();

    /// <summary>The station's 3D world, or null when the front-end level could not be read.</summary>
    public WorldScene? Station => _world;

    private Action? _openStation;

    /// <summary>Keeps the station's start for <see cref="_Ready"/>: its world needs the node in the tree.</summary>
    private void PrepareStation(string install, CampaignProfile profile, bool secretFiveAvailable, ulong seed, float? brightness,
        string[] args) =>
        _openStation = () => OpenStation(install, profile, secretFiveAvailable, seed, brightness, args);

    public override void _Ready()
    {
        Action? open = _openStation;
        _openStation = null;
        open?.Invoke();
    }

    /// <summary>Unity's runner's front-end start: the front-end level's world, its scenery from the loaded profile, the map's
    /// pick table, and the launch subscribed, which also enables the briefing's Launch button
    /// (<see cref="CampaignMapController.CanLaunch"/>).</summary>
    private void OpenStation(string install, CampaignProfile profile, bool secretFiveAvailable, ulong seed, float? brightness,
        string[] args)
    {
        CampaignLevelData level;
        try { level = CampaignLevelData.Load(install, GameSessionRequest.FrontEnd().LevelName); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException)
        {
            GD.PushError("DK2.Godot: the front-end level could not be read (", ex.Message, "); the menu shows no station.");
            return;
        }
        PlayerId viewer = PlayerIds.FirstKeeper;
        _station = FrontEndStation.Open(level, profile, secretFiveAvailable, viewer, seed);
        _world = new WorldScene { Name = "Station" };
        AddChild(_world);
        MoveChild(_world, 0);
        _world.OpenEffects(_station.Simulation, _station.View, viewer, seed);
        _world.OpenFrontEnd(install, level, _station, viewer);
        _world.StartEffects();
        _world.SetBrightness(brightness);
        if (_campaign != null) _campaign.LaunchRequested += QueueLaunch;
        // --perf-report: the performance harness over the station (PerformanceReport.cs); without it, no node.
        WorldState world = _station.Simulation.World;
        PerformanceReport.Attach(this, args, level.Name, seed, _world, world.Width, world.Height);
    }

    private void QueueLaunch(CampaignSlot slot)
    {
        if (slot.LevelName is string level) _pendingLaunch = GameSessionRequest.Campaign(level);
    }

    /// <summary>Unity's <c>UpdateFrontEnd</c> after the menu's refresh: a queued launch replaces the scene; otherwise the
    /// camera from the session's path, the map pick under <paramref name="pointer"/> while the map shows, the map's sounds,
    /// the scenery the map's revision shows, and the frame drawn on the scenery's creature clock. Returns true when the
    /// scene is being replaced.</summary>
    private bool UpdateStation(Vector2 pointer, bool primaryHeld, bool pointerInside, long milliseconds)
    {
        if (_launching) return true;
        if (_pendingLaunch is GameSessionRequest request)
        {
            // A click replaces this scene; switch only after the menu has finished with it.
            _launching = true;
            InGameHud.QueueSession(request);
            GD.Print("DK2.Godot front end: launching ", request.LevelName, " at process frame ", Engine.GetProcessFrames(), ".");
            Callable.From(ChangeToLevel).CallDeferred();
            return true;
        }
        if (_campaign == null || _session == null) return false;
        if (_world == null || _station == null)
        {
            // Without the station there is nothing to pick, as before it existed.
            _campaign.UpdatePointer(CampaignSlot.None, primaryHeld);
            return false;
        }
        if (_session.Pose is CameraPathPose pose) _world.FrontEndPose = pose;
        _world.PlaceCamera();
        CampaignSlot picked = _campaign.IsAtMap && pointerInside
            && _world.TryPickRay(pointer, out System.Numerics.Vector3 origin, out System.Numerics.Vector3 direction)
            ? _world.PickFrontEndMap(origin, direction, _station.PickSlots) : CampaignSlot.None;
        _campaign.UpdatePointer(picked, primaryHeld);
        (GameTick tick, float fraction) = FrontEndHost.CreatureFrame(_elapsed);
        _world.SetFrameTime(tick, fraction, milliseconds);
        // As Unity's RefreshCampaignMap: the world never ticks, so it is presented again only when the map's projection
        // changed (the first frame included), which keeps a still frame from walking the scenery.
        if (_station.Refresh(_campaign)) _world.Present(_station.Simulation.World, _elapsed);
        _world.DrawFrame(tick, fraction);
        _world.FinishFrame();
        _world.CommitHoverOverlay();
        _world.DrawBlendedItems();
        return false;
    }

    private void ChangeToLevel()
    {
        Error error = GetTree().ChangeSceneToFile(InGameHudScene);
        if (error == Error.Ok) return;
        _launching = false;
        _pendingLaunch = null;
        InGameHud.QueueSession(null);
        GD.PushWarning("DK2.Godot: the level could not be opened (", error.ToString(), ").");
    }

    /// <summary>Unity's <c>FrontEndHost.TypeName</c> call: while a hi-score row takes a name, the frame's typed text.</summary>
    private void TypeHiScoreName()
    {
        if (_campaign != null && _campaign.EntryRow >= 0) FrontEndHost.TypeName(_campaign, _keyboard.Typed);
        _keyboard.EndFrame();
    }

    private void CloseStation()
    {
        if (_campaign != null) _campaign.LaunchRequested -= QueueLaunch;
        _station = null;
        _world = null;
    }
}
