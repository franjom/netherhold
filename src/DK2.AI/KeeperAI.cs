// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI;

/// <summary>A computer keeper, as an input source, not part of the simulation.</summary>
/// <remarks>
/// <para>Once per tick <see cref="Think"/> returns the commands this keeper would issue by hand, for the
/// host to put in the next <see cref="InputFrame"/>. Nothing in the simulation sees this type, so the
/// world hash is untouched. Source: the original's <c>ComputerPlayer.cpp</c> (docs/KEEPER_AI.md §5, §12.1).</para>
/// <para>The proactive foundation of §12.8, the attack response (<see cref="KeeperThreatResponder"/>),
/// the battle step (<see cref="KeeperBattleStep"/>), the retreat (<see cref="KeeperRetreatStep"/>), the
/// training moves (<see cref="KeeperTrainingStep"/>), the attack planner (<see cref="KeeperAttackPlanner"/>), room
/// messages 41/42 (<see cref="KeeperRoomRequests"/>) and mood messages 17/18 (<see cref="KeeperMoodRemedies"/>).
/// Not implemented: traps and doors, prisoners and the researcher's portal move.</para>
/// <para>Deviates: randomness is the keeper's own <see cref="SimRandom"/>, not the world's (§12.1).</para>
/// </remarks>
public sealed class KeeperAI
{
    /// <summary>Ticks before an unfinished task is abandoned (§5.9).</summary>
    public const int TaskTimeoutTicks = 1680;

    /// <summary>Ticks after tagging before every gold tag is removed (§5.9).</summary>
    public const int UntagGoldAfterTicks = 240;

    /// <summary>The slap step asks <c>SpeedChangeTimer</c> (<c>+0x459</c>) to be zero. Ours runs it for
    /// a speed effect (<c>0x0048CAD0</c>) but a slap does not start it, though DK2's sets it to
    /// variable 19 x 100 = 400 when it is zero (hand-of-evil.md §4); so the time since the last slap
    /// stands in for the slap's part (computer-keeper-management.md §8).</summary>
    public const int SlapSpeedChangeTicks = 400;

    /// <summary>The gold seam is mined in an expanding square until this radius (§5.2, task 6).</summary>
    public const int MineMaxRadius = 6;

    private readonly KeeperAIParameters _p;
    private readonly SimRandom _rng;
    private readonly Func<byte, bool>? _isRoomAvailable;
    private readonly BuildArea? _area;
    private readonly PlayerId[]? _playerActivationOrder;
    private bool _directionsFixed;
    private readonly KeeperRouteCache _routes = new();

    public KeeperAI(
        PlayerId playerId, KeeperAIParameters parameters, SimRandom rng,
        Func<byte, bool>? isRoomAvailable = null, BuildArea? buildArea = null,
        IEnumerable<PlayerId>? playerActivationOrder = null)
    {
        PlayerId = playerId;
        _p = parameters;
        _rng = rng;
        _isRoomAvailable = isRoomAvailable;
        _area = buildArea;
        _playerActivationOrder = playerActivationOrder?.ToArray();
        State.InitialiseBreaches(parameters.BreachAtXPoints);
    }

    public PlayerId PlayerId { get; }
    public KeeperAIState State { get; } = new();

    /// <summary>How many routing meshes this keeper has built: one per change of the navigation
    /// bytes between turns that ask a route, not one per turn.</summary>
    public int RouteMeshBuilds => _routes.Builds;

    /// <summary>One player tick (§5.1). Returns the commands to put in the next input frame.</summary>
    public IReadOnlyList<PlayerCommand> Think(GameDatabase database, WorldState world)
        => Think(new KeeperView(database, world, PlayerId, _playerActivationOrder));

