// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation.Systems;

/// <summary>Lair acquisition and sleep, including persistent debt and the four-turn tired retry.</summary>
/// <remarks>STRONGLY INFERRED from creature-needs-routine.md and lair-acquisition.md.
/// Bedless idle acquisition retains its separate twelve-turn fallback.</remarks>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/lair-acquisition.md")]
public sealed class CreatureSleepSystem : ICreatureUpdateStep
{
    private const string SystemName = "CreatureSleep";

    private readonly IPathfinder _pathfinder;
    private readonly LairSeeker _seeker;
    private readonly List<TilePos> _route = [];

    public CreatureSleepSystem() : this(new GridPathfinder()) { }

    public CreatureSleepSystem(IPathfinder pathfinder)
    {
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
        _seeker = new LairSeeker(_pathfinder);
    }

    public string Name => SystemName;

    public void Execute(SimulationContext context) => CreatureUpdateSystem.RunAlone(context, this);

    /// <summary>The sleep branch of the needs routine (<c>0x00477C20</c>), after hunger, and the
    /// lair states, in the creature's update before its state handler.</summary>
    public void Update(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (creature.Possessed) return;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        if (creature.Dk2StateId == LairRules.MistressBedExitState)
        {
            if (!HandRules.IsHeld(creature)) ExitMistressBed(context, creature);
            return;
        }


        // Explicit lair states run their own eligibility checks; autonomous
        // seeking excludes workers and creatures that never sleep.
        bool directed = creature.Dk2StateId is >= LairRules.SelectingBedState and <= LairRules.SleepingState
            || (StateShadow.IsTravelling(creature)
                && creature.UtilityNewState is >= LairRules.MakingAHomeState and <= LairRules.SleepingState);
        if (definition.Flags.HasFlag(CreatureFlags.IsWorker) && !directed) return;
        if (!LairRules.NeedsBed(definition) && !directed) return;

        // A bed on a tile that has stopped being this keeper's lair is not a
        // bed. Sold, dug out or taken by a rival while its owner was away.
        int bedIndex = LairRules.BedTileIndex(world, database, creature);
        if (bedIndex >= 0 && !LairRules.BedStillValid(world, database, creature))
        {
            context.Emit(SystemName, "bed_lost",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} tile={bedIndex}");

            LairRules.ReleaseBed(world, database, creature);
            bedIndex = -1;
            if (creature.Activity is CreatureActivity.Sleeping
                or CreatureActivity.GoingToSleep)
            {
                Wake(context, creature);
            }
        }

        // A creature in the keeper's hand is out of the dungeon: it does
        // not act until it is put down (HandRules.IsHeld, ADR-0062).
        if (HandRules.IsHeld(creature)) return;

        NeedRules.UpdateSleepClock(definition, creature, context.Tick);

        // A prisoner's cell IS what it does: it wanders (PrisonerWanderSystem,
        // every tick), eats what is dropped among it (PrisonerEatSystem) and
        // its health is the prison's (PrisonSystem, every second). ADR-0073.
        if (PrisonRules.IsImprisoned(creature)) return;
        // A collapsed creature does nothing at all (ADR-0051), and where this
        // line sits is the rule. After the bed-validity check above, because
        // a tile that stopped being a lair stopped existing whatever its
        // owner is doing and a leftover bed object would make BedTaken lie;
        // before an autonomous attempt. A rescued state-83 sleeper at zero
        // health still runs its sleep hook and handler while waiting for healing.
        if (CreatureDeathRules.IsDown(creature) && creature.Dk2StateId != LairRules.SleepingState) return;

        // A creature walking out of the dungeon does not stop for a nap,
        // and above all does not go for a bed on its way past the lair —
        // which it would, having given its own up when it decided to go
        // (CreatureAngerSystem.Leave). Without this line a deserter takes
        // the last free tile out from under a creature that intends to
        // stay, and then leaves with it.
        if (creature.Activity == CreatureActivity.Leaving) return;
        if (Combat.CombatRules.Preoccupied(creature)) return;

        switch (creature.Activity)
        {
            case CreatureActivity.Sleeping:
                Sleep(context, creature, definition);
                break;

            case CreatureActivity.GoingToSleep:
                Arriving(context, creature, definition);
                break;

            case CreatureActivity.SeekingLair:
                if (creature.Dk2StateId == LairRules.SelectingBedState)
                    SelectingBed(context, creature, definition);
                else Seeking(context, creature, definition);
                break;

            default:
                if (bedIndex >= 0)
                {
                    if (!LairRules.WantsToSleep(database, definition, creature)
                        || !NeedRules.MayAsk(creature, CreatureStateCategory.Sleeping)) break;
                    bool wounded = CreatureHealthRules.ShouldSleepFromWounds(database, definition, creature);
                    bool retryDue = context.Tick >= creature.NextSleepTryTick;
                    if (retryDue)
                        creature.NextSleepTryTick = new GameTick(context.Tick.Value
                            + NeedRules.RetryTurns * SimulationTiming.OriginalTickPeriodTicks);
                    else if (!wounded) break;
                    if (!NeedRules.IsBusy(context.World, creature, context.Tick.Value))
                        SetOff(context, creature, definition);
                    break;
                }

                // No bed. An idle creature's first act is to go and get
                // one (Q150) — and a tired one's too, which is the same
                // attempt. Only from idle: a non-worker has no other
                // state to be in today, and when it has, the attempt
                // belongs at its idle decision, not mid-job.
                if (creature.Activity != CreatureActivity.Idle) break;
                // The hero and neutral players' creatures run no needs or choosers (Q599).
                if (!NeedRules.RunsNeeds(creature)) break;

                // Same countdown shape as an imp's idle delay: a failed
                // attempt sets the timer, and the next one runs when it
                // has counted down — every IdleRedecisionTicks exactly.
                creature.TimerTicks--;
                if (creature.TimerTicks > 0) break;

                if (!_seeker.TryGoForALair(context, SystemName, creature, definition))
                    creature.TimerTicks = LairRules.IdleRedecisionTicks;
                break;
        }
    }

