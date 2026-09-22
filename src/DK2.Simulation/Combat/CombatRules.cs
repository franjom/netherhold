// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Combat;

/// <summary>The engagement record and the rules around it: who may look for a fight, who can be
/// attacked, how an engagement is registered on its target and how it is let go. Each is one of
/// DK2's routines, named by address in docs/research/combat-acquisition.md: the gate
/// <c>0x0045A6D0</c> (§2), the attackability recompute <c>0x0045A8B0</c> (§4), the registration
/// <c>0x0045A9A0</c> and the fight-starter <c>0x0045A550</c> (§8). Where a DK2 test reads a state
/// or timer this simulation does not have, the mapping is said at the test.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-acquisition.md")]
public static class CombatRules
{
    public const int NoTarget = 0;

    /// <summary>Eight melee and eight ranged slots on every creature.</summary>
    public const int Slots = 8;

    /// <summary>DK2 tests the creature <i>type</i> id 1 by literal wherever the Imp is special.</summary>
    public const byte ImpCreatureTypeId = 1;

    /// <summary>A creature standing where it stood last tick looks for a fight on the tenth
    /// tick since it last looked; an Imp on the fourth (<c>ProcessFight</c>, §3).</summary>
    public const int StandingScanPeriod = 10;
    public const int ImpStandingScanPeriod = 4;

    /// <summary>The low nibble of <c>creature+0x529</c>, which <c>spFighting</c> switches on to
    /// start action 16 (spell) or 17 (melee) — combat-entry.md §3.</summary>
    public static class AttackTypes
    {
        public const byte SpellAtCreature = 0;
        public const byte MeleeAtCreature = 1;
        public const byte SpellAtThing = 2;
        public const byte SpellAtAlly = 3;
        public const byte MeleeAtThing = 4;
    }

    public static bool IsFighting(CreatureState creature) => creature.Activity == CreatureActivity.Fighting;

    /// <summary>The engagement's registration on its target: the first free slot of the melee or
    /// the ranged array. False when all eight are taken, and then the attacker does not engage.</summary>
    public static bool Register(CreatureState target, int attackerId, bool ranged)
    {
        int[] slots = ranged ? target.EngagedRanged : target.EngagedMelee;
        for (int i = 0; i < Slots; i++)
        {
            if (slots[i] != NoTarget) continue;
            slots[i] = attackerId;
            return true;
        }
        return false;
    }

    /// <summary>Strikes <paramref name="attackerId"/> out of both of the target's arrays — the walk
    /// every disengagement makes over the other side's lists.</summary>
    public static void Unregister(CreatureState target, int attackerId)
    {
        for (int i = 0; i < Slots; i++)
        {
            if (target.EngagedMelee[i] == attackerId) target.EngagedMelee[i] = NoTarget;
            if (target.EngagedRanged[i] == attackerId) target.EngagedRanged[i] = NoTarget;
        }
    }

    /// <summary>Eight melee and eight ranged attackers already on it: nobody else may.</summary>
    public static bool AllSlotsTaken(CreatureState creature)
    {
        for (int i = 0; i < Slots; i++)
            if (creature.EngagedMelee[i] == NoTarget || creature.EngagedRanged[i] == NoTarget) return false;
        return true;
    }

    /// <summary>DK2's <c>IsAttackable</c>, recomputed every tick by <c>0x0045A8B0</c> rather than
    /// stored, so it is a predicate here. Cleared for a dying creature, a captive, one leaving, one
    /// in a state whose table flag says so — the lair walk and sleep among them — and one with all
    /// sixteen slots taken. Not modelled: the combat-pit fighter, torture, invisibility.</summary>
    public static bool IsAttackable(CreatureState creature) =>
        !CreatureDeathRules.IsDown(creature)
        && !PrisonRules.IsImprisoned(creature)
        && creature.Activity != CreatureActivity.Leaving
        && !HandRules.IsHeld(creature)
        && creature.Activity is not (CreatureActivity.SeekingLair or CreatureActivity.GoingToSleep or CreatureActivity.Sleeping)
        && !AllSlotsTaken(creature);

