// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation.Combat;

/// <summary>Fear and flight — docs/research/combat-fear.md, all DK2's own names from its dumper:
/// every tick a creature radiates its level-scaled <c>Threat</c>, split among its engagements,
/// into the <c>TempFear</c> of each opponent it can see (<c>0x0045E4F0</c>); a creature's own
/// <c>CurrentFear</c> is last tick's intake scaled by its distance from its heart. In the fight
/// state, <c>OverThreatCounter</c> climbs while <c>CurrentFear</c> exceeds the level-scaled
/// <c>Fear</c> stat and at three the creature backs off for <c>BackOffTimer</c> ticks (state 203),
/// then runs for a lair, its heart, a room or a nearby tile (the withdraw state,
/// <c>FightWithDrawState.cpp</c>). A creature below a third of its health and far from its heart
/// withdraws at once. Not built and named there: the Scout's alarm, the hero gates, the casino's
/// fearlessness, the per-type player modifiers and the steering impulse of the step back.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/combat-fear.md")]
public static class FearRules
{
    private const string SystemName = "combat";

    /// <summary>A creature farther than this from its heart, in tiles, may withdraw when hurt.</summary>
    public const int WithdrawHeartDistance = 5;

    /// <summary>The distance factor's cap: ten tiles from the heart is full fear.</summary>
    public const int FullFearDistance = 10;

    /// <summary>The counter's flee mark and its two clamps.</summary>
    public const int FleeCount = 3;
    private const int CounterReset = 5;

    /// <summary>The head-count's radius, five tiles, squared in the scans' units.</summary>
    public const int HeadCountRadiusSquared = 0x19000;

    /// <summary>The back-off keeps at least a tile's reach from its attacker, and five quarters of it.</summary>
    public const int MinimumStandOffSquared = 0x4000;

    /// <summary>The axis fall-back of the step back, three quarters of a tile (<c>0x3000</c>), and here its length.</summary>
    public const int StepBackLength = 0x3000;

    /// <summary>The withdraw walk's option words: the exact spot, then the same with no door rule.</summary>
    private const Navigation.MoveOptions WithdrawFallbackOptions = (Navigation.MoveOptions)0x68;