    /// <summary>State 79 selects a bed in the room where the creature was dropped.</summary>
    private void SelectingBed(SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int here = world.TileIndex(creature.Tile);
        if (!LairRules.IsLairTileOf(world, database, here, creature.OwnerPlayerId) || definition.LairObjectId == 0)
        {
            StateShadow.ToIdle(context, SystemName, creature);
            return;
        }
        if (InBedsRoom(context, creature))
        {
            SetOff(context, creature, definition);
            return;
        }
        RoomInstance room = RoomIndex.RoomAt(world, database, here)!;
        if (!LairRules.TryFindFreeTile(world, database, room, creature.Tile, creature.OwnerPlayerId,
            context.Rng, out int found, out _))
        {
            RoomNeedRules.Post(context, creature.OwnerPlayerId, PlayerMessageKind.RoomFull, RoomTypeId.Lair);
            StateShadow.ToIdle(context, SystemName, creature);
            return;
        }
        if (LairRules.BedOf(world, database, creature) is not null)
            LairRules.ReleaseBed(world, database, creature);
        int angle = context.Rng.NextInt(2048);
        var target = new TilePos(found % world.Width, found / world.Width);
        long x = CreatureState.CentreOf(target.X) + (1365L * TrapRules.Cosine(angle) >> 16);
        long y = CreatureState.CentreOf(target.Y) + (1365L * TrapRules.Sine(angle) >> 16);
        if (!context.Navigation.TryRouteToStance(creature, x, y, Navigation.MoveOptions.ExactSpot))
        {
            StateShadow.ToIdle(context, SystemName, creature);
            return;
        }
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, LairRules.MakingAHomeState);
    }

    /// <summary>State 80 places a bed on arrival, retrying the local search when the tile is taken.</summary>
    private void Seeking(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.HasRoute) return;
        StateShadow.Arrive(context, SystemName, creature);

        WorldState world = context.World;
        GameDatabase database = context.Database;
        TilePos at = creature.Tile;
        int here = (at.Y * world.Width) + at.X;

        if (!LairRules.IsLairTileOf(world, database, here, creature.OwnerPlayerId))
        {
            context.Emit(SystemName, "lair_attempt_failed",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} at=({at.X},{at.Y}) reason=not_in_lair");
            GiveUp(context, creature);
            return;
        }

        if (LairRules.IsFreeLairTile(world, database, here, creature.OwnerPlayerId))
        {
            MakeBed(context, creature, definition, here);
            return;
        }

        // Somebody's bed is on this tile — placed since the room was chosen, or
        // the arrival tile was simply already taken. Look next door.
        RoomInstance room = RoomIndex.RoomAt(world, database, here)!;
        if (LairRules.TryFindFreeTile(
                world, database, room, at, creature.OwnerPlayerId, context.Rng,
                out int found, out LairRules.TileFound how))
        {
            var to = new TilePos(found % world.Width, found / world.Width);
            if (context.Navigation.TryRoute(creature, to, Navigation.MoveOptions.Spot))
            {
                // State 80 walks to the next candidate in 6 and resumes in 80 (0x0046F7D3).
                CreatureStateMachine.BeginTravel(context.Hooks,
                    creature, StateShadow.TravelToPoint, LairRules.MakingAHomeState);

                context.Emit(SystemName, "lair_tile_found",
                    $"creature={creature.Id} type={definition.Name} " +
                    $"player={creature.OwnerPlayerId} at=({at.X},{at.Y}) " +
                    $"taken=({at.X},{at.Y}) to=({to.X},{to.Y}) " +
                    $"how={how.ToString().ToLowerInvariant()} steps={_route.Count}");
                return;
            }
        }

        // This room has nothing for it. Another lair, if there is one.
        if (_seeker.TrySetOutForALair(context, SystemName, creature, definition)) return;
        GiveUp(context, creature);
    }

    /// <summary>The bed goes down on the tile the creature is standing on, at the tile
    /// centre; what happens next is decided by health alone — it sleeps at once only
    /// if hurt past <c>CreatureSleepsWhenBelowPercentHealth</c>, otherwise the state
    /// ends and it is idle until tiredness brings it back.</summary>
    /// <remarks>State 79 normally releases the old bed before walking to this tile.
    /// Retain the guard for other callers that arrive while still owning a bed.</remarks>
    private static void MakeBed(
        SimulationContext context, CreatureState creature, CreatureDef definition, int tileIndex)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;

        int old = LairRules.BedTileIndex(world, database, creature);
        if (old >= 0 && old != tileIndex)
        {
            LairRules.ReleaseBed(world, database, creature);
            context.Emit(SystemName, "moved_house",
                $"creature={creature.Id} type={definition.Name} " +
                $"player={creature.OwnerPlayerId} from={old} to={tileIndex}");
        }

        LairRules.PlaceBed(context, creature, new TilePos(tileIndex % world.Width, tileIndex / world.Width));
        CreatureAngerRules.Forget(world, definition, creature, AngerBucket.Lair);
        creature.StopMoving();
        creature.TimerTicks = 0;

        TilePos at = creature.Tile;
        context.Emit(SystemName, "bed_claimed",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} object={definition.LairObjectId} " +
            $"tile=({at.X},{at.Y}) health={creature.Health}/{definition.Hp}");

        // State 80 asks the wound test once the bed is down (0x0046F785): hurt is 81, and
        // anything else returns failure, which is idle.
        if (CreatureHealthRules.ShouldSleepFromWounds(database, definition, creature))
        {
            StateShadow.Move(context, SystemName, creature, LairRules.GoingToBedState);
            FallAsleep(context, creature, definition);
            return;
        }

        StateShadow.ToIdle(context, SystemName, creature);
    }

    /// <summary>The attempt ended with nothing. Idle, and try again later.</summary>
    private static void GiveUp(SimulationContext context, CreatureState creature)
    {
        creature.StopMoving();
        creature.TimerTicks = LairRules.IdleRedecisionTicks;
        StateShadow.ToIdle(context, SystemName, creature);
    }

    // ---- sleeping in a bed it has ------------------------------------------

    /// <summary>Routes a creature to its own bed.</summary>
    private void SetOff(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        int index = LairRules.BedTileIndex(world, context.Database, creature);
        if (index < 0) return;

        var bed = new TilePos(index % world.Width, index / world.Width);

        if (!context.Navigation.TryRoute(creature, bed, Navigation.MoveOptions.ExactSpot)) return;

        if (NeedRules.IsAtAJob(creature)) NeedRules.EndJob(creature);

        // The Sleep job calls the room helper with 81 (0x0046F4B0), from whatever state the
        // need interrupted; already in the bed's room is 1, 81, and 81's own walk to the bed.
        if (InBedsRoom(context, creature))
        {
            StateShadow.Move(context, SystemName, creature, CreatureStateMachine.Idle);
            StateShadow.Move(context, SystemName, creature, LairRules.GoingToBedState);
            WalkToTheBed(context, creature);
        }
        else
        {
            CreatureStateMachine.BeginTravel(context.Hooks,
                creature, StateShadow.TravelToRoom, LairRules.GoingToBedState);
        }

        context.Emit(SystemName, "going_to_sleep",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} to=({bed.X},{bed.Y}) " +
            $"steps={_route.Count} health={creature.Health}/{definition.Hp}");
    }

    /// <summary>Falls asleep once it is standing on its own bed.</summary>
    private static void Arriving(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        WorldState world = context.World;
        TilePos at = creature.Tile;
        int here = (at.Y * world.Width) + at.X;

        // Our one walk is DK2's two: state 7 to the room, then 81 walks to the bed in 6.
        // The hand-over happens on the first tile of the bed's room.
        if (creature.Dk2StateId == StateShadow.TravelToRoom && InBedsRoom(context, creature))
        {
            StateShadow.Arrive(context, SystemName, creature);
            WalkToTheBed(context, creature);
        }

        if (here != LairRules.BedTileIndex(world, context.Database, creature))
        {
            // Still walking. If the path has run out without arriving — the
            // route was blocked mid-walk — fall back to standing up, so a
            // creature cannot be stuck in GoingToSleep forever.
            if (creature.HasRoute) return;
            Wake(context, creature);
            return;
        }

        FallAsleep(context, creature, definition);
    }

    /// <summary>State 81 walks to the bed in state 6, resuming in 82 (0x0046F8EC).</summary>
    private static void WalkToTheBed(SimulationContext context, CreatureState creature)
    {
        if (creature.Dk2StateId != LairRules.GoingToBedState) return;
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, LairRules.LyingDownState);
    }

    /// <summary>Whether the creature stands in the room its bed is in, the room helper's
    /// already-inside test. The flood fill runs only on a lair tile of its own keeper.</summary>
    private static bool InBedsRoom(SimulationContext context, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int bed = LairRules.BedTileIndex(world, database, creature);
        if (bed < 0 || !world.Contains(creature.Tile)) return false;

        int here = (creature.Tile.Y * world.Width) + creature.Tile.X;
        if (!LairRules.IsLairTileOf(world, database, here, creature.OwnerPlayerId)) return false;

        RoomInstance? room = RoomIndex.RoomAt(world, database, here);
        return room is not null && EatingRules.Contains(room, bed);
    }

    private static void FallAsleep(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        // 81's walk arrives in 82, which lines the creature up and moves to 83 - or to 1
        // and then 83 if that is refused. A bed placed underfoot walks nowhere, so 81's
        // walk and its arrival fall in this tick, as eating's bird in reach does.
        WalkToTheBed(context, creature);
        StateShadow.Arrive(context, SystemName, creature);
        if (creature.Dk2StateId == LairRules.LyingDownState)
        {
            if (LairRules.BedOf(context.World, context.Database, creature) is { } bed)
            {
                creature.Heading = bed.Heading;
                if (bed.ObjectTypeId != LairRules.MistressBedObjectId) PlaceOnBed(context.World, creature, bed);
            }
            StateShadow.MoveOrViaIdle(context, SystemName, creature, LairRules.SleepingState);
        }

        creature.SleepTicks = 0;
        creature.StopMoving();

        TilePos at = creature.Tile;
        context.Emit(SystemName, "fell_asleep",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} at=({at.X},{at.Y}) " +
            $"health={creature.Health}/{definition.Hp}");
    }

    private static void Sleep(
        SimulationContext context, CreatureState creature, CreatureDef definition)
    {
        if (creature.SleepTicks == 0 && LairRules.BedOf(context.World, context.Database, creature) is { ObjectTypeId: LairRules.MistressBedObjectId } bed)
            PlaceOnBed(context.World, creature, bed);
        if (creature.Dk2StateId == LairRules.SleepingState)
        {
            creature.SleepDebtTicks = Math.Max(0, creature.SleepDebtTicks - 1);
            creature.LastSleptTick = context.Tick;
        }
        creature.SleepTicks++;

        if (!LairRules.HasSleptEnough(context.Database, definition, creature)) return;

        context.Emit(SystemName, "woke_up",
            $"creature={creature.Id} type={definition.Name} " +
            $"player={creature.OwnerPlayerId} slept={creature.SleepTicks} " +
            $"health={creature.Health}/{definition.Hp}");

        if (LairRules.BedOf(context.World, context.Database, creature) is { ObjectTypeId: LairRules.MistressBedObjectId })
            StateShadow.Move(context, SystemName, creature, LairRules.MistressBedExitState);
        else Wake(context, creature);
    }

    private static void ExitMistressBed(SimulationContext context, CreatureState creature)
    {
        ObjectState? bed = LairRules.BedOf(context.World, context.Database, creature);
        if (bed is null) return;
        int hold = AnimationTimingRules.FirstCycleHoldTicks(context.Database.ObjectById(bed.ObjectTypeId)!.ArtSlot(7));
        if (context.Tick.Value - creature.StateEnteredTick <= hold) return;
        CreatureStateMachine.EndAction(creature);
        PlaceOnBed(context.World, creature, bed);
        creature.PosZ = bed.PosZ;
        creature.LastBedExitTick = context.Tick;
        // The native offset is in12.12 units; ours uses16.16 positions.
        int angle = (-410 * bed.Heading) & 0x7FF;
        creature.PosX += (TrapRules.Sine(angle) >> 16) << 4;
        creature.PosY += (TrapRules.Cosine(angle) >> 16) << 4;
        creature.SleepTicks = 0;
        StateShadow.Move(context, SystemName, creature, ImpStates.Wandering);
    }

    private static void PlaceOnBed(WorldState world, CreatureState creature, ObjectState bed)
    {
        TilePos at = world.TilePosition(bed.TileIndex);
        bool explicitPosition = bed.PosX != 0 || bed.PosY != 0;
        creature.PosX = explicitPosition ? bed.PosX : CreatureState.CentreOf(at.X);
        creature.PosY = explicitPosition ? bed.PosY : CreatureState.CentreOf(at.Y);
    }

    /// <summary>83's own exit for an ordinary bed returns failure, which is idle.</summary>
    private static void Wake(SimulationContext context, CreatureState creature)
    {
        creature.SleepTicks = 0;
        creature.StopMoving();
        StateShadow.ToIdle(context, SystemName, creature);
    }
}
