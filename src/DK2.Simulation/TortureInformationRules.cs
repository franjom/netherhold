// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What reward 11 did.</summary>
public enum RevealResult : byte
{
    None = 0,
    Room = 1,
    Creature = 2,
}

/// <summary>The manual's "valuable piece of information" (<c>0x004F2370</c>, ADR-0230): what a
/// keeper learns when an enemy dies on one of its torture devices. Rewards 3 to 10 each unlock a
/// row of the enemy-information panel (<c>0x00423450</c>, values from <c>0x004C2C30</c>), once per
/// pair of keepers; 11, always a candidate, reveals the victim keeper's ground.</summary>
/// <remarks>Presentation only for 3–10: nothing in the simulation reads the rows. Reward 11
/// explores a room of the victim's keeper, or tags one of its creatures (<see cref="RevealTo"/>).</remarks>
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

    /// <summary>Passes reward 11's fallback gives a creature (<c>+0x475</c> = 16).</summary>
    public const byte SightPasses = 16;

    /// <summary>The whole of <c>0x004F2370</c>: <see cref="Give"/>, then for 11 the reveal, then
    /// message 97 to the chamber's keeper and either 96 (rewards 3 to 10) or 59 (11). Returns the
    /// reward and what the reveal did.</summary>
    public static (int Reward, RevealResult Revealed) Award(
        WorldState world, GameDatabase database, SimRandom rng, CreatureDef victim, PlayerId victimOwner, PlayerId chamberOwner)
    {
        int reward = Give(world, rng, victim, victimOwner, chamberOwner);
        RevealResult revealed = reward == Reveal ? RevealTo(world, database, victimOwner, chamberOwner) : RevealResult.None;
        if (world.HasPlayer(chamberOwner))
        {
            world.Messages.Post(world, chamberOwner.Value, PlayerMessageKind.InformationLearned, 0, 0);
            world.Messages.Post(world, chamberOwner.Value,
                reward == Reveal ? PlayerMessageKind.InformationReveal : PlayerMessageKind.InformationUnlocked, 0, 0);
        }
        return (reward, revealed);
    }

    /// <summary>Reward 11 (<c>0x004F2370</c>). Over the victim keeper's rooms, newest first, the first
    /// whose first slab is unexplored has every tile explored. Failing that, the victim keeper's first
    /// creature, newest first, standing on an unexplored tile takes <see cref="SightPasses"/> radius-one
    /// passes for the chamber's keeper (<see cref="CreatureState.TortureSightPasses"/>).
    /// <para>Ours, stated: DK2 tests and writes its one explored layer, the local machine's view,
    /// whoever tortured; ours keeps a layer per keeper and uses the chamber keeper's, which is the
    /// same when that keeper is the local player. With fog off nothing is unexplored, so nothing
    /// happens. Command 6, the 3 x 3 redraw it posts after each tile, is presentation and not
    /// needed here: the view rebuilds from the explored layer.</para></summary>
    public static RevealResult RevealTo(WorldState world, GameDatabase database, PlayerId victimOwner, PlayerId chamberOwner)
    {
        if (!world.FogOfWarEnabled) return RevealResult.None;
        foreach (RoomInstance room in RoomLists.Of(world, database, victimOwner))
        {
            if (world.TileAtIndex(SlabChain.FirstSlab(world, room)).IsExploredBy(chamberOwner)) continue;
            foreach (int tile in SlabChain.Order(world, room))
                DiscoveryRules.SetExplored(world, database, new TilePos(tile % world.Width, tile / world.Width), chamberOwner);
            return RevealResult.Room;
        }

        foreach (CreatureState creature in world.OwnedCreatures(victimOwner))
        {
            if (world.Tile(creature.Tile).IsExploredBy(chamberOwner)) continue;
            creature.TortureSightPasses = SightPasses;
            creature.TortureSightPlayerId = chamberOwner.Value;
            creature.TortureSightTileIndex = world.TileIndex(creature.Tile);
            return RevealResult.Creature;
        }

        return RevealResult.None;
    }

    private static ulong Bit(int reward, PlayerId victimOwner) => 1UL << (((reward - 3) * 8) + (victimOwner.Value & 7));
}
