// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>
/// What a creature that is not a worker does with its day — <b>the last of
/// M4's seven items</b>, and the thing that had a Goblin standing on the portal
/// it came out of unless it was hungry, tired or leaving.
///
/// <para><b>Read from the original's two remaining mood choosers</b>
/// (<c>FUN_004780e0</c> happy, <c>FUN_004782c0</c> unhappy) and the dispatcher
/// they share, <c>FUN_00478800</c> — ADR-0082,
/// <c>docs/research/job-selection.md</c>. The third chooser, the angry one, has
/// been in <see cref="CreatureAngerSystem"/> since ADR-0061 and stays there;
/// the two here are its siblings and are built to match it line for line, which
/// is why the roll, the ranges and the five-second gate are all shared code
/// rather than copies.</para>
///
/// <para>A selection goes:</para>
///
/// <list type="number">
/// <item><b>Not more often than every five game seconds</b>, per mood, on that
/// mood's own timestamp. The original keeps three of them side by side in the
/// creature — happy, unhappy, angry — so a creature that has just rolled while
/// content is free to roll again the moment it turns unhappy.</item>
/// <item><b>The unhappy chooser tries for a lair first</b> and the happy one
/// does not, because the idle state already did (Q150, ADR-0060). Both are the
/// original's, and the asymmetry is in the code rather than inferred.</item>
/// <item><b>Cumulative ranges over the mood's table, then <c>rand(100)</c></b>
/// — inclusive at both ends, in record order, exactly as
/// <see cref="CreatureAngerRules.CumulativeRanges"/> builds them and with the
/// same two quirks reproduced.</item>
/// <item><b>Try the job whose range holds the roll; if it fails, try every job
/// in the table in list order.</b> A job "fails" when its handler says no, and
/// what a handler tests is <see cref="JobRules"/>.</item>
/// <item><b>If every job in the table failed and the creature was content, it
/// takes <c>AngerNoWork</c></b> — and if it was unhappy, it takes nothing at
/// all. See <see cref="JobRules.ChargesNoWorkAnger"/>; the asymmetry is the
/// original's.</item>
/// </list>
///
/// <para><b>What a job then does is mostly nothing yet, and the log says which
/// nothing.</b> Only Praying has an effect built, which is the deliberate order:
/// since ADR-0081 the Temple is the only thing in DK2 that drains an anger
/// bucket it did not fill, so it is the one job whose absence leaves a hole in
/// a mechanic that already exists. A creature that takes Research, Train, Guard
/// or Drink walks to the room and stands in it, and every second of that is a
/// second it is not idle and not accruing <c>AngerNoWork</c> — which is
/// behaviour, and is the half of each job the choosers actually depend on.</para>
///
/// <para><b>What is ours.</b> The walk to the room is
/// <see cref="CreatureEatSystem"/>'s, tile for tile — nearest room by squared
/// distance to its centre, first reachable one wins, arrive at the centre or at
/// the lowest tile index when the centre is not in the room. The original's job
/// states each do their own travelling and none of them was read. <b>How long a
/// creature keeps a job is also ours, and there is evidence that it is not a
/// timer</b>: DK2 ships <c>MotionlessWhileResearchingSeconds</c>,
/// <c>...Manufacturing...</c>, <c>...Praying...</c>, <c>...Training...</c>,
/// <c>StateCounterWhileGuarding</c> and <c>MinimumJobDurationSeconds</c>, and
/// <b>every one of the six is read by nothing at all</b> (ADR-0082). So this
/// keeps a job until the room stops being the creature's keeper's, or a need
/// takes the creature away, and re-chooses when that leaves it idle.</para>
///
/// <para><b>Order.</b> After the needs — hunger, sleep, pay — and before
/// <see cref="CreatureAngerSystem"/>, which is the original's own sequence: its
/// decision routine runs the current state, then the needs, then the mood's
/// chooser. Before the anger system so that a creature which found no work this
/// second is charged for it in the same second it looked, rather than one
/// later.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/job-selection.md")]
public sealed class CreatureJobSystem : ISimulationSystem
{
    private const string SystemName = "CreatureJob";

