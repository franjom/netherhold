// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Combat;

/// <summary>Where the enemy hearts stand this tick, for the acquirer's heart scan: each hostile
/// keeper's heart centre and the heart tiles round it that stand in for its Arch Pillars, whose
/// placement is unread (Q363). Built once a tick by <see cref="Systems.CombatSystem"/>.</summary>
public sealed class HeartSites
{
    public readonly struct Site
    {
        public Site(PlayerId owner, int centreX, int centreY, int[] ringTiles, bool standing)
        {
            Owner = owner.Value;
            CentreX = centreX;
            CentreY = centreY;
            RingTiles = ringTiles;
            Standing = standing;
        }

        public byte Owner { get; }

        /// <summary>Live and unruined: a target. A fallen heart keeps its place — DK2's
        /// <c>DungeonHeartPosition</c> is never cleared — for the distance fear is scaled by.</summary>
        public bool Standing { get; }

        /// <summary>The heart's centre tile, as a position: tile centre in 1/4096 units.</summary>
        public int CentreX { get; }
        public int CentreY { get; }
        public int[] RingTiles { get; }
    }

    private readonly List<Site> _sites = [];

    public IReadOnlyList<Site> Sites => _sites;

    /// <summary>The site of this player's heart, standing or fallen; none for a player without one.</summary>
    public Site? SiteOf(PlayerId owner)
    {
        for (int i = 0; i < _sites.Count; i++)
            if (_sites[i].Owner == owner) return _sites[i];
        return null;
    }

    /// <summary>DK2's <c>DungeonHeartPosition</c> (<c>player+0xB43</c>, <c>0x004C5F50</c>) as ours
    /// derives it: the centre tile of the bounding box of the player's heart tiles, as
    /// <see cref="Refresh"/> computes it; -1 for a player with none.</summary>
    public static int PositionOf(WorldState world, GameDatabase database, PlayerId playerId)
    {
        byte? heartTerrain = ManaRules.DungeonHeartTerrainId(database);
        if (heartTerrain is null) return -1;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (tile.OwnerPlayerId != playerId || tile.TerrainId != heartTerrain.Value) continue;
            int x = i % world.Width, y = i / world.Width;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }
        return maxX < 0 ? -1 : (((minY + maxY) / 2) * world.Width) + ((minX + maxX) / 2);
    }

    /// <summary>Every keeper's heart: the bounding box of its tiles gives the centre and the box's
    /// border the ring; a fallen or ruined one is kept with <see cref="Site.Standing"/> clear.</summary>
    public void Refresh(WorldState world, GameDatabase database)
    {
        _sites.Clear();
        byte? heartTerrain = ManaRules.DungeonHeartTerrainId(database);
        if (heartTerrain is null) return;

        for (int p = 0; p < world.PlayerCount; p++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(p);
            if (!PlayerIds.IsKeeper(player.PlayerId.Value)) continue;
            bool standing = !player.HeartRuined && player.HeartHealth > 0;

            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int i = 0; i < world.TileCount; i++)
            {
                ref TileState tile = ref world.TileAtIndex(i);
                if (tile.OwnerPlayerId != player.PlayerId || tile.TerrainId != heartTerrain.Value) continue;
                int x = i % world.Width, y = i / world.Width;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
            if (maxX < 0) continue;

            var ring = new List<int>();
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    bool border = x == minX || x == maxX || y == minY || y == maxY;
                    ref TileState tile = ref world.Tile(new TilePos(x, y));
                    if (border && tile.OwnerPlayerId == player.PlayerId && tile.TerrainId == heartTerrain.Value)
                        ring.Add((y * world.Width) + x);
                }
            }

            int centreX = CreatureState.CentreOf((minX + maxX) / 2), centreY = CreatureState.CentreOf((minY + maxY) / 2);
            _sites.Add(new Site(player.PlayerId, centreX, centreY, ring.ToArray(), standing));
        }
    }
}

/// <summary>DK2's <c>SelectAVictim</c> (<c>0x0045CB00</c>, its own name) for the four fighting arms:
/// the shared scan over every creature in the game with each arm's score, the acceptance by sight
/// or by a walkable route, the route check on the winner, the trap scan (§6 scan D), the heart scan
/// (scan E), the friendly scan (F), the final gate, and the fight-starter that registers the
/// engagement and sets the reach (docs/research/combat-acquisition.md §6–§8), and the withdrawal of
/// a hurt creature in place of the fight (combat-fear.md §3), and the attack-spell survey that lets
/// a caster engage at its spell's range (combat-spells.md §5, ADR-0167). Not built and named there:
/// the thing-targeting survey and scans A, B and C.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-acquisition.md")]
public static class CombatTargetRules
{
    /// <summary>One tile squared in the scans' units: closer than this, a creature fights hand to hand.</summary>
    public const int MeleeDecisionDistance = 0x1000;

    /// <summary>Every arm runs: the four fighting arms since ADR-0163/0164, the NonFighter since ADR-0166.</summary>
    public static bool IsBuilt(FightStyle style) => true;

    /// <summary>A worker within this of its own heart, squared, fights anything (NonFighter §3.1).</summary>
    public const int HeartDefenceRadiusSquared = 0xD000;

