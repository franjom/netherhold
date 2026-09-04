// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;

namespace DK2.Simulation;

/// <summary>
/// What a creature is currently doing.
///
/// Four states, because four is what the tile-work loop needs: look for
/// something to do, walk to it, do it, and carry away what doing it produced.
/// DK2's own creature state machine is much larger — Flame names dozens of state
/// constants — and reconstructing it is a later job (Q2, Q7). Nothing here
/// claims to be that machine.
/// </summary>
public enum CreatureActivity
{
    /// <summary>Standing still, waiting to re-evaluate what to do.</summary>
    Idle = 0,

    /// <summary>Walking towards a tile it intends to work on.</summary>
    MovingToJob = 1,

    /// <summary>Standing next to its target tile, applying work to it.</summary>
    Working = 2,

    /// <summary>
    /// Carrying gold to somewhere its keeper can keep it.
    ///
    /// Not a job an imp chooses — it is the tail of a mining job (Q31,
    /// ADR-0018), which is why it is a state rather than a
    /// <c>JobPriority</c>.
    /// </summary>
    DeliveringGold = 3,

    /// <summary>Walking to its own bed in a lair, to sleep in it.</summary>
    GoingToSleep = 4,

    /// <summary>
    /// Asleep in its own bed. The only state in which a lair heals a creature
    /// (ADR-0049, CONFIRMED by the owner).
    /// </summary>
    Sleeping = 5,

    /// <summary>
    /// Collapsed at zero health, lying on the ground, and not yet dead —
    /// DK2's <c>CreatureDyingStateDurationSeconds</c> window (ADR-0051,
    /// CONFIRMED by the owner).
    ///
    /// <para>A dying creature does nothing at all: it does not move, work,
    /// sleep, regenerate or grow hungry. It <b>keeps its bed</b>, because the
    /// way out of this state is an imp carrying it back to that bed. What ends
    /// the window is either that rescue or death.</para>
    /// </summary>
    Dying = 6,
}

/// <summary>
/// One creature in the world.
///
/// A class rather than a struct because a creature carries a path, and because
/// creatures are few where tiles are tens of thousands. Stored in a list whose
/// order is spawn order and is never sorted: the order creatures are updated in
/// is behaviour, in exactly the way the work queue's order is (ADR-0010).
///
/// <para>Only the fields some implemented mechanic actually reads are here.
/// Health, mood, anger, experience, hunger and the rest of DK2's creature record
/// arrive with the mechanics that read them, not before — the same discipline
/// <see cref="TileState"/> follows.</para>
/// </summary>
public sealed class CreatureState
{
    /// <summary>Stable identity for the life of the level. Assigned by <see cref="WorldState"/>.</summary>
    public int Id;

    /// <summary>Index into the CREATURES table — the definition this creature is an instance of.</summary>
    public byte CreatureTypeId;

    public byte OwnerPlayerId;

    /// <summary>
    /// Position within the dungeon in <see cref="Fp32"/> units, where
    /// <see cref="Fp32.One"/> is one tile. The centre of tile (x,y) is
    /// <c>(x * 4096 + 2048, y * 4096 + 2048)</c>.
    ///
    /// Sub-tile rather than per-tile because how long a creature takes to reach
    /// its work is gameplay: an imp crossing a dungeon is not teleporting from
    /// tile to tile. Fixed point rather than floating (ADR-0001) so that the
    /// same walk produces the same arrival tick on every machine. Never
    /// negative — the dungeon starts at the origin.
    /// </summary>
    public int PosX;
    public int PosY;

    /// <summary>
    /// Fractional movement carried between ticks, in units of
    /// <c>1 / SimulationTiming.TicksPerSecond</c> of an <see cref="Fp32"/> unit.
    ///
    /// A creature's speed is given per second and spent per tick, and integer
    /// division would throw the remainder away every tick — an imp at 1.84
    /// tiles/second would quietly walk 0.4% slow forever. Keeping the remainder
    /// makes the speed exactly what the data says without leaving integer maths.
    /// </summary>
    public int MoveRemainder;