    private readonly IPathfinder _pathfinder;
    private readonly LairSeeker _seeker;
    private readonly List<TilePos> _route = [];
    private readonly List<(JobType Job, int Low, int High)> _ranges = [];

    public CreatureJobSystem() : this(new GridPathfinder()) { }

    public CreatureJobSystem(IPathfinder pathfinder)
    {
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
        _seeker = new LairSeeker(_pathfinder);
    }

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);

            // Workers have their own whole system and their own priority order
            // (ADR-0018). DK2 dispatches them before any of this too - an Imp's
            // happy table is Explore at 100%, and nothing here would know what
            // to do with it.
            if (definition.Flags.HasFlag(CreatureFlags.IsWorker)) continue;

            if (HandRules.IsHeld(creature)) continue;
            if (PrisonRules.IsImprisoned(creature)) continue;
            if (CreatureDeathRules.IsDown(creature)) continue;
            if (creature.Activity == CreatureActivity.Leaving) continue;

            switch (creature.Activity)
            {
                case CreatureActivity.GoingToJob:
                    Travelling(context, creature, definition);
                    break;

                case CreatureActivity.AtWork:
                    Working(context, creature, definition);
                    break;

                case CreatureActivity.Idle:
                    Choose(context, creature, definition);
                    break;
            }
        }
    }

    // ------------------------------------------------------------- choosing

    /// <summary>
    /// The happy and unhappy choosers, which differ in exactly two places: the
    /// lair attempt, and who pays for coming back empty-handed.
    /// </summary>
    private void Choose(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        CreatureMood mood = CreatureAngerRules.MoodOf(definition, creature);

        // An angry creature is CreatureAngerSystem's, and has been since
        // ADR-0061. Two systems rolling on two tables in one second would be
        // two decisions where DK2 makes one.
        if (mood == CreatureMood.Angry) return;

        long now = context.Tick.Value;
        long last = mood == CreatureMood.Content
            ? creature.LastHappyChoiceTick
            : creature.LastUnhappyChoiceTick;

        if (last != 0 && now - last < JobRules.ReselectionGateTicks) return;

        // THE UNHAPPY CHOOSER TRIES FOR A LAIR FIRST AND THE HAPPY ONE DOES
        // NOT, and both halves are the original's. The happy path does not need
        // to because the idle state's own first act for a bedless creature is
        // the same attempt (Q150); the unhappy path is reached from elsewhere
        // and repeats it.
        if (mood == CreatureMood.Unhappy
            && !LairRules.HasBed(context.World, context.Database, creature)
            && _seeker.TryGoForALair(context, SystemName, creature, definition))
        {
            creature.LastUnhappyChoiceTick = now;
            return;
        }

        IReadOnlyList<CreatureJobPreference> table = JobRules.TableFor(definition, mood);
        CreatureAngerRules.CumulativeRanges(table, _ranges);
        int roll = CreatureAngerRules.RollPercent(context.Rng);

        // A SELECTION THAT FAILS STAMPS NOTHING, so a creature with nothing to
        // do asks again on the very next tick - which is the original's shape
        // (its gate is at the top and its failure path falls straight out) and
        // is what makes a keeper who builds a training room see the effect at
        // once. It also means twenty identical refusals a second, so the
        // REPORTING is throttled to the same once-a-second phase the charge
        // below uses. The decision is per tick; only the log is not.
        bool report = OnTheSecond(context);

        // The rolled entry first, then every entry in record order - which is
        // the original's fall-through and is why a table's chances are a
        // preference rather than a partition. Refusals are reported from the
        // second pass only, because the first is a subset of it and a
        // single-entry table would otherwise say the same thing twice.
        int taken = -1;
        for (int i = 0; i < _ranges.Count && taken < 0; i++)
        {
            (JobType job, int low, int high) = _ranges[i];
            if (job == JobType.None || roll < low || high < roll) continue;
            if (TryTake(context, creature, definition, job, roll, "rolled", false)) taken = i;
        }

        for (int i = 0; i < _ranges.Count && taken < 0; i++)
        {
            JobType job = _ranges[i].Job;
            if (job == JobType.None) continue;
            if (TryTake(context, creature, definition, job, roll, "fell_through", report)) taken = i;
        }

        if (taken >= 0)
        {
            if (mood == CreatureMood.Content) creature.LastHappyChoiceTick = now;
            else creature.LastUnhappyChoiceTick = now;
            return;
        }

        // Nothing in the table could be done. ONLY A CONTENT CREATURE PAYS FOR
        // THAT - the unhappy chooser's tail is a bare return where the happy
        // one's charges angerNoWork (JobRules.ChargesNoWorkAnger).
        //
        // Nothing is stamped either way, exactly as the angry chooser leaves a
        // failed selection unstamped: the creature is free to try again next
        // second, and the answer will be the same until the dungeon changes.
        if (!JobRules.ChargesNoWorkAnger(mood)) return;
        if (definition.AngerNoWork == 0) return;
        if (!report) return;

        CreatureAngerRules.Apply(definition, creature, AngerBucket.Work, definition.AngerNoWork);

        context.Emit(SystemName, "no_work",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} roll={roll} anger=+{definition.AngerNoWork} " +
            $"work_anger={creature.Anger.Work} " +
            $"tried=[{string.Join(",", _ranges.Select(r => r.Job))}]");
    }

    /// <summary>
    /// One job's handler: its own test, then the room, then the walk. Says why
    /// not in the log whichever part refused, which is the "logged reasons"
    /// half of M4's seventh item.
    /// </summary>
    private bool TryTake(
        SimulationContext context, CreatureState creature, CreatureDef definition,
        JobType job, int roll, string how, bool report)
    {
        string? refused = JobRules.Refused(context.World, context.Database, definition, creature, job);
        RoomTypeId? wants = JobRules.RoomFor(job);

        if (refused is null && wants is null)
        {
            // Leave, Rebel, Explore, StealGold and the rest. Real DK2 outcomes
            // with no room to walk to and nothing here to act on; the angry
            // chooser reports them the same way and neither invents one.
            refused = "no_such_job_here";
        }

        if (refused is null && !TrySetOut(context, creature, definition, job, wants!.Value))
        {
            refused = "no_room";
        }

        if (refused is not null)
        {
            if (report)
            {
                context.Emit(SystemName, "job_refused",
                $"creature={creature.Id} type={definition.Name} " +
                    $"player={creature.OwnerPlayerId} job={job} roll={roll} how={how} " +
                    $"reason={refused}");
            }

            return false;
        }

        creature.CurrentJob = job;
        context.Emit(SystemName, "job_taken",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} job={job} roll={roll} how={how} " +
            $"room={wants} effect={(JobRules.EffectIsBuilt(job) ? "built" : "unbuilt")} " +
            $"steps={creature.Path.Count}");
        return true;
    }

    /// <summary>
    /// Choose the room and set off for it.
    ///
    /// <para>Nearest first by squared distance to the room's centre, the first
    /// one this creature can actually reach over its own
    /// <see cref="WorldPathGrid"/> wins, equal distances broken by the lowest
    /// tile index. Deliberately the same rule as the hatchery walk and the lair
    /// walk, down to the arrival tile: an L-shaped room's centre may not be in
    /// it, in which case the walk is at its lowest tile instead.</para>
    ///
    /// <para><b>The keeper's own rooms only.</b> An ally's library is no use to
    /// a creature, exactly as an ally's hatchery is not.</para>
    /// </summary>
    private bool TrySetOut(
        SimulationContext context, CreatureState creature, CreatureDef definition,
        JobType job, RoomTypeId wants)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        var candidates = new List<(long Distance, int FirstTile, RoomInstance Room)>();
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != creature.OwnerPlayerId) continue;
            if (room.Definition.TypeId != wants) continue;
            if (room.TileIndices.Count == 0) continue;

            candidates.Add((
                EatingRules.DistanceSquared(world, creature.Tile, CentreTileIndex(world, room)),
                room.TileIndices[0], room));
        }

        candidates.Sort((a, b) =>
            a.Distance != b.Distance
                ? a.Distance.CompareTo(b.Distance)
                : a.FirstTile.CompareTo(b.FirstTile));

        var grid = new WorldPathGrid(database, world, definition);
        foreach ((_, _, RoomInstance room) in candidates)
        {
            int target = ArrivalTile(world, room);
            var at = new TilePos(target % world.Width, target / world.Width);
            if (!_pathfinder.TryFindPath(grid, creature.Tile, at, _route)) continue;

            creature.Path.Clear();
            creature.Path.AddRange(_route);
            creature.PathIndex = 0;
            creature.JobTileIndex = target;
            creature.Activity = CreatureActivity.GoingToJob;

            // Standing in it already - a creature that rolls Train while stood
            // in the training room. No walk to make, so it is at work now.
            if (creature.Path.Count == 0) Arrive(context, creature, definition, job);
            return true;
        }

        return false;
    }

    // -------------------------------------------------------------- working

    /// <summary>Walking there. Nothing happens until the route runs out.</summary>
    private void Travelling(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.PathIndex < creature.Path.Count) return;
        Arrive(context, creature, definition, creature.CurrentJob);
    }

    private void Arrive(
        SimulationContext context, CreatureState creature, CreatureDef definition, JobType job)
    {
        creature.Path.Clear();
        creature.PathIndex = 0;
        creature.Activity = CreatureActivity.AtWork;

        context.Emit(SystemName, "job_started",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} job={job} " +
            $"at=({creature.Tile.X},{creature.Tile.Y})");
    }

    /// <summary>
    /// At work, once a game second.
    ///
    /// <para>The room is re-tested every second: sold, dug out or taken by a
    /// rival while the creature stood in it and the job simply ends, which is
    /// the same shape as a bed that stops being a bed
    /// (<see cref="CreatureSleepSystem"/>). A creature whose job ends is idle
    /// and re-chooses at its next gate.</para>
    /// </summary>
    private void Working(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        bool second = OnTheSecond(context);
        bool originalTick = SimulationTiming.OnOriginalTick(context.Tick.Value);
        if (!second && !originalTick) return;

        WorldState world = context.World;
        GameDatabase database = context.Database;
        JobType job = creature.CurrentJob;
        RoomTypeId? wants = JobRules.RoomFor(job);

        if (second && (wants is null
                       || !StandingInOneOfIts(world, database, creature, wants.Value)))
        {
            context.Emit(SystemName, "job_lost",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} job={job} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=room_gone");
            Stop(creature);
            return;
        }

        // TRAINING IS THE ONE JOB PAID ON THE ORIGINAL'S OWN CADENCE, four
        // times a game second rather than once (Q205). Not a flourish: DK2
        // discards the overshoot when a creature levels, so the size of an
        // award decides how much is thrown away and collapsing four into one
        // would leave a different balance behind.
        if (job == JobType.Train)
        {
            if (originalTick) Train(context, world, database, creature, definition);
            if (second) ChargeForTraining(context, world, database, creature, definition);
            return;
        }

        if (!second || job != JobType.Praying) return;

        // THE ONLY JOB WITH AN EFFECT, and the only source in the game that
        // drains an anger bucket it did not fill (ADR-0081). Six calls, one per
        // bucket, at -275 each; then 33 health, clamped by the health rules to
        // what is actually missing.
        CreatureAnger before = creature.Anger;
        CreatureAngerRules.Apply(definition, creature, JobRules.PrayingCalm(database));
        int healed = CreatureHealthRules.Apply(
            database, definition, creature, JobRules.PrayingHeal(database));

        if (before.Equals(creature.Anger) && healed == 0) return;

        context.Emit(SystemName, "prayed",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} calm={JobRules.PrayingCalm(database).Lair} " +
            $"health=+{healed} anger={creature.Anger.Max} [{creature.Anger}]");
    }

    /// <summary>
    /// One of the original's ticks of training: experience, and a promotion if
    /// it lands on the threshold.
    ///
    /// <para>A creature that has reached <c>TrainingRoomMaxExperienceLevel</c>
    /// earns nothing and <b>keeps the job</b>, which is the original's shape at
    /// this point rather than an oversight — the cap ejects a creature through
    /// four of the training <i>state</i> handlers, which this project does not
    /// have, so here it simply stops paying. The creature will re-choose at its
    /// next gate and the job handler will refuse it then
    /// (<see cref="JobRules.Refused"/>), so it does leave, one gate late.</para>
    /// </summary>
    private void Train(
        SimulationContext context, WorldState world, GameDatabase database,
        CreatureState creature, CreatureDef definition)
    {
        int amount = TrainingRules.ExperiencePerOriginalTick(database, definition, creature);
        if (amount <= 0) return;

        int before = creature.ExperiencePoints;
        int level = creature.Level;
        bool promoted = CreatureLevelRules.Grant(database, definition, creature, amount);

        if (!promoted)
        {
            // Every original tick would be four lines a second in a log that is
            // read by eye. The gain is reported on the second only.
            if (!OnTheSecond(context)) return;

            context.Emit(SystemName, "trained",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} level={level} " +
                $"exp={creature.ExperiencePoints} gain={amount} " +
                $"needed={CreatureLevelRules.ExperienceForNextLevel(database, definition, level)}");
            return;
        }

        context.Emit(SystemName, "level_up",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} {level}->{creature.Level} " +
            $"via=training discarded={before + amount - CreatureLevelRules.ExperienceForNextLevel(database, definition, level)} " +
            $"cap={TrainingRules.LevelCap(database)}");
    }

    /// <summary>
    /// A game second of training billed to the keeper, and the job lost if the
    /// keeper cannot pay.
    ///
    /// <para><b>Training is the only job a keeper can be evicted from by being
    /// poor</b> (Q205). The original charges
    /// <c>ModifyPlayerColdWhileTrainingPerSecond</c> once a game second on a
    /// countdown reloaded from variable 19, tests the keeper's gold before every
    /// charge, and returns failure from the category hook when the test fails —
    /// which the state dispatcher turns into "back to idle".</para>
    ///
    /// <para>Charged <i>after</i> the tick's experience, because that is the
    /// order the original's hook uses: a creature is paid for the tick it just
    /// trained even if that is the tick the money runs out on.</para>
    /// </summary>
    private void ChargeForTraining(
        SimulationContext context, WorldState world, GameDatabase database,
        CreatureState creature, CreatureDef definition)
    {
        int cost = TrainingRules.GoldPerSecond(database);
        if (cost <= 0) return;

        if (GoldStore.TryWithdraw(world, creature.OwnerPlayerId, cost)) return;

        context.Emit(SystemName, "job_lost",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} job={creature.CurrentJob} " +
            $"at=({creature.Tile.X},{creature.Tile.Y}) reason=cannot_pay cost={cost}");
        Stop(creature);
    }

    /// <summary>Gives up the job and goes idle.</summary>
    private static void Stop(CreatureState creature)
    {
        creature.CurrentJob = JobType.None;
        creature.JobTileIndex = -1;
        creature.Path.Clear();
        creature.PathIndex = 0;
        creature.Activity = CreatureActivity.Idle;
    }

    /// <summary>
    /// The tile underfoot is a room of this type belonging to this creature's
    /// own keeper — the same test <see cref="EatingRules.HatcheryUnder"/> makes,
    /// and made every second so that a room sold out from under a working
    /// creature ends the job at once.
    /// </summary>
    private static bool StandingInOneOfIts(
        WorldState world, GameDatabase database, CreatureState creature, RoomTypeId wants)
    {
        TilePos at = creature.Tile;
        if (!world.Contains(at)) return false;

        RoomInstance? room = RoomIndex.RoomAt(world, database, (at.Y * world.Width) + at.X);
        return room is not null
            && room.Definition.TypeId == wants
            && room.OwnerPlayerId == creature.OwnerPlayerId;
    }

    /// <summary>The last tick of a game second, which is the phase every
    /// per-second system in this simulation shares.</summary>
    private static bool OnTheSecond(SimulationContext context) =>
        (context.Tick.Value + 1) % SimulationTiming.TicksPerSecond == 0;

    private static int CentreTileIndex(WorldState world, RoomInstance room) =>
        ((room.MinY + ((room.MaxY - room.MinY) / 2)) * world.Width)
        + room.MinX + ((room.MaxX - room.MinX) / 2);

    private static int ArrivalTile(WorldState world, RoomInstance room)
    {
        int centre = CentreTileIndex(world, room);
        return EatingRules.Contains(room, centre) ? centre : room.TileIndices[0];
    }
}
