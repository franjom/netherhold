// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;

namespace DK2.Simulation;

/// <summary>What the win sequence keeps on the player (campaign-win-sequence.md §2, §5, §7).</summary>
public struct PlayerFinale
{
    /// <summary>DK2's player <c>+0x76</c> bits that the sequence and the continuation write:
    /// <see cref="FinaleRules.SequenceStarted"/>, <see cref="FinaleRules.ReaperCreated"/> and the
    /// three of <c>0x004C0120</c>.</summary>
    public uint Flags;
    /// <summary><c>+0xB29</c>/<c>+0xB2D</c>/<c>+0xB31</c>: where the Reaper lands.</summary>
    public int PointX, PointY, PointZ;
    /// <summary><c>+0xB35</c>/<c>+0xB39</c>: the Portal Gem's tile.</summary>
    public int GemTileX, GemTileY;
    /// <summary><c>+0xB3D</c>: the gem's thing id, ours its object id; 0 for none.</summary>
    public int GemObjectId;
    /// <summary>The tick the camera's timed move to a point ends (<c>+0xDFA</c>), 0 for none.</summary>
    public long CameraGlideEndTick;
    /// <summary><c>+0x111D</c> FireworksTime as our tick, 0 for none.</summary>
    public long FireworksTick;
    /// <summary>The tick action 47 began in state 233 (its <c>+0x3A5</c> stamp).</summary>
    public long PickupTick;
    /// <summary>State 233's two flags: 1 once the 13 s stage ran (<c>+0xAB</c>), 2 once the win
    /// screen was asked for (<c>+0x72</c>).</summary>
    public byte PickupStage;

    public readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(Flags); hash.Add(PointX); hash.Add(PointY); hash.Add(PointZ);
        hash.Add(GemTileX); hash.Add(GemTileY); hash.Add(GemObjectId);
        hash.Add(CameraGlideEndTick); hash.Add(FireworksTick); hash.Add(PickupTick); hash.Add(PickupStage);
    }

    public readonly void Write(BinaryWriter w)
    {
        w.Write(Flags); w.Write(PointX); w.Write(PointY); w.Write(PointZ);
        w.Write(GemTileX); w.Write(GemTileY); w.Write(GemObjectId);
        w.Write(CameraGlideEndTick); w.Write(FireworksTick); w.Write(PickupTick); w.Write(PickupStage);
    }

    public static PlayerFinale Read(BinaryReader r) => new()
    {
        Flags = r.ReadUInt32(), PointX = r.ReadInt32(), PointY = r.ReadInt32(), PointZ = r.ReadInt32(),
        GemTileX = r.ReadInt32(), GemTileY = r.ReadInt32(), GemObjectId = r.ReadInt32(),
        CameraGlideEndTick = r.ReadInt64(), FireworksTick = r.ReadInt64(), PickupTick = r.ReadInt64(),
        PickupStage = r.ReadByte(),
    };
}

/// <summary>The campaign win (campaign-win-sequence.md): the dispatch at the end of
/// <c>0x004BE630</c>, the Horned Reaper finale <c>0x004BF410</c> and its states 228-234, the
/// secret-level celebration <c>0x004BF9E0</c>, and the continuation <c>0x004C0120</c>.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/campaign-win-sequence.md")]
public static class FinaleRules
{
    private const string SystemName = "finale";

    public const uint SequenceStarted = 0x40;
    public const uint ReaperCreated = 0x800;
    public const uint ResultContinued = 0x40000;
    public const uint ContinuedFromConquered = 0x80000;
    public const uint Continued = 0x400000;

    public const int VanishState = 228, WaitCameraState = 229, AppearState = 230, RoarState = 231;
    public const int RouteToGemState = 232, PickUpGemState = 233, WalkState = 234;
    public const int CelebrateState = 270;

    /// <summary>The LEVEL flags of <c>0x004BEDC0</c>: a secret level, and the special one no shipped
    /// level carries.</summary>
    public const ushort SecretLevelFlag = 0x20, SpecialLevelFlag = 0x40;

