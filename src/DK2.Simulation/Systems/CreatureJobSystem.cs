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
/// ranges and the five-second gate; in the creature's update this runs after the needs
/// routine and the angry chooser, as the state handler follows them in the original's
/// dispatcher (<c>0x00476FC0</c>). Ours: how long a job is kept — all six of DK2's
/// duration variables, <c>MotionlessWhileResearchingSeconds</c> through
/// <c>MinimumJobDurationSeconds</c>, are read by nothing (ADR-0082), so a job lasts
/// until the room stops being the keeper's or a need intervenes. Leave, Rebel and Explore
/// are chosen and logged only; StealGold runs <see cref="StealGoldRules"/> (ADR-0223).</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/job-selection.md")]
public sealed partial class CreatureJobSystem : ICreatureUpdateStep
{
    private const string SystemName = "CreatureJob";

    private readonly IPathfinder _pathfinder;
    private readonly LairSeeker _seeker;
    private readonly CreatureAngerSystem _moodJobs;
    private readonly List<TilePos> _route = [];
    private readonly List<(JobType Job, int Low, int High)> _ranges = [];

    public CreatureJobSystem() : this(new GridPathfinder()) { }

    public CreatureJobSystem(IPathfinder pathfinder)
    {
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
        _seeker = new LairSeeker(_pathfinder);
        _moodJobs = new CreatureAngerSystem(_pathfinder);
    }

