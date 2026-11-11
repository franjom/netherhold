// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;

namespace DK2.Simulation.Systems;

/// <summary>The Dungeon Heart's responder dispatch, the tail of its room update <c>0x004D9690</c>
/// (combat-alarm.md §7): every fourth turn an unruined heart finds the first attackable enemy
/// creature within <c>DungeonHeartReportingDistanceTiles</c> of its position word and sends every
/// creature of its owner within <c>DungeonHeartCreatureGatheringDistanceTiles</c> that may go,
/// workers included, running to it to stand in state 254, as the alarm's responders do.</summary>
/// <remarks>Deviates: DK2's phase is the heart room's id (<c>(turn + room+4) &amp; 3</c>); ours has
/// no room ids and uses Morgana's on Level 11A (1100) for every heart; a willing torturee is not kicked to 135 first
/// (ADR-0400); the job is let go as the alarm's responders do.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/combat-alarm.md")]
public sealed class HeartDefenceSystem : IRoomTickStep
{
    /// <summary>The update's period in turns.</summary>
    public const int PeriodTurns = 4;

    /// <summary>The heart room id that DK2's phase adds to the turn, modulo the period: Morgana's
    /// heart on Level 11A is room 1100, so it dispatches on turns 0 mod 4 (capture rows 1 mod 4).</summary>
    public const int RoomIdPhase = 1100 % PeriodTurns;

    public string Name => "heart defence";

    private bool _dispatches;
    private int _reporting, _gathering;

    public void Execute(SimulationContext context) => RoomTickSystem.RunAlone(context, this);

    public void BeginTurn(SimulationContext context)
    {
        // DK2's turn is ours' tick plus one (ADR-0119).
        _dispatches = (context.Tick.Value + 1 + RoomIdPhase) % PeriodTurns == 0;
        if (!_dispatches) return;
        // A table without the two radii (a test fixture) has no dispatch.
        GlobalVariables variables = context.Database.Variables;
        _dispatches = variables.TryGet(VariableTypeId.DungeonHeartReportingDistanceTiles, out _reporting)
            && variables.TryGet(VariableTypeId.DungeonHeartCreatureGatheringDistanceTiles, out _gathering);
    }

    public bool Applies(SimulationContext context, RoomInstance room) =>
        _dispatches && room.Definition.TypeId == RoomTypeId.DungeonHeart
        && PlayerIds.IsKeeper(room.OwnerPlayerId) && context.World.HasPlayer(room.OwnerPlayerId)
        && !RoomRules.IsRuined(context.World, room);

    public void Tick(SimulationContext context, RoomInstance room)
    {
        WorldState world = context.World;
        PlayerId ownerId = new(room.OwnerPlayerId);
        int x = CreatureState.CentreOf(room.CentreFieldX), y = CreatureState.CentreOf(room.CentreFieldY);
        CreatureState? intruder = Intruder(world, ownerId, x, y, _reporting * _reporting * Fp32.One);
        if (intruder is not null) Gather(context, ownerId, x, y, _gathering * _gathering * Fp32.One, intruder);
    }

    /// <summary>The first attackable creature of an enemy of the owner within the radius, players
    /// and their creatures in list order.</summary>
    private static CreatureState? Intruder(WorldState world, PlayerId owner, int x, int y, int limit)
    {
        for (int p = 0; p < world.PlayerCount; p++)
        {
            PlayerId other = world.PlayerAtIndex(p).PlayerId;
            if (other == owner || !CombatRules.IsHostile(world, owner.Value, other.Value)) continue;
            foreach (CreatureState c in world.OwnedCreatures(other))
                if (CombatRules.DistanceSquared(x, y, c.PosX, c.PosY) < limit && CombatRules.IsAttackable(c))
                    return c;
        }
        return null;
    }