    private const int LandingTries = 500, NearTries = 0x80, NearDistanceSquared = 0x19;
    /// <summary>The route request option word of state 232 (<c>0x004744A3</c>).</summary>
    private const MoveOptions GemRouteOptions = (MoveOptions)0x26;
    /// <summary>The camera's timed move with no duration lasts a tenth of the summed squared
    /// quarter-unit offsets, 25.6 ms a square tile, capped at 500 ms (<c>0x0044A270</c>); the
    /// glide state 230 waits on runs from the gem to a landing at least five tiles away, so it
    /// reaches the cap. Ours holds every glide for the cap in ticks (HYPOTHESIS: the wall-clock
    /// deadline against 250 ms turns; a shorter glide, as to the fallback corner, holds too long).</summary>
    private const int GlideTicks = 2;
    private const int WinZoom = 0x5800, WinZoomMilliseconds = 1000, KeeperCameraMode = 3;
    private const int GemPath = 102, ArrivalPath = 100, PickupPath = 101;
    private const int SmokeEffect = 122, BirthTrailEffect = 30, GrandEntranceEffect = 126, EndOfLevelEffect = 258;
    private const int FireworksEffect = 320;
    private const int ArrivalAction = 59, RoarAction = 46, PickupAction = 47, CelebrationAction = 0x39;
    private const int ArrivalArt = 3, PickupArt = 23, CelebrationArt = 28;

    /// <summary>The end of <c>0x004BE630</c> for a win (§1): the local campaign keeper runs the
    /// finale, or on a LEVEL with flag <c>0x20</c> or <c>0x40</c> the celebration; in My Pet Dungeon
    /// every winner celebrates. The profile writes of that dispatch are the host's.</summary>
    public static void OnVerdict(SimulationContext context, PlayerId playerId, byte status)
    {
        if (status != PlayerStatus.Won) return;
        WorldState world = context.World;
        if (world.GameMode == GameMode.MyPetDungeon)
        {
            Celebrate(context, playerId);
            return;
        }
        if (world.GameMode != GameMode.Campaign || playerId != world.LocalPlayerId) return;
        ushort flags = context.Database.Level?.Flags ?? 0;
        if ((flags & (SecretLevelFlag | SpecialLevelFlag)) != 0) Celebrate(context, playerId);
        else Begin(context, playerId);
    }

    /// <summary><c>0x004BF410</c> (§2).</summary>
    public static void Begin(SimulationContext context, PlayerId playerId)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (world.GameMode == GameMode.Multiplayer) return;
        ref PlayerState player = ref world.Player(playerId);
        (ushort hx, ushort hy) = ManaRemovalLifecycle.HeartTile(world, database, playerId);
        player.Finale.PointX = (hx - 2) << 12;
        player.Finale.PointY = (hy - 2) << 12;
        player.Finale.PointZ = 0;

