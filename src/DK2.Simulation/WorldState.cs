// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The mutable per-tile simulation state.
///
/// Distinct from <see cref="MapTile"/>, which is the immutable file view. Every
/// field is justified by a mechanic that reads it today:
/// <list type="bullet">
/// <item>terrain, owner and flags come straight from the MAP record (CONFIRMED);</item>
/// <item>health is seeded from <see cref="TerrainDef.StartingHealth"/> and is what
/// digging, claiming and reinforcing modify (CONFIRMED — see
/// docs/research/kwd-terrain-rooms.md);</item>
/// <item>gold held is where a keeper's gold actually is, because every limit DK2
/// sets on gold is per tile (see <see cref="GoldStore"/>).</item>
/// </list>
/// Anything else a tile needs (room membership beyond its terrain, gold left in
/// a seam) arrives with the mechanic that reads it, not before.
/// </summary>
public struct TileState
{
    public byte TerrainId;
    public byte OwnerPlayerId;

    /// <summary>
    /// The MAP record's third byte per tile, preserved verbatim — and, for a
    /// bridge, <b>the hazard the bridge spans</b>: 2 for lava, 1 for water.
    ///
    /// <para>Carried since M2 with the meaning UNKNOWN (Q16). It was worked out
    /// on 2026-09-03, when the owner confirmed that a sold bridge reverts to the
    /// water or lava underneath it rather than to floor: the byte is 1 on every
    /// tile of all 66 shipped maps except bridge tiles over lava, where it is 2,
    /// and the two sets never cross. See <see cref="BridgeRules"/> for the
    /// evidence and the confidence, and Q64.</para>
    ///
    /// <para>Written by <see cref="RoomRules.Place"/> when a player builds a
    /// bridge, since the level file only speaks for bridges it shipped with.
    /// Whether the byte carries other meanings on other terrain is still
    /// UNKNOWN; nothing reads it except the bridge rules.</para>
    /// </summary>
    public byte Flags;

    /// <summary>Current tile health. Seeded from the terrain type's starting health.</summary>
    public int Health;

    /// <summary>
    /// Gold physically stored on this tile.
    ///
    /// Non-zero only where <see cref="GoldStore.Capacity"/> is — a treasury or
    /// dungeon-heart tile. A player's spendable gold is the sum of this over the
    /// tiles they own; there is no player-level gold counter, on purpose
    /// (ADR-0015, <see cref="GoldStore"/>).
    /// </summary>
    public int GoldHeld;

    /// <summary>
    /// Which players have this tile tagged for digging: bit <c>n</c> is set for
    /// player id <c>n</c>.
    ///
    /// STRONGLY INFERRED that tagging is per player rather than global: DK2 is a
    /// multiplayer game in which each keeper marks and sees their own dig
    /// orders, and the terrain table carries a per-type <c>Taggable</c> flag
    /// rather than a shared tag list. The bitmask is our representation, not a
    /// claim about the original's storage.
    /// </summary>
    public ushort TaggedBy;

    /// <summary>
    /// Keepers that have discovered this tile: bit <c>n</c> belongs to player
    /// id <c>n</c>. This is our per-keeper representation, not a claim about
    /// DK2's packing. DK2 persists its live tile fog bit in saved games, so the
    /// information is simulation state and must be hashed and snapshotted.
    /// </summary>
    public ushort DiscoveredBy;

    /// <summary>
    /// Whether this tile is floor that a worker <b>dug out</b> and nobody has
    /// claimed since — the tile an imp goes back for before it does anything
    /// else.
    ///
    /// <para><b>Read from the original, 2026-09-05</b> (Q154,
    /// docs/research/imp-task-priority.md §6). When a dig leaves plain unowned
    /// path beside a keeper's floor, DK2 raises a task on that tile which sits
    /// <b>first</b> in the imp's whole-map order — ahead of digging — and is
    /// taken as soon as none of the tile's eight neighbours still carries a dig
    /// task. That is why a corridor in the original is dug through and then
    /// claimed behind the digging front, rather than every mark on the map
    /// being dug before any of it is claimed.</para>
    ///
    /// <para>Set by <see cref="Systems.TileWorkSystem"/> when excavation
    /// destroys a tile; cleared by any later change of terrain, which is what a
    /// claim is. Our representation of DK2's task — the original keeps it in
    /// the keeper's task list, not on the tile — and explicitly not a claim
    /// about its storage.</para>
    /// </summary>
    public bool FreshlyDug;