    public IReadOnlyList<PlayerCommand> Think(KeeperView view)
    {
        if (view.PlayerId != PlayerId)
            throw new ArgumentException("The observation belongs to another keeper.", nameof(view));
        view.RouteCache ??= _routes;
        var commands = new List<PlayerCommand>();
        if (view.Status is PlayerStatus.Conquered or PlayerStatus.Lost)
            return commands;
        if (view.Heart is not TilePos heart) return commands;

        BuildArea area = _area ?? RecordArea(_p, view);
        if (!_directionsFixed)
        {
            (State.DefendDirection, State.OpenDirection) = KeeperDirections.AtCreation(heart, view.Width, view.Height);
            State.SearchDirection = State.OpenDirection;
            State.SearchOrigin = heart;
            _directionsFixed = true;
        }

        int budget = _p.TendancySpeed;

        // 2. The task clock, and the timeout that abandons a stuck task.
        State.TaskTimeCounter++;
        if (State.TaskTimeCounter > TaskTimeoutTicks)
        {
            State.BeginTask(KeeperTask.Decide);
            State.WallHug = false;
        }

        // 3. Gold tags come off when their timer runs out.
        bool goldUntagged = UntagGold(view, commands);

        // 4. The attack and Call to Arms countdowns.
        if (State.CannotAttackCounter != 0) State.CannotAttackCounter--;
        if (State.CallToArmsCounter != 0) State.CallToArmsCounter--;

        // 5. The message walk, then 6. the head event, which spends the whole tick when it runs.
        var rooms = new KeeperRoomRequests(view, State, _p, RoomAvailable(view), commands, goldUntagged);
        var mood = new KeeperMoodRemedies(view, State, _p, _rng, commands);
        var responder = new KeeperThreatResponder(view, State, _p, _rng, commands, rooms, mood);
        responder.HandleMessages();
        budget = responder.Dispatch(budget);

        // 7. Attack management while a player is attacked, the call's removal test while allowance
        // remains, spending none, then the retreat of the wounded.
        var retreat = new KeeperRetreatStep(view, _p, _rng, commands);
        var battle = new KeeperBattleStep(view, State, _p, _rng, commands, retreat.Prepared);
        var readiness = new KeeperReadiness(view, _p, commands);
        var attack = new KeeperAttackPlanner(view, State, _p, _rng, commands, readiness, battle);
        if (budget > 0 && State.PlayersAttackedFlags != 0) budget = attack.Manage(budget);
        if (budget > 0 && State.CallToArmsInUse) battle.TestRemoval();
        if (budget > 0) budget = retreat.Run(budget);

        // 9. The training roll, drawn with or without allowance; readiness grants levels as it
        // tests, and a not-ready army gets the training moves (0x004FF5C0).
        if (_rng.NextInt(100) < 50 && budget > 0 && !readiness.ArmyReady())
            budget = new KeeperTrainingStep(view, State, _p, _rng, commands, readiness, retreat.Prepared).Run(budget);

        // 11. Imps while no fight is on, else the battle step; then 13. slaps.
        if (budget > 0)
        {
            if (State.FightGoingOn) budget = battle.Run(budget);
            else CreateImps(view, commands, ref budget);
        }
        if (budget > 0) Slap(view, commands, ref budget);

        // 14. DK2's prisoner step (0x005006D0) comes first and is unbuilt here, so a Prison selection
        // that would take the last allowance does not hold Sight back (ADR-0352).
        if (budget > 0 && (_p.UseSightOfEvil & SightModeMask) != 0) SightOfEvil(view, commands, ref budget);

        // 15. The task machine, while the budget lasts.
        int guard = 64;
        while (budget > 0 && guard-- > 0 && State.Task != KeeperTask.Stop)
            RunTask(view, area, commands, attack, ref budget);

        return commands;
    }

    // ----------------------------------------------------------------- housekeeping

    /// <summary>True when every gold-dig tag came off this tick.</summary>
    private bool UntagGold(KeeperView view, List<PlayerCommand> commands)
    {
        if (State.UntagGoldCounter <= 0) return false;
        if (--State.UntagGoldCounter > 0) return false;

        var tiles = new List<TilePos>();
        for (int i = 0; i < view.TileCount; i++)
        {
            TilePos p = view.FromIndex(i);
            if (view.IsTaggedByMe(p) && view.IsGoldRock(p)) tiles.Add(p);
        }
        if (tiles.Count > 0) commands.Add(new TagTilesCommand(PlayerId, tiles, false));
        return true;
    }

    /// <summary>Availability 3 (<c>0x004BAB20</c>), or the caller's override.</summary>
    private Func<byte, bool> RoomAvailable(KeeperView view) =>
        _isRoomAvailable ?? (id => view.Availability(AvailabilityKind.Room, id) == RoomNeedRules.Available);

    private void CreateImps(KeeperView view, List<PlayerCommand> commands, ref int budget)
    {
        if (view.Availability(AvailabilityKind.Spell, 4) != 3) return;
        int imps = view.ImpCount;
        int perTiles = Math.Max(1, (int)_p.TilesPerImpRatio);
        if (imps >= _p.MaximumImps || imps >= view.OwnedTiles / perTiles) return;
        // 0x004BBD30's own cost test (0x004BB8A0: cost at most the mana), predicted as the
        // simulation's Create Imp will price it; a refused cast spends no allowance in DK2.
        int cost = ImpSupplyRules.CreateImpBaseCost(view.Database)
            * (Math.Max(0, imps - ImpSupplyRules.MinimumImps(view.Database)) + 1);
        if (cost > view.Mana) return;
        commands.Add(new CreateImpCommand(PlayerId));
        budget--;
    }

