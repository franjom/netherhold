// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>The Jack In The Box — docs/research/jack-in-the-box.md: the trap's shot (process 39,
/// <c>0x004B02E0</c>) strikes the victim its second data word names and, for an enemy's, makes a
/// Jack (creature 46) in state 273 (<c>0x0046E820</c>), which walks to the victim's keeper's heart
/// losing a point of health a tick, fifty a tick once there, and explodes (shot 65) when it dies.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/jack-in-the-box.md")]
public static class JackInTheBoxRules
{
    private const string SystemName = "jack_in_the_box";

    /// <summary>The creature process 39 makes (<c>0x2E</c>), its state, and the shot its death makes (<c>0x41</c>).</summary>
    public const byte JackCreatureId = 0x2E;
    public const int SpringState = 273;
    public const byte ExplosionShotId = 0x41;

    /// <summary>What state 273 costs the Jack a tick, and a tick at the heart (<c>0x0046E831</c>, <c>0x0046E8A8</c>).</summary>
    private const int WearPerTick = 1;
    private const int WearAtTheHeart = 50;

    /// <summary>Process 39 on the shot's first tick: on a passable spot, the creature its second data
    /// word names, when its owner is an enemy of the shot's, takes the first through the health routine;
    /// and only then is a Jack of the shot's owner made at the shot, the trap on the tile destroyed, the
    /// Jack set in 273 with its victim's keeper to walk to. Its threat share at radius 2 counts nobody.</summary>
    public static void Spring(SimulationContext context, ShotState shot)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (!ShotRules.Passable(world, context.Navigation, shot.PosX, shot.PosY)) return;
        // An untargeted shot keeps the definition's word, which DK2 reads as thing slot 2: no creature here.
        if (shot.TargetKind != FightTargetKind.Creature || shot.Data2 == 0) return;
        if (world.CreatureById(shot.Data2) is not { } victim || !world.HasPlayer(victim.OwnerPlayerId)) return;
        if (!CombatRules.IsHostile(world, shot.OwnerPlayerId, victim.OwnerPlayerId)) return;

        CreatureDef victimDef = database.CreatureById(victim.CreatureTypeId);
        int landed = StatusEffectRules.Hurt(world, database, victimDef, victim, shot.Data1, shot.OwnerPlayerId); // 0x004B039A: the shot's owner
        byte heartOf = victim.OwnerPlayerId;

        int tx = shot.PosX >> 12, ty = shot.PosY >> 12;
        if (!database.Creatures.ContainsKey(JackCreatureId)) return;
        CreatureState jack = world.SpawnCreature(database, JackCreatureId, shot.OwnerPlayerId, new TilePos(tx, ty));
        jack.PosX = shot.PosX;
        jack.PosY = shot.PosY;
        foreach (TrapState trap in world.Traps)
        {
            if (trap.X != tx || trap.Y != ty) continue;
            trap.Health = 0;
            break;
        }
        // 0x0048DFA0(0) and 0x0048DF10(0) clear +0x46F bits 3 and 0, which ours does not hold.
        CreatureStateMachine.TrySetState(context.Hooks, jack, SpringState, context.Tick.Value);
        jack.HeartTargetPlayerId = heartOf;
        context.Emit(SystemName, "jack_sprung",
            $"shot={shot.Id} victim={victim.Id} landed={landed} jack={jack.Id} player={shot.OwnerPlayerId} heart_of={heartOf} at={tx},{ty}");
    }

    /// <summary>State 273 (<c>0x0046E820</c>): a point of health; then, on the tile one east and one south
    /// of its target keeper's heart position (<c>0x004C5F50</c>), fifty more; elsewhere, a walk to that
    /// tile's centre resuming 273, with no door rule and no tunnel (<c>0x00471890(.., 273, 0)</c>).</summary>
    public static void Handle(SimulationContext context, CreatureState jack)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureDef definition = database.CreatureById(jack.CreatureTypeId);
        // State 273's wear passes the Jack's own owner (0x0046E833, 0x0046E8AA).
        StatusEffectRules.Hurt(world, database, definition, jack, WearPerTick, jack.OwnerPlayerId);
        (ushort hx, ushort hy) = ManaRemovalLifecycle.HeartTile(world, database, jack.HeartTargetPlayerId);
        TilePos at = jack.Tile;
        if (at.X == hx + 1 && at.Y == hy + 1)
        {
            StatusEffectRules.Hurt(world, database, definition, jack, WearAtTheHeart, jack.OwnerPlayerId);
            return;
        }
        int x = (hx << 12) + 0x1800, y = (hy << 12) + 0x1800;
        TunnelRules.RouteOrTunnel(context, SystemName, jack, x, y, StateShadow.TravelToPoint, SpringState, MoveOptions.Plain, hx + 1, hy + 1);
    }

    /// <summary>The travel state's own toll on a Jack (<c>0x00470C80</c>): a point of health a tick, on any walk.</summary>
    public static void Travelling(SimulationContext context, CreatureState jack)
    {
        CreatureDef definition = context.Database.CreatureById(jack.CreatureTypeId);
        StatusEffectRules.Hurt(context.World, context.Database, definition, jack, WearPerTick, jack.OwnerPlayerId);
    }

    /// <summary>The health routine's death arm for type <c>0x2E</c> (<c>0x004B3108</c>): shot 65 at the Jack,
    /// of its owner, with the definition's data words. Once: ours removes the dead Jack later than DK2.</summary>
    public static void Explode(SimulationContext context, CreatureState jack)
    {
        if (jack.DeathShotFired || !context.Database.Shots.TryGetValue(ExplosionShotId, out ShotDef? definition)) return;
        jack.DeathShotFired = true;
        ShotState shot = ShotRules.Spawn(context.World, definition, jack.OwnerPlayerId, jack.PosX, jack.PosY,
            (int)definition.Unknown_0098, (int)definition.Unknown_009C, 0, FightTargetKind.None, CombatRules.NoTarget, 0, -1, jack.PosX, jack.PosY);
        context.Emit(SystemName, "jack_exploded", $"jack={jack.Id} shot={shot.Id} at={jack.PosX >> 12},{jack.PosY >> 12}");
    }

    public static bool IsJack(CreatureState creature) => creature.CreatureTypeId == JackCreatureId;
}
