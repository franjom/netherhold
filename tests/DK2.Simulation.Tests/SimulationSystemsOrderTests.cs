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
            "CasinoLeverSystem",
            "AllianceSystem",
            "DoorSystem",
            "KeeperSpellSystem",
            "PossessionSystem",
            "CreatureStatusSystem",
            "FootprintSystem",
            "CreatureUpdateSystem",
            "CreatureUpdateSystem/CombatSystem",
            "CreatureUpdateSystem/CreatureEatSystem",
            "CreatureUpdateSystem/CreatureSleepSystem",
            "CreatureUpdateSystem/PaydaySystem",
            "CreatureUpdateSystem/CreatureAngerSystem",
            "CreatureUpdateSystem/StateHandlers",
            "CreatureUpdateSystem/TremorCreatureSystem",
            "CreatureUpdateSystem/ReaperSystem",
            "CreatureUpdateSystem/FinaleSystem",
            "CreatureUpdateSystem/CombatPitSystem",
            "CreatureUpdateSystem/JackInTheBoxSystem",
            "CreatureUpdateSystem/CasinoJackpotSystem",
            "CreatureUpdateSystem/HeroSystem",
            "CreatureUpdateSystem/CreatureJobSystem",
            "CreatureUpdateSystem/MovementDustSystem",
            "CreatureUpdateSystem/CreatureMovementSystem",
            "CreatureUpdateSystem/ImpTaskSystem",
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
            "TileWorkSystem",
            "BridgeBurnSystem",
            "StoreSweepSystem",
            "HeartDefenceSystem",
            "CasinoSystem",
            "PortalSystem",
            "HatedCompanySystem",
            "RoomBorderFlameSystem",
            "RoomHealthSystem",
            "ManaRemovalSystem",
            "RatSpawnSystem",
            "ManaSystem",
            "ImpSupplySystem",
            "RoomBuildWaveSystem",
            "ResearchSystem",
            "ManufactureDecaySystem",
            "HatcherySystem",
            "ObjectReactionSystem",
            "CreatureHealthSystem",
            "PrisonSystem",
            "TortureSystem",
            "RescueSystem",
            "PrisonerEatSystem",
            "PrisonerWanderSystem",
            "CreatureExperienceSystem",
            "DungeonHeartSystem",
            "TremorSystem",
        ];

        Assert.Equal(expected, SimulationSystems.Default().SelectMany(Names).ToArray());

        // A composite's steps follow it, in the order each creature takes them.
        static IEnumerable<string> Names(ISimulationSystem system)
        {
            string name = system.GetType().Name;
            yield return name;
            if (system is CreatureUpdateSystem update)
                foreach (ICreatureUpdateStep step in update.Steps) yield return $"{name}/{step.GetType().Name}";
        }
    }
}
