// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>Applies a selected special before creatures act on this tick.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/specials.md")]
public sealed class SpecialSystem : ISimulationSystem
{
    private const byte IncreaseLevel = 117;
    private const byte RevealMap = 118;
    private const byte MakeSafe = 119;
    private const byte DestroyWalls = 120;
    private const byte IncreaseGold = 121;
    private const byte ManaBoost = 122;
    private const byte StunImps = 123;
    private const byte ReceiveImps = 124;
    private const byte MakeHappy = 125;
    private const byte MakeUnhappy = 126;
    private const byte KillCreatures = 127;
    private const byte HealAll = 128;
    private const byte LocateHiddenLand = 129;
    private const byte TransferCreature = 140;

    /// <summary>The sound each arm ends with, in the special's own category with its handle
    /// (specials.md §5); the transfer arm asks none, and the arms ours has not built never run.</summary>
    private static ThingSoundEvent? SoundOf(byte objectTypeId) => objectTypeId switch
    {
        IncreaseLevel => ThingSoundEvent.SpecialIncreasedLevel,
        RevealMap => ThingSoundEvent.SpecialRevealedMap,
        MakeSafe => ThingSoundEvent.SpecialMadeSafe,
        DestroyWalls => ThingSoundEvent.SpecialDestroyedWalls,
        IncreaseGold => ThingSoundEvent.SpecialIncreasedGold,
        ManaBoost => ThingSoundEvent.SpecialBoostedMana,
        StunImps => ThingSoundEvent.SpecialStunnedImps,
        ReceiveImps => ThingSoundEvent.SpecialReceivedImps,
        MakeHappy => ThingSoundEvent.SpecialMadeHappy,
        MakeUnhappy => ThingSoundEvent.SpecialMadeUnhappy,
        KillCreatures => ThingSoundEvent.SpecialKilledCreature,
        HealAll => ThingSoundEvent.SpecialHealedAll,
        LocateHiddenLand => ThingSoundEvent.SpecialLocatedHiddenLand,
        _ => null,
    };
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
                case RevealMap:
                    // Inline in 0x004A3FC0: 0x00451EF0 for the local player only, as action 75's reveal.
                    FinaleRules.RevealMap(context.World, command.PlayerId);
                    affected = 0;
                    break;
                case MakeSafe:
                    affected = Reinforce(context, command.PlayerId);
                    break;
                case DestroyWalls:
                    affected = BreakEnemyWalls(context, command.PlayerId);
                    break;
                case IncreaseGold:
                    affected = AddGold(context, command.PlayerId, item);
                    break;
                case ManaBoost:
                    affected = BoostMana(context, command.PlayerId);
                    break;
                case StunImps:
                    affected = StunWorkers(context, command.PlayerId);
                    break;
                case ReceiveImps:
                    affected = GiveImps(context, command.PlayerId);
                    break;
                case MakeHappy:
                    affected = CalmOwn(context, command.PlayerId);
                    break;
                case MakeUnhappy:
                    affected = InfuriateOthers(context, command.PlayerId);
                    break;
                case KillCreatures:
                    affected = KillTarget(context, command, item);
                    break;
                case HealAll:
                    affected = HealOwn(context, command.PlayerId);
                    break;
                case LocateHiddenLand:
                    // 0x004A4F70: the profile write is the host's (CampaignCarry); its effect 0xC6
                    // and message 49 are not modelled; the sound is asked below.
                    context.World.Carry.HiddenLandFound++;
                    affected = 0;
                    break;
                case TransferCreature:
                    affected = StoreTransferCreature(context, command) ? 1 : 0;
                    break;
                default:
                    // 0x004A3FC0 has no arm for the rest (object 7); action 88 destroys the box all the same.
                    affected = 0;
                    break;
            }

            // 0x004A4D80 plays its sound only when it kills; the box goes either way (0x00513760).
            bool sounded = item.ObjectTypeId != KillCreatures || affected != 0;
            if (sounded && SoundOf(item.ObjectTypeId) is { } sound) ThingSoundRules.Request(context, Name, item, sound);
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

    /// <summary>The eight neighbours in the order of <c>0x006BC370</c>, clockwise from north.</summary>
    private static readonly (int X, int Y)[] Around = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    /// <summary>Special 119, <c>0x004A43E0</c>: for each tile the keeper owns that is not solid to
    /// navigation (row by row), each in-bounds neighbour that is a diggable wall (<c>0x00453D10</c>)
    /// owned by the keeper or the neutral player and short of its terrain's <c>MaxHealth</c> takes that
    /// shortfall as work in the keeper's name, so it becomes its max-health type. Returns the walls worked.</summary>
    private static int Reinforce(SimulationContext context, PlayerId playerId)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int worked = 0;
        // DK2 reads the live route byte (0x00509200); ours brings the cache up to date first.
        context.Navigation.Refresh();
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                if (world.Tile(new TilePos(x, y)).OwnerPlayerId != playerId.Value
                    || NavBytes.IsSolid(context.Navigation.NavByte(x, y))) continue;
                foreach ((int dx, int dy) in Around)
                {
                    var at = new TilePos(x + dx, y + dy);
                    if (!world.Contains(at)) continue;
                    ref TileState wall = ref world.Tile(at);
                    TerrainDef terrain = database.TerrainById(wall.TerrainId);
                    if (!TileWorkRules.IsDiggableWall(terrain)
                        || (wall.OwnerPlayerId != playerId.Value && !TileWorkRules.IsNeutralTile(wall))
                        || wall.Health == terrain.MaxHealth) continue;
                    Work(context, world.TileIndex(at), terrain.MaxHealth - wall.Health, playerId);
                    worked++;
                }
            }
        if (worked > 0) context.Navigation.Refresh();
        return worked;
    }

    /// <summary>Special 120, <c>0x004A45F0</c>: every tile, in index order, that is not the keeper's, is
    /// a diggable wall, belongs to an enemy by <c>0x004BF390</c> and is a <c>PlayerColouredWall</c>
    /// (terrain <c>+0x17B</c> bit <c>0x800</c>) takes minus its health as work, so it is destroyed.</summary>
    private static int BreakEnemyWalls(SimulationContext context, PlayerId playerId)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int broken = 0;
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState wall = ref world.TileAtIndex(i);
            TerrainDef terrain = database.TerrainById(wall.TerrainId);
            if (wall.OwnerPlayerId == playerId.Value || !TileWorkRules.IsDiggableWall(terrain)
                || !CombatRules.IsHostile(world, playerId.Value, wall.OwnerPlayerId)
                || (terrain.Flags & TerrainFlags.PlayerColouredWall) != TerrainFlags.PlayerColouredWall) continue;
            Work(context, i, -wall.Health, playerId);
            broken++;
        }
        if (broken > 0) context.Navigation.Refresh();
        return broken;
    }

    /// <summary>One <c>Tile_ApplyWork</c> call (<see cref="TileWorkRules.ApplyDelta"/>) with the
    /// follow-ups <see cref="TileWorkSystem"/> makes for a strike: the slab chain, the neighbouring
    /// Hatchery's coops, the conversion's sound and effect, a broken tile's sound, effect and message 12
    /// to the wall's owner, and spilled gold on the floor.</summary>
    private static void Work(SimulationContext context, int index, int delta, PlayerId playerId)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        ref TileState tile = ref world.TileAtIndex(index);
        byte before = tile.TerrainId;
        PlayerId ownerBefore = tile.OwnerPlayerId;
        TileWorkResult result = TileWorkRules.ApplyDelta(database, ref tile, delta, playerId);
        SlabChain.Changed(world, database, index, before, ownerBefore, context.Rng, context);
        if (tile.TerrainId == before) return;
        tile.FreshlyDug = false;
        HatcheryRules.AfterNeighbourWork(world, database, index, context.Rng);
        var at = new TilePos(index % world.Width, index / world.Width);
        if (result.Outcome == TileWorkOutcome.Transformed) TileWorkSystem.RequestCompletionEffect(context, before, at, playerId);
        if (result.Outcome == TileWorkOutcome.Destroyed)
        {
            TerrainDef old = database.TerrainById(before);
            ThingSoundRules.RequestTile(context, "Special", before, index, ThingSoundEvent.TileBroken);
            context.RequestEffect("Special", ViewEffectRequest.AtTileCentre(old.DestroyedEffectId, playerId, at, new Fp32(Fp32.One)));
            if (ownerBefore != playerId && world.HasPlayer(ownerBefore) && old.IsSolid && old.IsOwnable)
                world.Messages.Post(world, ownerBefore.Value, PlayerMessageKind.WallBreached, at.X, at.Y);
        }
        if (result.GoldSpilled > 0) FloorGold.Drop(world, database, index, result.GoldSpilled, PlayerIds.Neutral);
    }

    /// <summary>Special 123, <c>0x004A4870</c>: every worker (<c>IsWorker</c>) of every other player
    /// whose move to state 209 passes is kicked to it (<see cref="StatusEffectRules.StunFromStarter"/>).</summary>
    private static int StunWorkers(SimulationContext context, PlayerId playerId)
    {
        int stunned = 0;
        foreach (CreatureState creature in context.World.Creatures)
        {
            if (creature.OwnerPlayerId == playerId) continue;
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            if ((definition.Flags & CreatureFlags.IsWorker) != CreatureFlags.IsWorker) continue;
            if (StatusEffectRules.StunFromStarter(context, creature, definition)) stunned++;
        }
        return stunned;
    }

    /// <summary>Special 121, <c>0x004A4710</c>: <c>SpecialIncreaseGoldAmount</c> into the keeper's
    /// Treasuries and Heart up to their capacity (<c>Room_DepositGold</c>), the rest put down at the box
    /// (<c>0x004510C0</c>). DK2 fills them room by room in its list order, each from the room's start
    /// tile, with an unsigned space test (a room over capacity takes the rest), and a ruined Heart too;
    /// ours fills <see cref="GoldStore"/>'s tiles in index order, so the split can differ, and puts the
    /// rest down in the box's cell rather than at its point. Returns the gold stored.</summary>
    private static int AddGold(SimulationContext context, PlayerId playerId, ObjectState box)
    {
        int amount = context.Database[VariableTypeId.SpecialIncreaseGoldAmount];
        int stored = GoldStore.Deposit(context.World, context.Database, playerId, amount);
        if (amount > stored) FloorGold.PutDown(context.World, context.Database, box.TileIndex, box.Cell, amount - stored);
        return stored;
    }

    /// <summary>Special 127, <c>0x004A4D80</c>: a target that exists and is not the activator's takes
    /// minus its health, in the name of the box's owner (<c>+0x28</c>); otherwise nothing, and the box
    /// is still used up. Returns 1 for a kill.</summary>
    private static int KillTarget(SimulationContext context, ActivateSpecialCommand command, ObjectState box)
    {
        CreatureState? target = context.World.CreatureById(command.TargetId);
        if (target is null || target.OwnerPlayerId == command.PlayerId.Value) return 0;
        // Through the health routine (0x004B2CE0): its refusals and multiplier apply, so an armoured
        // creature takes half and lives; a creature brought to zero collapses inside the call.
        StatusEffectRules.Hurt(context.World, context.Database, context.Database.CreatureById(target.CreatureTypeId),
            target, target.Health, box.OwnerPlayerId);
        CollapseSystem.Now(context, target);
        return 1;
    }

    /// <summary>Special 122, inline in <c>0x004A3FC0</c>: <c>SpecialIncreaseManaAmount</c> through
    /// <c>Player_AddMana</c> (<c>0x004C19C0</c>), clamped at the ceiling. DK2 adds the same amount to the
    /// player dword <c>+0x10EE</c>, whose meaning is unread and ours does not keep.</summary>
    private static int BoostMana(SimulationContext context, PlayerId playerId)
    {
        ref PlayerState player = ref context.World.Player(playerId);
        player.Mana = Math.Min(player.Mana + context.Database[VariableTypeId.SpecialIncreaseManaAmount],
            ManaRules.Threshold(context.Database));
        return 0;
    }

    /// <summary>Special 125, <c>0x004A4BD0</c>: <c>0x00486E20</c> on each of the keeper's creatures,
    /// which zeroes the six counters behind the world's anger switch.</summary>
    private static int CalmOwn(SimulationContext context, PlayerId playerId)
    {
        int affected = 0;
        foreach (CreatureState creature in context.World.Creatures)
        {
            if (creature.OwnerPlayerId != playerId) continue;
            CreatureAngerRules.ForgetAll(context.World, context.Database.CreatureById(creature.CreatureTypeId), creature);
            affected++;
        }
        return affected;
    }

    /// <summary>Special 126, <c>0x004A4C90</c>: every creature of every other player, through
    /// <c>0x0048B870</c>: unless <c>NeverAngered</c>, <c>0x00486ED0</c> adds 0xFFFE to each counter
    /// behind the world's switch (clamped at <see cref="CreatureAngerRules.Saturation"/>, no message)
    /// and creature event 11 fires. DK2 also sets the creature word <c>+0x457</c> to 100, unread here.</summary>
    private static int InfuriateOthers(SimulationContext context, PlayerId playerId)
    {
        WorldState world = context.World;
        int affected = 0;
        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.OwnerPlayerId == playerId) continue;
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            if (!CreatureAngerRules.CanBeAngered(definition)) continue;
            CreatureAngerRules.SaturateAll(world, definition, creature);
            context.FireCreatureEvent(creature, CreatureTriggerEvent.Angry);
            affected++;
        }
        return affected;
    }

    /// <summary>Special 128, <c>0x004A4E50</c>: each of the keeper's creatures gains its level-scaled
    /// maximum less its health, in the keeper's name. DK2's arm tests nothing; ours skips a creature at
    /// or below zero, as whether a knocked-out one gets up is unread, and does not mirror the health
    /// routine's refusals of a heal (<c>OnlyAttackableByHorny</c>, <c>+0x3E6</c> bit 0x200000, vtable
    /// <c>+0x74</c>).</summary>
    private static int HealOwn(SimulationContext context, PlayerId playerId)
    {
        int affected = 0;
        foreach (CreatureState creature in context.World.Creatures)
        {
            if (creature.OwnerPlayerId != playerId || creature.Health <= 0) continue;
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            CreatureHealthRules.Apply(context.Database, definition, creature,
                CreatureHealthRules.MaxHealth(context.Database, definition, creature) - creature.Health, playerId.Value);
            affected++;
        }
        return affected;
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
    /// (M9) and puts each down as the hand does, as Create Imp's (ADR-0312). Its effect and message 49
    /// are not modelled; the sound is asked by <see cref="Execute"/>.</summary>
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
            .FirstOrDefault(c => ((c.Flags & CreatureFlags.IsWorker) == CreatureFlags.IsWorker)) ?? database.CreatureById(1);

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

            CreatureState? imp = world.TrySpawnCreature(database, worker.CreatureId, playerId, tile);
            if (imp is null) continue;
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
