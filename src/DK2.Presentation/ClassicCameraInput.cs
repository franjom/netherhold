// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Data.Settings;

namespace DK2.Presentation;

/// <summary>The frame's pointer as the ordinary camera reads it.</summary>
/// <param name="Position">Host pixels from the screen's bottom-left corner, y up (Unity's <c>Input.mousePosition</c>). A
/// top-left host passes <c>y = ScreenHeight - yTop</c>, not <c>ScreenHeight - 1 - yTop</c>: Unity documents the range as
/// (0, 0) bottom-left to (Screen.width, Screen.height) top-right, an edge-to-edge flip, and Godot's pointer is the same
/// continuous span from the top. So yTop 0 maps to ScreenHeight, which the on-screen test rejects; the Up band is
/// 0 &lt; yTop &lt; 5 and the Down band ScreenHeight - 5 &lt; yTop. From the documented range, not measured on a window.</param>
/// <param name="Focused">Whether the game window has the focus; edge scrolling needs it.</param>
/// <param name="OverHud">Whether the pointer is over a HUD panel (<see cref="HudPointerMap.Over{TPanels}"/>); the wheel
/// zooms only over the world.</param>
/// <param name="Wheel">The wheel's vertical step this frame, positive away from the user.</param>
public readonly record struct CameraPointerSample(Vector2 Position, int ScreenWidth, int ScreenHeight, bool Focused,
    bool OverHud, float Wheel);

/// <summary>The ordinary camera's input mapping, from Unity's runner (migration plan step 2i): the edge scroll, the
/// bound keys, the wheel and key zoom and the held-button mouse rotate and zoom, over <see cref="ClassicCameraControls"/>.</summary>
public static class ClassicCameraInput
{
    /// <summary>The edge-scroll band, pixels: 0x00406DF0's strict test (x &lt; 5, x &gt; width - 5, likewise y).</summary>
    public const int EdgeScrollMargin = 5;

    /// <summary>The edges the pointer is within <see cref="EdgeScrollMargin"/> of, only while the window has the focus
    /// and the pointer is on the screen.</summary>
    public static ClassicCameraKeys EdgeScroll(in CameraPointerSample pointer)
    {
        ClassicCameraKeys edges = ClassicCameraKeys.None;
        Vector2 p = pointer.Position;
        if (!pointer.Focused || !(p.X >= 0 && p.X < pointer.ScreenWidth && p.Y >= 0 && p.Y < pointer.ScreenHeight)) return edges;
        if (p.X < EdgeScrollMargin) edges |= ClassicCameraKeys.Left;
        if (p.X > pointer.ScreenWidth - EdgeScrollMargin) edges |= ClassicCameraKeys.Right;
        if (p.Y > pointer.ScreenHeight - EdgeScrollMargin) edges |= ClassicCameraKeys.Up;
        if (p.Y < EdgeScrollMargin) edges |= ClassicCameraKeys.Down;
        return edges;
    }

    /// <summary>A frame's length in seconds as the <see cref="TimeSpan"/> <see cref="ClassicCameraControls.Advance"/>
    /// takes, rounded to the nearest 100 ns tick.</summary>
    public static TimeSpan FrameTime(double seconds) =>
        TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

    /// <summary>The native pointer delta (right and down) between two y-up host positions, scaled by the mouse sensitivity
    /// (<see cref="GameBindingInput.HostPointerDelta"/>).</summary>
    public static Vector2 PointerDelta(Vector2 previous, Vector2 current, int mouseSensitivity) =>
        GameBindingInput.HostPointerDelta(new Vector2(current.X - previous.X, previous.Y - current.Y), mouseSensitivity);

    /// <summary>One frame of the ordinary camera: the pan by the bound keys and the edges, then one zoom step (the wheel
    /// away from the HUD, or the zoom keys), then the held-button rotate or zoom by the pointer's move since
    /// <paramref name="previousPointer"/>. Returns the new focus.</summary>
    public static Vector3 Apply(ClassicCameraControls controls, Vector3 focus, in GameBindingInput input,
        in CameraPointerSample pointer, Vector2 previousPointer, int mouseSensitivity, double frameSeconds)
    {
        if (controls is null) throw new ArgumentNullException(nameof(controls));
        ClassicCameraKeys keys = input.CameraKeys;
        ClassicCameraKeys edges = EdgeScroll(pointer);
        Vector3 next = controls.Advance(focus, keys, FrameTime(frameSeconds), edges);
        ClassicCamera camera = controls.Camera;
        float wheel = pointer.OverHud ? 0 : pointer.Wheel;
        if (wheel > 0 || input.ZoomIn) camera.ZoomIn();
        else if (wheel < 0 || input.ZoomOut) camera.ZoomOut();
        controls.AdvancePointer(PointerDelta(previousPointer, pointer.Position, mouseSensitivity),
            input.Held(GameAction.MouseRotate, exactModifiers: true), input.Held(GameAction.MouseZoom, exactModifiers: true));
        return next;
    }
}
