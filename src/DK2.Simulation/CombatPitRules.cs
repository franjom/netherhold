// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;

namespace DK2.Simulation;

/// <summary>The Combat Pit: its floor, the pairing of two fighters as each other's forced target,
/// the waiting fighter's step, and what clears a fighter (<c>combat-pit.md</c>, ADR-0213).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/combat-pit.md")]
public static class CombatPitRules
{
    /// <summary>137, the arrival: every fighter already in the pit is placed again.</summary>
    public const int ArrivingState = 137;

    /// <summary>138, placing: on the floor a fighter, anywhere else in the pit not.</summary>
    public const int PlacingState = 138;

    /// <summary>139, waiting for an opponent.</summary>
    public const int WaitingState = 139;

    /// <summary>141, thrown out by the room's reset.</summary>
    public const int ThrownOutState = 141;

    /// <summary>The four-way table at <c>0x006BC350</c>: north, east, south, west.</summary>
    private static readonly (int Dx, int Dy)[] Directions = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>The eight-way table at <c>0x006BC370</c>: north, north-east, east, south-east,
    /// south, south-west, west, north-west.</summary>
    private static readonly (int Dx, int Dy)[] EightDirections =
        [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    /// <summary>The Combat Pit on this tile, any owner's (<c>0x00453A20(16)</c>).</summary>
    public static RoomInstance? PitAt(WorldState world, GameDatabase database, int tileIndex)
    {
        if (tileIndex < 0 || tileIndex >= world.TileCount) return null;
        if (database.RoomByTerrain(world.TileAtIndex(tileIndex).TerrainId)?.TypeId != RoomTypeId.CombatPit) return null;
        return RoomIndex.RoomAt(world, database, tileIndex);
    }

    /// <summary>The pit's floor: floor height <c>0x400</c>, which <c>0x004545C0</c> gives an
    /// interior tile of a Combat Pit and nothing else (§5).</summary>
    public static bool IsFloor(RoomInstance? pit, int tileIndex) => pit != null && pit.IsInterior(tileIndex);

    /// <summary>The distinct Combat Pits standing on any of <paramref name="tiles"/>.</summary>
    public static List<RoomInstance> PitsUnder(WorldState world, GameDatabase database, IEnumerable<TilePos> tiles)
    {
        var pits = new List<RoomInstance>();
        foreach (TilePos pos in tiles)
        {
            if (!world.Contains(pos)) continue;
            int index = (pos.Y * world.Width) + pos.X;
            bool known = false;
            for (int i = 0; i < pits.Count && !known; i++) known = pits[i].Contains(index);
            if (!known && PitAt(world, database, index) is { } pit) pits.Add(pit);
        }

        return pits;
    }

    /// <summary>Whether this is a state the pit's step runs.</summary>
    public static bool IsPitState(int stateId) =>
        stateId is ArrivingState or PlacingState or WaitingState or ThrownOutState;

    /// <summary><c>0x0048F390</c>, both ways: each drops its old forced target and the engagement it
    /// drove, and takes the other, and both are fighters.</summary>
    public static void Pair(WorldState world, CreatureState seeker, CreatureState opponent)
    {
        Force(world, seeker, opponent.Id);
        Force(world, opponent, seeker.Id);
    }

    private static void Force(WorldState world, CreatureState creature, int targetId)
    {
        if (creature.AttackingId != CombatRules.NoTarget) ClearForcedTarget(world, creature);
        creature.AttackAbnormalTargets = true;
        creature.AttackingId = targetId;
        creature.CombatPitFighter = true;
    }

    /// <summary><c>0x0048F3F0</c>: the forced target goes, and with it the engagement
    /// (<c>0x0045EF50</c>).</summary>
    public static void ClearForcedTarget(WorldState world, CreatureState creature)
    {
        creature.AttackingId = CombatRules.NoTarget;
        creature.AttackAbnormalTargets = false;
        CombatRules.Unlink(world, creature);
    }

    /// <summary>The pick-up (<c>0x004BC500</c>) and the room reset: no longer a fighter, and no
    /// forced target.</summary>
    public static void Unmark(WorldState world, CreatureState creature)
    {
        creature.CombatPitFighter = false;
        if (creature.AttackAbnormalTargets || creature.AttackingId != CombatRules.NoTarget) ClearForcedTarget(world, creature);
    }

    /// <summary><c>0x004D8100</c>: the nearest fighter on the pit's floor that may be paired with
    /// <paramref name="seeker"/>, a hostile one first and a friendly one if there is none.</summary>
    /// <remarks>DK2 walks the room's tile chain and each tile's creature list; ours walks the
    /// creatures in id order, so between two at exactly one distance the pick may differ.</remarks>
    public static CreatureState? Opponent(WorldState world, GameDatabase database, RoomInstance pit, CreatureState seeker)
    {
        return Nearest(world, database, pit, seeker, hostile: true) ?? Nearest(world, database, pit, seeker, hostile: false);
    }

    private static CreatureState? Nearest(WorldState world, GameDatabase database, RoomInstance pit, CreatureState seeker, bool hostile)
    {
        CreatureState? chosen = null;
        int best = int.MaxValue;
        int cap = database[VariableTypeId.CombatPitMaxExperienceLevel];
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState candidate = creatures[i];
            if (candidate.Id == seeker.Id || !candidate.CombatPitFighter || candidate.Level >= cap) continue;
            if (CreatureDeathRules.IsDown(candidate) || !IsOnTheMap(candidate)) continue;
            if (CombatRules.IsHostile(world, seeker.OwnerPlayerId, candidate.OwnerPlayerId) != hostile) continue;
            TilePos tile = candidate.Tile;
            if (!IsFloor(pit, (tile.Y * world.Width) + tile.X)) continue;
            int d = CombatRules.DistanceSquared(seeker.PosX, seeker.PosY, candidate.PosX, candidate.PosY);
            if (d >= best) continue;
            best = d;
            chosen = candidate;
        }

        return chosen;
    }

