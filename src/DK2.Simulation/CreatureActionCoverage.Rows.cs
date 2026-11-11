// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Combat;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

public static partial class CreatureActionCoverage
{
    private const string Table = "creature-action-table.md section 4";

    // One line per action, in id order; the starters as creature-action-table.md section 4 lists them.
    // A Built row's member is checked to exist, and a constant to equal its id.
    private static void AddRows(List<ActionCoverageRow> rows)
    {
        rows.Add(NeverBegun(0, $"the null action: state 148 passes 0 for none, which starts and ends at once; {Table}"));
        rows.Add(Built(1, typeof(ImpTaskSystem), "StepWorkAnimation", "states 14, 186, 38; possession", "possession queues the strike with no clip"));
        rows.Add(Built(2, typeof(ImpTaskSystem), "StepWorkAnimation", "states 26, 186; possession", "possession queues the strike with no clip"));
        rows.Add(NeverBegun(3, $"no begin call passes 3 (its handler 0x00484940 returns 0); {Table}"));
        rows.Add(Built(4, typeof(ClaimActionRules), "Begin", "state 16; possession", "possession starts through its existing claim adapter; definitions without timing use WorkCadence"));
        rows.Add(Built(5, typeof(ClaimActionRules), "Begin", "state 20", "definitions without timing use WorkCadence"));
        rows.Add(Unbuilt(6, "possession", "possession queues the attack at once, no art 13 action"));
        rows.Add(Built(7, typeof(ReinforcementActionRules), "Begin", "state 22"));
        rows.Add(Unbuilt(8, "state 24", "the WorkCadence stand-in; ImpTaskSystem.UpdateWorking asks only the begin's sound"));
        rows.Add(Unbuilt(9, "state 185", "state 185 not read"));
        rows.Add(Built(10, typeof(ClaimActionRules), "Begin", "state 18", "definitions without timing use WorkCadence"));
        rows.Add(Unbuilt(11, "states 180, 181, 267", "RoomAttackRules queues the attack each turn with no art 5 action; 180 and 181 unbuilt"));
        rows.Add(NeverBegun(12, $"no begin call passes 12 (room-health.md); {Table}"));
        rows.Add(Unreachable(13, "state 206", "DK2 never enters state 206 (Q455, traps.md section 11)"));
        rows.Add(NeverBegun(14, $"no begin call passes 14; {Table}"));
        rows.Add(Built(15, typeof(FightRules), nameof(FightRules.BeginFreeAim), "possession", "no hold (FightRules.BeginPossessedAction)"));
        rows.Add(Built(16, typeof(FightRules), nameof(FightRules.StartCast), "state 200; possession", "possession begins it with no hold"));
        rows.Add(Built(17, typeof(FightRules), nameof(FightRules.StartSwing), "state 200; possession", "possession begins it with no hold"));
        rows.Add(Unbuilt(18, "state 78", "state 78 not read"));
        rows.Add(Built(19, typeof(SlapRules), nameof(SlapRules.React), "state 77"));
        rows.Add(Unreachable(20, "state 207", "DK2 never enters state 207 (room-return-states.md section 5)"));
        rows.Add(NeverBegun(21, $"excluded by the runner and the begin as no action; {Table}"));
        rows.Add(Unbuilt(22, "the hand slap", "the slap's state-83 arm, which slap-states.md section 1 finds never matches after the state move"));
        rows.Add(NeverBegun(23, $"no begin call passes 23; {Table}"));
        rows.Add(Unbuilt(24, "state 83", "the sleep plays no art 12 action"));
        rows.Add(Unbuilt(25, "state 85", "the sulk has no handler"));
        rows.Add(Built(26, typeof(CreatureEatSystem), "Swallow", "states 93, 118; eating; the creature update; possession",
            "the update's 0x00489329, the dropped chicken (EatingRules.EatDropped) and possession, which eat at once"));
        rows.Add(Built(27, typeof(TempleStep), "Face", "states 104, 74", "state 74, which DK2 never enters"));
        rows.Add(NeverBegun(28, $"no begin call passes 28; {Table}"));
        rows.Add(Built(29, typeof(ResearchStep), "AtTheShelf", "state 54"));
        rows.Add(Built(30, typeof(CreatureActionTable), nameof(CreatureActionTable.TrainMelee), "state 111"));
        rows.Add(Built(31, typeof(CreatureActionTable), nameof(CreatureActionTable.TrainSpell), "state 111"));
        rows.Add(Built(32, typeof(WorkshopStep), "AtTheMachine", "state 64"));
        rows.Add(NeverBegun(33, $"no begin call passes 33; {Table}"));
        rows.Add(NeverBegun(34, $"no begin call passes 34; {Table}"));
        rows.Add(Built(35, typeof(ResearchStep), "Celebrate", "states 60, 140, 152, 153, 158, 159, 68; the celebration",
            "states 140, 158, 159, and 68, which draws but plays nothing"));
        rows.Add(Unbuilt(36, "states 130, 131, 136", "the torture states play no art 17 action"));
        rows.Add(Unbuilt(37, "states 131, 136", "the torture states play no art 25 action"));
        rows.Add(NeverBegun(38, $"no begin call passes 38; {Table}"));
        rows.Add(Built(39, typeof(CasinoStep), "AtTheAlcove", "state 146"));
        rows.Add(Built(40, typeof(ResearchStep), "Watch", "the Library's intruder watch"));
        rows.Add(Built(41, typeof(PrisonRules), nameof(PrisonRules.FlourishTicks), "state 116"));
        rows.Add(NeverBegun(42, $"no begin call passes 42; {Table}"));
        rows.Add(Built(43, typeof(ImpTaskSystem), "Rest", "states 12, 5, 53, 63, 88, 176, 198, 250; job 16's start",
            "state 198 (not read), 250 (built, begins none), 88 (unreachable) and job 16; the creature idle holds a guessed length"));
        rows.Add(Built(44, typeof(ImpTaskSystem), "Rest", "states 12, 5, 108, 139", "states 108 and 139"));
        rows.Add(Unbuilt(45, "state 240", "state 240 read (special-role-idle.md), not built"));
        rows.Add(Built(46, typeof(ReaperRules), "BeginAction", "states 231, 237, 242"));
        rows.Add(Built(47, typeof(FinaleRules), "PickUpGem", "state 233"));
        rows.Add(NeverBegun(48, $"no begin call passes 48; {Table}"));
        rows.Add(Built(49, typeof(PortalRules), nameof(PortalRules.JoiningTicks), "portal arrival; special arrival", "the special arrival, not read"));
        rows.Add(Unbuilt(50, "the creature update", "a possessed creature's prayer heal, begun with +0x3E6 bit 0x2000 (whose setter is unread); ours has none"));
        rows.Add(NeverBegun(51, $"no begin call passes 51; {Table}"));
        rows.Add(Unbuilt(52, "state 209; the stun", "state 209 (0x00481020) begins it before 255; ours enters 255 with no art 24 action (StatusEffectRules.StunInto, StunFromStarter)"));
        rows.Add(Built(53, typeof(DropLandingRules), nameof(DropLandingRules.GetUpTicks), "states 255, 263; routine 0x004813C0", "routine 0x004813C0, not read"));
        rows.Add(Unreachable(54, "states 258, 262", "DK2 never enters either: the Reaper scan's gate never passes (room-return-states.md)"));
        rows.Add(Built(55, typeof(VictoryRules), nameof(VictoryRules.Celebrate), "state 259; the victory arm"));
        rows.Add(Built(56, typeof(CasinoStep), "AtTheAlcove", "state 146"));
        rows.Add(Built(57, typeof(CasinoStep), "Dance", "states 152, 153, 158, 159; the win celebration", "states 158 and 159"));
        rows.Add(NeverBegun(58, $"no begin call passes 58; {Table}"));
        rows.Add(Built(59, typeof(FinaleRules), "Appear", "state 230"));
        rows.Add(Unbuilt(60, "the imp teleport", "the teleport is not built (Q418)"));
        rows.Add(Unbuilt(61, "state 51", "the teleport is not built (Q418)"));
        rows.Add(Unbuilt(62, "the collapse", "the collapse sets state 216 with no art 7 action"));
        rows.Add(Built(63, typeof(TorturerStep), "Face", "state 128", "a fixed six-turn hold (TortureRules.WhipTurns), no art 32 clip"));
        rows.Add(Built(64, typeof(TremorRules), nameof(TremorRules.RunRecovery), "state 210"));
        rows.Add(Built(65, typeof(CasinoStep), "AtTheTable", "state 148"));
        rows.Add(Built(66, typeof(CasinoStep), "AtTheTable", "state 148"));
        rows.Add(Built(67, typeof(CasinoStep), "AtTheTable", "state 148"));
        rows.Add(Built(68, typeof(CasinoStep), "OnTheFloor", "states 148, 150"));
        rows.Add(Built(69, typeof(CombatPitSpectatorRules), "Face", "state 143", "the hold is a guess (CheerGuessTicks)"));
        rows.Add(Built(70, typeof(CombatPitSpectatorRules), "Face", "state 143", "the hold is a guess (CheerGuessTicks)"));
        for (int id = 71; id <= 75; id++) rows.Add(Unbuilt(id, "state 272", "state 272 not read"));
        rows.Add(Unbuilt(76, "states 272, 276", "272 not read; 276 is a Crypt state, dead code (Q375)"));
        rows.Add(Unbuilt(77, "state 272", "state 272 not read"));
        rows.Add(Unbuilt(78, "state 272", "state 272 not read"));
        rows.Add(Built(79, typeof(FearRules), nameof(FearRules.RespondToThreat), "state 251"));
        rows.Add(Unreachable(80, "state 275", "a Crypt state; the Crypt is dead code, never shipped (Q375, guard-room.md section 7)"));
    }
}
