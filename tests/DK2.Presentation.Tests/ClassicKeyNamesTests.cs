// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;

namespace DK2.Presentation.Tests;

/// <summary>The key list's names: Unity's <c>KeyCode</c> names for the scan codes its <c>ClassicKeyBindings.Key</c> maps.</summary>
public sealed class ClassicKeyNamesTests
{
    /// <summary>Unity's <c>ClassicKeyBindings.Key</c> table, row by row, as scan code and <c>KeyCode</c> name.</summary>
    private static readonly Dictionary<int, string> UnityTable = new()
    {
        [0x01] = "Escape",
        [0x02] = "Alpha1",
        [0x03] = "Alpha2",
        [0x04] = "Alpha3",
        [0x05] = "Alpha4",
        [0x06] = "Alpha5",
        [0x07] = "Alpha6",
        [0x08] = "Alpha7",
        [0x09] = "Alpha8",
        [0x0A] = "Alpha9",
        [0x0B] = "Alpha0",
        [0x0C] = "Minus",
        [0x0D] = "Equals",
        [0x0E] = "Backspace",
        [0x0F] = "Tab",
        [0x10] = "Q",
        [0x11] = "W",
        [0x12] = "E",
        [0x13] = "R",
        [0x14] = "T",
        [0x15] = "Y",
        [0x16] = "U",
        [0x17] = "I",
        [0x18] = "O",
        [0x19] = "P",
        [0x1A] = "LeftBracket",
        [0x1B] = "RightBracket",
        [0x1C] = "Return",
        [0x1D] = "LeftControl",
        [0x1E] = "A",
        [0x1F] = "S",
        [0x20] = "D",
        [0x21] = "F",
        [0x22] = "G",
        [0x23] = "H",
        [0x24] = "J",
        [0x25] = "K",
        [0x26] = "L",
        [0x27] = "Semicolon",
        [0x28] = "Quote",
        [0x29] = "BackQuote",
        [0x2A] = "LeftShift",
        [0x2B] = "Backslash",
        [0x2C] = "Z",
        [0x2D] = "X",
        [0x2E] = "C",
        [0x2F] = "V",
        [0x30] = "B",
        [0x31] = "N",
        [0x32] = "M",
        [0x33] = "Comma",
        [0x34] = "Period",
        [0x35] = "Slash",
        [0x36] = "RightShift",
        [0x38] = "LeftAlt",
        [0x39] = "Space",
        [0x3A] = "CapsLock",
        [0x3B] = "F1",
        [0x3C] = "F2",
        [0x3D] = "F3",
        [0x3E] = "F4",
        [0x3F] = "F5",
        [0x40] = "F6",
        [0x41] = "F7",
        [0x42] = "F8",
        [0x43] = "F9",
        [0x44] = "F10",
        [0x45] = "Numlock",
        [0x46] = "ScrollLock",
        [0x47] = "Keypad7",
        [0x48] = "Keypad8",
        [0x49] = "Keypad9",
        [0x4A] = "KeypadMinus",
        [0x4B] = "Keypad4",
        [0x4C] = "Keypad5",
        [0x4D] = "Keypad6",
        [0x4E] = "KeypadPlus",
        [0x4F] = "Keypad1",
        [0x50] = "Keypad2",
        [0x51] = "Keypad3",
        [0x52] = "Keypad0",
        [0x53] = "KeypadPeriod",
        [0x57] = "F11",
        [0x58] = "F12",
        [0x9C] = "KeypadEnter",
        [0x9D] = "RightControl",
        [0xB5] = "KeypadDivide",
        [0xB7] = "Print",
        [0xB8] = "RightAlt",
        [0xC7] = "Home",
        [0xC8] = "UpArrow",
        [0xC9] = "PageUp",
        [0xCB] = "LeftArrow",
        [0xCD] = "RightArrow",
        [0xCF] = "End",
        [0xD0] = "DownArrow",
        [0xD1] = "PageDown",
        [0xD2] = "Insert",
        [0xD3] = "Delete",
    };

    [Fact]
    public void EveryScanCodeIsNamedAsUnitysKeyCodeAndTheRestAreNone()
    {
        Assert.Equal(99, UnityTable.Count);
        for (int scan = -1; scan <= 256; scan++)
        {
            Assert.Equal(UnityTable.TryGetValue(scan, out string? name) ? name : "None", ClassicKeyNames.Name(new KeyScanCode(scan)));
        }
    }
}
