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
/// no room ids and uses Morgana's on Level 11A (1100) for every heart; the ruin gate reads the
/// player's heart, not the room's <c>+0x9A</c>; a willing torturee is not kicked to 135 first
/// (ADR-0400); <c>+0x2CC</c> is not modelled; the job is let go as the alarm's responders do.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/combat-alarm.md")]
public sealed class HeartDefenceSystem : ISimulationSystem
{
    /// <summary>The update's period in turns.</summary>
    public const int PeriodTurns = 4;

    /// <summary>The heart room id that DK2's phase adds to the turn, modulo the period: Morgana's
    /// heart on Level 11A is room 1100, so it dispatches on turns 0 mod 4 (capture rows 1 mod 4).</summary>
    public const int RoomIdPhase = 1100 % PeriodTurns;

    public string Name => "heart defence";

    public void Execute(SimulationContext context)
    {
        // DK2's turn is ours' tick plus one (ADR-0119).
        if ((context.Tick.Value + 1 + RoomIdPhase) % PeriodTurns != 0) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;
        // A table without the two radii (a test fixture) has no dispatch.
        if (!database.Variables.TryGet(VariableTypeId.DungeonHeartReportingDistanceTiles, out int reporting)
            || !database.Variables.TryGet(VariableTypeId.DungeonHeartCreatureGatheringDistanceTiles, out int gathering))
            return;
        List<RoomInstance>? rooms = null;

        for (int p = 0; p < world.PlayerCount; p++)
        {
            ref PlayerState owner = ref world.PlayerAtIndex(p);
            if (!PlayerIds.IsKeeper(owner.PlayerId.Value) || owner.HeartRuined) continue;
            PlayerId ownerId = owner.PlayerId;
            rooms ??= RoomIndex.Build(world, database).ToList();
            foreach (RoomInstance room in rooms)
            {
                if (room.OwnerPlayerId != ownerId || room.Definition.TypeId != RoomTypeId.DungeonHeart) continue;
                int x = CreatureState.CentreOf(room.CentreFieldX), y = CreatureState.CentreOf(room.CentreFieldY);
                CreatureState? intruder = Intruder(world, ownerId, x, y, reporting * reporting * Fp32.One);
                if (intruder is not null) Gather(context, ownerId, x, y, gathering * gathering * Fp32.One, intruder);
            }
        }
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
        int sent = 0;
        foreach (CreatureState c in world.OwnedCreatures(owner))
        {
            if (!CreatureStateMachine.HasState(c) || CombatRules.DistanceSquared(x, y, c.PosX, c.PosY) >= limit) continue;
            CreatureDef def = database.CreatureById(c.CreatureTypeId);
            if (def.Flags.HasFlag(CreatureFlags.IsHorny) || CallToArmsRules.IsUnderCallToArms(world, c)) continue;
            if (NeedRules.ResolvedCategory(c) == CreatureStateCategory.Combat) continue;
            if (!CombatRules.MayLookForAFight(world, database, c, force: false)) continue;
            if (c.Health <= CreatureLevelRules.MaxHealth(database, def, c.Level) / 4) continue;
            if (CreatureStateTable.Get(c.Dk2StateId).Group == 3 || c.DroppedToDoJob != JobType.None) continue;
            if (c.Dk2StateId == StateShadow.TravelToPoint && c.UtilityNewState == CombatStates.ReportingDanger) continue;

            // The Imp with 0x80, everyone else with 0 (0x004D99A4, 0x004D99F6).
            MoveOptions options = c.CreatureTypeId == CombatRules.ImpCreatureTypeId ? MoveOptions.Loose : MoveOptions.Plain;
            if (!context.Navigation.TryRouteTo(c, intruder.PosX, intruder.PosY, options)) continue;
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
}
