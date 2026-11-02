// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The Dungeon Heart's sounds (docs/research/sound-effects.md section 8). All are in the category of
/// the heart room's terrain and at the heart's centre tile. DK2 plays <c>0x329</c> and <c>0xDA</c> at the heart
/// object's position, which ours does not keep: it takes the tile centre at the floor height the room
/// has (a deviation). The room's one handle (<c>+0x14</c>) is the voice of the blow and the destruction.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/sound-effects.md#8-the-dungeon-heart")]
public static class HeartSoundRules
{
    /// <summary>A blow on a heart, <see cref="WorldState.DamageHeart"/> with the sounds of
    /// <c>Player_ModifyHeartHealth</c> (<c>0x004DA200</c>): <c>0x329</c> for any loss while the heart has
    /// health, before the invulnerability test (ours has none), and <c>0xDA</c> when it dies
    /// (<c>Heart_OnDestroyed</c>, <c>0x004DA5B2</c>). A heart with no object left, health 0, sounds nothing.</summary>
    public static bool Damage(SimulationContext context, string system, PlayerId owner, int amount, PlayerId attacker)
    {
        WorldState world = context.World;
        if (world.Player(owner).HeartHealth <= 0) return false;
        if (amount > 0) Request(context, system, owner, ThingSoundEvent.HeartDamaged, voiced: true, height: null);
        bool killed = world.DamageHeart(owner, amount, attacker);
        if (killed) Request(context, system, owner, ThingSoundEvent.HeartDestroyed, voiced: true, height: null);
        return killed;
    }

    /// <summary>The local player's heart opening (<c>0x004D9E00</c>): <c>0xF1</c>, handle 0, at height
    /// 0x1000, played only when <paramref name="owner"/> is the viewer.</summary>
    internal static void RequestOpened(SimulationContext context, PlayerId owner) =>
        Request(context, "HeartIntro", owner, ThingSoundEvent.HeartOpened, voiced: false, height: HeartIntroRules.RequestHeight,
            viewerOnlyFor: owner);

    /// <summary>The fallen keeper's heart tile on the turn the consequences run (<c>0x004DAC38</c>): <c>0xF4</c>,
    /// the player's own handle (<c>+0x10A2</c>, so a <see cref="SoundSourceKind.Player"/> voice, not the room's
    /// that the blow and destruction share), height 0.</summary>
    internal static void RequestCollapse(SimulationContext context, string system, PlayerId owner) =>
        Request(context, system, owner, ThingSoundEvent.HeartCollapsed, voiced: true, height: 0, player: true);

    private static void Request(SimulationContext context, string system, PlayerId owner, ThingSoundEvent sound,
        bool voiced, int? height, PlayerId? viewerOnlyFor = null, bool player = false)
    {
        GameDatabase database = context.Database;
        if (ManaRules.DungeonHeartTerrainId(database) is not byte terrain) return;
        // A heart with no tile has no position (the collapse is guarded the same way): no sound.
        if (Combat.HeartSites.PositionOf(context.World, database, owner) < 0) return;
        (ushort x, ushort y) = ManaRemovalLifecycle.HeartTile(context.World, database, owner);
        int index = (y * context.World.Width) + x;
        ThingSoundRules.RequestTile(context, system, terrain, index, sound,
            height ?? EggLaunchRules.FloorHeight(context.World, database, new Fp32(CreatureState.CentreOf(x)),
                new Fp32(CreatureState.CentreOf(y))),
            SoundSourceKind.Room, voiced, viewerOnlyFor,
            player ? new SoundSourceId(SoundSourceKind.Player, owner.Value) : null);
    }
}
