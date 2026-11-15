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

namespace DK2.Presentation;

/// <summary>One frame of host input for the possessed view. <paramref name="Allowed"/> is false while a
/// panel, a camera path or a window without focus owns the input. <paramref name="PointerDelta"/> is
/// the pointer's travel this frame in host pixels, Y down the screen, before Mouse Sensitivity.</summary>
public readonly record struct PossessionInput(
    bool Allowed, long ClockMilliseconds, double FrameMilliseconds, Vector2 PointerDelta,
    bool PrimaryPressed, bool SecondaryReleased, GameBindingInput Keys, int MouseSensitivity, bool InvertMouse);

/// <summary>What a frame changed for the host. <paramref name="Entered"/>: a possession began, so a
/// pointer gesture in progress is dropped. <paramref name="Ended"/>: one ended. <paramref name="OrdinaryFocus"/>:
/// where the ordinary camera's focus now is, when the way back to it began.</summary>
public readonly record struct PossessionUpdate(bool Entered, bool Ended, Vector3? OrdinaryFocus);

/// <summary>The possessing client outside the simulation (possession-client.md): follows the viewer's
/// possession in the presented world, runs the first-person step, the keys and the timed camera
/// moves, and says what the host applies: the camera's pose, the creature whose model is left out,
/// whether the pointer is held, the commands to queue and the sounds to play. A host only reads its
/// input into <see cref="PossessionInput"/> and applies the result (ADR-0805).</summary>
public sealed class PossessionSession
{
    private static readonly GameAction[] PressActions =
    [
        GameAction.Melee, GameAction.Spell1, GameAction.Spell2, GameAction.Spell3, GameAction.SelectAlly,
        GameAction.RemoveAlly, GameAction.Sniper, GameAction.Fire,
    ];

    private readonly GameDatabase _database;
    private readonly CameraThing _preset;
    private readonly PossessionView _view = new();
    private readonly PossessionControls _controls = new();
    private readonly PossessionFlight _flight = new();
    private PossessionWorld? _world;
    private CreatureState? _possessed;
    private WorldState? _boundWorld;
    private long _boundTick;
    private double _milliseconds;
    private Vector2 _mouse;
    private bool? _creepingSent;
    private bool _flyingOut;
    private uint _held;

    /// <param name="preset">Camera preset 2 of <c>GlobalVariables.kwd</c>; without one its shipped lens
    /// and view distance stand in.</param>
    public PossessionSession(GameDatabase database, CameraThing? preset)
    {
        _database = database;
        _preset = preset is { Lens: > 0 } ? preset : new CameraThing(new byte[84]) { Lens = 4096, ViewDistance = 49152 };
    }

    public bool Possessing => _possessed != null;

    /// <summary>The camera is the possession's: in first person, or on the timed move to or from it.</summary>
    public bool CameraHeld => Possessing || _flyingOut;

    /// <summary>The id of the creature whose own model is left out of the picture, or 0. DK2 clears
    /// the thing's draw bit (<c>+0xF3</c> bit 0) when the move into first person lands
    /// (<c>Camera_CommitMode</c>, <c>0x0044BD70</c>) and sets it again as the move out is asked for
    /// (<c>Camera_RequestMode</c>, <c>0x0044A5D0</c>).</summary>
    public int HiddenCreatureId => Possessing && !_flight.Active ? _possessed!.Id : 0;

    /// <summary>The pointer is hidden and held at the window's middle: a remake choice for mouse look.</summary>
    public bool PointerLockWanted { get; private set; }

    public PossessionView View => _view;
    public PossessionControls Controls => _controls;
    public PossessionFlight Flight => _flight;