    /// <summary><c>MaxBuildArea</c> (keeper <c>+0x12</c>), which the PLAYERS record unpacker loads
    /// from block <c>0x92</c> and the personality setter sets to the whole map (KEEPER_AI.md §2.3).
    /// Level 17's Harkan has (1,1)-(40,64), which refuses the neutral Portals at (53,6) and (16,70)
    /// (Q546). Deviates: a record with no rectangle gets the whole map where DK2 would store zeros;
    /// none of the 121 shipped computer records has one.</summary>
    private static BuildArea RecordArea(KeeperAIParameters p, KeeperView view) =>
        p.MaxBuildAreaMaxX == 0 && p.MaxBuildAreaMaxY == 0
            ? BuildArea.WholeMap(view.Width, view.Height)
            : new BuildArea(p.MaxBuildAreaMinX, p.MaxBuildAreaMinY, p.MaxBuildAreaMaxX, p.MaxBuildAreaMaxY);

    private void Slap(KeeperView view, List<PlayerCommand> commands, ref int budget)
    {
        long now = view.Tick.Value;
        foreach (KeeperCreatureView c in view.Creatures)
        {
            if (budget <= 0) return;
            // 0x00502F00: the activity code, health above half, the speed timer, then 1 in 100.
            if (c.OwnerPlayerId != PlayerId || c.ActivityCode != CreatureActivityRules.Slappable) continue;
            if (!c.CanSlap) continue;
            CreatureDef def = view.Database.CreatureById(c.CreatureTypeId);
            if (c.Health * 2 <= CreatureLevelRules.MaxHealth(view.Database, def, c.Level)) continue;
            if (c.SpeedChanging || (c.LastSlapTick != 0 && now - c.LastSlapTick < SlapSpeedChangeTicks)) continue;
            if (_rng.NextInt(100) >= 1) continue;
            commands.Add(new SlapCreatureCommand(PlayerId, c.Id));
            budget--;
        }
    }

    /// <summary><c>UseSightOfEvil</c> as the tick tests it: bits 20-21 of <c>+0x4B6</c>.</summary>
    private const int SightModeMask = 3;

    /// <summary>Step 14's Sight of Evil (<c>0x005003A0</c>): a random map position, drawn before the
    /// mode test; mode 1 casts on <c>random(100) &lt; 2</c>, mode 2 on <c>random(100) &lt; 20</c> at an
    /// enemy's tile; spell 1 must be at availability 3. The cast is issued whenever DK2 calls it, and
    /// spends one allowance when its permission, predicted from the capture, passes.</summary>
    private void SightOfEvil(KeeperView view, List<PlayerCommand> commands, ref int budget)
    {
        int x = _rng.NextInt(view.Width << 12);
        int y = _rng.NextInt(view.Height << 12);
        var at = new TilePos(x >> 12, y >> 12);
        switch (_p.UseSightOfEvil & SightModeMask)
        {
            case 1:
                if ((uint)_rng.NextInt(100) >= 2) return;
                break;
            case 2:
                if ((uint)_rng.NextInt(100) >= 20 || !view.IsEnemyLand(at)) return;
                break;
            default:
                return;
        }
        if (view.Availability(AvailabilityKind.Spell, KeeperSpellRules.SightOfEvil) != 3) return;
        commands.Add(new CastSpellAtPlaceCommand(PlayerId, KeeperSpellRules.SightOfEvil, x, y));
        // 0x004BB8A0 inside 0x004BBD30: the cost against the mana, the cooldown, then location 5 and target 4.
        KeeperSpellDef? spell = view.Database.KeeperSpellById(KeeperSpellRules.SightOfEvil);
        if (spell is not null && (spell.ManaCost == 0 || spell.ManaCost <= view.Mana)
            && KeeperBattleStep.Ready(view, spell) && view.SightPlaceAllowed(at))
            budget--;
    }

    // ----------------------------------------------------------------- the task machine

