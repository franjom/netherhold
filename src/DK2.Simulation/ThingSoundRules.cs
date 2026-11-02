// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The kinds of thing that request world sounds; each owns its own range of voices.</summary>
public enum SoundSourceKind : byte { Creature, Object, Door, Trap, Shot, Room, Terrain, Player }

/// <summary>The thing a world sound belongs to, so that its next request replaces its last one.</summary>
public readonly record struct SoundSourceId(SoundSourceKind Kind, int Value)
{
    public static SoundSourceId Of(CreatureId creature) => new(SoundSourceKind.Creature, creature.Value);
}

/// <summary>An event number as DK2 passes it to the sound manager: an SFX.map group in the
/// requester's category.</summary>
public enum ThingSoundEvent : ushort
{
    PickedUp = 0xC3, Dropped = 0xC4, BattleCall = 0xD6, Sacked = 0xDF,
    /// <summary>A tile worked down to a different terrain (<c>Tile_ApplyWork</c>, <c>0x0045327C</c>).</summary>
    TileBroken = 0xF4,
    /// <summary>A tile worked up to its max-health terrain (<c>Tile_ApplyWork</c>, <c>0x00453359</c>).</summary>
    TileConverted = 0x10C,
    /// <summary>The imp's claim finished on a tile with no room link (<c>Imp_ClaimAction</c>, <c>0x00497635</c>).</summary>
    ImpClaimedFloor = 0x10A,
    /// <summary>The same, on a tile that still has one.</summary>
    ImpClaimedRoomTile = 0x10B,
    /// <summary>A room tile built (<c>Player_BuildRoomTile</c>, <c>0x004C5D87</c>).</summary>
    RoomTileBuilt = 0xF1,
    /// <summary>A room tile sold for a refund (<c>Player_SellRoomTile</c>, <c>0x004C5EF8</c>).</summary>
    RoomTileSold = 0xF5,
    /// <summary>A room rectangle accepted at its corner (the build command, <c>0x00442D57</c>).</summary>
    RoomBuildAccepted = 0x2F8,
    /// <summary>The local player's Heart opens (<c>DungeonHeart_Update</c>, <c>0x004D9E00</c>); the same
    /// group as <see cref="RoomTileBuilt"/>, in the heart's category.</summary>
    HeartOpened = 0xF1,
    /// <summary>A blow lands on a heart (<c>Player_ModifyHeartHealth</c>, <c>0x004DA2DB</c>).</summary>
    HeartDamaged = 0x329,
    /// <summary>A heart's health reaches zero (<c>Heart_OnDestroyed</c>, <c>0x004DA5B2</c>).</summary>
    HeartDestroyed = 0xDA,
    /// <summary>The fallen keeper's heart tile, on the turn its consequences run (<c>0x004DAC38</c>); the
    /// same group as <see cref="TileBroken"/>, in the heart's category.</summary>
    HeartCollapsed = 0xF4,
    /// <summary>An imp's gold put into a room (<c>ImpState29_DepositGold</c>, <c>0x00466B95</c>); the same
    /// group as <see cref="Dropped"/>, in the gold object's category.</summary>
    GoldDeposited = 0xC4,
    /// <summary>A creature holds a thing in its arms (<c>Creature_UpdateCarriedThing</c>, <c>0x0048B687</c>;
    /// the burial state <c>0x00467B37</c>).</summary>
    CarriedThing = 0x10D,
    /// <summary>An imp shelves a spell book (state 31, <c>0x00466DC7</c>) or stores a crate (state 32,
    /// <c>0x00466F0B</c>).</summary>
    ImpStoredItem = 0x326,
    /// <summary>An imp installs a door (state 37, <c>0x00467575</c>).</summary>
    ImpInstalledDoor = 0x327,
    /// <summary>An imp installs a trap (state 35, <c>0x0046729F</c>).</summary>
    ImpInstalledTrap = 0x328,
    /// <summary>A creature of the local keeper finishes coming out of its portal (state 193,
    /// <c>CreatureState_JoiningDungeon</c>, <c>0x00459C73</c>).</summary>
    JoinedDungeon = 0xE0,
    /// <summary>A creature is knocked down (the tremor starter <c>0x00481520</c>, state 210).</summary>
    KnockedDown = 0xC7,
    /// <summary>A creature runs from a fight (<c>Fight_WithdrawState</c> <c>0x004617E6</c>, the Scout's
    /// <c>Combat_RaiseAlarm</c> <c>0x004754AC</c>).</summary>
    Withdrew = 0xDC,
    /// <summary>The Scout at the alarm's room dispatches its responders (<c>State251_RespondToThreat</c>,
    /// <c>0x00475934</c>).</summary>
    RespondedToThreat = 0x2FA,
    /// <summary>A guard's patrol leg cannot be routed (state 98, <c>0x004630FF</c>).</summary>
    GuardLostItsWay = 0xDD,
    /// <summary>A chicken is eaten, in the chicken's own category (<c>Eat_EatChicken</c>, <c>0x00463859</c>).</summary>
    ChickenEaten = 0x111,
    /// <summary>A trainee begins its melee swing at the dummy (<c>Training_State111</c>, <c>0x004857CD</c>).</summary>
    Trained = 0xD3,
    /// <summary>A creature is offered in the Temple's pool (state 101, <c>0x004729FB</c>).</summary>
    Sacrificed = 0xE2,
    /// <summary>A worshipper begins its prayer (state 104, <c>0x00472E70</c>).</summary>
    Prayed = 0xD4,
    /// <summary>A gambler plays (state 149, <c>0x00456B37</c>).</summary>
    Gambled = 0xD5,
    /// <summary>A gambler drinks (state 147, <c>0x00456DF0</c>).</summary>
    Drank = 0xE1,
    /// <summary>Gold reaches a creature's purse from a room: a jackpot taken (state 157, <c>0x00457677</c>) or a
    /// wage (<c>Payday_TakeWage</c>, <c>0x0047287A</c>).</summary>
    GoldReceived = 0xCA,
    /// <summary>The coin sound a wage taken opens with, the gold object's pick-up group (<c>0x00472803</c>).</summary>
    WageTaken = 0xC3,
    /// <summary>The creature that ate it (<c>0x0046398D</c>).</summary>
    Ate = 0xCD,
}

