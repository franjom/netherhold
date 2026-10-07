// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>A gambler's states in its keeper's Casino (<c>casino.md</c> §1–§7, ADR-0396): 145 chooses
/// a table, a barrel alcove, a bare tile or the party, and a walk takes it there; 146 and 148 face
/// the object and start an action; 147, 149 and 151 wait it out and send it back to 145; 150 starts
/// one on a bare tile; 153 dances. The category's update hook runs under all of them
/// (<see cref="Systems.CreatureJobSystem"/>).</summary>
/// <remarks>Ours: a state entered this turn runs from the next, where DK2's dispatcher can run a
/// kicked creature's new handler later in the same turn. Sounds, the wheel's spin and the drunk
/// walk are presentation and not kept.</remarks>
[Dk2Behavior(Confidence.Confirmed, "docs/research/casino.md")]
public static class CasinoStep
{
    /// <summary>145 (<c>0x00456160</c>): the chooser.</summary>
    public const int ChoosingState = 145;

    /// <summary>146 (<c>0x00456B70</c>): at an alcove, facing it, then a drink.</summary>
    public const int AtTheAlcoveState = 146;

    /// <summary>147 (<c>0x00456D60</c>): the drink.</summary>
    public const int DrinkingState = 147;

    /// <summary>148 (<c>0x004567D0</c>): at a table, facing it, then a play.</summary>
    public const int AtTheTableState = 148;

    /// <summary>149 (<c>0x00456980</c>): the play.</summary>
    public const int PlayingState = 149;

    /// <summary>150 (<c>0x00456450</c>): on a bare tile, action 68 once.</summary>
    public const int OnTheFloorState = 150;

    /// <summary>151 (<c>0x00456490</c>): waiting it out.</summary>
    public const int StandingState = 151;

    /// <summary>153 (<c>0x00456E20</c>): dancing at a jackpot party.</summary>
    public const int DancingState = 153;

    /// <summary>The Roulette Table, object 87: a gambler stands on one side or the other of it.</summary>
    public const byte RouletteTableObjectId = 87;

    /// <summary>The Alcove Barrels, object 68, the Casino's wall furniture: what 146 drinks from.</summary>
    public const byte AlcoveObjectId = 68;

    /// <summary>146's drunk chance and length, literals in DK2 (25 %, 45 game seconds): the shipped
    /// <c>DrunkChancePercentagePerDrink</c> and <c>DrunkenDurationSeconds</c> have no reader.</summary>
    public const int DrunkPercent = 25;

    /// <inheritdoc cref="DrunkPercent"/>
    public const int DrunkSeconds = 45;

    /// <summary>146's two thresholds of drunk occupants: above 10, message 89; above 2, message 75.</summary>
    public const int RowdyAbove = 10, MerryAbove = 2;

    /// <summary>The variant 146 posts message 89 with.</summary>
    public const int RowdyVariant = 11;

    /// <summary>154 (<c>0x00456FB0</c>): back into a Casino that changed.</summary>
    public const int ReturningState = 154;

    public static bool IsCasinoState(int state) => state is >= ChoosingState and <= StandingState or DancingState or ReturningState;

    /// <summary>One turn of a gambler. False when it is left idle, which ends the job.</summary>
    public static bool Turn(SimulationContext context, string system, CreatureState creature)
    {
        if (RoomEvictRules.Evict(context, system, creature)) return true;
        if (StateShadow.IsTravelling(creature))
        {
            if (creature.HasRoute) return true;
            creature.StopMoving();
            return StateShadow.Arrive(context, system, creature);
        }

        long age = context.Tick.Value - creature.StateEnteredTick;
        if (age == 0) return true;
        return creature.Dk2StateId switch
        {
            ChoosingState => Choose(context, system, creature, age),
            AtTheAlcoveState => AtTheAlcove(context, system, creature),
            AtTheTableState => AtTheTable(context, system, creature),
            DrinkingState or PlayingState or StandingState => Wait(context, system, creature, age),
            OnTheFloorState => OnTheFloor(context, system, creature),
            DancingState => Dance(context, system, creature, age),
            ReturningState => RoomEvictRules.BackIn(context, system, creature, RoomTypeId.Casino, ChoosingState, endsAction: false),
            _ => false,
        };
    }

    // ----------------------------------------------------------------------------- 145

    /// <summary>145: the party first, then a free table or alcove in a random order, then a bare
    /// tile (casino.md §2).</summary>
    private static bool Choose(SimulationContext context, string system, CreatureState creature, long age)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (age == 1) creature.StopMoving();
        if (OwnCasinoUnder(world, database, creature) is not { } room) return false;