    /// <summary>
    /// Gold in this creature's hands.
    ///
    /// CONFIRMED that an imp carries mined gold itself rather than pushing it
    /// straight into a treasury (Q31, owner observation): it mines into its own
    /// hands up to <see cref="Data.Model.CreatureDef.MaxGoldHeld"/>, runs to a
    /// place its keeper can keep gold, and puts it down there. That is why gold
    /// is on the creature and not on the player — the same reason it is on tiles
    /// and not on the player (ADR-0015). Gold in a creature's hands is gold that
    /// is <i>somewhere</i>, and while it is here it is not spendable.
    ///
    /// <para>Survives losing a job on purpose: <see cref="ClearJob"/> does not
    /// touch it. An imp whose seam collapsed is still holding what it mined.</para>
    ///
    /// <para><b>Not</b> seeded from <c>InitialGoldHeld</c> (Imp: 500). What that
    /// field is for is UNKNOWN and nothing here reads it.</para>
    /// </summary>
    public int GoldHeld;

    /// <summary>
    /// Current health. Seeded from the creature record's <c>Hp</c> on spawn and
    /// clamped to it, so <c>Hp</c> is the maximum as well as the starting value.
    ///
    /// <para>DK2 has no separate max-health field — a creature's level scales
    /// its stats through the <c>CreatureStats</c> table, which this project does
    /// not model yet, so today max health is simply the record's number. When
    /// levels arrive, the maximum becomes a function of level and this stays the
    /// current value.</para>
    ///
    /// <para><b>Reaching zero is not automatically death</b> — see
    /// <see cref="CreatureHealthRules"/>, and Q99 for the part that is still
    /// the owner's to confirm.</para>
    /// </summary>
    public int Health;

    /// <summary>
    /// Ticks since this creature last ate, counting up. Compared against the
    /// record's <c>HungerRate</c> in seconds.
    ///
    /// <para>Counts from spawn, so a creature arrives fed. It keeps counting
    /// while hungry — <see cref="ChickensNeeded"/> is what says "hungry", not
    /// this — because DK2 only resets the clock when a creature is
    /// <i>fully</i> sated, not on each chicken.</para>
    /// </summary>
    public int HungerTicks;

    /// <summary>
    /// How many chickens this creature still needs to be full, or 0 when it is
    /// not hungry. Set to the record's <c>HungerFill</c> the moment hunger
    /// arrives.
    ///
    /// <para><b>Nothing can bring this back down yet.</b> Eating needs chickens,
    /// chickens are objects, and the OBJECTS record is unparsed (M6). So a
    /// creature here becomes hungry and stays hungry — which is honest rather
    /// than harmless, and it is why hunger costs no health: in DK2 the price of
    /// going unfed is <i>anger</i> (the record's own <c>angerNoFood</c>), and
    /// there is no variable anywhere in the balance table that turns hunger into
    /// damage.</para>
    /// </summary>
    public int ChickensNeeded;

    /// <summary>
    /// The lair tile this creature's own bed stands on, or -1 if it has none.
    ///
    /// <para><b>One bed per tile, one tile per creature</b> — CONFIRMED by the
    /// owner: <i>"a lair is a room with tiles, on each tile only one
    /// bed/chair/whatever per creature can be present. When a creature enters a
    /// dungeon through a portal it goes to a lair, finds a free tile and
    /// occupies it with its bed."</i> So a lair holds exactly as many creatures
    /// as it has tiles, and this is the tile that belongs to this one.</para>
    ///
    /// <para><b>Occupancy is derived from this field and never stored on the
    /// tile</b>, the same discipline <see cref="RoomInstance"/> follows: a tile
    /// is taken if some creature points at it. That way a creature leaving,
    /// dying or being removed cannot leave a bed behind that nothing owns.</para>
    ///
    /// <para>The bed <i>object</i> is
    /// <see cref="Data.Model.CreatureDef.LairObjectId"/> — 22 for a Goblin, 26
    /// for a Vampire, 18 for a Bile Demon, and <b>0 for the Imp, Skeleton and
    /// Horny</b>, which is how the record says a creature has no bed at all.
    /// Drawing it needs the OBJECTS record (M6); occupying a tile does
    /// not.</para>
    /// </summary>
    public int LairTileIndex = -1;

    /// <summary>
    /// Ticks this creature has been awake, counting up towards the record's
    /// <c>TimeAwake</c> in seconds (240 for a Goblin).
    ///
    /// <para>Reset when it wakes, not when it falls asleep, so the cycle is
    /// "awake for TimeAwake, then asleep for TimeSleep" rather than a fixed
    /// period. Whether that is DK2's cycle is Q103.</para>
    /// </summary>
    public int AwakeTicks;