    /// <summary>Finds and engages a target for <paramref name="me"/>. True when it now fights, or —
    /// DK2's <c>0xEA60</c> — when the NonFighter arm has taken it out of the fight instead.</summary>
    public static bool SelectAVictim(
        SimulationContext context, ISightBlocker sight, HeartSites hearts, RoomCache rooms, CreatureState me, CreatureDef definition,
        List<Fixed16Vector> scratch)
    {
        if (me.AttackAbnormalTargets)
        {
            if (!Forced(context, sight, me, definition, scratch)) return false;
        }
        else if (!Choose(context, sight, hearts, rooms, me, definition, scratch, out bool left)) return false;
        else if (left) return true;

        // The acquirer's own withdraw check (0x0045DEE0): a hurt creature far from its heart lets
        // the target it just chose go and runs instead; finding nowhere to run, it fights.
        if (FearRules.ShouldWithdraw(context.World, context.Database, hearts, me, definition) && !CreatureDeathRules.IsDown(me))
            FearRules.Withdraw(context, sight, hearts, rooms, me, definition, scratch);
        return true;
    }

    /// <summary>The forced target (<c>AttackingId</c>, combat-pit.md §4): taken with no
    /// attackability, sight, hostility or range test, but a route to it must be found, or an attack
    /// spell must see it. As for a door, the forced arm leaves the running best at
    /// <c>0x7FFFFFFF</c>, so a surveyed attack spell, status ones included, is cast however near the
    /// target is (<c>0x0045CF05</c>, Q510). A dead one is cleared. Not built: scan A, and the scans
    /// D, E and F a forced creature target still runs (door-blocking.md §2).</summary>
    private static bool Forced(
        SimulationContext context, ISightBlocker sight, CreatureState me, CreatureDef definition, List<Fixed16Vector> scratch)
    {
        if (me.AttackingKind == FightTargetKind.Door) return ForcedDoor(context, sight, me, definition, scratch);
        CreatureState? target = context.World.CreatureById(me.AttackingId);
        if (target is null || !CreatureStateMachine.HasState(target))
        {
            CombatPitRules.ClearForcedTarget(context.World, me);
            return false;
        }

        int spellSlot = -1;
        CreatureSpellDef? spell = null;
        if (!definition.Flags.HasFlag(CreatureFlags.IsWorker))
            spellSlot = SpellRules.SurveyAttack(context.Database, me, definition, context.Tick.Value, out spell,
                whileRecharging: SpellWhileRecharging(context, me, definition, scratch));
        if (CombatPitRules.IsOnTheMap(target)
            && !CanReach(context.Navigation, me, definition, target.PosX, target.PosY, int.MaxValue, scratch)
            && (spell is null || !LineOfSight.Clear(sight, me.PosX, me.PosY, target.PosX, target.PosY)))
            return false;

        return Engage(context, me, definition, target, context.Database.CreatureById(target.CreatureTypeId), spellSlot, spell, forced: true);
    }

    /// <summary>A forced door (door-blocking.md, <c>0x0045CB00</c>): taken with no route, sight or
    /// final-gate test, and a gone one clears the forced target. The survey skips an attack spell
    /// with the status bit (flag 3), as against any forced thing. Scan F still runs, so an ally
    /// wanting the friendly spell outranks the door, and one that cannot be reached means no fight. The forced arm leaves the running best at
    /// <c>0x7FFFFFFF</c>, so the within-a-tile melee rule never applies: a spell, if any, is cast.</summary>
    private static bool ForcedDoor(
        SimulationContext context, ISightBlocker sight, CreatureState me, CreatureDef definition, List<Fixed16Vector> scratch)
    {
        DoorState? door = context.World.DoorById(me.AttackingId);
        if (door is null)
        {
            CombatPitRules.ClearForcedTarget(context.World, me);
            return false;
        }

        int spellSlot = -1, friendlySlot = -1;
        CreatureSpellDef? spell = null, friendly = null;
        if (!definition.Flags.HasFlag(CreatureFlags.IsWorker))
        {
            spellSlot = SpellRules.SurveyAttack(context.Database, me, definition, context.Tick.Value, out spell, skipStatus: true,
                whileRecharging: SpellWhileRecharging(context, me, definition, scratch));
            friendlySlot = SpellRules.SurveyFriendly(context.Database, me, definition, context.Tick.Value, out friendly);
        }

        if (friendly != null)
        {
            CreatureState? ally = FriendlyScan(context, sight, me, definition, friendly,
                CombatRules.RangeSquared(definition.DistanceCanSee), CombatRules.RangeSquared(definition.DistanceCanHear), scratch);
            // An ally found outranks the door; one the route refuses ends this call with no fight.
            if (ally != null) return EngageAlly(context, me, definition, ally, friendlySlot, friendly, scratch);
        }
        return EngageDoor(context, me, definition, door, spellSlot, spell);
    }