    private void RunTask(KeeperView view, BuildArea area, List<PlayerCommand> commands, KeeperAttackPlanner attack, ref int budget)
    {
        switch (State.Task)
        {
            case KeeperTask.Decide:
                budget--;
                // The attack planner first; the room planner when it declines (Flags bit 0 is
                // always on); the gold step when neither started a task.
                if (!attack.Plan() && !PlanRoom(view, area) && WantsGold(view)) GoldStep(view, commands);
                break;

            case KeeperTask.FindSite:
                budget--;
                FindSite(view, area);
                break;

            case KeeperTask.DigRectangle:
                DigRectangle(view, commands, ref budget);
                break;

            case KeeperTask.PlaceRoom:
                PlaceRoom(view, commands, ref budget);
                break;

            case KeeperTask.Done:
                budget--;
                State.BeginTask(KeeperTask.Decide);
                if (WantsGold(view)) GoldStep(view, commands);
                break;

            case KeeperTask.RouteToGold:
                budget--;
                RouteToGold(view);
                break;

            case KeeperTask.MineSeam:
                budget--;
                MineSeam(view, commands);
                break;

            case KeeperTask.DigPath:
                budget--;
                DigPath(view, area, commands, ref budget);
                break;

            default:
                budget = 0;
                break;
        }
    }

    // ----------------------------------------------------------------- rooms

    /// <summary>The room planner (§5.5, <c>0x004FC690</c>). True when a task was started.</summary>
    /// <remarks>A taken request leaves Portal (3) waiting behind it, so the next pass looks for a
    /// neutral portal unless a room message replaces the placeholder first; Portal itself is taken
    /// and cleared. A request with <see cref="KeeperRoomRequests.ExpandBit"/> enlarges a random room of
    /// its type when <see cref="RoomExpansion"/> finds a side, and is otherwise built anew.</remarks>
    private bool PlanRoom(KeeperView view, BuildArea area)
    {
        if (!State.NextRoomToBuildSet)
        {
            State.CurrentRoomToBuild = 0; // 0x004FC690 clears this before the chooser.
            byte? chosen = RoomPlanner.ChooseRoom(view, _p, RoomAvailable(view), State.CurrentRoomToBuild);
            if (chosen is null)
            {
                State.NextRoomToBuild = (byte)RoomTypeId.Portal;
                State.AllRoomsBuilt = true;
            }
            else
            {
                State.AllRoomsBuilt = false;
                State.NextRoomToBuild = chosen.Value;
            }
            State.NextRoomToBuildSet = true;
        }

        byte request = State.NextRoomToBuild;
        State.CurrentRoomToBuild = request;
        if (request == (byte)RoomTypeId.Portal) State.NextRoomToBuildSet = false;
        else State.NextRoomToBuild = (byte)RoomTypeId.Portal;
        byte room = (byte)(request & ~KeeperRoomRequests.ExpandBit);
        bool portal = room is (byte)RoomTypeId.Portal or (byte)RoomTypeId.HeroPortal;

        // A neutral room of that type within reach is claimed by digging to it. DK2 searches with the
        // request byte, so an expansion request never matches one.
        KeeperRoomView? neutral = RoomPlanner.ClaimableNeutralRoom(view, _p, portal ? room : request, area, out TilePos origin);
        if (neutral is not null)
        {
            TilePos target = view.PositionOf(neutral);
            State.DigRoomAtCurrent = origin;
            State.DigRoomAt = target;
            State.WallHug = false;
            State.BeginTask(KeeperTask.DigPath, KeeperTask.Done);
            return true;
        }

        if (portal)
        {
            State.NextRoomToBuildSet = false;
            return false;
        }

        RoomDef def = view.Database.RoomById(room);
        if (RoomRules.CostPerTile(def) * _p.DefaultRoomSize > view.Gold)
            return RouteToStoredGold(view, takeFreshScan: true);

        if ((request & KeeperRoomRequests.ExpandBit) != 0)
        {
            KeeperRoomView? existing = view.RandomRoomOfType(PlayerId, (RoomTypeId)room, _rng);
            if (existing is not null && RoomExpansion.Find(view, existing) is RoomSite grown)
            {
                // 0x004FBE00 sets task 2 over the grown rectangle and leaves NextTask alone.
                State.RectangleOrigin = grown.Origin;
                State.RectangleWidth = grown.Width;
                State.RectangleHeight = grown.Height;
                State.RectangleStart = grown.Origin;
                State.RectangleStepX = State.RectangleStepY = 1;
                State.JoinPathPending = false; // the cursor is the rectangle's corner: no path
                State.BeginTask(KeeperTask.DigRectangle, State.NextTask);
                return true;
            }
            State.CurrentRoomToBuild = room;
        }

        State.SearchDirection = RoomPlanner.DirectionFor(room, State, _rng);
        State.SearchOrigin = RoomPlanner.Origin(view, _p, room, _rng) ?? view.Heart!.Value;
        State.BeginTask(KeeperTask.FindSite);
        return true;
    }

