// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Simulation.Systems;

/// <summary>
/// The systems the game runs, in the order it runs them: the one declaration of
/// that order, shared by the tests, the scenario harness and any front end
/// (ADR-0013). Two systems that touch the same state in a different order
/// produce a different game, so every ordering constraint states its reason at
/// the entry it constrains. UNKNOWN: DK2's own per-tick subsystem order —
/// Flame's disassembly names the subsystems but not their sequence, so this
/// order is ours, chosen to be explicable rather than to match something we
/// have not measured.
/// </summary>
public static class SimulationSystems
{
    public static ISimulationSystem[] Default() =>
    [
        // The keeper's own hand first: one picked up this tick does not also
        // take a step, and one dropped onto marked work starts at once (ADR-0062).
        new HandSystem(),

        // The player's commands: a tile marked on tick N is marked for all of
        // tick N; selling before building lets one room pay for another on the
        // same tick; building after tagging, so a tile both marked and built on
        // in one tick ends up a room.
        new TileTagSystem(),
        new RoomSellSystem(),
        new RoomBuildSystem(),
        // Movement, then the fog it uncovers: a worker that arrives beside its
        // job is noticed on the tick it arrives rather than the next.
        new CreatureMovementSystem(),
        new FogOfWarSystem(),
        // Workers queue work and the queue resolves in the same tick, so
        // nothing survives a tick boundary half-done. Bridges burn after the
        // work (Q67), making the fire final rather than outrunnable by a tick.
        new ImpTaskSystem(),
        new TileWorkSystem(),
        new BridgeBurnSystem(),
        // Mana after everything that can change what a keeper owns, so a tile
        // claimed on tick N is worth mana on tick N; the imp it pays for after
        // the mana, so the price earned on tick N can be cast on tick N and the
        // new imp starts looking for work on tick N+1.
        new ManaSystem(),
        new ImpSupplySystem(),
        // A lair built on tick N attracts on tick N; the creature born acts
        // from tick N+1 either way, so nothing above it is affected.
        new PortalSystem(),

        // The laying half of eating, once a game second (ADR-0066), before
        // anything eats.
        new HatcherySystem(),
        // Hunger is asked BEFORE tiredness, the order the original's own needs
        // routine has: its hunger block short-circuits and returns before the
        // sleep block is reached (docs/research/eating.md).
        new CreatureEatSystem(),

        // Sleep before health, so a creature that falls asleep this tick is
        // healed in the same second; health last of the per-second pair, so
        // damage dealt earlier in the tick is settled before death is checked.
        new CreatureSleepSystem(),
        new CreatureHealthSystem(),

        // Straight after it, on the same second and the same phase: a
        // prisoner's health is the prison's business alone, and the health
        // system skips them so that exactly one system settles their second
        // (ADR-0064).
        new PrisonSystem(),

        // The rest of being a prisoner, every tick. After PrisonSystem, so one
        // freed or turned to bone is not given a step it will never take.
        // Eating before wandering: one handler in the original's order, which
        // asks about hunger first and wanders only when the answer is no
        // (ADR-0074, ADR-0073).
        new PrisonerEatSystem(),
        new PrisonerWanderSystem(),

        // Payday after the needs, the original's order — hunger, then sleep,
        // then pay — and before the anger ladder, because the charge it ought
        // to make (AngerNoPay) is not built and will want to land before the
        // ladder is walked rather than after.
        new PaydaySystem(),

        // The mood's job chooser after the needs and before the anger, the
        // original's own sequence (ADR-0082), and so a creature that found no
        // work this second takes AngerNoWork in the second it looked.
        new CreatureJobSystem(),

        new CreatureAngerSystem(),

        // Experience last of all: a creature is paid for a second it actually
        // worked, and nothing that died or walked out this second is promoted
        // on its way through the door (Q124).
        new CreatureExperienceSystem(),
    ];
}