    private static readonly (int Dx, int Dy)[] Neighbours =
        [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    // ------------------------------------------------------------------ the accumulator

    /// <summary><c>0x0045E4F0</c>, every tick after the creature's fight processing: hand last
    /// tick's intake over, scale it by the heart distance, then split my threat among everyone I
    /// fight and add each visible one's share to its intake. A stunned creature or a chicken
    /// neither fears nor radiates; here a chicken and a fallen one are skipped, a stunned one not yet.</summary>
    public static void Radiate(SimulationContext context, ISightBlocker sight, HeartSites hearts, CreatureState me, CreatureDef mine)
    {
        if (CreatureDeathRules.IsDown(me) || ChickenRules.IsChicken(me)) return;
        WorldState world = context.World;

        int d = Math.Min(FullFearDistance, HeartDistance(world, hearts, me));
        me.CurrentFear = (ushort)(me.TempFear * d / FullFearDistance);
        me.TempFear = 0;

        int engagements = CombatRules.AttackerCount(me) + (me.FightTargetKind != FightTargetKind.None ? 1 : 0);
        if (engagements == 0) return;
        int share = ThreatAtLevel(context.Database, mine, me.Level) / engagements;

        if (me.FightTargetKind == FightTargetKind.Creature) Give(world, sight, me, me.FightTargetId, share);
        for (int i = 0; i < CombatRules.Slots; i++) Give(world, sight, me, me.EngagedMelee[i], share);
        for (int i = 0; i < CombatRules.Slots; i++) Give(world, sight, me, me.EngagedRanged[i], share);
    }

    private static void Give(WorldState world, ISightBlocker sight, CreatureState me, int id, int share)
    {
        if (id == CombatRules.NoTarget) return;
        CreatureState? them = world.CreatureById(id);
        if (them is null || CreatureDeathRules.IsDown(them)) return;
        if (!LineOfSight.Clear(sight, me.PosX, me.PosY, them.PosX, them.PosY)) return;
        them.TempFear = (ushort)(them.TempFear + share);
    }

    /// <summary>The level-scaled <c>Threat</c> stat, a creature's weight in every head-count.</summary>
    public static int ThreatAtLevel(GameDatabase database, CreatureDef definition, int level) =>
        CreatureLevelRules.Scale(definition.Threat, CreatureLevelRules.Percent(database, CreatureStatId.Threat, level));

    /// <summary>The level-scaled <c>Fear</c> stat: the threshold <c>CurrentFear</c> is measured against.</summary>
    public static int FearThreshold(GameDatabase database, CreatureDef definition, int level) =>
        CreatureLevelRules.Scale(definition.Fear, CreatureLevelRules.Percent(database, CreatureStatId.Fear, level));

    /// <summary>A heartless player's <c>DungeonHeartPosition</c>: the player initialiser
    /// <c>0x004B8640</c> stores 1 in both words (<c>0x004B883E</c>, <c>0x004B8845</c>), and only the
    /// heart setter <c>0x004C5F60</c> writes them again (Q372).</summary>
    public const int NoHeartTile = 1;

    /// <summary>Chebyshev tiles from my tile to my owner's <c>DungeonHeartPosition</c>. A player
    /// with no heart — the heroes, the neutral player — keeps <see cref="NoHeartTile"/> on both axes.</summary>
    public static int HeartDistance(WorldState world, HeartSites hearts, CreatureState me)
    {
        int hx = NoHeartTile, hy = NoHeartTile;
        HeartSites.Site? site = hearts.SiteOf(me.OwnerPlayerId);
        if (site.HasValue)
        {
            hx = site.Value.CentreX / Fp32.One;
            hy = site.Value.CentreY / Fp32.One;
        }
        TilePos tile = me.Tile;
        return Math.Max(Math.Abs(tile.X - hx), Math.Abs(tile.Y - hy));
    }

    // ------------------------------------------------------------------ the two checks

    /// <summary><c>0x0045EDA0</c>: hurt below a third of my level-scaled health, more than five tiles
    /// from my heart, not fearless (by definition or a Casino win) and not a Combat Pit fighter. A hero
    /// counts as six tiles away only with a creature flag this simulation does not carry, so here a
    /// hero never withdraws (Q372).</summary>
    public static bool ShouldWithdraw(WorldState world, GameDatabase database, HeartSites hearts, CreatureState me, CreatureDef mine)
    {
        if (me.CombatPitFighter || mine.Flags.HasFlag(CreatureFlags.IsFearless)
            || CasinoRules.IsFearlessFromGambling(world, me)) return false;
        int d = me.OwnerPlayerId == PlayerIds.Good ? 0 : HeartDistance(world, hearts, me);
        if (d <= WithdrawHeartDistance) return false;
        return me.Health < CreatureLevelRules.MaxHealth(database, mine, me.Level) / 3;
    }

    private static long Isqrt(long value)
    {
        if (value <= 0) return 0;
        long root = (long)Math.Sqrt(value);
        while (root * root > value) root--;
        while ((root + 1) * (root + 1) <= value) root++;
        return root;
    }

    /// <summary><c>0x0045EC80</c>, the counter: clamped, cleared for the fearless and a pit fighter, up by one each
    /// tick <c>CurrentFear</c> exceeds the threshold and back to zero when it does not; a
    /// <c>NonFighter</c> with any attacker that is not an Imp (<c>0x0045EC10</c>) jumps to three.
    /// True at three or more. An Imp fighting an Imp is not forced out (ADR-0347).</summary>
    public static bool OverThreat(WorldState world, GameDatabase database, CreatureState me, CreatureDef mine)
    {
        if (me.OverThreatCounter > CounterReset) me.OverThreatCounter = 0;
        if (me.OverThreatCounter > FleeCount) me.OverThreatCounter = FleeCount;
        if (me.CombatPitFighter || mine.Flags.HasFlag(CreatureFlags.IsFearless)
            || CasinoRules.IsFearlessFromGambling(world, me))
        {
            me.OverThreatCounter = 0;
            return false;
        }

        if (me.CurrentFear > FearThreshold(database, mine, me.Level)) me.OverThreatCounter++;
        else me.OverThreatCounter = 0;

        if (mine.FightStyle == FightStyle.NonFighter && NonImpAmongAttackers(world, me)) me.OverThreatCounter = FleeCount;
        return me.OverThreatCounter >= FleeCount;
    }

    /// <summary><c>0x0045EC10</c>: true when a melee or ranged slot, in slot order, holds a live
    /// thing whose creature type is not the Imp (<c>+0x36F != 1</c>).</summary>
    public static bool NonImpAmongAttackers(WorldState world, CreatureState me)
    {
        for (int i = 0; i < CombatRules.Slots; i++)
        {
            if (IsNonImp(world, me.EngagedMelee[i]) || IsNonImp(world, me.EngagedRanged[i])) return true;
        }
        return false;

        static bool IsNonImp(WorldState world, int id) =>
            id != CombatRules.NoTarget && world.CreatureById(id) is { } attacker
            && attacker.CreatureTypeId != CombatRules.ImpCreatureTypeId;
    }

    /// <summary><c>0x00460C50</c>: true when every neighbouring tile I can walk to holds a hostile
    /// creature that is attacking me — no tile to back on to. The fear checks are skipped while
    /// this holds, and a back-off that cannot step turns and fights.</summary>
    public static bool Cornered(SimulationContext context, CreatureState me, CreatureDef mine, List<Fixed16Vector> scratch)
    {
        WorldState world = context.World;
        TilePos tile = me.Tile;
        for (int n = 0; n < Neighbours.Length; n++)
        {
            int x = tile.X + Neighbours[n].Dx, y = tile.Y + Neighbours[n].Dy;
            if (!world.Contains(x, y)) continue;
            if (!CombatTargetRules.CanReach(context.Navigation, me, mine, CreatureState.CentreOf(x), CreatureState.CentreOf(y), int.MaxValue, scratch))
                continue;
            if (!HeldByAnAttacker(world, me, x, y)) return false;
        }
        return true;
    }

    private static bool HeldByAnAttacker(WorldState world, CreatureState me, int x, int y)
    {
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState c = creatures[i];
            if (c.Id == me.Id || CreatureDeathRules.IsDown(c)) continue;
            TilePos t = c.Tile;
            if (t.X != x || t.Y != y) continue;
            if (!CombatRules.IsHostile(world, me.OwnerPlayerId, c.OwnerPlayerId)) continue;
            if (c.FightTargetKind == FightTargetKind.Creature && c.FightTargetId == me.Id) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ the flight

    /// <summary>The fear checks of <c>spFighting</c> step 6, run only when an escape tile exists:
    /// withdraw if hurt and the state is a tick old, else back off if over threat. True when the
    /// creature has left the fight state; a withdrawal ends its swing, a flight does not.</summary>
    public static bool FearChecks(
        SimulationContext context, ISightBlocker sight, HeartSites hearts, RoomCache rooms, CreatureState me, CreatureDef mine,
        List<Fixed16Vector> scratch)
    {
        if (Cornered(context, me, mine, scratch)) return false;
        long tick = context.Tick.Value;
        if (ShouldWithdraw(context.World, context.Database, hearts, me, mine) && tick != me.CombatStateEnteredTick
            && Withdraw(context, sight, hearts, rooms, me, mine, scratch))
            return true;
        if (!OverThreat(context.World, context.Database, me, mine)) return false;
        EnterFlee(context, me, mine);
        return true;
    }

    /// <summary>Into state 203 with <c>BackOffTimer</c> set: between the two BackOff variables,
    /// shorter the more <c>CurrentFear</c> exceeds the threshold, and zero — run at once — for a
    /// zero threshold. From the fight state (<c>0x0045F030</c>) also zero for no target, a
    /// <c>NonFighter</c>, or a creature target that runs faster; from the approach state
    /// (<c>0x00470E30</c>) only for an <c>AlwaysFlee</c> definition (ADR-0347).</summary>
    public static void EnterFlee(SimulationContext context, CreatureState me, CreatureDef mine, bool fromApproach = false)
    {
        GameDatabase database = context.Database;
        int threshold = FearThreshold(database, mine, me.Level);
        byte timer = 0;
        if (threshold != 0)
        {
            int q = Math.Min(4, me.CurrentFear / threshold);
            byte min = (byte)database.Variables[VariableTypeId.BackOffMinimumDurationSeconds];
            byte max = (byte)database.Variables[VariableTypeId.BackOffMaximumDurationSeconds];
            timer = (byte)(min + (byte)((byte)(max - min) * (4 - q)));
        }

        if (fromApproach)
        {
            if (mine.Flags.HasFlag(CreatureFlags.AlwaysFlee)) timer = 0;
        }
        else if (me.FightTargetKind == FightTargetKind.None || mine.FightStyle == FightStyle.NonFighter) timer = 0;
        else if (me.FightTargetKind == FightTargetKind.Creature)
        {
            CreatureState? target = context.World.CreatureById(me.FightTargetId);
            if (target != null && !CreatureDeathRules.IsDown(target)
                && RunPerTick(database.CreatureById(target.CreatureTypeId)) > RunPerTick(mine))
                timer = 0;
        }

        me.BackOffTimer = timer;
        CombatStates.Enter(context, me, CombatStates.Fleeing);
        me.CombatStateEnteredTick = context.Tick.Value;
        context.Emit(SystemName, "flees",
            $"creature={me.Id} type={mine.Name} fear={me.CurrentFear} threshold={threshold} backoff={timer}");
    }

    /// <summary>The definition's run speed per tick, as the loader keeps it (<c>def+0x5B8</c>).</summary>
    public static int RunPerTick(CreatureDef definition) => SimulationTiming.LoaderRatePerTick(definition.RunSpeed.Raw);

    /// <summary>State 203 (<c>0x004602E0</c>), each tick: while <c>BackOffTimer</c> runs, hold the
    /// nearest attacker at five quarters of its reach, stepping back when it closes, and return to
    /// the fight the tick the counter calms; when the timer is out, run (<see cref="FleeWalk"/>).</summary>
    public static void Flee(
        SimulationContext context, ISightBlocker sight, HeartSites hearts, RoomCache rooms, CreatureState me, CreatureDef mine,
        List<Fixed16Vector> scratch)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        long tick = context.Tick.Value;

        if (me.FightTargetKind == FightTargetKind.Creature && world.CreatureById(me.FightTargetId) is null) ClearTarget(world, me);

        if (me.BackOffTimer == 0)
        {
            FleeWalk(context, sight, hearts, rooms, me, mine, scratch);
            return;
        }
        me.BackOffTimer--;

        if (me.FightTargetKind != FightTargetKind.None && !FightRules.TargetIsValid(world, me)) ClearTarget(world, me);

        if (ShouldWithdraw(world, database, hearts, me, mine) && tick != me.CombatStateEnteredTick
            && Withdraw(context, sight, hearts, rooms, me, mine, scratch))
            return;

        bool afraid = OverThreat(world, database, me, mine);
        if (!afraid || me.FightTargetKind == FightTargetKind.None)
        {
            FightOn(context, me, "calm");
            return;
        }

        if (!CombatRules.MayLookForAFight(world, context.Database, me, force: false))
        {
            End(context, me, "cannot_continue");
            return;
        }

        CreatureState? target = me.FightTargetKind == FightTargetKind.Creature ? world.CreatureById(me.FightTargetId) : null;
        if (me.FightTargetKind == FightTargetKind.Creature && target is null) return;

        CreatureState? nearest = NearestAttacker(context, sight, me, mine, scratch);
        if (nearest != null && (me.FightTargetKind != FightTargetKind.Creature || nearest.Id != me.FightTargetId))
        {
            ClearTarget(world, me);
            if (!CombatRules.Register(nearest, me.Id, ranged: false))
            {
                End(context, me, "slots_full");
                return;
            }
            me.FightTargetKind = FightTargetKind.Creature;
            me.FightTargetId = nearest.Id;
            me.AttackType = CombatRules.AttackMode.MeleeAtCreature;
            target = nearest;
        }

        if (!FightRules.TargetPosition(world, me, out int tx, out int ty, out _)) return;
        int d = CombatRules.DistanceSquared(me.PosX, me.PosY, tx, ty);
        int range = MinimumStandOffSquared;
        if (target != null && CombatRules.InCombat(target)) range = Math.Max(range, target.AttackRangeSquared);
        range = (5 * range) >> 2;

        if (d > range)
        {
            me.Running = true;
            return;
        }
        if (StepBack(context, me, mine, tx, ty, scratch))
        {
            me.Running = false;
            return;
        }
        if (Cornered(context, me, mine, scratch))
        {
            FightOn(context, me, "cornered");
            return;
        }
        me.BackOffTimer = 0;
    }

    /// <summary>The timer is out. A keeper's creature within <c>GuardRoomReportingDistanceTiles</c>
    /// of one of its rooms counts heads: the state ends when its side, at three quarters, outweighs
    /// the enemy's within five tiles, and otherwise a Scout would raise the alarm — not built, so
    /// the state ends too (Q374). Anyone else lets go and withdraws.</summary>
    private static void FleeWalk(
        SimulationContext context, ISightBlocker sight, HeartSites hearts, RoomCache rooms, CreatureState me, CreatureDef mine,
        List<Fixed16Vector> scratch)
    {
        WorldState world = context.World;
        CreatureState? target = me.FightTargetKind == FightTargetKind.Creature ? world.CreatureById(me.FightTargetId) : null;

        if (PlayerIds.IsKeeper(me.OwnerPlayerId) && target != null && !CreatureDeathRules.IsDown(target) && NearOwnRooms(context, rooms, me))
        {
            int theirs = LocalStrength(context, target.OwnerPlayerId, me.PosX, me.PosY);
            int ours = LocalStrength(context, me.OwnerPlayerId, me.PosX, me.PosY) * 3 / 4;
            End(context, me, ours > theirs ? "outnumbers" : "no_alarm");
            return;
        }

        ClearTarget(world, me);
        if (Withdraw(context, sight, hearts, rooms, me, mine, scratch)) return;
        End(context, me, "nowhere_to_go");
    }

    /// <summary><c>0x00460B50</c>'s room test: my tile strictly inside a room of mine grown by the
    /// reporting distance on every side.</summary>
    public static bool NearOwnRooms(SimulationContext context, RoomCache rooms, CreatureState me)
    {
        int g = context.Database.Variables[VariableTypeId.GuardRoomReportingDistanceTiles];
        TilePos t = me.Tile;
        foreach (RoomInstance room in rooms.Of(context, me.OwnerPlayerId))
        {
            if (t.X > room.MinX - g && t.X < room.MaxX + g && t.Y > room.MinY - g && t.Y < room.MaxY + g) return true;
        }
        return false;
    }

    /// <summary><c>0x004FAC40</c> over creatures: the summed level-scaled <c>Threat</c> of a player's
    /// standing creatures within five tiles of a point.</summary>
    public static int LocalStrength(SimulationContext context, PlayerId playerId, int x, int y)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int sum = 0;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState c = creatures[i];
            if (c.OwnerPlayerId != playerId || CreatureDeathRules.IsDown(c)) continue;
            if (CombatRules.DistanceSquared(c.PosX, c.PosY, x, y) >= HeadCountRadiusSquared) continue;
            sum += ThreatAtLevel(database, database.CreatureById(c.CreatureTypeId), c.Level);
        }
        return sum;
    }

    /// <summary><c>0x0045C690</c>: of those attacking me, the nearest — an eighth nearer across the
    /// alignments — that is attackable and either seen within sight or reached within hearing.</summary>
    public static CreatureState? NearestAttacker(
        SimulationContext context, ISightBlocker sight, CreatureState me, CreatureDef mine, List<Fixed16Vector> scratch)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        bool imp = me.CreatureTypeId == CombatRules.ImpCreatureTypeId;
        int sightSq = imp ? CombatTargetRules.MeleeDecisionDistance : CombatRules.RangeSquared(mine.DistanceCanSee);
        int hearSq = imp ? 0 : CombatRules.RangeSquared(mine.DistanceCanHear);

        int best = int.MaxValue;
        CreatureState? chosen = null;
        for (int i = 0; i < 2 * CombatRules.Slots; i++)
        {
            int id = i < CombatRules.Slots ? me.EngagedMelee[i] : me.EngagedRanged[i - CombatRules.Slots];
            if (id == CombatRules.NoTarget) continue;
            CreatureState? c = world.CreatureById(id);
            if (c is null) continue;

            int d = CombatRules.DistanceSquared(me.PosX, me.PosY, c.PosX, c.PosY);
            if (CombatRules.OppositeAlignment(mine, database.CreatureById(c.CreatureTypeId))) d = CombatRules.Discounted(d);
            if (d >= best || !CombatRules.IsAttackable(c)) continue;

            bool seen = d < sightSq && LineOfSight.Clear(sight, me.PosX, me.PosY, c.PosX, c.PosY);
            if (!seen && !(d < hearSq && CombatTargetRules.CanReach(context.Navigation, me, mine, c.PosX, c.PosY, hearSq, scratch))) continue;
            if (!CombatTargetRules.CanReach(context.Navigation, me, mine, c.PosX, c.PosY, int.MaxValue, scratch)) continue;

            best = d;
            chosen = c;
        }
        return chosen;
    }

    /// <summary><c>0x00460DA0</c>: a step straight back from the target when a route exists and one
    /// of the two axis tiles is not solid; failing that, along whichever axis routes. DK2 gives the
    /// body an impulse of twenty-four ticks of shuffle along its heading; here, with no impulse in
    /// the steering, it is a short route three quarters of a tile away from the target (Q373).</summary>
    public static bool StepBack(SimulationContext context, CreatureState me, CreatureDef mine, int targetX, int targetY, List<Fixed16Vector> scratch)
    {
        WorldSteering navigation = context.Navigation;
        WorldState world = context.World;
        long dx = me.PosX - targetX, dy = me.PosY - targetY;
        long dist = Isqrt((dx * dx) + (dy * dy));
        if (dist == 0) return false;
        int vx = (int)(dx * StepBackLength / dist), vy = (int)(dy * StepBackLength / dist);

        int px = me.PosX + vx, py = me.PosY + vy;
        if (Passable(world, navigation, px, py)
            && CombatTargetRules.CanReach(navigation, me, mine, px, py, int.MaxValue, scratch)
            && (Passable(world, navigation, me.PosX, py) || Passable(world, navigation, px, me.PosY)))
            return navigation.TryRouteTo(me, px, py, MoveOptions.Loose);

        if (vy != 0 && Passable(world, navigation, me.PosX, py) && CombatTargetRules.CanReach(navigation, me, mine, me.PosX, py, int.MaxValue, scratch))
            return navigation.TryRouteTo(me, me.PosX, me.PosY + (vy > 0 ? StepBackLength : -StepBackLength), MoveOptions.Loose);
        if (vx != 0 && Passable(world, navigation, px, me.PosY) && CombatTargetRules.CanReach(navigation, me, mine, px, me.PosY, int.MaxValue, scratch))
            return navigation.TryRouteTo(me, me.PosX + (vx > 0 ? StepBackLength : -StepBackLength), me.PosY, MoveOptions.Loose);
        return false;
    }

    private static bool Passable(WorldState world, WorldSteering navigation, int x, int y)
    {
        int tx = x / Fp32.One, ty = y / Fp32.One;
        return x >= 0 && y >= 0 && world.Contains(tx, ty) && !NavBytes.IsSolid(navigation.NavByte(tx, ty));
    }

    // ------------------------------------------------------------------ the withdrawal

    /// <summary>The withdraw state (<c>0x004611D0</c>): a destination — my lair, else my heart two
    /// tiles off on each axis, else one of three random rooms of mine that is not a bridge, else a
    /// random neighbouring tile I can see — then a run to it, arriving idle. A hero would go to a
    /// hero gate; that walk is not built (gates themselves are, ADR-0320), so a hero never withdraws
    /// (Q372). True when the creature is on its way and out of the fight.</summary>
    public static bool Withdraw(
        SimulationContext context, ISightBlocker sight, HeartSites hearts, RoomCache rooms, CreatureState me, CreatureDef mine,
        List<Fixed16Vector> scratch)
    {
        if (me.OwnerPlayerId == PlayerIds.Good) return false;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        SimRandom rng = context.Rng;
        // The Imp with 0x80, everyone else with 0 (0x0046138B, 0x0046142C; ADR-0332).
        Navigation.MoveOptions options = me.CreatureTypeId == CombatRules.ImpCreatureTypeId ? MoveOptions.Loose : MoveOptions.Plain;

        int destX = -1, destY = -1;
        int bed = LairRules.BedTileIndex(world, database, me);
        if (bed >= 0)
        {
            destX = CombatTargetRules.TileCentreX(world, bed);
            destY = CombatTargetRules.TileCentreY(world, bed);
        }
        else
        {
            // 3. A random unruined heart of mine — 0x004C6460 counts them and draws random(count)
            // even for one — at its position word (+0x27/+0x29) two tiles off on each axis, each
            // sign a random(2), when the point routes. The heart's word sits one tile past its
            // centre (RoomInstance.CentreFieldX), so round a 5×5 heart three of the four points lie
            // in its wall and the draw falls through to a random room: on Level 3 DK2's imps landed
            // at the heart 6 times in 21 and at other rooms 15 (ADR-0323).
            IReadOnlyList<RoomInstance> mineRooms = rooms.Of(context, me.OwnerPlayerId);
            if (!world.Player(me.OwnerPlayerId).HeartRuined)
            {
                var heartsOfMine = new List<RoomInstance>();
                foreach (RoomInstance room in mineRooms)
                    if (room.Definition.TypeId == RoomTypeId.DungeonHeart) heartsOfMine.Add(room);
                if (heartsOfMine.Count > 0)
                {
                    RoomInstance heart = heartsOfMine[rng.NextInt(heartsOfMine.Count)];
                    int ox = rng.NextInt(2) != 0 ? -2 : 2;
                    int oy = rng.NextInt(2) != 0 ? -2 : 2;
                    int tx = heart.CentreFieldX + ox, ty = heart.CentreFieldY + oy;
                    if (world.Contains(tx, ty))
                    {
                        int x = CreatureState.CentreOf(tx), y = CreatureState.CentreOf(ty);
                        if (CombatTargetRules.CanReach(context.Navigation, me, mine, x, y, int.MaxValue, scratch))
                        {
                            destX = x;
                            destY = y;
                        }
                    }
                }
            }

            if (destX < 0)
            {
                for (int attempt = 0; attempt < 3 && destX < 0 && mineRooms.Count > 0; attempt++)
                {
                    RoomInstance? room = PickRoom(mineRooms, rng.NextInt(mineRooms.Count));
                    if (room is null) continue;
                    int x = CreatureState.CentreOf(room.CentreFieldX), y = CreatureState.CentreOf(room.CentreFieldY);
                    if (!CombatTargetRules.CanReach(context.Navigation, me, mine, x, y, int.MaxValue, scratch)) continue;
                    destX = x;
                    destY = y;
                }
            }

            if (destX < 0)
            {
                int attempts = me.FightTargetKind == FightTargetKind.Creature && world.CreatureById(me.FightTargetId) != null ? 2 : 5;
                for (int attempt = 0; attempt < attempts && destX < 0; attempt++)
                {
                    int x = me.PosX + ((rng.NextInt(3) - 1) * Fp32.One), y = me.PosY + ((rng.NextInt(3) - 1) * Fp32.One);
                    if (!Passable(world, context.Navigation, x, y) || !LineOfSight.Clear(sight, me.PosX, me.PosY, x, y)) continue;
                    destX = x;
                    destY = y;
                }
            }
        }
        if (destX < 0) return false;

        Navigation.MoveOptions withdrawOptions = MoveOptions.ExactSpot;
        if (!context.Navigation.TryRouteTo(me, destX, destY, withdrawOptions)
            && !context.Navigation.TryRouteTo(me, destX, destY, withdrawOptions = WithdrawFallbackOptions))
            return false;

        ClearTarget(world, me);
        me.SwingInProgress = false;
        me.CurrentJob = JobType.None;
        me.JobTileIndex = -1;
        me.Running = true;
        if (CombatStates.IntoTheGroup(context, me)) context.Navigation.TryRouteTo(me, destX, destY, withdrawOptions);
        CombatStates.Travel(context, me, CombatStates.Withdrawn);
        me.CombatStateEnteredTick = context.Tick.Value;
        FightReturnRules.Stamp(context, me);
        context.Emit(SystemName, "withdraws",
            $"creature={me.Id} type={mine.Name} health={me.Health} to={destX / Fp32.One},{destY / Fp32.One} lair={bed >= 0}");
        return true;
    }

    /// <summary><c>0x004C63D0</c>'s walk: the draw is over every room, bridges are skipped without
    /// counting down, so a draw that lands past the last non-bridge room finds nothing.</summary>
    private static RoomInstance? PickRoom(IReadOnlyList<RoomInstance> rooms, int draw)
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            if (BridgeRules.IsBridge(rooms[i].Definition)) continue;
            if (draw == 0) return rooms[i];
            draw--;
        }
        return null;
    }

    /// <summary>State 6 on the way to state 202, which sets the walk speed and ends at once: the
    /// creature stands idle where the route ended, or where it was lost.</summary>
    public static void Withdrawing(SimulationContext context, CreatureState me)
    {
        if (me.HasRoute) return;
        me.Running = false;
        CombatStates.Arrive(context, me);
        CombatStates.Idle(context.Hooks, me);
        context.Emit(SystemName, "withdrawn", $"creature={me.Id} tile={me.Tile.X},{me.Tile.Y}");
    }

    // ------------------------------------------------------------------ the alarm

    /// <summary>Ticks a creature stands at the threat's position before standing down (state 254).</summary>
    public const int ReportTicks = 30;

    /// <summary>The alarm is not raised twice: another responder within five tiles of the threat refuses it.</summary>
    private const int RespondingRadiusSquared = 0x19000;

    /// <summary><c>0x004752B0</c>: a <c>Scout</c> with Call to Arms off, when none of its owner's
    /// creatures already responds within five tiles of the threat, runs to its owner's nearest room
    /// carrying the threat's position and the strength still wanted there (combat-alarm.md §2).
    /// True when it is on its way and out of the fight.</summary>
    public static bool RaiseAlarm(
        SimulationContext context, RoomCache rooms, CreatureState me, CreatureDef mine, int threatX, int threatY, int deficit)
    {
        if (mine.JobClass != JobClass.Scout) return false;
        WorldState world = context.World;
        if (AlreadyResponding(world, me.OwnerPlayerId, threatX, threatY)) return false;

        RoomInstance? nearest = null;
        int best = int.MaxValue;
        foreach (RoomInstance room in rooms.Of(context, me.OwnerPlayerId))
        {
            int d = CombatRules.DistanceSquared(threatX, threatY, RoomCentreX(room), RoomCentreY(room));
            if (d >= best) continue;
            best = d;
            nearest = room;
        }
        if (nearest is null) return false;

        // +0x36F == 1 (the Imp) routes with 0x80, everyone else with 0 (0x0047542A, Q517).
        Navigation.MoveOptions options = me.CreatureTypeId == CombatRules.ImpCreatureTypeId ? MoveOptions.Loose : MoveOptions.Plain;
        if (!context.Navigation.TryRouteTo(me, RoomCentreX(nearest), RoomCentreY(nearest), options)) return false;

        ClearTarget(world, me);
        me.SwingInProgress = false;
        me.CurrentJob = JobType.None;
        me.JobTileIndex = -1;
        me.AlarmX = threatX;
        me.AlarmY = threatY;
        me.AlarmDeficit = deficit;
        me.AlarmRoomTile = nearest.TileIndices[0];
        me.Running = true;
        if (CombatStates.IntoTheGroup(context, me)) context.Navigation.TryRouteTo(me, RoomCentreX(nearest), RoomCentreY(nearest), options);
        CombatStates.Travel(context, me, CombatStates.RespondingToThreat);
        me.CombatStateEnteredTick = context.Tick.Value;
        context.Emit(SystemName, "raises_alarm",
            $"creature={me.Id} type={mine.Name} threat={threatX / Fp32.One},{threatY / Fp32.One} deficit={deficit} room={nearest.Definition.Name}");
        return true;
    }

    /// <summary><c>0x004754D0</c>: one of the owner's creatures already raising the alarm or
    /// reporting danger whose destination — or, standing, position — lies within five tiles.</summary>
    public static bool AlreadyResponding(WorldState world, PlayerId owner, int x, int y)
    {
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState c = creatures[i];
            if (c.OwnerPlayerId != owner || c.Activity is not (CreatureActivity.RaisingAlarm or CreatureActivity.Reporting)) continue;
            int cx = c.PosX, cy = c.PosY;
            if (c.HasRoute)
            {
                TilePos to = c.Destination;
                cx = CreatureState.CentreOf(to.X);
                cy = CreatureState.CentreOf(to.Y);
            }
            if (CombatRules.DistanceSquared(cx, cy, x, y) < RespondingRadiusSquared) return true;
        }
        return false;
    }

    /// <summary>State 6 on the way to 251: arriving, the Scout dispatches the room (<see cref="RespondToThreat"/>).</summary>
    public static void RaisingAlarm(SimulationContext context, RoomCache rooms, CreatureState me, CreatureDef mine)
    {
        if (me.HasRoute) return;
        RespondToThreat(context, rooms, me, mine);
    }

    /// <summary>State 251 (<c>0x00475660</c>), "responding to a threat": every creature of the owner
    /// working in the alarm's room — no worker, no Horned Reaper, not fighting, allowed to fight,
    /// above a quarter of its health — is sent running to the threat's position until their summed
    /// level-scaled <c>Threat</c> covers the deficit. The Scout then stands down (state 252, which
    /// ends at once here; DK2 plays a worker's action 79 first, of unread length).</summary>
    public static void RespondToThreat(SimulationContext context, RoomCache rooms, CreatureState me, CreatureDef mine)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        RoomInstance? room = me.AlarmRoomTile >= 0 ? RoomIndex.RoomAt(world, database, me.AlarmRoomTile) : null;
        int wanted = me.AlarmDeficit;
        int sent = 0;

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; room != null && wanted >= 0 && i < creatures.Count; i++)
        {
            CreatureState c = creatures[i];
            if (c.OwnerPlayerId != me.OwnerPlayerId || c.Id == me.Id) continue;
            CreatureDef def = database.CreatureById(c.CreatureTypeId);
            if (def.Flags.HasFlag(CreatureFlags.IsWorker) || def.Flags.HasFlag(CreatureFlags.IsHorny)) continue;
            if (CombatRules.InCombat(c) || !CombatRules.MayLookForAFight(world, context.Database, c, force: false)) continue;
            if (c.Activity is not (CreatureActivity.Working or CreatureActivity.AtWork or CreatureActivity.GoingToJob)) continue;
            if (c.JobTileIndex < 0 || !room.TileIndices.Contains(c.JobTileIndex)) continue;
            if (c.Health <= CreatureLevelRules.MaxHealth(database, def, c.Level) / 4) continue;

            // The Imp with 0x80, everyone else with 0 (0x0047583F); workers are skipped above.
            Navigation.MoveOptions options = c.CreatureTypeId == CombatRules.ImpCreatureTypeId ? MoveOptions.Loose : MoveOptions.Plain;
            if (!context.Navigation.TryRouteTo(c, me.AlarmX, me.AlarmY, options)) continue;
            c.CurrentJob = JobType.None;
            c.JobTileIndex = -1;
            c.Running = true; // DK2 sets the raiser's run speed on it (combat-alarm.md §3)
            CombatStates.Travel(context, c, CombatStates.ReportingDanger);
            c.ReportTicks = 0;
            c.CombatStateEnteredTick = context.Tick.Value;
            sent++;
            wanted -= ThreatAtLevel(database, def, c.Level);
        }

        context.Emit(SystemName, "alarm_raised", $"creature={me.Id} sent={sent} deficit={me.AlarmDeficit} left={wanted}");
        me.AlarmRoomTile = -1;
        me.AlarmDeficit = 0;
        me.Running = false;
        // 251, then SetState(1) and SetState(252), which ends at once here.
        CombatStates.Arrive(context, me);
        CombatStates.Enter(context, me, CreatureStateMachine.Idle);
        CombatStates.Enter(context, me, CombatStates.StandingDown);
        CombatStates.Idle(context.Hooks, me);
    }

    /// <summary>State 6 on the way to 254, then state 254 "reporting danger" (<c>0x004759A0</c>):
    /// stand at the threat's position for thirty ticks, looking for a fight the ordinary way each
    /// tick, then walk again and stand down.</summary>
    public static void Reporting(SimulationContext context, CreatureState me)
    {
        if (me.HasRoute) return;
        CombatStates.Arrive(context, me);
        if (++me.ReportTicks <= ReportTicks) return;
        me.ReportTicks = 0;
        me.Running = false;
        CombatStates.Idle(context.Hooks, me);
        context.Emit(SystemName, "stands_down", $"creature={me.Id}");
    }

    /// <summary>The room's position as the alarm and the withdrawal take it: DK2 reads the room's
    /// own <c>+0x27</c>/<c>+0x29</c> tile, taken here as its bounding box's centre (Q391).</summary>
    public static int RoomCentreX(RoomInstance room) => CreatureState.CentreOf((room.MinX + room.MaxX) / 2);
    public static int RoomCentreY(RoomInstance room) => CreatureState.CentreOf((room.MinY + room.MaxY) / 2);

    // ------------------------------------------------------------------ transitions

    /// <summary>Back into state 199 with the target kept.</summary>
    private static void FightOn(SimulationContext context, CreatureState me, string reason)
    {
        CombatStates.Enter(context, me, CombatStates.Engaging);
        me.CombatStateEnteredTick = context.Tick.Value;
        me.Running = true;
        context.Emit(SystemName, "fights_on", $"creature={me.Id} reason={reason}");
    }

    /// <summary>The state handler returning zero: the dispatcher sets state 1, idle. DK2 leaves
    /// the target tag in place; here the engagement is let go with the state (Q373).</summary>
    private static void End(SimulationContext context, CreatureState me, string reason)
    {
        CombatRules.Disengage(context.Hooks, me);
        context.Emit(SystemName, "disengaged", $"creature={me.Id} player={me.OwnerPlayerId} reason={reason}");
    }

    private static void ClearTarget(WorldState world, CreatureState me)
    {
        if (me.FightTargetKind == FightTargetKind.Creature && me.FightTargetId != CombatRules.NoTarget)
        {
            CreatureState? target = world.CreatureById(me.FightTargetId);
            if (target != null) CombatRules.Unregister(target, me.Id);
        }
        me.FightTargetId = CombatRules.NoTarget;
        me.FightTargetKind = FightTargetKind.None;
        me.FightTargetPlayerId = 0;
        me.FightTargetTileIndex = -1;
    }
}

/// <summary>The room index built at most once a tick, and only when a flight asks for it.</summary>
public sealed class RoomCache
{
    private List<RoomInstance>? _rooms;
    private long _tick = -1;
    private readonly List<RoomInstance> _owned = [];

    public IReadOnlyList<RoomInstance> Of(SimulationContext context, PlayerId owner)
    {
        if (_rooms is null || _tick != context.Tick.Value)
        {
            _rooms = RoomIndex.Build(context.World, context.Database);
            _tick = context.Tick.Value;
        }
        _owned.Clear();
        for (int i = 0; i < _rooms.Count; i++)
            if (_rooms[i].OwnerPlayerId == owner) _owned.Add(_rooms[i]);
        return _owned;
    }
}
