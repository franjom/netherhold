// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation;

/// <summary>Action 26's object arms (possession-arms-and-inputs.md §1): the chicken, the Training
/// Dummy, and the Arch Pillars the pick and the spell scan see.</summary>
public static partial class PossessionRules
{
    /// <summary>A Dungeon Heart's Arch Pillar (object type 94) as the pick sees it. Ours holds no pillar
    /// object: <see cref="HeartPillarRules"/> places the three a heart builds.</summary>
    internal sealed record PillarThing(byte Owner, HeartPillarId Id, HeartPillarTarget Target);

    /// <summary>The chicken arm (<c>0x005149C9</c>). A creature owing no food that is not a Bile Demon
    /// kills the bird. Otherwise, unless a Bile Demon, hunger anger grows by its own share over the
    /// level-scaled <c>HungerFill</c> (an add, where the Hatchery forgets it) and that fill is paid off;
    /// then the heal, the lesser of what is missing and <c>HpFromChicken</c> (an unsigned test); the bird
    /// goes either way. No state change and no hunger-clock restart, unlike state 93. The sounds, the
    /// death effect and action 26 on art 13 are presentation.</summary>
    internal static void EatChicken(SimulationContext context, CreatureState me, CreatureDef mine, ObjectState chicken)
    {
        GameDatabase database = context.Database;
        bool bile = me.CreatureTypeId == Systems.CreatureJobSystem.BileDemonTypeId;
        if (me.HungerNeed == 0 && !bile)
        {
            context.World.RemoveObject(chicken.Id);
            context.Emit(ActSystem, "possessed_chicken_killed", $"creature={me.Id} chicken={chicken.Id}");
            return;
        }

        if (!bile)
        {
            int fill = EatingRules.HungerFill(database, mine, me.Level);
            if (fill != 0)
            {
                CreatureAngerRules.Apply(context.World, mine, me, AngerBucket.Hunger, me.Anger[AngerBucket.Hunger] / fill, AngerComplaint.General);
                me.HungerNeed = Math.Max(0, me.HungerNeed - fill);
            }
        }

        int missing = CreatureHealthRules.MaxHealth(database, mine, me) - me.Health;
        int heal = (uint)missing <= mine.HpFromChicken ? missing : mine.HpFromChicken;
        int gained = CreatureHealthRules.Apply(database, mine, me, heal, chicken.OwnerPlayerId); // 0x00514B9E: the chicken's owner
        context.World.RemoveObject(chicken.Id);
        context.Emit(ActSystem, "possessed_chicken_eaten",
            $"creature={me.Id} chicken={chicken.Id} gained={gained} need={me.HungerNeed}");
    }

    /// <summary>The Training Dummy arm (<c>0x005149BB</c>): <c>Object_SetProcessState(18)</c>, which
    /// restarts a spin already running (<see cref="ObjectReactionRules.Step"/>). No damage.</summary>
    internal static void SpinDummy(SimulationContext context, CreatureState me, ObjectState dummy)
    {
        dummy.ProcessState = ObjectReactionRules.StruckProcess;
        dummy.ProcessAgeTicks = 0;
        context.Emit(ActSystem, "possessed_dummy", $"creature={me.Id} object={dummy.Id}");
    }

    /// <summary>The standing hearts' pillars, each list in the order the heart built them.</summary>
    internal static IEnumerable<PillarThing> Pillars(WorldState world, HeartSites? hearts)
    {
        if (hearts is null) yield break;
        foreach (HeartSites.Site site in hearts.Sites)
        {
            if (!site.Standing) continue;
            foreach (HeartPillarTarget pillar in site.Pillars)
                yield return new PillarThing(site.Owner, HeartPillarId.For(world, pillar), pillar);
        }
    }
}