    /// <summary>
    /// Health owed to this tile that was too small to apply, carried between
    /// applications. Always smaller in magnitude than
    /// <see cref="SimulationTiming.WorkApplicationsPerSecond"/>.
    ///
    /// <para>Work lands five times a second (the fifth is Efficiency 80% over
    /// four ticks — ADR-0020, amended) and each application moves a fifth
    /// of the tabled per-second delta (Q47). Eight of the ten deltas divide by
    /// five exactly; <c>DigEnemyWallHealth</c> (−8) and <c>RepairWallHealth</c>
    /// (22) do not, and integer division alone would quietly run them 37% and
    /// 10% slow forever. Carrying the remainder makes every rate exactly what
    /// the table says over any whole second while keeping the arithmetic in
    /// integers — the same device <see cref="CreatureState.MoveRemainder"/> uses
    /// for the same reason (ADR-0001).</para>
    ///
    /// <para>On the tile rather than on the worker, because the fraction belongs
    /// to the wearing-down of this tile: two imps on one wall should between
    /// them remove what the table says per second, which is what a shared
    /// accumulator gives and two private ones would not. Shared across work
    /// kinds too, which is a simplification with no observable consequence —
    /// only the two wall jobs above ever leave a remainder, and a tile that
    /// changes terrain starts again at zero.</para>
    /// </summary>
    public sbyte WorkRemainder;

    /// <summary>
    /// Ticks left before this tile burns away, or 0 when nothing is burning.
    ///
    /// <para><b>A wooden bridge over lava does not last</b>
    /// (<c>WoodBridgeLifeOnLavaSeconds</c> = 30, a variable that names its own
    /// mechanic). CONFIRMED by the owner (2026-09-03), including the detail that
    /// decides the shape: <b>the clock starts when the bridge is built</b> — not
    /// when a creature steps on it, and not as damage to the tile's health.
    /// Q67.</para>
    ///
    /// <para>A separate countdown rather than health decay, because health is
    /// already what a rival attacking the bridge reduces, and a bridge over
    /// water has exactly the same 2000 health and never burns. Conflating the
    /// two would make combat damage and fire the same quantity, which they are
    /// not.</para>
    ///
    /// <para>Set by <see cref="RoomRules.Place"/> and spent by
    /// <see cref="Systems.BridgeBurnSystem"/>. Reset to zero the moment the tile
    /// stops being that bridge, however it stops.</para>
    /// </summary>
    public int BurnTicksRemaining;

    /// <summary>
    /// Ticks this portal has waited since it last produced a creature, or 0 on
    /// every tile that is not a portal's centre.
    ///
    /// <para><b>The clock is per portal — CONFIRMED by observation</b> (owner,
    /// 2026-09-07): a second portal produced 8 seconds after the first at a
    /// point where the interval was about 60 seconds, and one clock cannot emit
    /// twice in 8 seconds while it is waiting 60. It also has to be, because two
    /// portals produce on their own schedules and the cap rises with the second
    /// one (<see cref="PortalRules.CreatureCapacity"/>). It cannot live on
    /// <see cref="PlayerState"/> for that reason, and it cannot live on
    /// <see cref="RoomInstance"/> because a room instance is derived fresh from
    /// the tiles every time it is asked for and remembers nothing. So it lives
    /// on the one tile that identifies a portal — the hole in its middle, which
    /// <see cref="PortalRules.CanProduce"/> guarantees the portal owns.</para>
    ///
    /// <para><b>Counted up rather than down</b>, which matches how DK2 does it:
    /// the original stores the tick of the last generation
    /// (<c>room+0x49</c>) and compares <c>now</c> against it, so elapsed time is
    /// the thing that exists and the interval is recomputed. One consequence
    /// survives that reasoning and is worth keeping in view: <b>a portal blocked
    /// for want of a lair banks its wait and produces the moment the lair is
    /// built.</b></para>
    ///
    /// <para><b>The other consequence used to be wrong and is now gone.</b> This
    /// paragraph said counting up means "a creature dying shortens the wait,
    /// which is the behaviour OpenKeeper has and the only one that makes the
    /// scaling coherent". It does not: the interval depends on
    /// <see cref="EntranceCreaturesGenerated"/>, which never falls, so a death
    /// changes nothing. The old sentence was true only of the formula it was
    /// written beside, and that formula was OpenKeeper's guess.</para>
    ///
    /// <para><b>Only while owned.</b> An unclaimed portal is not waiting for
    /// anything, so its clock does not run and a keeper who claims one waits a
    /// full interval for the first arrival. UNKNOWN, and the alternative — that
    /// claiming a portal that has stood idle hands you a creature at once — is
    /// what OpenKeeper does, though by accident rather than by design (it resets
    /// to Java's <c>Double.MIN_VALUE</c>, which is the smallest <i>positive</i>
    /// double and not the sentinel it reads as). Q86.</para>
    ///
    /// <para>Managed entirely by <see cref="Systems.PortalSystem"/>. On any
    /// other tile the value is meaningless and nothing reads it.</para>
    /// </summary>
    public int EntranceIdleTicks;

