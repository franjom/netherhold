// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>Maps physical key positions to DirectInput scan codes for the settings. <see cref="Read"/> answers held and newly pressed keys for <see cref="GameBindingInput"/>. <see cref="Typed"/> reports text typed since <see cref="EndFrame"/>.</summary>
public sealed class ClassicKeyboard
{
    private readonly bool[] _held = new bool[256];
    private readonly bool[] _pressed = new bool[256];
    private readonly Func<KeyScanCode, bool> _readHeld, _readPressed;
    /// <summary>This frame's typed characters (<see cref="TypedTextBuffer"/> decides them and the cap).</summary>
    private readonly TypedTextBuffer _typed = new();

    public ClassicKeyboard()
    {
        _readHeld = scan => scan.Value is > 0 and < 256 && _held[scan.Value];
        _readPressed = scan => scan.Value is > 0 and < 256 && _pressed[scan.Value];
    }

    /// <summary>Shift, Control and Alt from either side.</summary>
    public KeyModifiers Modifiers
    {
        get
        {
            return (_held[0x2A] || _held[0x36] ? KeyModifiers.Shift : 0)
        | (_held[0x1D] || _held[0x9D] ? KeyModifiers.Control : 0)
        | (_held[0x38] || _held[0xB8] ? KeyModifiers.Alt : 0);
        }
    }

    /// <summary>Escape went down this frame, whatever it is bound to.</summary>
    public bool EscapePressed
    {
        get
        {
            return _pressed[0x01];
        }
    }

    /// <summary>Space went down this frame, whatever it is bound to.</summary>
    public bool SpacePressed
    {
        get
        {
            return _pressed[0x39];
        }
    }

    public GameBindingInput Read(GameSettings settings)
    {
        return new(settings, Modifiers, _readHeld, _readPressed);
    }

    /// <summary>Characters typed since the last <see cref="EndFrame"/>, including key repeats.</summary>
    public ReadOnlySpan<char> Typed
    {
        get
        {
            return _typed.Text;
        }
    }

    /// <summary>Takes a key event; returns whether it was a key the table knows.</summary>
    public bool Feed(InputEventKey key)
    {
        if (key.Pressed)
        {
            Type(key);
        }

        int scan = ScanCode(key.PhysicalKeycode, key.Location);
        if (scan == 0)
        {
            return false;
        }

        if (key.Pressed) { if (!key.Echo && !_held[scan]) { _pressed[scan] = true; } _held[scan] = true; }
        else
        {
            _held[scan] = false;
        }

        return true;
    }

    /// <summary>Forgets the keys pressed this frame; call once the frame has read them.</summary>
    public void EndFrame() { Array.Clear(_pressed); _typed.Clear(); }

    /// <summary>Releases every key, for a window that lost focus and so misses the releases.</summary>
    public void ReleaseAll() { Array.Clear(_held); Array.Clear(_pressed); _typed.Clear(); }

    /// <summary>A pressed key as <see cref="TypedTextBuffer"/> takes it: Backspace and either Enter by their logical key,
    /// anything else by its Unicode code point (0 for none, which types nothing).</summary>
    private void Type(InputEventKey key)
    {
        Key logical = key.Keycode;
        TypedKey kind = logical == Key.Backspace ? TypedKey.Backspace
            : logical is Key.Enter or Key.KpEnter ? TypedKey.Enter : TypedKey.Other;
        long unicode = key.Unicode;
        _typed.Add(kind, unicode is > 0 and <= uint.MaxValue ? (uint)unicode : 0);
    }

    /// <summary>Returns the DirectInput scan code for a physical key, or zero for an unmapped key.
    /// An unspecified side (<see cref="KeyLocation.Unspecified"/>) resolves to the left key.</summary>
    public static int ScanCode(Key key, KeyLocation location)
    {
        bool right = location == KeyLocation.Right;
        return key switch
        {
            Key.Escape => 0x01,
            Key.Key1 => 0x02, Key.Key2 => 0x03, Key.Key3 => 0x04, Key.Key4 => 0x05, Key.Key5 => 0x06,
            Key.Key6 => 0x07, Key.Key7 => 0x08, Key.Key8 => 0x09, Key.Key9 => 0x0A, Key.Key0 => 0x0B,
            Key.Minus => 0x0C, Key.Equal => 0x0D, Key.Backspace => 0x0E, Key.Tab => 0x0F,
            Key.Q => 0x10, Key.W => 0x11, Key.E => 0x12, Key.R => 0x13, Key.T => 0x14,
            Key.Y => 0x15, Key.U => 0x16, Key.I => 0x17, Key.O => 0x18, Key.P => 0x19,
            Key.Bracketleft => 0x1A, Key.Bracketright => 0x1B, Key.Enter => 0x1C,
            Key.Ctrl => right ? 0x9D : 0x1D,
            Key.A => 0x1E, Key.S => 0x1F, Key.D => 0x20, Key.F => 0x21, Key.G => 0x22,
            Key.H => 0x23, Key.J => 0x24, Key.K => 0x25, Key.L => 0x26,
            Key.Semicolon => 0x27, Key.Apostrophe => 0x28, Key.Quoteleft => 0x29,
            Key.Shift => right ? 0x36 : 0x2A,
            Key.Backslash => 0x2B,
            Key.Z => 0x2C, Key.X => 0x2D, Key.C => 0x2E, Key.V => 0x2F, Key.B => 0x30, Key.N => 0x31, Key.M => 0x32,
            Key.Comma => 0x33, Key.Period => 0x34, Key.Slash => 0x35,
            Key.Alt => right ? 0xB8 : 0x38,
            Key.Space => 0x39, Key.Capslock => 0x3A,
            Key.F1 => 0x3B, Key.F2 => 0x3C, Key.F3 => 0x3D, Key.F4 => 0x3E, Key.F5 => 0x3F,
            Key.F6 => 0x40, Key.F7 => 0x41, Key.F8 => 0x42, Key.F9 => 0x43, Key.F10 => 0x44,
            Key.Numlock => 0x45, Key.Scrolllock => 0x46,
            Key.Kp7 => 0x47, Key.Kp8 => 0x48, Key.Kp9 => 0x49, Key.KpSubtract => 0x4A,
            Key.Kp4 => 0x4B, Key.Kp5 => 0x4C, Key.Kp6 => 0x4D, Key.KpAdd => 0x4E,
            Key.Kp1 => 0x4F, Key.Kp2 => 0x50, Key.Kp3 => 0x51, Key.Kp0 => 0x52, Key.KpPeriod => 0x53,
            Key.F11 => 0x57, Key.F12 => 0x58,
            Key.KpEnter => 0x9C, Key.KpDivide => 0xB5, Key.Print => 0xB7,
            Key.Home => 0xC7, Key.Up => 0xC8, Key.Pageup => 0xC9, Key.Left => 0xCB, Key.Right => 0xCD,
            Key.End => 0xCF, Key.Down => 0xD0, Key.Pagedown => 0xD1, Key.Insert => 0xD2, Key.Delete => 0xD3,
            _ => 0,
        };
    }
}
