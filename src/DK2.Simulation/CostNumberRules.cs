// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>Which digits a floating amount uses: the 4th argument of <c>0x0050FB70</c>.</summary>
public enum CostNumberKind : byte { Gold, Mana }

/// <summary>The floating gold and mana numbers (<see href="../../docs/research/floating-cost-numbers.md"/>):
/// <c>World_ShowCostNumber</c> <c>0x0050FB70</c> forwards to <c>Presentation_ShowCostNumber</c>
/// <c>0x00441060</c>, which shows nothing for a negative amount or a player other than the local
/// viewer, and otherwise creates effect 22 (gold) or 364 (mana) through view slot <c>+0x94</c>
/// (<c>0x0043A8E0</c>), which calls the gated creator <c>0x00440420</c>: an amount off camera or on an
/// unexplored tile is never shown. It stores the amount in the effect. Observation only: the request is
/// an event, never state.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/floating-cost-numbers.md")]
public static class CostNumberRules
{
    public const ushort GoldEffectId = 22;
    public const ushort ManaEffectId = 364;

    /// <summary>Asks <paramref name="player"/>'s view to float <paramref name="amount"/> at
    /// <paramref name="point"/>. The viewer test is the presentation's: the simulation has no viewer.</summary>
    public static void Show(SimulationContext context, string system, PlayerId player, FpPosition3 point,
        int amount, CostNumberKind kind = CostNumberKind.Gold)
    {
        if (amount < 0) return;
        context.RequestEffect(system, new ViewEffectRequest(
            new EffectId(kind == CostNumberKind.Gold ? GoldEffectId : ManaEffectId), player, point)
        {
            Amount = amount,
        });
    }

    /// <summary>The tile's centre at <paramref name="heightRaw"/>, as the callers build their point.</summary>
    public static FpPosition3 TileCentre(TilePos tile, int heightRaw) =>
        new(new Fp32(CreatureState.CentreOf(tile.X)), new Fp32(CreatureState.CentreOf(tile.Y)), new Fp32(heightRaw));
}
