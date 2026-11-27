// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

/// <summary>The ordinary camera a level starts with: the chosen
/// preset with its zoom-out limit scaled, the reference screen width, the controls over the map's focus bounds, and the
/// viewer's start focus.</summary>
public sealed class ClassicCameraSetup
{
    /// <summary>The screen width whose camera distance and horizontal field of view every resolution keeps
    /// (<see cref="ClassicCamera.ReferenceScreenWidth"/>; the owner's native recording, classic-camera.md §8).</summary>
    public const int ReferenceScreenWidth = 1600;

    /// <summary>The zoom-out limit scale both adapters set the camera up with (a remake option; 1 is DK2's own bound):
    /// 1.3, the value Unity's <c>Dungeon.unity</c> serialised for the runner's inspector field it replaces.</summary>
    public const float DefaultZoomOutLimitScale = 1.3f;

    private ClassicCameraSetup(ClassicCamera camera, ClassicCameraControls controls, Vector3 startFocus)
    {
        Camera = camera; Controls = controls; StartFocus = startFocus;
    }

    public ClassicCamera Camera { get; }
    public ClassicCameraControls Controls { get; }
    /// <summary>The level start's focus (<see cref="ClassicCamera.LevelStartFocus(IReadOnlyList{PlayerDef}, PlayerId, int, int)"/>).</summary>
    public Vector3 StartFocus { get; }

    /// <summary>The first preset with <paramref name="mode"/>'s camera id, or null.</summary>
    public static CameraThing? Preset(IReadOnlyList<CameraThing> presets, ClassicCameraMode mode)
    {
        return Find(presets, (ushort)mode);
    }

    /// <summary>The first preset with the possession camera's id (<see cref="PossessionCamera.PresetId"/>), or null.</summary>
    public static CameraThing? PossessionPreset(IReadOnlyList<CameraThing> presets)
    {
        return Find(presets, PossessionCamera.PresetId);
    }

    /// <summary>The preset's zoom-out limit times <paramref name="scale"/>, the scale kept within [1, 2] (1 is DK2's own
    /// bound; a remake option), rounded half to even as Unity's <c>Mathf.RoundToInt</c> does.</summary>
    public static int ZoomOutLimit(int maxZoom, float scale)
    {
        return (int)Math.Round((float)(maxZoom * CameraFocus.Clamp(scale, 1f, 2f)));
    }

    /// <summary>The camera, its controls and the start focus for a level <paramref name="mapWidth"/> by
    /// <paramref name="mapHeight"/> tiles; <paramref name="players"/> is null for a level with no player records.</summary>
    public static ClassicCameraSetup Create(CameraThing preset, float zoomOutLimitScale, int ticksPerSecond, int scrollSpeed,
        IReadOnlyList<PlayerDef>? players, PlayerId viewer, int mapWidth, int mapHeight)
    {
        if (preset is null)
        {
            throw new ArgumentNullException(nameof(preset));
        }

        var camera = new ClassicCamera(preset with { MaxZoom = ZoomOutLimit(preset.MaxZoom, zoomOutLimitScale) },
            initialYaw: ClassicCamera.LevelStartYaw) { ReferenceScreenWidth = ReferenceScreenWidth };
        var controls = new ClassicCameraControls(camera, ticksPerSecond, scrollSpeed, CameraFocus.Bounds(mapWidth, mapHeight));
        return new ClassicCameraSetup(camera, controls, ClassicCamera.LevelStartFocus(players, viewer, mapWidth, mapHeight));
    }

    private static CameraThing? Find(IReadOnlyList<CameraThing> presets, ushort cameraId)
    {
        if (presets is null)
        {
            throw new ArgumentNullException(nameof(presets));
        }

        for (int i = 0; i < presets.Count; i++)
        {
            if (presets[i].CameraId == cameraId)
            {
                return presets[i];
            }
        }

        return null;
    }
}
