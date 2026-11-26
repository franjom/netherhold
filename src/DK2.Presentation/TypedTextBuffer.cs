// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>What a pressed key types besides its character: Backspace and Enter type control characters.</summary>
public enum TypedKey : byte { Other, Backspace, Enter }

/// <summary>The characters typed in one frame, as sent by
/// <see cref="SaveLoadHud.Type"/>: Backspace as <c>'\b'</c>, Enter (either one) as <c>'\n'</c>, any other key as its
/// Unicode code point (a surrogate pair above the BMP). A host feeds it each pressed key, key repeats included, reads
/// <see cref="Text"/> and clears it once a frame. It holds a fixed number of characters; a key whose characters do not all
/// fit closes it for the rest of the frame, so nothing later is taken out of order.</summary>
public sealed class TypedTextBuffer
{
    private readonly char[] _chars;
    private int _count;
    private bool _full;

    /// <param name="capacity">Characters held per frame.</param>
    public TypedTextBuffer(int capacity = 64)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "A frame must hold at least a surrogate pair.");
        }

        _chars = new char[capacity];
    }

    /// <summary>This frame's characters.</summary>
    public ReadOnlySpan<char> Text => new(_chars, 0, _count);

    /// <summary>Whether a key overflowed it this frame; no more is taken until <see cref="Clear"/>.</summary>
    public bool Full => _full;

    /// <summary>Takes a pressed key's text. A key that types nothing (code point 0, a lone surrogate, beyond U+10FFFF) is
    /// ignored. Returns false when the key's characters do not fit, which closes the buffer for the frame.</summary>
    /// <param name="key">Backspace or Enter, whatever their code point; otherwise <see cref="TypedKey.Other"/>.</param>
    /// <param name="codePoint">The key's Unicode code point, 0 for none.</param>
    public bool Add(TypedKey key, uint codePoint)
    {
        if (_full)
        {
            return false;
        }

        if (key == TypedKey.Backspace)
        {
            return Put('\b');
        }

        if (key == TypedKey.Enter)
        {
            return Put('\n');
        }

        if (codePoint == 0 || codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
        {
            return true;
        }

        if (codePoint < 0x10000)
        {
            return Put((char)codePoint);
        }

        if (_count + 2 > _chars.Length) { _full = true; return false; }
        uint value = codePoint - 0x10000;
        _chars[_count++] = (char)(0xD800 + (value >> 10));
        _chars[_count++] = (char)(0xDC00 + (value & 0x3FF));
        return true;
    }

    /// <summary>Forgets the frame's characters and opens the buffer again.</summary>
    public void Clear() { _count = 0; _full = false; }

    private bool Put(char c)
    {
        if (_count >= _chars.Length) { _full = true; return false; }
        _chars[_count++] = c;
        return true;
    }
}
