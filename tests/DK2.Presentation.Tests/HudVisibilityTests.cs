// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation.Tests;

public sealed class HudVisibilityTests
{
    [Fact]
    public void BlockingOrdinaryInputCancelsADragBeforeAReleaseAfterRestoration()
    {
        var translator = new PlayerInputTranslator();
        var tile = new TilePos(1, 2);
        var snapshot = new PlayerInputSnapshot(false, excavation: new ExcavationTarget(tile, false));
        var press = new PlayerIntent(PlayerIds.FirstKeeper, PlayerToolMode.Hand, tile, PointerButtonTransition.Pressed);
        Assert.Empty(translator.Translate(press, snapshot).All);
        Assert.NotNull(translator.DragFrom(PlayerToolMode.Hand));
        translator.CancelPointerGesture();
        Assert.Null(translator.DragFrom(PlayerToolMode.Hand));
        Assert.Empty(translator.Translate(press with { PrimaryButton = PointerButtonTransition.Released }, snapshot).All);
    }

    [Theory]
    [InlineData(3, 0, true, true)]
    [InlineData(3, 1, false, false)]
    [InlineData(3, -1, false, false)]
    [InlineData(4, 2, false, false)]
    [InlineData(5, 0, true, true)]
    [InlineData(18, 0, false, false)]
    [InlineData(18, 1, false, false)]
    [InlineData(2, 1, true, true)]
    [InlineData(10, 1, true, true)]
    [InlineData(11, 1, true, false)]
    public void CameraGateUsesNativeModeExceptionsAndTheRequestCount(
        int mode, int count, bool visible, bool inputAllowed)
    {
        HudVisibility state = HudVisibilityRules.ForCamera(new((ushort)mode), count);
        Assert.Equal(visible, state.ShowNormalHud);
        Assert.Equal(inputAllowed, state.AllowOrdinaryInput);
    }

    [Fact]
    public void CameraInputLockDoesNotHideAnOtherwiseVisibleHud()
    {
        HudVisibility state = HudVisibilityRules.ForCamera(new(3), 0, cameraInputBlocked: true);
        Assert.True(state.ShowNormalHud);
        Assert.False(state.AllowOrdinaryInput);
    }
}
