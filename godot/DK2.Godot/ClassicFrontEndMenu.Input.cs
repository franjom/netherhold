// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The front end's pointer and keys: the OS pointer polled once a frame (as Unity reads
/// <c>Input.mousePosition</c>), or, for a check run, <c>--front-end-pointer-path</c> (<see cref="PointerScript"/>, the
/// in-game <c>--pointer-path</c>'s form) and <c>--front-end-keys</c> (<see cref="KeyScript"/>, Godot <see cref="Key"/>
/// names) injected through Godot's input, so a capture never moves the owner's cursor or types into another window. Both
/// count <see cref="Engine.GetProcessFrames"/>, so one script spans every front end of the run (before a level and after
/// it); with a scripted pointer the OS mouse stops at <see cref="ScriptedPointerGate"/>.</summary>
public partial class ClassicFrontEndMenu
{
    private PointerScript? _pointerScript;
    private PointerScriptSample _scripted;
    private KeyScript? _keyScript;
    private int _nextKey;

    private void OpenInput(string[] args)
    {
        string? path = LaunchArguments.Value(args, "--front-end-pointer-path");
        _pointerScript = LaunchArguments.ParsePointerPath(path);
        if (_pointerScript == null && path != null)
            GD.PushWarning("DK2.Godot: --front-end-pointer-path is malformed (LaunchArguments.ParsePointerPath); the OS pointer is used.");
        string? keys = LaunchArguments.Value(args, "--front-end-keys");
        _keyScript = KeyScript.Parse(keys);
        if (_keyScript == null && keys != null) GD.PushWarning("DK2.Godot: --front-end-keys is malformed (KeyScript.Parse); no keys are injected.");
        if (_keyScript != null)
            foreach (KeyScriptKey tap in _keyScript.Keys)
                if (!Enum.TryParse(tap.Key, out Key _)) GD.PushWarning("DK2.Godot: --front-end-keys names no Godot key: ", tap.Key);
        _nextKey = _keyScript?.FirstAtOrAfter((int)Engine.GetProcessFrames()) ?? 0;
        if (_pointerScript != null) AddChild(new ScriptedPointerGate { Name = "ScriptedPointerGate" });
    }

    /// <summary>The frame's pointer in window pixels and the primary button: the script's sample at this process frame, or
    /// the OS's.</summary>
    private (Vector2 Pointer, bool PrimaryHeld) SamplePointer()
    {
        if (_pointerScript == null) return (GetViewport().GetMousePosition(), Input.IsMouseButtonPressed(MouseButton.Left));
        _scripted = _pointerScript.At((int)Engine.GetProcessFrames());
        return (new Vector2(_scripted.Position.X, _scripted.Position.Y), _scripted.PrimaryHeld);
    }

    /// <summary>The key taps due at this process frame, each a press and a release through Godot's input, as a real key
    /// arrives; the press carries the key's character (its name's when it is one letter, the digit of <c>Key0</c> to <c>Key9</c>, a space for Space).</summary>
    private void InjectKeys()
    {
        if (_keyScript == null) return;
        int frame = (int)Engine.GetProcessFrames();
        IReadOnlyList<KeyScriptKey> taps = _keyScript.Keys;
        _nextKey = _keyScript.FirstAtOrAfter(frame, _nextKey);
        while (_nextKey < taps.Count && taps[_nextKey].Frame == frame)
        {
            string name = taps[_nextKey++].Key;
            if (!Enum.TryParse(name, out Key key)) continue;
            long unicode = name.Length == 1 ? name[0] : key == Key.Space ? ' '
                : key is >= Key.Key0 and <= Key.Key9 ? '0' + (key - Key.Key0) : 0;
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        }
    }
}
