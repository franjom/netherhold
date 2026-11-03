// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>The flashing tab's and slot's chime, sound <c>0x33A</c> (docs/research/sound-effects-interface.md section 2). Both draw functions
/// (<c>0x00412260</c>, <c>0x00412FC0</c>) test and then write one shared latch, interface <c>+0x4F0D</c>, with the half-second phase:
/// a flashing widget drawn in the bright phase while the latch holds the dark one, and the game not suspended (world <c>+0x284</c> zero), asks the sound; every flashing draw then stores its phase.
/// So the chime comes once at the start of each bright half-second, in the category of whichever flashing widget draws first.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects-interface.md#2-the-flash-chime-0x33a")]
public sealed class FlashChime
{
    public const uint EventId = 0x33A;
    private bool _dark;

    /// <summary>One flashing widget's draw. Returns whether it asks the chime.</summary>
    public bool Draw(bool dark, bool paused)
    {
        bool chime = !dark && _dark && !paused;
        _dark = dark;
        return chime;
    }

    /// <summary>The request, nonpositional and without a handle, in the table category of the widget's sound slot: <c>0x006AC8C4</c> plus the slot times 0x30 (tabs 1 to 4, slots 5 and 6).
    /// A slot with no category asks nothing.</summary>
    public static SoundRequest Request(GuiSoundSlot slot) => new(GuiSoundSlots.CategoryOf(slot), new SoundId(EventId));
}
