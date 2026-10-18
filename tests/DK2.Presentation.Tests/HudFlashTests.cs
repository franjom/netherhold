// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public sealed class HudFlashTests
{
    [Fact]
    public void ScriptedResourceFlashUsesTheWallClockAndStrictExpiryEvenWithoutSimulationTicks()
    {
        var director = new ScriptedViewDirector(PlayerIds.FirstKeeper, _ => null, _ => null);
        director.Emit(Flash(HudFlashTarget.Mana, durationSeconds: 2));
        director.Update(1000, 0);
        Assert.Equal(KeeperResourceFlash.Mana, director.ResourceFlash);
        director.Update(3000, 0);
        Assert.Equal(KeeperResourceFlash.Mana, director.ResourceFlash);
        director.Update(3001, 0);
        Assert.Equal(KeeperResourceFlash.None, director.ResourceFlash);
    }

    [Fact]
    public void ZeroDurationPersistsUntilReplacedOrExplicitlyCleared()
    {
        var director = new ScriptedViewDirector(PlayerIds.FirstKeeper, _ => null, _ => null);
        director.Emit(Flash(HudFlashTarget.Mana, 0));
        director.Update(1, 0);
        director.Update(999999, 0);
        Assert.Equal(KeeperResourceFlash.Mana, director.ResourceFlash);
        director.Emit(Flash(HudFlashTarget.Gold, 0));
        director.Update(1000000, 0);
        Assert.Equal(KeeperResourceFlash.Gold, director.ResourceFlash);
        director.Emit(Flash(HudFlashTarget.Mana, 0, enabled: false));
        director.Update(1000001, 0);
        Assert.Equal(KeeperResourceFlash.None, director.ResourceFlash);
    }

    private static SimEvent Flash(HudFlashTarget target, uint durationSeconds, bool enabled = true)
        => new(default, "script", "flash", "")
        {
            ScriptedView = new ScriptedViewRequest(ScriptedViewKind.FlashButton, PlayerIds.Unowned,
                0, default, false, FlashButton: new HudFlashRequest(target, durationSeconds, enabled)),
        };
}
