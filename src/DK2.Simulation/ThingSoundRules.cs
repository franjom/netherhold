// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>The kinds of thing that request world sounds; each owns its own range of voices.</summary>
public enum SoundSourceKind : byte { Creature, Object, Door, Trap, Shot, Room, Terrain, Player, Tremor, ShotSecond, Corpse }

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
    /// <summary>The Horned Reaper arrives (state 235, <c>0x004747BA</c>) or is shown at the finale's landing
    /// (state 230, <c>0x00474295</c>).</summary>
    ReaperArrived = 0x32D,
    /// <summary>The finale's Reaper takes the Portal Gem (state 233, <c>0x00474644</c>).</summary>
    ReaperTookGem = 0x32E,
    /// <summary>A creature burns (<c>Creature_UpdateTimedEffects</c>, <c>0x0048E4F1</c>).</summary>
    Burning = 0x340,
    /// <summary>A dying Jack in the Box makes its blast (the health routine, <c>0x004B315E</c>).</summary>
    JackExploded = 0x34C,
    /// <summary>A creature gains a level (<c>Creature_LevelUp</c>, <c>0x0048B2DB</c>).</summary>
    LeveledUp = 0x349,
    /// <summary>A trainee begins its melee swing at the dummy (<c>Training_State111</c>, <c>0x004857CD</c>).</summary>
    Trained = 0xD3,
    /// <summary>A creature is offered in the Temple's pool (state 101, <c>0x004729FB</c>).</summary>
    Sacrificed = 0xE2,
    /// <summary>A worshipper begins its prayer (state 104, <c>0x00472E70</c>).</summary>
    Prayed = 0xD4,
    /// <summary>A chicken's step (<c>Object_Process9MoveTo</c>, <c>0x0049FA95</c>), on the call where its step count is 1.</summary>
    ChickenStep = 0x110,
    /// <summary>A door's open state turns on (<c>Door_Update</c>, <c>0x004A60B9</c>).</summary>
    DoorOpened = 0xE7,
    /// <summary>A door's open state turns off (<c>0x004A60B9</c>).</summary>
    DoorClosed = 0xE8,
    /// <summary>A door's lock mode is set nonzero (<c>Door_SetLockMode</c>, <c>0x004A6844</c>).</summary>
    DoorLocked = 0xE9,
    /// <summary>A door's lock mode is set to zero (<c>0x004A6844</c>).</summary>
    DoorUnlocked = 0xEA,
    /// <summary>A trap's shot is made (<c>Trap_CreateShot</c>, <c>0x004A82C2</c>, <c>0x004A830E</c>).</summary>
    TrapFired = 0xEB,
    /// <summary>A trap's cleanup (<c>0x004A7B48</c>).</summary>
    TrapDestroyed = 0xF4,
    /// <summary>A boulder rolls (<c>Boulder_Roll</c>, <c>0x004A0E30</c>), at the start of each update.</summary>
    BoulderRolling = 0x14F,
    /// <summary>A boulder turns at a wall and stays whole (<c>0x004A101F</c>).</summary>
    BoulderTurned = 0x149,
    /// <summary>A boulder strikes a creature it leaves standing (<c>Boulder_Strike</c>, <c>0x004A0930</c>).</summary>
    BoulderHitCreature = 0x14D,
    /// <summary>The same for a creature it leaves dying.</summary>
    BoulderHitDownedCreature = 0x14E,
    /// <summary>A boulder strikes a door whose impact byte is not 4 or 5.</summary>
    BoulderHitDoor = 0x14A,
    /// <summary>The same for impact byte 4 or 5.</summary>
    BoulderHitMetalDoor = 0x15A,
    /// <summary>A boulder wears on water (<c>Boulder_TerrainWear</c>, <c>0x004A10E0</c>).</summary>
    BoulderInWater = 0x14B,
    /// <summary>A boulder wears on lava.</summary>
    BoulderInLava = 0x14C,
    /// <summary>A boulder breaks (<c>Boulder_Update</c>, <c>0x004A1390</c>).</summary>
    BoulderBroke = 0xF4,
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
    /// <summary>An object is made (<c>Object_Init</c>, <c>0x0049B4C8</c>), with the object's first handle.</summary>
    ObjectMade = 0xF1,
    /// <summary>The same, with its second handle (<c>0x0049B4ED</c>).</summary>
    ObjectMadeSecond = 0xF2,
    /// <summary>An object is deleted (<c>Object_OnDelete</c>, <c>0x0049B691</c>).</summary>
    ObjectRemoved = 0xF4,
    /// <summary>A Training Dummy begins its struck spin (<c>ObjectState18_Struck</c>, <c>0x004A14F2</c>).</summary>
    StruckDummy = 0x311,
    /// <summary>The Increase Level special is used (<c>0x004A42C0</c>).</summary>
    SpecialIncreasedLevel = 0x80,
    /// <summary>The Receive Imps special is used (<c>0x004A4980</c>).</summary>
    SpecialReceivedImps = 0x8B,
    /// <summary>The Locate Hidden Land special is used (<c>0x004A4F70</c>).</summary>
    SpecialLocatedHiddenLand = 0x8F,
    /// <summary>The Workshop's steam effect and lava spill (<c>0x004F4C27</c>, <c>0x004F4DC7</c>).</summary>
    WorkshopSteamOrSpill = 0x2E9,
    /// <summary>The Workshop's smoke effect (<c>0x004F4F03</c>).</summary>
    WorkshopSmoke = 0x2E8,
    /// <summary>A keeper spell is cast (<c>Spell_CastAtPosition</c> <c>0x004BBEBD</c>, <c>Spell_CastOnCreature</c> <c>0x004BC26F</c>) in
    /// the spell's category, or a shot is constructed (<c>Shot_InitialiseFromDefinition</c> <c>0x004AAF1E</c>) in the shot's.</summary>
    SpellCast = 0xEF,
    /// <summary>A shot is constructed, with its second handle (<c>0x004AAF40</c>).</summary>
    ShotMadeSecond = 0xF2,
    /// <summary>A shot dies of its lifetime (<c>Shot_Update</c> <c>0x004AB509</c>), or is removed (<c>Shot_Teardown</c>
    /// <c>0x004AAF9F</c>, second handle).</summary>
    ShotEnded = 0xF4,
    /// <summary>The world stopped a shot (<c>Shot_Update</c> <c>0x004AB49B</c>).</summary>
    ShotHitSolid = 0x1BC,
    /// <summary>The Sight of Evil has twenty updates left (<c>Shot_ProcessSightOfEvil</c> <c>0x004AF1BF</c>).</summary>
    SightOfEvilEnding = 0xE8,
    /// <summary>A creature finishes its melee swing, in its own category (<c>Combat_MeleeSwing</c> <c>0x004992EB</c>), or a shot
    /// that turns what it strikes hits a creature, in that creature's (<c>Shot_Collide</c> <c>0x004AC099</c>).</summary>
    Struck = 0xDB,
    /// <summary>A creature thing is freed (<c>Creature_DeathBookkeeping</c>, <c>0x00487E4A</c>).</summary>
    CreatureFreed = 0xDA,
    /// <summary>A Tremor starts (<c>World_StartTremor</c> <c>0x00510355</c>), in the Tremor keeper spell's category.</summary>
    TremorStarted = 0xFB,
    /// <summary>A Tremor shakes a tile (<c>World_UpdateTremors</c> <c>0x005105C3</c>).</summary>
    TremorShook = 0x2EC,
    /// <summary>A Tremor ends (<c>0x005104C6</c>).</summary>
    TremorEnded = 0x31F,
    /// <summary>A possessed worker acts at solid rock it cannot dig or mine (<c>GameAction_PossessedCreatureAct</c>, <c>0x00515021</c>).</summary>
    PossessedRock = 0x335,
    /// <summary>A Casino party's music, asked for each dancer of the room while the party runs (<c>Casino_RoomUpdate</c>, <c>0x004D63D0</c>), with the room's handle.</summary>
    PartyMusic = 0x316,
    /// <summary>The party's end, after the music's stop (<c>0x004D6727</c>), with the same handle.</summary>
    PartyEnded = 0x33E,
    /// <summary>A manufacturer works at an Anvil, in the Anvil's category (<c>CreatureAction_Manufacture</c>, <c>0x00498E08</c>).</summary>
    Forged = 0x112,
    /// <summary>A corpse is destroyed, in <c>OBJECT_FLIES</c> (<c>Corpse_Destroy</c> <c>0x004B10A0</c>).</summary>
    CorpseDestroyed = 0xF4,
}

