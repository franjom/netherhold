// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

public enum TrainingActionKind : byte { None = 0, Melee = 30, Spell = 31 }

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/training-actions.md")]
public static class TrainingStep
{
    public const int AtMachineState = 111;
    public const int WorkingState = 112;
    private const byte Dummy = 14;
    private const byte SpellTarget = 29;

    public static bool Turn(SimulationContext context, string system, CreatureState trainee)
    {
        if (StateShadow.IsTravelling(trainee))
        {
            if (trainee.HasRoute) return true;
            trainee.StopMoving();
            return StateShadow.Arrive(context, system, trainee);
        }
        long age = context.Tick.Value - trainee.StateEnteredTick;
        if (age == 0) return true;
        if (!TrainingRules.BelowCap(context.Database, trainee)) return false;
        return trainee.Dk2StateId switch
        {
            TrainingRules.WalkToMachineState => Choose(context, trainee),
            AtMachineState => AtMachine(context, system, trainee),
            WorkingState => Work(context, system, trainee, age),
            _ => true,
        };
    }

    private static bool Choose(SimulationContext context, CreatureState trainee)
    {
        trainee.StopMoving();
        RoomInstance? room = Room(context, trainee);
        if (room is null || !TrainingRules.CanAfford(context.World, context.Database, trainee.OwnerPlayerId)) return true;
        ObjectState? machine = FindMachine(context, room, trainee);
        int x, y;
        if (machine is not null)
        {
            (x, y) = AutoFurnitureRules.FurniturePosition(context.World, context.Database, room, machine);
            (trainee.StateTargetX, trainee.StateTargetY) = (x, y);
            if (machine.ObjectTypeId == Dummy)
            {
                int angle = context.Rng.NextInt(Bearing.Turn);
                x += (TrapRules.Cosine(angle) * 0x7FF) >> 16;
                y += (TrapRules.Sine(angle) * 0x7FF) >> 16;
            }
            else if (SpellStandPoint(context, room, trainee, machine) is { } stand)
            {
                x = CreatureState.CentreOf(stand.X);
                y = CreatureState.CentreOf(stand.Y);
            }
        }
        else
        {
            int[] chain = SlabChain.Order(context.World, room);
            if (chain.Length == 0) return true;
            int tile = chain[context.Rng.NextInt(chain.Length)];
            int angle = context.Rng.NextInt(Bearing.Turn);
            x = CreatureState.CentreOf(tile % context.World.Width) + ((TrapRules.Cosine(angle) << 10) >> 16);
            y = CreatureState.CentreOf(tile / context.World.Width) + ((TrapRules.Sine(angle) << 10) >> 16);
        }
        if (context.Navigation.TryRouteToStance(trainee, x, y, MoveOptions.Loose))
            CreatureStateMachine.BeginTravel(context.Hooks, trainee, StateShadow.TravelToPoint, AtMachineState);
        return true;
    }

    private static ObjectState? FindMachine(SimulationContext context, RoomInstance room, CreatureState trainee)
    {
        int spell = TrainingSpell(context, trainee);
        bool dummyFirst = spell < 0 || context.Rng.NextInt(2) != 0;
        foreach (byte type in dummyFirst ? new[] { Dummy, SpellTarget } : new[] { SpellTarget, Dummy })
        {
            ObjectState? best = null;
            uint distance = uint.MaxValue;
            // A room's object list is newest-first, like its linked thing list.
            for (int i = context.World.Objects.Count - 1; i >= 0; i--)
            {
                ObjectState candidate = context.World.Objects[i];
                if (candidate.InHand || candidate.ObjectTypeId != type || !room.Contains(candidate.TileIndex)) continue;
                context.Rng.NextInt(100);
                (int x, int y) = AutoFurnitureRules.FurniturePosition(context.World, context.Database, room, candidate);
                uint d = Distance(trainee.PosX - x, trainee.PosY - y);
                if (d >= distance) continue;
                best = candidate;
                distance = d;
            }
            if (best is not null) return best;
        }
        return null;
    }

