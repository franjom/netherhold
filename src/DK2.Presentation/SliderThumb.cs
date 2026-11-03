// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>Where a horizontal slider's thumb sits. DK2's slider update (<c>0x00529510</c>) sets the grab state 3, which starts the test sound,
/// only for a press inside the thumb rectangle (widget <c>+0x90..+0x9C</c>); the arrows and the rest of the track are other states and silent
/// (docs/research/sound-effects-interface.md section 4).</summary>
public static class SliderThumb
{
    /// <summary>The thumb for <paramref name="value"/> in a track <paramref name="box"/>: <paramref name="thumbWidth"/> wide, at the value's share of the
    /// travel, centred vertically. The front end draws it so.</summary>
    public static FrontEndRect Rect(FrontEndRect box, float thumbWidth, float thumbHeight, int value, int minimum, int maximum)
    {
        float share = maximum > minimum ? (value - minimum) / (float)(maximum - minimum) : 0f;
        return new FrontEndRect(box.X + (box.Width - thumbWidth) * share, box.Y + (box.Height - thumbHeight) / 2, thumbWidth, thumbHeight);
    }
}