/// <summary>One positional request. <paramref name="Voiced"/> is false where DK2 passes handle 0 (the
/// creature's lift and drop): the sound then neither replaces nor is retained by the thing's own voice.
/// <paramref name="ViewerOnlyFor"/> names the owner of a request DK2 makes only for the local player's own
/// thing (the heart's opening): the view plays it only when that player is the viewer.
/// <paramref name="ListenerRelative"/> marks a request DK2 makes at the camera's point with a volume set from the distance to <paramref name="Position"/> (the Casino party's music, <c>0x004D63D0</c>): playback is nonspatial and the player scales it.
/// <paramref name="Stop"/> marks the request that is not a sound: DK2's stop command on the thing's handle (slot <c>+0x38</c>, command <c>0x0066C77C</c>), which ends the voice the thing owns (<see cref="ThingSoundRules.Stop"/>); category and event are unused.
/// <paramref name="Alternating"/> marks a request DK2 gates on a global counter that nothing else reads (the chicken's
/// step, <c>0x006E4FD4</c>): the listener keeps the counter, tests bit 0 and then adds one, for every such request it is
/// given, so the simulation keeps no state for it.</summary>
public readonly record struct ThingSoundRequest(
    SoundSourceId Source, string Category, ThingSoundEvent Event, FpPosition3 Position, bool Voiced = true,
    PlayerId? ViewerOnlyFor = null, bool Alternating = false, bool Stop = false, bool ListenerRelative = false);

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
        FpPosition3? at = null, bool alternating = false)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        string category = context.Database.ObjectById(thing.ObjectTypeId)?.SoundCategory ?? "";
        if (category.Length == 0) return;
        FpPosition3 point = at ?? PointOf(context, thing);
        Request(context, system, new SoundSourceId(SoundSourceKind.Object, thing.Id), category, sound, point, alternating: alternating);
    }

    /// <summary>An object's own position (DK2's <c>+0x1A</c>): its stored x and y, or its tile's centre while it
    /// keeps none, at floor height (a deviation: DK2 reads its stored height).</summary>
    public static FpPosition3 PointOf(SimulationContext context, ObjectState thing)
    {
        bool placed = thing.PosX != 0 || thing.PosY != 0;
        var x = new Fp32(placed ? thing.PosX : CreatureState.CentreOf(thing.TileIndex % context.World.Width));
        var y = new Fp32(placed ? thing.PosY : CreatureState.CentreOf(thing.TileIndex / context.World.Width));
        return new(x, y, new Fp32(EggLaunchRules.FloorHeight(context.World, context.Database, x, y)));
    }

    /// <summary>A door's request: its definition's category (<c>+0xA9</c>, <see cref="DoorDef.SoundCategory"/>), its own
    /// handle, at the middle of its tile and the floor's height (DK2 reads the position it stored; ours keeps none).
    /// Nothing is emitted for a door with no category.</summary>
    public static void Request(SimulationContext context, string system, DoorState door, ThingSoundEvent sound)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        if (!context.Database.Doors.TryGetValue(door.DoorId, out DoorDef? definition) || definition.SoundCategory.Length == 0) return;
        Request(context, system, new SoundSourceId(SoundSourceKind.Door, door.Id), definition.SoundCategory, sound,
            AtTile(context, door.X, door.Y));
    }

    /// <summary>A trap's request: its definition's category (<c>+0xB0</c>, <see cref="TrapDef.SoundCategory"/>), its own
    /// handle, at the middle of its tile and the floor's height. <paramref name="viewerOnlyFor"/> names the keeper whose
    /// screen DK2 makes the request for alone (the Alarm's two). Nothing is emitted for a trap with no category.</summary>
    public static void Request(SimulationContext context, string system, TrapState trap, ThingSoundEvent sound,
        PlayerId? viewerOnlyFor = null)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        if (!context.Database.Traps.TryGetValue(trap.TrapId, out TrapDef? definition) || definition.SoundCategory.Length == 0) return;
        Request(context, system, new SoundSourceId(SoundSourceKind.Trap, trap.Id), definition.SoundCategory, sound,
            AtTile(context, trap.X, trap.Y), viewerOnlyFor: viewerOnlyFor);
    }

    private static FpPosition3 AtTile(SimulationContext context, int x, int y)
    {
        var px = new Fp32(CreatureState.CentreOf(x));
        var py = new Fp32(CreatureState.CentreOf(y));
        return new(px, py, new Fp32(EggLaunchRules.FloorHeight(context.World, context.Database, px, py)));
    }

    /// <summary>A tile's request: handle 0, in the category of <paramref name="terrainId"/> (definition
    /// <c>+0x195</c>), at the tile's centre and <paramref name="height"/> (0 as <c>Tile_ApplyWork</c> and
    /// the room builder pass it, 0x1000 as the room sale does). Nothing is emitted for a terrain with no
    /// category. A room's tiles use its room terrain's category (room definition <c>+0x44E</c>).</summary>
    public static void RequestTile(SimulationContext context, string system, byte terrainId, int tileIndex,
        ThingSoundEvent sound, int height = 0, SoundSourceKind kind = SoundSourceKind.Terrain,
        bool voiced = false, PlayerId? viewerOnlyFor = null, SoundSourceId? source = null, bool listenerRelative = false)
    {
        string category = context.Database.TerrainById(terrainId).SoundCategory;
        if (category.Length == 0) return;
        int width = context.World.Width;
        Request(context, system, source ?? new SoundSourceId(kind, tileIndex), category, sound,
            new(new Fp32(CreatureState.CentreOf(tileIndex % width)), new Fp32(CreatureState.CentreOf(tileIndex / width)),
                new Fp32(height)), voiced, viewerOnlyFor, listenerRelative: listenerRelative);
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
        ThingSoundEvent sound, FpPosition3 position, bool voiced = true, PlayerId? viewerOnlyFor = null, bool alternating = false, bool listenerRelative = false)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        if (category.Length > 31) category = category.Substring(0, 31);
        context.RequestSound(system, new(source, category, sound, position, voiced, viewerOnlyFor, alternating, ListenerRelative: listenerRelative));
    }

    /// <summary>DK2's stop command on a thing's handle (slot <c>+0x38</c> with the command object <c>0x0066C77C</c>: the instance found by handle gets its stop method,
    /// vtable <c>+0x18</c>). Asked where a thing's loop ends: the burning creature's fire (<c>0x0048E62E</c>).</summary>
    public static void Stop(SimulationContext context, string system, SoundSourceId source)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        context.RequestSound(system, new(source, "", default, default, Stop: true));
    }

    /// <summary>A creature's request from a hook, which holds no event sink of its own: the creature's handle and
    /// position, the category of <paramref name="definition"/>. The level increment asks this way
    /// (<c>0x0048B2DB</c>), as it asks for its effect.</summary>
    public static void Request(in StateHookContext hooks, string system, CreatureDef definition, CreatureState creature,
        ThingSoundEvent sound)
    {
        string category = definition.SoundCategory;
        if (category.Length > 31) category = category.Substring(0, 31);
        hooks.RequestSound(system, new(SoundSourceId.Of(new CreatureId(creature.Id)), category, sound,
            new(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ))));
    }
}
