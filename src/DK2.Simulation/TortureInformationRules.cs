// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The manual's "valuable piece of information" (<c>0x004F2370</c>, ADR-0230): what a
/// keeper learns when an enemy dies on one of its torture devices. Rewards 3 to 10 each unlock a
/// row of the enemy-information panel (<c>0x00423450</c>, values from <c>0x004C2C30</c>), once per
/// pair of keepers; 11, always a candidate, reveals the victim keeper's ground.</summary>
/// <remarks>Presentation only for 3–10: nothing in the simulation reads the rows. Reward 11's
/// reveal needs fog of war, which is not built, so it is chosen and reported but does nothing.
/// Speeches <c>0x61</c>, <c>0x60</c> and <c>0x3B</c> are not built.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/torture-information.md")]
public static class TortureInformationRules
{
    /// <summary>The reveal, always the last candidate.</summary>
    public const int Reveal = 11;

    /// <summary>The candidates in DK2's order: by the victim's <c>JobClass</c> (<c>def+0x67D</c>),
    /// Artisan 3 then 7, Thinker 4 then 9, Fighter 5 then 8, a Scout none; then 10; each only while
    /// the chamber's keeper has not been given it about the victim's keeper (<c>0x004C2BC0</c>).
    /// Then 11. A hero victim gives 11 alone.</summary>
    public static List<int> Candidates(WorldState world, CreatureDef victim, PlayerId victimOwner, PlayerId chamberOwner)
    {
        var list = new List<int>(4);
        if (victimOwner != PlayerIds.Good)
        {
            (int First, int Second)? pair = victim.JobClass switch
            {
                JobClass.Artisan => (3, 7),
                JobClass.Thinker => (4, 9),
                JobClass.Fighter => (5, 8),
                _ => null,
            };
            if (pair is (int a, int b))
            {
                if (!Given(world, a, chamberOwner, victimOwner)) list.Add(a);
                if (!Given(world, b, chamberOwner, victimOwner)) list.Add(b);
            }
            if (!Given(world, 10, chamberOwner, victimOwner)) list.Add(10);
        }
        list.Add(Reveal);
        return list;
    }

    /// <summary><c>0x004C2BC0(k, victim's keeper, chamber's keeper)</c>: true for a keeper about
    /// itself, otherwise the stored flag.</summary>
    public static bool Given(WorldState world, int reward, PlayerId chamberOwner, PlayerId victimOwner)
    {
        if (chamberOwner == victimOwner) return true;
        if (reward is < 3 or > 10) return false;
        return (world.Player(chamberOwner).InformationGiven & Bit(reward, victimOwner)) != 0;
    }

    /// <summary><c>rand(n)</c> over <see cref="Candidates"/>; a reward other than 11 is marked
    /// given (<c>0x004C2B60</c>). Returns the reward.</summary>
    public static int Give(WorldState world, SimRandom rng, CreatureDef victim, PlayerId victimOwner, PlayerId chamberOwner)
    {
        List<int> candidates = Candidates(world, victim, victimOwner, chamberOwner);
        int reward = candidates[rng.NextInt(candidates.Count)];
        if (reward != Reveal) world.Player(chamberOwner).InformationGiven |= Bit(reward, victimOwner);
        return reward;
    }

    private static ulong Bit(int reward, PlayerId victimOwner) => 1UL << (((reward - 3) * 8) + (victimOwner.Value & 7));
}
