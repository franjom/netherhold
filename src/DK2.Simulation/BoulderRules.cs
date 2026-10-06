// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Steering;

namespace DK2.Simulation;

/// <summary>The Boulder (object 74) — docs/research/boulder.md: made by the Boulder trap's shot
/// (process 22, <c>0x004AEBD0</c>) and updated as object process state 17 (<c>0x004A1390</c>): it
/// strikes what it overlaps, rolls along the tile grid turning at blocked tiles, wears on water,
/// lava and sunken floors, frightens enemies around it, and breaks when its wear reaches its health.
/// Not built: a shot striking it, a slap, its heading, and the effects and sounds.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/boulder.md")]
public static class BoulderRules
{
    private const string SystemName = "boulders";

    /// <summary>OBJECTS record 74, and its process state (<c>def+0x11B</c>, table <c>0x006E4B38</c> entry 17).</summary>
    public const byte ObjectId = 74;
    public const byte ProcessState = 17;

    /// <summary>One whole tile along an axis, the length the roll's direction is kept at.</summary>
    private const int Unit = 0x1000;

    /// <summary>A position at a tile's centre on an axis.</summary>
    private const int Centre = 0x800;

    /// <summary>The states the strike passes over: 76, the drop's landing, and 256, the stun it gives.</summary>
    private const int LandingState = 76;
    public const int StunnedState = 256;

    /// <summary>The state group (<c>0x006DA8C6</c>) whose creature is struck without being knocked down.</summary>
    private const byte UnknockedGroup = 1;

    // ------------------------------------------------------------------ the launch

    /// <summary>Process 22 (<c>0x004AEBD0</c>), on the shot's first tick: on a passable spot, a boulder
    /// of the shot's owner at the shot; the trap on that tile destroyed; health
    /// <c>BoulderInitialHealth</c>, no wear, speed <c>BoulderSpeedTilesPerSecond</c> over ticks a second,
    /// and the direction toward the target's tile from the shot's, a whole tile long, with its signs.</summary>
    public static void Launch(SimulationContext context, ShotState shot)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (!ShotRules.Passable(world, context.Navigation, shot.PosX, shot.PosY)) return;
        int tx = shot.PosX >> 12, ty = shot.PosY >> 12;
        if (database.ObjectById(ObjectId) is not { } definition) return;
        ObjectState boulder = world.SpawnObject(database, ObjectId, shot.OwnerPlayerId, (ty * world.Width) + tx);
        boulder.ProcessState = definition.StartState;
        boulder.PosX = shot.PosX;
        boulder.PosY = shot.PosY;

        foreach (TrapState trap in world.Traps)
        {
            if (trap.X != tx || trap.Y != ty) continue;
            trap.Health = 0;
            break;
        }

