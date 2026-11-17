// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Godot;

namespace DK2.GodotAdapter;

/// <summary>While a scripted pointer drives the scene (<c>--pointer-path</c>), the one gate the OS mouse passes: its
/// buttons, wheel and motion are marked handled before any panel's <c>_Input</c> sees them, so a stray real click cannot
/// change a capture; the script's own events carry <see cref="Device"/> and pass. Keys pass. Added as the scene's last
/// child, since Godot calls <c>_Input</c> in reverse tree order (<c>SceneTree::_call_input_pause</c>, c51ff06e8).</summary>
public partial class ScriptedPointerGate : Node
{
    /// <summary>The device id the scripted pointer's events carry: negative, as no physical device is.</summary>
    public const int Device = -77;

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouse && @event.Device != Device) GetViewport().SetInputAsHandled();
    }
}
