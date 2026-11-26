// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Presentation;

/// <summary>An adapter's panel that holds a press across frames until its release (the bottom panel); cancelling drops the
/// press and releases the engine's pointer capture.</summary>
public interface IPointerGestureHolder
{
    void CancelPointerGesture();
}

/// <summary>Every pointer gesture in flight, ended without its release: a window that loses focus never sees the release.</summary>
public static class PointerGestures
{
    /// <summary>The panel's press, then the world drag, then the hand cursor's transient feedback. An adapter that has no
    /// panel or no cursor feedback passes null.</summary>
    public static void CancelAll(IPointerGestureHolder? panel, PlayerInputTranslator input, HandCursorFeedback? feedback)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        panel?.CancelPointerGesture();
        input.CancelPointerGesture();
        feedback?.Reset();
    }
}
