// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Trigger action 14, DK2's <c>0x00519F90</c> (hero-parties-and-effect-generators.md): a
/// hero party is raised at an action point's centre tile, one creature per occupied member slot,
/// each given its level, gold, health, job, flags and trigger root from the member record, and the
/// party then linked follower-to-leader. Ours differs, stated: creatures stand at tile centres where
/// DK2 uses the tile's corner; things already on the tile are not cleared; no party pace,
/// no gate state 269 or effects, no King notification (Q427).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hero-parties-and-effect-generators.md")]
public static class HeroPartyRules
{
    /// <summary>DK2's "no party" value for a creature's <c>PartyId</c> (<c>+0x417 != 0xFF</c> in
    /// <c>travel-states.md</c> §2.3).</summary>
    public const byte NoParty = 0xFF;

    /// <summary>The world holds at most this many creatures (<c>0x004B7900</c>); a spawn past it fails.</summary>
    public const int CreatureCap = 0x1C0;

    /// <summary>Creature type 14, the Dwarf, always the first member of a random party.</summary>
    public const byte DwarfCreatureId = 14;

    /// <summary>The handler's guard scan: a living, unimprisoned creature of the hero player already
    /// carrying this party id means the party is up and is not raised again.</summary>
    public static bool IsAlive(WorldState world, byte partyId)
    {
        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.OwnerPlayerId != PlayerIds.Good || creature.PartyId != partyId) continue;
            if (PrisonRules.IsImprisoned(creature) || CreatureDeathRules.IsDying(creature)) continue;
            return true;
        }
        return false;
    }

    /// <summary>The spawn tile: the action point's centre (signed halving), or (0,0) when the id
    /// matches nothing — DK2 leaves the position zeroed. A Hero Gate 2×2 under it moves the spawn
    /// one tile right and down, to the tile whose corner is the gate's centre point.</summary>
    public static TilePos SpawnTile(WorldState world, GameDatabase database, byte actionPointId, out bool fromGate)
    {
        int x = 0, y = 0;
        foreach (ActionPointState point in world.ActionPoints)
        {
            if (point.Id != actionPointId) continue;
            x = (point.StartX + point.EndX) / 2;
            y = (point.StartY + point.EndY) / 2;
            break;
        }
        fromGate = false;
        if (!world.Contains(x, y)) return new TilePos(x, y);
        RoomDef? room = database.RoomByTerrain(world.Tile(x, y).TerrainId);
        if (room is null) return new TilePos(x, y);
        if (room.RoomId == (byte)RoomTypeId.HeroGate2By2) { fromGate = true; x++; y++; }
        else if (room.RoomId == (byte)RoomTypeId.HeroGate3By1) fromGate = true;
        return new TilePos(x, y);
    }

    /// <summary>Raise the party. Returns <see langword="false"/> only where DK2 returns 0: a
    /// creature could not be made (the cap, or an unplaceable tile), the members already made
    /// staying. <paramref name="armSubtree"/> is the executor's arming of a trigger subtree and
    /// <paramref name="created"/> its firing of creature event 3 for each member made.</summary>
    public static bool Spawn(
        WorldState world, GameDatabase database, SimRandom? rng, TriggerTree tree,
        HeroPartyState party, byte actionPointId, byte partyType, Action<ushort> armSubtree,
        Action<CreatureState>? created = null)
    {
        if (partyType != 0 && IsAlive(world, party.PartyId)) return true;

        TilePos at = SpawnTile(world, database, actionPointId, out _);
        if (party.TriggerId != 0) armSubtree(party.TriggerId);

        for (int i = 0; i < party.Members.Count; i++)
        {
            HeroPartyMemberState member = party.Members[i];
            if (member.CreatureId == 0) continue;

            byte type = partyType == 0 ? member.CreatureId
                : i == 0 ? DwarfCreatureId
                : RandomInvader(database, rng);
            if (!world.Contains(at) || world.Creatures.Count >= CreatureCap) return false;

            CreatureDef def = database.CreatureById(type);
            CreatureState state = world.SpawnCreature(database, type, PlayerIds.Good, at);

            // Byte arithmetic, as DK2's: the level-ups to buy, plus one per earlier raising of a
            // random party.
            int levelUps = (byte)(member.Level - 1 + (partyType != 0 ? party.SpawnCount : 0)) % 10;

            // Percentages of the member level's maxima, computed as doubles and truncated; both
            // land on a level-1 creature and are capped by its level-1 maxima.
            int gold = (int)(member.GoldHeld * 0.01 * CreatureLevelRules.MaxGoldHeld(database, def, member.Level));
            PurseRules.Receive(database, def, state, gold);
            int health = (int)(member.InitialHealth * 0.01 * CreatureLevelRules.MaxHealth(database, def, member.Level));
            CreatureHealthRules.Apply(database, def, state, health - state.Health);
            for (int k = 0; k < levelUps; k++) CreatureLevelRules.LevelUp(database, def, state);

            state.WanderRadius = member.WanderRadius;
            state.PartyId = member.PartyId;
            state.GoodJob = partyType != 0 && levelUps == 0 ? JobType.KillPlayer : (JobType)member.Objective;
            state.GoodJobPlayerId = member.ObjectiveTargetPlayerId;
            state.GoodJobActionPointId = member.ObjectiveTargetActionPointId;

            // Seven of the eight member flag bits; bit 4 has no setter in the handler.
            state.Flags = (byte)(member.Flags & 0xEF);
            state.DestroysRooms = (member.Flags2 & 0x01) != 0;
            state.IsTool = (member.Flags2 & 0x02) != 0;
            state.DieInstantly = (member.Flags2 & 0x04) != 0;
            state.IsMercenary = (member.Flags2 & 0x08) != 0;

            state.TriggerId = member.TriggerId;
            state.TriggerRepeat = tree.ResolveCondition(member.TriggerId)?.RepeatCount ?? 0;
            if (member.TriggerId != 0) armSubtree(member.TriggerId);
            created?.Invoke(state);
        }

        party.SpawnCount++;
        LinkFollowers(world, party.PartyId);
        return true;
    }

    /// <summary>A random creature id whose definition is flagged <c>MpdRandomInvader</c>: DK2 draws
    /// over the definition count and redraws until one fits.</summary>
    public static byte RandomInvader(GameDatabase database, SimRandom? rng)
    {
        if (rng is null)
            throw new InvalidOperationException("A random hero party needs the simulation's random source.");
        int count = 0;
        foreach (CreatureDef def in database.Creatures.Values)
            if ((def.Flags & CreatureFlags.MpdRandomInvader) != 0) count++;
        if (count == 0)
            throw new InvalidOperationException("No creature definition is flagged MpdRandomInvader; DK2 would loop forever here.");

        int range = database.Creatures.Keys.Max();
        while (true)
        {
            byte id = (byte)(rng.NextInt(range) + 1);
            if (database.Creatures.TryGetValue(id, out CreatureDef? def)
                && (def.Flags & CreatureFlags.MpdRandomInvader) != 0)
                return id;
        }
    }

    /// <summary>The tail of the handler: the last leader-flagged member of the party is the leader;
    /// every follower-flagged member points at it and is numbered from 1; the leader's own number is
    /// the follower count.</summary>
    public static void LinkFollowers(WorldState world, byte partyId)
    {
        CreatureState? leader = null;
        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.OwnerPlayerId != PlayerIds.Good || creature.PartyId != partyId) continue;
            if ((creature.Flags & 0x02) != 0) leader = creature;
        }

        byte k = 0;
        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.OwnerPlayerId != PlayerIds.Good || creature.PartyId != partyId) continue;
            if ((creature.Flags & 0x04) == 0) continue;
            creature.LeaderId = leader?.Id ?? -1;
            creature.PositionInParty = ++k;
        }
        if (leader is not null) leader.PositionInParty = k;
    }
}
