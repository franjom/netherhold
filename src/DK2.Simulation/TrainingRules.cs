// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>What a second in the training room is worth, and what it costs the
/// keeper. CONFIRMED (DKII.EXE v1.70, 2026-09-07; Q205, docs/research/training.md).
/// DK2 pays not from any of the six training states (108–113) but from the state
/// <b>category</b>'s update hook — category 13, carried by five of the six — the same
/// trick behind the prison drain (14), research (4) and manufacture (5). The rate is
/// per second: the loader divides <c>ExpPerSecondTraining</c> by variable 19
/// (<see cref="SimulationTiming.LoaderRatePerTick"/>) and the hook adds one unit a
/// tick, truncating, so 30 pays <b>28 a game second</b>. <c>ExpPerSecond</c> is a
/// different mechanic, combat experience paid by category 22.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/training.md")]
public static class TrainingRules
{
    /// <summary>The level a training room cannot take a creature past:
    /// <c>TrainingRoomMaxExperienceLevel</c>, <b>4</b>. CONFIRMED (owner, Q124), and
    /// enforced in six separate places in the binary, so a creature that reaches the
    /// cap mid-session loses the state rather than earning nothing. Not implemented: a
    /// creature at the cap but below <c>CombatPitMaxExperienceLevel</c> is handed to
    /// the combat pit's finder, not refused (training.md §9).</summary>
    public static int LevelCap(GameDatabase database) =>
        database[VariableTypeId.TrainingRoomMaxExperienceLevel];

    /// <summary>The Training Room's drop and work state, 108 (<c>0x00485310</c>): a capped creature
    /// in it goes to <see cref="CapRefusalState"/>.</summary>
    public const int WalkToMachineState = 108;

    /// <summary>State 110 (<c>0x00485580</c>): returns 0 for a capped creature, which then idles.</summary>
    public const int CapRefusalState = 110;

    /// <summary>State 113 (<c>0x00485A60</c>): back in after the Training Room changed
    /// (<see cref="RoomEvictRules"/>, refused-states.md §4).</summary>
    public const int ReturningState = 113;

    /// <summary>113: a turn's wait, then 108 on a Training Room of its own keeper's (returning 1
    /// whether or not 108 is taken); anywhere else 0, idle.</summary>
    internal static bool Return(SimulationContext context, string system, CreatureState creature)
    {
        if (context.Tick.Value - creature.StateEnteredTick <= 1) return true;
        WorldState world = context.World;
        if (!world.Contains(creature.Tile)
            || RoomIndex.RoomAt(world, context.Database, (creature.Tile.Y * world.Width) + creature.Tile.X) is not { } room
            || room.Definition.TypeId != RoomTypeId.TrainingRoom || room.OwnerPlayerId != creature.OwnerPlayerId)
            return false;
        Systems.StateShadow.TryMove(context, system, creature, WalkToMachineState);
        return true;
    }

    /// <summary>Whether this creature is still low enough to gain from training.</summary>
    public static bool BelowCap(GameDatabase database, CreatureState state) =>
        state.Level < LevelCap(database);

    /// <summary>Experience for one of the original's ticks of training: the record's
    /// <c>ExpPerSecondTraining</c> made per-tick by the loader, then scaled by stat 16,
    /// which is 100% at every level in the shipped table — applied because the original
    /// applies it, so a mod that fills the column in still works. A Goblin's 30 gives 7
    /// a tick, 28 a game second, and its 12300 to level 4 takes <b>about 7 min
    /// 20 s</b> of unbroken training — a stopwatch number (training.md §4).</summary>
    public static int ExperiencePerOriginalTick(
        GameDatabase database, CreatureDef creature, CreatureState state)
    {
        if (!BelowCap(database, state)) return 0;

        int perTick = SimulationTiming.LoaderRatePerTick(creature.ExpPerSecondTraining);
        if (perTick == 0) return 0;

        return CreatureLevelRules.Scale(
            perTick,
            CreatureLevelRules.Percent(database, CreatureStatId.ExpPerSecondTraining, state.Level));
    }

    /// <summary>What one game second of one creature's training costs its keeper:
    /// <c>ModifyPlayerColdWhileTrainingPerSecond</c>, <b>5 gold</b> ("cold" is the
    /// shipped name and means gold). The original negates the tabled −5 and charges
    /// once a game second off a countdown reloaded from variable 19; it also pops a
    /// floating "−20" every fourth charge, which nothing here draws (training.md).</summary>
    public static int GoldPerSecond(GameDatabase database) =>
        -database[VariableTypeId.ModifyPlayerColdWhileTrainingPerSecond];

    /// <summary>Whether the keeper can pay for another second of it. The original's
    /// test is <c>&gt;=</c> against keeper gold (<c>player+0x7E</c>, <c>Money</c>), and a keeper
    /// who cannot pay <b>loses the creature's job</b> — training is the only job poverty evicts
    /// you from. The job handler, the enter hook <c>0x0047B230</c> and the update hook each test it.</summary>
    public static bool CanAfford(WorldState world, GameDatabase database, PlayerId playerId) =>
        GoldStore.Total(world, playerId) >= GoldPerSecond(database);

    /// <summary>Data1 of message 55, <see cref="PlayerMessageKind.TrainingUnpaid"/>, as the enter
    /// hook <c>0x0047B230</c> and the update hook <c>0x0047B2D0</c> both pass it.</summary>
    public const int UnpaidMessageVariant = 5;

    /// <summary>The per-second experience drain DK2 ships and then disables:
    /// <c>EPLossSecondWhileNotTraining</c>, which is <b>0</b>. The mechanism is live —
    /// a per-tick hook negates this variable whenever the creature's state category is
    /// not 13 — so DK2 was built to decay experience unless creatures kept training,
    /// and shipped the rate at zero. Inert by value, not for want of a reader (Q203);
    /// <see cref="CreatureLevelRules.Grant"/> already handles a non-zero one.</summary>
    public static int ExperienceLostPerSecondWhileNotTraining(GameDatabase database) =>
        database[VariableTypeId.EPLossSecondWhileNotTraining];
}