    public string Name => SystemName;

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    /// <summary>A non-worker's state handler in its update: the walks and jobs it is in, and the
    /// happy and unhappy choosers when it is idle.</summary>
    public void Update(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (creature.Possessed) return;
        // Isolated job-system hosts omit TrainingActionSystem; completed handler work is guarded.
        if (creature.TrainingActionId != 0) TrainingStep.PollAction(context, creature);
        if (context.TremorRecoveryCompletedThisTick.Contains(creature)) return;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if (creature.Dk2StateId == LeavingRules.PortalDropState)
        {
            if (HandRules.IsHeld(creature) || CreatureDeathRules.IsDown(creature)) return;
            if (context.Tick.Value > creature.StateEnteredTick
                && !LeavingRules.PortalDrop(context, creature, SystemName)) StateShadow.ToIdle(context, SystemName, creature);
            return;
        }


        // Workers have their own whole system and their own priority order
        // (ADR-0018). DK2 dispatches them before any of this too - an Imp's
        // happy table is Explore at 100%, and nothing here would know what
        // to do with it.
        if (definition.Flags.HasFlag(CreatureFlags.IsWorker)) return;

        if (CreatureEligibility.IsExcluded(creature, CreatureGuards.Held | CreatureGuards.Imprisoned | CreatureGuards.Down)) return;
        if (creature.CombatPitFighter) return;   // idle sends it back to the pit (ADR-0213)
        // Before the leaving test: 169, the thief's escape, reads as leaving (ADR-0223).
        if (!HeroJobRules.IsHero(creature) && StealGoldRules.KeeperTurn(context, SystemName, creature)) return;
        if (creature.Activity == CreatureActivity.Leaving) return;
        if (HeroJobRules.IsHero(creature)) return;   // HeroSystem: state 1 sends a hero to 90
        if (PossessionRules.FollowerTurn(context, SystemName, creature)) return;

        // A walk resuming 1 is state 1's own, back to a fight (ADR-0225): the arrival is idle.
        if (StateShadow.IsTravelling(creature) && creature.UtilityNewState == CreatureStateMachine.Idle)
        {
            if (!creature.HasRoute)
            {
                creature.StopMoving();
                StateShadow.Arrive(context, SystemName, creature);
            }
            return;
        }
        if (Combat.CombatRules.Preoccupied(creature)) return;
        // A combat handler set 1 this turn: 1's handler is next turn's (0x00476FC0).
        if (context.IdleHandlerWaits(creature)) return;

        // 193, joining your Dungeon: the Entrance animation, a step off the portal, then 1 and the
        // lair attempt in the same tick, its result unread (0x00459C20, the call at 0x00459D34).
        // State 1's handler runs on the next turn (ADR-0574).
        if (creature.Dk2StateId == PortalRules.JoiningState)
        {
            if (PortalRules.StillJoining(world, creature, definition)) return;
            StateShadow.ToIdle(context, SystemName, creature);
            _seeker.TryGoForALair(context, SystemName, creature, definition);
            return;
        }

        // 3 and 87, 89's "nothing to do": an idle action or a wander, then idle (ADR-0228).
        if (creature.Dk2StateId == DoingNothingState)
        {
            DoingNothing(context, creature);
            return;
        }
        if (creature.Dk2StateId == NeutralIdleState)
        {
            NeutralIdle(context, creature);
            return;
        }
        if (creature.Dk2StateId == ImpStates.Wandering)
        {
            if (context.UpdateStartStateId == LairRules.MistressBedExitState) return;
            if (!ImpTaskSystem.Wander(context, creature, definition)) StateShadow.ToIdle(context, SystemName, creature);
            return;
        }

        // 211, the gold celebration: holds while its action plays, then idle (ADR-0400).
        if (creature.Dk2StateId == DropDeliveryRules.CelebratingState)
        {
            if (!DropDeliveryRules.Celebrating(context, creature)) StateShadow.ToIdle(context, SystemName, creature);
            return;
        }

        // 250, after a drop with nowhere to go: stands, then idle (ADR-0226).
        if (creature.Dk2StateId == DropLandingRules.StandingState)
        {
            if (!DropLandingRules.Stands(context, creature)) StateShadow.ToIdle(context, SystemName, creature);
            return;
        }

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
        if (creature.Dk2StateId == IdleChooserState && TryDispatchMoodPrelude(context, creature, definition)) return;
        // State 1 (0x004773D0) requests 89 and returns; 89's body runs on a later turn (ADR-0574).
        // A Horny's arm is 238, which is not built, so a Horny not fleeing still chooses below.
        if (creature.Dk2StateId == CreatureStateMachine.Idle
            && (creature.Fleeing || !definition.Flags.HasFlag(CreatureFlags.IsHorny)))
        {
            DispatchIdle(context, creature, definition);
            return;
        }
        // State 1's first arm, before it sends the creature to 89: back to a fight it withdrew from.
        if (creature.Dk2StateId == CreatureStateMachine.Idle && Combat.FightReturnRules.TryReturn(context, creature)) return;

        // State 1's neutral arm (0x004775BE-0x004775DA): every neutral creature, fleeing or not,
        // takes +0x7E = now + 12 and state 5 (ADR-0400).
        if (creature.Dk2StateId == CreatureStateMachine.Idle && creature.OwnerPlayerId == PlayerIds.Neutral)
        {
            creature.StateDeadlineTick = context.Tick.Value + NothingToDoTicks;
            CreatureStateMachine.TrySetState(context.Hooks, creature, NeutralIdleState, context.Tick.Value);
            return;
        }

        // 89 clears the heeding bit, and with the owner's Call to Arms on walks to the flag
        // resuming 191, or sends message 84 (0x004778E0; call-to-arms.md).
        creature.HeedingCallToArms = false;
        creature.RebelActive = false; // 89's next write clears RebelActive too (0x0047792A)
        if (CallToArmsRules.IsOn(context.World, creature.OwnerPlayerId))
        {
            TilePos flag = context.World.Player(creature.OwnerPlayerId).CallToArms.Tile;
            if (context.Navigation.TryRoute(creature, flag, 0))
            {
                CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, CallToArmsRules.ArrivedState);
                creature.HeedingCallToArms = true;
                context.Emit(SystemName, "call_to_arms_heeded", $"player={creature.OwnerPlayerId} creature={creature.Id} tile={flag.X},{flag.Y} how=idle");
                return;
            }
            context.World.Messages.Post(context.World, creature.OwnerPlayerId, PlayerMessageKind.CallToArmsUnreachable, 0, 0);
        }

        // State 89's step 5 (0x004778E0, idle-fallback.md §1): with no lair, the lair attempt
        // 0x0046F390 comes before the dropped job, the remembered job and either chooser, and
        // outside the chooser's gate — whatever the mood. CreatureSleepSystem's tired-and-bedless
        // path is the same attempt on its own 12-tick retry; a creature that turns idle between
        // the two systems used to roll a job here first and carry its lair grievance into
        // leaving (Level 3's Goblins, ADR-0328).
        if (!LairRules.HasBed(context.World, context.Database, creature)
            && _seeker.TryGoForALair(context, SystemName, creature, definition))
            return;