    /// <summary>Commit on a door: <c>AttackType</c> 4 at <c>(MeleeRange + 0x800)² >> 12</c>, or
    /// <c>0xC00</c> for a Secret Door, or 2 at the spell's range; no attacker slots and no event.</summary>
    private static bool EngageDoor(
        SimulationContext context, CreatureState me, CreatureDef mine, DoorState door, int spellSlot, CreatureSpellDef? spell)
    {
        bool useSpell = spell != null;
        bool same = me.FightTargetKind == FightTargetKind.Door && me.FightTargetId == door.Id;
        me.FightTargetKind = FightTargetKind.Door;
        me.FightTargetId = door.Id;
        me.FightTargetPlayerId = door.PlayerId;
        me.FightTargetTileIndex = (door.Y * context.World.Width) + door.X;
        if (useSpell)
        {
            me.AttackType = CombatRules.AttackMode.SpellAtThing;
            me.AttackSpellSlot = (byte)spellSlot;
            me.AttackRangeSquared = SpellRules.RangeSquared(spell!);
        }
        else
        {
            bool secret = context.Database.Doors.TryGetValue(door.DoorId, out DoorDef? def) && def.Flags.HasFlag(DoorFlags.Secret);
            me.AttackType = CombatRules.AttackMode.MeleeAtThing;
            me.AttackSpellSlot = 0;
            me.AttackRangeSquared = CombatRules.ReachSquared(mine.MeleeRange + new Fp32(secret ? SecretDoorReach : DoorReach));
        }
        Enter(context, me, mine, same, $"door={door.Id} attack={(byte)me.AttackType}");
        return true;
    }

    /// <summary>The survey's Support clause (<c>0x0045CB00</c>, combat-spells.md §5): a Support
    /// creature with a tile to back on to (<c>0x00460C50</c> finds an escape) surveys its attack
    /// spell whether or not it has recharged, so it keeps the spell nibble and its range while the
    /// spell cools — standing off and stepping back rather than closing to melee. A cornered one,
    /// and every other fight style, needs the spell ready.</summary>
    private static bool SpellWhileRecharging(SimulationContext context, CreatureState me, CreatureDef definition, List<Fixed16Vector> scratch) =>
        definition.FightStyle == FightStyle.Support && !FearRules.Cornered(context, me, definition, scratch);

    /// <summary>The thing reach a door adds to <c>MeleeRange</c>, and a Secret Door's.</summary>
    public const int DoorReach = 0x800;
    public const int SecretDoorReach = 0xC00;

    /// <summary>The scans in <c>0x0045CB00</c>'s order: the arm's choice, the route check on it, the traps
    /// (scan D) when nothing is left, the hearts (E), the friendly spell (F), then the commit. True with
    /// <paramref name="left"/> when the NonFighter arm took the creature out of the fight (<c>0xEA60</c>).</summary>
    private static bool Choose(
        SimulationContext context, ISightBlocker sight, HeartSites hearts, RoomCache rooms, CreatureState me, CreatureDef definition,
        List<Fixed16Vector> scratch, out bool left)
    {
        left = false;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int sightSq = CombatRules.RangeSquared(definition.DistanceCanSee);
        int hearSq = CombatRules.RangeSquared(definition.DistanceCanHear);

        // The spell survey (combat-spells.md §5), skipped for a worker: the attack spell the
        // fight-starter will pick over melee at range, and the last friendly slot for scan F.
        // Thing-targeting spells are not built.
        int spellSlot = -1, friendlySlot = -1;
        CreatureSpellDef? spell = null, friendly = null;
        if (!definition.Flags.HasFlag(CreatureFlags.IsWorker))
        {
            spellSlot = SpellRules.SurveyAttack(database, me, definition, context.Tick.Value, out spell,
                whileRecharging: SpellWhileRecharging(context, me, definition, scratch));
            friendlySlot = SpellRules.SurveyFriendly(database, me, definition, context.Tick.Value, out friendly);
        }

        int best = int.MaxValue;
        CreatureState? chosen;
        // 0x0045D5BF: a follower takes its leader's last melee target and no fight-style arm runs
        // (0x0045D5F5); the best becomes its plain squared distance (0x0045D69D).
        CreatureState? leaders = PossessionRules.LeaderTarget(world, me);
        if (leaders != null)
        {
            chosen = leaders;
            best = unchecked((int)TrapRules.DistanceSquared(me.PosX, me.PosY, leaders.PosX, leaders.PosY));
        }
        else if (definition.FightStyle == FightStyle.NonFighter)
        {
            if (NonFighter(context, sight, hearts, rooms, me, definition, scratch, out chosen) && chosen is null)
            {
                left = true;
                return true;
            }
        }
        else chosen = Scan(context, sight, me, definition, sightSq, hearSq, scratch, ref best);

        // The route check on the arm's creature (0x0045D74A), before the thing scans: unwalkable, it is
        // dropped unless an attack spell can see it, and the scans below look on.
        if (chosen != null && !CanReach(context.Navigation, me, definition, chosen.PosX, chosen.PosY, int.MaxValue, scratch)
            && (spell is null || !LineOfSight.Clear(sight, me.PosX, me.PosY, chosen.PosX, chosen.PosY)))
            chosen = null;

        // Scan D: an enemy's revealed trap, when no creature is left; skipped during Turncoat.
        TrapState? trap = null;
        int trapDistance = 0;
        if (chosen is null) trap = ScanTraps(world, sight, me, definition, sightSq, out trapDistance);

        // Scan E: the enemy heart outranks nothing but a worker, and only an Imp or a Horned
        // Reaper never looks at it; a trap outranks it (the class test at 0x0045D91B).
        bool heartMayEnter = trap is null
            && (chosen is null || database.CreatureById(chosen.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker));
        int heartTile = -1;
        PlayerId heartOwner = 0;
        if (heartMayEnter && me.CreatureTypeId != CombatRules.ImpCreatureTypeId && !definition.Flags.HasFlag(CreatureFlags.IsHorny))
            heartTile = ScanHearts(world, sight, hearts, me, sightSq, ref best, out heartOwner);

        // Scan F (spell-friendly-arm.md §8): an ally that wants the friendly spell outranks whatever
        // the hostile scans found; none found, the hostile best stands.
        if (friendly != null)
        {
            CreatureState? ally = FriendlyScan(context, sight, me, definition, friendly, sightSq, hearSq, scratch);
            if (ally != null && EngageAlly(context, me, definition, ally, friendlySlot, friendly, scratch)) return true;
        }

        if (heartTile >= 0)
        {
            if (!CanReach(context.Navigation, me, definition, TileCentreX(world, heartTile), TileCentreY(world, heartTile), int.MaxValue, scratch))
                return false;
            return EngageHeart(context, me, definition, heartOwner, heartTile, spellSlot, spell);
        }

        if (trap != null) return EngageTrap(context, sight, me, definition, trap, trapDistance, sightSq, hearSq, spellSlot, spell);
        if (chosen is null) return false;
        return Commit(context, sight, me, definition, chosen, spellSlot, spell);
    }