        ObjectState? gem = FindGem(world);
        if (gem is not null)
        {
            int gx = gem.PosX >> 12, gy = gem.PosY >> 12;
            player.Finale.GemTileX = gx;
            player.Finale.GemTileY = gy;
            player.Finale.GemObjectId = gem.Id;
            if (!FindLanding(context, playerId, gem, gx, gy, out int lx, out int ly))
            {
                // All tries failed: the gem tile's corner, not its centre (0x004BF70F).
                lx = gx << 12;
                ly = gy << 12;
            }
            player.Finale.PointX = lx;
            player.Finale.PointY = ly;
            player.Finale.PointZ = 0;

            CreatureState? reaper = PrepareReaper(context, ref player);
            if (reaper is null) return;
            reaper.ReaperCanAcquireFight = reaper.ReaperIsAttackable = false;
            reaper.Heading = 0x400;
            if (CreatureStateMachine.TrySetState(context.Hooks, reaper, VanishState, context.Tick.Value))
            {
                View(context, ScriptedViewKind.CameraMode, playerId, KeeperCameraMode);
                Glide(context, ref player, new TilePos(gx, gy));
                View(context, ScriptedViewKind.CameraZoom, playerId, WinZoom, value2: WinZoomMilliseconds);
            }
            player.Finale.Flags |= SequenceStarted;
            context.Emit(SystemName, "begun", $"player={playerId} reaper={reaper.Id} gem={gem.Id} landing=({lx >> 12},{ly >> 12})");
        }
        else context.Emit(SystemName, "no_gem", $"player={playerId}");
        View(context, ScriptedViewKind.Letterbox, playerId, 0, flag: true);
    }

    /// <summary>The first object of type 28 in the first player list that holds one: DK2 walks the
    /// world's players, then each one's objects. Ours walks players in slot order and objects in
    /// creation order (HYPOTHESIS for the order within one list; a level holds one gem).</summary>
    private static ObjectState? FindGem(WorldState world)
    {
        PlayerState[] players = world.RawPlayers;
        for (int p = 0; p < players.Length; p++)
            foreach (ObjectState thing in world.Objects)
                if (thing.OwnerPlayerId == players[p].PlayerId.Value && thing.ObjectTypeId == TriggerExecutor.PortalGemObjectId)
                    return thing;
        return null;
    }

    /// <summary>The 500 tries of §2 step 3, two draws each on the simulation stream.</summary>
    private static bool FindLanding(SimulationContext context, PlayerId playerId, ObjectState gem, int gx, int gy, out int x, out int y)
    {
        WorldState world = context.World;
        var points = new List<Fixed16Vector>();
        var from = new Fixed16Vector(gem.PosX << 4, gem.PosY << 4);
        var mover = new MoverProfile(false, false, playerId.Value);
        context.Navigation.Refresh();
        for (int attempt = 0; attempt < LandingTries; attempt++)
        {
            int dx = context.Rng.NextInt(16) - 8;
            int dy = context.Rng.NextInt(16) - 8;
            if (attempt < NearTries && dx * dx + dy * dy < NearDistanceSquared) continue;
            var tile = new TilePos(gx + dx, gy + dy);
            if (!world.Contains(tile)) continue;
            // DK2 keeps one explored bit (0x00510000); ours asks the winner's layer.
            if (!world.Tile(tile).IsExploredBy(playerId)) continue;
            if ((context.Navigation.NavByte(tile.X, tile.Y) & 8) != 0) continue;
            if (ExcludedRoom(world, context.Database, tile) || HoldsAThing(world, tile)) continue;
            x = CreatureState.CentreOf(tile.X);
            y = CreatureState.CentreOf(tile.Y);
            points.Clear();
            if (!context.Navigation.Route(from, new Fixed16Vector(x << 4, y << 4), mover, points, refresh: false)) continue;
            return true;
        }
        x = y = 0;
        return false;
    }

    /// <summary>The jump table at <c>0x004BF9C8</c>: Portal, Dungeon Heart, Prison, Temple, Combat
    /// Pit and the six hero rooms 18-23.</summary>
    private static bool ExcludedRoom(WorldState world, GameDatabase database, TilePos tile) =>
        RoomIndex.RoomAt(world, database, tile.Y * world.Width + tile.X)?.Definition.TypeId is
            RoomTypeId.Portal or RoomTypeId.DungeonHeart or RoomTypeId.Prison or RoomTypeId.Temple
            or RoomTypeId.CombatPit or (>= RoomTypeId.HeroGateFinal and <= RoomTypeId.HeroGate3By1);

    /// <summary>A creature, object, trap or door on the tile (thing classes 0, 2, 3 and 4).</summary>
    private static bool HoldsAThing(WorldState world, TilePos tile)
    {
        if (world.DoorAt(tile.X, tile.Y) is not null) return true;
        foreach (TrapState trap in world.Traps)
            if (trap.X == tile.X && trap.Y == tile.Y) return true;
        foreach (CreatureState creature in world.Creatures)
            if (creature.Tile == tile) return true;
        foreach (ObjectState thing in world.Objects)
            if (thing.PosX >> 12 == tile.X && thing.PosY >> 12 == tile.Y) return true;
        return false;
    }

    /// <summary>§2 step 4: the player's first Reaper, freed of its timer and kicked to state 1, or a
    /// level-1 one made at the point.</summary>
    private static CreatureState? PrepareReaper(SimulationContext context, ref PlayerState player)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        CreatureState? reaper = ReaperRules.OwnedReaper(world, database, player);
        if (reaper is not null)
        {
            player.Finale.Flags &= ~ReaperCreated;
            reaper.ReaperTicks = 0;
            reaper.IsOneShotReaper = false;
            reaper.ReaperCanAcquireFight = reaper.ReaperIsAttackable = false;
            CreatureStateMachine.TrySetState(context.Hooks, reaper, CreatureStateMachine.Idle, context.Tick.Value);
            reaper.StopMoving();
            return reaper;
        }
        CreatureDef? definition = ReaperRules.ReaperDefinition(database);
        var at = new TilePos(player.Finale.PointX >> 12, player.Finale.PointY >> 12);
        if (definition is null || !world.Contains(at)) return null;
        reaper = world.SpawnCreature(database, definition.CreatureId, player.PlayerId, at);
        reaper.PosX = reaper.LastPosX = player.Finale.PointX;
        reaper.PosY = reaper.LastPosY = player.Finale.PointY;
        reaper.ReaperHidden = true;
        player.Finale.Flags |= ReaperCreated;
        context.Emit(SystemName, "reaper_created", $"creature={reaper.Id} player={player.PlayerId}");
        return reaper;
    }

    /// <summary>States 228-234 for every creature in one of them (§3).</summary>
    public static void Run(SimulationContext context)
    {
        IReadOnlyList<CreatureState> creatures = context.World.Creatures;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState reaper = creatures[i];
            if (reaper.Dk2StateId is < VanishState or > WalkState) continue;
            uint age = ++reaper.ReaperStateAgeTicks;
            ref PlayerState player = ref context.World.Player(reaper.OwnerPlayerId);
            switch (reaper.Dk2StateId)
            {
                case VanishState: Vanish(context, ref player, reaper, age); break;
                case WaitCameraState: WaitCamera(context, ref player, reaper, age); break;
                case AppearState: Appear(context, ref player, reaper, age); break;
                case RoarState: Roar(context, reaper, age); break;
                case RouteToGemState: RouteToGem(context, ref player, reaper); break;
                case WalkState: Walk(context, reaper); break;
                case PickUpGemState: PickUpGem(context, ref player, reaper, age); break;
            }
        }
    }

    /// <summary>State 228 (<c>0x00473F80</c>): an existing Reaper vanishes in smoke; from two seconds
    /// the camera is asked for path 102, which does not ship.</summary>
    private static void Vanish(SimulationContext context, ref PlayerState player, CreatureState reaper, uint age)
    {
        int tps = SimulationTiming.OriginalTicksPerGameSecond;
        if ((player.Finale.Flags & ReaperCreated) == 0)
        {
            if (age == 1)
            {
                reaper.ReaperActionId = 0;
                reaper.ActionUntilTick = 0;
                Effect(context, reaper, SmokeEffect);
                reaper.ReaperAlpha = byte.MaxValue;
            }
            else if (age == tps) Effect(context, reaper, BirthTrailEffect, reaper.Heading);
            else if (age > tps) reaper.ReaperAlpha = (byte)Math.Max(0, reaper.ReaperAlpha - 64);
        }
        if (age < 2 * tps) return;
        reaper.ReaperHidden = true;
        StartPath(context, ref player, GemPath, new TilePos(player.Finale.GemTileX, player.Finale.GemTileY));
        Enter(context, reaper, WaitCameraState);
    }

    /// <summary>State 229 (<c>0x00474100</c>): the age is held while a camera path plays; from age 4
    /// the camera glides to the landing tile.</summary>
    private static void WaitCamera(SimulationContext context, ref PlayerState player, CreatureState reaper, uint age)
    {
        if (CameraPathRules.Playing(player, context.Tick))
        {
            reaper.ReaperStateAgeTicks--;
            return;
        }
        if (age < 4) return;
        Glide(context, ref player, new TilePos(player.Finale.PointX >> 12, player.Finale.PointY >> 12));
        Enter(context, reaper, AppearState);
    }

    /// <summary>State 230 (<c>0x004741D0</c>): held while the glide runs; then the Reaper is placed at
    /// the landing point, shown at alpha 0 with its grand entrance, followed by the camera on path
    /// 100, and fades in while action 59 plays.</summary>
    private static void Appear(SimulationContext context, ref PlayerState player, CreatureState reaper, uint age)
    {
        if (player.Finale.CameraGlideEndTick > context.Tick.Value)
        {
            reaper.ReaperStateAgeTicks--;
            return;
        }
        if (age == 1)
        {
            reaper.PosX = reaper.LastPosX = player.Finale.PointX;
            reaper.PosY = reaper.LastPosY = player.Finale.PointY;
            reaper.StopMoving();
            Effect(context, reaper, GrandEntranceEffect);
            StartPath(context, ref player, ArrivalPath, reaper.Tile);
            View(context, ScriptedViewKind.CameraFollow, player.PlayerId, reaper.Id);
            BeginAction(context, reaper, ArrivalAction, ArrivalArt);
            reaper.ReaperHidden = false;
            reaper.ReaperAlpha = 0;
            return;
        }
        if (reaper.ReaperAlpha < byte.MaxValue) reaper.ReaperAlpha = (byte)Math.Min(byte.MaxValue, reaper.ReaperAlpha + 16);
        if (context.Tick.Value < reaper.ActionUntilTick) return;
        reaper.ReaperActionId = 0;
        reaper.ReaperAlpha = byte.MaxValue;
        Enter(context, reaper, RoarState);
    }

    /// <summary>State 231 (<c>0x004743C0</c>): the roar, action 46, then on to the gem.</summary>
    private static void Roar(SimulationContext context, CreatureState reaper, uint age)
    {
        if (age == 1)
        {
            reaper.ReaperActionId = RoarAction;
            reaper.ActionUntilTick = context.Tick.Value + ReaperRules.SummonHoldTicks(context.Database.CreatureById(reaper.CreatureTypeId));
            return;
        }
        if (context.Tick.Value < reaper.ActionUntilTick) return;
        reaper.ReaperActionId = 0;
        Enter(context, reaper, RouteToGemState);
    }

    /// <summary>State 232 (<c>0x00474430</c>): while the gem lives and its position takes a thing,
    /// a route to it; the walk resumes 233 on arrival. A refused request retries next turn.</summary>
    private static void RouteToGem(SimulationContext context, ref PlayerState player, CreatureState reaper)
    {
        ObjectState? gem = context.World.ObjectById(player.Finale.GemObjectId);
        if (gem is null) return;
        var tile = new TilePos(gem.PosX >> 12, gem.PosY >> 12);
        if (!context.World.Contains(tile) || (context.Navigation.NavByte(tile.X, tile.Y) & 8) != 0) return;
        if (!context.Navigation.TryRouteTo(reaper, gem.PosX, gem.PosY, GemRouteOptions)) return;
        Enter(context, reaper, WalkState);
        reaper.UtilityNewState = PickUpGemState;
    }

    /// <summary>State 234 (<c>0x00474F80</c>): on its own tile and the four beside it, a completed
    /// trap is destroyed and a completed door opened: its own when locked is unlocked, another
    /// keeper's has its lock picked. Then the walk; on arrival, 233.</summary>
    private static void Walk(SimulationContext context, CreatureState reaper)
    {
        WorldState world = context.World;
        TilePos at = reaper.Tile;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 && dy != 0) continue;
                var tile = new TilePos(at.X + dx, at.Y + dy);
                if (!world.Contains(tile)) continue;
                foreach (TrapState trap in world.Traps)
                {
                    if (trap.X != tile.X || trap.Y != tile.Y) continue;
                    if (!trap.UnderConstruction) trap.Health = 0;
                    break;
                }
                if (world.DoorAt(tile.X, tile.Y) is not { UnderConstruction: false } door) continue;
                if (door.PlayerId == reaper.OwnerPlayerId)
                {
                    if (door.LockMode != 0) door.LockMode = 0;
                }
                else door.LockPickTicks = (context.Database.Variables.Misc.TryGetValue(
                    VariableTypeId.DoorPickedTimeSeconds, out MiscVariable? seconds) ? seconds.Value : 0)
                    * SimulationTiming.OriginalTicksPerGameSecond;
            }
        DiscoveryRules.SetExplored(world, context.Database, reaper.Tile, reaper.OwnerPlayerId);
        if (reaper.HasRoute) return;
        reaper.UtilityNewState = CreatureStateMachine.Idle;
        Enter(context, reaper, PickUpGemState);
    }

    /// <summary>State 233 (<c>0x004744F0</c>): the pickup. Age 1 faces the camera, stops following,
    /// starts path 101 and action 47 and removes the gem; thirteen seconds after the action began,
    /// effect 258; a turn later the win screen. The Reaper stays here until the player continues.</summary>
    private static void PickUpGem(SimulationContext context, ref PlayerState player, CreatureState reaper, uint age)
    {
        WorldState world = context.World;
        if (age == 1)
        {
            reaper.Heading = 0x400;
            View(context, ScriptedViewKind.CameraFollow, reaper.OwnerPlayerId, 0);
            StartPath(context, ref player, PickupPath, reaper.Tile);
            View(context, ScriptedViewKind.ViewFlag84, reaper.OwnerPlayerId, 0);
            BeginAction(context, reaper, PickupAction, PickupArt);
            player.Finale.PickupTick = context.Tick.Value;
            if (world.ObjectById(player.Finale.GemObjectId) is not null) world.RemoveObject(player.Finale.GemObjectId);
            player.Finale.PickupStage = 0;
            context.Emit(SystemName, "gem_taken", $"player={reaper.OwnerPlayerId} reaper={reaper.Id}");
            return;
        }
        if (player.Finale.PickupStage == 0)
        {
            reaper.ReaperStateAgeTicks--;
            if (context.Tick.Value <= player.Finale.PickupTick + 13 * SimulationTiming.OriginalTicksPerGameSecond) return;
            // The effect stands at the mesh's point 8000 when the renderer has one (0x00441AA0);
            // ours at the Reaper (HYPOTHESIS: the point lies on the Reaper's mesh).
            Effect(context, reaper, EndOfLevelEffect);
            player.Finale.PickupStage = 1;
            return;
        }
        if (player.Finale.PickupStage != 1) return;
        player.Finale.PickupStage = 2;
        View(context, ScriptedViewKind.WinScreen, reaper.OwnerPlayerId, 0);
        context.Emit(SystemName, "win_screen", $"player={reaper.OwnerPlayerId}");
    }

    /// <summary><c>0x004BF9E0</c> (§7): the camera to the heart, fireworks six turns on (fourteen while
    /// possessing), the letterbox, the GUI hidden, and every creature not working a Torture Chamber
    /// dances for a random number of cycles.</summary>
    public static void Celebrate(SimulationContext context, PlayerId playerId)
    {
        WorldState world = context.World;
        if (world.GameMode == GameMode.Multiplayer) return;
        ref PlayerState player = ref world.Player(playerId);
        player.Finale.Flags |= SequenceStarted;
        (ushort hx, ushort hy) = ManaRemovalLifecycle.HeartTile(world, context.Database, playerId);
        Glide(context, ref player, new TilePos(hx, hy));
        player.Finale.FireworksTick = context.Tick.Value + (player.PossessedCreatureId != 0 ? 14 : 6);
        View(context, ScriptedViewKind.Letterbox, playerId, 0, flag: true);
        View(context, ScriptedViewKind.HideGui, playerId, 0);
        foreach (CreatureState creature in world.CreatureOwnershipOrder)
        {
            if (creature.OwnerPlayerId != playerId || !CreatureStateMachine.HasState(creature)) continue;
            if (creature.HeldPlace.IsHeld
                && RoomIndex.RoomAt(world, context.Database, creature.HeldPlace.TileIndex)?.Definition.TypeId == RoomTypeId.TortureChamber)
                continue;
            CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value);
            int cycles = context.Rng.NextInt(24);
            CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
            creature.ReaperActionId = CelebrationAction;
            creature.ActionUntilTick = context.Tick.Value + AnimationTimingRules.CountedHoldTicks(definition, CelebrationArt, cycles);
            // DK2 enters 270 only when the action began (0x004BFB4B); ours takes every begin as taken.
            CreatureStateMachine.TrySetState(context.Hooks, creature, CelebrateState, context.Tick.Value);
        }
        context.Emit(SystemName, "celebration", $"player={playerId}");
    }

    /// <summary><c>Player_Tick</c>'s fireworks (<c>0x004B9EC0</c>): once, at FireworksTime, effect 320
    /// at the heart tile's corner, a tile high.</summary>
    public static void Fireworks(SimulationContext context)
    {
        WorldState world = context.World;
        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(i);
            if (player.Finale.FireworksTick == 0 || context.Tick.Value < player.Finale.FireworksTick) continue;
            player.Finale.FireworksTick = 0;
            (ushort hx, ushort hy) = ManaRemovalLifecycle.HeartTile(world, context.Database, player.PlayerId);
            context.RequestEffect(SystemName, new ViewEffectRequest(new EffectId(FireworksEffect), player.PlayerId,
                new FpPosition3(new Fp32(hx << 12), new Fp32(hy << 12), new Fp32(0x1000))));
        }
    }

    /// <summary>Action 134, <c>Player_ContinueAfterResult</c> <c>0x004C0120</c>: the continuation
    /// flags, Status back to playing, the letterbox forced off, every creature that is not a Reaper,
    /// not in the hand (<c>+0x3E6</c> bit 0) and held by no prison or pit (<c>+0x3CB</c>) kicked to
    /// state 1, and in the campaign the Reaper dismissed.</summary>
    public static void Continue(SimulationContext context, PlayerId playerId)
    {
        WorldState world = context.World;
        ref PlayerState player = ref world.Player(playerId);
        player.Finale.Flags = player.Status == PlayerStatus.Won
            ? player.Finale.Flags | ResultContinued | Continued
            : (player.Finale.Flags & ~ResultContinued) | Continued;
        if (player.Status == PlayerStatus.Conquered) player.Finale.Flags |= ContinuedFromConquered;
        player.Status = PlayerStatus.Playing;
        View(context, ScriptedViewKind.Letterbox, playerId, 2, flag: false);
        CreatureState? reaper = null;
        foreach (CreatureState creature in world.CreatureOwnershipOrder)
        {
            if (creature.OwnerPlayerId != playerId || !CreatureStateMachine.HasState(creature)) continue;
            if (context.Database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsHorny))
            {
                reaper ??= creature;
                continue;
            }
            if (creature.InHand || creature.PrisonOwnerPlayerId != PrisonRules.NoPrison) continue;
            CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value);
        }
        if (world.GameMode == GameMode.Campaign && reaper is not null) ReaperRules.Depart(context, reaper);
        context.Emit(SystemName, "continued", $"player={playerId}");
    }

    /// <summary>Action 75 (<c>0x00505D70</c>): for the local player only, the whole map explored
    /// (<c>0x00451EF0</c> sets DK2's one explored bit; ours that player's layer).</summary>
    public static void RevealMap(WorldState world, PlayerId playerId)
    {
        if (playerId != world.LocalPlayerId) return;
        for (int i = 0; i < world.TileCount; i++) world.TileAtIndex(i).SetExploredBy(playerId);
    }

    /// <summary>The skip (<c>0x00405362</c>) also finishes a timed camera move (camera-paths.md).</summary>
    public static void SkipGlide(ref PlayerState player, GameTick now)
    {
        if (player.Finale.CameraGlideEndTick > now.Value) player.Finale.CameraGlideEndTick = now.Value;
    }

    private static void Enter(SimulationContext context, CreatureState reaper, int state) =>
        CreatureStateMachine.TrySetState(context.Hooks, reaper, state, context.Tick.Value);

    private static void BeginAction(SimulationContext context, CreatureState creature, int action, int art)
    {
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        creature.ReaperActionId = (byte)action;
        creature.ActionUntilTick = context.Tick.Value + AnimationTimingRules.CountedHoldTicks(definition, art, 1);
    }

    private static void Glide(SimulationContext context, ref PlayerState player, TilePos tile)
    {
        player.Finale.CameraGlideEndTick = context.Tick.Value + GlideTicks;
        View(context, ScriptedViewKind.CameraMove, player.PlayerId, 0, tile);
    }

    /// <summary>Action <c>0x73</c>: <c>Camera_StartPath</c>, refused while a path plays, while the
    /// keeper possesses, or when the path does not load.</summary>
    private static void StartPath(SimulationContext context, ref PlayerState player, int pathId, TilePos tile)
    {
        if (!CameraPathRules.TryStart(context.Database, ref player, player.PossessedCreatureId != 0, pathId, context.Tick))
            return;
        context.RequestView(SystemName, new ScriptedViewRequest(
            ScriptedViewKind.CameraPath, player.PlayerId, pathId, tile, false, context.Tick.Value));
    }

    private static void View(SimulationContext context, ScriptedViewKind kind, PlayerId playerId, int value,
        TilePos tile = default, bool flag = false, int value2 = 0) =>
        context.RequestView(SystemName, new ScriptedViewRequest(kind, playerId, value, tile, flag, Value2: value2));

    private static void Effect(SimulationContext context, CreatureState creature, int effect, int heading = 0) =>
        context.RequestEffect(SystemName, new ViewEffectRequest(new EffectId(unchecked((ushort)effect)), creature.OwnerPlayerId,
            new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(0)), new Dk2Angle(unchecked((ushort)heading))));
}
