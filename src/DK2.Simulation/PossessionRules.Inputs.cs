// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Steering;

namespace DK2.Simulation;

/// <summary>What the combat helpers the possessed actions call need beside the sight.</summary>
public sealed class PossessionCombat
{
    internal readonly HeartSites Hearts = new();
    internal readonly RoomCache Rooms = new();
    internal readonly List<Fixed16Vector> Scratch = [];
}

/// <summary>The possession commands that set the creature's inputs (possession-arms-and-inputs.md
/// §5): sneak (0x1D), hypnosis (0x1B) and sniper mode (39), and what reads them.</summary>
public static partial class PossessionRules
{
    // ------------------------------------------------------------------ sneak

    /// <summary>ToggleSneak (<c>0x0048EE90</c>): a disguise as the owner of the tile named, ended by a
    /// second press, a tile of its own or the neutral player's, or a watcher allied to that owner
    /// within its sight and in sight. Creeping (<c>+0x3E6</c> bit 12), which scales the distance, and
    /// <c>0x00455990</c>, called first, are not represented. DK2 reads any tile unchecked; a tile off
    /// this map changes nothing.</summary>
    public static void ToggleSneak(SimulationContext context, ISightBlocker sight, CreatureState me, int tileX, int tileY)
    {
        WorldState world = context.World;
        if (!world.Contains(tileX, tileY)) return;
        byte owner = world.Tile(tileX, tileY).OwnerPlayerId;
        if (me.SneakingAs != 0 || owner == me.OwnerPlayerId || owner == PlayerIds.Neutral)
        {
            me.SneakingAs = 0;
            return;
        }
        for (int i = 0; i < world.PlayerCount; i++)
            foreach (CreatureState c in world.OwnedCreatures(world.PlayerAtIndex(i).PlayerId))
            {
                if (!world.AreAllied(TurncoatRules.CombatSide(c), owner)) continue;
                int range = CombatRules.RangeSquared(context.Database.CreatureById(c.CreatureTypeId).DistanceCanSee);
                if (unchecked((int)TrapRules.DistanceSquared(me.PosX, me.PosY, c.PosX, c.PosY)) >= range) continue;
                if (!LineOfSight.Clear(sight, me.PosX, me.PosY, c.PosX, c.PosY)) continue;
                me.SneakingAs = 0;
                return;
            }
        me.SneakingAs = owner;
    }

    /// <summary>The combat scans' skip (<c>0x0045B350</c> in the fight-style arms, and in scan C, the
    /// NonFighter and the nearest-attacker scans): a candidate disguised as the searcher's own side
    /// is not seen as an enemy.</summary>
    public static bool IsDisguisedFrom(CreatureState searcher, CreatureState candidate) =>
        candidate.SneakingAs != 0 && candidate.SneakingAs == TurncoatRules.CombatSide(searcher).Value;

    /// <summary><c>0x0051496E</c>/<c>0x00515156</c>: striking or casting at a thing whose owner is
    /// allied to the disguise ends it.</summary>
    private static void UnmaskAgainst(WorldState world, CreatureState me, byte targetOwner)
    {
        if (me.SneakingAs != 0 && world.AreAllied(new PlayerId((byte)me.SneakingAs), targetOwner)) me.SneakingAs = 0;
    }

    // ------------------------------------------------------------------ hypnosis

    /// <summary>Command 0x1B (<c>0x005163C0</c>). Held: the second press releases. Otherwise the
    /// nearest enemy of the sender, by the pose, that can be hypnotised and is in the cone and in sight
    /// from the pose, with no range, dying or possessed test, the world's newest first; one immune to
    /// Turncoat takes nothing. The take keeps its owner, gives it the possessor's, sets it following
    /// (which may refuse), marks it hypnotised and zeroes its attack count. The world list's order,
    /// which decides an exact tie, is taken as newest first, as the creature update walks it; how the
    /// creature manager links a new one is UNREAD.</summary>
    public static void Hypnotise(SimulationContext context, ISightBlocker sight, PlayerId keeper, CreatureState me, PossessionAim aim)
    {
        if (me.HypnotisedCreatureId != 0)
        {
            ReleaseHypnotised(context, me);
            return;
        }
        WorldState world = context.World;
        GameDatabase database = context.Database;
        (int px, int py, int pz) = aim.PacketOrigin;
        var pose = new Vec3(px, py, pz);
        CreatureState? best = null;
        uint nearest = uint.MaxValue;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = creatures.Count - 1; i >= 0; i--)
        {
            CreatureState c = creatures[i];
            if (!CreatureStateMachine.HasState(c) || !CombatRules.IsHostile(world, keeper.Value, c.OwnerPlayerId)) continue;
            if (!database.CreatureById(c.CreatureTypeId).Flags.HasFlag(CreatureFlags.CanBeHypnotized)) continue;
            if (!InAimCone(aim.Yaw.Value, pose, new Vec3(c.PosX, c.PosY, c.PosZ))) continue;
            if (!LineOfSight.Clear(sight, px, py, c.PosX, c.PosY)) continue;
            uint d = TrapRules.DistanceSquared(px, py, c.PosX, c.PosY);
            if (d >= nearest) continue;
            nearest = d;
            best = c;
        }
        if (best is null || database.CreatureById(best.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsImmuneToTurncoat)) return;