        // State 89 (0x004778E0) asks the job the hand dropped this creature on before either
        // chooser, whatever the mood, and forgets it only when it is refused (ADR-0215).
        if (creature.DroppedToDoJob != JobType.None)
        {
            if (TryTake(context, creature, definition, creature.DroppedToDoJob, roll: -1, "dropped", report: false)) return;
            creature.DroppedToDoJob = JobType.None;
        }

        CreatureMood mood = creature.Dk2StateId == IdleChooserState
            ? CreatureMood.Content : CreatureAngerRules.MoodOf(definition, creature);

        // An angry creature is CreatureAngerSystem's, and has been since
        // ADR-0061. Two systems rolling on two tables in one second would be
        // two decisions where DK2 makes one.
        if (mood == CreatureMood.Angry) return;

        long now = context.Tick.Value;
        long last = mood == CreatureMood.Content
            ? creature.LastHappyChoiceTick
            : creature.LastUnhappyChoiceTick;

        // Inside its gate the happy chooser returns 0 untried, and 89 goes on to its fallback (ADR-0228).
        if (last != 0 && now - last < JobRules.ReselectionGateTicks)
        {
            NothingChosen(context, creature, definition);
            return;
        }

        // The unhappy chooser 0x004782C0 is the dispatcher's prelude (TryDispatchMoodPrelude): its
        // five-second gate first, then the lair attempt (0x00478346-0x00478382). 89's ungated
        // attempt above has already been made this tick, so none is repeated here.

        // While the owner's CallToArmsOn bit is set the dispatcher admits only Leave and Rebel
        // and fails every other job untried (0x00478849, ADR-0145): TryTake's first refusal.
        IReadOnlyList<CreatureJobPreference> table = JobRules.TableFor(definition, mood);
        CreatureAngerRules.CumulativeRanges(table, _ranges);
        int roll = CreatureAngerRules.RollPercent(context.Rng);

        // A selection that fails stamps nothing, but 89 then parks the creature in
        // state 3 (ADR-0228), so a pass is rare enough to report every refusal.
        const bool report = true;

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