/// <summary>One positional request. <paramref name="Voiced"/> is false where DK2 passes handle 0 (the
/// creature's lift and drop): the sound then neither replaces nor is retained by the thing's own voice.
/// <paramref name="ViewerOnlyFor"/> names the owner of a request DK2 makes only for the local player's own
/// thing (the heart's opening): the view plays it only when that player is the viewer.</summary>
public readonly record struct ThingSoundRequest(
    SoundSourceId Source, string Category, ThingSoundEvent Event, FpPosition3 Position, bool Voiced = true,
    PlayerId? ViewerOnlyFor = null);

/// <summary>A sound a thing requests at its position. Read for creatures (<c>0xD6</c>, <c>0xDF</c>,
/// and the hand's <c>0xC3</c>/<c>0xC4</c>) and objects (the hand's <c>0xC3</c>/<c>0xC4</c>): positional
/// slot <c>+0x20</c> in the definition's category (creature <c>+0x6E3</c>, object <c>+0x10E</c>, terrain <c>+0x195</c>); each new kind's
/// slot and category are read at its call site before it is emitted here.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-leaving.md")]
public static class ThingSoundRules
{
    /// <summary>An object's request, in its definition's category at <paramref name="at"/> or, with none,
    /// its own position (the tile's centre, at floor height, while it keeps none: a deviation, DK2 reads its
    /// stored height). Nothing is emitted for an object with no category: the shipped objects whose category
    /// is empty have no group to play.</summary>
    public static void Request(SimulationContext context, string system, ObjectState thing, ThingSoundEvent sound,
        FpPosition3? at = null)
    {
        string category = context.Database.ObjectById(thing.ObjectTypeId)?.SoundCategory ?? "";
        if (category.Length == 0) return;
        if (at is not FpPosition3 point)
        {
            bool placed = thing.PosX != 0 || thing.PosY != 0;
            var x = new Fp32(placed ? thing.PosX : CreatureState.CentreOf(thing.TileIndex % context.World.Width));
            var y = new Fp32(placed ? thing.PosY : CreatureState.CentreOf(thing.TileIndex / context.World.Width));
            point = new(x, y, new Fp32(EggLaunchRules.FloorHeight(context.World, context.Database, x, y)));
        }
        Request(context, system, new SoundSourceId(SoundSourceKind.Object, thing.Id), category, sound, point);
    }