    private void FindSite(KeeperView view, BuildArea area)
    {
        SiteChoice? site = SiteFinder.Search(view, State.SearchOrigin, State.SearchDirection,
            _p.DefaultRoomSize, _p.TilesLeftBetweenRooms & 0xF, _p.Openness, area);
        if (site is SiteChoice found)
        {
            State.RectangleOrigin = found.Site.Origin;
            State.RectangleWidth = found.Site.Width;
            State.RectangleHeight = found.Site.Height;
            State.RectangleStart = found.Start;
            State.RectangleStepX = found.StepX;
            State.RectangleStepY = found.StepY;
            State.JoinPathStart = found.JoinPoint;
            State.JoinPathEnd = State.SearchOrigin;
            State.JoinPathPending = true;
            State.BeginTask(KeeperTask.DigRectangle);
            return;
        }

        // 0x004FCE70: preferred, +1, -1, +2; after +2 a roll of 5 to 99 restores the preferred
        // direction and reseeds from a random non-bridge room's stored position, else the task ends.
        int preferred = RoomPlanner.PreferredDirection(State.CurrentRoomToBuild, State);
        if (RoomPlanner.NextSearchDirection(State.SearchDirection, preferred) is int next)
        {
            State.SearchDirection = next;
            return;
        }
        if (_rng.NextInt(100) > 4)
        {
            State.SearchDirection = preferred;
            if (view.RandomNonBridgeRoom(PlayerId, _rng) is KeeperRoomView reseed)
            {
                State.SearchOrigin = view.PositionOf(reseed);
                return;
            }
        }
        State.BeginTask(KeeperTask.Decide);
    }

    /// <summary>The rectangle in the order tasks 2 and 3 walk it: from <see cref="KeeperAIState.RectangleStart"/>
    /// along x by its step, then a row along y.</summary>
    private IEnumerable<TilePos> RectangleWalk()
    {
        for (int j = 0; j < State.RectangleHeight; j++)
            for (int i = 0; i < State.RectangleWidth; i++)
                yield return new TilePos(State.RectangleStart.X + (i * State.RectangleStepX),
                    State.RectangleStart.Y + (j * State.RectangleStepY));
    }

    /// <summary>Task 2 (<c>0x004FD030</c>). After a site search the first pass digs the joining path
    /// from the search's origin to where its walk stopped (task 7, continuation 2), spending nothing;
    /// DK2 tests nothing first, so a site next to the dungeon still gets its path. The next pass tags
    /// the rectangle in walking order and spends one allowance.</summary>
    /// <remarks>DK2 tags a tile only while its dig pool has more than 20 free entries; ours does not
    /// model the pool's size.</remarks>
    private void DigRectangle(KeeperView view, List<PlayerCommand> commands, ref int budget)
    {
        if (State.JoinPathPending)
        {
            State.JoinPathPending = false;
            State.DigRoomAtCurrent = State.JoinPathEnd;
            State.DigRoomAt = State.JoinPathStart;
            State.WallHug = false;
            State.BeginTask(KeeperTask.DigPath, KeeperTask.DigRectangle);
            return;
        }

        budget--;
        var toTag = new List<TilePos>();
        foreach (TilePos t in RectangleWalk())
            if (view.InBounds(t) && view.Terrain(t).IsTaggable && !view.IsTaggedByMe(t)) toTag.Add(t);
        if (toTag.Count > 0)
        {
            commands.Add(new TagTilesCommand(PlayerId, toTag, true));
            State.UntagGoldCounter = UntagGoldAfterTicks;
        }
        State.DigRoomAtCurrent = State.RectangleStart; // task 3's cursor (+0x76)
        State.BeginTask(KeeperTask.PlaceRoom);
    }