    private static TilePos? SpellStandPoint(SimulationContext context, RoomInstance room, CreatureState trainee, ObjectState target)
    {
        (int targetX, int targetY) = AutoFurnitureRules.FurniturePosition(context.World, context.Database, room, target);
        TilePos? found = null;
        uint best = uint.MaxValue;
        var sight = new WorldSight(context.Navigation, context.World, context.Database);
        bool candidate;
        do
        {
            candidate = false;
            foreach (int tile in SlabChain.Order(context.World, room))
            {
                if (AutoFurnitureRules.IsFloorSlot(context.World, room, tile)
                    || AutoFurnitureRules.WallSide(context.World, context.Database, room, tile) >= 0) continue;
                var point = new TilePos(tile % context.World.Width, tile / context.World.Width);
                int x = CreatureState.CentreOf(point.X), y = CreatureState.CentreOf(point.Y);
                if (!LineOfSight.Clear(sight, x, y, targetX, targetY)) continue;
                uint d = Distance(trainee.PosX - x, trainee.PosY - y);
                if (d >= best) continue;
                candidate = true;
                int chance = point == trainee.Tile ? 50 : 100;
                if (context.Rng.NextInt(100) >= chance) continue;
                found = point;
                best = d;
            }
        } while (candidate && !found.HasValue);
        return found;
    }

    private static bool AtMachine(SimulationContext context, string system, CreatureState trainee)
    {
        var tile = new TilePos(trainee.StateTargetX >> 12, trainee.StateTargetY >> 12);
        ObjectState? machine = OnTile(context.World, tile, SpellTarget) ?? OnTile(context.World, tile, Dummy);
        if (machine is null)
        {
            if (!StateShadow.TryMove(context, system, trainee, TrainingRules.WalkToMachineState)) return false;
            Begin(context, trainee, TrainingActionKind.Melee, null, 0);
            return true;
        }
        if (CasinoStep.TurnTo(context, trainee, trainee.StateTargetX, trainee.StateTargetY) != 0) return true;
        CreatureDef definition = context.Database.CreatureById(trainee.CreatureTypeId);
        int slot = TrainingSpell(context, trainee);
        TrainingActionKind action = slot >= 0 && machine.ObjectTypeId == SpellTarget
            ? TrainingActionKind.Spell : TrainingActionKind.Melee;
        if (action == TrainingActionKind.Melee && machine.ObjectTypeId != Dummy) return false;
        int art = action == TrainingActionKind.Spell ? 6 : 5;
        if (!AnimationTimingRules.ResolvesPresent(definition, art)) return false;
        Begin(context, trainee, action, machine, (byte)Math.Max(0, slot));
        if (action == TrainingActionKind.Spell) trainee.SpellStamps[slot] = context.Tick.Value;
        return StateShadow.TryMove(context, system, trainee, WorkingState);
    }

    private static void Begin(SimulationContext context, CreatureState trainee, TrainingActionKind action,
        ObjectState? machine, byte spellSlot)
    {
        trainee.TrainingActionId = (byte)action;
        trainee.TrainingActionStartedTick = context.Tick.Value;
        trainee.TrainingTargetObjectId = machine?.Id ?? -1;
        trainee.TrainingSpellSlot = spellSlot;
        trainee.TrainingActionHitDone = false;
        int art = action == TrainingActionKind.Spell ? 6 : 5;
        trainee.ActionUntilTick = context.Tick.Value + AnimationTimingRules.CountedHoldTicks(
            context.Database.CreatureById(trainee.CreatureTypeId), art, 1);
    }

    public static void PollAction(SimulationContext context, CreatureState trainee)
    {
        if (trainee.TrainingActionId == 0) return;
        if (trainee.InHand || trainee.CurrentJob != JobType.Train
            || CreatureStateMachine.UpdatingCategory(trainee) != CreatureStateCategory.Training)
        {
            CancelAction(trainee);
            return;
        }
        long now = context.Tick.Value;
        CreatureDef definition = context.Database.CreatureById(trainee.CreatureTypeId);
        if (!trainee.TrainingActionHitDone)
        {
            bool spell = trainee.TrainingActionId == (byte)TrainingActionKind.Spell;
            int delay = spell ? SpellRules.DelayTicks(definition, trainee.TrainingSpellSlot)
                : CombatRules.TicksOf(definition.MeleeHitDelay);
            bool ready = spell ? now > trainee.TrainingActionStartedTick + delay
                : now >= trainee.TrainingActionStartedTick + delay;
            if (ready)
            {
                trainee.TrainingActionHitDone = true;
                ObjectState? target = context.World.ObjectById(trainee.TrainingTargetObjectId);
                if (target is not null && !target.InHand)
                {
                    if (spell) FireTrainingSpell(context, trainee, definition, target);
                    else ObjectReactionRules.StartStruck(context, target);
                }
            }
        }
        if (trainee.TrainingActionHitDone && now >= trainee.ActionUntilTick) trainee.TrainingActionId = 0;
    }

