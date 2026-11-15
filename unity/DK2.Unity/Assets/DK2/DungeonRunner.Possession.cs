// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>The host side of the first-person view: reads Unity's keys, mouse and clock into a
    /// <see cref="PossessionInput"/>, and applies what <see cref="PossessionSession"/> answers (the
    /// camera, the pointer lock, the hidden model, commands and sounds). Every rule is the session's
    /// (ADR-0805); it reads only the presented world.</summary>
    public sealed partial class DungeonRunner
    {
        private readonly List<SoundRequest> _possessionSounds = new List<SoundRequest>();
        private PossessionSession _possession;
        private bool _possessionPointerLocked;

        private bool Possessing => _possession != null && _possession.Possessing;
        private bool PossessionCameraHeld => _possession != null && _possession.CameraHeld;
        private int PossessionHiddenCreatureId => _possession != null ? _possession.HiddenCreatureId : 0;

        // Without the ordinary camera the session has nothing to fly from or to, and the view cuts.
        private ClassicCamera PossessionOrdinaryCamera => frameWholeMap ? null : _classicCamera;

        private void UpdatePossession(bool inputAllowed)
        {
            if (_possession == null) return;
            WorldState world = World;
            Vector3 moved = Input.mousePositionDelta;
            var input = new PossessionInput(
                inputAllowed && Application.isFocused,
                (long)(Time.unscaledTimeAsDouble * 1000d), Time.unscaledDeltaTime * 1000d,
                new System.Numerics.Vector2(moved.x, -moved.y),
                Input.GetMouseButtonDown(0), Input.GetMouseButtonUp(1),
                ClassicKeyBindings.Read(_gameSettings), _gameSettings.MouseSensitivity, _gameSettings.InvertMouse);
            PossessionUpdate update = _possession.Update(world, new PlayerId((byte)viewingPlayerId), input,
                PossessionOrdinaryCamera, new System.Numerics.Vector3(_cameraFocus.x, _cameraFocus.y, _cameraFocus.z),
                _pendingCommands, _possessionSounds);

            foreach (SoundRequest sound in _possessionSounds) _soundPlayer?.Play(sound);
            _possessionSounds.Clear();
            if (update.Entered) _input.CancelPointerGesture();
            if (update.OrdinaryFocus is System.Numerics.Vector3 focus) _cameraFocus = new Vector3(focus.X, focus.Y, focus.Z);
            else if (update.Ended && PossessionOrdinaryCamera == null) LookAt(world);
            LockPossessionPointer(_possession.PointerLockWanted);
            if (Possessing && !_scriptedView.PathPlaying && PossessionOrdinaryCamera == null) ApplyPossessionCamera();
        }

        private void QueuePossessionPose()
        {
            PlayerCommand pose = _possession?.TurnPose(new PlayerId((byte)viewingPlayerId));
            if (pose != null) _pendingCommands.Insert(0, pose);
        }

        /// <summary>Places the camera when the picture is the possession's; false leaves it to the ordinary camera.</summary>
        private bool ApplyPossessionCamera()
        {
            Camera camera = Camera.main;
            if (_possession == null || camera == null || camera.pixelWidth <= 0 || camera.pixelHeight <= 0) return false;
            int screenWidth = camera.targetTexture != null ? camera.targetTexture.width : Screen.width;
            if (screenWidth <= 0) return false;
            if (!_possession.TryCamera(PossessionOrdinaryCamera,
                    new CameraViewport(screenWidth, camera.pixelWidth, camera.pixelHeight), out ClassicCameraPose pose))
                return false;
            ApplyCameraPose(camera, pose);
            return true;
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
            _possession?.Reset();
            LockPossessionPointer(false);
        }
    }
}