    /// <summary>
    /// How many creatures this portal has generated, on the same tile as
    /// <see cref="EntranceIdleTicks"/> and zero everywhere else. <b>It is the
    /// <c>n</c> in the arrival interval, and it only ever climbs.</b>
    ///
    /// <para><b>CONFIRMED three ways</b> (Q86, <c>portals.md</c>). DK2 keeps this
    /// on the room at <c>room+0x4D</c> and increments it once per creature
    /// generated; the interval is <c>(n² + EntranceGenerationSpeedSeconds)</c>
    /// game seconds, read off six instructions at <c>0x004DBBE0</c>. Then the
    /// owner timed it twice: portal 1's gaps of 25, 26, 32, 33, 42 seconds
    /// against a predicted 25, 26, 29, 34, 41 (mean error 1.0 s), and — the
    /// measurement that settled which counter it is — <b>a second portal
    /// claimed after five arrivals started again at 25, not at 50</b>.</para>
    ///
    /// <para><b>Per portal, not per keeper.</b> That was the open half of Q86
    /// and the observed gaps do not leave it open: portal 2's first four gaps
    /// were 27, 30, 31, 33 seconds where a keeper-wide tally predicts 50, 61,
    /// 74, 89 and this project's previous formula predicted 62, 75, 87, 100.
    /// Mean absolute error 2.3 s against 38.3 s and 50.8 s.</para>
    ///
    /// <para><b>It never falls, and that is read rather than inferred.</b> There
    /// are 129 references to a <c>+0x4D</c> field across the whole executable
    /// and not one is a <c>DEC</c> or a <c>SUB</c>. So a portal does not speed
    /// up when creatures die, desert, or are thrown back into it — which is why
    /// this counts generations and not the keeper's living creatures, as the
    /// formula it replaces did.</para>
    ///
    /// <para><b>What is still UNKNOWN is whether it is ever zeroed.</b> Two
    /// routines store a literal zero here and both are reached only through a
    /// vtable, which has the shape of an ownership change — the only room event
    /// a portal has, since a portal cannot be sold or rebuilt (owner). A freshly
    /// claimed portal starting at zero is observed; a <i>worn</i> portal lost to
    /// a rival and re-claimed is not, and only a rival can answer it. Zero here
    /// on claim is therefore ours: <c>APortalStartsItsTallyAtZeroWhenClaimed</c>
    /// says so in its name.</para>
    ///
    /// <para>Shares the portal centre tile with the clock, so the two are lost
    /// and restored together. Managed entirely by
    /// <see cref="Systems.PortalSystem"/>.</para>
    /// </summary>
    public int EntranceCreaturesGenerated;

    /// <summary>
    /// A hatchery's clock towards its next egg, on the room's lowest tile index
    /// and zero everywhere else.
    ///
    /// <para><b>DK2 keeps this on the room</b> — its room debug dump prints
    /// <c>GameTickLastGenerated</c>, <c>GenerationSpeed</c> and
    /// <c>ChickensGenerated</c> for the Hatchery case — and rooms here are
    /// derived rather than stored (<see cref="RoomInstance"/>), so the clock has
    /// to live on something that is. A tile is what
    /// <see cref="EntranceIdleTicks"/> already uses for the portal's clock, and
    /// the lowest tile index is stable because <c>RoomInstance.TileIndices</c>
    /// is kept in ascending order.</para>
    ///
    /// <para><b>Consequence, stated:</b> a hatchery that grows or is cut so that
    /// its lowest tile changes starts its clock again. That is a real
    /// difference from the original, where the clock belongs to the room object
    /// and survives; it costs at most one laying interval and only when a
    /// keeper is building.</para>
    /// </summary>
    public int HatchTicks;