        best.HypnosisOwnerPlayerId = best.OwnerPlayerId;
        world.ChangeCreatureOwner(database, best, new PlayerId(me.OwnerPlayerId));
        StartFollowing(context, best, me.Id);
        best.Hypnotised = true;
        best.AttackCreatureHits = 0;
        me.HypnotisedCreatureId = best.Id;
        context.Emit(ActSystem, "possessed_hypnotise", $"creature={me.Id} target={best.Id} owner_was={best.HypnosisOwnerPlayerId}");
    }

    /// <summary>The release (the second press, and action 21 before it leaves possession,
    /// <c>0x005145D9</c>): the engagement unlinked, the count zeroed, the old owner back, the mark gone.</summary>
    internal static void ReleaseHypnotised(SimulationContext context, CreatureState me)
    {
        if (context.World.CreatureById(me.HypnotisedCreatureId) is { } held)
        {
            CombatRules.Unlink(context.World, held);
            held.AttackCreatureHits = 0;
            context.World.ChangeCreatureOwner(context.Database, held, new PlayerId(held.HypnosisOwnerPlayerId));
            held.HypnosisOwnerPlayerId = 0;
            held.Hypnotised = false;
        }
        me.HypnotisedCreatureId = 0;
    }

    /// <summary><c>0x00514DAD</c>: a possessed blow begun at a creature sends the hypnotised one at it
    /// (forced target, then <c>SelectAVictim</c> at once) and counts one commanded attack.</summary>
    private static void Command(SimulationContext context, ISightBlocker sight, PossessionCombat combat, CreatureState me, CreatureState target)
    {
        if (me.HypnotisedCreatureId == 0 || context.World.CreatureById(me.HypnotisedCreatureId) is not { } held) return;
        CombatPitRules.ClearForcedTarget(context.World, held);
        ForceTarget(context.World, held, target.Id);
        CombatTargetRules.SelectAVictim(context, sight, combat.Hearts, combat.Rooms, held,
            context.Database.CreatureById(held.CreatureTypeId), combat.Scratch);
        held.HypnotisedAttacks = unchecked((ushort)(held.HypnotisedAttacks + 1));
    }

    /// <summary><c>0x0048F390</c>: an old forced target is dropped with its engagement, then the
    /// abnormal-target bit and the new one.</summary>
    private static void ForceTarget(WorldState world, CreatureState c, int targetId)
    {
        if (c.AttackingId != CombatRules.NoTarget)
        {
            CombatRules.Unlink(world, c);
            c.AttackingId = CombatRules.NoTarget;
        }
        c.AttackAbnormalTargets = true;
        c.AttackingId = targetId;
        c.AttackingKind = FightTargetKind.Creature;
    }

    /// <summary>Creature_Update (<c>0x00488C37</c>): a hypnotised creature that has launched as many
    /// attacks as it was commanded drops the forced target and both counts. It stays hypnotised.
    /// DK2 runs it inside the creature's own update; ours once a tick in the possession system.</summary>
    internal static void EndCommandedAttack(WorldState world, CreatureState c)
    {
        if (!c.Hypnotised || c.HypnotisedAttacks == 0 || c.HypnotisedAttacks > c.AttackCreatureHits) return;
        CombatPitRules.ClearForcedTarget(world, c);
        c.HypnotisedAttacks = 0;
        c.AttackCreatureHits = 0;
    }

    /// <summary>The death bookkeeping for a freed creature (<c>0x00487DD2</c>, <c>0x00487E6A</c>):
    /// a hypnotised one clears its owner's possessed creature's hold, unchecked, and does not get its
    /// old owner back; a follower lowers its leader's count. DK2 runs it once per creature; ours can
    /// reach it twice (a corpse removed later), so it clears both marks as it goes.</summary>
    public static void CreatureFreed(WorldState world, CreatureState c)
    {
        if (c.Hypnotised && world.HasPlayer(c.OwnerPlayerId)
            && world.CreatureById(world.Player(c.OwnerPlayerId).PossessedCreatureId) is { } possessor)
            possessor.HypnotisedCreatureId = 0;
        c.Hypnotised = false;
        LeaderLosesFollower(world, c);
    }

    // ------------------------------------------------------------------ sniper

    /// <summary>Command 39 (<c>0x005171D0</c>): the flag takes the packet's value, for any possessed
    /// creature.</summary>
    public static void SetSniperMode(CreatureState me, bool on) => me.SniperMode = on;
}