    /// <summary>Task 3 (<c>0x004FD2B0</c>): each allowance unit works on the cursor tile alone. A tile
    /// the placement test accepts is built and the turn's task work ends there; a solid tile, or one
    /// not yet this keeper's, holds the cursor, is tagged when taggable and untagged, and eats the
    /// allowance; any other tile (built, or refused for another reason) advances it. The room is
    /// therefore laid tile by tile in walking order, each waiting until it is dug and claimed.</summary>
    /// <remarks>Ours builds one tile a tick: our build lands on the next tick, where DK2's is made at
    /// once and the next pass in the same turn can take the following tile. Solid stands in for
    /// navigation's solidity, as in task 7.</remarks>
    private void PlaceRoom(KeeperView view, List<PlayerCommand> commands, ref int budget)
    {
        // 0x004FD2B0 builds the type without the expansion bit.
        RoomDef def = view.Database.RoomById((byte)(State.CurrentRoomToBuild & ~KeeperRoomRequests.ExpandBit));
        while (budget > 0)
        {
            budget--;
            TilePos at = State.DigRoomAtCurrent;
            if (view.InBounds(at))
            {
                if (view.CanBuild(at, def) == RoomPlacement.Allowed)
                {
                    commands.Add(new BuildRoomCommand(PlayerId, (RoomTypeId)def.RoomId, [at]));
                    budget = 0;
                    return;
                }
                if (view.IsSolid(at) || view.Tile(at).OwnerPlayerId != PlayerId)
                {
                    if (view.Terrain(at).IsTaggable && !view.IsTaggedByMe(at))
                    {
                        commands.Add(new TagTilesCommand(PlayerId, [at], true));
                        State.UntagGoldCounter = UntagGoldAfterTicks;
                    }
                    budget = 0;
                    return;
                }
            }
            if (!AdvanceRectangleCursor())
            {
                // The rectangle's end: the cursor back to its start, task 4, the allowance gone.
                State.DigRoomAtCurrent = State.RectangleStart;
                State.BeginTask(KeeperTask.Done);
                budget = 0;
                return;
            }
        }
    }

    /// <summary>One step of the rectangle cursor along x, wrapping to the next row; false past the
    /// last row.</summary>
    private bool AdvanceRectangleCursor()
    {
        TilePos c = State.DigRoomAtCurrent;
        int x = c.X + State.RectangleStepX, y = c.Y;
        if (x == State.RectangleStart.X + (State.RectangleWidth * State.RectangleStepX))
        {
            x = State.RectangleStart.X;
            y += State.RectangleStepY;
            if (y == State.RectangleStart.Y + (State.RectangleHeight * State.RectangleStepY)) return false;
        }
        State.DigRoomAtCurrent = new TilePos(x, y);
        return true;
    }

    // ----------------------------------------------------------------- gold

    private bool WantsGold(KeeperView view) =>
        view.Gold < _p.TendancyMoney
        && State.Task is not (KeeperTask.RouteToGold or KeeperTask.MineSeam)
        && State.NextTask != KeeperTask.MineSeam;

    /// <summary>The gold step (<c>0x004FD590</c>, §5.10): a stored deposit becomes task 5's target;
    /// with none stored the keeper casts Create Gold beside the heart, spending no allowance and
    /// ignoring the result. Both arms rescan and leave the index at 0, so the stored list is always
    /// the previous scan's. True when a task started.</summary>
    private bool GoldStep(KeeperView view, List<PlayerCommand> commands)
    {
        if (RouteToStoredGold(view)) return true;
        if (view.Availability(AvailabilityKind.Spell, KeeperSpellRules.CreateGold) == 3)
        {
            // The centre of the tile diagonally south-east of the heart's centre (0x004FD619): inside
            // a 5x5 Heart's refused middle, so the simulation refuses it there (Q540).
            TilePos heart = view.Heart!.Value;
            commands.Add(new CastSpellAtPlaceCommand(PlayerId, KeeperSpellRules.CreateGold,
                ((heart.X + 1) << 12) + HalfTile, ((heart.Y + 1) << 12) + HalfTile));
        }
        return false;
    }

    /// <summary>Half a tile in 1/4096 tile: a tile's centre.</summary>
    private const int HalfTile = 0x800;

    /// <summary>The arm the gold step and the room planner's cost test share (<c>0x004FD590</c>,
    /// <c>0x004FC9AE</c>): copy the stored first deposit into <see cref="KeeperAIState.DigRoomAt"/>,
    /// rescan, and start task 5 with <c>NextTask</c> kept. Without a stored deposit only the rescan
    /// runs; the cost test then takes the fresh scan's first, where DK2 routes to (0, 0) (Q539).</summary>
    private bool RouteToStoredGold(KeeperView view, bool takeFreshScan = false)
    {
        GoldDeposit? stored = StoredDeposit();
        State.CurrentGoldArea = 0;
        State.GoldDeposits.Clear();
        State.GoldDeposits.AddRange(GoldScanner.Scan(view));
        if (stored is null && takeFreshScan) stored = StoredDeposit();
        if (stored is not GoldDeposit deposit) return false;
        State.DigRoomAt = deposit.Centre;
        State.BeginTask(KeeperTask.RouteToGold, State.NextTask);
        return true;
    }

