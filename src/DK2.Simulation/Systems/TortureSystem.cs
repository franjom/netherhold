// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>A captive on a torture device: the category 15 update hook <c>0x0047B590</c>, every
/// fifth turn from being strapped on (ADR-0193).</summary>
/// <remarks>Each act adds variable 19 to <see cref="CreatureState.TorturedDuration"/> and applies
/// the definition's <c>TortureHpChange</c> through the health routine. <c>TortureMoodChange</c>
/// goes to bucket 5 only when the chamber is the creature's own keeper's, so never for a
/// captive. Past the level-scaled <see cref="TortureRules.ConversionThreshold"/> the duration
/// resets, and an enemy dies on a <c>TortureChanceOfDyingWhenConverted</c> roll (shipped 0) or
/// else joins the chamber's keeper.
/// <para>Ours, stated: a captive whose health reaches zero is let go of there and then, and the
/// health system takes it; DK2 knocks it out on the device and then kills it, revealing
/// information (<c>0x004F2370</c>, <see cref="TortureInformationRules"/>, ADR-0230). A convert is let go of too, and stands in the
/// chamber idle as its new keeper's, and beds down like any other creature (owner, Q425).</para></remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/state-category-hooks.md")]
public sealed class TortureSystem : ISimulationSystem
{
    private const string SystemName = "Torture";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        for (int i = world.Creatures.Count - 1; i >= 0; i--)
        {
            CreatureState creature = world.Creatures[i];
            if (creature.Possessed) continue;
            if (!TortureRules.IsBeingTortured(creature)) continue;
            if (HandRules.IsHeld(creature)) continue;

            int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
            byte keeper = TortureRules.KeeperOf(creature);
            // The chamber freed, taken (0x004F1FB0), rebuilt (0x004F1F10) or beside a changed tile
            // (0x004E44E0) kicks its holders to 136 as a lost device does (ADR-0406).
            ObjectState? device = null;
            if (RoomEvictRules.Kicked(world, database, creature, RoomTypeId.TortureChamber)
                || (device = TortureRules.FreeDeviceAt(world, database, keeper, here, creature.Id)) is null)
            {
                // The device is gone: 131 goes to 136, which gets onto another free one within
                // reach (ADR-0200). Failing that, or with the chamber gone, a captive standing
                // outside its keeper's prison is not one any more.
                int next = TortureRules.DeviceWithinReach(world, database, creature, keeper);
                if (next >= 0)
                {
                    TortureRules.Restrap(context.Hooks, creature, next);
                    context.Emit(SystemName, "restrapped",
                        $"creature={creature.Id} at=({next % world.Width},{next / world.Width})");
                    continue;
                }

                creature.TortureRoll = 0; // 136's failure tail (0x00484CE0)
                Free(context, creature, "device_gone");
                continue;
            }

            if (creature.Dk2StateId == TortureRules.TorturedState) TurnTheWheel(context, creature, device, here);

            if (CreatureStateMachine.UpdatingCategory(creature) != CreatureStateCategory.Torture) continue;
            if (!CreatureStateMachine.HookFires(creature, CreatureStateMachine.HookCountdownSeed)) continue;

            Act(context, creature, keeper);
        }
    }

    /// <summary>A victim on a wall wheel, each turn. First the device action's handler (<c>0x00499300</c>):
    /// once a game second while the roll is in <c>[0x3A0, 0x460)</c>, the trough under the wheel raises
    /// its effect at the trough's point, owned by the victim's own keeper, with the victim's heading;
    /// water before coals, and nothing without a trough. That effect is not behind the sound switch
    /// <c>0x00759ADC</c>. Then 131's body turns the victim a step (<c>0x00484742</c>).</summary>
    private static void TurnTheWheel(SimulationContext context, CreatureState creature, ObjectState device, int here)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (RoomIndex.RoomAt(world, database, here) is not RoomInstance room
            || !TortureRules.OnWallWheel(world, room, device)) return;

        if (creature.TortureRoll is >= TortureRules.TroughEffectFirstRoll and < TortureRules.TroughEffectEndRoll
            && (context.Tick.Value + 1) % SimulationTiming.OriginalTicksPerGameSecond == 0
            && Trough(world, here) is ObjectState trough)
        {
            ushort effect = trough.ObjectTypeId == TortureRules.WaterTroughObjectId
                ? TortureRules.WaterEffectId : TortureRules.CoalsEffectId;
            (int x, int y) = AutoFurnitureRules.FurniturePosition(world, database, room, trough);
            int z = EggLaunchRules.FloorHeight(world, database, new Fp32(x), new Fp32(y));
            context.RequestEffect(SystemName, new ViewEffectRequest(new EffectId(effect),
                new PlayerId(creature.OwnerPlayerId), new FpPosition3(new Fp32(x), new Fp32(y), new Fp32(z)),
                new Dk2Angle((ushort)creature.Heading)));
        }

        TortureRules.Roll(world, creature, device.TileIndex);
    }

    /// <summary><c>0x004537E0</c> on the device's tile: a water trough, else a coals one.</summary>
    private static ObjectState? Trough(WorldState world, int tileIndex)
    {
        ObjectState? coals = null;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.TileIndex != tileIndex || thing.InHand) continue;
            if (thing.ObjectTypeId == TortureRules.WaterTroughObjectId) return thing;
            if (thing.ObjectTypeId == TortureRules.CoalsTroughObjectId) coals ??= thing;
        }
        return coals;
    }

    private static void Act(SimulationContext context, CreatureState creature, byte keeper)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);

        creature.TorturedDuration += TortureRules.DurationPerAct;
        int hurt = definition.TortureHpChange < 0
            // 0x0047B5E1 passes the victim's own owner as the source.
            ? Combat.StatusEffectRules.Hurt(world, database, definition, creature, -definition.TortureHpChange, creature.OwnerPlayerId)
            : CreatureHealthRules.Apply(database, definition, creature, definition.TortureHpChange, creature.OwnerPlayerId);

        // Only a creature tortured in its own keeper's chamber takes the mood: selector 8, bucket 5.
        if (creature.OwnerPlayerId == keeper)
            CreatureAngerRules.Apply(world, definition, creature, AngerBucket.Treatment, definition.TortureMoodChange,
                AngerComplaint.Tortured);

        if (creature.Health <= 0)
        {
            // An enemy that dies here tells its torturer's keeper something (0x004F2370, ADR-0230).
            string information = "none";
            if (creature.OwnerPlayerId != keeper)
            {
                (int reward, RevealResult revealed) = TortureInformationRules.Award(
                    world, database, context.Rng, definition, creature.OwnerPlayerId, keeper);
                information = reward.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Emit(SystemName, "information",
                    $"keeper={keeper} about={creature.OwnerPlayerId} reward={reward}" +
                    (reward == TortureInformationRules.Reveal ? $" reveal={revealed}" : ""));
            }
            context.Emit(SystemName, "tortured_to_death",
                $"creature={creature.Id} type={definition.Name} owner={creature.OwnerPlayerId} " +
                $"keeper={keeper} duration={creature.TorturedDuration} information={information}");
            Free(context, creature, "health_gone");
            return;
        }

        int threshold = TortureRules.ConversionThreshold(database, definition, creature.Level);
        context.Emit(SystemName, "tortured",
            $"creature={creature.Id} type={definition.Name} health={creature.Health} " +
            $"hurt={hurt} duration={creature.TorturedDuration}/{threshold}");
        if (creature.TorturedDuration <= threshold) return;

        creature.TorturedDuration = 0;
        if (creature.OwnerPlayerId == keeper) return;

        if (CreatureAngerRules.RollPercent(context.Rng) < database[VariableTypeId.TortureChanceOfDyingWhenConverted])
        {
            creature.Health = 0;
            context.Emit(SystemName, "died_converting",
                $"creature={creature.Id} type={definition.Name} owner={creature.OwnerPlayerId} keeper={keeper}");
            Free(context, creature, "died_converting");
            return;
        }

        // 0x004F2800, in its order: message 57 to the old owner, the owner change, every grievance
        // wiped, the converting keeper's rescue bit cleared, message 58 to the new owner, creature
        // event 9 on the convert (its tree now reads the new owner), then the statistic.
        byte from = creature.OwnerPlayerId;
        if (world.HasPlayer(from)) world.Messages.Post(world, from, PlayerMessageKind.CreatureConvertedAway, creature.Id, 0);
        world.ChangeCreatureOwner(database, creature, keeper);
        CreatureAngerRules.ForgetAll(world, definition, creature);
        RescuePriorityRules.Set(creature, keeper, false);
        world.Messages.Post(world, keeper, PlayerMessageKind.CreatureConverted, creature.Id, 0);
        context.FireCreatureEvent(creature, CreatureTriggerEvent.Converted);
        creature.TortureRoll = 0; // 0x004F28E9
        world.Player(keeper).Statistics.CreaturesConverted++; // 0x004F28FC
        context.Emit(SystemName, "converted",
            $"creature={creature.Id} type={definition.Name} from={from} to={keeper} " +
            $"at=({creature.Tile.X},{creature.Tile.Y})");
        Free(context, creature, "converted");
    }

    /// <summary>Off the device and out of captivity.</summary>
    private static void Free(SimulationContext context, CreatureState creature, string reason)
    {
        TortureRules.Unstrap(context.Hooks, creature);
        PrisonRules.Release(context.Hooks, creature);
        context.Emit(SystemName, "released",
            $"creature={creature.Id} owner={creature.OwnerPlayerId} reason={reason}");
    }
}
