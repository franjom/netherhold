// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Presentation;

/// <summary>While a speech line plays, music and effects run at two thirds of their settings (<c>0x00568490</c>, docs/research/sound-effects-interface.md section 7).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects-interface.md#7-the-speech-line-and-its-duck")]
public static class SpeechDuck
{
    /// <summary>The setting's whole percent times four over six, unsigned (the multiply by <c>0xAAAAAAAB</c> and shift by two at <c>0x005684AA</c>).</summary>
    public static int Percent(int setting) => (int)((uint)setting * 4u / 6u);
}