    /// <summary>The slot <c>CurrentGoldArea</c> names, when its gold total is nonzero.</summary>
    private GoldDeposit? StoredDeposit() =>
        State.CurrentGoldArea < State.GoldDeposits.Count && State.GoldDeposits[State.CurrentGoldArea].GoldAmount != 0
            ? State.GoldDeposits[State.CurrentGoldArea]
            : null;

    /// <summary>Task 5 (<c>0x004FD660</c>): dig from the own room nearest the target the gold step
    /// stored, then mine it (task 6).</summary>
    private void RouteToGold(KeeperView view)
    {
        KeeperRoomView? from = view.NearestOwnRoom(State.DigRoomAt);
        State.DigRoomAtCurrent = from is null ? view.Heart!.Value : KeeperView.Centre(from);
        State.WallHug = false;
        State.MineRadius = 0;
        State.BeginTask(KeeperTask.DigPath, KeeperTask.MineSeam);
    }

    /// <summary>Task 6 (<c>0x004FD750</c>): tag gold in a square ring around the stored target,
    /// one radius a pass.</summary>
    private void MineSeam(KeeperView view, List<PlayerCommand> commands)
    {
        TilePos c = State.DigRoomAt;
        int r = State.MineRadius;
        var tiles = new List<TilePos>();
        for (int y = c.Y - r; y <= c.Y + r; y++)
            for (int x = c.X - r; x <= c.X + r; x++)
            {
                if (Math.Max(Math.Abs(x - c.X), Math.Abs(y - c.Y)) != r) continue;
                var t = new TilePos(x, y);
                if (view.IsGoldRock(t) && view.Terrain(t).IsTaggable && !view.IsTaggedByMe(t)) tiles.Add(t);
            }
        if (tiles.Count > 0)
        {
            commands.Add(new TagTilesCommand(PlayerId, tiles, true));
            State.UntagGoldCounter = UntagGoldAfterTicks;
        }

        if (++State.MineRadius > MineMaxRadius)
        {
            State.MineRadius = 0;
            State.BeginTask(KeeperTask.Decide);
        }
    }

    // ----------------------------------------------------------------- paths

    private void DigPath(KeeperView view, BuildArea area, List<PlayerCommand> commands, ref int budget)
    {
        // 0x004FDC50: a target outside MaxBuildArea aborts only a continuation other than 0 or 4,
        // so an attack tunnel or a room's own path digs on past the area.
        if (!area.Contains(State.DigRoomAt) && State.NextTask is not (KeeperTask.Decide or KeeperTask.Done))
        {
            State.BeginTask(KeeperTask.Done, KeeperTask.Done);
            return;
        }
        if (State.DigRoomAtCurrent == State.DigRoomAt)
        {
            EndPath();
            return;
        }
        // During an attack, enemy land in the cursor's 3x3 stores the rest of the tunnel for the
        // planner to resume, and the task ends here (ADR-0298).
        if (State.AttackTarget != 0 && view.EnemyLandAround(State.DigRoomAtCurrent))
        {
            KeeperAttackPlanner.StoreTunnel(State);
            EndPath();
            return;
        }
        // The cursor's own tile comes before any step: water or lava is bridged where it lies.
        bool hazard = view.IsWater(State.DigRoomAtCurrent) || view.IsLava(State.DigRoomAtCurrent);
        bool walkOwed = false;
        if (State.BridgeRequestedAt is TilePos requested)
        {
            State.BridgeRequestedAt = null;
            // A span that landed was paid for by last tick's allowance, which in DK2 also stepped
            // the walker at once, with no route query between; a refused one is retried below.
            walkOwed = requested == State.DigRoomAtCurrent && !hazard;
            if (walkOwed) budget++;
        }
        if (hazard)
        {
            Bridge(view, commands, ref budget);
            return;
        }

        TilePos cursor = State.DigRoomAtCurrent;
        if (!walkOwed)
        {
            // Rock on the cursor is tagged and then waited on: the cursor does not move until the
            // tile is dug, and every pass meanwhile spends an allowance (0x004FDE91).
            if (view.Terrain(cursor).IsTaggable)
            {
                // DK2 skips the tag when 0x00508290 finds a task of kind 1 or 2 at the tile; ours
                // assumes a tile it tagged is one.
                if (!view.IsTaggedByMe(cursor))
                {
                    commands.Add(new TagTilesCommand(PlayerId, [cursor], true));
                    State.UntagGoldCounter = UntagGoldAfterTicks;
                }
                budget = 0;
                return;
            }
            // Open ground: the path is done once the target routes to the cursor (0x00452970).
            if (TargetRoutesToCursor(view))
            {
                State.DigRoomAtCurrent = State.DigRoomAt;
                EndPath();
                return;
            }
        }

        WalkStep step = LineWalker.Step(view, State);
        switch (step.Outcome)
        {
            case WalkOutcome.Arrived:
                EndPath();
                break;
            case WalkOutcome.Dig:    // the cursor now stands on the rock; the next pass tags it
            case WalkOutcome.Moved:
            case WalkOutcome.Hazard: // the cursor now stands on it; the next pass bridges it
                break;
            case WalkOutcome.Failed:
                // DK2's walker returns 0 on a hug that loops back and the task takes NextTask
                // (0x004FDFA3); task 2 then tags its rectangle, its path already taken.
                EndPath();
                break;
        }
    }

