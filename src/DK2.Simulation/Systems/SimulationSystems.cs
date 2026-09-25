// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Systems;

/// <summary>The systems the game runs, in the order it runs them: the one declaration
/// of that order (ADR-0013). Order is behaviour, so each entry says why it sits where
/// it does. UNKNOWN: DK2's own per-tick subsystem order — Flame's disassembly names
/// the subsystems but not their sequence — so this order is ours.</summary>
public static class SimulationSystems
{
    public static ISimulationSystem[] Default() =>
    [
        // Hand first: one picked up this tick takes no step, and one dropped onto
        // marked work starts on it at once (ADR-0062).
        new HandSystem(),

        // A tile marked on tick N is marked for all of N; sell before build, so one
        // room can pay for another on one tick; build after tag, so both land as room.
        new TileTagSystem(),
        new RoomSellSystem(),
        new RoomBuildSystem(),
        new BlueprintSystem(),
        // Room-owned objects are rebuilt after the room shape changes and before
        // movement or attraction can observe it (Q270).
        new AutoFurnitureSystem(),
        // Persistent Temple histories follow the derived room's anchor after all
        // room mutations for this tick have settled (ADR-0149).
        new TempleSacrificeSystem(),
        // Graveyard decomposition tallies use the same persistent room identity.
        new GraveyardSystem(),
        new CasinoSystem(), // the lever, and the gold settled at it every four seconds (ADR-0188)
        // Alliances before doors: a door opens for an ally, so a pact made this tick is
        // honoured on the tick it was made.
        new AllianceSystem(),
        // Doors open and close before anyone routes across them this tick.
        new DoorSystem(),
        // Acquisition before state handlers and steering, as ProcessFight sits in DK2's update.
        new CreatureStatusSystem(), // the status countdowns (0x0048E160) before the fight reads their words
        new CombatSystem(),
        new CombatPitSystem(), // the pit's states, after the fight processing as DK2's handler is (ADR-0213)
        // Movement, then the fog it uncovers: an arrival is noticed on its own tick.
        new CreatureMovementSystem(),
        // Shots fly after the creatures have moved; where DK2's thing list puts them is Q398.
        new ShotSystem(),
        new FogOfWarSystem(),
        // Work queued and resolved inside one tick, so nothing survives the boundary
        // half-done; bridges burn after the work (Q67), which makes the fire final.
        new ImpTaskSystem(),
        new TileWorkSystem(),
        new BridgeBurnSystem(),
        // Mana after all ownership changes, so a tile claimed on N pays on N; the imp
        // it buys next, so N's mana casts on N; a lair built on N attracts on N.
        new ManaSystem(),
        new ImpSupplySystem(),
        new PortalSystem(),

        // Laying, once a game second (ADR-0066), before anything eats; then hunger
        // before tiredness, the original's needs order (docs/research/eating.md).
        new HatcherySystem(),
        new CreatureEatSystem(),

        // Sleep, health, prison: one asleep this tick heals this second; health settles
        // damage before death is checked; the health system skips prisoners, so exactly
        // one system settles a prisoner's second (ADR-0064).
        new CreatureSleepSystem(),
        new CreatureHealthSystem(),

        new PrisonSystem(),
        new TortureSystem(), // a captive on a device: the category 15 hook (ADR-0193)
        new RescueSystem(), // an ally freed from an enemy's device: 134 (ADR-0201)

        // The rest of being a prisoner, every tick; after PrisonSystem so one freed or
        // boned gets no step, and eat before wander as its one handler does in the
        // original (ADR-0073, ADR-0074).
        new PrisonerEatSystem(),
        new PrisonerWanderSystem(),

        // Payday after the needs - hunger, sleep, pay - then the job chooser, then the
        // anger ladder: the original's sequence (ADR-0082), so no-work anger lands in
        // the second it looked and a wage credited now is owed when AngerNoPay is
        // charged (ADR-0079). Experience last: nothing that died or left is promoted (Q124).
        new PaydaySystem(),

        new HeroSystem(), // a hero's standing job and its party walk, instead of the choosers (ADR-0216)
        new CreatureJobSystem(),

        new CreatureAngerSystem(),

        new CreatureExperienceSystem(),
        new DungeonHeartSystem(), // last, as the verdict is the last thing DK2's player frame does
    ];
}
