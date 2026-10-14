// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The one declared order of the systems (ADR-0013), pinned: grouping them into phases, or
/// moving one, must be a decision this test is changed for, never a side effect.</summary>
public class SimulationSystemsOrderTests
{
    [Fact]
    public void TheDefaultSystemsRunInTheirDeclaredOrder()
    {
        string[] expected =
        [
            "HandSystem",
            "RescuePrioritySystem",
            "SpecialSystem",
            "TileTagSystem",
            "RoomSellSystem",
            "RoomBuildSystem",
            "BlueprintSystem",
            "AutoFurnitureSystem",
            "TempleSacrificeSystem",
            "GraveyardSystem",
            "CasinoSystem",
            "AllianceSystem",
            "DoorSystem",
            "KeeperSpellSystem",
            "PossessionSystem",
            "CreatureStatusSystem",
            "CombatSystem",
            "TremorCreatureSystem",
            "ReaperSystem",
            "CombatPitSystem",
            "JackInTheBoxSystem",
            "MovementDustSystem",
            "CreatureMovementSystem",
            "DoorHealthSystem",
            "TrainingActionSystem",
            "ShotSystem",
            "BoulderSystem",
            "ObjectMovementSystem",
            "TrapSystem",
            "TrapRemovalSystem",
            "DoorRemovalSystem",
            "ReaperRemovalSystem",
            "CallToArmsSystem",
            "ChickenSystem",
            "FogOfWarSystem",
            "WalkOverGoldSystem",
            "ImpTaskSystem",
            "TileWorkSystem",
            "BridgeBurnSystem",
            "StoreSweepSystem",
            "HeartDefenceSystem",
            "ManaRemovalSystem",
            "RatSpawnSystem",
            "ManaSystem",
            "ImpSupplySystem",
            "RoomBuildWaveSystem",
            "ResearchSystem",
            "PortalSystem",
            "RoomHealthSystem",
            "HatcherySystem",
            "ObjectReactionSystem",
            "CreatureEatSystem",
            "CreatureSleepSystem",
            "CreatureHealthSystem",
            "PrisonSystem",
            "TortureSystem",
            "RescueSystem",
            "PrisonerEatSystem",
            "PrisonerWanderSystem",
            "PaydaySystem",
            "CasinoJackpotSystem",
            "HeroSystem",
            "CreatureJobSystem",
            "CreatureAngerSystem",
            "CreatureExperienceSystem",
            "DungeonHeartSystem",
            "TremorSystem",
        ];

        Assert.Equal(expected, SimulationSystems.Default().Select(system => system.GetType().Name).ToArray());
    }
}