    /// <summary>Highest player id that fits in <see cref="TaggedBy"/>.</summary>
    public const int MaxTaggingPlayerId = 15;

    public readonly bool IsTaggedBy(byte playerId) =>
        playerId <= MaxTaggingPlayerId && (TaggedBy & (ushort)(1 << playerId)) != 0;

    public readonly bool IsTaggedByAnyone => TaggedBy != 0;

    public readonly bool IsDiscoveredBy(byte playerId) =>
        playerId <= MaxTaggingPlayerId && (DiscoveredBy & (ushort)(1 << playerId)) != 0;

    public void SetDiscoveredBy(byte playerId)
    {
        if (playerId > MaxTaggingPlayerId)
            throw new ArgumentOutOfRangeException(
                nameof(playerId), playerId,
                $"Tile discovery only tracks player ids 0..{MaxTaggingPlayerId}.");

        DiscoveredBy |= (ushort)(1 << playerId);
    }

    public void SetTaggedBy(byte playerId, bool tagged)
    {
        if (playerId > MaxTaggingPlayerId)
            throw new ArgumentOutOfRangeException(
                nameof(playerId), playerId,
                $"Tile tags only track player ids 0..{MaxTaggingPlayerId}.");

        ushort bit = (ushort)(1 << playerId);
        TaggedBy = tagged ? (ushort)(TaggedBy | bit) : (ushort)(TaggedBy & ~bit);
    }

    public readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(TerrainId);
        hash.Add(OwnerPlayerId);
        hash.Add(Flags);
        hash.Add(Health);
        hash.Add(GoldHeld);
        hash.Add(TaggedBy);
        hash.Add(DiscoveredBy);
        hash.Add(FreshlyDug ? (byte)1 : (byte)0);
        hash.Add(WorkRemainder);
        hash.Add(BurnTicksRemaining);
        hash.Add(EntranceIdleTicks);
        hash.Add(EntranceCreaturesGenerated);
        hash.Add(HatchTicks);
    }
}

/// <summary>
/// Per-player simulation state.
///
/// <para><b>Mana is here and gold is not</b>, and the asymmetry is the data's.
/// Mana in DK2 is generated by owned land and spent from a single pool
/// (<c>MaximumManaThreshold</c>, <c>ExcessManaDecreaseRatePerSecond</c>) — a
/// player-level number with no location. Gold is always in a place, and every
/// limit the balance table sets on it is per tile, so it lives on tiles instead;
/// see <see cref="GoldStore"/> and ADR-0015. Research, spells, creature rosters
/// and the rest arrive with the mechanics that need them.</para>
/// </summary>
public struct PlayerState
{
    public byte PlayerId;
    public int Mana;

    /// <summary>
    /// Research points banked by this keeper, DK2's <c>player+0x20F</c>.
    ///
    /// <para><b>Only ever added to, and never spent here.</b> The original keeps
    /// this beside a <i>current target</i> at <c>player+0x213</c> and, when a
    /// target is set and the points reach its cost, completes it. This project
    /// has the accrual and not the tree (<see cref="ResearchRules"/>), so the
    /// points bank indefinitely — which is faithful for the case that matters,
    /// because the original credits them <b>whether or not a target is
    /// selected</b>: the field is written before the target is even looked
    /// at.</para>
    ///
    /// <para>Reduced by nothing in this simulation. The original's adder does
    /// have a subtract path — a negative credit is clamp-subtracted rather than
    /// allowed to go below zero — but nothing has been found that passes it a
    /// negative amount, so what it is for is UNKNOWN (Q218).</para>
    /// </summary>
    public int ResearchPoints;

    /// <summary>
    /// The earliest tick at which this keeper may be handed a free imp.
    ///
    /// <para><b>A deadline, not a countdown</b>, and the distinction is the
    /// whole of Q56. DK2 keeps one field on the keeper and writes it in exactly
    /// one place — immediately after a free imp is made, as
    /// <c>now + TimeBeforeFreeImpGeneratedSeconds</c>. Nothing resets it, nothing
    /// pauses it, and nothing touches it while the keeper is at full strength.
    /// So the deadline of a keeper who has been at four imps for a minute is a
    /// minute in the past, and the first tick they drop below
    /// <c>MinimumImpThreshold</c> the replacement is already due: it arrives
    /// <b>at once</b>, and only the ones after it are five seconds apart.</para>
    ///
    /// <para>This project had the opposite for three weeks — a countdown held at
    /// zero while the keeper was up to strength, which made every replacement
    /// wait a fresh five seconds. See <see cref="ImpSupplyRules"/> and
    /// docs/research/imps-and-mana.md.</para>
    /// </summary>
    public GameTick NextFreeImpTick;

