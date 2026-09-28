// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

public sealed class ThunderboltOnRealDataTests
{
    [InstallFact]
    public void ShippedThunderboltDescriptorAndShotMatchTheBinaryRead()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        KeeperSpellDef spell = db.KeeperSpellById(6)!;
        Assert.Equal("Thunderbolt", spell.Name);
        Assert.Equal((0x1770, 0, 0, 1, 2),
            (spell.ManaCost, spell.ManaUpkeepPerSecond, spell.CooldownRaw, (int)spell.LocationSelector, (int)spell.TargetSelector));
        Assert.Equal(((byte)0x15, 0x96, 0), spell.Shot(false));
        Assert.Equal(((byte)0x15, 0xC8, 0), spell.Shot(true));
        Assert.Equal(TileConstruction.ThreeByThree, db.Rooms.Values.Single(r => r.TypeId == RoomTypeId.Portal).Construction);
        foreach (RoomTypeId kind in new[] { RoomTypeId.Prison, RoomTypeId.Temple, RoomTypeId.CombatPit })
            Assert.Equal(TileConstruction.DoubleQuad, db.Rooms.Values.Single(r => r.TypeId == kind).Construction);
        ShotDef shot = db.Shots[0x15];
        Assert.Equal((9, 0, 0, 6), ((int)shot.ProcessType, (int)shot.DamageType, (int)shot.CollideType, (int)shot.AttackCategory));
        Assert.Equal((0x40u, 4u, 8, 0x800, 0x333), (shot.ShotProcessFlags, shot.Flags, (int)shot.Health, shot.Speed, shot.Radius));
        Assert.Equal((0, 0, 0), ((int)shot.DeathShotId, (int)shot.HitThingShotId, (int)shot.HitSolidShotId));
        Assert.Equal(0u, (uint)db.CreatureById(14).Flags2 & 1u);
        Assert.Equal(0u, (uint)db.CreatureById(29).Flags2 & 1u);
    }
}
