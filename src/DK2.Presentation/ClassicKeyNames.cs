// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;

namespace DK2.Presentation;

/// <summary>The name the game options key list shows for a DirectInput scan code (<see cref="GameOptionsHud"/>'s key
/// column). The labels preserve the key names already used by the project. An unmapped scan code displays "None".</summary>
public static class ClassicKeyNames
{
    private static readonly string[] Names = Build();

    /// <summary>The key's name; "None" for a scan code no key maps to.</summary>
    public static string Name(KeyScanCode scan) => scan.Value is > 0 and < 256 ? Names[scan.Value] : "None";

    private static string[] Build()
    {
        string[] names = new string[256];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = "None";
        }

        void Row(int first, params string[] row) { for (int i = 0; i < row.Length; i++) { names[first + i] = row[i]; } }
        Row(0x01, "Escape", "Alpha1", "Alpha2", "Alpha3", "Alpha4", "Alpha5", "Alpha6", "Alpha7", "Alpha8", "Alpha9", "Alpha0",
            "Minus", "Equals", "Backspace", "Tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "LeftBracket", "RightBracket",
            "Return", "LeftControl", "A", "S", "D", "F", "G", "H", "J", "K", "L", "Semicolon", "Quote", "BackQuote", "LeftShift",
            "Backslash", "Z", "X", "C", "V", "B", "N", "M", "Comma", "Period", "Slash", "RightShift");
        Row(0x38, "LeftAlt", "Space", "CapsLock", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "Numlock",
            "ScrollLock", "Keypad7", "Keypad8", "Keypad9", "KeypadMinus", "Keypad4", "Keypad5", "Keypad6", "KeypadPlus",
            "Keypad1", "Keypad2", "Keypad3", "Keypad0", "KeypadPeriod");
        Row(0x57, "F11", "F12");
        Row(0x9C, "KeypadEnter", "RightControl");
        Row(0xB5, "KeypadDivide");
        Row(0xB7, "Print", "RightAlt");
        Row(0xC7, "Home", "UpArrow", "PageUp");
        Row(0xCB, "LeftArrow");
        Row(0xCD, "RightArrow");
        Row(0xCF, "End", "DownArrow", "PageDown", "Insert", "Delete");
        return names;
    }
}
