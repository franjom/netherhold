// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public static partial class PossessionRules
{
    /// <summary>LEVEL <c>+0x638C</c> bit2, installed as global <c>0x006F2957</c> bit0
    /// (<c>0x0056C100</c>, <c>0x0051E6D0</c>): only then is possession charged.</summary>
    public const ushort UpkeepLevelFlag = 0x0004;

    /// <summary>Message 53's Data1 when possession ends for want of mana (<c>0x004C13CB</c>).</summary>
    public const int PossessionEndedSubtype = 5;

    /// <summary>The per-second possession charge (<c>0x004C17A0</c>, repeated in <c>0x004C10E0</c>):
    /// the creature's unsigned <c>PossessionManaCost</c> at level 1, otherwise scaled by its level
    /// row (stat 27) through a signed divide and clamped to 0..0xFFFE; then
    /// <c>UpgradedPossessionCostPercentage</c> once the spell is upgraded. Zero when the LEVEL
    /// flag is clear or nothing is possessed. The binary also adds variable 116 for a creature
    /// whose <c>+0x4A7</c> word is set; that field is not represented and the variable ships 0.</summary>
    public static int UpkeepPerSecond(WorldState world, GameDatabase database, in PlayerState player)
    {
        if (player.PossessedCreatureId == 0 || ((database.Level?.Flags ?? 0) & UpkeepLevelFlag) == 0
            || world.CreatureById(player.PossessedCreatureId) is not { } creature)
            return 0;

        uint cost = unchecked((ushort)database.CreatureById(creature.CreatureTypeId).PossessionManaCost);
        if (creature.Level != 1)
        {
            int scaled = database.StatPercent(CreatureStatId.PossessionManaCost, creature.Level) * (int)cost / 100;
            cost = scaled < 1 ? 0u : scaled < 0xFFFE ? (uint)scaled : 0xFFFEu;
        }

        if (player.Availabilities.Exists(a => a.Kind == KeeperSpellRules.SpellAvailabilityKind && a.TypeId == SpellId && a.Upgraded))
            cost = (uint)(byte)database[VariableTypeId.UpgradedPossessionCostPercentage] * cost / 100;
        return (int)cost;
    }

    /// <summary>After a keeper's payment (<c>0x004C10E0</c>), before Call to Arms and the Reaper:
    /// mana less the imp and trap upkeep strictly below the charge ends the possession through
    /// action 21, posts message 53 with Data1 5 and Data2 0 (0x004C13C8) and ends the update.
    /// Equality keeps it. True when the possession ended.</summary>
    public static bool AfterPayment(SimulationContext context, string system, ref PlayerState player, int impAndTrapUpkeep)
    {
        // A zero charge (an Imp) still compares: mana below the imp and trap upkeep ends it.
        if (player.PossessedCreatureId == 0 || ((context.Database.Level?.Flags ?? 0) & UpkeepLevelFlag) == 0
            || context.World.CreatureById(player.PossessedCreatureId) is null)
            return false;
        int upkeep = UpkeepPerSecond(context.World, context.Database, player);
        if (player.Mana - impAndTrapUpkeep >= upkeep) return false;
        int creatureId = player.PossessedCreatureId;
        Leave(context, ref player);
        context.World.Messages.Post(context.World, player.PlayerId, PlayerMessageKind.ManaShortfall, PossessionEndedSubtype, 0);
        context.Emit(system, "possession_unaffordable",
            $"player={player.PlayerId} creature={creatureId} upkeep={upkeep} mana={player.Mana}");
        return true;
    }
}