    /// <summary><c>+0x16 != 2</c>: not in a hand and not carried (<c>combat-pit.md</c> §6).</summary>
    public static bool IsOnTheMap(CreatureState creature) =>
        !HandRules.IsHeld(creature) && creature.CarriedByCreatureId < 0;

    /// <summary><c>0x004E50F0(position, out, count, rimOnly, empty)</c>: from a random start, the
    /// first of the <paramref name="count"/> neighbours (4: orthogonal; 8: all round) that is the
    /// same room, not solid, and floor exactly when the creature's own tile is, or with
    /// <paramref name="rimOnly"/> not floor; with <paramref name="empty"/>, nobody stands on it
    /// (<c>0x00453670</c>). -1 when none is. <c>0x00455000</c>'s byte is taken as equal within a
    /// room. 139 passes <c>(4, 0, 0)</c>, 142 <c>(4 or 8, 1, 1)</c>.</summary>
    public static int NeighbourStep(SimulationContext context, RoomInstance pit, CreatureState creature,
        int count = 4, bool rimOnly = false, bool empty = false)
    {
        WorldState world = context.World;
        TilePos at = creature.Tile;
        int here = (at.Y * world.Width) + at.X;
        bool floor = IsFloor(pit, here);
        (int Dx, int Dy)[] table = count == 4 ? Directions : EightDirections;
        int start = context.Rng.NextInt(count);
        for (int i = 0; i < count; i++)
        {
            (int dx, int dy) = table[(start + i) % count];
            var next = new TilePos(at.X + dx, at.Y + dy);
            if (!world.Contains(next)) continue;
            int index = (next.Y * world.Width) + next.X;
            if (!pit.Contains(index)) continue;
            if (context.Database.TerrainById(world.TileAtIndex(index).TerrainId).IsSolid) continue;
            if (IsFloor(pit, index) != (!rimOnly && floor)) continue;
            if (empty && AnyoneOn(world, next, null)) continue;
            return index;
        }

        return -1;
    }

    /// <summary>Whether a creature on the map other than <paramref name="except"/> stands on the
    /// tile: its tile list's things with container state 0 (<c>+0x12</c>).</summary>
    public static bool AnyoneOn(WorldState world, TilePos tile, CreatureState? except)
    {
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState other = creatures[i];
            if (ReferenceEquals(other, except) || !IsOnTheMap(other)) continue;
            if (other.Tile == tile) return true;
        }

        return false;
    }

    /// <summary>The level increment <c>0x0048B120</c> and the adder <c>0x00457880</c>: a fighter
    /// at <c>CombatPitMaxExperienceLevel</c> or above.</summary>
    public static bool AtTheCap(GameDatabase database, CreatureState creature) =>
        creature.CombatPitFighter && creature.Level >= database[VariableTypeId.CombatPitMaxExperienceLevel];
}
