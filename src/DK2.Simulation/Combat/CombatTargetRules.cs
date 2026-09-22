// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Combat;

/// <summary>Where the enemy hearts stand this tick, for the acquirer's heart scan: each hostile
/// keeper's heart centre and the heart tiles round it that stand in for its Arch Pillars (Q363).
/// Built once a tick by <see cref="Systems.CombatSystem"/>.</summary>
public sealed class HeartSites
{
    public readonly struct Site
    {
        public Site(byte owner, int centreX, int centreY, int[] ringTiles)
        {
            Owner = owner;
            CentreX = centreX;
            CentreY = centreY;
            RingTiles = ringTiles;
        }

        public byte Owner { get; }

        /// <summary>The heart's centre tile, as a position: tile centre in 1/4096 units.</summary>
        public int CentreX { get; }
        public int CentreY { get; }
        public int[] RingTiles { get; }
    }

    private readonly List<Site> _sites = [];

    public IReadOnlyList<Site> Sites => _sites;

    /// <summary>Every live, unruined heart of a keeper still playing: the bounding box of its tiles
    /// gives the centre and the box's border the ring.</summary>
    public void Refresh(WorldState world, GameDatabase database)
    {
        _sites.Clear();
        byte? heartTerrain = ManaRules.DungeonHeartTerrainId(database);
        if (heartTerrain is null) return;

        for (int p = 0; p < world.PlayerCount; p++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(p);
            if (!PlayerIds.IsKeeper(player.PlayerId) || player.HeartRuined || player.HeartHealth <= 0) continue;

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
            _sites.Add(new Site(player.PlayerId, centreX, centreY, ring.ToArray()));
        }
    }
}

/// <summary>DK2's <c>SelectAVictim</c> (<c>0x0045CB00</c>, its own name) for the four fighting arms:
/// the shared scan over every creature in the game with each arm's score, the acceptance by sight
/// or by a walkable route, the heart scan (§6 scan E), the route check on the winner, the final
/// gate, and the fight-starter that registers the engagement and sets the reach
/// (docs/research/combat-acquisition.md §6–§8). Not built and named there: the NonFighter arm, the
/// spell survey and the spell arms, the scan over who is already attacking me, the door scan
/// and the flee check. Every engagement made here is melee.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-acquisition.md")]
public static class CombatTargetRules
{
    /// <summary>One tile squared in the scans' units: closer than this, a creature fights hand to hand.</summary>
    public const int MeleeDecisionDistance = 0x1000;

    /// <summary>Whether this simulation runs an arm for the style yet.</summary>
    public static bool IsBuilt(FightStyle style) => style != FightStyle.NonFighter;

