// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public sealed class CreateGoldOnRealDataTests
{
    [InstallFact]
    public void ShippedCreateGoldMatchesTheIsolatedSimulationFixture()
    {
        var db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var spell = db.KeeperSpellById(13)!;
        Assert.Equal("Create Gold", spell.Name);
        Assert.Equal((15000, 0, 0), (spell.ManaCost, spell.ManaUpkeepPerSecond, spell.CooldownRaw));
        Assert.Equal(((byte)1, (byte)4), (spell.LocationSelector, spell.TargetSelector));
        Assert.Equal(((byte)5, 1, 1000), spell.Shot(false));
        Assert.Equal(((byte)5, 1, 2000), spell.Shot(true));
        Assert.Equal((byte)2, db.Shots[spell.ShotTypeId].ProcessType);
    }
}