    public static void CancelAction(CreatureState trainee)
    {
        if (trainee.TrainingActionId == 0) return;
        trainee.TrainingActionId = 0;
        trainee.TrainingActionStartedTick = -1;
        trainee.TrainingTargetObjectId = -1;
        trainee.TrainingSpellSlot = 0;
        trainee.TrainingActionHitDone = false;
        trainee.ActionUntilTick = 0;
    }

    private static void FireTrainingSpell(SimulationContext context, CreatureState trainee, CreatureDef definition, ObjectState target)
    {
        CreatureSpellDef? spell = SpellRules.SpellOf(context.Database, definition, trainee.TrainingSpellSlot);
        if (spell is null || !context.Database.Shots.TryGetValue(spell.ShotTypeId, out ShotDef? shotDefinition)) return;
        RoomInstance? room = Room(context, trainee);
        (int x, int y) = room is not null
            ? AutoFurnitureRules.FurniturePosition(context.World, context.Database, room, target)
            : (target.PosX, target.PosY);
        ShotState shot = ShotRules.Spawn(context.World, shotDefinition, trainee.OwnerPlayerId, trainee.PosX, trainee.PosY,
            (int)spell.ShotData1, (int)spell.ShotData2, trainee.Id, FightTargetKind.None, -1, PlayerIds.Neutral,
            target.TileIndex, x, y);
        ShotRules.Launch(shot, shotDefinition, x, y);
        ShotRules.RequestTrail(context, shot, new Dk2Angle((ushort)trainee.Heading),
            new Fp32(Fp32.One + trainee.PosZ + definition.Spells[trainee.TrainingSpellSlot].OriginZ));
    }

    private static bool Work(SimulationContext context, string system, CreatureState trainee, long age)
    {
        if (age > 1 && trainee.TrainingActionId != 0
            && (trainee.WaitPosX != trainee.PosX || trainee.WaitPosY != trainee.PosY))
        {
            CancelAction(trainee);
        }
        (trainee.WaitPosX, trainee.WaitPosY) = (trainee.PosX, trainee.PosY);
        return trainee.TrainingActionId != 0 || StateShadow.TryMove(context, system, trainee, TrainingRules.WalkToMachineState);
    }

    private static int TrainingSpell(SimulationContext context, CreatureState trainee)
    {
        CreatureDef definition = context.Database.CreatureById(trainee.CreatureTypeId);
        for (int slot = 0; slot < Math.Min(SpellRules.Slots, definition.Spells.Count); slot++)
            if (definition.Spells[slot].LevelAvailable <= trainee.Level
                && SpellRules.SpellOf(context.Database, definition, slot) is { } spell && SpellRules.IsAttack(spell)
                && SpellRules.CanCast(context.Database, trainee, definition, slot, context.Tick.Value)) return slot;
        return -1;
    }

    private static ObjectState? OnTile(WorldState world, TilePos at, byte type) => world.Objects.LastOrDefault(
        thing => !thing.InHand && thing.TileIndex == at.Y * world.Width + at.X && thing.ObjectTypeId == type);

    private static RoomInstance? Room(SimulationContext context, CreatureState trainee) => context.World.Contains(trainee.Tile)
        && RoomIndex.RoomAt(context.World, context.Database, trainee.Tile.Y * context.World.Width + trainee.Tile.X) is { } room
        && room.Definition.TypeId == RoomTypeId.TrainingRoom && room.OwnerPlayerId == trainee.OwnerPlayerId ? room : null;

    private static uint Distance(int dx, int dy) => unchecked((uint)(((long)dx * dx) >> 12) + (uint)(((long)dy * dy) >> 12));
}