    /// <summary>One frame. <paramref name="ordinary"/> is the ordinary camera the timed moves start
    /// from and return to, null when the host has none (then the view cuts); <paramref name="ordinaryFocus"/>
    /// is its focus in host axes. The navigation bytes are taken again whenever the presented world
    /// has moved on a turn.</summary>
    public PossessionUpdate Update(WorldState world, PlayerId viewer, in PossessionInput input,
        ClassicCamera? ordinary, Vector3 ordinaryFocus, ICollection<PlayerCommand> commands, ICollection<SoundRequest> sounds)
    {
        int id = world.Player(viewer).PossessedCreatureId;
        CreatureState? creature = id != 0 ? world.CreatureById(id) : null;
        if (creature == null)
        {
            bool ended = Possessing;
            Vector3? focus = null;
            if (ended)
            {
                CameraFlightState from = _flight.Active ? _flight.State : PossessionFlight.FirstPerson(_view, _preset);
                Reset();
                if (ordinary != null) focus = BeginFlyOut(from, ordinary, ordinaryFocus, world, input.ClockMilliseconds);
            }
            if (_flyingOut)
            {
                _flight.Step(input.ClockMilliseconds);
                _flyingOut = _flight.Active;
            }
            return new PossessionUpdate(false, ended, focus);
        }

        bool entered = _possessed == null || _possessed.Id != id;
        if (entered || !ReferenceEquals(world, _boundWorld) || world.CurrentTick.Value != _boundTick)
        {
            _world ??= new PossessionWorld(_database);
            _world.Bind(world, creature);
            _boundWorld = world;
            _boundTick = world.CurrentTick.Value;
        }
        _possessed = creature;
        if (entered)
        {
            _view.Enter(creature.PosX, creature.PosY, creature.PosZ + FloorHeightRules.Ordinary, _world!.Body());
            _controls.Reset();
            _creepingSent = null;
            _milliseconds = 0;
            _mouse = default;
            // DK2 flies the camera to the eye in a timed move (Camera_SetMode 0x0044ACE0). Remake choice: a move out still running is turned round.
            if (ordinary != null)
                _flight.Start(_flyingOut ? _flight.State : ordinary.State(ordinaryFocus),
                    PossessionFlight.FirstPerson(_view, _preset), input.ClockMilliseconds);
            else _flight.Cancel();
            _flyingOut = false;
        }

        // The first-person step, the look and the once-a-turn pose are mode 2's: none runs until the move lands.
        bool flying = _flight.Active;
        if (flying) _flight.Step(input.ClockMilliseconds);
        bool lockedBefore = PointerLockWanted;
        PointerLockWanted = input.Allowed;
        if (flying)
        {
            _milliseconds = 0;
            _mouse = default;
            _held = uint.MaxValue;
        }
        else if (input.Allowed) Step(creature, viewer, input, lookAllowed: lockedBefore, commands, sounds);
        return new PossessionUpdate(entered, false, null);
    }

    private void Step(CreatureState creature, PlayerId viewer, in PossessionInput input, bool lookAllowed,
        ICollection<PlayerCommand> commands, ICollection<SoundRequest> sounds)
    {
        // The right button leaves: the owner's recollection (2026-11-14); the binary's sender of the leave action is unread.
        if (input.SecondaryReleased) commands.Add(new LeavePossessionCommand(viewer));
        if (_world!.Frozen) return;

        // Remake choice: only the frames the step runs count as its time, and the remainder of a millisecond is carried.
        _milliseconds += input.FrameMilliseconds;
        int milliseconds = (int)_milliseconds;
        _milliseconds -= milliseconds;

        // Remake choice: one pixel of host pointer travel is one unit of DK2's mouse delta, whose units are unread.
        // The frame the pointer is first held on is skipped: its delta is the jump to the middle.
        if (lookAllowed) _mouse += GameBindingInput.HostPointerDelta(input.PointerDelta, input.MouseSensitivity);
        int mouseX = (int)MathF.Round(_mouse.X), mouseY = (int)MathF.Round(_mouse.Y);
        _mouse -= new Vector2(mouseX, mouseY);

        GameBindingInput keys = input.Keys;
        PossessionFrame frame = _view.Step(milliseconds,
            new PossessionKeys(keys.Held(GameAction.ScrollUp), keys.Held(GameAction.ScrollDown), keys.Held(GameAction.ScrollLeft),
                keys.Held(GameAction.ScrollRight), keys.Held(GameAction.Run), keys.Held(GameAction.Creep)),
            mouseX, mouseY, input.InvertMouse, _world.Body(), _world);

        // The body's creeping bit is the presented world's and lags the command by a turn: one command a change.
        if (frame.SetCreeping is bool creep)
        {
            if (_creepingSent != creep) commands.Add(new SetCreepingCommand(viewer, creep));
            _creepingSent = creep;
        }
        else _creepingSent = null;

        CreatureDef definition = _database.CreatureById(creature.CreatureTypeId);
        Footstep(frame.LeftFoot, definition, sounds);
        Footstep(frame.RightFoot, definition, sounds);

        // Remake choice: a press is the key going down whatever else is held, so Fire works while Run or Creep is.
        uint held = 0;
        foreach (GameAction action in PressActions)
            if (keys.Held(action)) held |= 1u << (int)action;
        uint pressed = held & ~_held;
        _held = held;
        bool Pressed(GameAction action) => (pressed & (1u << (int)action)) != 0;

        if (Pressed(GameAction.Melee)) _controls.SelectMelee();
        if (Pressed(GameAction.Spell1)) _controls.SelectSpell(0, definition, creature.Level);
        if (Pressed(GameAction.Spell2)) _controls.SelectSpell(1, definition, creature.Level);
        if (Pressed(GameAction.Spell3)) _controls.SelectSpell(2, definition, creature.Level);
        if (Pressed(GameAction.SelectAlly)) _controls.SelectFollowers();
        if (Pressed(GameAction.RemoveAlly)) commands.Add(new DismissFollowersCommand(viewer));
        if (Pressed(GameAction.Sniper)) commands.Add(new SetSniperModeCommand(viewer, !creature.SniperMode));
        if (Pressed(GameAction.Fire) || input.PrimaryPressed) commands.Add(_controls.Fire(viewer, _view));
    }