    /// <summary>
    /// The tick this keeper's next payday falls due.
    ///
    /// <para><b>A deadline, like <see cref="NextFreeImpTick"/></b>, and the
    /// original keeps it the same way: one field on the player, compared
    /// against now, rewritten as <c>now + PayDayFrequencySeconds x
    /// ticksPerSecond</c> the moment it fires. The first one is seeded from
    /// <c>TimeBeforeFirstPayDaySeconds</c> rather than from zero, so a level
    /// does not open with the whole dungeon owed money.</para>
    ///
    /// <para><b>Zero means "not scheduled yet"</b>, and
    /// <see cref="Systems.PaydaySystem"/> seeds it the first tick it sees one —
    /// a divergence, because the original reads zero as <i>never</i> and seeds
    /// the field in an initialiser instead. Ours has nowhere to seed it
    /// (<see cref="FromMap"/> has no <c>GameDatabase</c>) and nothing that needs
    /// <i>never</i>: a player slot with no creatures bills nobody. ADR-0078
    /// records it.</para>
    /// </summary>
    public GameTick NextPaydayTick;

    /// <summary>
    /// The Hand of Evil: the <b>things</b> this keeper is holding, in the order
    /// they were picked up.

    /// <para><b>Things, not creatures</b>, since ADR-0075. DK2 keeps one array
    /// of thing ids on the player record and its pick-up handler branches on
    /// the thing's kind — a creature and a chicken go into the same slots, and
    /// the owner has watched four chickens held at once. Our creature and
    /// object ids are separate spaces, so each slot carries which it
    /// is.</para>
    ///
    /// <para><b>A stack, and the original says so plainly.</b> Its pick-up
    /// handler appends at <c>NumberOfThingsInHand</c> and its drop handler
    /// reads back from the same index before decrementing it, so a keeper who
    /// picks up A then B puts down B first. Capacity is
    /// <c>MaxNumberOfThingsInHand</c>, which ships at 64 — see
    /// <see cref="HandRules"/>.</para>
    ///
    /// <para>On the keeper rather than on the creature because that is where
    /// DK2 keeps it — a fixed array on the player record, with the count and
    /// the capacity beside it — and because the ordering is the keeper's, not
    /// any creature's. The creature's own half of the relation is
    /// <see cref="CreatureState.InHand"/>, exactly as a bed and its sleeper
    /// each know one thing about the other (ADR-0059).</para>
    ///
    /// <para>Never null: <see cref="WorldState"/> gives every keeper a list when
    /// it creates them, so the field can be read without a check even though
    /// <see cref="PlayerState"/> is a struct.</para>
    /// </summary>
    public List<HandItem> Hand;

    public readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(PlayerId);
        hash.Add(Mana);
        hash.Add(ResearchPoints);
        hash.Add(NextFreeImpTick);
        hash.Add(NextPaydayTick);
        hash.Add(Hand.Count);
        foreach (HandItem held in Hand)
        {
            hash.Add((byte)held.Kind);
            hash.Add(held.Id);
        }
    }
}

/// <summary>What kind of thing a hand slot holds.</summary>
public enum HandItemKind : byte
{
    /// <summary>A creature — <see cref="CreatureState.InHand"/>.</summary>
    Creature = 0,

    /// <summary>A loose object — <see cref="ObjectState.InHand"/>.</summary>
    Object = 1,
}

/// <summary>
/// One slot of the Hand of Evil.
///
/// <para>DK2 needs no discriminator: creatures and objects share one thing
/// table and one id space, and its handlers branch on a kind field inside the
/// thing. Ours are separate lists with independent ids, so the kind rides on
/// the slot.</para>
/// </summary>
public readonly record struct HandItem(HandItemKind Kind, int Id)
{
    public static HandItem Of(CreatureState creature) =>
        new(HandItemKind.Creature, creature.Id);

    public static HandItem Of(ObjectState thing) => new(HandItemKind.Object, thing.Id);

    public bool IsCreature => Kind == HandItemKind.Creature;

    public bool IsObject => Kind == HandItemKind.Object;
}