        // Nothing in the table could be done: only the happy chooser's tail charges
        // angerNoWork (JobRules.ChargesNoWorkAnger), on every failed pass; an unhappy
        // creature is charged by 89's happy pass instead (ADR-0574). Nothing is
        // stamped on failure. Then 89's fallback and state 3 (ADR-0228).
        // The charge is the loaded def+0x6C7: the file's per-second figure over four (ADR-0228).
        int charge = SimulationTiming.LoaderRatePerTick(definition.AngerNoWork);
        if (JobRules.ChargesNoWorkAnger(mood) && charge != 0)
        {
            CreatureAngerRules.Apply(
                context.World, definition, creature, AngerBucket.Work, charge, AngerComplaint.NoWork);

            context.Emit(SystemName, "no_work",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} roll={roll} anger=+{charge} " +
                $"work_anger={creature.Anger.Work} " +
                $"tried=[{string.Join(",", _ranges.Select(r => r.Job))}]");
        }

        NothingChosen(context, creature, definition);
    }

    /// <summary>
    /// One job's handler: its own test, then the room, then the walk. Says why
    /// not in the log whichever part refused, which is the "logged reasons"
    /// half of M4's seventh item.
    /// </summary>
    private static bool TryTake(
        SimulationContext context, CreatureState creature, CreatureDef definition,
        JobType job, int roll, string how, bool report)
    {
        string? refused = JobRules.Refused(context.World, context.Database, definition, creature, job);
        // The dispatcher (0x00478800) admits only Leave and Rebel while the owner's Call to Arms is on (ADR-0145, ADR-0279).
        if (refused is null && job is not (JobType.Leave or JobType.Rebel) && CallToArmsRules.IsOn(context.World, creature.OwnerPlayerId))
            refused = "call_to_arms";
        RoomTypeId? wants = JobRules.RoomFor(job);

        // Job 26's handler (0x00457780) wants a fight on in one of the keeper's pits (ADR-0227).
        if (refused is null && job == JobType.CombatPitSpectate)
        {
            if (!CombatPitSpectatorRules.TakeTheJob(context, SystemName, creature))
            {
                refused = "no_fight_to_watch";
                RoomNeedRules.NoSpectatorPitFor(context, creature, definition);
            }
            else
            {
                creature.CurrentJob = job;
                context.Emit(SystemName, "job_taken",
                    $"creature={creature.Id} type={definition.Name} " +
                    $"player={creature.OwnerPlayerId} job={job} roll={roll} how={how} room=CombatPit effect=built steps={creature.Body.PathTotalCount}");
                return true;
            }
        }

        // Job 13's arm is the same for a keeper's creature as for a hero (0x00481FA0, ADR-0223).
        if (refused is null && job == JobType.StealGold)
        {
            if (!StealGoldRules.Arm(context, SystemName, creature)) refused = "no_target";
            else
            {
                creature.CurrentJob = job;
                context.Emit(SystemName, "job_taken",
                    $"creature={creature.Id} type={definition.Name} " +
                    $"player={creature.OwnerPlayerId} job={job} roll={roll} how={how} room=Treasury effect=built steps=0");
                return true;
            }
        }

        if (refused is null && wants is null)
        {
            // Leave, Rebel, Explore and the rest. Real DK2 outcomes
            // with no room to walk to and nothing here to act on; the angry
            // chooser reports them the same way and neither invents one.
            refused = "no_such_job_here";
        }

        if (refused is null && !TrySetOut(context, creature, definition, job, wants!.Value))
        {
            refused = "no_room";
            // The Library finder's messages are posted by its search in TrySetOut: one call in DK2.
            if (job != JobType.Research) RoomNeedRules.NoJobRoomFor(context, creature, definition, wants.Value);
        }

        // Job_StartTrain (0x00485240) calls the fighter's pit finder, for its messages only, for a
        // creature at or above the Training Room's level cap and below the Combat Pit's.
        if (job == JobType.Train && refused != "call_to_arms"
            && creature.Level >= context.Database[VariableTypeId.TrainingRoomMaxExperienceLevel]
            && creature.Level < context.Database[VariableTypeId.CombatPitMaxExperienceLevel])
            RoomNeedRules.NoFightingPitFor(context, creature, definition);

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
        return TryTake(context, creature, definition, JobType.Drink, roll: -1, how: "paid", report: true);
    }

    /// <summary>Chooses the room and sets off for it.</summary>
    /// <remarks>The keeper's own rooms only — an ally's library is no use to a
    /// creature, exactly as an ally's hatchery is not. Nearest first by squared
    /// distance to the room's centre, the first one this creature can actually
    /// reach over its own <see cref="WorldPathGrid"/> wins, equal distances
    /// broken by the lowest tile index. The walk ends at the room point
    /// <see cref="RoomWalkRules.ArrivalTile"/>, as DK2's room walk does.</remarks>
    private static bool TrySetOut(
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

        // The torture handler 0x00483AE0: while the keeper has victims, the nearest chamber
        // holding one (0x004F29A0), which asks no capacity, before any other (0x004F2B80),
        // which asks for two places. Every other finder asks for one, so a full room is
        // passed over (0x004F3560, 0x004F1A20, 0x004C6610; ADR-0205).
        // The Casino finder's party branch (0x004D75F0, casino.md §9): only a Casino with a party on and
        // a place or a lever, the last in room order that the creature reaches, with no distance ranking.
        bool party = job == JobType.Drink && world.Player(creature.OwnerPlayerId).CasinoParties != 0;
        if (job == JobType.Research)
        {
            // Job_TryResearch (0x004759D0): the Library finder 0x004E2500 names one room, or none.
            candidates.Clear();
            if (RoomNeedRules.FindLibrary(context, creature, definition, out _) is { } library)
                candidates.Add((0, library.TileIndices[0], library));
        }
        else if (party)
        {
            candidates.RemoveAll(c => !CasinoJackpotRules.IsPartyOn(world, c.Room)
                || !(RoomCapacityRules.HasRoom(world, database, c.Room, creature.Id) || CasinoRules.Lever(world, c.Room) is not null));
            candidates.Reverse();
        }
        else if (job == JobType.Torture && candidates.Any(c => HoldsAVictim(world, c.Room)))
            candidates.RemoveAll(c => !HoldsAVictim(world, c.Room));
        else
        {
            int units = job == JobType.Torture ? 2 * RoomCapacityRules.Units : RoomCapacityRules.Units;
            candidates.RemoveAll(c => !RoomCapacityRules.HasRoom(world, database, c.Room, creature.Id, units));
        }

        if (!party)
            candidates.Sort((a, b) =>
                a.Distance != b.Distance
                    ? a.Distance.CompareTo(b.Distance)
                    : a.FirstTile.CompareTo(b.FirstTile));

        foreach ((_, _, RoomInstance room) in candidates)
        {
            // 0x00471A70: already in the room it enters the state at once; otherwise it walks to the
            // room point 0x004E4C10, which is never a furniture slot (ADR-0403).
            int here = world.Contains(creature.Tile) ? (creature.Tile.Y * world.Width) + creature.Tile.X : -1;
            int target;
            if (here >= 0 && room.Contains(here)) target = here;
            else if (RoomWalkRules.ArrivalTile(context, room) is int point) target = point;
            else continue;
            var at = new TilePos(target % world.Width, target / world.Width);
            if (!context.Navigation.TryRoute(creature, at, Navigation.MoveOptions.Loose)) continue;

            creature.JobTileIndex = target;
            bool entered = SetOutInState(context, creature, job);

            // Standing in it already - a creature that rolls Train while stood
            // in the training room. No walk to make, so it is at work now.
            if (!creature.IsRouted) Arrive(context, creature, definition, job, entered);
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

    /// <param name="entered">For a creature already in the room, whether the job state was
    /// taken; a walk's arrival decides it here.</param>
    private static void Arrive(
        SimulationContext context, CreatureState creature, CreatureDef definition, JobType job,
        bool entered = true)
    {
        creature.StopMoving();

        if (StateShadow.IsTravelling(creature)) entered = StateShadow.Arrive(context, SystemName, creature);
        if (!entered && ClaimRefused(context, creature, definition, job)) return;

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

        // A guard's states test the room themselves, and its patrol may end on a post outside it.
        if (second && job != JobType.Guard && (wants is null
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

        // A spectator's hook runs, then its state's handler: 142, 143 and 144 (ADR-0227).
        if (job == JobType.CombatPitSpectate)
        {
            if (CreatureStateMachine.UpdatingCategory(creature) == CreatureStateCategory.Spectating
                && CreatureStateMachine.HookFires(creature, CreatureStateMachine.HookCountdownSeed))
                Spectate(context, world, database, creature, definition);
            if (originalTick && !CombatPitSpectatorRules.Turn(context, SystemName, creature)) Stop(context, creature);
            return;
        }

        // A researcher's hook runs every turn, then its state's handler: 53-57 and 59 (ADR-0397).
        // RESEARCH IS PAID EVERY TURN, and the bonuses once a game second. That split is the
        // original's: its adder credits the base amount unconditionally and computes the bonuses
        // only inside a `tick % variable19 == 0` guard (FUN_004c2dc0). So the tabled
        // ResearchPerSecond arrives in four instalments and a slap's bonus in one lump.
        if (job == JobType.Research)
        {
            if (originalTick && CreatureStateMachine.UpdatingCategory(creature) == CreatureStateCategory.Research)
                Research(context, world, database, creature, definition, second);
            if (originalTick && !ResearchStep.Turn(context, SystemName, creature)) Stop(context, creature);
            return;
        }

        // A worshipper's hook runs, then its state's handler: 101-107 (ADR-0403).
        if (job == JobType.Praying)
        {
            if (CreatureStateMachine.UpdatingCategory(creature) == CreatureStateCategory.Temple
                && CreatureStateMachine.HookFires(creature, CreatureStateMachine.HookCountdownSeed))
                Pray(context, world, database, creature, definition);
            if (originalTick && !TempleStep.Turn(context, SystemName, creature) && world.CreatureById(creature.Id) is not null)
                Stop(context, creature);
            return;
        }

        // A gambler's hook runs, then its state's handler: 145-151 and 153 (ADR-0396).
        if (job == JobType.Drink)
        {
            if (CreatureStateMachine.UpdatingCategory(creature) == CreatureStateCategory.Casino
                && CreatureStateMachine.HookFires(creature, CreatureStateMachine.HookCountdownSeed)
                && !Gamble(context, world, database, creature, definition))
                return;
            if (creature.CurrentJob != JobType.Drink) return;   // the jackpot's winner
            if (originalTick && !CasinoStep.Turn(context, SystemName, creature)) Stop(context, creature);
            return;
        }

        // Guard's category has no update hook either: 97, 98 and 99 are the whole of it (ADR-0212).
        if (job == JobType.Guard)
        {
            if (originalTick && !GuardStep.Turn(context, SystemName, creature)) Stop(context, creature);
            return;
        }

        // A crate was just made (ADR-0214): state 68 starts the celebration and sets 1 and 69,
        // and returns 0, so the dispatcher sets 1 again. The creature is idle from this turn.
        if (creature.Dk2StateId == CrateRules.CrateMadeState)
        {
            if (originalTick) CrateMade(context, creature, definition);
            return;
        }

        // A manufacturer's hook runs every turn, then its state's handler: 63-65 (ADR-0404).
        // MANUFACTURE IS CREDITED EVERY TURN, like research: 0x0047B190 has no countdown.
        if (job == JobType.Manufacture)
        {
            if (!originalTick) return;
            if (CreatureStateMachine.UpdatingCategory(creature) == CreatureStateCategory.Manufacture)
            {
                if (!Manufacture(context, world, database, creature, definition)) return;
                // The maker's own handler runs after its hook, in the state the crate maker set.
                if (creature.Dk2StateId == CrateRules.CrateMadeState)
                {
                    CrateMade(context, creature, definition);
                    return;
                }
            }

            if (!WorkshopStep.Turn(context, SystemName, creature)) Stop(context, creature);
            return;
        }

        // A trainee's Training Room rebuilt since its claim sends it to 113, which waits a turn
        // and goes back to 108 (0x004F2EA0, 0x00485A60; ADR-0400).
        if (job == JobType.Train)
        {
            if (originalTick && RoomEvictRules.Evict(context, SystemName, creature, RoomTypeId.TrainingRoom)) return;
            if (creature.Dk2StateId == TrainingRules.ReturningState)
            {
                if (originalTick && !TrainingRules.Return(context, SystemName, creature)) Stop(context, creature);
                return;
            }
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
                if (originalTick)
                {
                    if (!TrainingRules.BelowCap(database, creature)) TrainingCapHandlers(context, database, creature, definition);
                    else if (creature.CurrentJob == JobType.Train && !TrainingStep.Turn(context, SystemName, creature)) Stop(context, creature);
                }
                return;

            case CreatureStateCategory.Spectating:
                if (CreatureStateMachine.HookFires(creature, reload))
                    Spectate(context, world, database, creature, definition);
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
    /// ADR-0188); then the jackpot roll (ADR-0396). False when the hook failed.</summary>
    private static bool Gamble(
        SimulationContext context, WorldState world, GameDatabase database,
        CreatureState creature, CreatureDef definition)
    {
        if (CasinoRules.StepFearlessCountdown(database, creature))
            context.Emit(SystemName, "gambling_fearless", $"creature={creature.Id} type={definition.Name}");

        if (creature.GoldHeld <= CasinoRules.Stake(database))
        {
            context.Emit(SystemName, "job_lost",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} job={creature.CurrentJob} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=cannot_afford_to_gamble " +
                $"purse={creature.GoldHeld}");
            Stop(context, creature);
            return false;
        }

        byte setting = CasinoRules.SettingUnder(world, database, creature);
        CreatureAngerRules.Apply(world, definition, creature, CasinoRules.AngerPerSecond(database, setting), AngerComplaint.Money);

        int change = CasinoRules.PursePerSecond(database, setting);
        int won = change >= 0
            ? PurseRules.Receive(database, definition, creature, change)
            : -PurseRules.Spend(creature, -change);

        context.Emit(SystemName, "gambled",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} setting={setting} won={won} purse={creature.GoldHeld} " +
            $"anger={creature.Anger.Max} [{creature.Anger}]");

        int chance = CasinoJackpotRules.Chance(database, setting);
        if (chance > 0 && context.Rng.NextInt(chance) == CasinoJackpotRules.WinningRoll)
            CasinoJackpotRules.Jackpot(context, SystemName, creature);
        return true;
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
        CreatureAngerRules.Apply(world, definition, creature, JobRules.PrayingCalm(database), CreatureAngerRules.PrayingComplaints);
        // 0x0049984E heals by variable 115 with the creature's own owner as the source.
        int healed = CreatureHealthRules.Apply(
            database, definition, creature, JobRules.PrayingHeal(database), creature.OwnerPlayerId);

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
        CreatureAngerRules.Apply(world, definition, creature, JobRules.SpectatorCalm(database), AngerComplaint.General);
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
    /// earns nothing here, which is the hook's shape; the state handlers eject it
    /// (<see cref="TrainingCapHandlers"/>: states 108, 110, 111 and 112).
    /// Ours still charges where DK2's hook does not (CategoryHooks).</para>
    /// </summary>
    private void Train(
        SimulationContext context, WorldState world, GameDatabase database,
        CreatureState creature, CreatureDef definition)
    {
        int amount = TrainingRules.ExperiencePerOriginalTick(database, definition, creature);
        if (amount <= 0) return;

        int before = creature.ExperiencePoints;
        int level = creature.Level;
        bool promoted = CreatureLevelRules.Grant(context.Hooks, definition, creature, amount);

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

        // The adder goes on to the target in the same call (0x004C2DC0): the credit is
        // always banked first, and only then may it pay for something.
        ResearchTreeRules.Advance(context, creature.OwnerPlayerId);

        if (!second) return;

        context.Emit(SystemName, "researched",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} level={creature.Level} " +
            $"gain={amount} bonus={bonus} points={keeper.ResearchPoints}");
    }

    /// <summary>One turn in the Workshop: the credit, and a crate when an order is paid for
    /// (ADR-0203, <see cref="ManufactureRules"/>). A zero credit fails the hook (<c>0x0047B190</c>
    /// returns 0), which is idle: a hand-dropped creature that cannot manufacture leaves at once,
    /// where the Research hook keeps a zero-rate creature (ADR-0573). False when the hook failed.</summary>
    private static bool Manufacture(
        SimulationContext context, WorldState world, GameDatabase database,
        CreatureState creature, CreatureDef definition)
    {
        int amount = ManufactureRules.PerOriginalTick(database, definition, creature);
        if (amount <= 0)
        {
            context.Emit(SystemName, "job_lost",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} job={creature.CurrentJob} " +
                $"at=({creature.Tile.X},{creature.Tile.Y}) reason=cannot_manufacture");
            Stop(context, creature);
            return false;
        }

        ObjectState? crate = ManufactureRules.Credit(context.Hooks, creature, amount);
        if (crate is null) return true;
        context.Emit(SystemName, "crate_manufactured",
            $"creature={creature.Id} player={creature.OwnerPlayerId} object={crate.ObjectTypeId} " +
            $"at=({crate.TileIndex % world.Width},{crate.TileIndex / world.Width}) " +
            $"made={world.Player(creature.OwnerPlayerId).ItemsManufactured}");
        return true;
    }

    /// <summary>State 68 (<c>0x00470B20</c>): a creature whose definition has the celebration
    /// flag starts action 35 (<c>0x23</c>) for one or two plays; either way it ends the turn in
    /// state 1, because the handler returns 0 after setting 1 and 69 and the dispatcher then sets
    /// 1. Ours: no action is played, so the celebration takes no time, and the flag (animation
    /// record 8's <c>+0xB</c> bit 0) is not read, since both arms end idle.</summary>
    private static void CrateMade(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        context.Emit(SystemName, "crate_celebrated",
            $"creature={creature.Id} type={definition.Name} player={creature.OwnerPlayerId}");
        Stop(context, creature);
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

    /// <summary>The job state's enter hook refused and left the creature in state 1 (ADR-0309):
    /// the job ends. Training's gold and the Casino's lever and purse are tested before the claim
    /// (ADR-0313), and only Training's raises a message, 55. Then the claim <c>0x004E5A70</c>: a
    /// room that filled while the creature walked raises message <c>0x2A</c> only when no other
    /// room of the type has a place (<c>0x004C6AE0</c>, ADR-0205); no room underfoot raises none.</summary>
    private static bool ClaimRefused(
        SimulationContext context, CreatureState creature, CreatureDef definition, JobType job)
    {
        if (JobRules.StateFor(job) is not int state || creature.Dk2StateId != CreatureStateMachine.Idle) return false;
        CreatureStateCategory category = CreatureStateTable.Get(state).Category;
        if (!RoomCapacityRules.Claims(category)) return false;

        WorldState world = context.World;
        RoomInstance? room = world.Contains(creature.Tile)
            ? RoomIndex.RoomAt(world, context.Database, (creature.Tile.Y * world.Width) + creature.Tile.X)
            : null;
        string why = CategoryHooks.RefusedBeforeClaim(context.Hooks, category, creature) is string refused
            ? $"reason={refused} message={(category == CreatureStateCategory.Training ? "0x37" : "none")}"
            : room is null ? "reason=no_room message=none"
            : RoomCapacityRules.RaisesFull(world, context.Database, room, creature)
                ? "reason=room_full message=0x2A" : "reason=room_full message=none";
        context.Emit(SystemName, "job_lost",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} job={job} " +
            $"at=({creature.Tile.X},{creature.Tile.Y}) {why}");
        Stop(context, creature);
        return true;
    }

    /// <summary>The training state handlers' cap test, after the category hook as DK2's dispatcher
    /// runs them: state 108 (<c>0x00485310</c>) sends a creature at or above
    /// <c>TrainingRoomMaxExperienceLevel</c> to 110, and 110 (<c>0x00485580</c>) then returns 0, so
    /// it idles. A capped creature dropped on a Training Room thus leaves it two turns later, as
    /// Level 17's Black Knights do in the capture (Q509, ADR-0393). Action 0x2C, which 108 starts
    /// on the way, is not modelled, nor 108's return of 0 (to idle) when that action or the move to
    /// 110 fails. States 111 and 112 refuse at the cap directly.</summary>
    private static void TrainingCapHandlers(
        SimulationContext context, GameDatabase database, CreatureState creature, CreatureDef definition)
    {
        if (TrainingRules.BelowCap(database, creature)) return;
        if (creature.Dk2StateId == TrainingRules.WalkToMachineState)
        {
            context.Emit(SystemName, "training_refused",
                $"creature={creature.Id} type={definition.Name} player={creature.OwnerPlayerId} level={creature.Level}");
            CreatureStateMachine.TrySetState(context.Hooks, creature, TrainingRules.CapRefusalState);
        }
        else if (creature.Dk2StateId is TrainingRules.CapRefusalState or TrainingStep.AtMachineState or TrainingStep.WorkingState)
        {
            Stop(context, creature);
        }
    }

    /// <summary>Gives up the job and goes idle: state 1.</summary>
    private static void Stop(SimulationContext context, CreatureState creature)
    {
        TrainingStep.CancelAction(creature);
        creature.CurrentJob = JobType.None;
        creature.JobTileIndex = -1;
        creature.StopMoving();

        StateShadow.ToIdle(context, SystemName, creature);
    }

    /// <summary>DK2's room helper, called by the job's handler from idle with the job's
    /// state as the resume (ADR-0177): state 7 for a walk, or 1 and then the job state
    /// when the creature is already in the room (<c>travel-states.md</c> §8), from the state
    /// the caller is in: idle, or 189 for the Drink handler after pay.</summary>
    /// <returns>False when the job state was refused on the spot.</returns>
    private static bool SetOutInState(
        SimulationContext context, CreatureState creature, JobType job)
    {
        if (JobRules.StateFor(job) is not int state) return true;

        if (creature.IsRouted)
        {
            CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToRoom, state);
            return true;
        }

        StateShadow.Move(context, SystemName, creature, CreatureStateMachine.Idle);
        return StateShadow.TryMove(context, SystemName, creature, state);
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

}
