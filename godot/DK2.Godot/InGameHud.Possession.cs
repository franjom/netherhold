// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Presentation;
using DK2.Simulation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Feeds keys, pointer travel and the scene clock to <see cref="PossessionSession"/>. Applies its camera, pointer lock, commands and sounds (ADR-0805).</summary>
public partial class InGameHud
{
    private readonly List<SoundRequest> _possessionSounds = new();
    private PossessionSession? _possession;
    /// <summary>The OS pointer's travel since the last frame, in screen pixels, y down.</summary>
    private Vector2 _possessionMotion;
    /// <summary>The scripted pointer last frame, whose travel stands in for the OS pointer's.</summary>
    private Vector2 _possessionScripted = new(float.NaN, float.NaN);
    private bool _pointerCaptured;

    private bool Possessing
    {
        get
        {
            return _possession is { Possessing: true };
        }
    }

    private bool PossessionCameraHeld
    {
        get
        {
            return _possession is { CameraHeld: true };
        }
    }

    /// <summary>The pointer is the view's: the Hand is not drawn and the hover is off.</summary>
    private bool PossessionPointerHeld
    {
        get
        {
            return _possession is { PointerLockWanted: true };
        }
    }

    /// <summary>Begin the possession session on camera preset 2. The check argument
    /// <c>--possess-first-creature</c> (<see cref="PossessionStaging"/>), once the level's first turn has run.</summary>
    private void OpenPossession(string install, CampaignLevelData level, string[] args)
    {
        if (_world == null || _simulation == null)
        {
            return;
        }

        _possession = new PossessionSession(level.Database,
            ClassicCameraSetup.PossessionPreset(GameDatabaseLoader.LoadCameraPresets(install, level.Name)));
        _world.Possession = _possession;
        _possession.Colours.ApplyColourChanges = LaunchArguments.PossessionColourChanges(args, "--");
        if (_possession.Colours.ApplyColourChanges)
        {
            GD.Print("DK2.Godot: developer possession colours enabled.");
        }

        OpenScreenColours();
        if (!LaunchArguments.PossessFirstCreature(args, "--"))
        {
            return;
        }

        WorldScene world = _world;
        GameSimulation simulation = _simulation;
        if (PossessionStaging.PossessFirst(simulation.World, _viewer, world.Focus, (creature, focus) =>
            {
                world.Focus = focus;
                world.PlaceCamera();
                if (PossessionEntryEffect.ForCreature(level.Database, creature, _viewer) is { } effect)
                {
                    world.Emit(new SimEvent(simulation.CurrentTick, "possession-staging", "effect_requested", "")
                        { EffectRequest = effect });
                }
            }) is CreatureState possessed)
        {
            GD.Print("DK2.Godot: --possess-first-creature possessed creature ", possessed.Id, " at tile (", possessed.Tile.X, ", ",
                possessed.Tile.Y, ")");
        }
        else
        {
            GD.PushWarning("DK2.Godot: --possess-first-creature found no creature of the viewer's to possess.");
        }
    }

    /// <summary>The OS pointer's travel, summed over the frame's events; a scripted pointer's is its own.</summary>
    private void ReadPossessionMotion(InputEventMouseMotion motion)
    {
        if (_pointerScript == null)
        {
            _possessionMotion += motion.ScreenRelative;
        }
    }

    /// <summary>Update possession before the camera step while ordinary input is allowed and no menu is modal,
    /// no camera path plays and the window has focus. The clock is the scene's engine time, so a fixed-fps capture
    /// repeats. The presses are this frame's, which <see cref="QueueInput"/> takes after.</summary>
    private void UpdatePossession(HudVisibility visibility, double delta, long milliseconds)
    {
        Vector2 moved = _possessionMotion;
        _possessionMotion = default;
        if (_pointerScript != null)
        {
            moved = float.IsNaN(_possessionScripted.X) ? Vector2.Zero : _pointer - _possessionScripted;
            _possessionScripted = _pointer;
        }
        if (_possession == null || _simulation == null || _world == null || _scriptedView == null)
        {
            return;
        }

        bool allowed = visibility.AllowOrdinaryInput && !_hudControls.Modal && !_scriptedView.PathPlaying;
        var input = new PossessionInput(allowed && Focused, milliseconds, delta * 1000d,
            new System.Numerics.Vector2(moved.X, moved.Y), _primaryDown, _secondaryUp,
            _keyboard.Read(_settings), _settings.MouseSensitivity, _settings.InvertMouse);
        PossessionUpdate update = _possession.Update(_simulation.World, _viewer, input, _world.ClassicCamera, _world.Focus,
            _pending, _possessionSounds);
        UpdateScreenColours();
        DrawPossessionForeground(milliseconds);
        // Play the view's sounds as the session produces them.
        for (int i = 0; i < _possessionSounds.Count; i++)
        {
            _mixer?.Play(_possessionSounds[i]);
        }

        _possessionSounds.Clear();
        if (update.Entered)
        {
            _input.CancelPointerGesture();
        }

        if (update.OrdinaryFocus is System.Numerics.Vector3 focus)
        {
            _world.Focus = focus;
        }

        CapturePointer(_possession.PointerLockWanted);
    }

    /// <summary>The pointer lock: hidden and held by the window, its travel read as relative motion. A scripted pointer
    /// never takes it, as that would hide and hold the owner's cursor during a capture; nothing here moves the pointer.</summary>
    private void CapturePointer(bool wanted)
    {
        bool capture = wanted && _pointerScript == null;
        if (_pointerCaptured == capture)
        {
            return;
        }

        _pointerCaptured = capture;
        Input.MouseMode = capture ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }

    /// <summary>Release possession's hold on the level and on the pointer.</summary>
    private void ClosePossession()
    {
        _possession?.Reset();
        CloseScreenColours();
        CapturePointer(false);
    }
}