/// <summary>
/// All mutable simulation state for one level. Owns no behaviour — systems act
/// on it. Everything reachable from here is hashed and snapshotted; anything
/// not reachable from here does not exist as far as determinism is concerned,
/// which is the invariant that keeps the gate meaningful.
/// </summary>
public sealed class WorldState
{
    private readonly TileState[] _tiles;
    private readonly PlayerState[] _players; // kept sorted by PlayerId, for stable hashing
    private readonly List<TileWorkOrder> _pendingWork = [];
    private readonly List<CreatureState> _creatures = [];
    private readonly List<ObjectState> _objects = [];
    private int _nextCreatureId = 1;
    private int _nextObjectId = 1;

    private WorldState(
        int width, int height, TileState[] tiles, PlayerState[] players, bool fogOfWarEnabled)
    {
        Width = width;
        Height = height;
        _tiles = tiles;
        _players = players;
        FogOfWarEnabled = fogOfWarEnabled;

        // One place, so that no construction path can produce a keeper without
        // a hand. PlayerState is a struct in an array, so a caller that built
        // one with an object initialiser could not have given it a list without
        // knowing to; here nobody has to know.
        for (int i = 0; i < _players.Length; i++)
            _players[i].Hand ??= [];

        CurrentTick = GameTick.Zero;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Whether player-specific discovery gates observation of this world.
    /// DKII.EXE stores this as a boolean game configuration value. The other
    /// setup-screen fog labels have not yet been tied to proven engine state.
    /// </summary>
    public bool FogOfWarEnabled { get; }

    /// <summary>The tick the world is currently *at*; the next Tick() executes this one.</summary>
    public GameTick CurrentTick { get; internal set; }

    public int TileCount => _tiles.Length;

    public ref TileState Tile(int x, int y)
    {
        if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
        return ref _tiles[(y * Width) + x];
    }

    public ref TileState Tile(TilePos p) => ref Tile(p.X, p.Y);

    public ref TileState TileAtIndex(int index) => ref _tiles[index];

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
    public bool Contains(TilePos p) => Contains(p.X, p.Y);

    public IReadOnlyList<byte> PlayerIds => _players.Select(p => p.PlayerId).ToArray();

    public int PlayerCount => _players.Length;

    public ref PlayerState PlayerAtIndex(int index) => ref _players[index];

    public ref PlayerState Player(byte playerId)
    {
        for (int i = 0; i < _players.Length; i++)
            if (_players[i].PlayerId == playerId)
                return ref _players[i];
        throw new KeyNotFoundException($"No player with id {playerId} in this world.");
    }

    /// <summary>
    /// Work applications waiting to be resolved by
    /// <see cref="Systems.TileWorkSystem"/>, in the order they were queued.
    ///
    /// A list, not a set or a dictionary: order is behaviour. Two imps working
    /// the same seam in a different order can end up carrying different amounts
    /// of gold, so the queue is FIFO and never sorted or deduplicated.
    ///
    /// In DK2 the producer is a working creature. That creature does not exist
    /// yet, so for now tests and the scenario harness queue work directly and
    /// say so in their reports.
    /// </summary>
    public IReadOnlyList<TileWorkOrder> PendingWork => _pendingWork;

    public void QueueWork(TileWorkOrder order)
    {
        if ((uint)order.TileIndex >= (uint)_tiles.Length)
            throw new ArgumentOutOfRangeException(
                nameof(order), order.TileIndex, "Work order references a tile outside the map.");
        _pendingWork.Add(order);
    }

    public void QueueWork(int x, int y, TileWorkKind kind, byte playerId)
    {
        if (!Contains(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is outside the map.");
        _pendingWork.Add(new TileWorkOrder((y * Width) + x, kind, playerId));
    }

    internal void ClearPendingWork() => _pendingWork.Clear();

    /// <summary>
    /// Every creature in the level, in spawn order.
    ///
    /// The order is behaviour, not bookkeeping: creatures are updated in it, so
    /// two imps that reach for the same job get it in this order. Never sorted,
    /// never reordered — removing a creature must preserve the order of the
    /// rest.
    /// </summary>
    public IReadOnlyList<CreatureState> Creatures => _creatures;

    /// <summary>
    /// Puts a creature on the map, at the centre of the given tile.
    ///
    /// <b>This is not how DK2 makes creatures.</b> The original's imps come from
    /// a dungeon heart, from the Create Imp spell at a mana cost, and from
    /// specials — governed by <c>TimeBeforeFreeImpGeneratedSeconds</c>,
    /// <c>MinimumImpThreshold</c> and <c>ImpPopCountdownTimeSeconds</c>, none of
    /// which can run without rooms and mana. Until they exist, something outside
    /// the simulation (a test, a scenario file) places creatures directly, and
    /// says so.
    /// </summary>
    public CreatureState SpawnCreature(
        GameDatabase database, byte creatureTypeId, byte ownerPlayerId, TilePos at)
    {
        if (!Contains(at))
            throw new ArgumentOutOfRangeException(nameof(at), at, "Spawn tile is outside the map.");

        var creature = new CreatureState
        {
            Id = _nextCreatureId++,
            CreatureTypeId = creatureTypeId,
            OwnerPlayerId = ownerPlayerId,
            PosX = CreatureState.CentreOf(at.X),
            PosY = CreatureState.CentreOf(at.Y),
            Activity = CreatureActivity.Idle,

            // The database is a parameter for this one line. A creature spawned
            // at zero health is a creature that dies on the tick it arrives, so
            // there must be no way to make one — which means no overload that
            // leaves health out.
            Health = database.CreatureById(creatureTypeId).Hp,
        };

        _creatures.Add(creature);
        return creature;
    }

    /// <summary>
    /// Takes a creature out of the world, and says whether it was there.
    ///
    /// <para><b>Not death — the end of it.</b> Dying in DK2 is a process: a
    /// creature collapses at zero health, lies there rescuable for
    /// <c>CreatureDyingStateDurationSeconds</c>, and only then dies (Q99,
    /// CONFIRMED, ADR-0051). <see cref="Systems.CreatureHealthSystem"/> runs
    /// that process and calls this at the end of it. What is still missing is
    /// what happens <i>after</i> — the corpse — so this removal currently stands
    /// in for "and then the body rots away too". A corpse is <b>not</b> an
    /// object, which is what this note used to say: it is a creature in a state,
    /// and what it waits on is an imp that can carry one
    /// (<c>docs/research/objects.md</c> §4).</para>
    ///
    /// <para>The creature's id is <b>not</b> reused: <c>_nextCreatureId</c> only
    /// ever goes up, so a level that has had creatures die cannot hand out an id
    /// it has already used, and a snapshot restores the counter rather than
    /// deriving it.</para>
    /// </summary>
    public bool RemoveCreature(int id)
    {
        for (int i = 0; i < _creatures.Count; i++)
        {
            if (_creatures[i].Id != id) continue;
            _creatures.RemoveAt(i);
            return true;
        }
        return false;
    }

    public CreatureState? CreatureById(int id)
    {
        foreach (CreatureState c in _creatures)
            if (c.Id == id) return c;
        return null;
    }

    /// <summary>
    /// Every object standing in the level, in the order it was placed.
    ///
    /// <para><b>One list for every kind</b>, because DK2's object is one type
    /// with a tagged union rather than a family of types — see
    /// <see cref="ObjectState"/> and <c>docs/research/objects.md</c> §7. A list
    /// per kind would have been easier to iterate and would have made "the
    /// gold pile that is also a bed" unrepresentable, which sounds like a gain
    /// until a mechanic needs to ask what is on a tile without knowing what it
    /// is looking for — an imp choosing by <c>PickUpPriority</c> does exactly
    /// that.</para>
    ///
    /// <para>Order is behaviour, for the same reason it is on
    /// <see cref="Creatures"/>: two beds free on one tick are claimed in this
    /// order. Never sorted, never reordered.</para>
    /// </summary>
    public IReadOnlyList<ObjectState> Objects => _objects;

    /// <summary>
    /// Puts an object on a tile.
    ///
    /// <para>Takes a definition id rather than a definition so that a caller
    /// cannot place something the database has never heard of: the lookup is the
    /// validation, and it throws rather than placing a nameless object that
    /// every later rule would have to tolerate.</para>
    /// </summary>
    public ObjectState SpawnObject(
        GameDatabase database, byte objectTypeId, byte ownerPlayerId, int tileIndex)
    {
        if ((uint)tileIndex >= (uint)_tiles.Length)
            throw new ArgumentOutOfRangeException(
                nameof(tileIndex), tileIndex, "Object tile is outside the map.");

        // Refused rather than placed. An object the database has never heard
        // of would be treated as "not a bed, not gold, not food" by every rule
        // that asks a type flag, which is a silent wrong answer rather than a
        // loud one.
        if (database.ObjectById(objectTypeId) is null)
            throw new KeyNotFoundException(
                $"No object definition with id {objectTypeId}.");

        var instance = new ObjectState
        {
            Id = _nextObjectId++,
            ObjectTypeId = objectTypeId,
            OwnerPlayerId = ownerPlayerId,
            TileIndex = tileIndex,
        };

        _objects.Add(instance);
        return instance;
    }

    /// <summary>
    /// Takes an object out of the world, and says whether it was there. Ids are
    /// never reused, exactly as for creatures.
    /// </summary>
    public bool RemoveObject(int id)
    {
        for (int i = 0; i < _objects.Count; i++)
        {
            if (_objects[i].Id != id) continue;
            _objects.RemoveAt(i);
            return true;
        }
        return false;
    }

    public ObjectState? ObjectById(int id)
    {
        foreach (ObjectState o in _objects)
            if (o.Id == id) return o;
        return null;
    }

    public bool HasPlayer(byte playerId)
    {
        foreach (PlayerState p in _players)
            if (p.PlayerId == playerId) return true;
        return false;
    }

    /// <summary>
    /// Builds the initial world for a level: every tile takes its terrain,
    /// owner and flags from the MAP record and its health from that terrain
    /// type's <see cref="TerrainDef.StartingHealth"/>.
    ///
    /// Starting gold and mana are NOT set here — those come from the level's
    /// PLAYERS record, which is not parsed yet (M6). Callers that need them set
    /// them explicitly, and scenarios say so out loud. Gold has to go somewhere
    /// real: a caller places it with <see cref="GoldStore.Deposit"/>, which will
    /// refuse it outright if the player owns no treasury.
    /// </summary>
    public static WorldState FromMap(
        GameDatabase database,
        LevelMap map,
        IEnumerable<byte> playerIds,
        bool fogOfWarEnabled = false)
    {
        var tiles = new TileState[map.Width * map.Height];
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                MapTile source = map[x, y];
                TerrainDef terrain = database.TerrainById(source.TerrainId);
                tiles[(y * map.Width) + x] = new TileState
                {
                    TerrainId = source.TerrainId,
                    OwnerPlayerId = source.PlayerId,
                    Flags = source.Flags,
                    Health = terrain.StartingHealth,
                };
            }
        }

        PlayerState[] players = playerIds
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new PlayerState { PlayerId = id })
            .ToArray();

        return new WorldState(map.Width, map.Height, tiles, players, fogOfWarEnabled);
    }

