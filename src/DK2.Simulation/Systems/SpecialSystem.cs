// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>Applies a selected special before creatures act on this tick.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/specials.md")]
public sealed class SpecialSystem : ISimulationSystem
{
    private const byte IncreaseLevel = 117;
    private const byte ReceiveImps = 124;
    private const byte LocateHiddenLand = 129;
    private const byte TransferCreature = 140;
    public string Name => "Special";

    public void Execute(SimulationContext context)
    {
        foreach (ActivateSpecialCommand command in context.Commands<ActivateSpecialCommand>())
        {
            ObjectState? item = context.World.ObjectById(command.ObjectId);
            if (item is null) continue;
            ObjectDef? definition = context.Database.ObjectById(item.ObjectTypeId);
            if (definition?.IsSpecial != true) continue;
            int affected;
            switch (item.ObjectTypeId)
            {
                case IncreaseLevel:
                    affected = RaiseLevels(context, command.PlayerId);
                    break;
                case ReceiveImps:
                    affected = GiveImps(context, command.PlayerId);
                    break;
                case LocateHiddenLand:
                    // 0x004A4F70: the profile write is the host's (CampaignCarry); its effect 0xC6,
                    // message 49 and sound are not modelled.
                    context.World.Carry.HiddenLandFound++;
                    affected = 0;
                    break;
                case TransferCreature:
                    affected = StoreTransferCreature(context, command) ? 1 : 0;
                    break;
                default:
                    context.Emit(Name, "special_unimplemented",
                        $"player={command.PlayerId} object={item.Id} type={item.ObjectTypeId}");
                    continue;
            }

            context.World.RemoveObject(item.Id);
            context.World.Player(command.PlayerId).Statistics.SpecialsFound++; // 0x004A3FE6
            context.Emit(Name, "special_activated",
                $"player={command.PlayerId} object={item.Id} type={item.ObjectTypeId} creatures={affected}");
        }
    }

    /// <summary>Arm 0x8C of <c>0x004A3FC0</c>: in the campaign, a target the activator owns has its
    /// type and level stored and dies leaving no body (bit 0x20 cleared, health zeroed through the
    /// thing's slot +0x20, <c>0x004B2AD0</c>), so the death is booked as any other. A refusal still
    /// uses the box up (<c>0x00513760</c>).</summary>
    private static bool StoreTransferCreature(SimulationContext context, ActivateSpecialCommand command)
    {
        WorldState world = context.World;
        CreatureState? target = world.CreatureById(command.TargetId);
        if (world.GameMode != GameMode.Campaign || target is null
            || target.OwnerPlayerId != command.PlayerId.Value) return false;
        world.Carry.TransferStored = true;
        world.Carry.TransferCreatureTypeId = target.CreatureTypeId;
        world.Carry.TransferCreatureLevel = (byte)target.Level;
        target.CorpseSuppressed = true;
        target.Health = 0;
        return true;
    }

    private static int RaiseLevels(SimulationContext context, PlayerId playerId)
    {
        int affected = 0;
        foreach (CreatureState creature in context.World.Creatures)
        {
            if (creature.OwnerPlayerId != playerId || creature.Health <= 0)
                continue;
            CreatureDef creatureDef = context.Database.CreatureById(creature.CreatureTypeId);
            CreatureLevelRules.LevelUp(context.Hooks, creatureDef, creature);
            // 0x004A434A passes the picking player's TagId (+0x04) as the source.
            CreatureHealthRules.Apply(context.Database, creatureDef, creature,
                CreatureHealthRules.MaxHealth(context.Database, creatureDef, creature), playerId.Value);
            affected++;
        }
        return affected;
    }

    /// <summary>Special 124, <c>0x004A4980</c>: <c>SpecialReceiveImpsAmount</c> of the first worker
    /// type, on a two-tile circle round the Heart's tile centre at angles i × (0x800 / count), each
    /// four levels up and facing <c>rand(0x800)</c> on the world stream, kicked to 214. 214
    /// (<c>0x00481750</c>) waits for the fall to the floor, then runs 76's landing; ours has no fall
    /// (M9) and puts each down as the hand does, as Create Imp's (ADR-0312). Its effect, message 49
    /// and sound are not modelled.</summary>
    private static int GiveImps(SimulationContext context, PlayerId playerId)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int count = database[VariableTypeId.SpecialReceiveImpsAmount];
        int heart = HeartSites.PositionOf(world, database, playerId);
        if (count <= 0 || heart < 0) return 0;

        // The world's type list, taken in id order (its iterators are unread); with no worker the
        // type stays id 1, the default stored before the walk (0x004A4992).
        CreatureDef worker = database.Creatures.Values.OrderBy(c => c.CreatureId)
            .FirstOrDefault(c => c.Flags.HasFlag(CreatureFlags.IsWorker)) ?? database.CreatureById(1);

        int centreX = ((heart % world.Width) * 0x1000) + 0x800;
        int centreY = ((heart / world.Width) * 0x1000) + 0x800;
        int step = (0x800 / count) & 0x7FF;
        int made = 0;
        for (int i = 0, angle = 0; i < count; i++, angle = (angle + step) & 0x7FF)
        {
            int x = centreX + (int)(((long)TrapRules.Sine(angle) * 0x2000) >> 16);
            int y = centreY - (int)(((long)TrapRules.Cosine(angle) * 0x2000) >> 16);
            var tile = new TilePos(x >> 12, y >> 12);
            if (!world.Contains(tile)) continue;

            CreatureState imp = world.SpawnCreature(database, worker.CreatureId, playerId, tile);
            imp.PosX = x;
            imp.PosY = y;
            for (int level = 0; level < 4; level++) CreatureLevelRules.LevelUp(context.Hooks, worker, imp);
            imp.Heading = context.Rng.NextInt(0x800) & 0x7FF;

            // 76's landing for a keeper's worker, as the hand's drop writes it.
            imp.JustDropped = true;
            imp.LastTaskKind = ImpTaskKind.None;
            imp.TimerTicks = 0;
            imp.WorkerIdle = false;
            made++;
        }
        return made;
    }
}