    /// <summary>Every creature of the owner within the radius that is not Horny, under Call to
    /// Arms or fighting, may look for a fight, has more than a quarter of its health, is not held,
    /// slapped or dying (group tag 3), was not dropped to a job and is not already walking to 254,
    /// and can route to the intruder, runs there to report danger.</summary>
    private void Gather(SimulationContext context, PlayerId owner, int x, int y, int limit, CreatureState intruder)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        context.Navigation.Refresh();
        // 0x0047189E -> 0x004B35C0: test the raw destination before routing.
        if ((context.Navigation.NavByte(intruder.PosX >> 12, intruder.PosY >> 12) & 0x08) != 0) return;
        int sent = 0;
        foreach (CreatureState c in world.OwnedCreatures(owner))
        {
            if (!CreatureStateMachine.HasState(c) || CombatRules.DistanceSquared(x, y, c.PosX, c.PosY) >= limit) continue;
            CreatureDef def = database.CreatureById(c.CreatureTypeId);
            if (((def.Flags & CreatureFlags.IsHorny) == CreatureFlags.IsHorny) || CallToArmsRules.IsUnderCallToArms(world, c)) continue;
            if (NeedRules.ResolvedCategory(c) == CreatureStateCategory.Combat) continue;
            if (!CombatRules.MayLookForAFight(world, database, c, force: false)) continue;
            if (c.Health <= CreatureLevelRules.MaxHealth(database, def, c.Level) / 4) continue;
            if (CreatureStateTable.Get(c.Dk2StateId).Group == 3 || c.DroppedToDoJob != JobType.None) continue;
            if (c.Dk2StateId == StateShadow.TravelToPoint && c.UtilityNewState == CombatStates.ReportingDanger) continue;

            // The Imp with 0x80, everyone else with 0 (0x004D99A4, 0x004D99F6).
            MoveOptions options = c.CreatureTypeId == CombatRules.ImpCreatureTypeId ? MoveOptions.Loose : MoveOptions.Plain;
            // 0x004D0E60 first: a route query into a local buffer that leaves the body's path alone,
            // so a worker that cannot reach the intruder keeps its own walk (0x004D99C9). Only then
            // the travel routes the body; retain the point using ADR-0388's tile approximation of
            // 0x004CF490's triangle case.
            if (!CanRoute(context, c, def, intruder, options)) continue;
            if (!context.Navigation.TryRouteToStance(c, intruder.PosX, intruder.PosY, options)) continue;
            c.CurrentJob = JobType.None;
            c.JobTileIndex = -1;
            c.Running = true;
            CombatStates.Travel(context, c, CombatStates.ReportingDanger);
            c.ReportTicks = 0;
            c.CombatStateEnteredTick = context.Tick.Value;
            sent++;
        }
        if (sent > 0)
            context.Emit(Name, "gathered",
                $"player={owner} intruder={intruder.Id} at={intruder.PosX / Fp32.One},{intruder.PosY / Fp32.One} sent={sent}");
    }

    /// <summary><c>0x004D0E60</c>: whether the creature can route from its position to the
    /// intruder's, asked without loading a route into its body. Standing on the intruder's tile
    /// passes, as the travel's own one-point route there does.</summary>
    private static bool CanRoute(SimulationContext context, CreatureState c, CreatureDef def, CreatureState intruder, MoveOptions options)
    {
        if (c.Tile == intruder.Tile) return true;
        List<Steering.Fixed16Vector> points = _routeScratch ??= [];
        points.Clear();
        // The found word is the route's point count (0x004D0F4B), as the travel's own test
        // (RouteWindow.Request) asks for a non-empty route.
        return context.Navigation.Route(
            new Steering.Fixed16Vector(c.PosX << 4, c.PosY << 4),
            new Steering.Fixed16Vector(intruder.PosX << 4, intruder.PosY << 4),
            WorldSteering.ProfileOf(def, c.OwnerPlayerId, options), points, refresh: false)
            && points.Count != 0;
    }

    // Per thread, as the other route queries' scratch: two worlds can tick at once.
    [ThreadStatic] private static List<Steering.Fixed16Vector>? _routeScratch;
}