    /// <summary>The gate <c>0x0045A6D0</c>: may this creature look for a fight? Refused for a captive,
    /// one asleep, one leaving unless forced, one in the hand unless forced, the possessed
    /// creature and a dying one. Not modelled: frozen and chickened timers, hypnosis, the flee and
    /// threat-response states, the Call to Arms radius (Q361) and the Horned Reaper's states.</summary>
    public static bool MayLookForAFight(WorldState world, CreatureState creature, bool force) =>
        !PrisonRules.IsImprisoned(creature)
        && creature.Activity != CreatureActivity.Sleeping
        && (creature.Activity != CreatureActivity.Leaving || force)
        && (!HandRules.IsHeld(creature) || force)
        && !CreatureDeathRules.IsPossessed(world, creature)
        && !CreatureDeathRules.IsDown(creature);

    /// <summary>The throttle at the top of <c>ProcessFight</c>: bump the counter; a creature on a
    /// new tile looks now, one standing still looks when the counter reaches its period. Either
    /// way the counter is cleared when it looks.</summary>
    public static bool ScanIsDue(CreatureState creature, bool movedTile)
    {
        creature.FightProcessCounter = (byte)((creature.FightProcessCounter + 1) & 0xF);
        bool due = movedTile
            || creature.FightProcessCounter >= StandingScanPeriod
            || (creature.FightProcessCounter >= ImpStandingScanPeriod && creature.CreatureTypeId == ImpCreatureTypeId);
        if (due) creature.FightProcessCounter = 0;
        return due;
    }

    /// <summary>Squared distance between two positions in DK2's units: each axis squared and
    /// shifted down twelve, then summed, exactly as every scan computes it.</summary>
    public static int DistanceSquared(int ax, int ay, int bx, int by)
    {
        long dx = Math.Abs((long)ax - bx), dy = Math.Abs((long)ay - by);
        return (int)(((dx * dx) >> 12) + ((dy * dy) >> 12));
    }

    /// <summary>A sight or hearing range as the scans hold it: the loader rounds the definition's
    /// value to whole tiles, the scan squares that and shifts it up twelve.</summary>
    public static int RangeSquared(Fp32 range)
    {
        int tiles = (range.Raw + 0x800) >> 12;
        return (tiles * tiles) << 12;
    }

    /// <summary>The alignment discount: a target of the opposite alignment reads an eighth nearer.</summary>
    public static int Discounted(int distanceSquared) => (7 * distanceSquared) >> 3;

    public static bool OppositeAlignment(CreatureDef me, CreatureDef candidate) =>
        me.Flags.HasFlag(CreatureFlags.IsGood)
            ? candidate.Flags.HasFlag(CreatureFlags.IsEvil)
            : candidate.Flags.HasFlag(CreatureFlags.IsGood);

    /// <summary><c>0x004BF390</c>: another player's creature is an enemy unless allied.</summary>
    public static bool IsHostile(WorldState world, byte mySide, byte theirSide) => !world.AreAllied(mySide, theirSide);

    /// <summary>The fight-starter's melee reach: my <c>MeleeRange</c> plus half the target's
    /// footprint, squared and shifted down twelve.</summary>
    public static int MeleeReachSquared(CreatureDef me, CreatureDef target)
    {
        int reach = me.MeleeRange.Raw + (target.FootprintRadius.Raw >> 1);
        return unchecked(reach * reach) >> 12;
    }

    /// <summary>Lets go of the engagement: struck out of the target's arrays, target and reach
    /// cleared, and a creature that was fighting stands idle with no job.</summary>
    public static void Disengage(WorldState world, CreatureState creature)
    {
        if (creature.FightTargetId != NoTarget)
        {
            CreatureState? target = world.CreatureById(creature.FightTargetId);
            if (target != null) Unregister(target, creature.Id);
        }
        creature.FightTargetId = NoTarget;
        creature.AttackType = 0;
        creature.AttackRangeSquared = 0;
        if (!IsFighting(creature)) return;
        creature.CurrentJob = JobType.None;
        creature.JobTileIndex = -1;
        creature.StopMoving();
        creature.Activity = CreatureActivity.Idle;
    }
}
