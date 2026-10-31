// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-create-gold.md")]
public static class FloorGoldLandingRules
{
    private enum LandingState : byte { ImplicitPosition = 0, Temple = 4, Store = 5, Standing = 16 }
    private const int BandWidth = 0x555, PositionMask = Fp32.One - 1;

    public static bool Handles(ObjectState pile) => pile.ObjectTypeId is 1 or 2 or 3;

    // Elevated scatter has coordinates before a landing process; legacy implicit piles have Z zero.
    public static bool HasPosition(ObjectState pile) =>
        Handles(pile) && (pile.ProcessState != (byte)LandingState.ImplicitPosition || pile.PosZ != 0);

    /// <summary>The gold arm of 0x0049EF90. Height is collapsed; positions and strict candidate
    /// bounds are retained. Legacy floor piles use their represented cell or tile centre.</summary>
    public static void Land(SimulationContext context, ObjectState incoming)
    {
        WorldState world = context.World;
        int x = incoming.PosX & PositionMask, y = incoming.PosY & PositionMask;
        int bandX = Math.Min(x / BandWidth, 2), bandY = Math.Min(y / BandWidth, 2);
        incoming.Cell = (byte)(bandY * 3 + bandX);
        RoomInstance? room = RoomIndex.RoomAt(world, context.Database, incoming.TileIndex);
        LandingState state = room?.Definition.TypeId switch
        {
            RoomTypeId.Treasury or RoomTypeId.DungeonHeart => LandingState.Store,
            RoomTypeId.Temple => LandingState.Temple,
            _ => LandingState.Standing,
        };
        incoming.ProcessState = (byte)state;
        if (state != LandingState.Standing) return;
        incoming.OwnerPlayerId = PlayerIds.Neutral;

        int lowX = bandX * BandWidth, highX = bandX == 2 ? PositionMask : lowX + BandWidth - 1;
        int lowY = bandY * BandWidth, highY = bandY == 2 ? PositionMask : lowY + BandWidth - 1;
        for (int index = world.Objects.Count - 1; index >= 0; index--)
        {
            ObjectState candidate = world.Objects[index];
            if (candidate.Id == incoming.Id || candidate.ObjectTypeId != FloorGold.FloorGoldObjectId
                || candidate.TileIndex != incoming.TileIndex || candidate.Health <= 0
                || candidate.InHand || candidate.CarriedByCreatureId >= 0) continue;
            int cell = candidate.Cell < 9 ? candidate.Cell : 4;
            int candidateX = HasPosition(candidate) ? candidate.PosX & PositionMask : cell % 3 * BandWidth + BandWidth / 2;
            int candidateY = HasPosition(candidate) ? candidate.PosY & PositionMask : cell / 3 * BandWidth + BandWidth / 2;
            if (candidateX <= lowX || candidateX >= highX || candidateY <= lowY || candidateY >= highY) continue;
            candidate.GoldHeld = Math.Max(0, unchecked(candidate.GoldHeld + incoming.GoldHeld));
            candidate.OwnerPlayerId = PlayerIds.Neutral;
            world.RemoveObject(incoming.Id);
            context.Emit("gold landing", "merged", $"incoming={incoming.Id} pile={candidate.Id} gold={candidate.GoldHeld}");
            return;
        }
    }
}