        boulder.Health = database[VariableTypeId.BoulderInitialHealth];
        boulder.Wear = 0;
        boulder.RollSpeed = (int)((uint)database[VariableTypeId.BoulderSpeedTilesPerSecond]
            / (uint)SimulationTiming.OriginalTicksPerGameSecond);
        int heading = Bearing.Of((shot.TargetX >> 12) - tx, (shot.TargetY >> 12) - ty) & Bearing.Mask;
        boulder.RollX = (int)(((long)TrapRules.Sine(heading) * Unit) >> 16);
        boulder.RollY = -(int)(((long)TrapRules.Cosine(heading) * Unit) >> 16);
        boulder.RollSignY = (sbyte)Math.Sign(boulder.RollY);
        boulder.RollSignX = (sbyte)Math.Sign(boulder.RollX);
        context.Emit(SystemName, "boulder_launched",
            $"shot={shot.Id} boulder={boulder.Id} player={shot.OwnerPlayerId} at={tx},{ty} roll={boulder.RollX},{boulder.RollY} speed={boulder.RollSpeed}");
    }

    // ------------------------------------------------------------------ the update

    /// <summary>State 17 (<c>0x004A1390</c>): the strike, the roll, the wear, then the break when wear
    /// has reached health. True when the boulder is gone; <paramref name="destroyed"/> collects the
    /// objects it broke, for the caller to remove.</summary>
    public static bool Update(SimulationContext context, ISightBlocker sight, ObjectState boulder, List<ObjectState> destroyed)
    {
        Strike(context, boulder, destroyed);
        if (!Roll(context, sight, boulder)) return true;
        Wear(context, boulder);
        if (boulder.Health > boulder.Wear) return false;
        context.Emit(SystemName, "boulder_broken", $"boulder={boulder.Id} at={boulder.PosX >> 12},{boulder.PosY >> 12} wear={boulder.Wear}");
        return true;
    }

    // ------------------------------------------------------------------ the strike

    /// <summary><c>0x004A0930</c>: on the nine tiles of table <c>0x006BC3B0</c> (N, NE, E, SE, S, SW, W,
    /// NW, then its own), each thing it overlaps by half its width plus half the other's — a creature
    /// by its footprint, a trap or a door by the boulder's half width alone — takes damage capped by
    /// the boulder's health left, and the boulder wears by what landed. Ours orders a tile's things as
    /// creatures newest-linked first, then objects newest first, the heart, the trap and the door.</summary>
    private static void Strike(SimulationContext context, ObjectState boulder, List<ObjectState> destroyed)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        ObjectDef definition = database.ObjectById(ObjectId)!;
        int half = (ushort)definition.Width.Raw >> 1;
        uint ownReach = (uint)(half * half) >> 12;
        int bx = boulder.PosX >> 12, by = boulder.PosY >> 12;
        List<CreatureState> onTile = [];

        foreach ((int ox, int oy) in StrikeOffsets)
        {
            int x = bx + ox, y = by + oy;
            if (!world.Contains(x, y)) continue;
            int tile = (y * world.Width) + x;

            onTile.Clear();
            foreach (CreatureState c in world.Creatures)
                if (c.Tile.X == x && c.Tile.Y == y && !CreatureDeathRules.IsCorpse(c) && CombatPitRules.IsOnTheMap(c)) onTile.Add(c);
            onTile.Sort((a, b) => b.ChainOrder.CompareTo(a.ChainOrder));
            foreach (CreatureState creature in onTile) StrikeCreature(context, boulder, half, creature);

            for (int i = world.Objects.Count - 1; i >= 0; i--)
            {
                ObjectState other = world.Objects[i];
                if (other == boulder || other.TileIndex != tile || other.InHand || other.CarriedByCreatureId >= 0 || destroyed.Contains(other)) continue;
                if (database.ObjectById(other.ObjectTypeId) is not { } otherDef) continue;
                int reach = half + ((ushort)otherDef.Width.Raw >> 1);
                (int px, int py) = other.PosX != 0 || other.PosY != 0
                    ? (other.PosX, other.PosY) : (CreatureState.CentreOf(x), CreatureState.CentreOf(y));
                if (TrapRules.DistanceSquared(boulder.PosX, boulder.PosY, px, py) >= (uint)(reach * reach) >> 12) continue;
                if (!otherDef.Flags.HasFlag(ObjectFlags.BoulderDestroys)) continue;
                int damage = Capped(boulder, otherDef.Hp);
                int landed;
                if (damage < other.Health)
                {
                    other.Health -= damage;
                    landed = damage;
                }
                else
                {
                    landed = other.Health;
                    destroyed.Add(other);
                }
                boulder.Wear += landed;
                context.Emit(SystemName, "boulder_struck_object",
                    $"boulder={boulder.Id} object={other.Id} type={other.ObjectTypeId} damage={landed} wear={boulder.Wear}{(destroyed.Contains(other) ? " destroyed" : "")}");
            }

            StrikeHeart(context, boulder, half, x, y);

            foreach (TrapState trap in world.Traps)
            {
                if (trap.X != x || trap.Y != y || trap.UnderConstruction || TrapRules.IsDead(trap)) continue; // DK2 destroyed it already
                if (!database.Traps.TryGetValue(trap.TrapId, out TrapDef? trapDef)) continue;
                if (trapDef.TriggerType == TrapRules.TriggerSelector && trap.PlayerId == boulder.OwnerPlayerId) continue;
                if (TrapRules.DistanceSquared(boulder.PosX, boulder.PosY, trap.PosX, trap.PosY) >= ownReach) continue;
                TrapRules.AttemptAt(context, trap, trapDef, boulder.PosX, boulder.PosY);
                int damage = Capped(boulder, trapDef.Health >> 2);
                int landed = damage < trap.Health ? damage : trap.Health;
                TrapRules.Damage(trap, -landed);
                boulder.Wear += landed;
                context.Emit(SystemName, "boulder_struck_trap", $"boulder={boulder.Id} trap={trap.Id} damage={landed} health={trap.Health} wear={boulder.Wear}");
            }

            if (world.DoorAt(x, y) is { } door && !door.UnderConstruction
                && TrapRules.DistanceSquared(boulder.PosX, boulder.PosY, CreatureState.CentreOf(x), CreatureState.CentreOf(y)) < ownReach
                && database.Doors.TryGetValue(door.DoorId, out DoorDef? doorDef))
            {
                int damage = Capped(boulder, doorDef.Health);
                int landed = damage < door.Health ? damage : door.Health;
                DoorRules.Damage(context, door, -landed, boulder.OwnerPlayerId);
                boulder.Wear += landed;
                context.Emit(SystemName, "boulder_struck_door", $"boulder={boulder.Id} door={door.Id} damage={landed} health={door.Health} wear={boulder.Wear}");
            }
        }
    }

    /// <summary>A creature (class 0): not landing, already stunned by it, or dying; within half the
    /// boulder's width plus half its footprint. It takes its level-scaled <c>Hp</c> halved and over
    /// <c>BoulderToCreatureDamagePercentage</c>, capped, through the health routine; still standing,
    /// it is knocked into 256 unless its state's group is 1 or it is possessed.</summary>
    private static void StrikeCreature(SimulationContext context, ObjectState boulder, int half, CreatureState creature)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (creature.Dk2StateId is LandingState or StunnedState || CreatureDeathRules.IsDown(creature)) return;
        CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
        int reach = half + ((ushort)definition.FootprintRadius.Raw >> 1);
        if (TrapRules.DistanceSquared(boulder.PosX, boulder.PosY, creature.PosX, creature.PosY) >= (uint)(reach * reach) >> 12) return;

        uint maximum = (uint)CreatureLevelRules.MaxHealth(database, definition, creature.Level);
        uint percent = (uint)(byte)database[VariableTypeId.BoulderToCreatureDamagePercentage];
        int damage = Capped(boulder, (int)(((maximum >> 1) * 100) / percent));
        int before = creature.Health;
        StatusEffectRules.Hurt(world, database, definition, creature, damage);
        int landed = before - creature.Health;
        bool knocked = false;
        if (!CreatureDeathRules.IsDown(creature)
            && CreatureStateTable.Get(creature.Dk2StateId).Group != UnknockedGroup
            && !CreatureDeathRules.IsPossessed(world, creature))
            knocked = StatusEffectRules.StunInto(context, creature, definition, StunnedState, "boulder");
        boulder.Wear += landed;
        context.Emit(SystemName, "boulder_struck_creature",
            $"boulder={boulder.Id} creature={creature.Id} damage={damage} landed={landed} health={creature.Health} wear={boulder.Wear}{(knocked ? " knocked" : "")}");
    }

    /// <summary>The heart object (13, <c>BoulderDestroys</c>) at a heart's centre tile, which ours holds
    /// as its owner's heart health rather than an object: the object arm's damage through
    /// <see cref="WorldState.DamageHeart"/>.</summary>
    private static void StrikeHeart(SimulationContext context, ObjectState boulder, int half, int x, int y)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        PlayerId owner = world.Tile(x, y).OwnerPlayerId;
        if (!world.HasPlayer(owner) || !DisposalRules.IsHeartCentreTile(world, database, new TilePos(x, y), owner)) return;
        if (database.ObjectById(Navigation.WorldSteering.DungeonHeartObjectId) is not { } heartDef) return;
        if (!heartDef.Flags.HasFlag(ObjectFlags.BoulderDestroys)) return;
        ref PlayerState player = ref world.Player(owner);
        if (player.HeartHealth <= 0) return;
        int reach = half + ((ushort)heartDef.Width.Raw >> 1);
        if (TrapRules.DistanceSquared(boulder.PosX, boulder.PosY, CreatureState.CentreOf(x), CreatureState.CentreOf(y)) >= (uint)(reach * reach) >> 12) return;
        int damage = Capped(boulder, heartDef.Hp);
        int landed = damage < player.HeartHealth ? damage : player.HeartHealth;
        bool killed = world.DamageHeart(owner, landed, boulder.OwnerPlayerId);
        CombatMessageRules.HeartDamaged(context, owner);
        boulder.Wear += landed;
        context.Emit(SystemName, "boulder_struck_heart",
            $"boulder={boulder.Id} heart={owner} damage={landed} health={world.Player(owner).HeartHealth} wear={boulder.Wear}{(killed ? " destroyed" : "")}");
    }

    /// <summary>The damage capped by the health the boulder has left, as DK2's unsigned compare.</summary>
    private static int Capped(ObjectState boulder, int damage)
    {
        uint left = unchecked((uint)(boulder.Health - boulder.Wear));
        return (int)Math.Min(left, unchecked((uint)damage));
    }

    /// <summary>Table <c>0x006BC3B0</c>: the eight neighbours from the north clockwise, then the tile itself.</summary>
    private static readonly (int X, int Y)[] StrikeOffsets =
        [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, 0)];

    // ------------------------------------------------------------------ the roll

    /// <summary><c>0x004A0E30</c>: the speed falls with wear, <c>s·r/100·(h−w)/h + (100−r)·s·5/100</c> with
    /// <c>r</c> <c>BoulderSpeedToHealthRatio</c>; at a tile centre the handler for its direction may turn
    /// it; then it moves, stopping at each centre line it crosses to ask that line's handler, and a turn
    /// that leaves it whole ends the tick's move. Then the fear: <c>BoulderFearAmount</c> scaled by health
    /// over the definition's, within <c>BoulderFearRange</c> tiles. False when it has no health and is gone.</summary>
    private static bool Roll(SimulationContext context, ISightBlocker sight, ObjectState boulder)
    {
        if (boulder.Health == 0) return false;
        GameDatabase database = context.Database;
        int s = boulder.RollSpeed;
        int r = (byte)database[VariableTypeId.BoulderSpeedToHealthRatio];
        uint scaled = unchecked((uint)(s * r)) / 100u;
        int speed = unchecked((int)scaled * (boulder.Health - boulder.Wear)) / boulder.Health
            + (int)(unchecked((uint)((100 - r) * s * 5)) / 100u);

        int startX = boulder.PosX, startY = boulder.PosY;
        if ((startY & 0xFFF) == Centre)
        {
            if (boulder.RollY < 0) Turn(context, boulder, Direction.North);
            else if (boulder.RollY > 0) Turn(context, boulder, Direction.South);
        }
        if ((startX & 0xFFF) == Centre)
        {
            if (boulder.RollX < 0) Turn(context, boulder, Direction.West);
            else if (boulder.RollX > 0) Turn(context, boulder, Direction.East);
        }

        int dx = unchecked(speed * boulder.RollX) >> 12, dy = unchecked(speed * boulder.RollY) >> 12;
        while (dx != 0 || dy != 0)
        {
            if (!Step(context.World, boulder, ref dx, ref dy, out Direction crossed)) continue;
            if (!Turn(context, boulder, crossed)) continue;
            if (boulder.Wear >= boulder.Health) continue;
            break;
        }

        ObjectDef definition = database.ObjectById(ObjectId)!;
        int amount = database[VariableTypeId.BoulderFearAmount] & 0xFFFF;
        int threat = boulder.Health != 0 && definition.Hp != 0 ? amount * boulder.Health / definition.Hp : amount;
        int radius = (database[VariableTypeId.BoulderFearRange] & 0xFFFF) << 12;
        TrapRules.ShareThreat(context.World, sight, boulder.OwnerPlayerId, boulder.PosX, boulder.PosY, threat, radius);
        return true;
    }

    /// <summary>The direction a centre-line crossing reports, the jump table at <c>0x004A10C8</c>.</summary>
    private enum Direction { North = 0, East = 1, South = 2, West = 3 }

    /// <summary><c>0x004A0050</c>: move by what is left of the tick's step, but stop on the next tile
    /// centre line ahead on either axis, the later axis winning; what was not travelled stays in
    /// <paramref name="dx"/>, <paramref name="dy"/>. True with the line's direction when it stopped.</summary>
    private static bool Step(WorldState world, ObjectState boulder, ref int dx, ref int dy, out Direction crossed)
    {
        int x = boulder.PosX, y = boulder.PosY;
        int nx = x + dx, ny = y + dy;
        bool stopped = false;
        crossed = Direction.North;

        int cx = (int)((unchecked((uint)(dx >= 0 ? x + Centre : x - Centre)) & 0xFFFFF000u) + Centre);
        if (Math.Sign(x - cx) != Math.Sign(nx - cx) && cx != x)
        {
            stopped = true;
            ny = ((cx - x) * dy / dx) + y;
            crossed = dx >= 0 ? Direction.East : Direction.West;
            nx = cx;
        }
        int cy = (int)((unchecked((uint)(dy >= 0 ? y + Centre : y - Centre)) & 0xFFFFF000u) + Centre);
        if (Math.Sign(y - cy) != Math.Sign(ny - cy) && cy != y)
        {
            stopped = true;
            nx = ((cy - y) * dx / dy) + x;
            crossed = dy >= 0 ? Direction.South : Direction.North;
            ny = cy;
        }

        dx -= nx - x;
        dy -= ny - y;
        boulder.PosX = nx;
        boulder.PosY = ny;
        int tx = nx >> 12, ty = ny >> 12;
        if (world.Contains(tx, ty)) boulder.TileIndex = (ty * world.Width) + tx;
        return stopped;
    }

    /// <summary>The four handlers (<c>0x004A05B0</c> N, <c>0x004A03E0</c> E, <c>0x004A0770</c> S,
    /// <c>0x004A0210</c> W): the tile ahead free, no turn. Blocked, the two sides in a fixed order (west
    /// then east going north or south, north then south going east or west): both blocked, it turns
    /// back; one free, it turns that way; both free, the launch's sign on that axis or a coin. A turn
    /// wears it by a tenth of its health. True when it turned.</summary>
    private static bool Turn(SimulationContext context, ObjectState boulder, Direction going)
    {
        WorldState world = context.World;
        int x = boulder.PosX >> 12, y = boulder.PosY >> 12;
        bool vertical = going is Direction.North or Direction.South;
        int ahead = going switch { Direction.North => -1, Direction.South => 1, Direction.West => -1, _ => 1 };
        if (!(vertical ? Blocked(context.World, context.Database, x, y + ahead) : Blocked(context.World, context.Database, x + ahead, y))) return false;

        bool lowBlocked = vertical ? Blocked(context.World, context.Database, x - 1, y) : Blocked(context.World, context.Database, x, y - 1);
        bool highBlocked = vertical ? Blocked(context.World, context.Database, x + 1, y) : Blocked(context.World, context.Database, x, y + 1);
        int across;
        if (lowBlocked && highBlocked)
        {
            if (vertical) boulder.RollY = -ahead * Unit;
            else boulder.RollX = -ahead * Unit;
            across = int.MinValue;
        }
        else if (lowBlocked) across = Unit;
        else if (highBlocked) across = -Unit;
        else
        {
            sbyte sign = vertical ? boulder.RollSignX : boulder.RollSignY;
            across = sign != 0 ? sign << 12 : (context.Rng.NextInt(2) * 0x2000) - Unit;
        }
        if (across != int.MinValue)
        {
            if (vertical)
            {
                boulder.RollX = across;
                boulder.RollY = 0;
            }
            else
            {
                boulder.RollY = across;
                boulder.RollX = 0;
            }
        }
        boulder.Wear += boulder.Health / 10;
        context.Emit(SystemName, "boulder_turned", $"boulder={boulder.Id} at={x},{y} roll={boulder.RollX},{boulder.RollY} wear={boulder.Wear}");
        return true;
    }

    /// <summary><c>0x00453C10</c>, what stops a boulder: off the map, solid terrain, a Prison's interior,
    /// a 2×2 Hero Gate, or an object on the tile without <c>BoulderCanRollThrough</c>.</summary>
    public static bool Blocked(WorldState world, GameDatabase database, int x, int y)
    {
        if (!world.Contains(x, y)) return true;
        if (database.TerrainById(world.Tile(x, y).TerrainId).IsSolid) return true;
        int index = (y * world.Width) + x;
        if (RoomIndex.RoomAt(world, database, index) is { } room)
        {
            if (room.Definition.TypeId == RoomTypeId.Prison && room.IsInterior(index)) return true;
            if (room.Definition.TypeId == RoomTypeId.HeroGate2By2) return true;
        }
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.TileIndex != index || thing.InHand || thing.CarriedByCreatureId >= 0) continue;
            if (database.ObjectById(thing.ObjectTypeId) is { } def && !def.Flags.HasFlag(ObjectFlags.BoulderCanRollThrough)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ the wear

    /// <summary><c>0x004A10E0</c> on the tile under it: lava wears a sixth of its health, water a tenth,
    /// and a sunken floor of a Portal, Temple or Combat Pit (floor height under a tile, <c>0x004545C0</c>:
    /// the Portal's hole, a Temple's or a Combat Pit's interior) a third, never past its health.</summary>
    private static void Wear(SimulationContext context, ObjectState boulder)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int x = boulder.PosX >> 12, y = boulder.PosY >> 12;
        if (!world.Contains(x, y)) return;
        TerrainDef terrain = database.TerrainById(world.Tile(x, y).TerrainId);
        uint health = unchecked((uint)boulder.Health);
        uint share;
        if (terrain.IsLava) share = health / 6;
        else if (terrain.IsWater) share = health / 10;
        else if (Sunken(world, database, x, y)) share = health / 3;
        else return;
        uint worn = unchecked((uint)boulder.Wear + share);
        boulder.Wear = (int)(health < worn ? health : worn);
        context.Emit(SystemName, "boulder_worn", $"boulder={boulder.Id} at={x},{y} wear={boulder.Wear}");
    }

    /// <summary>Whether the floor height (<c>0x004545C0</c>) is below a whole tile on a room of type 3,
    /// 13 or 16: the Portal's centre piece, or a Temple's or Combat Pit's interior.</summary>
    private static bool Sunken(WorldState world, GameDatabase database, int x, int y)
    {
        int index = (y * world.Width) + x;
        if (RoomIndex.RoomAt(world, database, index) is not { } room) return false;
        return room.Definition.TypeId switch
        {
            RoomTypeId.Portal => room.Width >= 3 && room.Height >= 3 && PortalRules.EntranceTileIndex(world, room) == index,
            RoomTypeId.Temple or RoomTypeId.CombatPit => room.IsInterior(index),
            _ => false,
        };
    }
}
