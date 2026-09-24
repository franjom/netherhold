// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>The happy and unhappy job choosers, and the jobs they take.</summary>
/// <remarks>STRONGLY INFERRED from the original's two remaining mood choosers
/// (<c>FUN_004780e0</c> happy, <c>FUN_004782c0</c> unhappy) and their dispatcher
/// <c>FUN_00478800</c> — ADR-0082, docs/research/job-selection.md. The angry chooser
/// stays in <see cref="CreatureAngerSystem"/> (ADR-0061) and shares the roll, the
/// ranges and the five-second gate; this runs after the needs and before it, which
/// is the original's own sequence. Ours: how long a job is kept — all six of DK2's
/// duration variables, <c>MotionlessWhileResearchingSeconds</c> through
/// <c>MinimumJobDurationSeconds</c>, are read by nothing (ADR-0082), so a job lasts
/// until the room stops being the keeper's or a need intervenes. Not implemented:
/// the effects of Guard and the roomless jobs (Leave, Rebel, Explore,
/// StealGold), which are still chosen, logged and occupy the creature.</remarks>
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

            // Taken off its job by a system that still sets Activity directly (ADR-0177).
            CreatureStateMachine.ReleaseIfLeft(creature);

            // Workers have their own whole system and their own priority order
            // (ADR-0018). DK2 dispatches them before any of this too - an Imp's
            // happy table is Explore at 100%, and nothing here would know what
            // to do with it.
            if (definition.Flags.HasFlag(CreatureFlags.IsWorker)) continue;

            if (HandRules.IsHeld(creature)) continue;
            if (PrisonRules.IsImprisoned(creature)) continue;
            if (CreatureDeathRules.IsDown(creature)) continue;
            if (creature.Activity == CreatureActivity.Leaving) continue;
            if (Combat.CombatRules.Preoccupied(creature)) continue;

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

    /// <summary>The happy and unhappy choosers, which differ in exactly two
    /// places: the lair attempt, and who pays for coming back empty-handed.</summary>
    /// <remarks>Not more often than every five game seconds, per mood, on that
    /// mood's own timestamp — the original keeps three side by side in the
    /// creature (happy, unhappy, angry), so a creature that has just rolled while
    /// content may roll again the moment it turns unhappy.</remarks>
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

        // The unhappy chooser tries for a lair first and the happy one does not;
        // both halves are the original's. The happy path does not need to,
        // because the idle state's own first act for a bedless creature is the
        // same attempt (Q150, ADR-0060); the unhappy path is reached from
        // elsewhere and repeats it.
        if (mood == CreatureMood.Unhappy
            && !LairRules.HasBed(context.World, context.Database, creature)
            && _seeker.TryGoForALair(context, SystemName, creature, definition))
        {
            creature.LastUnhappyChoiceTick = now;
            return;
        }

        // Missing guard, and it cannot be added yet: while the owner's CallToArmsOn bit
        // is set DK2 admits only Leave and Rebel here and fails every other job untried
        // (0x00478849, ADR-0145). Nothing models a Call To Arms, so creatures keep
        // working through a call that would have stopped them.
        IReadOnlyList<CreatureJobPreference> table = JobRules.TableFor(definition, mood);
        CreatureAngerRules.CumulativeRanges(table, _ranges);
        int roll = CreatureAngerRules.RollPercent(context.Rng);

        // A selection that fails stamps nothing, so a creature with nothing to
        // do asks again on the very next tick - the original's shape, with its
        // gate at the top and its failure path falling straight out. That also
        // means twenty identical refusals a second, so the reporting is
        // throttled to the same once-a-second phase the charge below uses: the
        // decision is per tick, only the log is not.
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

        // Nothing in the table could be done, and only a content creature pays
        // for that: the unhappy chooser's tail is a bare return where the happy
        // one's charges angerNoWork (JobRules.ChargesNoWorkAnger). Nothing is
        // stamped either way, exactly as the angry chooser leaves a failed
        // selection unstamped, so the creature may try again next second.
        if (!JobRules.ChargesNoWorkAnger(mood)) return;
        if (definition.AngerNoWork == 0) return;
        if (!report) return;

        CreatureAngerRules.Apply(
            context.World, definition, creature, AngerBucket.Work, definition.AngerNoWork);

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
    private static bool TryTake(
        SimulationContext context, CreatureState creature, CreatureDef definition,
        JobType job, int roll, string how, bool report, bool fromCurrentState = false)
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

        if (refused is null && !TrySetOut(context, creature, definition, job, wants!.Value, fromCurrentState))
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
            $"steps={creature.Body.PathTotalCount}");
        return true;
    }

    /// <summary>State 189's tail (<c>0x004728C3</c>): a creature just paid whose happy table
    /// holds Drink calls the Drink handler <c>0x004560F0</c>, and goes idle if it refuses
    /// (<c>payday.md</c> §4.4, Q421, ADR-0189).</summary>
    internal static bool TryGoGamblingWhenPaid(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (!definition.HappyJobs.Any(j => j.Job == JobType.Drink)) return false;
        return TryTake(context, creature, definition, JobType.Drink, roll: -1, how: "paid", report: true,
            fromCurrentState: true);
    }

    /// <summary>Chooses the room and sets off for it.</summary>
    /// <remarks>The keeper's own rooms only — an ally's library is no use to a
    /// creature, exactly as an ally's hatchery is not. Nearest first by squared
    /// distance to the room's centre, the first one this creature can actually
    /// reach over its own <see cref="WorldPathGrid"/> wins, equal distances
    /// broken by the lowest tile index. Deliberately the same rule as the
    /// hatchery and lair walks, down to the arrival tile: an L-shaped room's
    /// centre may not be in it, in which case the walk ends at its lowest
    /// tile.</remarks>
    private static bool TrySetOut(
        SimulationContext context, CreatureState creature, CreatureDef definition,
        JobType job, RoomTypeId wants, bool fromCurrentState = false)
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

        // The torture handler 0x00483AE0: while the keeper has victims, the nearest chamber
        // holding one (0x004F29A0) before any other (0x004F2B80).
        if (job == JobType.Torture && candidates.Any(c => HoldsAVictim(world, c.Room)))
            candidates.RemoveAll(c => !HoldsAVictim(world, c.Room));

        candidates.Sort((a, b) =>
            a.Distance != b.Distance
                ? a.Distance.CompareTo(b.Distance)
                : a.FirstTile.CompareTo(b.FirstTile));

        foreach ((_, _, RoomInstance room) in candidates)
        {
            int target = ArrivalTile(world, room);
            var at = new TilePos(target % world.Width, target / world.Width);
            if (!context.Navigation.TryRoute(creature, at, Navigation.MoveOptions.Loose)) continue;

            creature.JobTileIndex = target;
            creature.Activity = CreatureActivity.GoingToJob;
            SetOutInState(context, creature, job, fromCurrentState);

            // Standing in it already - a creature that rolls Train while stood
            // in the training room. No walk to make, so it is at work now.
            if (!creature.IsRouted) Arrive(context, creature, definition, job);
            return true;
        }

        return false;
    }

    // -------------------------------------------------------------- working

    /// <summary>Walking there. Nothing happens until the route runs out.</summary>
    private void Travelling(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.HasRoute) return;
        Arrive(context, creature, definition, creature.CurrentJob);
    }

    private static void Arrive(
        SimulationContext context, CreatureState creature, CreatureDef definition, JobType job)
    {
        creature.StopMoving();
        creature.Activity = CreatureActivity.AtWork;

        StateShadow.Arrive(context, SystemName, creature);

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
            Stop(context, creature);
            return;
        }

        // A torturer's states are category 0, with no hook: what she does is their handlers,
        // one turn at a time (ADR-0196).
        if (job == JobType.Torture)
        {
            if (originalTick && !TorturerStep.Turn(context, SystemName, creature)) Stop(context, creature);
            return;
        }

        // What happens at work is the update hook of the job state's CATEGORY, which the
        // dispatcher runs before the state's own handler (ADR-0186). The countdown hooks act on
        // the creature's own countdown, every fifth turn from entry (Q424, ADR-0192).
        const int reload = CreatureStateMachine.HookCountdownSeed;
        switch (CreatureStateMachine.UpdatingCategory(creature))
        {
            // TRAINING IS PAID ON THE ORIGINAL'S OWN CADENCE, four times a game second
            // rather than once (Q205). Not a flourish: DK2 discards the overshoot when a
            // creature levels, so the size of an award decides how much is thrown away and
            // collapsing four into one would leave a different balance behind.
            case CreatureStateCategory.Training:
                if (originalTick) Train(context, world, database, creature, definition);
                if (CreatureStateMachine.HookFires(creature, reload))
                    ChargeForTraining(context, world, database, creature, definition);
                return;

            // RESEARCH IS PAID EVERY TICK, and the bonuses once a game second. That split
            // is the original's: its adder credits the base amount unconditionally and
            // computes the bonuses only inside a `tick % variable19 == 0` guard
            // (FUN_004c2dc0). So the tabled ResearchPerSecond arrives in four instalments
            // and a slap's bonus arrives in one lump.
            case CreatureStateCategory.Research:
                if (originalTick) Research(context, world, database, creature, definition, second);
                return;

            case CreatureStateCategory.Spectating:
                if (CreatureStateMachine.HookFires(creature, reload))
                    Spectate(context, world, database, creature, definition);
                return;

            case CreatureStateCategory.Temple:
                if (CreatureStateMachine.HookFires(creature, reload))
                    Pray(context, world, database, creature, definition);
                return;

            case CreatureStateCategory.Casino:
                if (CreatureStateMachine.HookFires(creature, reload))
                    Gamble(context, world, database, creature, definition);
                return;
        }
    }

    /// <summary>One act of the Casino's update hook <c>0x0047AD70</c>, every fifth turn. A purse
    /// at or below the stake fails the hook, which is idle; otherwise the lever's setting
    /// decides the anger and the purse: calm and a coin, or anger and eight lost (ADR-0187,
    /// ADR-0188).</summary>
    /// <remarks>Not built: the jackpot roll and the fearless countdown
    /// (<c>state-category-hooks.md</c> §3.1).</remarks>
    private static void Gamble(
        SimulationContext context, WorldState world, GameDatabase database,
        CreatureState creature, CreatureDef definition)
    {
        if (creature.GoldHeld <= CasinoRules.Stake(database))
        {
            context.Emit(SystemName, "job_lost",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} job={creature.CurrentJob} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=cannot_afford_to_gamble " +
                $"purse={creature.GoldHeld}");
            Stop(context, creature);
            return;
        }

        byte setting = CasinoRules.SettingUnder(world, database, creature);
        CreatureAngerRules.Apply(world, definition, creature, CasinoRules.AngerPerSecond(database, setting));

        int change = CasinoRules.PursePerSecond(database, setting);
        int won = change >= 0
            ? PurseRules.Receive(database, definition, creature, change)
            : -PurseRules.Spend(creature, -change);

        context.Emit(SystemName, "gambled",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} setting={setting} won={won} purse={creature.GoldHeld} " +
            $"anger={creature.Anger.Max} [{creature.Anger}]");
    }

    /// <summary>One act of the Temple's update hook <c>0x0047B970</c>, every fifth turn.</summary>
    private static void Pray(
        SimulationContext context, WorldState world, GameDatabase database,
        CreatureState creature, CreatureDef definition)
    {

        // The only source in the game that drains an anger bucket it did not
        // fill (ADR-0081). Six calls, one per bucket, at -275 each; then 33
        // health, clamped by the health rules to what is actually missing.
        CreatureAnger before = creature.Anger;
        CreatureAngerRules.Apply(world, definition, creature, JobRules.PrayingCalm(database));
        int healed = CreatureHealthRules.Apply(
            database, definition, creature, JobRules.PrayingHeal(database));

        if (before.Equals(creature.Anger) && healed == 0) return;

        context.Emit(SystemName, "prayed",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} calm={JobRules.PrayingCalm(database).Lair} " +
            $"health=+{healed} anger={creature.Anger.Max} [{creature.Anger}]");
    }

    /// <summary>One act of the Spectating hook, every fifth turn: its calm, which always
    /// succeeds.</summary>
    private static void Spectate(
        SimulationContext context, WorldState world, GameDatabase database,
        CreatureState creature, CreatureDef definition)
    {
        CreatureAnger before = creature.Anger;
        CreatureAngerRules.Apply(world, definition, creature, JobRules.SpectatorCalm(database));
        if (before.Equals(creature.Anger)) return;

        context.Emit(SystemName, "spectated",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} anger={creature.Anger.Max} [{creature.Anger}]");
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

    /// <summary>One of the original's ticks of research, credited to the
    /// researcher's keeper.</summary>
    /// <remarks>The keeper, not the creature: DK2's hook ends
    /// <c>MOV ECX,[ESI + 0x2A]</c>, the creature's player pointer, and calls the
    /// keeper's adder — the only job so far whose whole effect lands on the
    /// player. Nothing charges for it: training bills the keeper once a game
    /// second and can evict for poverty, while the research hook has no cost path
    /// at all, so the two rooms are not the symmetrical pair they look like.
    /// Reported on the second only, like training.</remarks>
    private void Research(
        SimulationContext context, WorldState world, GameDatabase database,
        CreatureState creature, CreatureDef definition, bool second)
    {
        int amount = ResearchRules.PerOriginalTick(database, definition, creature);
        if (amount <= 0) return;

        int bonus = second
            ? ResearchRules.SlapBonus(database, creature, amount, context.Tick.Value)
            : 0;

        ref PlayerState keeper = ref world.Player(creature.OwnerPlayerId);
        keeper.ResearchPoints += amount + bonus;

        if (!second) return;

        context.Emit(SystemName, "researched",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} level={creature.Level} " +
            $"gain={amount} bonus={bonus} points={keeper.ResearchPoints}");
    }

    /// <summary>One charge of training billed to the keeper, and the job lost
    /// if the keeper cannot pay.</summary>
    /// <remarks>Training is the only job a keeper can be evicted from by being
    /// poor (Q205). The original charges
    /// <c>ModifyPlayerColdWhileTrainingPerSecond</c> every fifth turn on a
    /// countdown reloaded from variable 19 (Q424), tests the keeper's gold before every
    /// charge, and returns failure from the category hook, which the state
    /// dispatcher turns into "back to idle". Charged after the tick's experience,
    /// the order the original's hook uses: a creature is paid for the tick it
    /// just trained even if that is the tick the money runs out on.</remarks>
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
        Stop(context, creature);
    }

    /// <summary>Gives up the job and goes idle: state 1, then the shim takes the id, idle
    /// not being on state ids yet.</summary>
    private static void Stop(SimulationContext context, CreatureState creature)
    {
        creature.CurrentJob = JobType.None;
        creature.JobTileIndex = -1;
        creature.StopMoving();
        creature.Activity = CreatureActivity.Idle;

        StateShadow.ToIdle(context, SystemName, creature);
    }

    /// <summary>DK2's room helper, called by the job's handler from idle with the job's
    /// state as the resume (ADR-0177): state 7 for a walk, or 1 and then the job state
    /// when the creature is already in the room (<c>travel-states.md</c> §8). From idle, or
    /// with <paramref name="fromCurrentState"/> from the state the caller is in (189).</summary>
    private static void SetOutInState(
        SimulationContext context, CreatureState creature, JobType job, bool fromCurrentState)
    {
        if (CreatureStateMachine.HasState(creature) && !fromCurrentState)
            CreatureStateMachine.Release(creature);
        if (JobRules.StateFor(job) is not int state) return;

        StateShadow.FromIdle(creature);
        if (creature.IsRouted)
        {
            CreatureStateMachine.BeginTravel(creature, StateShadow.TravelToRoom, state);
            return;
        }

        StateShadow.Move(context, SystemName, creature, CreatureStateMachine.Idle);
        StateShadow.Move(context, SystemName, creature, state);
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

    private static bool HoldsAVictim(WorldState world, RoomInstance room) =>
        world.Creatures.Any(c => TortureRules.IsBeingTortured(c)
            && EatingRules.Contains(room, (c.Tile.Y * world.Width) + c.Tile.X));

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