    /// <summary>Task 7's route test, <c>0x00452970</c> → <c>0x004D1700</c>: from the target's tile
    /// centre to the cursor's, for this keeper, with option 1 (its own locked doors pass) and no
    /// water, lava or portal hole. DK2 leaves the water global from the previous query; ours
    /// refuses water. Deviates: a target on a solid tile never routes. Our search never judges
    /// the triangle a route starts in, so from gold or rock it leaves through any side, and
    /// Level 17's seam (19,2) routed past walls to the Portal; DK2's Harkan digs to it, so its
    /// query probably fails there (inferred from the outcome, Q561).</summary>
    private bool TargetRoutesToCursor(KeeperView view) =>
        !view.IsSolid(State.DigRoomAt) && view.CanRoute(CreatureState.CentreOf(State.DigRoomAt.X), CreatureState.CentreOf(State.DigRoomAt.Y),
            CreatureState.CentreOf(State.DigRoomAtCurrent.X), CreatureState.CentreOf(State.DigRoomAtCurrent.Y),
            new MoverProfile(CanWalkOnWater: false, CanWalkOnLava: false, Owner: PlayerId.Value, PassOwnLockedDoors: true));

    /// <summary>Task 7's exit: <c>Task</c> takes <c>NextTask</c>, which is left at 4 (0x004FDC50).</summary>
    private void EndPath()
    {
        State.WallHug = false;
        State.BeginTask(State.NextTask, KeeperTask.Done);
    }

    /// <summary>
    /// Task 7 on water or lava (0x004FDC50, docs/research/computer-keeper-handlers.md §9): water
    /// takes a Wooden Bridge when it is available, else a Stone one; lava only a Stone one. With
    /// neither the task ends. A span the placement test refuses is retried on the same tile until
    /// the allowance is gone, as DK2's loop retries it. A submitted span lands on the next tick
    /// (ADR-0244), so the allowance ends there too and the walk resumes once it is seen (ADR-0304).
    /// </summary>
    private void Bridge(KeeperView view, List<PlayerCommand> commands, ref int budget)
    {
        TilePos at = State.DigRoomAtCurrent;
        RoomDef? bridge = BridgeFor(view, water: view.IsWater(at));
        if (bridge is null)
        {
            EndPath();
            return;
        }
        // DK2's wrapper (0x004C5B40) builds the tile free when one tile's price exceeds the money;
        // the command charges, so ours waits for the gold instead (ADR-0304).
        if (view.CanBuild(at, bridge) == RoomPlacement.Allowed && view.Gold >= RoomRules.CostPerTile(bridge))
        {
            commands.Add(new BuildRoomCommand(PlayerId, (RoomTypeId)bridge.RoomId, [at]));
            State.BridgeRequestedAt = at;
        }
        budget = 0;
    }

    /// <summary>The bridge task 7 asks for: DK2's code names rooms 8 and 17 here, not placement flags,
    /// so lava never takes the Wooden Bridge its flags would allow.</summary>
    private RoomDef? BridgeFor(KeeperView view, bool water)
    {
        if (water && IsRoomAvailable(view, RoomTypeId.WoodenBridge, out RoomDef? wood)) return wood;
        return IsRoomAvailable(view, RoomTypeId.StoneBridge, out RoomDef? stone) ? stone : null;
    }

    /// <summary>Availability 3 (0x004BAB20), or the caller's override; a room the database lacks is not.</summary>
    private bool IsRoomAvailable(KeeperView view, RoomTypeId room, out RoomDef? def)
    {
        byte id = (byte)room;
        if (!view.Database.Rooms.TryGetValue((RoomTypeId)id, out def)) return false;
        return _isRoomAvailable?.Invoke(id) ?? view.Availability(AvailabilityKind.Room, id) == 3;
    }
}
