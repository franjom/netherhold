// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
namespace DK2.Simulation;
public readonly record struct FearAmount(ushort Value);
/// <summary>Possession actions 30 and 31 and action 50's per-turn prayer handler.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/possession-special-abilities.md")]
public static class PossessionAbilityRules
{
    public const byte PrayerActionId = 50;
    public static CreatureId PickFrightenTarget(WorldState world, GameDatabase database, CreatureState source, PossessionAim aim)
    {
        var hearts = new HeartSites();
        hearts.Refresh(world, database);
        PossessionRules.ChainThing? picked = PossessionRules.PickMeleeTarget(world, database, source,
            database.CreatureById(source.CreatureTypeId), aim, hearts);
        return picked is { Class: PossessionRules.ChainClass.Creature, Thing: CreatureState target }
            ? new CreatureId(target.Id) : CreatureId.None;
    }
    public static void Frighten(SimulationContext context, CreatureId targetId, CreatureId sourceId, FearAmount amount)
    {
        if (context.World.CreatureById(targetId.Value) is not { } target || CreatureDeathRules.IsCorpse(target)) return;
        if (target.PossessionFearTicks == 0)
        {
            int eye = (ushort)context.Database.CreatureById(target.CreatureTypeId).EyeHeight.Raw;
            // Clone PosZ is above the ordinary floor; native effect coordinates are absolute.
            FearEffect(context, target, new EffectId(239), new Fp32(target.PosZ + Fp32.One + eye));
        }
        target.PossessionFearTicks = 20;
        target.PossessionFearAmount = amount.Value;
        target.PossessionFearSource = sourceId;
    }
    internal static void UpdateFear(SimulationContext context, CreatureState creature)
    {
        if (creature.PossessionFearTicks == 0) return;
        creature.PossessionFearTicks--;
        creature.TempFear = unchecked((ushort)(creature.TempFear + creature.PossessionFearAmount));
        ThreatFleeRules.Record(creature, creature.PossessionFearAmount,
            new ThreatSource(ThreatSource.Creature, creature.PossessionFearSource.Value));
        if ((creature.PossessionFearTicks & 4) == 0)
            FearEffect(context, creature, new EffectId(336), new Fp32(creature.PosZ + Fp32.One));
    }
    private static void FearEffect(SimulationContext context, CreatureState creature, EffectId effect, Fp32 height) =>
        context.RequestEffect("possession", new ViewEffectRequest(effect, new PlayerId(PlayerIds.Neutral),
            new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), height),
            new Dk2Angle(0), creature.Id));
    public static void ToggleForm(SimulationContext context, CreatureState creature)
    {
        creature.PossessionFlightForm = !creature.PossessionFlightForm;
        context.RequestEffect("possession", new ViewEffectRequest(new EffectId(201), creature.OwnerPlayerId,
            new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ + Fp32.One)),
            new Dk2Angle(0)));
    }
    internal static void TogglePrayer(CreatureState creature) =>
        creature.PossessionPraying = !creature.PossessionPraying;
    internal static void BeginPrayer(SimulationContext context, CreatureState creature)
    {
        if (!creature.Possessed || !creature.PossessionPraying || creature.PossessionPrayerAction) return;
        CreatureStateMachine.EndAction(creature);
        ActionAnimationClock.Begin(creature, context.Database.CreatureById(creature.CreatureTypeId), 23, context.Rng);
        creature.PossessionPrayerAction = true;
        ThingSoundRules.Request(context, "possession", creature, ThingSoundEvent.Prayed);
    }
    internal static void PollPrayer(SimulationContext context, CreatureState creature)
    {
        if (!creature.PossessionPrayerAction) return;
        if (!creature.PossessionPraying)
        {
            CancelPrayer(creature);
            return;
        }
        int delta = unchecked((ushort)context.Database[VariableTypeId.ModifyCreatureHealthWhilePrayingPerSecond])
            / SimulationTiming.OriginalTicksPerGameSecond;
        creature.HealthSourcePlayerId = creature.OwnerPlayerId;
        if (delta == 0 || CreatureDeathRules.IsDying(creature) || creature.CarriedByCreatureId >= 0) return;
        CreatureHealthRules.Apply(context.Database, context.Database.CreatureById(creature.CreatureTypeId),
            creature, delta, creature.OwnerPlayerId);
    }
    internal static void AdvancePrayer(SimulationContext context, CreatureState creature)
    {
        if (creature.PossessionPrayerAction && creature.FrozenTicks == 0)
            ActionAnimationClock.Advance(creature, context.Database.CreatureById(creature.CreatureTypeId));
    }
    internal static void CancelPrayer(CreatureState creature)
    {
        if (!creature.PossessionPrayerAction) return;
        creature.PossessionPrayerAction = false;
        ActionAnimationClock.Clear(creature);
    }
}
