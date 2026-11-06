// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>The stationary, non-flying Create Imp subset of state 214 and the physical pass.
/// Heights use the simulation's zero floor; see imp-create-position.md.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/imp-create-position.md")]
public static class ImpCreationRules
{
    public const int FallingState = 214;
    public const int BirthHeightAboveFloor = 0x800;
    /// <summary>Imp Birth Trail, which 0x00481750 makes for a worker at its landing point, heading 0.</summary>
    public static readonly EffectId BirthEffect = new(29);

    /// <summary>Process 1, <c>0x004AD7A0</c>, Create Creature (shot 2): <c>Data2</c> creatures, spread
    /// over a ring when more than one, each with a random(0x800) heading.</summary>
    public const byte CreateCreatureProcess = 1;

    /// <summary>Process 36, <c>0x004AFE50</c>, Create Single Creature (shot 60, the upgraded Create
    /// Imp): one creature, a cardinal heading, levelled up to <c>Data2</c>.</summary>
    public const byte CreateSingleCreatureProcess = 36;

    /// <summary>The creation process a Create Imp shot runs on its death event, on the shot's first
    /// update (a bought cast's turn, the turn after a free cast's): both gates (<c>0x004B35C0</c>, world <c>+0x20C</c>: bounds and nav bit 3) at the shot's
    /// point, then the creature through Creature_Construct (its name and blood-group draws), its
    /// heading draw, and for a worker the fall from half a tile up. Shots die newest first, so
    /// the newest cast draws first (native capture Q716FreeImps, ADR-0635). Process 1 is CONFIRMED by
    /// that capture; process 36 is read from <c>0x004AFE50</c> only. The non-worker arm
    /// (effect 0x93 for data 9, no fall) is not built; it is refused before Creature_Construct,
    /// so its name and blood-group draws are not made either.</summary>
    public static void FromShot(SimulationContext context, ShotState shot)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (!database.Shots.TryGetValue(shot.ShotTypeId, out ShotDef? definition)) return;
        bool single = definition.ProcessType == CreateSingleCreatureProcess;
        int count = single ? 1 : shot.Data2;
        if (count <= 0) return;
        var at = new TilePos(shot.PosX >> 12, shot.PosY >> 12);
        if (shot.PosX < 0 || shot.PosY < 0 || !world.Contains(at)) return;
        context.Navigation.Refresh();
        if ((context.Navigation.NavByte(at.X, at.Y) & 8) != 0)
        {
            context.Emit("ImpCreation", "creature_creation_refused", $"shot={shot.Id} at=({at.X},{at.Y}) reason=solid");
            return;
        }
        CreatureDef type = database.CreatureById((byte)shot.Data1);
        if (!((type.Flags & CreatureFlags.IsWorker) == CreatureFlags.IsWorker))
        {
            context.Emit("ImpCreation", "creature_creation_refused", $"shot={shot.Id} type={shot.Data1} reason=not_a_worker");
            return;
        }

        // The ring (0x004AD84B..): radius count * 0x2000 capped at 0x800, the angle stepping
        // 0x800 / (count - 1). The ring point only chooses where the creature is allocated; the
        // worker arm then moves it back to the shot's own point (0x004AD9B4).
        int radius = Math.Min(count * 0x2000, 0x800);
        int step = count > 1 ? (0x800 / (count - 1)) & 0x7FF : 0;
        for (int i = 0, angle = 0, offsetX = 0, offsetY = 0; i < count; i++)
        {
            if (count > 1)
            {
                offsetX = (int)(((long)radius * TrapRules.Sine(angle)) >> 16);
                offsetY = -(int)(((long)radius * TrapRules.Cosine(angle)) >> 16);
                angle = (angle + step) & 0x7FF;
            }
            int x = shot.PosX + (offsetX / 16), y = shot.PosY + (offsetY / 16);
            var tile = new TilePos(x >> 12, y >> 12);
            if (x < 0 || y < 0 || !world.Contains(tile)) continue;
            if (world.TrySpawnCreature(database, new CreatureTypeId(type.CreatureId), new PlayerId(shot.OwnerPlayerId), tile)
                is not { } made) continue;
            made.PosX = made.LastPosX = shot.PosX;
            made.PosY = made.LastPosY = shot.PosY;
            made.Heading = single ? (context.Rng.NextInt(4) & 3) << 9 : context.Rng.NextInt(0x800) & 0x7FF;

            // The cast puts the imp down as the hand does: DK2's new imps pass 214, then 11 and
            // 10, and look around where they landed (Level 2 seed 04: four imps made at (14,24)
            // reinforce the walls beside them). The same fields the drop writes (HandSystem):
            // the one-shot look, the kind just done zeroed, no chooser cap. ADR-0312.
            made.LastTaskKind = ImpTaskKind.None;
            made.TimerTicks = 0;
            made.WorkerIdle = false;
            Begin(context, made);

            // Process 36 then calls Creature_LevelUp (0x0048B120) until the level reaches Data2,
            // each with its promotion effect 339; process 1 never levels.
            if (single)
                for (int level = CreatureLevelRules.Clamp(shot.Data2); made.Level < level;)
                    if (!CreatureLevelRules.LevelUp(context.Hooks, type, made)) break;
            context.Emit("ImpCreation", "creature_created",
                $"shot={shot.Id} player={shot.OwnerPlayerId} creature={made.Id} at=({tile.X},{tile.Y}) heading={made.Heading} level={made.Level}");
        }
    }

    public static void Begin(SimulationContext context, CreatureState creature)
    {
        creature.PosZ = creature.LastPosZ = BirthHeightAboveFloor;
        creature.CreationVelocityZ = 0;
        creature.JustDropped = false;
        StateShadow.Move(context, "ImpCreation", creature, FallingState);
    }

    internal static void Step(SimulationContext context, CreatureState creature)
    {
        creature.LastPosZ = creature.PosZ;
        if (creature.PosZ <= 0)
        {
            creature.PosZ = creature.CreationVelocityZ = 0;
            context.RequestEffect("ImpCreation", new ViewEffectRequest(BirthEffect, creature.OwnerPlayerId,
                new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(0))));
            StateShadow.ToIdle(context, "ImpCreation", creature);
            creature.DroppedOnARoom = RoomIndex.RoomAt(context.World, context.Database,
                creature.Tile.Y * context.World.Width + creature.Tile.X) is not null;
            StateShadow.Move(context, "ImpCreation", creature, ImpStates.AfterDrop);
            return;
        }

        // State dispatch sees the old height; the physical pass follows it. The floor
        // clamp clears the persistent vertical vector, not state 214 itself.
        int gravity = unchecked((byte)context.Database[VariableTypeId.GravityConstant]);
        creature.CreationVelocityZ -= gravity * 0x100;
        creature.PosZ += creature.CreationVelocityZ / 16;
        if (creature.PosZ < 0) creature.PosZ = creature.CreationVelocityZ = 0;
    }

    internal static void Leave(CreatureState creature)
    {
        creature.PosZ = creature.LastPosZ = creature.CreationVelocityZ = 0;
    }
}