    /// <summary>Ticks this creature has been asleep, counting up towards <c>TimeSleep</c>.</summary>
    public int SleepTicks;

    /// <summary>
    /// Ticks this creature has spent collapsed, counting up towards
    /// <c>CreatureDyingStateDurationSeconds</c> (60).
    ///
    /// <para>Only meaningful while <see cref="Activity"/> is
    /// <see cref="CreatureActivity.Dying"/>. It is the rescue window the owner
    /// described: <i>"it remains lying on the ground for a period of time, that
    /// gives imps an opportunity to return a creature to its bed in the lair for
    /// healing, or to prison if it is an enemy creature"</i> — see
    /// <see cref="CreatureDeathRules"/>.</para>
    /// </summary>
    public int DyingTicks;

    /// <summary>
    /// How cross this creature is, from 0 to <see cref="CreatureAngerRules.Ceiling"/>
    /// (32767).
    ///
    /// <para>The ceiling is the owner's stopwatch, not a guess: a lairless
    /// Goblin accruing 100 a second became unhappy at 2:45 and angry at 5:30,
    /// which is 32767/2 and 32767 to within a percent (Q106). See
    /// <see cref="CreatureAngerRules"/>.</para>
    /// </summary>
    public int Anger;

    /// <summary>
    /// What this creature decided to do when it became angry, drawn once from
    /// its own <c>AngryJobs</c> table and then kept.
    ///
    /// <para>Kept rather than re-rolled because half of a Goblin's table is
    /// <c>Rebel</c>, which is not implemented: a creature re-rolling every
    /// second would eventually draw <c>Leave</c> by luck and desert, which
    /// would misreport the unbuilt outcome as the built one.</para>
    /// </summary>
    public Data.Model.JobType AngryChoice;

    public CreatureActivity Activity;

    /// <summary>The tile this creature intends to work on, or -1 for none.</summary>
    public int TargetTileIndex = -1;

    /// <summary>Which kind of work it intends to apply. Meaningless when <see cref="TargetTileIndex"/> is -1.</summary>
    public TileWorkKind TargetWork;

    /// <summary>
    /// Countdown in ticks. What it counts down to depends on
    /// <see cref="Activity"/>: the next re-evaluation when idle, the next work
    /// application when working.
    /// </summary>
    public int TimerTicks;

    /// <summary>
    /// The route being walked, excluding the tile it started from. Empty when
    /// the creature is not going anywhere.
    /// </summary>
    public readonly List<TilePos> Path = [];

    /// <summary>How far along <see cref="Path"/> the creature is.</summary>
    public int PathIndex;

    /// <summary>The tile the creature is standing on.</summary>
    public TilePos Tile => new(PosX / Fp32.One, PosY / Fp32.One);

    /// <summary>The centre of a tile, in position units.</summary>
    public static int CentreOf(int tile) => (tile * Fp32.One) + (Fp32.One / 2);

    /// <summary>
    /// Forgets what this creature was doing. Deliberately leaves
    /// <see cref="GoldHeld"/> alone: what is in an imp's hands is not part of
    /// its job.
    /// </summary>
    public void ClearJob()
    {
        Activity = CreatureActivity.Idle;
        TargetTileIndex = -1;
        TargetWork = default;
        Path.Clear();
        PathIndex = 0;
    }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(Id);
        hash.Add(CreatureTypeId);
        hash.Add(OwnerPlayerId);
        hash.Add(PosX);
        hash.Add(PosY);
        hash.Add(MoveRemainder);
        hash.Add(GoldHeld);
        hash.Add(Health);
        hash.Add(HungerTicks);
        hash.Add(ChickensNeeded);
        hash.Add(LairTileIndex);
        hash.Add(AwakeTicks);
        hash.Add(SleepTicks);
        hash.Add(DyingTicks);
        hash.Add(Anger);
        hash.Add((int)AngryChoice);
        hash.Add((int)Activity);
        hash.Add(TargetTileIndex);
        hash.Add((int)TargetWork);
        hash.Add(TimerTicks);
        hash.Add(Path.Count);
        foreach (TilePos p in Path)
        {
            hash.Add(p.X);
            hash.Add(p.Y);
        }
        hash.Add(PathIndex);
    }
}