    /// <summary>Finds and engages a target for <paramref name="me"/>. True when it now fights.</summary>
    public static bool SelectAVictim(
        SimulationContext context, ISightBlocker sight, HeartSites hearts, CreatureState me, CreatureDef definition,
        List<Fixed16Vector> scratch)
    {
        if (!IsBuilt(definition.FightStyle)) return false;

        WorldState world = context.World;
        GameDatabase database = context.Database;
        int sightSq = CombatRules.RangeSquared(definition.DistanceCanSee);
        int hearSq = CombatRules.RangeSquared(definition.DistanceCanHear);

        int best = int.MaxValue;
        CreatureState? chosen = Scan(context, sight, me, definition, sightSq, hearSq, scratch, ref best);

        // Scan E: the enemy heart outranks nothing but a worker, and only an Imp or a Horned
        // Reaper never looks at it.
        bool heartMayEnter = chosen is null
            || database.CreatureById(chosen.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker);
        int heartTile = -1;
        byte heartOwner = 0;
        if (heartMayEnter && me.CreatureTypeId != CombatRules.ImpCreatureTypeId && !definition.Flags.HasFlag(CreatureFlags.IsHorny))
            heartTile = ScanHearts(world, sight, hearts, me, sightSq, ref best, out heartOwner);

        if (heartTile >= 0)
        {
            if (!CanReach(context.Navigation, me, definition, TileCentreX(world, heartTile), TileCentreY(world, heartTile), int.MaxValue, scratch))
                return false;
            return EngageHeart(context, me, definition, heartOwner, heartTile);
        }

        if (chosen is null) return false;

        // The winner must be walkable to; a creature with an attack spell may keep one it can only
        // see, and none here has one, so the route decides.
        int distance = CombatRules.DistanceSquared(me.PosX, me.PosY, chosen.PosX, chosen.PosY);
        if (!CanReach(context.Navigation, me, definition, chosen.PosX, chosen.PosY, int.MaxValue, scratch)) return false;

        // The last gate, on a third of the distance: within hearing, or within sight and seen.
        int third = distance / 3;
        if (third >= hearSq && (third >= sightSq || !LineOfSight.Clear(sight, me.PosX, me.PosY, chosen.PosX, chosen.PosY)))
            return false;

        return Engage(context, me, definition, chosen, database.CreatureById(chosen.CreatureTypeId));
    }

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
            if (!CombatRules.IsHostile(world, me.OwnerPlayerId, candidate.OwnerPlayerId)) continue;

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
    /// Pillar objects; the ring tile is the stand-in until they are placed (Q363).</summary>
    private static int ScanHearts(
        WorldState world, ISightBlocker sight, HeartSites hearts, CreatureState me, int sightSq, ref int best, out byte owner)
    {
        owner = 0;
        int chosen = -1;
        for (int s = 0; s < hearts.Sites.Count; s++)
        {
            HeartSites.Site site = hearts.Sites[s];
            if (!CombatRules.IsHostile(world, me.OwnerPlayerId, site.Owner)) continue;
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
    /// false. Imps route with no door rule, everyone else respects doors.</summary>
    public static bool CanReach(
        WorldSteering navigation, CreatureState me, CreatureDef definition, int toX, int toY, int rangeSq, List<Fixed16Vector> scratch)
    {
        if (me.PosX == toX && me.PosY == toY) return true;

        int options = me.CreatureTypeId == CombatRules.ImpCreatureTypeId ? MoveOptions.Plain : MoveOptions.Loose;
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

    /// <summary>Commit and start: the target and its attack type on me — melee, since the spell
    /// survey is not built — my id in the target's melee slots, the reach, and the state. A target
    /// with all eight melee slots taken refuses the engagement and leaves me as I was.</summary>
    private static bool Engage(SimulationContext context, CreatureState me, CreatureDef mine, CreatureState target, CreatureDef theirs)
    {
        bool same = me.FightTargetKind == FightTargetKind.Creature && me.FightTargetId == target.Id;
        if (!same && !CombatRules.Register(target, me.Id, ranged: false)) return false;

        me.FightTargetKind = FightTargetKind.Creature;
        me.FightTargetId = target.Id;
        me.FightTargetPlayerId = 0;
        me.FightTargetTileIndex = -1;
        me.AttackType = CombatRules.AttackTypes.MeleeAtCreature;
        me.AttackRangeSquared = CombatRules.MeleeReachSquared(mine, theirs);
        Enter(context, me, mine, same, $"target={target.Id}");
        return true;
    }

    /// <summary>The heart as a thing target: melee at a thing, an object's reach, no slots.</summary>
    private static bool EngageHeart(SimulationContext context, CreatureState me, CreatureDef mine, byte owner, int tile)
    {
        bool same = me.FightTargetKind == FightTargetKind.Heart && me.FightTargetTileIndex == tile;
        me.FightTargetKind = FightTargetKind.Heart;
        me.FightTargetId = CombatRules.NoTarget;
        me.FightTargetPlayerId = owner;
        me.FightTargetTileIndex = tile;
        me.AttackType = CombatRules.AttackTypes.MeleeAtThing;
        me.AttackRangeSquared = CombatRules.ReachSquared(mine.MeleeRange.Raw + CombatRules.ObjectReach);
        Enter(context, me, mine, same, $"heart={owner} tile={tile}");
        return true;
    }

    private static void Enter(SimulationContext context, CreatureState me, CreatureDef mine, bool sameTarget, string target)
    {
        // Ours: the state change drops the route unless the target is the one already approached.
        if (!sameTarget) me.StopMoving();
        me.Activity = CreatureActivity.Fighting;
        context.Emit("combat", "engaged",
            $"creature={me.Id} type={mine.Name} player={me.OwnerPlayerId} {target} reach2={me.AttackRangeSquared}");
    }
}
