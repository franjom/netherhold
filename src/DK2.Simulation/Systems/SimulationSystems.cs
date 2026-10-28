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
        .. Promotion(),
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

    /// <summary>DK2's thing pass: every creature's update, then shots, objects and traps, then
    /// the second walk that removes whatever died.</summary>
    private static ISimulationSystem[] ThingPass() =>
    [
        new CreatureStatusSystem(), // the status countdowns (0x0048E160) before the fight reads their words
        new FootprintSystem(),
        CreatureUpdate(),
        // The doors' health after the creatures, as DK2's thing pass updates creatures first; ours
        // puts it before the shots, whose place in that pass is Q398 (door-blocking.md).
        new DoorHealthSystem(),
        // Shots fly after the creatures have moved; where DK2's thing list puts them is Q398.
        new TrainingActionSystem(), // creature action gates before the shots and object reactions they create
        new ShotSystem(),
        new BoulderSystem(), // objects (list 2) after shots (1), before traps (3)
        new ObjectMovementSystem(),
        new TrapSystem(), // after the shots, as DK2's class lists run shots (1) before traps (3)
        new ActionPointTriggerSystem(), // list 5, after creature work and before the player frame
        // The thing pass's second walk: whatever is dead now is removed this turn (0x004B7830).
        new TrapRemovalSystem(),
        new DoorRemovalSystem(),
        new ReaperRemovalSystem(),
        // The Call to Arms flag is an object the shot puts down; its recall, release and the
        // heeding states run in the thing update, before the payment that keeps it (ADR-0279).
        // Ours after the whole creature list, not in each creature's update: Call to Arms' heeding
        // states, state 96 and the gold underfoot.
        new CallToArmsSystem(),
        new ChickenSystem(), // state 96: the walk to the Hatchery or the wander (keeper-spell-inferno-chicken.md §3)
        new FogOfWarSystem(), // after every creature has moved: an arrival is noticed on its own tick
        new WalkOverGoldSystem(), // gold underfoot, after movement (ADR-0300)
    ];

    /// <summary>One creature's whole update before the next one's, newest first (<c>0x004B7830</c>,
    /// ADR-0554): the fight scan, the dispatcher's needs routine and mood chooser, the state handler,
    /// then the steering step (<c>0x00488930</c>, <c>0x00476FC0</c>); an imp's walk step is in its handler.</summary>
    private static CreatureUpdateSystem CreatureUpdate()
    {
        var combat = new CombatSystem();
        return new CreatureUpdateSystem(
            combat, // the action runner, the fight scan and the fear radiation
            // The needs routine: hunger, then sleep, then pay (creature-needs-routine.md §3).
            new CreatureEatSystem(),
            new CreatureSleepSystem(),
            new PaydaySystem(), // its clock, before the first creature (0x0050A7BE): credited and set off this tick
            new CreatureAngerSystem(), // the ladder and the angry chooser, after the needs routine
            // The state handlers. A creature holds one state, so their order among themselves is
            // only which system owns it. A creature the scan engaged runs its fight state now (ADR-0555).
            combat.States, // the combat states, then the trap reveal (0x0048A0B4)
            new TremorCreatureSystem(), // recovery handler; world Tremor runs after the entity pass
            new ReaperSystem(),
            new FinaleSystem(), // the finale Reaper's states 228-234; the fireworks after the last creature
            new CombatPitSystem(), // the pit's states (ADR-0213)
            new JackInTheBoxSystem(), // state 273 and the travel toll
            new CasinoJackpotSystem(), // the drunk timer, a winner's deadline and its states 152, 155-157 (ADR-0396)
            new HeroSystem(), // a hero's standing job and its party walk, instead of the choosers (ADR-0216)
            new CreatureJobSystem(), // a non-worker's jobs and its happy and unhappy choosers
            new MovementDustSystem(), // foot dust for last tick's crossing onto dirt, before the mover (ADR-0477)
            new CreatureMovementSystem(),
            // A worker's handler after the mover: DK2 steps an imp inside its walk state's handler,
            // before that handler's arrival test (0x00470C80), and the steering step skips imps.
            // Each worker resolves its queued work before the next.
            new ImpTaskSystem());
    }

    /// <summary>Tile work queued this tick and not yet resolved, and what burns after it.</summary>
    private static ISimulationSystem[] Work() =>
    [
        // Each worker resolved its own work in its update; this drains any other orders.
        // Bridges burn after the work (Q67), which makes the fire final.
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
        new PlayerTriggerSystem(), // player root before the player's counters and mana
        new ManaRemovalSystem(),
        new RatSpawnSystem(),
        // Mana after all ownership changes, so a tile claimed on N pays on N; the imp
        // it buys next, so N's mana casts on N; a lair built on N attracts on N.
        new ManaSystem(),
        new ImpSupplySystem(),
        new RoomBuildWaveSystem(), // keeper frame after mana, before its research chooser (0x004BB614)
        new ResearchSystem(), // the same player frame chooses a research target when it has none
        new ManufactureDecaySystem(), // the player update's manufacture loss (0x004C3340, ADR-0203)
    ];

    /// <summary>The hatchery's laying, health, and a prisoner's lot. The needs routine itself runs
    /// in each creature's update (<see cref="ThingPass"/>).</summary>
    private static ISimulationSystem[] Needs() =>
    [
        // Laying, once a game second (ADR-0066).
        new HatcherySystem(),
        new ObjectReactionSystem(),

        // Health, prison: one asleep this tick heals this second; health settles damage before
        // death is checked; the health system skips prisoners, so exactly one system settles a
        // prisoner's second (ADR-0064).
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

    /// <summary>Experience last: nothing that died or left is promoted (Q124).</summary>
    private static ISimulationSystem[] Promotion() =>
    [
        new CreatureExperienceSystem(),
    ];

    /// <summary>The level's verdict, then the world's own records.</summary>
    private static ISimulationSystem[] Verdict() =>
    [
        new DungeonHeartSystem(), // verdict is the last thing DK2's player frame does
        new TremorSystem(), // world records after the entity and player passes
    ];
}
