// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using DK2.Data.Settings;
using UnityEngine;

namespace DK2.Unity
{
    /// <summary>Platform translation of the settings' DirectInput scan codes.</summary>
    public static class ClassicKeyBindings
    {
        public static KeyModifiers Modifiers =>
            ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? KeyModifiers.Shift : 0)
            | ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) ? KeyModifiers.Control : 0)
            | ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) ? KeyModifiers.Alt : 0);
        public static bool Held(GameSettings settings, GameAction action, bool exactModifiers = false) =>
            Matches(settings.Binding(action), false, exactModifiers);
        public static bool Pressed(GameSettings settings, GameAction action) => Matches(settings.Binding(action), true, true);
        private static bool Matches(GameKeyBinding binding, bool pressed, bool exact)
        {
            KeyCode key = Key(binding.ScanCode); if (key == KeyCode.None) return false;
            KeyModifiers held = Modifiers;
            if (exact ? held != binding.Modifiers : (held & binding.Modifiers) != binding.Modifiers) return false;
            return pressed ? Input.GetKeyDown(key) : Input.GetKey(key);
        }
        public static string Name(GameKeyBinding binding) => binding.ScanCode == 0 ? "-"
            : (binding.Modifiers == KeyModifiers.None ? "" : binding.Modifiers + " + ") + Key(binding.ScanCode);
        public static KeyScanCode ScanCode(KeyCode key)
        {
            for (int scan = 1; scan < 256; scan++) if (Key(scan) == key) return scan;
            return 0;
        }
        public static KeyCode Key(KeyScanCode scan) => scan.Value switch
        {
            0x01 => KeyCode.Escape,
            0x02 => KeyCode.Alpha1,
            0x03 => KeyCode.Alpha2,
            0x04 => KeyCode.Alpha3,
            0x05 => KeyCode.Alpha4,
            0x06 => KeyCode.Alpha5,
            0x07 => KeyCode.Alpha6,
            0x08 => KeyCode.Alpha7,
            0x09 => KeyCode.Alpha8,
            0x0A => KeyCode.Alpha9,
            0x0B => KeyCode.Alpha0,
            0x0C => KeyCode.Minus,
            0x0D => KeyCode.Equals,
            0x0E => KeyCode.Backspace,
            0x0F => KeyCode.Tab,
            0x10 => KeyCode.Q,
            0x11 => KeyCode.W,
            0x12 => KeyCode.E,
            0x13 => KeyCode.R,
            0x14 => KeyCode.T,
            0x15 => KeyCode.Y,
            0x16 => KeyCode.U,
            0x17 => KeyCode.I,
            0x18 => KeyCode.O,
            0x19 => KeyCode.P,
            0x1A => KeyCode.LeftBracket,
            0x1B => KeyCode.RightBracket,
            0x1C => KeyCode.Return,
            0x1D => KeyCode.LeftControl,
            0x1E => KeyCode.A,
            0x1F => KeyCode.S,
            0x20 => KeyCode.D,
            0x21 => KeyCode.F,
            0x22 => KeyCode.G,
            0x23 => KeyCode.H,
            0x24 => KeyCode.J,
            0x25 => KeyCode.K,
            0x26 => KeyCode.L,
            0x27 => KeyCode.Semicolon,
            0x28 => KeyCode.Quote,
            0x29 => KeyCode.BackQuote,
            0x2A => KeyCode.LeftShift,
            0x2B => KeyCode.Backslash,
            0x2C => KeyCode.Z,
            0x2D => KeyCode.X,
            0x2E => KeyCode.C,
            0x2F => KeyCode.V,
            0x30 => KeyCode.B,
            0x31 => KeyCode.N,
            0x32 => KeyCode.M,
            0x33 => KeyCode.Comma,
            0x34 => KeyCode.Period,
            0x35 => KeyCode.Slash,
            0x36 => KeyCode.RightShift,
            0x38 => KeyCode.LeftAlt,
            0x39 => KeyCode.Space,
            0x3A => KeyCode.CapsLock,
            0x3B => KeyCode.F1,
            0x3C => KeyCode.F2,
            0x3D => KeyCode.F3,
            0x3E => KeyCode.F4,
            0x3F => KeyCode.F5,
            0x40 => KeyCode.F6,
            0x41 => KeyCode.F7,
            0x42 => KeyCode.F8,
            0x43 => KeyCode.F9,
            0x44 => KeyCode.F10,
            0x45 => KeyCode.Numlock,
            0x46 => KeyCode.ScrollLock,
            0x47 => KeyCode.Keypad7,
            0x48 => KeyCode.Keypad8,
            0x49 => KeyCode.Keypad9,
            0x4A => KeyCode.KeypadMinus,
            0x4B => KeyCode.Keypad4,
            0x4C => KeyCode.Keypad5,
            0x4D => KeyCode.Keypad6,
            0x4E => KeyCode.KeypadPlus,
            0x4F => KeyCode.Keypad1,
            0x50 => KeyCode.Keypad2,
            0x51 => KeyCode.Keypad3,
            0x52 => KeyCode.Keypad0,
            0x53 => KeyCode.KeypadPeriod,
            0x57 => KeyCode.F11,
            0x58 => KeyCode.F12,
            0x9C => KeyCode.KeypadEnter,
            0x9D => KeyCode.RightControl,
            0xB5 => KeyCode.KeypadDivide,
            0xB7 => KeyCode.Print,
            0xB8 => KeyCode.RightAlt,
            0xC7 => KeyCode.Home,
            0xC8 => KeyCode.UpArrow,
            0xC9 => KeyCode.PageUp,
            0xCB => KeyCode.LeftArrow,
            0xCD => KeyCode.RightArrow,
            0xCF => KeyCode.End,
            0xD0 => KeyCode.DownArrow,
            0xD1 => KeyCode.PageDown,
            0xD2 => KeyCode.Insert,
            0xD3 => KeyCode.Delete,
            _ => KeyCode.None,
        };
    }
}
