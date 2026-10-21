// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Systems;

/// <summary>The systems the game runs, in the order it runs them: the one declaration
/// of that order (ADR-0013). Order is behaviour, so each entry says why it sits where
/// it does. DK2's frame runs things, then every room's tick, then the players
/// (docs/research/binary-census.md section 2); <see cref="RoomTicks"/> keeps that slot (ADR-0479).</summary>
/// <remarks>The phases only name stretches of the one sequence; <see cref="Default"/> runs them
/// back to back, and a system that moves between phases moves in the order too.</remarks>
public static class SimulationSystems
{
    public static ISimulationSystem[] Default() =>
    [
        .. Commands(),
        .. RoomLifecycle(),
        .. KeeperActions(),
        .. ThingPass(),
        .. Work(),
        .. RoomTicks(),
        .. PlayerFrame(),
        .. Needs(),
        .. Choosers(),
        .. Verdict(),
    ];

    /// <summary>The player's own hand and marks, before anything moves.</summary>
    private static ISimulationSystem[] Commands() =>
    [
        // Hand first: one picked up this tick takes no step, and one dropped onto
        // marked work starts on it at once (ADR-0062).
        new HandSystem(),
        new RescuePrioritySystem(), // command-driven casualty marks before worker selection (ADR-0261)
        // A picked Special changes levels before this tick's movement and combat.
        new SpecialSystem(),
    ];

    /// <summary>Tiles marked, rooms sold and built, and everything that follows a room's shape.</summary>
    private static ISimulationSystem[] RoomLifecycle() =>
    [
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
        new CasinoLeverSystem(), // the owner's lever command, with the frame's actions (0x00513E50)
    ];

    /// <summary>Pacts, doors, spells and possession: what a keeper does to the world this tick.</summary>
    private static ISimulationSystem[] KeeperActions() =>
    [
        // Alliances before doors: a door opens for an ally, so a pact made this tick is
        // honoured on the tick it was made.
        new AllianceSystem(),
        // Doors open and close before anyone routes across them this tick.
        new DoorSystem(),
        new KeeperSpellSystem(),
        new PossessionSystem(),
    ];

    /// <summary>DK2's thing pass: creatures fight and move, then shots, objects and traps, then
    /// the second walk that removes whatever died.</summary>
    private static ISimulationSystem[] ThingPass() =>
    [
        // Acquisition before state handlers and steering, as ProcessFight sits in DK2's update.
        new CreatureStatusSystem(), // the status countdowns (0x0048E160) before the fight reads their words
        new FootprintSystem(),
        new CombatSystem(),
        new TremorCreatureSystem(), // recovery handler before steering; world Tremor runs after the entity pass
        new ReaperSystem(),
        new CombatPitSystem(), // the pit's states, after the fight processing as DK2's handler is (ADR-0213)
        // Movement, then the fog it uncovers: an arrival is noticed on its own tick.
        new JackInTheBoxSystem(), // state 273 and the travel toll, before the mover as DK2's state dispatcher is
        new MovementDustSystem(), // foot dust for last tick's crossing onto dirt, before the mover as the creature update's request is (ADR-0477)
        new CreatureMovementSystem(),
        // The doors' health after the creatures, as DK2's thing pass updates creatures first; ours
        // puts it before the shots, whose place in that pass is Q398 (door-blocking.md).
        new DoorHealthSystem(),
        // Shots fly after the creatures have moved; where DK2's thing list puts them is Q398.
        new TrainingActionSystem(), // creature action gates before the shots and object reactions they create
        new ShotSystem(),
        new BoulderSystem(), // objects (list 2) after shots (1), before traps (3)
        new ObjectMovementSystem(),
        new TrapSystem(), // after the shots, as DK2's class lists run shots (1) before traps (3)
        // The thing pass's second walk: whatever is dead now is removed this turn (0x004B7830).
        new TrapRemovalSystem(),
        new DoorRemovalSystem(),
        new ReaperRemovalSystem(),
        // The Call to Arms flag is an object the shot puts down; its recall, release and the
        // heeding states run in the thing update, before the payment that keeps it (ADR-0279).
        new CallToArmsSystem(),
        new ChickenSystem(), // state 96: the walk to the Hatchery or the wander (keeper-spell-inferno-chicken.md §3)
        new FogOfWarSystem(),
        new WalkOverGoldSystem(), // gold underfoot, after movement and before the state choosers (ADR-0300)
    ];

