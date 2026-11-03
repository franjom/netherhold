// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.TestSupport;

namespace DK2.Data.Tests;

/// <summary>The surface byte the swing's impact lookup (<c>0x004998A0</c>) reads from the struck thing's definition: creature
/// <c>+0x685</c> (<c>0x00492320</c>, intermediate <c>+0x1366</c>, file <c>0xF76</c>), trap <c>+0xBF</c> (<c>0x004AA440</c>,
/// intermediate <c>+0x2D7</c>, file <c>0x22F</c>), object <c>+0x11C</c> (<c>0x0049DD60</c>, intermediate <c>+0x3C0</c>, file
/// <c>0x340</c>) and the door's <c>+0xB3</c> (file <c>0x226</c>, read earlier). Docs: sound-effects-combat.md section 5.</summary>
public sealed class ImpactKindTests
{
    private static string Editor() => Path.Combine(Dk2InstallGate.Path, "Data", "editor");

    [InstallFact]
    public void TheShippedCreatureTrapAndObjectSurfaceBytesAreTheOnesAtTheLoadersOffsets()
    {
        var creatures = KwdCreatures.Parse(KwdReader.ReadFile(Path.Combine(Editor(), "Creatures.kwd")))
            .ToDictionary(c => c.Name.Trim(), c => (int)c.ImpactKind);
        Assert.Equal(1, creatures["Imp"]);
        Assert.Equal(1, creatures["Goblin"]);
        Assert.Equal(4, creatures["Knight"]);
        Assert.Equal(2, creatures["Stone Knight"]);
        Assert.Equal(3, creatures["Jack"]);
        Assert.Equal(1, creatures["Horny"]);

        var traps = KwdTraps.Parse(KwdReader.ReadFile(Path.Combine(Editor(), "Traps.kwd"))).ToDictionary(t => (int)t.TrapId, t => (int)t.ImpactKind);
        Assert.Equal(4, traps[1]);   // Gas Trap
        Assert.Equal(2, traps[2]);   // Boulder Trap
        Assert.Equal(3, traps[9]);   // Trigger Trap
        Assert.Equal(0, traps[13]);  // the spare door

        var objects = KwdObjects.Parse(KwdReader.ReadFile(Path.Combine(Editor(), "Objects.kwd"))).ToDictionary(o => (int)o.ObjectId, o => (int)o.ImpactKind);
        Assert.Equal(4, objects[1]);   // Gold
        Assert.Equal(3, objects[4]);   // Spell Book
        Assert.Equal(1, objects[9]);   // Chicken
        Assert.Equal(5, objects[13]);  // Dungeon Heart
        Assert.Equal(2, objects[31]);  // Tomb Stone 1
    }
}
