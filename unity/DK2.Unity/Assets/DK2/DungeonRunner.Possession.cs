// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>The host side of the first-person view (docs/research/possession-client.md): reads the
    /// keys and the mouse, steps <see cref="PossessionView"/>, queues what it sends and places the
    /// camera. It reads only the presented world; the rules are DK2.Presentation's.</summary>
    public sealed partial class DungeonRunner
    {
        private readonly PossessionView _possessionView = new PossessionView();
        private readonly PossessionControls _possessionControls = new PossessionControls();
        private PossessionWorld _possessionWorld;
        private CameraThing _possessionPreset;
        private CreatureState _possessed;
        private WorldState _possessionBoundWorld;
        private long _possessionBoundTick;
        private double _possessionMilliseconds;
        private Vector2 _possessionMouse;
        private bool? _creepingSent;
        private bool _possessionPointerLocked;

        private readonly PossessionFlight _possessionFlight = new PossessionFlight();
        private bool _possessionFlyingOut;

        private bool Possessing => _possessed != null;

        /// <summary>The camera is the possession's: in first person, or on the timed move to or from it.</summary>
        private bool PossessionCameraHeld => Possessing || _possessionFlyingOut;

        /// <summary>The id of the creature whose own model is left out of the picture, or 0. DK2 clears
        /// the thing's draw bit (<c>+0xF3</c> bit 0) when the move into first person lands
        /// (<c>Camera_CommitMode</c>, <c>0x0044BD70</c>) and sets it again as the move out is asked for
        /// (<c>Camera_RequestMode</c>, <c>0x0044A5D0</c>).</summary>
        private int PossessionHiddenCreatureId => Possessing && !_possessionFlight.Active ? _possessed.Id : 0;

        private bool PossessionCanFly =>
            _classicCamera != null && !frameWholeMap && _possessionPreset != null && _possessionPreset.Lens > 0;

        private static long PossessionClock => (long)(Time.unscaledTimeAsDouble * 1000d);

        /// <summary>Follows the viewer's possession in the presented world and, while it lasts and
        /// <paramref name="inputAllowed"/>, runs one frame of the view. The navigation bytes are taken
        /// again whenever the presented world has moved on a turn.</summary>
        private void UpdatePossession(bool inputAllowed)
        {
            WorldState world = World;
            var viewer = new PlayerId((byte)viewingPlayerId);
            int id = world.Player(viewer).PossessedCreatureId;
            CreatureState creature = id != 0 ? world.CreatureById(id) : null;
            if (creature == null)
            {
                if (Possessing)
                {
                    bool fly = PossessionCanFly;
                    CameraFlightState from = _possessionFlight.Active ? _possessionFlight.State
                        : fly ? PossessionFlight.FirstPerson(_possessionView, _possessionPreset) : default;
                    EndPossessionView();
                    if (fly) BeginPossessionFlyOut(from);
                    else if (_classicCamera == null || frameWholeMap) LookAt(world);
                }
                if (_possessionFlyingOut)
                {
                    _possessionFlight.Step(PossessionClock);
                    _possessionFlyingOut = _possessionFlight.Active;
                }
                return;
            }

            bool entered = _possessed == null || _possessed.Id != id;
            if (entered || !ReferenceEquals(world, _possessionBoundWorld) || world.CurrentTick.Value != _possessionBoundTick)
            {
                _possessionWorld ??= new PossessionWorld(_simulation.Database);
                _possessionWorld.Bind(world, creature);
                _possessionBoundWorld = world;
                _possessionBoundTick = world.CurrentTick.Value;
            }
            _possessed = creature;
            if (entered)
            {
                _possessionView.Enter(creature.PosX, creature.PosY,
                    creature.PosZ + FloorHeightRules.Ordinary, _possessionWorld.Body());
                _possessionControls.Reset();
                _creepingSent = null;
                _possessionMilliseconds = 0;
                _possessionMouse = default;
                _input.CancelPointerGesture();
                // DK2 flies the camera to the eye in a timed move (Camera_SetMode 0x0044ACE0). Remake choice: a move out still running is turned round.
                if (PossessionCanFly)
                    _possessionFlight.Start(
                        _possessionFlyingOut ? _possessionFlight.State
                            : _classicCamera.State(new System.Numerics.Vector3(_cameraFocus.x, _cameraFocus.y, _cameraFocus.z)),
                        PossessionFlight.FirstPerson(_possessionView, _possessionPreset), PossessionClock);
                else _possessionFlight.Cancel();
                _possessionFlyingOut = false;
            }

            // The first-person step, the look and the once-a-turn pose are mode 2's: none runs until the move lands.
            bool flying = _possessionFlight.Active;
            if (flying) _possessionFlight.Step(PossessionClock);
            bool active = inputAllowed && Application.isFocused;
            bool lockedBefore = _possessionPointerLocked;
            LockPossessionPointer(active);
            if (flying)
            {
                _possessionMilliseconds = 0;
                _possessionMouse = default;
                _possessionHeld = uint.MaxValue;
            }
            else if (active) StepPossession(creature, viewer, lookAllowed: lockedBefore);
            if (!_scriptedView.PathPlaying && (_classicCamera == null || frameWholeMap)) ApplyPossessionCamera();
        }

        private void StepPossession(CreatureState creature, PlayerId viewer, bool lookAllowed)
        {
            // The right button leaves: the owner's recollection (2026-11-14); the binary's sender of the leave action is unread.
            if (Input.GetMouseButtonUp(1)) _pendingCommands.Add(new LeavePossessionCommand(viewer));
            if (_possessionWorld.Frozen) return;

            // Remake choice: only the frames the step runs count as its time, and the remainder of a millisecond is carried.
            _possessionMilliseconds += Time.unscaledDeltaTime * 1000d;
            int milliseconds = (int)_possessionMilliseconds;
            _possessionMilliseconds -= milliseconds;

            // Remake choice: one pixel of host pointer travel is one unit of DK2's mouse delta, whose units are unread.
            // The frame the pointer is locked on is skipped: its delta is the jump to the middle.
            if (lookAllowed)
            {
                Vector3 moved = Input.mousePositionDelta;
                System.Numerics.Vector2 scaled = GameBindingInput.HostPointerDelta(
                    new System.Numerics.Vector2(moved.x, -moved.y), _gameSettings.MouseSensitivity);
                _possessionMouse += new Vector2(scaled.X, scaled.Y);
            }
            int mouseX = Mathf.RoundToInt(_possessionMouse.x), mouseY = Mathf.RoundToInt(_possessionMouse.y);
            _possessionMouse -= new Vector2(mouseX, mouseY);

            GameBindingInput input = ClassicKeyBindings.Read(_gameSettings);
            var keys = new PossessionKeys(
                input.Held(GameAction.ScrollUp), input.Held(GameAction.ScrollDown), input.Held(GameAction.ScrollLeft),
                input.Held(GameAction.ScrollRight), input.Held(GameAction.Run), input.Held(GameAction.Creep));
            PossessionFrame frame = _possessionView.Step(milliseconds, keys, mouseX, mouseY, _gameSettings.InvertMouse,
                _possessionWorld.Body(), _possessionWorld);

            // The body's creeping bit is the presented world's and lags the command by a turn: one command a change.
            if (frame.SetCreeping is bool creep)
            {
                if (_creepingSent != creep) _pendingCommands.Add(new SetCreepingCommand(viewer, creep));
                _creepingSent = creep;
            }
            else _creepingSent = null;

            CreatureDef definition = _simulation.Database.CreatureById(creature.CreatureTypeId);
            PlayPossessionFootstep(frame.LeftFoot, definition);
            PlayPossessionFootstep(frame.RightFoot, definition);

            // Remake choice: a press is the key going down whatever else is held, so Fire works while Run or Creep is.
            uint held = 0;
            foreach (GameAction action in PossessionPressActions)
                if (input.Held(action)) held |= 1u << (int)action;
            uint pressed = held & ~_possessionHeld;
            _possessionHeld = held;
            bool Pressed(GameAction action) => (pressed & (1u << (int)action)) != 0;

            if (Pressed(GameAction.Melee)) _possessionControls.SelectMelee();
            if (Pressed(GameAction.Spell1)) _possessionControls.SelectSpell(0, definition, creature.Level);
            if (Pressed(GameAction.Spell2)) _possessionControls.SelectSpell(1, definition, creature.Level);
            if (Pressed(GameAction.Spell3)) _possessionControls.SelectSpell(2, definition, creature.Level);
            if (Pressed(GameAction.SelectAlly)) _possessionControls.SelectFollowers();
            if (Pressed(GameAction.RemoveAlly)) _pendingCommands.Add(new DismissFollowersCommand(viewer));
            if (Pressed(GameAction.Sniper)) _pendingCommands.Add(new SetSniperModeCommand(viewer, !creature.SniperMode));
            if (Pressed(GameAction.Fire) || Input.GetMouseButtonDown(0))
                _pendingCommands.Add(_possessionControls.Fire(viewer, _possessionView));
        }

        private static readonly GameAction[] PossessionPressActions =
        {
            GameAction.Melee, GameAction.Spell1, GameAction.Spell2, GameAction.Spell3, GameAction.SelectAlly,
            GameAction.RemoveAlly, GameAction.Sniper, GameAction.Fire,
        };

        private uint _possessionHeld;

        private void PlayPossessionFootstep(PossessionFootstep? step, CreatureDef definition)
        {
            if (!step.HasValue || _soundPlayer == null) return;
            SoundRequest? request = PossessionFootsteps.For(definition.SoundCategory, step.Value);
            if (request.HasValue) _soundPlayer.Play(request.Value);
        }

        /// <summary>The once-a-turn pose (<c>0x004039A0</c>), put first among the commands of the input
        /// frame about to be handed to a tick.</summary>
        private void QueuePossessionPose()
        {
            if (!Possessing || _possessionFlight.Active) return;
            _pendingCommands.Insert(0, PossessionControls.Pose(
                new PlayerId((byte)viewingPlayerId), _possessionView, _possessionWorld.Body()));
        }

        /// <summary>Starts the move back to the ordinary camera (Camera_SetMode's ordinary arm): the
        /// ordinary camera is over the eye's point and faces the way the view did from the start, and the
        /// picture flies there. The focus is kept inside the map as the ordinary camera's always is.</summary>
        private void BeginPossessionFlyOut(in CameraFlightState from)
        {
            int one = Fp32.One;
            CameraFlightState to = _classicCamera.Return(from, Mathf.RoundToInt(_cameraFocus.y * one));
            to = new CameraFlightState(
                Mathf.Clamp(to.X, 0, Mathf.RoundToInt(_cameraBounds.x * one) - 1),
                Mathf.Clamp(to.Y, 0, Mathf.RoundToInt(_cameraBounds.y * one) - 1),
                to.Z, to.Zoom, to.Lens, to.ViewDistance, to.Yaw, to.Pitch, to.Roll);
            _classicCamera.Face(new Dk2Angle((ushort)to.Yaw));
            _cameraFocus = new Vector3(to.X / (float)one, _cameraFocus.y, -to.Y / (float)one);
            _possessionFlight.Start(from, to, PossessionClock);
            _possessionFlyingOut = _possessionFlight.Active;
        }

        /// <summary>The picture during the timed move, from the ordinary camera's own pose formula.</summary>
        private void ApplyPossessionFlight()
        {
            Camera camera = Camera.main;
            if (camera == null || camera.pixelWidth <= 0 || camera.pixelHeight <= 0) return;
            int screenWidth = camera.targetTexture != null ? camera.targetTexture.width : Screen.width;
            if (screenWidth <= 0) return;
            float progress = _possessionFlight.Progress;
            ApplyCameraPose(camera, _classicCamera.Pose(_possessionFlight.State,
                new CameraViewport(screenWidth, camera.pixelWidth, camera.pixelHeight),
                _possessionFlyingOut ? 1f - progress : progress));
        }

        private void ApplyPossessionCamera()
        {
            Camera camera = Camera.main;
            if (camera == null || camera.pixelWidth <= 0 || camera.pixelHeight <= 0) return;
            camera.orthographic = false;
            // Clears the ordinary camera's off-centre frustum: the first-person view axis is the screen's middle (a remake choice).
            camera.ResetProjectionMatrix();
            // Without preset 2 the field of view and far plane stay as the ordinary camera last set them.
            if (_possessionPreset != null && _possessionPreset.Lens > 0)
            {
                camera.fieldOfView = PossessionCamera.VerticalFieldOfView(_possessionPreset);
                camera.farClipPlane = Mathf.Max(camera.nearClipPlane + 0.01f, PossessionCamera.FarClipDistance(_possessionPreset));
            }
            PossessionCameraPose pose = PossessionCamera.Pose(_possessionView);
            // The side a positive roll leans to is not read: this sign is a HYPOTHESIS.
            camera.transform.SetPositionAndRotation(
                new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z),
                Quaternion.LookRotation(new Vector3(pose.Forward.X, pose.Forward.Y, pose.Forward.Z), Vector3.up)
                    * Quaternion.AngleAxis(pose.RollDegrees, Vector3.forward));
        }

        // A locked pointer is hidden and held at the window's middle; the Hand is not drawn while it is.
        private void LockPossessionPointer(bool locked)
        {
            if (_possessionPointerLocked == locked) return;
            _possessionPointerLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        }

        private void EndPossessionView()
        {
            _possessed = null;
            _possessionWorld = null;
            _possessionBoundWorld = null;
            _possessionFlight.Cancel();
            _possessionFlyingOut = false;
            LockPossessionPointer(false);
        }
    }
}