    /// <summary>Scan D (<c>0x0045D7A5</c>–<c>0x0045D8D8</c>): each hostile player's traps, newest linked
    /// first (the owner list is prepend, <c>0x004B4C61</c>); one hidden or under construction
    /// (<c>+0x10C &amp; 0x30</c>) or a Jack In The Box (trap 16) is passed over. The nearest by the
    /// squared distance, strictly, within sight inclusive and seen; a worker takes one only under
    /// <see cref="WorkerTrapDistance"/>. The players' order is the game's list, unread, here ascending
    /// id; it decides only a tie between two players' traps.</summary>
    public static TrapState? ScanTraps(
        WorldState world, ISightBlocker sight, CreatureState me, CreatureDef definition, int sightSq, out int distance)
    {
        if (TurncoatRules.IsActive(me))
        {
            distance = int.MaxValue;
            return null;
        }
        TrapState? chosen = null;
        int best = int.MaxValue;
        bool worker = definition.Flags.HasFlag(CreatureFlags.IsWorker);
        IReadOnlyList<TrapState> traps = world.Traps;
        for (int i = traps.Count - 1; i >= 0; i--)
        {
            TrapState trap = traps[i];
            if (trap.Hidden || trap.UnderConstruction || trap.TrapId == TrapRules.JackInTheBoxId) continue;
            if (!CombatRules.IsHostile(world, me.OwnerPlayerId, trap.PlayerId)) continue;
            int d = CombatRules.DistanceSquared(me.PosX, me.PosY, trap.PosX, trap.PosY);
            if (worker && d >= WorkerTrapDistance) continue;
            // Walking newest first, an equal distance keeps the earlier find, and a lower player's.
            if (d > best || (d == best && trap.PlayerId >= chosen!.PlayerId) || d > sightSq) continue;
            if (!LineOfSight.Clear(sight, me.PosX, me.PosY, trap.PosX, trap.PosY)) continue;
            best = d;
            chosen = trap;
        }
        distance = best;
        return chosen;
    }

    /// <summary>The worker's limit in scan D, squared (<c>0x0045D86A</c>): <c>0x800</c>, about 0.7 of a tile.</summary>
    public const int WorkerTrapDistance = 0x800;

    /// <summary>Commit on a trap (<c>0x0045DDA8</c>–<c>0x0045E0BA</c>): the final gate on a third of the
    /// distance; then melee within a tile unless a Support, else the surveyed spell unless its status
    /// bit (3) sends it to melee against a thing, else melee. Melee is <c>AttackType</c> 4 at
    /// <c>(MeleeRange + 0x400)² >> 12</c>; a spell 2 at its range. No attacker slots and no event.</summary>
    private static bool EngageTrap(
        SimulationContext context, ISightBlocker sight, CreatureState me, CreatureDef mine, TrapState trap, int distance,
        int sightSq, int hearSq, int spellSlot, CreatureSpellDef? spell)
    {
        int third = distance / 3;
        if (third >= hearSq && (third >= sightSq || !LineOfSight.Clear(sight, me.PosX, me.PosY, trap.PosX, trap.PosY)))
            return false;

        bool melee = spell is null || (distance < MeleeDecisionDistance && mine.FightStyle != FightStyle.Support) || SpellRules.IsStatus(spell);
        bool same = me.FightTargetKind == FightTargetKind.Trap && me.FightTargetId == trap.Id;
        me.FightTargetKind = FightTargetKind.Trap;
        me.FightTargetId = trap.Id;
        me.FightTargetPlayerId = trap.PlayerId;
        me.FightTargetTileIndex = (trap.Y * context.World.Width) + trap.X;
        if (melee)
        {
            me.AttackType = CombatRules.AttackMode.MeleeAtThing;
            me.AttackSpellSlot = 0;
            me.AttackRangeSquared = CombatRules.ReachSquared(mine.MeleeRange + new Fp32(CombatRules.TrapReach));
        }
        else
        {
            me.AttackType = CombatRules.AttackMode.SpellAtThing;
            me.AttackSpellSlot = (byte)spellSlot;
            me.AttackRangeSquared = SpellRules.RangeSquared(spell!);
        }
        Enter(context, me, mine, same, $"trap={trap.Id} attack={(byte)me.AttackType}");
        return true;
    }

