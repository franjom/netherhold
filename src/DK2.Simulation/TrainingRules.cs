// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// What a second in the training room is worth, and what it costs the keeper.
///
/// <para>Read out of DKII.EXE v1.70 on 2026-09-07 (Q205). The training room was
/// a job a creature would choose and walk to and be paid nothing for; this is
/// the payment.</para>
///
/// <para><b>Where it lives in the original, which is not where it was looked
/// for.</b> None of the six training <i>states</i> (108–113) awards anything.
/// DK2 hangs the payment on the state's <b>category</b> instead: every state
/// record carries a category byte, there are 32 category records of 0x40 bytes
/// at <c>0x00699688</c>, each carries an enter hook at <c>+0x04</c>, an update
/// hook at <c>+0x14</c> and a leave hook at <c>+0x24</c>, and the per-tick state
/// dispatcher calls the current category's update hook immediately before the
/// state's own handler. <b>Category 13 is training</b> and its update hook is
/// what pays. Five of the six training states carry category 13, so all five
/// pay identically and the payment did not have to be written five times — the
/// same trick that gave the prison its drain (category 14) and gives research
/// and manufacture theirs (categories 4 and 5).</para>
///
/// <para><b>The rate is genuinely per second and the name is right.</b> The
/// worry going in was <c>ExpPerSecond</c>, which reads 1200 for an Imp against a
/// balance-table <c>ImpExperienceGainPerSecond</c> of 20 — a 60x "agreement"
/// this project had read as proof that the record's fields were per minute. They
/// are not. The loader divides <c>ExpPerSecondTraining</c> by <b>variable 19</b>
/// as it stores it (<see cref="SimulationTiming.LoaderRatePerTick"/>), which is
/// the signature of a per-second number becoming a per-tick one, and the hook
/// adds one such unit a tick. <b>The 1200 was a coincidence between two
/// unrelated mechanics</b> — <c>ExpPerSecond</c> is combat experience, paid by
/// category 22 (battle, fleeing, threat-response), which is the owner's own
/// <i>"to get to level 10 they need to fight actual fights"</i>.</para>
///
/// <para><b>The truncation is real and is reproduced.</b> 30 / 4 = 7, so a
/// creature carrying 30 earns <b>28 a game second, not 30</b>.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/training.md")]
public static class TrainingRules
{
    /// <summary>
    /// The level a training room cannot take a creature past:
    /// <c>TrainingRoomMaxExperienceLevel</c>, <b>4</b>.
    ///
    /// <para><b>CONFIRMED by the owner</b> (Q124) — <i>"creatures can get up to
    /// level 4 using the training room"</i> — and the binary enforces it in
    /// <b>six</b> separate places, not one: the job handler that picks the job,
    /// the category's update hook, and four of the six training state handlers
    /// (108, 110, 111, 112). A creature that reaches the cap mid-session
    /// therefore stops earning <i>and</i> loses the state, rather than standing
    /// there earning nothing. The category's <i>enter</i> hook is the one place
    /// that does not test it — it tests only the keeper's gold — so the cap is
    /// enforced on the way through rather than at the door.</para>
    ///
    /// <para>The job handler has one further clause: a creature at or above this
    /// cap but below <c>CombatPitMaxExperienceLevel</c> is handed to the combat
    /// pit's finder instead of being refused. So <b>"too good to train" routes
    /// to the pit</b> rather than failing, which is the shape the owner
    /// described. Unbuilt — there is no combat pit.</para>
    /// </summary>
    public static int LevelCap(GameDatabase database) =>
        database[VariableTypeId.TrainingRoomMaxExperienceLevel];

    /// <summary>Whether this creature is still low enough to gain from training.</summary>
    public static bool BelowCap(GameDatabase database, CreatureState state) =>
        state.Level < LevelCap(database);

    /// <summary>
    /// Experience for one of the original's ticks of training: the record's
    /// <c>ExpPerSecondTraining</c> converted to a per-tick rate by the loader,
    /// then scaled by the creature's level.
    ///
    /// <para><b>The scaling column is stat 16</b>, which is 100% at every level
    /// in the shipped table — so this scales nothing today. It is applied
    /// because the original applies it, and because a column of hundreds is data
    /// rather than an absence: a mod that filled it in would change the game and
    /// should change this too.</para>
    ///
    /// <para><b>What the numbers come to.</b> A Goblin's 30 gives 7 a tick, 28 a
    /// game second. Its thresholds are 3000, 3900 and 5400 (the record's 3000 at
    /// 100%, 130% and 180%), so reaching the cap at level 4 takes
    /// 12300 / 28 = <b>about 7 minutes 20 seconds</b> of unbroken training. That
    /// is a number the owner can hold a stopwatch to.</para>
    /// </summary>
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

    /// <summary>
    /// What one game second of one creature's training costs its keeper:
    /// <c>ModifyPlayerColdWhileTrainingPerSecond</c>, <b>5 gold</b>.
    ///
    /// <para><b>"Cold" is the shipped name and it means gold.</b> The variable
    /// is read at five sites and two of them are the training room's; the
    /// original tests <c>keeper gold &gt;= 5</c> before entering the state and
    /// again on every charge, and spends on a countdown reloaded from variable
    /// 19 — once a game second. It also negates the tabled figure, so the
    /// table's −5 is a charge of 5 and this returns a positive amount.</para>
    ///
    /// <para><b>A quirk worth keeping for when the UI exists:</b> the original
    /// counts its own charges and pops a floating <i>"−20"</i> over the creature
    /// every fourth one, so the player sees the cost in twenties every four
    /// seconds rather than in fives every second. Nothing here draws it; the
    /// accounting is per second either way.</para>
    /// </summary>
    public static int GoldPerSecond(GameDatabase database) =>
        -database[VariableTypeId.ModifyPlayerColdWhileTrainingPerSecond];

    /// <summary>
    /// Whether the keeper can pay for another second of it.
    ///
    /// <para>The original's test is <c>&gt;=</c> against the keeper's gold, and
    /// a keeper who cannot pay <b>loses the creature's job</b>: the category
    /// hook returns failure and the dispatcher puts the creature back to idle.
    /// Training is the only job in the game that a keeper can be evicted from
    /// by being poor.</para>
    /// </summary>
    public static bool CanAfford(WorldState world, GameDatabase database, byte playerId) =>
        GoldStore.Total(world, playerId) >= GoldPerSecond(database);

    /// <summary>
    /// The per-second experience drain DK2 ships and then disables:
    /// <c>EPLossSecondWhileNotTraining</c>, which is <b>0</b>.
    ///
    /// <para>Recorded rather than implemented, because the mechanism is real and
    /// only the number is dead. The original has a per-second hook that calls
    /// its experience adder with the negation of this variable whenever the
    /// creature's current state category is <b>not</b> 13 — that is, whenever it
    /// is not training. So DK2 was built to make experience <i>decay</i> unless
    /// you kept your creatures in the training room, and shipped with the rate
    /// set to zero.</para>
    ///
    /// <para>It is the ninth balance variable found to be inert (Q203), and the
    /// first one that is inert by <i>value</i> rather than by having no reader at
    /// all — which is a different kind of dead and a more interesting one. If it
    /// is ever non-zero in a mod, <see cref="CreatureLevelRules.Grant"/> already
    /// handles the negative case the way the original does.</para>
    /// </summary>
    public static int ExperienceLostPerSecondWhileNotTraining(GameDatabase database) =>
        database[VariableTypeId.EPLossSecondWhileNotTraining];
}