        if (CasinoJackpotRules.IsPartyOn(world, room) && Walk(context, system, creature, creature.PosX, creature.PosY, DancingState))
            return true;
        if (CasinoRules.Lever(world, room) is null) return false;
        creature.CasinoFacing = true;
        creature.CasinoObjectId = CreatureState.NoCasinoObject;
        if (creature.GoldHeld <= CasinoRules.Stake(database) || !creature.HeldPlace.IsHeld) return false;

        bool tableFirst = context.Rng.NextInt(2) != 0;
        for (int pass = 0; pass < 2; pass++)
        {
            bool table = tableFirst == (pass == 0);
            if (Nearest(world, database, room, creature, table ? 0 : 3) is not { } found) continue;
            if (table) ToTable(context, system, creature, room, found);
            else ToAlcove(context, system, creature, room, found);
            return true;
        }

        return FloorTile(context, world, database, room) is int tile
            && Walk(context, system, creature,
                CreatureState.CentreOf(tile % world.Width), CreatureState.CentreOf(tile / world.Width), OnTheFloorState);
    }

    /// <summary><c>0x004D72D0</c> (slots 0–2) and <c>0x004D7460</c> (slots 3–5): the nearest object of
    /// the room's furniture types no other occupant names (casino.md §3). DK2's room object list
    /// breaks a tie; ours takes the first in world order.</summary>
    private static ObjectState? Nearest(
        WorldState world, GameDatabase database, RoomInstance room, CreatureState creature, int firstSlot)
    {
        ObjectState? best = null;
        uint bestDistance = uint.MaxValue;
        foreach (ObjectState thing in world.Objects)
        {
            if (thing.InHand || !EatingRules.Contains(room, thing.TileIndex)) continue;
            if (!IsFurnitureOf(room, thing.ObjectTypeId, firstSlot)) continue;
            (int x, int y) = FurniturePosition(world, database, room, thing);
            uint distance = Distance(creature.PosX - x, creature.PosY - y);
            if (distance >= bestDistance || IsNamedByAnother(world, room, creature, thing.Id)) continue;
            best = thing;
            bestDistance = distance;
        }

        return best;
    }

    private static bool IsFurnitureOf(RoomInstance room, byte type, int firstSlot)
    {
        IReadOnlyList<byte> ids = room.Definition.ObjectIds;
        for (int slot = firstSlot; slot < firstSlot + 3 && slot < ids.Count; slot++)
            if (ids[slot] != 0 && ids[slot] == type) return true;
        return false;
    }

    private static bool IsNamedByAnother(WorldState world, RoomInstance room, CreatureState seeker, int objectId)
    {
        foreach (CreatureState c in world.Creatures)
            if (c.Id != seeker.Id && c.HeldPlace.In(room) && c.CasinoObjectId == objectId) return true;
        return false;
    }

    /// <summary><c>0x00456500</c>: beside a table, its angle either side for a Roulette Table and any
    /// for the rest, a walk resuming 148. It returns 1 whether the walk was taken or not.</summary>
    private static void ToTable(
        SimulationContext context, string system, CreatureState creature, RoomInstance room, ObjectState found)
    {
        WorldState world = context.World;
        if (FirstOnTile(world, room, found.TileIndex, 0, 3) is not { } thing)
        {
            StateShadow.TryMove(context, system, creature, ChoosingState);
            return;
        }

        creature.CasinoObjectId = thing.Id;
        int radius = StandOff(context.Database, creature, thing);
        int angle = thing.ObjectTypeId == RouletteTableObjectId
            ? (context.Rng.NextInt(2) + 1) << 10
            : context.Rng.NextInt(Bearing.Turn);
        StandAt(context, system, creature, room, thing, radius, angle, AtTheTableState);
    }

    /// <summary><c>0x00456690</c>: in front of an alcove, 45° to 135° (casino.md §3), a walk resuming 146.</summary>
    private static void ToAlcove(
        SimulationContext context, string system, CreatureState creature, RoomInstance room, ObjectState found)
    {
        WorldState world = context.World;
        if (FirstOfType(world, found.TileIndex, AlcoveObjectId) is not { } thing)
        {
            StateShadow.TryMove(context, system, creature, ChoosingState);
            return;
        }

        creature.CasinoObjectId = thing.Id;
        int radius = StandOff(context.Database, creature, thing);
        int heading = WallAngle(world, context.Database, room, thing);
        int angle = context.Rng.NextInt(0x200) + ((sbyte)((heading >> 2) & 0xFF) << 9) + 0x100;
        StandAt(context, system, creature, room, thing, radius, angle, AtTheAlcoveState);
    }

    private static void StandAt(
        SimulationContext context, string system, CreatureState creature, RoomInstance room, ObjectState thing,
        int radius, int angle, int resume)
    {
        (int x, int y) = FurniturePosition(context.World, context.Database, room, thing);
        angle &= Bearing.Mask;
        x += (int)(((long)TrapRules.Cosine(angle) * radius) >> 16);
        y += (int)(((long)TrapRules.Sine(angle) * radius) >> 16);
        Walk(context, system, creature, x, y, resume);
    }

    /// <summary>The stand-off: half the object's width and half the creature's footprint, both
    /// words, times 110 / 100.</summary>
    private static int StandOff(GameDatabase database, CreatureState creature, ObjectState thing)
    {
        int width = database.ObjectById(thing.ObjectTypeId) is { } def ? (ushort)def.Width.Raw : 0;
        int footprint = (ushort)database.CreatureById(creature.CreatureTypeId).FootprintRadius.Raw;
        return ((width >> 1) + (footprint >> 1)) * 110 / 100;
    }

    /// <summary><c>0x004E4EA0</c>: a random tile along the slab chain, then on to the first that is
    /// no floor slot, of nav class 0, and holding no Obstacle (casino.md §3).</summary>
    private static int? FloorTile(SimulationContext context, WorldState world, GameDatabase database, RoomInstance room)
    {
        int[] chain = SlabChain.Order(world, room);
        if (chain.Length == 0) return null;
        int start = context.Rng.NextInt(chain.Length);
        for (int k = 0; k <= chain.Length; k++)
        {
            int tile = chain[(start + k) % chain.Length];
            if (AutoFurnitureRules.IsFloorSlot(world, room, tile)) continue;
            if (NavClass(database, world.TileAtIndex(tile)) != 0 || HasObstacle(world, database, tile)) continue;
            return tile;
        }

        return null;
    }

    /// <summary>The nav-cache byte <c>0x00455000</c> reads, as <c>Tile_SetTerrain</c> writes it.</summary>
    internal static int NavClass(GameDatabase database, in TileState tile)
    {
        TerrainFlags flags = database.TerrainById(tile.TerrainId).Flags;
        if (flags.HasFlag(TerrainFlags.Solid)) return 8;
        if (flags.HasFlag(TerrainFlags.Water)) return 1;
        return flags.HasFlag(TerrainFlags.Lava) ? 3 : 0;
    }

    internal static bool HasObstacle(WorldState world, GameDatabase database, int tile)
    {
        foreach (ObjectState thing in world.Objects)
            if (!thing.InHand && thing.TileIndex == tile
                && database.ObjectById(thing.ObjectTypeId) is { } def && def.Flags.HasFlag(ObjectFlags.Obstacle))
                return true;
        return false;
    }

    // ------------------------------------------------------------------------ 146, 148

    /// <summary>146: face the alcove, perhaps get drunk, then a drink of action 39 or 56 (casino.md §4).</summary>
    private static bool AtTheAlcove(SimulationContext context, string system, CreatureState creature)
    {
        if (Facing(context, system, creature) is bool facing) return facing;
        WorldState world = context.World;
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        int action, slot;
        if (context.Rng.NextInt(100) < 75)
        {
            if (context.Rng.NextInt(100) < DrunkPercent && AnimationTimingRules.ResolvesPresent(definition, 34))
                GetDrunk(context, system, creature);
            (action, slot) = (39, 19);
        }
        else (action, slot) = (56, 37);

        if (!AnimationTimingRules.ResolvesPresent(definition, slot)) return false;
        Begin(context, system, creature, definition, action, slot, count: 3);
        return Enter(context, system, creature, DrinkingState);
    }

    /// <summary>146's drunk arm: the timer, then the count of drunk occupants and its message.</summary>
    public static void GetDrunk(SimulationContext context, string system, CreatureState creature)
    {
        WorldState world = context.World;
        creature.DrunkTicks = DrunkSeconds * SimulationTiming.OriginalTicksPerGameSecond;
        int drunk = 0;
        if (creature.HeldPlace.IsHeld && RoomIndex.RoomAt(world, context.Database, creature.HeldPlace.TileIndex) is { } room)
            foreach (CreatureState c in world.Creatures)
                if (c.HeldPlace.In(room) && c.DrunkTicks > 0) drunk++;

        if (drunk > RowdyAbove)
            world.Messages.Post(world, creature.OwnerPlayerId, PlayerMessageKind.CasinoRowdy, RowdyVariant, 0);
        else if (drunk > MerryAbove)
            world.Messages.Post(world, creature.OwnerPlayerId, PlayerMessageKind.CasinoDrinking, 0, 0);
        context.Emit(system, "casino_drunk", $"creature={creature.Id} drunk_here={drunk}");
    }

    /// <summary>148: face the table, then a play whose action the creature's art and the lever's
    /// setting choose (casino.md §5).</summary>
    private static bool AtTheTable(SimulationContext context, string system, CreatureState creature)
    {
        if (Facing(context, system, creature) is bool facing) return facing;
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        bool stingy = CasinoRules.SettingUnder(context.World, context.Database, creature) == CasinoRules.Stingy;
        int action = TableAction(
            AnimationTimingRules.HasArt(definition, 14), AnimationTimingRules.HasArt(definition, stingy ? 9 : 8),
            AnimationTimingRules.HasArt(definition, 20), stingy, () => context.Rng.NextInt(100));
        int slot = action switch { 65 => 14, 66 => 8, 67 => 9, 68 => 20, _ => -1 };
        if (slot < 0) creature.ActionUntilTick = 0;
        else Begin(context, system, creature, definition, action, slot, count: 3);
        return Enter(context, system, creature, PlayingState);
    }

    /// <summary>148's choice (<c>0x004568A0</c>–<c>0x00456920</c>): with the setting's art (<paramref name="b"/>)
    /// and art 20 (<paramref name="d"/>), <c>random(100)</c> picks 65 below 50 when art 14 is there
    /// (<paramref name="a"/>), 66 (Smiley) or 67 ($) below 75, else 68; with art 20 alone, 68; without
    /// art 20, action 0, which ends at once (casino.md §5).</summary>
    public static int TableAction(bool a, bool b, bool d, bool stingy, Func<int> roll)
    {
        if (!d) return 0;
        if (!b) return 68;
        int r = roll();
        return a && r < 50 ? 65 : r < 75 ? (stingy ? 67 : 66) : 68;
    }

    /// <summary>146 and 148's first step: with the facing latch set, the object must still be there
    /// (else back to 145) and the creature turns toward it. Null once it faces it.</summary>
    private static bool? Facing(SimulationContext context, string system, CreatureState creature)
    {
        if (!creature.CasinoFacing) return null;
        WorldState world = context.World;
        if (world.ObjectById(creature.CasinoObjectId) is not { InHand: false } thing
            || RoomIndex.RoomAt(world, context.Database, thing.TileIndex) is not { } room)
        {
            StateShadow.TryMove(context, system, creature, ChoosingState);
            return true;
        }

        (int x, int y) = FurniturePosition(world, context.Database, room, thing);
        if (TurnTo(context, creature, x, y) != 0) return true;
        creature.CasinoFacing = false;
        return null;
    }

    // ------------------------------------------------------------------- 147, 149, 150, 151

    /// <summary>147, 149 and 151: moved since the last turn, the action ends; while it runs, hold;
    /// then 145. 149 draws the wheel's <c>random(2)</c> every fourth second at a Roulette Table.</summary>
    private static bool Wait(SimulationContext context, string system, CreatureState creature, long age)
    {
        long now = context.Tick.Value;
        bool moved = creature.WaitPosX != creature.PosX || creature.WaitPosY != creature.PosY;
        if (age > 1 && moved && now < creature.ActionUntilTick) creature.ActionUntilTick = 0;
        creature.WaitPosX = creature.PosX;
        creature.WaitPosY = creature.PosY;
        if (now >= creature.ActionUntilTick) return Enter(context, system, creature, ChoosingState);

        if (creature.Dk2StateId == PlayingState && age % (4 * SimulationTiming.OriginalTicksPerGameSecond) == 0
            && context.World.ObjectById(creature.CasinoObjectId) is { InHand: false, ObjectTypeId: RouletteTableObjectId })
            context.Rng.NextInt(2);
        return true;
    }

    /// <summary>150: action 68 once, then 151.</summary>
    private static bool OnTheFloor(SimulationContext context, string system, CreatureState creature)
    {
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        Begin(context, system, creature, definition, 68, 20, count: 1);
        return Enter(context, system, creature, StandingState);
    }

    // ----------------------------------------------------------------------------- 153

    /// <summary>153: at its first turn the dance, action 57 or 35 sixty-four times over; when it
    /// ends, 1 and then 145 (casino.md §9).</summary>
    private static bool Dance(SimulationContext context, string system, CreatureState creature, long age)
    {
        CreatureDef definition = context.Database.CreatureById(creature.CreatureTypeId);
        if (age == 1)
        {
            creature.ActionUntilTick = 0;
            if (AnimationTimingRules.ResolvesPresent(definition, 28)) Begin(context, system, creature, definition, 57, 28, count: 64);
            else if (AnimationTimingRules.ResolvesPresent(definition, 8)) Begin(context, system, creature, definition, 35, 8, count: 64);
            else return false;
            return true;
        }

        if (context.Tick.Value < creature.ActionUntilTick) return true;
        CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value);
        return Enter(context, system, creature, ChoosingState);
    }

    // --------------------------------------------------------------------------- shared

    /// <summary>The Casino under the creature, if it is its own keeper's (<c>0x004539B0(15, owner, 0)</c>).</summary>
    internal static RoomInstance? OwnCasinoUnder(WorldState world, GameDatabase database, CreatureState creature)
    {
        if (!world.Contains(creature.Tile)) return null;
        RoomInstance? room = RoomIndex.RoomAt(world, database, (creature.Tile.Y * world.Width) + creature.Tile.X);
        return room is not null && room.Definition.TypeId == RoomTypeId.Casino && room.OwnerPlayerId == creature.OwnerPlayerId
            ? room : null;
    }

    /// <summary>An object's position, its <c>+0x1A</c> (<see cref="AutoFurnitureRules.FurniturePosition"/>).</summary>
    public static (int X, int Y) FurniturePosition(WorldState world, GameDatabase database, RoomInstance room, ObjectState thing) =>
        AutoFurnitureRules.FurniturePosition(world, database, room, thing);

    /// <summary>The angle the wall pass gives wall furniture, <c>((side + 2) &amp; 3) &lt;&lt; 9</c>.</summary>
    private static int WallAngle(WorldState world, GameDatabase database, RoomInstance room, ObjectState thing)
    {
        int side = AutoFurnitureRules.WallSide(world, database, room, thing.TileIndex);
        return side < 0 ? 0 : ((side + 2) & 3) << 9;
    }

    private static ObjectState? FirstOnTile(WorldState world, RoomInstance room, int tile, int firstSlot, int count)
    {
        IReadOnlyList<byte> ids = room.Definition.ObjectIds;
        for (int slot = firstSlot; slot < firstSlot + count && slot < ids.Count; slot++)
            if (ids[slot] != 0 && FirstOfType(world, tile, ids[slot]) is { } thing) return thing;
        return null;
    }

    private static ObjectState? FirstOfType(WorldState world, int tile, byte type)
    {
        foreach (ObjectState thing in world.Objects)
            if (!thing.InHand && thing.TileIndex == tile && thing.ObjectTypeId == type) return thing;
        return null;
    }

    private static uint Distance(int dx, int dy)
    {
        long ax = Math.Abs((long)dx), ay = Math.Abs((long)dy);
        return unchecked((uint)((ax * ax) >> 12) + (uint)((ay * ay) >> 12));
    }

    /// <summary>The turn step <c>0x004B2AE0</c> toward a point: the angle still to turn.</summary>
    internal static int TurnTo(SimulationContext context, CreatureState creature, int x, int y)
    {
        int heading = creature.Heading;
        int rate = context.Database.CreatureById(creature.CreatureTypeId).TurnRate;
        int left = Bearing.TurnToward(ref heading, Bearing.Of(x - creature.PosX, y - creature.PosY), rate);
        creature.Heading = heading;
        return left;
    }

    /// <summary>A walk to a point resuming <paramref name="resume"/>, options <c>0x80</c>.</summary>
    private static bool Walk(SimulationContext context, string system, CreatureState creature, int x, int y, int resume)
    {
        if (!context.Navigation.TryRouteTo(creature, x, y, MoveOptions.Loose)) return false;
        CreatureStateMachine.BeginTravel(context.Hooks, creature, StateShadow.TravelToPoint, resume);
        context.Emit(system, "casino_walk", $"creature={creature.Id} to=({x >> 12},{y >> 12}) resume={resume}");
        return true;
    }

    /// <summary><c>0x004969D0</c>: the action and how long it holds.</summary>
    private static void Begin(
        SimulationContext context, string system, CreatureState creature, CreatureDef definition, int action, int slot, int count)
    {
        creature.ActionUntilTick = context.Tick.Value + AnimationTimingRules.CountedHoldTicks(definition, slot, count);
        context.Emit(system, "casino_action", $"creature={creature.Id} action={action} until={creature.ActionUntilTick}");
    }

    /// <summary>SetState, stamped. False when the creature is left idle.</summary>
    private static bool Enter(SimulationContext context, string system, CreatureState creature, int state)
    {
        StateShadow.TryMove(context, system, creature, state);
        return creature.Dk2StateId != CreatureStateMachine.Idle;
    }
}