    /// <summary>Imp work, queued and resolved inside the tick.</summary>
    private static ISimulationSystem[] Work() =>
    [
        // Each worker resolves its work before the next; the final pass drains any other orders.
        // Bridges burn after the work (Q67), which makes the fire final.
        new ImpTaskSystem(),
        new TileWorkSystem(),
        new BridgeBurnSystem(),
    ];

    /// <summary>DK2's room ticks (<c>0x004EC450</c>): each room type's handler and the health step,
    /// after the creatures and before the players (ADR-0479). The Hatchery's laying stays in
    /// <see cref="Needs"/>, whose system also runs the eggs and coops of the thing pass.</summary>
    private static ISimulationSystem[] RoomTicks() =>
    [
        new StoreSweepSystem(), // the Treasury's handler and the Heart's: loose gold goes in every 16th turn
        new HeartDefenceSystem(), // the heart update's tail: its owner's creatures run to an intruder (0x004D9690)
        new CasinoSystem(), // the Casino's handler: the party, and the gold settled at the lever every four seconds (ADR-0188)
        new PortalSystem(), // the Portal's handler
        new HatedCompanySystem(), // the Library, Training Room, Guard Room and Workshop handlers' shared step
        new RoomBorderFlameSystem(), // the room tick's border flames (0x004E7CD0), before its health step
        new RoomHealthSystem(), // the room tick's health step (0x004E4B00)
    ];

    /// <summary>DK2's player frame: mana, imps and research, after every change of ownership this
    /// tick and after the room ticks.</summary>
    private static ISimulationSystem[] PlayerFrame() =>
    [
        // The removal countdown reads the stamp and the mana of the last payment before this
        // tick's payment, as DK2's thing update precedes its player update (ADR-0277).
        new ManaRemovalSystem(),
        new RatSpawnSystem(),
        // Mana after all ownership changes, so a tile claimed on N pays on N; the imp
        // it buys next, so N's mana casts on N; a lair built on N attracts on N.
        new ManaSystem(),
        new ImpSupplySystem(),
        new RoomBuildWaveSystem(), // keeper frame after mana, before its research chooser (0x004BB614)
        new ResearchSystem(), // the same player frame chooses a research target when it has none
    ];

    /// <summary>The creatures' needs: food, sleep, health, and a prisoner's lot.</summary>
    private static ISimulationSystem[] Needs() =>
    [
        // Laying, once a game second (ADR-0066), before anything eats; then hunger
        // before tiredness, the original's needs order (docs/research/eating.md).
        new HatcherySystem(),
        new ObjectReactionSystem(),
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
    ];

    /// <summary>Pay, then what each creature chooses to do, then what it thinks of it.</summary>
    private static ISimulationSystem[] Choosers() =>
    [
        // Payday after the needs - hunger, sleep, pay - then the job chooser, then the
        // anger ladder: the original's sequence (ADR-0082), so no-work anger lands in
        // the second it looked and a wage credited now is owed when AngerNoPay is
        // charged (ADR-0079). Experience last: nothing that died or left is promoted (Q124).
        new PaydaySystem(),
        new CasinoJackpotSystem(), // the drunk timer, a winner's deadline and its states 152, 155-157 (ADR-0396)

        new HeroSystem(), // a hero's standing job and its party walk, instead of the choosers (ADR-0216)
        new CreatureJobSystem(),

        new CreatureAngerSystem(),

        new CreatureExperienceSystem(),
    ];

    /// <summary>The level's verdict, then the world's own records.</summary>
    private static ISimulationSystem[] Verdict() =>
    [
        new DungeonHeartSystem(), // verdict is the last thing DK2's player frame does
        new TremorSystem(), // world records after the entity and player passes
    ];
}
