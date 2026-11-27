// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public sealed class PointerGesturesTests
{
    private sealed class Holder : IPointerGestureHolder
    {
        public int Cancels { get; private set; }
        public void CancelPointerGesture()
        {
            Cancels++;
        }
    }

    [Fact]
    public void FocusLossEndsThePanelPressTheWorldDragAndTheCursorFeedback()
    {
        var input = new PlayerInputTranslator();
        var tile = new TilePos(1, 2);
        input.Translate(new PlayerIntent(PlayerIds.FirstKeeper, PlayerToolMode.Hand, tile, PointerButtonTransition.Pressed),
            new PlayerInputSnapshot(false, excavation: new ExcavationTarget(tile, false)));
        Assert.NotNull(input.DragFrom(PlayerToolMode.Hand));
        var feedback = new HandCursorFeedback();
        feedback.Play(HandCursorKind.Slap, 0);
        Assert.Equal(HandCursorKind.Slap, feedback.Resolve(HandCursorKind.Default, false, false, false, 0));
        var panel = new Holder();

        PointerGestures.CancelAll(panel, input, feedback);

        Assert.Equal(1, panel.Cancels);
        Assert.Null(input.DragFrom(PlayerToolMode.Hand));
        Assert.Equal(HandCursorKind.Default, feedback.Resolve(HandCursorKind.Default, false, false, false, 0));
    }

    [Fact]
    public void AnAdapterWithoutThePanelOrTheCursorPassesNull()
    {
        PointerGestures.CancelAll(null, new PlayerInputTranslator(), null);
    }
}