    /// <summary>Scan F (spell-friendly-arm.md §8.2—§8.3): a self-only spell offers the caster alone, in the
    /// Combat category and wanting it; any other walks the caster's own creatures — the caster
    /// included, allies never — for the nearest that is not captive, wants the spell, is in the
    /// Combat category, has under a quarter of its level-scaled health, and is seen within sight or
    /// reached within hearing. Turncoat skips this scan; pit and torture refusals are not modelled.</summary>
    private static CreatureState? FriendlyScan(
        SimulationContext context, ISightBlocker sight, CreatureState me, CreatureDef definition, CreatureSpellDef spell,
        int sightSq, int hearSq, List<Fixed16Vector> scratch)
    {
        if (TurncoatRules.IsActive(me)) return null;
        GameDatabase database = context.Database;
        if (SpellRules.IsSelfOnly(spell))
            return CombatRules.InCombat(me) && SpellRules.BuffApplies(database, definition, me, spell) ? me : null;

        CreatureState? chosen = null;
        int best = int.MaxValue;
        IReadOnlyList<CreatureState> creatures = context.World.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState candidate = creatures[i];
            if (candidate.OwnerPlayerId != me.OwnerPlayerId) continue;
            int d = CombatRules.DistanceSquared(me.PosX, me.PosY, candidate.PosX, candidate.PosY);
            if (d >= best || PrisonRules.IsImprisoned(candidate)) continue;
            CreatureDef theirs = database.CreatureById(candidate.CreatureTypeId);
            if (!SpellRules.BuffApplies(database, theirs, candidate, spell) || !CombatRules.InCombat(candidate)) continue;
            if (candidate.Health >= (CreatureHealthRules.MaxHealth(database, theirs, candidate) >> 2)) continue;
            bool seen = d < sightSq && LineOfSight.Clear(sight, me.PosX, me.PosY, candidate.PosX, candidate.PosY);
            if (!seen && !(d < hearSq && CanReach(context.Navigation, me, definition, candidate.PosX, candidate.PosY, hearSq, scratch)))
                continue;
            best = d;
            chosen = candidate;
        }
        return chosen;
    }

    /// <summary>Scan F's engagement (§8.4): one final route test, then the ally as the target with nibble 3,
    /// the slot and the spell's range squared. No attacker slot is taken on the ally.</summary>
    private static bool EngageAlly(
        SimulationContext context, CreatureState me, CreatureDef mine, CreatureState ally, int slot, CreatureSpellDef spell,
        List<Fixed16Vector> scratch)
    {
        if (ally.Id != me.Id && !CanReach(context.Navigation, me, mine, ally.PosX, ally.PosY, int.MaxValue, scratch)) return false;
        bool same = me.FightTargetKind == FightTargetKind.Creature && me.FightTargetId == ally.Id
            && me.AttackType == CombatRules.AttackMode.SpellAtAlly;
        if (me.FightTargetKind == FightTargetKind.Creature && me.FightTargetId != CombatRules.NoTarget && !same)
        {
            CreatureState? previous = context.World.CreatureById(me.FightTargetId);
            if (previous != null) CombatRules.Unregister(previous, me.Id);
        }
        me.FightTargetKind = FightTargetKind.Creature;
        me.FightTargetId = ally.Id;
        me.FightTargetPlayerId = 0;
        me.FightTargetTileIndex = -1;
        me.AttackType = CombatRules.AttackMode.SpellAtAlly;
        me.AttackSpellSlot = (byte)slot;
        me.AttackRangeSquared = SpellRules.RangeSquared(spell);
        Enter(context, me, mine, same, $"ally={ally.Id} attack={(byte)me.AttackType} slot={slot}");
        return true;
    }

    /// <summary>The acquirer's common tail on a chosen creature, its route already checked in
    /// <see cref="Choose"/>: the last gate on a third of the distance, within hearing or within sight
    /// and seen. Then the fight-starter.</summary>
    private static bool Commit(
        SimulationContext context, ISightBlocker sight, CreatureState me, CreatureDef definition, CreatureState chosen,
        int spellSlot = -1, CreatureSpellDef? spell = null)
    {
        int sightSq = CombatRules.RangeSquared(definition.DistanceCanSee);
        int hearSq = CombatRules.RangeSquared(definition.DistanceCanHear);
        int distance = CombatRules.DistanceSquared(me.PosX, me.PosY, chosen.PosX, chosen.PosY);
        int third = distance / 3;
        if (third >= hearSq && (third >= sightSq || !LineOfSight.Clear(sight, me.PosX, me.PosY, chosen.PosX, chosen.PosY)))
            return false;

        return Engage(context, me, definition, chosen, context.Database.CreatureById(chosen.CreatureTypeId), spellSlot, spell);
    }

    /// <summary>The NonFighter arm (<c>0x0045BDF0</c>, the Imp's and Jack's; combat-target-selection.md
    /// §3, combat-alarm.md §1). Within <see cref="HeartDefenceRadiusSquared"/> of its own heart a
    /// worker judges every hostile creature by plain distance like a Blocker — an Imp only within a
    /// tile — and fights; an enemy Imp it judges that way anywhere (<c>0x00494360</c>, the base
    /// creature type, is 1). Elsewhere it looks only at heroes within sight that have no target or
    /// target it, and never fights them: away from its rooms it withdraws; near them it counts
    /// heads, keeps scanning when its side at three quarters outweighs the enemy's within five
    /// tiles, and otherwise raises the alarm or withdraws. False: nothing to do. True with a
    /// <paramref name="chosen"/>: fight it. True without: the creature has left.</summary>
    private static bool NonFighter(
        SimulationContext context, ISightBlocker sight, HeartSites hearts, RoomCache rooms, CreatureState me, CreatureDef definition,
        List<Fixed16Vector> scratch, out CreatureState? chosen)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        chosen = null;
        bool imp = me.CreatureTypeId == CombatRules.ImpCreatureTypeId;
        int sightSq = imp ? MeleeDecisionDistance : CombatRules.RangeSquared(definition.DistanceCanSee);
        int hearSq = imp ? 0 : CombatRules.RangeSquared(definition.DistanceCanHear);

        int heartX = 0, heartY = 0;
        HeartSites.Site? heart = hearts.SiteOf(me.OwnerPlayerId);
        if (heart.HasValue)
        {
            heartX = heart.Value.CentreX;
            heartY = heart.Value.CentreY;
        }
        bool nearHeart = CombatRules.DistanceSquared(me.PosX, me.PosY, heartX, heartY) < HeartDefenceRadiusSquared;

        int best = int.MaxValue;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState candidate = creatures[i];
            if (candidate.Id == me.Id || !TurncoatRules.AreCombatEnemies(world, me, candidate)) continue;
            CreatureDef theirs = database.CreatureById(candidate.CreatureTypeId);
            int d = CombatRules.DistanceSquared(me.PosX, me.PosY, candidate.PosX, candidate.PosY);

            // The escape (0x00494360 == 1, combat-alarm.md §6): an enemy Imp — by its base type, so a
            // unique's archetype counts — is judged by the shared template anywhere, not only near the heart.
            bool impCandidate = BaseTypeId(theirs) == CombatRules.ImpCreatureTypeId;
            if (nearHeart || impCandidate)
            {
                // The shared template: distance, the alignment discount, attackable, seen or reached.
                if (CombatRules.OppositeAlignment(definition, theirs)) d = CombatRules.Discounted(d);
                if (d >= best || !CombatRules.IsAttackable(candidate)) continue;
                bool seen = d < sightSq && LineOfSight.Clear(sight, me.PosX, me.PosY, candidate.PosX, candidate.PosY);
                if (!seen && !(d < hearSq && CanReach(context.Navigation, me, definition, candidate.PosX, candidate.PosY, hearSq, scratch)))
                    continue;
                best = d;
                chosen = candidate;
                continue;
            }

            // The hero path: reluctance, never a fight.
            if (candidate.OwnerPlayerId != PlayerIds.Good) continue;
            if (d >= sightSq || !LineOfSight.Clear(sight, me.PosX, me.PosY, candidate.PosX, candidate.PosY)) continue;
            if (d >= best) continue;
            if (candidate.FightTargetKind == FightTargetKind.Creature && candidate.FightTargetId != me.Id) continue;
            if (!CombatRules.IsAttackable(candidate)) continue;

            if (!FearRules.NearOwnRooms(context, rooms, me.OwnerPlayerId, candidate.Tile))
            {
                if (FearRules.Withdraw(context, sight, hearts, rooms, me, definition, scratch)) return true;
                continue;
            }
            int theirStrength = FearRules.LocalStrength(context, candidate.OwnerPlayerId, me.PosX, me.PosY);
            int ourStrength = FearRules.LocalStrength(context, me.OwnerPlayerId, me.PosX, me.PosY) * 3 / 4;
            if (ourStrength > theirStrength) continue;
            int deficit = (theirStrength - ourStrength) * 3 / 2;
            if (FearRules.RaiseAlarm(context, rooms, me, definition, candidate.PosX, candidate.PosY, deficit)) return true;
            if (FearRules.Withdraw(context, sight, hearts, rooms, me, definition, scratch)) return true;
        }
        return chosen != null;
    }

    /// <summary>DK2's <c>0x00494360</c>: a unique definition's ordinary archetype, any other's own id.</summary>
    private static byte BaseTypeId(CreatureDef definition) =>
        (definition.Flags & CreatureFlags.IsUnique) != 0 ? definition.UniqueBaseCreatureId : definition.CreatureId;

    /// <summary>The fighting arms (<c>0x0045B140</c> and its three siblings): every hostile creature in
    /// the game, scored by the arm's one line over the discounted distance and the counts of who
    /// already fights it, lower wins; a candidate that beats the best so far must be attackable
    /// and then either seen within sight range or walkable to within hearing range.</summary>
    private static CreatureState? Scan(
        SimulationContext context, ISightBlocker sight, CreatureState me, CreatureDef definition,
        int sightSq, int hearSq, List<Fixed16Vector> scratch, ref int best)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureState? chosen = null;

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState candidate = creatures[i];
            if (!TurncoatRules.AreCombatEnemies(world, me, candidate)) continue;

            CreatureDef theirs = database.CreatureById(candidate.CreatureTypeId);
            int d = CombatRules.DistanceSquared(me.PosX, me.PosY, candidate.PosX, candidate.PosY);
            if (CombatRules.OppositeAlignment(definition, theirs)) d = CombatRules.Discounted(d);

            CombatRules.CountEngaged(world, database, candidate, out int melee, out int ranged);
            int score = CombatRules.Score(definition.FightStyle, d, sightSq, melee, ranged, theirs.FightStyle == FightStyle.Support);
            if (score >= best) continue;
            if (!CombatRules.IsAttackable(candidate)) continue;

            bool seen = d < sightSq && LineOfSight.Clear(sight, me.PosX, me.PosY, candidate.PosX, candidate.PosY);
            if (!seen && !(d < hearSq && CanReach(context.Navigation, me, definition, candidate.PosX, candidate.PosY, hearSq, scratch)))
                continue;

            best = score;
            chosen = candidate;
        }
        return chosen;
    }

    /// <summary>Scan E: each hostile keeper's heart whose centre is within sight and seen, and then
    /// its nearest ring tile that beats the best so far. DK2's candidates are the room's Arch
    /// Pillar objects, each a proxy whose blow the health routine hands to the heart; the ring
    /// tile is the stand-in until their placement is read (Q363).</summary>
    private static int ScanHearts(
        WorldState world, ISightBlocker sight, HeartSites hearts, CreatureState me, int sightSq, ref int best, out PlayerId owner)
    {
        owner = 0;
        if (TurncoatRules.IsActive(me)) return -1;
        int chosen = -1;
        for (int s = 0; s < hearts.Sites.Count; s++)
        {
            HeartSites.Site site = hearts.Sites[s];
            if (!site.Standing || !CombatRules.IsHostile(world, me.OwnerPlayerId, site.Owner)) continue;
            if (CombatRules.DistanceSquared(me.PosX, me.PosY, site.CentreX, site.CentreY) > sightSq) continue;
            if (!LineOfSight.Clear(sight, me.PosX, me.PosY, site.CentreX, site.CentreY)) continue;

            for (int i = 0; i < site.RingTiles.Length; i++)
            {
                int tile = site.RingTiles[i];
                int d = CombatRules.DistanceSquared(me.PosX, me.PosY, TileCentreX(world, tile), TileCentreY(world, tile));
                if (d >= best) continue;
                best = d;
                chosen = tile;
                owner = site.Owner;
            }
        }
        return chosen;
    }

    public static int TileCentreX(WorldState world, int tileIndex) => CreatureState.CentreOf(tileIndex % world.Width);
    public static int TileCentreY(WorldState world, int tileIndex) => CreatureState.CentreOf(tileIndex / world.Width);

    /// <summary><c>0x0045AFF0</c>: not a distance but a route. Asks the mesh for a path to the
    /// point and sums its legs with the octile length; true when the squared length is under
    /// <paramref name="rangeSq"/>. Standing on the point, or an empty route, is true; no route is
    /// false. The Imp respects doors (<c>0x80</c>), everyone else routes with 0 (<c>0x0045B03E</c>, ADR-0332).</summary>
    public static bool CanReach(
        WorldSteering navigation, CreatureState me, CreatureDef definition, int toX, int toY, int rangeSq, List<Fixed16Vector> scratch) =>
        CanReach(navigation, me, definition, toX, toY, rangeSq, scratch,
            me.CreatureTypeId == CombatRules.ImpCreatureTypeId ? MoveOptions.Loose : MoveOptions.Plain);

    /// <summary>The same with the caller's route word. The fight's own tests use the Imp's 0 or
    /// 0x80, which refuses a foreign door; a hero job's arm passes its own word, 3 for StealGold
    /// (<c>0x00471740(room, 3)</c>), which routes through one (door-blocking.md, ADR-0324).</summary>
    public static bool CanReach(
        WorldSteering navigation, CreatureState me, CreatureDef definition, int toX, int toY, int rangeSq,
        List<Fixed16Vector> scratch, Navigation.MoveOptions options)
    {
        if (me.PosX == toX && me.PosY == toY) return true;

        scratch.Clear();
        bool routed = navigation.Route(
            new Fixed16Vector(me.PosX << 4, me.PosY << 4),
            new Fixed16Vector(toX << 4, toY << 4),
            WorldSteering.ProfileOf(definition, me.OwnerPlayerId, options),
            scratch,
            refresh: false);
        if (!routed) return false;
        if (scratch.Count == 0) return true;

        int length = 0, px = me.PosX, py = me.PosY;
        for (int i = 0; i < scratch.Count; i++)
        {
            int qx = scratch[i].X >> 4, qy = scratch[i].Y >> 4;
            int dx = Math.Abs(qx - px), dy = Math.Abs(qy - py);
            length += dx + dy - (Math.Min(dx, dy) >> 1);
            px = qx;
            py = qy;
        }
        return unchecked((length >> 12) * length) < rangeSq;
    }

    /// <summary>Commit and start (the fight-starter, combat-acquisition.md §6.2 step 3): the target
    /// and its attack type on me — melee within a tile unless I am <c>Support</c>, else the surveyed
    /// attack spell with its range as the reach, else melee — my id in the target's melee or ranged
    /// slots, and the state. A target with all eight slots of that kind taken refuses the
    /// engagement and leaves me as I was.</summary>
    private static bool Engage(
        SimulationContext context, CreatureState me, CreatureDef mine, CreatureState target, CreatureDef theirs,
        int spellSlot = -1, CreatureSpellDef? spell = null, bool forced = false)
    {
        int distance = CombatRules.DistanceSquared(me.PosX, me.PosY, target.PosX, target.PosY);
        bool useSpell = spell != null && (forced || !(distance < MeleeDecisionDistance && mine.FightStyle != FightStyle.Support));
        bool same = me.FightTargetKind == FightTargetKind.Creature && me.FightTargetId == target.Id;
        bool wasRanged = me.AttackType is CombatRules.AttackMode.SpellAtCreature or CombatRules.AttackMode.SpellAtThing;
        if (same && wasRanged != useSpell) CombatRules.Unregister(target, me.Id);
        if ((!same || wasRanged != useSpell) && !CombatRules.Register(target, me.Id, ranged: useSpell)) return false;

        // The fight-starter's 0x0049AA30(target, 6, 0, 0) after registration (combat-acquisition.md
        // §8). Ours fires on a new engagement only; DK2's fight-starter may run again for the same
        // target and fire again, which a one-shot event subtree would not notice.
        if (!same) context.FireCreatureEvent(target, CreatureTriggerEvent.Attacked);

        me.FightTargetKind = FightTargetKind.Creature;
        me.FightTargetId = target.Id;
        me.FightTargetPlayerId = 0;
        me.FightTargetTileIndex = -1;
        if (useSpell)
        {
            me.AttackType = CombatRules.AttackMode.SpellAtCreature;
            me.AttackSpellSlot = (byte)spellSlot;
            me.AttackRangeSquared = SpellRules.RangeSquared(spell!);
        }
        else
        {
            me.AttackType = CombatRules.AttackMode.MeleeAtCreature;
            me.AttackSpellSlot = 0;
            me.AttackRangeSquared = CombatRules.MeleeReachSquared(mine, theirs);
        }
        Enter(context, me, mine, same, $"target={target.Id} attack={(byte)me.AttackType}");
        return true;
    }

    /// <summary>The heart as a thing target, through the commit's class-blind choice
    /// (<c>0x0045DE6E</c>): melee within a tile unless a Support, else a spell at a thing when the
    /// surveyed spell deals damage (a status spell takes melee against a thing, combat-spells.md §2),
    /// else melee at an object's reach — the pillar is class 2, so half a tile (<c>0x0045E098</c>); no slots.</summary>
    private static bool EngageHeart(
        SimulationContext context, CreatureState me, CreatureDef mine, PlayerId owner, int tile, int spellSlot = -1, CreatureSpellDef? spell = null)
    {
        bool same = me.FightTargetKind == FightTargetKind.Heart && me.FightTargetTileIndex == tile;
        int distance = CombatRules.DistanceSquared(me.PosX, me.PosY, TileCentreX(context.World, tile), TileCentreY(context.World, tile));
        bool useSpell = spell != null && !SpellRules.IsStatus(spell)
            && !(distance < MeleeDecisionDistance && mine.FightStyle != FightStyle.Support);
        me.FightTargetKind = FightTargetKind.Heart;
        me.FightTargetId = CombatRules.NoTarget;
        me.FightTargetPlayerId = owner.Value;
        me.FightTargetTileIndex = tile;
        if (useSpell)
        {
            me.AttackType = CombatRules.AttackMode.SpellAtThing;
            me.AttackSpellSlot = (byte)spellSlot;
            me.AttackRangeSquared = SpellRules.RangeSquared(spell!);
        }
        else
        {
            me.AttackType = CombatRules.AttackMode.MeleeAtThing;
            me.AttackSpellSlot = 0;
            me.AttackRangeSquared = CombatRules.ReachSquared(mine.MeleeRange + new Fp32(CombatRules.ThingReach));
        }
        Enter(context, me, mine, same, $"heart={owner} tile={tile} attack={(byte)me.AttackType}");
        return true;
    }

    private static void Enter(SimulationContext context, CreatureState me, CreatureDef mine, bool sameTarget, string target)
    {
        // Ours: the state change drops the route unless the target is the one already approached.
        if (!sameTarget) me.StopMoving();
        if (!CombatRules.IsFighting(me)) me.CombatStateEnteredTick = context.Tick.Value;
        me.Running = true; // state 199's entry sets the run speed (0x004887D0(1))
        CombatStates.Enter(context, me, CombatStates.Engaging);
        context.Emit("combat", "engaged",
            $"creature={me.Id} type={mine.Name} player={me.OwnerPlayerId} {target} reach2={me.AttackRangeSquared}");
    }
}
