// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Combat;

/// <summary>Shot process 17, the area knockdown of the Wind spell (Cyclone shot), <c>0x004AE990</c>
/// (docs/research/shots.md §8). <see cref="ShotState.Data1"/> is the radius R in tiles and
/// <see cref="ShotState.Data2"/> a percentage. Every creature strictly within R of the shot but the
/// caster is spun, hurt if it is another player's, knocked down, and made to wait an extra turn
/// before it gets up.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/shots.md")]
public static class WindRules
{
    public const byte ProcessType = 17;
    private const string SystemName = "Shot";

    public static void Blow(SimulationContext context, ShotState shot)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int radius = shot.Data1;
        int tileX = shot.PosX >> 12, tileY = shot.PosY >> 12;
        // The tile window [tile - R, tile + R), floored at 0 and capped at the map's size.
        int lowX = Math.Max(0, tileX - radius), lowY = Math.Max(0, tileY - radius);
        int highX = Math.Min(world.Width, tileX + radius), highY = Math.Min(world.Height, tileY + radius);
        if (lowX >= highX || lowY >= highY)
        {
            return;
        }

        uint reach, turn, damageDivisor;
        unchecked
        {
            // 32-bit, as the original computes them: (R·0x1000)² >> 12, R·0x600000, R·0x2000.
            reach = (uint)((radius * 0x1000 * radius * 0x1000) >> 12);
            turn = (uint)(radius * 0x600000);
            damageDivisor = (uint)(radius * 0x2000);
        }

        EntityList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            // Class 0 things on the map's tiles: a body (class 6) and a creature in the hand are not.
            // Ours, stated: a creature already down is passed over, so the kick cannot stand it up.
            if (CreatureDeathRules.IsCorpse(creature) || HandRules.IsHeld(creature) || CreatureDeathRules.IsDown(creature))
            {
                continue;
            }

            if (creature.Id == shot.CasterId)
            {
                continue; // +0x145, the caster's tag
            }

            TilePos at = creature.Tile;
            if (at.X < lowX || at.X >= highX || at.Y < lowY || at.Y >= highY)
            {
                continue;
            }

            uint distanceSquared;
            unchecked
            {
                long dx = Math.Abs(shot.PosX - creature.PosX), dy = Math.Abs(shot.PosY - creature.PosY);
                distanceSquared = (uint)((dy * dy) >> 12) + (uint)((dx * dx) >> 12);
            }
            if (distanceSquared >= reach)
            {
                continue;
            }

            uint spread = unchecked((uint)IntegerMath.SquareRoot(unchecked((int)(distanceSquared * 0x1000))) + (uint)(radius * 0x1000));
            creature.Heading = (creature.Heading + (int)(turn / spread)) & 0x7FF;

            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
            int landed = 0;
            if (creature.OwnerPlayerId != shot.OwnerPlayerId)
            {
                // def+0x692 (the base health, not the level's) × spread / (R·0x2000), then × Data2 / 100,
                // through the health routine with the shot's owner as the source.
                int share = unchecked((int)(definition.Hp * spread)) / (int)damageDivisor;
                int damage = share * shot.Data2 / 100;
                landed = StatusEffectRules.Hurt(world, database, definition, creature, damage, shot.OwnerPlayerId);
            }

            if (creature.Dk2StateId is not (TremorRules.KnockdownState or TremorRules.WaitingState or TremorRules.GettingUpState))
            {
                // Creature_KickToState(0xD2): to idle, then to the knockdown.
                creature.StopMoving();
                CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value);
                CreatureStateMachine.TrySetState(context.Hooks, creature, TremorRules.KnockdownState, context.Tick.Value);
            }
            creature.ShouldDelayGetUp = true; // +0x3E6 |= 0x8000000

            context.Emit(SystemName, "wind_struck",
                $"shot={shot.Id} creature={creature.Id} heading={creature.Heading} landed={landed} state={creature.Dk2StateId}");
        }
    }
}