    private static void Footstep(PossessionFootstep? step, CreatureDef definition, ICollection<SoundRequest> sounds)
    {
        if (step.HasValue && PossessionFootsteps.For(definition.SoundCategory, step.Value) is SoundRequest request)
            sounds.Add(request);
    }

    /// <summary>Starts the move back to the ordinary camera (Camera_SetMode's ordinary arm): the
    /// ordinary camera is over the eye's point and faces the way the view did from the start, and the
    /// picture flies there. The focus is kept inside the map as the ordinary camera's always is.</summary>
    private Vector3 BeginFlyOut(in CameraFlightState from, ClassicCamera ordinary, Vector3 ordinaryFocus, WorldState world, long clock)
    {
        CameraFlightState to = ordinary.Return(from, (int)MathF.Round(ordinaryFocus.Y * Fp32.One));
        to = to with
        {
            X = Math.Clamp(to.X, 0, world.Width * Fp32.One - 1),
            Y = Math.Clamp(to.Y, 0, world.Height * Fp32.One - 1),
        };
        ordinary.Face(new Dk2Angle((ushort)to.Yaw));
        _flight.Start(from, to, clock);
        _flyingOut = _flight.Active;
        return new Vector3(to.X / (float)Fp32.One, ordinaryFocus.Y, -to.Y / (float)Fp32.One);
    }

    /// <summary>The once-a-turn pose (<c>0x004039A0</c>, sent in camera mode 2 only), or null: the
    /// host puts it first among the commands of the input frame about to be handed to a turn.</summary>
    public PlayerCommand? TurnPose(PlayerId viewer) =>
        Possessing && !_flight.Active ? PossessionControls.Pose(viewer, _view, _world!.Body()) : null;

    /// <summary>The picture, when the camera is the possession's: the timed move's from the ordinary
    /// camera's own pose formula, else the first-person one.</summary>
    public bool TryCamera(ClassicCamera? ordinary, CameraViewport viewport, out ClassicCameraPose pose)
    {
        if (_flight.Active && ordinary != null)
        {
            float progress = _flight.Progress;
            pose = ordinary.Pose(_flight.State, viewport, _flyingOut ? 1f - progress : progress);
            return true;
        }
        if (Possessing)
        {
            pose = PossessionCamera.Picture(_view, _preset);
            return true;
        }
        pose = default;
        return false;
    }

    /// <summary>Drops the possession without a move back: the level is being let go.</summary>
    public void Reset()
    {
        _possessed = null;
        _world = null;
        _boundWorld = null;
        _flight.Cancel();
        _flyingOut = false;
        PointerLockWanted = false;
    }
}