    /// <summary>Reconstructs a world from a snapshot. Used only by <see cref="WorldSnapshot"/>.</summary>
    internal static WorldState FromParts(
        int width, int height, TileState[] tiles, PlayerState[] players, bool fogOfWarEnabled)
        => new(width, height, tiles, players, fogOfWarEnabled);

    internal TileState[] RawTiles => _tiles;
    internal PlayerState[] RawPlayers => _players;
    internal List<TileWorkOrder> RawPendingWork => _pendingWork;
    internal List<CreatureState> RawCreatures => _creatures;
    internal List<ObjectState> RawObjects => _objects;

    /// <summary>Restores the id counter after a snapshot, so ids stay unique across a save.</summary>
    internal int NextCreatureId
    {
        get => _nextCreatureId;
        set => _nextCreatureId = value;
    }

    /// <summary>Restores the object id counter after a snapshot.</summary>
    internal int NextObjectId
    {
        get => _nextObjectId;
        set => _nextObjectId = value;
    }

    public void HashInto(ref Hash64 hash)
    {
        hash.Add(CurrentTick);
        hash.Add(Width);
        hash.Add(Height);
        hash.Add(FogOfWarEnabled ? (byte)1 : (byte)0);
        hash.Add(_tiles.Length);
        foreach (TileState t in _tiles) t.HashInto(ref hash);
        hash.Add(_players.Length);
        foreach (PlayerState p in _players) p.HashInto(ref hash);
        hash.Add(_pendingWork.Count);
        foreach (TileWorkOrder o in _pendingWork) o.HashInto(ref hash);

        // The id counter is hashed too: two worlds holding identical creatures
        // but disagreeing about the next id will diverge the moment either one
        // spawns, which is exactly the kind of late divergence the gate exists
        // to catch early.
        hash.Add(_creatures.Count);
        hash.Add(_nextCreatureId);
        foreach (CreatureState c in _creatures) c.HashInto(ref hash);

        hash.Add(_objects.Count);
        hash.Add(_nextObjectId);
        foreach (ObjectState o in _objects) o.HashInto(ref hash);
    }
}
