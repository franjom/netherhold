// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Combat;

/// <summary>Shot process 18, Raise Dead (the Vampire's spell), <c>0x004AE880</c>
/// (docs/research/shots.md §8). Every body (class 6) lying on the tile of the shot's target point
/// (<c>+0x131</c>) becomes a new creature of type <see cref="ShotState.Data1"/> (byte <c>+0x13D</c>)
/// for the shot's owner, whoever the body belonged to, and the body is removed even when no
/// creature could be made.</summary>
/// <remarks>The new creature is put in state 1 (<c>0x0048AD30(1)</c>) at the body's point and its
/// size scalar reset to 4096 (<c>0x0048FD70</c>), with <c>+0x3CD</c> set; neither the scalar nor
/// <c>+0x3CD</c> is modelled here. Effect <c>0x144</c> plays at the body, owned by the shot's owner.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/shots.md")]
public static class RaiseDeadRules
{
    public const byte ProcessType = 18;
    public static readonly EffectId RaisedEffect = new(0x144);
    private const string SystemName = "Shot";

    public static void Raise(SimulationContext context, ShotState shot)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (shot.TargetX < 0 || shot.TargetY < 0) return;
        var tile = new TilePos(shot.TargetX >> 12, shot.TargetY >> 12);
        if (!world.Contains(tile)) return;

        // Gather first: removing a body and adding a creature both change the list.
        List<CreatureState>? bodies = null;
        foreach (CreatureState thing in world.Creatures)
            if (CreatureDeathRules.IsCorpse(thing) && thing.CarriedByCreatureId < 0 && thing.Tile == tile)
                (bodies ??= []).Add(thing);
        if (bodies is null) return;

        var owner = new PlayerId(shot.OwnerPlayerId);
        foreach (CreatureState body in bodies)
        {
            if (world.TrySpawnCreature(database, new CreatureTypeId((byte)shot.Data1), owner, body.Tile) is { } made)
            {
                made.PosX = made.LastPosX = body.PosX;
                made.PosY = made.LastPosY = body.PosY;
                made.PosZ = made.LastPosZ = body.PosZ;
                CreatureStateMachine.TrySetState(context.Hooks, made, CreatureStateMachine.Idle, context.Tick.Value);
                context.RequestEffect(SystemName, new ViewEffectRequest(RaisedEffect, owner,
                    new FpPosition3(new Fp32(body.PosX), new Fp32(body.PosY), new Fp32(body.PosZ))));
                context.Emit(SystemName, "dead_raised",
                    $"shot={shot.Id} body={body.Id} creature={made.Id} type={shot.Data1} player={shot.OwnerPlayerId} at=({tile.X},{tile.Y})");
            }
            CreatureHealthSystem.Remove(context, SystemName, body); // the body's slot +0x20
        }
    }
}