    /// <summary>A tile's request: handle 0, in the category of <paramref name="terrainId"/> (definition
    /// <c>+0x195</c>), at the tile's centre and <paramref name="height"/> (0 as <c>Tile_ApplyWork</c> and
    /// the room builder pass it, 0x1000 as the room sale does). Nothing is emitted for a terrain with no
    /// category. A room's tiles use its room terrain's category (room definition <c>+0x44E</c>).</summary>
    public static void RequestTile(SimulationContext context, string system, byte terrainId, int tileIndex,
        ThingSoundEvent sound, int height = 0, SoundSourceKind kind = SoundSourceKind.Terrain,
        bool voiced = false, PlayerId? viewerOnlyFor = null, SoundSourceId? source = null)
    {
        string category = context.Database.TerrainById(terrainId).SoundCategory;
        if (category.Length == 0) return;
        int width = context.World.Width;
        Request(context, system, source ?? new SoundSourceId(kind, tileIndex), category, sound,
            new(new Fp32(CreatureState.CentreOf(tileIndex % width)), new Fp32(CreatureState.CentreOf(tileIndex / width)),
                new Fp32(height)), voiced, viewerOnlyFor);
    }

    /// <summary>A creature's request with handle 0, which DK2 passes when lifting and dropping it.</summary>
    public static void RequestUnvoiced(SimulationContext context, string system, CreatureState creature,
        ThingSoundEvent sound, FpPosition3 position) =>
        Request(context, system, SoundSourceId.Of(new CreatureId(creature.Id)),
            context.Database.CreatureById(creature.CreatureTypeId).SoundCategory, sound, position, voiced: false);

    /// <summary>A creature's request in the category of an object definition (<c>+0x10E</c>) at its position,
    /// with handle 0 unless <paramref name="voiced"/>: the imp's gold deposit asks the gold object's
    /// (<c>0x00466B95</c>); a wage taken passes the creature's own handle (<c>0x00472803</c>). Nothing is
    /// emitted for an object with no category.</summary>
    public static void RequestInObjectCategory(SimulationContext context, string system, CreatureState creature,
        byte objectTypeId, ThingSoundEvent sound, bool voiced = false)
    {
        string category = context.Database.ObjectById(objectTypeId)?.SoundCategory ?? "";
        if (category.Length == 0) return;
        Request(context, system, SoundSourceId.Of(new CreatureId(creature.Id)), category, sound,
            new(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ)), voiced);
    }

    public static void Request(SimulationContext context, string system, CreatureState creature, ThingSoundEvent sound,
        PlayerId? viewerOnlyFor = null) =>
        Request(context, system, SoundSourceId.Of(new CreatureId(creature.Id)),
            context.Database.CreatureById(creature.CreatureTypeId).SoundCategory, sound,
            new(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ)), viewerOnlyFor: viewerOnlyFor);

    public static void Request(SimulationContext context, string system, SoundSourceId source, string category,
        ThingSoundEvent sound, FpPosition3 position, bool voiced = true, PlayerId? viewerOnlyFor = null)
    {
        // Nobody listening: skip the detail string and the event (the null sink is the one cheap test there is).
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        if (category.Length > 31) category = category.Substring(0, 31);
        context.Events.Emit(new SimEvent(context.Tick, system, "thing_sound",
            $"{source.Kind.ToString().ToLowerInvariant()}={source.Value} event={(ushort)sound}")
        {
            ThingSound = new(source, category, sound, position, voiced, viewerOnlyFor),
        });
    }
}
