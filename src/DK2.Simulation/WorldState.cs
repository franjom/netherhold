// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The mutable per-tile simulation state, distinct from <see cref="MapTile"/>,
/// which is the immutable file view. Terrain, owner and flags come straight from
/// the MAP record (CONFIRMED); health is seeded from
/// <see cref="TerrainDef.StartingHealth"/> and is what digging, claiming and
/// reinforcing modify (CONFIRMED, docs/research/kwd-terrain-rooms.md); gold held
/// is where a keeper's gold actually is, because every limit DK2 sets on gold is
/// per tile (<see cref="GoldStore"/>).
/// </summary>
public struct TileState
{
    public byte TerrainId;
    public byte OwnerPlayerId;

    /// <summary>
    /// The MAP record's third byte per tile, preserved verbatim — and, for a
    /// bridge, <b>the hazard the bridge spans</b>: 2 for lava, 1 for water. The
    /// byte is 1 on every tile of all 66 shipped maps except bridge tiles over
    /// lava, where it is 2, and the two sets never cross; a sold bridge reverts to
    /// the water or lava under it rather than to floor (owner, 2026-09-03). Q16,
    /// Q64; <see cref="BridgeRules"/> carries the evidence and the confidence.
    /// <para>Written by <see cref="RoomRules.Place"/> when a player builds a
    /// bridge, since the level file only speaks for bridges it shipped with.
    /// Whether the byte carries other meanings on other terrain is UNKNOWN;
    /// nothing reads it except the bridge rules.</para>
    /// </summary>
    public byte Flags;

    /// <summary>Current tile health. Seeded from the terrain type's starting health.</summary>
    public int Health;

    /// <summary>
    /// Gold physically stored on this tile. Non-zero only where
    /// <see cref="GoldStore.Capacity"/> is — a treasury or dungeon-heart tile. A
    /// player's spendable gold is the sum of this over the tiles they own; there
    /// is no player-level gold counter, on purpose (ADR-0015,
    /// <see cref="GoldStore"/>).
    /// </summary>
    public int GoldHeld;

    /// <summary>The store pile's sampled 11-bit heading; null when its creation stream is unknown.</summary>
    public ushort? StoreGoldHeading;

    /// <summary>
    /// Which players have this tile tagged for digging: bit <c>n</c> is set for
    /// player id <c>n</c>. STRONGLY INFERRED that tagging is per player rather
    /// than global — DK2 is multiplayer and each keeper marks and sees their own
    /// dig orders, and the terrain table carries a per-type <c>Taggable</c> flag
    /// rather than a shared tag list. The bitmask is our representation, not a
    /// claim about the original's storage.
    /// </summary>
    public PlayerMask TaggedBy;

    /// <summary>Native tile byte +0x0B bit 0: a concealed Secret Door, independent of dig tags.</summary>
    public bool SecretDoor;

    /// <summary>Keepers with a kind-11 entry here. Rectangle untag removes only kinds 1 and 2.</summary>
    public PlayerMask SecretDoorTasks;

    /// <summary>When this tile was last marked, from <see cref="WorldState.NextTagOrder"/>; 0
    /// unmarked. DK2 files a marked tile at the head of its keeper's task list for the kind
    /// (<c>0x00507bb0</c>), and its dig walk keeps the first of equally near tasks, so the most
    /// recently marked wins a tie (ADR-0288). One number for all markers.</summary>
    public int TagOrder;

    /// <summary>
    /// Keepers that have scanned this tile: bit <c>n</c> belongs to player
    /// id <c>n</c>. This is our per-keeper representation, not a claim about
    /// DK2's packing. DK2 persists its live tile fog bit in saved games, so the
    /// information is simulation state and must be hashed and snapshotted.
    /// </summary>
    public PlayerMask DiscoveredBy;

    /// <summary>The native explored layer (+8 bit 0x2000), independent of the scanned circle.</summary>
    public PlayerMask ExploredBy;

    /// <summary>
    /// Whether this tile is floor that a worker <b>dug out</b> and nobody has
    /// claimed since — the tile an imp goes back for before anything else.
    /// <para>A dig leaving plain unowned path beside a keeper's floor raises a
    /// task that sits <b>first</b> in the imp's whole-map order, ahead of digging,
    /// and is taken as soon as none of the tile's eight neighbours still carries a
    /// dig task; so a corridor is dug through and claimed behind the digging
    /// front. STRONGLY INFERRED (Q154, docs/research/imp-task-priority.md §6 —
    /// rank and the eight-neighbour rule read, the trigger inferred).</para>
    /// <para>Set by <see cref="Systems.ImpTaskSystem"/> when the digger's action ends, if
    /// <see cref="DigTaskOutlivesWallFor"/> still holds its player (ADR-0305); cleared by
    /// any later change of terrain. The original keeps the task in the keeper's list.</para></summary>
    public bool FreshlyDug;

    /// <summary>
    /// The players whose dig or mine task still lists this tile after the wall fell, one bit
    /// per player as in <see cref="TaggedBy"/>. DK2 leaves the kind-1 or kind-2 entry in the
    /// pool: the digger, when its action ends, raises the kind-14 task only if the entry is
    /// still there (<c>0x004655D0</c>, <c>0x00465870</c>), and any imp's dig walk drops an
    /// entry whose tile can no longer be dug (<c>0x00469AF0</c>). ADR-0305.</summary>
    public PlayerMask DigTaskOutlivesWallFor;

    /// <summary>
    /// Health owed to this tile that was too small to apply, carried between
    /// applications; always smaller in magnitude than
    /// <summary>
    /// Ticks left before this tile burns away, or 0 when nothing is burning.
    /// <para>A wooden bridge over lava does not last
    /// (<c>WoodBridgeLifeOnLavaSeconds</c> = 30) and <b>the clock starts when the
    /// bridge is built</b> — not when a creature steps on it, and not as damage to
    /// the tile's health. CONFIRMED (owner, 2026-09-03; Q67).</para>
    /// <para>A separate countdown rather than health decay, because health is what
    /// a rival attacking the bridge reduces and a bridge over water has the same
    /// 2000 health and never burns. Set by <see cref="RoomRules.Place"/>, spent by
    /// <see cref="Systems.BridgeBurnSystem"/>, and reset to zero the moment the
    /// tile stops being that bridge, however it stops.</para>
    /// </summary>
    public int BurnTicksRemaining;

    /// <summary>
    /// Ticks this portal has waited since it last produced a creature, or 0 on every
    /// tile that is not a portal's centre. Managed by <see cref="Systems.PortalSystem"/>.
    /// <para><b>Per portal</b>, CONFIRMED (owner, 2026-09-07): a second portal produced
    /// 8 seconds after the first where the interval was about 60, and one clock cannot
    /// emit twice in 8 s while waiting 60. It sits on the portal's centre tile, which
    /// <see cref="PortalRules.CanProduce"/> guarantees the portal owns, because
    /// <see cref="RoomInstance"/> is derived fresh from the tiles and remembers nothing.</para>
    /// <para><b>Counted up</b>, as DK2 does (<c>room+0x49</c> holds the tick of the last
    /// generation), so a portal blocked for want of a lair banks its wait and produces
    /// the moment the lair is built. It runs only while owned, so claiming an idle
    /// portal costs a full interval — UNKNOWN, Q86.</para></summary>
    public int EntranceIdleTicks;

    /// <summary>
    /// How many of this portal's creatures are still alive, on the same tile as
    /// <see cref="EntranceIdleTicks"/> and zero elsewhere: the <c>n</c> in the arrival
    /// interval. CONFIRMED (Q86, Q508, docs/research/portals-interval.md).
    /// <para>DK2 keeps it at <c>room+0x4D</c>, increments it once per creature generated,
    /// and computes <c>(n² + EntranceGenerationSpeedSeconds)</c> game seconds at
    /// <c>0x004DBBE0</c>. Per portal, not per keeper: a second portal claimed after five
    /// arrivals started again at 25 s, not 50 (owner, 2026-09-07). A death of one of its
    /// creatures lowers it by one (<see cref="PortalRules.ArrivalDied"/>), so a portal speeds
    /// up again as its creatures die. An ownership change resets it: the room type's handler
    /// stamps the clock and zeros the tally
    /// (<c>ARivalTakingAWornPortalGetsItAtTheBaseInterval</c>).</para></summary>
    public int EntranceCreaturesGenerated;

    /// <summary>
    /// A hatchery's clock towards its next egg, on the room's lowest tile index
    /// and zero everywhere else. DK2 keeps it on the room — its room debug dump
    /// prints <c>GameTickLastGenerated</c>, <c>GenerationSpeed</c> and
    /// <c>ChickensGenerated</c> for the Hatchery case — but rooms here are derived
    /// rather than stored (<see cref="RoomInstance"/>), and the lowest tile index
    /// is stable because <c>RoomInstance.TileIndices</c> is kept ascending.
    /// <para>Deviation: a hatchery that grows or is cut so that its lowest tile
    /// changes starts its clock again, where the original's clock belongs to the
    /// room object and survives. Costs at most one laying interval, and only while
    /// a keeper is building.</para>
    /// </summary>
    public int HatchTicks;

    /// <summary>A hatchery's <c>ChickensGenerated</c> (<c>room+0x51</c>), on the same tile as
    /// <see cref="HatchTicks"/>: eggs laid since the room was made. Zeroed by the room's init
    /// <c>0x004DE060</c>, bumped by each egg <c>0x004DE620</c> creates; the finder's 42 needs it
    /// nonzero (<see cref="RoomNeedRules.NoHatcheryFor"/>). The same lowest-tile deviation: growth
    /// below that tile, a merge or its sale loses the count, and a tile that leaves its room
    /// drops it (<see cref="SlabChain.Changed"/>, Q522).</summary>
    public int ChickensGenerated;

    /// <summary>A Combat Pit's <c>+0x53</c>: the turn <c>0x004D7CF0</c> last found a fight on its
    /// floor, on the room's lowest tile index like <see cref="HatchTicks"/> and zero elsewhere. The
    /// spectators' 144 and job 26 read it for the quiet spell after a fight
    /// (<see cref="CombatPitSpectatorRules"/>). The same deviation: a pit whose lowest tile changes
    /// forgets it.</summary>
    public long PitFightSeenTick;

    /// <summary>A Casino's jackpot party, on the room's lowest tile like <see cref="HatchTicks"/>:
    /// <see cref="CasinoJackpotRules.PartyBit"/> is room <c>+0x41</c>, <see cref="CasinoJackpotRules.MusicBit"/>
    /// room <c>+0x3D</c>; <see cref="CasinoPartyStartTick"/> is <c>+0x45</c> (casino.md §8–§9).</summary>
    public byte CasinoParty;

    /// <inheritdoc cref="CasinoParty"/>
    public long CasinoPartyStartTick;

    /// <summary>A Prison's door, on the room's lowest tile like <see cref="HatchTicks"/>: its
    /// <c>Activation</c>, <c>DoorOpen</c> and <c>PrisonJailBroken</c> as the bits of
    /// <see cref="JailBreakRules.Activated"/>, <see cref="JailBreakRules.Open"/> and
    /// <see cref="JailBreakRules.Broken"/>.</summary>
    public byte PrisonDoor;
    public long PrisonDoorAnimationTick;

    /// <summary>A room tile's place in its room's slab chain (<c>FirstSlab</c> to <c>LastSlab</c>,
    /// <c>room+0x2D</c>/<c>+0x2F</c>): the chain is the room's tiles by this, then by tile index.
    /// Zero for the map's own rooms, which DK2 loads in row-major order. Kept by
    /// <see cref="SlabChain"/> (room-slab-chain.md).</summary>
    public int SlabOrder;
    public RoomListPosition RoomListPosition;

    /// <summary>A room's <c>GameTickLastOnChangeSize</c> (<c>room+0x9F</c>), held on each of its tiles:
    /// the tick <see cref="SlabChain"/> last rebuilt the room, which <c>Room_Construct</c> and
    /// <c>Room_OnTilesChanged</c> (<c>0x004E4480</c>) stamp with the game turn. Zero for the map's own
    /// rooms. The finders' full-room messages wait on it (<see cref="RoomNeedRules"/>).</summary>
    public long RoomSizeChangedTick;

    /// <summary>The tick a room's place-holders were last all kicked with its tiles unchanged: the room
    /// changed owner (its type's slot <c>+0x0D</c>). Held on each of its tiles, as
    /// <see cref="RoomSizeChangedTick"/> is; a holder compares both (<see cref="SlabChain.HoldersStamp"/>).</summary>
    public long RoomKickedTick;

    /// <summary>DK2's room ruin, <c>room+0x9A</c> bit 0, held on each tile of a ruined gate since
    /// rooms here are derived from tiles. Set by trigger action 32 through <c>0x004E9130</c> for the
    /// Hero Gate types 18, 20 and 23; it makes a non-Attackable room convertible (<c>0x00454040</c>).
    /// A heart's ruin is <see cref="PlayerState.HeartRuined"/> (hero-parties-and-effect-generators.md).</summary>
    public bool RoomRuined;

    /// <summary>This tile's part of its room's health deficit: the room's <c>Health</c>
    /// (<c>room+0x35</c>) is StartingHealth × Size less the sum over its tiles, kept by
    /// <see cref="RoomHealthRules"/> and <see cref="SlabChain"/>. Zero off a room.</summary>
    public int RoomHealthLost;

    /// <summary>The floor's <c>ManaGain</c> a room tile went on paying when the room was built over
    /// it: DK2's rate word (<c>player+0x10E2</c>) is adjusted by <c>Tile_SetTerrain</c> only when
    /// neither terrain is a Room (<c>0x00455B90</c>), so a room laid on claimed floor keeps the 1,
    /// and a room owned from the start or taken from a rival carries 0 (ADR-0334). Read by <see cref="ManaRules.GainPerSecond"/> on room tiles alone.</summary>
    public byte KeptManaGain;

    /// <summary>Highest player id that fits in <see cref="TaggedBy"/>.</summary>
    public const int MaxTaggingPlayerId = PlayerMask.MaxPlayerId;

    public readonly bool IsTaggedBy(PlayerId playerId) => TaggedBy.Contains(playerId);

    public readonly bool IsTaggedByAnyone => !TaggedBy.IsEmpty;

    public readonly bool IsDiscoveredBy(PlayerId playerId) => DiscoveredBy.Contains(playerId);

    public readonly bool IsExploredBy(PlayerId playerId) => ExploredBy.Contains(playerId);

    public void SetExploredBy(PlayerId playerId) => ExploredBy = ExploredBy.With(playerId);

    public void ClearExploredBy(PlayerId playerId) => ExploredBy = ExploredBy.Without(playerId);

    public void SetDiscoveredBy(PlayerId playerId)
    {
        if (playerId > MaxTaggingPlayerId)
            throw new ArgumentOutOfRangeException(
                nameof(playerId), playerId,
                $"Tile discovery only tracks player ids 0..{MaxTaggingPlayerId}.");

        DiscoveredBy = DiscoveredBy.With(playerId);
    }

    public void SetTaggedBy(PlayerId playerId, bool tagged)
    {
        if (playerId > MaxTaggingPlayerId)
            throw new ArgumentOutOfRangeException(
                nameof(playerId), playerId,
                $"Tile tags only track player ids 0..{MaxTaggingPlayerId}.");

        TaggedBy = tagged ? TaggedBy.With(playerId) : TaggedBy.Without(playerId);
    }

    public readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(TerrainId);
        hash.Add(OwnerPlayerId);
        hash.Add(Flags);
        hash.Add(Health);
        hash.Add(GoldHeld);
        hash.Add(StoreGoldHeading ?? ushort.MaxValue);
        hash.Add(TaggedBy.Bits);
        hash.Add(SecretDoor);
        hash.Add(SecretDoorTasks.Bits);
        hash.Add(DiscoveredBy.Bits);
        hash.Add(ExploredBy.Bits);
        hash.Add(FreshlyDug ? (byte)1 : (byte)0);
        hash.Add(DigTaskOutlivesWallFor.Bits);
        hash.Add(TagOrder);
        hash.Add(BurnTicksRemaining);
        hash.Add(EntranceIdleTicks);
        hash.Add(EntranceCreaturesGenerated);
        hash.Add(HatchTicks);
        hash.Add(ChickensGenerated);
        hash.Add(PitFightSeenTick);
        hash.Add(CasinoParty);
        hash.Add(CasinoPartyStartTick);
        hash.Add(PrisonDoor);
        hash.Add(PrisonDoorAnimationTick);
        hash.Add(SlabOrder);
        hash.Add(RoomListPosition.Value);
        hash.Add(RoomSizeChangedTick);
        hash.Add(RoomKickedTick);
        hash.Add(RoomRuined);
        hash.Add(RoomHealthLost);
        hash.Add(KeptManaGain);
    }
}

/// <summary>
/// Per-player simulation state. <b>Mana is here and gold is not</b>, and the
/// asymmetry is the data's: mana is generated by owned land and spent from a
/// single pool (<c>MaximumManaThreshold</c>,
/// <c>ExcessManaDecreaseRatePerSecond</c>) — a player-level number with no
/// location — while gold is always in a place and every limit the balance table
/// sets on it is per tile, so it lives on tiles (<see cref="GoldStore"/>,
/// ADR-0015).
/// </summary>
public struct PlayerState
{
    public PlayerId PlayerId;
    public int Mana;

    /// <summary>Cumulative one-shot mana loss, player +0x10F2 (keeper-spell-casting.md).</summary>
    public int OneShotManaSpent;
    public bool OneShotReaperUsed;

    /// <summary>Player <c>+0xAA</c>: the turn before which a trap short of mana posts no message
    /// (<c>0x004C1A30</c>, fifteen seconds on from each post); 0 from <c>Player_Initialise</c>.</summary>
    public long TrapNoManaMessageAt;

    /// <summary>Gold this keeper's creatures have mined, DK2's <c>player+0x15A</c> —
    /// <c>GoldMined</c> in the level-statistics struct embedded at <c>+0x142</c>
    /// (`player-object.md`). Trigger condition type 31 compares it.</summary>
    /// <remarks>Counts gold <b>released</b> from the seam, not gold banked: DK2 credits it
    /// at <c>0x004973CF</c> before anything asks whether the imp could carry it, so a full
    /// imp still adds to the total. Only ever added to; nothing spends it.</remarks>
    public int GoldMined;

    /// <summary>The keeper that killed this one, DK2's <c>player+0x132</c> —
    /// <c>KilledByPlayerId</c> in its debug dumper. Zero means alive: condition type 47
    /// returns false outright on zero and otherwise compares it (ADR-0116).</summary>
    /// <remarks><b>Nothing sets it.</b> No keeper can be defeated here yet — a fallen heart
    /// is not even claimable (Q207) — so every keeper reads as alive, which is the answer
    /// DK2 gives for a living keeper too. DK2 stores the killer's <i>object</i> id and
    /// converts it through that object's <c>Number</c>; we store the player id, which is
    /// the same value only while <c>TagId</c> and <c>Number</c> agree (Q271).</remarks>
    public byte KilledByPlayerId;

    /// <summary>DK2's <c>player+0x14A</c>, <c>HeroesDestroyed</c> (weight 50 in the level
    /// score): the heroes this player is credited with killing, which the Heroes Killed HUD box
    /// shows. The death bookkeeping (<c>0x00487C70</c>, <c>0x004882DC</c>) adds one when an
    /// <c>IsGood</c> creature dies, to the player its health source names, or else to the owner
    /// of its first engaged attacker; no owner test, so a keeper's own hero counts and the hero
    /// player can credit itself. Zeroed by <c>Player_Initialise</c>.</summary>
    /// <remarks>The level-end pass <c>0x004BE9E6</c> also counts hero-owned creatures still
    /// dying whose source names the player (<see cref="LevelStatisticsRules.Sample"/>).</remarks>
    public int HeroesDestroyed;

    /// <summary>Slaps this keeper has been counted for, DK2's <c>player+0xEE6</c> —
    /// <c>NumberOfSlaps</c> in its debug dumper. Condition type 71 compares it
    /// (<c>0x004C8491</c>); both shipped records ask for "at least", 11 and 50.</summary>
    /// <remarks><b>Nothing sets it.</b> Slapping is a keeper's own act and no such act
    /// exists here yet, so zero is this simulation's true count rather than a stand-in —
    /// which is why the condition reads the field instead of answering a constant. Whether
    /// the tally counts slaps <i>given</i> or <i>received</i> is UNKNOWN (Q278); the
    /// dumper's name does not say and no writer has been read.</remarks>
    public int SlapCount;

    /// <summary>The creature this keeper is possessing, DK2's <c>player+0x134</c> —
    /// <c>CreaturePossessed</c>. Zero means not possessing, and condition type 73 returns
    /// <b>false outright</b> on zero without reading its comparison
    /// (<c>0x004C8500</c>).</summary>
    /// <remarks>Set and cleared by PossessionRules. Condition73's follower-count
    /// comparison remains unbuilt (Q279).</remarks>
    public int PossessedCreatureId;

    /// <summary>
    /// Research points banked toward <see cref="ResearchTarget"/>, DK2's
    /// <c>player+0x20F</c>. Credited whether or not a target is selected, because the
    /// original writes the field before it looks at the target, and reset to zero when
    /// a target is delivered (<see cref="ResearchTreeRules.Advance"/>).
    /// <para>Not implemented: the adder's subtract path. A negative credit is
    /// clamp-subtracted rather than allowed below zero, but nothing has been found that
    /// passes one, so what that path is for is UNKNOWN (Q218).</para></summary>
    public int ResearchPoints;

    /// <summary>What this keeper is researching, DK2's <c>player+0x213</c>: a
    /// <b>1-based</b> index into <see cref="Data.Model.GlobalVariables.Research"/>, and 0
    /// for nothing (research-tree.md).</summary>
    public int ResearchTarget;

    /// <summary>
    /// The earliest tick at which this keeper may be handed a free imp.
    /// <para><b>A deadline, not a countdown</b> — the whole of Q56. DK2 keeps one
    /// field on the keeper and writes it in exactly one place, immediately after a
    /// free imp is made, as <c>now + TimeBeforeFreeImpGeneratedSeconds</c>. Nothing
    /// resets it, pauses it, or touches it while the keeper is at full strength, so
    /// a keeper who has been at four imps for a minute has a deadline a minute in
    /// the past: the first tick they drop below <c>MinimumImpThreshold</c> the
    /// replacement arrives <b>at once</b>, and only the ones after it are five
    /// seconds apart. See <see cref="ImpSupplyRules"/> and
    /// docs/research/imps-and-mana.md.</para>
    /// </summary>
    public GameTick NextFreeImpTick;

    /// <summary>
    /// The tick this keeper's next payday falls due. A deadline, like
    /// <see cref="NextFreeImpTick"/>, and the original keeps it the same way: one
    /// field on the player, compared against now, rewritten as
    /// <c>now + PayDayFrequencySeconds × ticksPerSecond</c> the moment it fires.
    /// Zero is <i>no payday</i>, as in DK2: the first is set
    /// <c>TimeBeforeFirstPayDaySeconds</c> out when the keeper gains its first
    /// non-worker creature (<see cref="PaydayRules.ArmOnGain"/>).</summary>
    public GameTick NextPaydayTick;
    /// <summary>Start of the current payday interval (player +0xB4F), also set when first armed.</summary>
    public GameTick LastPaydayTick;

    /// <summary>The level's mutable unlock rows for this keeper. Trigger action 6
    /// changes an existing row but never creates one (ADR-0161).</summary>
    public List<AvailabilityState> Availabilities;

    /// <summary>DK2's <c>CreatureEntranceInfo</c> (<c>player+0xB57</c>, 18-byte rows): a row
    /// for each creature type the portal has noticed, in creature-id order, carrying the
    /// two modifiers its pick applies (<see cref="PortalChoiceRules"/>, ADR-0272).</summary>
    public List<EntranceInfoState> EntranceInfo;

    /// <summary>CreatureEntranceInfo row +1 (player +0xB58): types already introduced by a Portal arrival.</summary>
    public List<byte> IntroducedCreatureTypes;

    /// <summary>DK2's manufacture points, <c>player+0x217</c>, a word (ADR-0203).</summary>
    public int ManufacturePoints;

    /// <summary>DK2's <c>NumberOfManufacturingCreatures</c>, <c>player+0x8A</c> (a dword): raised by
    /// the Manufacture enter hook and lowered by its leave hook on the keeper who owns the tile the
    /// maker stands on, with no floor. The decay reads it (<see cref="ManufactureRules.Decay"/>).</summary>
    public int ManufacturingCreatures;

    /// <summary>What this keeper has learned from torture (<c>0x004C2B60</c>'s table, ADR-0230):
    /// bit <c>(k − 3) × 8 + victim's player id</c> for reward <c>k</c> of 3–10.</summary>
    public ulong InformationGiven;

    /// <summary>DK2's manufacture list, <c>player+0x219</c>: the orders waiting, head first.</summary>
    public List<ManufactureOrder> ManufactureQueue;

    /// <summary>Crates made, DK2's <c>ItemsManufactured</c> (<c>player+0x162</c>).</summary>
    public int ItemsManufactured;

    /// <summary>The rest of the level-statistics block (<see cref="LevelStatisticsRules"/>).</summary>
    public PlayerLevelStatistics Statistics;

    /// <summary>The win sequence's player fields (<see cref="FinaleRules"/>).</summary>
    public PlayerFinale Finale;

    /// <summary>DK2's blueprint list, <c>player+0xA21</c>: the keeper's doors and traps waiting for
    /// a crate, in the order they were placed, which is the order task kinds 9 and 10 take them
    /// (ADR-0214).</summary>
    public List<BlueprintRef> Blueprints;

    /// <summary>DK2's <c>Status</c> at <c>player+0x19A</c>: one of <see cref="PlayerStatus"/>,
    /// written once by <see cref="WorldState.EndGame"/> and never changed after (ADR-0162).</summary>
    public byte Status;

    /// <summary>The tick <see cref="Status"/> left <see cref="PlayerStatus.Playing"/>. DK2
    /// snapshots the end-of-level statistics at that moment; this is the one of them a
    /// deterministic replay cannot recompute.</summary>
    public GameTick StatusTick;

    /// <summary>The dungeon heart object's health, <c>object+0x40</c>. Starts at
    /// <c>DungeonHeartObjectHealth</c>, regenerates once a second while above zero, and
    /// at zero the heart is dead: <see cref="KilledByPlayerId"/> is stamped from
    /// <see cref="HeartLastAttackerPlayerId"/> and the room ruins on the next turn.</summary>
    public int HeartHealth;

    /// <summary>The player whose blow last hit the heart, <c>object+0x36</c>; becomes
    /// <see cref="KilledByPlayerId"/> when the heart dies.</summary>
    public byte HeartLastAttackerPlayerId;

    /// <summary>When the heart died — DK2's <c>GameTickDied</c> at <c>player+0xEE2</c>.</summary>
    public GameTick HeartDiedTick;

    /// <summary>Bit 0 of the heart room's <c>+0x9A</c>: set by the room's own frame when it
    /// finds no heart object. A ruined heart stores no gold and can be claimed (Q93, Q263).</summary>
    public bool HeartRuined;

    /// <summary>The low-mana warning clocks and the removal-scan deadline, DK2's player
    /// <c>+0x76</c> bits 0 and 8, <c>+0x9E</c>, <c>+0xA6</c> and <c>+0xB48</c>; advanced by
    /// <see cref="Systems.ManaSystem"/> on every payment (imp-mana-removal.md, ADR-0277).</summary>
    public ManaPressureState ManaPressure;

    /// <summary>The Call to Arms: DK2's player <c>+0x76</c> bit 2 with <c>+0x138</c> to
    /// <c>+0x13E</c> (call-to-arms.md, ADR-0279).</summary>
    public CallToArmsState CallToArms;

    /// <summary>DK2's player <c>+0x76</c> bit 10, <c>PortalsOpen</c> in its dumper
    /// (<c>0x004BDCEA</c>), held inverted so a new player starts open, as every Level 2 capture
    /// does from tick 4. Only action 37 writes it (<c>0x004C892D</c>); while closed the creator
    /// <c>0x004DC9A0</c> refuses, so a due roll draws and restamps but nothing comes (ADR-0314).</summary>
    public bool PortalsClosed;

    /// <summary>The tick at which this keeper's scripted camera path (action 31) stops, 0 when
    /// none was started; while it lies ahead, condition 72 answers false. DK2 holds the camera's
    /// mode <c>0x12</c> and frame counter instead, advanced on the wall clock (camera-paths.md,
    /// ADR-0496).</summary>
    public long CameraPathEndTick;

    /// <summary>Player <c>+0xF4A</c>, the count of this keeper's Casinos with a jackpot party on:
    /// while nonzero, job 9 and the Casino's enter hook skip the purse test and the finder takes its
    /// party branch (casino.md §9).</summary>
    public int CasinoParties;

    /// <summary>Player <c>+0xF4E</c>: set when a 41 or 99 for a Graveyard is first admitted
    /// (<c>0x004C5650</c>), refusing every later one (<c>0x004C5530</c>). Nothing clears it and no
    /// code initialises it; ours starts at false, the heap's usual zero (HYPOTHESIS, Q544). Saved.</summary>
    public bool GraveyardWantedPosted;

    /// <summary>
    /// The Hand of Evil: the <b>things</b> this keeper is holding, in pick-up order.
    /// Things, not creatures (ADR-0075) — DK2 keeps one array of thing ids on the
    /// player record and branches on the thing's kind, so a creature and a chicken
    /// share the slots (the owner has watched four chickens held at once); our ids
    /// are separate spaces, so each slot carries which it is.
    /// <para><b>A stack</b>: the original appends at <c>NumberOfThingsInHand</c> and
    /// its drop handler reads back from the same index before decrementing, so A
    /// then B puts down B first. Capacity is <c>MaxNumberOfThingsInHand</c>, shipped
    /// at 64 (<see cref="HandRules"/>); the creature's half of the relation is
    /// <see cref="CreatureState.InHand"/> (ADR-0059). Never null —
    /// <see cref="WorldState"/> gives every keeper a list on creation.</para></summary>
    public List<HandItem> Hand;

    /// <summary>Which players this one is allied with, one bit per player id — DK2's eight bytes
    /// at <c>player+0x1AE</c>, indexed there by the player's compact number at <c>+0x10FC</c>.
    /// Symmetric: the make- and break-alliance handlers (<c>0x00513FF0</c>, <c>0x00513F60</c>)
    /// always write both sides, and nothing else writes the array. A player is never its own ally
    /// here; the original's predicate <c>0x004BF3D0</c> answers yes for the same player before it
    /// reads the array, which is what <see cref="WorldState.AreAllied"/> does.</summary>
    /// <remarks>DK2 keeps a second eight-byte array at <c>+0x1B6</c> beside it, cleared whenever
    /// an alliance is made; what it holds is UNKNOWN and nothing here stands in for it. One byte
    /// covers ids 0..7, which is every id the campaign uses (<see cref="PlayerIds"/>) and exactly
    /// as many as the original's array holds.</remarks>
    public byte AlliedWith;

    public readonly void HashInto(ref Hash64 hash)
    {
        hash.Add(PlayerId.Value);
        hash.Add(AlliedWith);
        hash.Add(Mana);
        hash.Add(OneShotManaSpent);
        hash.Add(OneShotReaperUsed);
        hash.Add(TrapNoManaMessageAt);
        hash.Add(GoldMined);
        hash.Add(KilledByPlayerId);
        hash.Add(HeroesDestroyed);
        hash.Add(SlapCount);
        hash.Add(PossessedCreatureId);
        hash.Add(ResearchPoints);
        hash.Add(ResearchTarget);
        hash.Add(NextFreeImpTick);
        hash.Add(NextPaydayTick);
        hash.Add(LastPaydayTick);
        hash.Add(Status);
        hash.Add(StatusTick);
        hash.Add(HeartHealth);
        hash.Add(HeartLastAttackerPlayerId);
        hash.Add(HeartDiedTick);
        hash.Add(HeartRuined ? (byte)1 : (byte)0);
        hash.Add(ManaPressure.WarningDeadline);
        hash.Add(ManaPressure.WarningStage);
        hash.Add(ManaPressure.WarningsSuppressed);
        hash.Add(ManaPressure.RemovalEnabled);
        hash.Add(ManaPressure.ScanDeadline);
        hash.Add(CallToArms.On);
        hash.Add(CallToArms.ObjectId);
        hash.Add(CallToArms.X);
        hash.Add(CallToArms.Y);
        hash.Add(CallToArms.ManaTick);
        hash.Add(PortalsClosed ? (byte)1 : (byte)0);
        hash.Add(CameraPathEndTick);
        hash.Add(CasinoParties);
        hash.Add(GraveyardWantedPosted ? (byte)1 : (byte)0);
        hash.Add(Availabilities.Count);
        foreach (AvailabilityState availability in Availabilities)
            availability.HashInto(ref hash);
        hash.Add(EntranceInfo.Count);
        foreach (EntranceInfoState entrance in EntranceInfo)
            entrance.HashInto(ref hash);
        hash.Add(IntroducedCreatureTypes.Count);
        foreach (byte creatureType in IntroducedCreatureTypes) hash.Add(creatureType);
        hash.Add(ManufacturePoints);
        hash.Add(ManufacturingCreatures);
        hash.Add(InformationGiven);
        hash.Add(ItemsManufactured);
        Statistics.HashInto(ref hash);
        Finale.HashInto(ref hash);
        hash.Add(ManufactureQueue.Count);
        foreach (ManufactureOrder order in ManufactureQueue) order.HashInto(ref hash);
        hash.Add(Blueprints.Count);
        foreach (BlueprintRef blueprint in Blueprints) blueprint.HashInto(ref hash);
        hash.Add(Hand.Count);
        foreach (HandItem held in Hand)
        {
            hash.Add((byte)held.Kind);
            hash.Add(held.Id);
        }
    }
}

/// <summary>One mutable room, creature, door, trap or spell availability row.</summary>
/// <param name="Upgraded">DK2's per-entry <c>Upgraded</c> dword: <c>@0xD</c> of a 17-byte
/// spell entry, <c>@5</c> of a room, door or trap entry. Only research sets it, and only
/// for spells (research-tree.md §2); a spell cast reads it to fire the bonus shot.</param>
/// <summary>One <c>CreatureEntranceInfo</c> row, which exists once the type is noticed
/// (row <c>+0x02</c> = 1): the boost (<c>+0x06</c>) and the sack block (<c>+0x0E</c>), both
/// in 1/4096 and both taken down by 1.0 after every pick.</summary>
public readonly record struct EntranceInfoState(byte CreatureTypeId, int Boost, int Block)
{
    internal void HashInto(ref Hash64 hash)
    {
        hash.Add(CreatureTypeId);
        hash.Add(Boost);
        hash.Add(Block);
    }
}

public readonly record struct AvailabilityState(AvailabilityKind Kind, int TypeId, int Value, bool Upgraded = false,
    long LastCastTick = 0, long LastCheckedTick = 0)
{
    internal void HashInto(ref Hash64 hash)
    {
        hash.Add((ushort)Kind);
        hash.Add(TypeId);
        hash.Add(Value);
        hash.Add(Upgraded ? (byte)1 : (byte)0);
        hash.Add(LastCastTick);
        hash.Add(LastCheckedTick);
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
/// One slot of the Hand of Evil. DK2 needs no discriminator — creatures and
/// objects share one thing table and one id space, and its handlers branch on a
/// kind field inside the thing — but ours are separate lists with independent
/// ids, so the kind rides on the slot.
/// </summary>
public readonly record struct HandItem(HandItemKind Kind, int Id)
{
    public static HandItem Of(CreatureState creature) =>
        new(HandItemKind.Creature, creature.Id);

    public static HandItem Of(ObjectState thing) => new(HandItemKind.Object, thing.Id);

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

    /// <summary>The last structure mask computed for this world (<see cref="StructureBlocks.For"/>).
    /// Derived state, never saved or hashed.</summary>
    internal StructureBlocks.Cache StructureCache { get; } = new();

    /// <summary>The last whole-map room sweep for this world (<see cref="RoomIndex.Build(WorldState, GameDatabase)"/>).
    /// Derived state, never saved or hashed.</summary>
    internal RoomIndex.Cache RoomSweepCache { get; } = new();
    internal const int TremorSlotCount = 8;
    internal TremorState[] TremorSlots { get; } = new TremorState[TremorSlotCount];
    public IReadOnlyList<TremorState> Tremors => TremorSlots;
    internal const int HeroSlotCount = 6;
    /// <summary>The six marked-creature records trigger action 42 writes (<see cref="HeroSlot"/>).</summary>
    internal HeroSlot[] HeroSlotArray { get; } = new HeroSlot[HeroSlotCount];
    public IReadOnlyList<HeroSlot> HeroSlots => HeroSlotArray;
    /// <summary>The border cursors of the rooms with flames, in room order (<see cref="RoomBorderCursor"/>).</summary>
    internal List<RoomBorderCursor> RoomBorderCursorList { get; } = [];
    public IReadOnlyList<RoomBorderCursor> RoomBorderCursors => RoomBorderCursorList;
    private readonly PlayerState[] _players; // kept sorted by PlayerId, for stable hashing
    internal RoomBuildWaveState?[] RoomBuildWaveSlots { get; }
    public IReadOnlyList<RoomBuildWaveState?> RoomBuildWaves => RoomBuildWaveSlots;
    private readonly List<TileWorkOrder> _pendingWork = [];
    private readonly List<CreatureState> _creatures = [];
    private readonly List<CreatureState> _creaturesByOwnership = [];
    private readonly List<ObjectState> _objects = [];
    private readonly List<ActionPointState> _actionPoints = [];
    private readonly DoorRegistry _doors = new();
    private readonly TrapRegistry _traps = new();
    private readonly List<NavStamp> _navStamps = [];
    private int _nextChainOrder;
    private int _nextTileLinkOrder;
    private readonly List<HeroPartyState> _heroParties = [];
    private readonly List<EffectGeneratorState> _effectGenerators = [];
    private readonly List<TempleSacrificeState> _templeSacrifices = [];
    private readonly List<GraveyardState> _graveyards = [];
    private int _nextCreatureId = 1;
    private SimRandom? _creatureIdentityRandom;
    private int _nextObjectId = 1;
    private readonly List<ShotState> _shots = [];
    private int _nextShotId = 1;

    private WorldState(
        int width, int height, TileState[] tiles, PlayerState[] players, bool fogOfWarEnabled)
    {
        Width = width;
        Height = height;
        _tiles = tiles;
        _players = players;
        RoomBuildWaveSlots = new RoomBuildWaveState?[players.Length * RoomBuildWaveRules.SlotsPerPlayer];
        FogOfWarEnabled = fogOfWarEnabled;

        // One place, so no construction path can produce a keeper without a hand:
        // PlayerState is a struct in an array, and a caller using an object
        // initialiser would have had to know to supply the list.
        for (int i = 0; i < _players.Length; i++)
        {
            _players[i].Hand ??= [];
            _players[i].Availabilities ??= [];
            _players[i].EntranceInfo ??= [];
            _players[i].IntroducedCreatureTypes ??= [];
            _players[i].ManufactureQueue ??= [];
            _players[i].Blueprints ??= [];
        }

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
    public int TileIndex(TilePos p) => (p.Y * Width) + p.X;
    public TilePos TilePosition(int index) => new(index % Width, index / Width);

    public ref TileState TileAtIndex(int index) => ref _tiles[index];

    /// <summary>Every tile, read-only, for a reader that compares the whole map at once rather
    /// than tile by tile. No route back into state: the span cannot be written.</summary>
    public ReadOnlySpan<TileState> Tiles => _tiles;

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
    public bool Contains(TilePos p) => Contains(p.X, p.Y);

    public IReadOnlyList<PlayerId> PlayerIds => _players.Select(p => p.PlayerId).ToArray();

    public int PlayerCount => _players.Length;

    public ref PlayerState PlayerAtIndex(int index) => ref _players[index];

    internal ref RoomBuildWaveState? RoomBuildWave(PlayerId playerId, RoomBuildSlot slot)
    {
        for (int i = 0; i < _players.Length; i++)
            if (_players[i].PlayerId == playerId)
                return ref RoomBuildWaveSlots[i * RoomBuildWaveRules.SlotsPerPlayer + (int)slot];
        throw new KeyNotFoundException($"No player with id {playerId} in this world.");
    }

    public ref PlayerState Player(PlayerId playerId)
    {
        for (int i = 0; i < _players.Length; i++)
            if (_players[i].PlayerId == playerId)
                return ref _players[i];
        throw new KeyNotFoundException($"No player with id {playerId} in this world.");
    }

    /// <summary>Returns a keeper's availability state, or 1 when no row exists.</summary>
    public int Availability(PlayerId playerId, AvailabilityKind kind, int typeId)
    {
        foreach (AvailabilityState entry in Player(playerId).Availabilities)
            if (entry.Kind == kind && entry.TypeId == typeId)
                return entry.Value;
        return 1;
    }

    /// <summary>Changes an existing availability row. A missing row is a successful no-op.</summary>
    public void SetAvailability(PlayerId playerId, AvailabilityKind kind, int typeId, int value)
    {
        ref PlayerState player = ref Player(playerId);
        for (int i = 0; i < player.Availabilities.Count; i++)
        {
            AvailabilityState entry = player.Availabilities[i];
            if (entry.Kind != kind || entry.TypeId != typeId) continue;
            player.Availabilities[i] = entry with { Value = value };
            return;
        }
    }

    /// <summary>A row's <see cref="AvailabilityState.Upgraded"/>, or false when no row
    /// exists — DK2's <c>0x004BB0D0</c> returns 0 for an absent entry.</summary>
    public bool IsUpgraded(PlayerId playerId, AvailabilityKind kind, int typeId)
    {
        foreach (AvailabilityState entry in Player(playerId).Availabilities)
            if (entry.Kind == kind && entry.TypeId == typeId)
                return entry.Upgraded;
        return false;
    }

    /// <summary>Sets an existing row's <see cref="AvailabilityState.Upgraded"/>; a missing
    /// row is a no-op, as DK2's writer <c>0x004BB070</c> is.</summary>
    public void SetUpgraded(PlayerId playerId, AvailabilityKind kind, int typeId, bool upgraded)
    {
        ref PlayerState player = ref Player(playerId);
        for (int i = 0; i < player.Availabilities.Count; i++)
        {
            AvailabilityState entry = player.Availabilities[i];
            if (entry.Kind != kind || entry.TypeId != typeId) continue;
            player.Availabilities[i] = entry with { Upgraded = upgraded };
            return;
        }
    }

    /// <summary>Whether any row of one kind has the requested state.</summary>
    public bool HasAvailability(PlayerId playerId, AvailabilityKind kind, int value) =>
        Player(playerId).Availabilities.Any(a => a.Kind == kind && a.Value == value);

    /// <summary>Ends the game for one player — DK2's <c>0x004BE630</c>, which returns at
    /// once if <c>Status</c> is already set, so the first verdict stands. Returns whether
    /// this call was the one that ended it (ADR-0162).</summary>
    public bool EndGame(GameDatabase database, PlayerId playerId, byte status)
    {
        ref PlayerState player = ref Player(playerId);
        if (PlayerStatus.HasEnded(player.Status)) return false;
        player.Status = status;
        player.StatusTick = CurrentTick;
        LevelStatisticsRules.Sample(this, database, playerId, status);
        GameEnded?.Invoke(playerId, status);
        return true;
    }

    /// <summary>The negative half of DK2's heart health change (<c>0x004DA200</c>, win-and-defeat.md
    /// §3.1): a blow on an Arch Pillar or a shot on the heart is redirected here by the health
    /// routine itself, the source is stamped first (<c>+0x36</c>, the killer at death) and the delta
    /// lands flat — no multiplier, no armour. At zero the heart is dead: <c>KilledByPlayerId</c> and
    /// <c>GameTickDied</c> are stamped as the object death handler does (<c>0x0049B5E0</c>, type 13
    /// arm); the ruin and the verdict follow in <see cref="Systems.DungeonHeartSystem"/>. Not modelled
    /// (2026-09-23): the <c>DungeonHeartIsInvulnerable</c> player flag, whose writer is unread.</summary>
    public bool DamageHeart(PlayerId playerId, int amount, PlayerId attackerPlayerId)
    {
        ref PlayerState player = ref Player(playerId);
        if (player.HeartHealth <= 0) return false;
        player.HeartLastAttackerPlayerId = attackerPlayerId.Value;
        player.HeartHealth -= amount;
        HeartHealthChanged?.Invoke(new HeartHealthChange(playerId, player.HeartHealth));
        if (player.HeartHealth > 0) return false;

        player.HeartHealth = 0;
        player.KilledByPlayerId = attackerPlayerId.Value;
        LevelStatisticsRules.CreditHeartDestroyed(this, playerId, attackerPlayerId);
        player.HeartDiedTick = CurrentTick;
        return true;
    }

    /// <summary>The positive half of the same routine, as the heart room's frame calls it
    /// (<c>0x004D9EFB</c>): the owner's own tag is the source, so a regenerating heart's last
    /// source reads as its keeper until the next blow. The caller has already capped the amount.</summary>
    public void RegenerateHeart(PlayerId playerId, int amount)
    {
        ref PlayerState player = ref Player(playerId);
        player.HeartLastAttackerPlayerId = playerId.Value;
        player.HeartHealth += amount;
        HeartHealthChanged?.Invoke(new HeartHealthChange(playerId, player.HeartHealth));
    }

    /// <summary>Observation only; health changes request the original extra animation advance.</summary>
    public event Action<HeartHealthChange>? HeartHealthChanged;

    /// <summary>Whether <paramref name="playerId"/> counts <paramref name="otherId"/> a friend —
    /// the original's <c>0x004BF3D0</c>, which answers yes for the same player without reading
    /// the array and otherwise returns the byte at <c>player+0x1AE+other</c>. The door update
    /// asks it of every creature standing at a door (doors-and-traps.md).</summary>
    public bool AreAllied(PlayerId playerId, byte otherId) =>
        playerId == otherId
        || (otherId < 8 && (Player(playerId).AlliedWith & (1 << otherId)) != 0);

    /// <summary>Make or break an alliance. <b>Always symmetric</b>: the original's two handlers
    /// and trigger action 18 (<c>0x00519376</c>) write both players' arrays, and nothing else
    /// writes them, so a one-sided alliance is a state DK2 cannot reach.</summary>
    public void SetAllied(PlayerId playerId, byte otherId, bool allied)
    {
        if (playerId == otherId) return;
        if (playerId >= 8 || otherId >= 8)
            throw new ArgumentOutOfRangeException(nameof(playerId), "The alliance array holds eight players.");
        Write(ref Player(playerId), otherId, allied);
        Write(ref Player(otherId), playerId.Value, allied);

        static void Write(ref PlayerState player, byte other, bool allied)
        {
            if (allied) player.AlliedWith |= (byte)(1 << other);
            else player.AlliedWith &= (byte)~(1 << other);
        }
    }

    /// <summary>
    /// The level's trigger flags and timers. One bank per level, never per
    /// player — see <see cref="TriggerState"/> for the four readings behind
    /// that.
    /// </summary>
    public TriggerState Triggers { get; } = new();
    public PlayerMessageQueue Messages { get; } = new();

    /// <summary>
    /// Work applications waiting to be resolved by
    /// <see cref="Systems.TileWorkSystem"/>, in the order they were queued. A
    /// list, not a set or a dictionary: order is behaviour (ADR-0010). Two imps
    /// working the same seam in a different order can end up carrying different
    /// amounts of gold, so the queue is FIFO and never sorted or deduplicated.
    /// Produced by <see cref="Systems.ImpTaskSystem"/>; the scenario harness and
    /// tests may also queue directly, and say so in their reports.
    /// </summary>
    public IReadOnlyList<TileWorkOrder> PendingWork => _pendingWork;

    public void QueueWork(TileWorkOrder order)
    {
        if ((uint)order.TileIndex >= (uint)_tiles.Length)
            throw new ArgumentOutOfRangeException(
                nameof(order), order.TileIndex, "Work order references a tile outside the map.");
        _pendingWork.Add(order);
    }

    public void QueueWork(int x, int y, TileWorkKind kind, PlayerId playerId)
    {
        if (!Contains(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) is outside the map.");
        _pendingWork.Add(new TileWorkOrder((y * Width) + x, kind, playerId));
    }

    internal void ClearPendingWork() => _pendingWork.Clear();

    /// <summary>
    /// Every creature in the level, in spawn order. The order is behaviour, not
    /// bookkeeping (ADR-0010): creatures are updated in it, so two imps reaching
    /// for the same job get it in this order. Never sorted, never reordered —
    /// removing a creature must preserve the order of the rest.
    /// </summary>
    public IReadOnlyList<CreatureState> Creatures => _creatures;

    /// <summary>Most recently acquired first; held and dying creatures remain owned.
    /// Corpses are separate things in DK2 and are excluded here (ADR-0273).</summary>
    [Dk2Behavior(Confidence.StronglyInferred, "docs/research/creature-ownership.md")]
    public IEnumerable<CreatureState> OwnedCreatures(PlayerId playerId)
    {
        foreach (CreatureState creature in _creaturesByOwnership)
            if (creature.OwnerPlayerId == playerId && CreatureStateMachine.HasState(creature))
                yield return creature;
    }

    /// <summary>Changes the owner and places the creature first in its new owner's list.</summary>
    public void ChangeCreatureOwner(CreatureState creature, PlayerId playerId)
    {
        if (!_creatures.Contains(creature))
            throw new ArgumentException("Creature does not belong to this world.", nameof(creature));
        if (creature.OwnerPlayerId == playerId) return;
        creature.OwnerPlayerId = playerId.Value;
        _creaturesByOwnership.Remove(creature);
        _creaturesByOwnership.Insert(0, creature);
    }

    /// <summary>Changes the owner as DK2's owner change does, which also arms the new owner's
    /// payday clock on its first non-worker (<c>0x0048D24E</c>, <see cref="PaydayRules.ArmOnGain"/>).</summary>
    public void ChangeCreatureOwner(GameDatabase database, CreatureState creature, PlayerId playerId)
    {
        ChangeCreatureOwner(creature, playerId);
        PaydayRules.ArmOnGain(this, database, creature);
    }

    internal IReadOnlyList<CreatureState> CreatureOwnershipOrder => _creaturesByOwnership;

    internal void RestoreCreatureOwnershipOrder(IReadOnlyList<int> ids)
    {
        if (ids.Count != _creatures.Count || ids.Distinct().Count() != ids.Count)
            throw new InvalidDataException("Invalid creature ownership order.");
        var ordered = new List<CreatureState>(ids.Count);
        foreach (int id in ids)
            ordered.Add(CreatureById(id)
                ?? throw new InvalidDataException("Ownership order names a missing creature."));
        _creaturesByOwnership.Clear();
        _creaturesByOwnership.AddRange(ordered);
    }

    /// <summary>
    /// Puts a creature on the map, at the centre of the given tile. Every
    /// in-simulation producer goes through here —
    /// <see cref="Systems.PortalSystem"/>, <see cref="Systems.ImpSupplySystem"/>,
    /// <see cref="Systems.PrisonSystem"/> and <see cref="ThingProjection"/> — and
    /// so do tests and scenario files, which place creatures directly and say so
    /// in their reports.
    /// </summary>
    public CreatureState SpawnCreature(
        GameDatabase database, byte creatureTypeId, PlayerId ownerPlayerId, TilePos at)
    {
        if (!Contains(at))
            throw new ArgumentOutOfRangeException(nameof(at), at, "Spawn tile is outside the map.");

        var creature = new CreatureState
        {
            Id = _nextCreatureId++,
            CreatureTypeId = creatureTypeId,
            OwnerPlayerId = ownerPlayerId.Value,
            PosX = CreatureState.CentreOf(at.X),
            PosY = CreatureState.CentreOf(at.Y),
            LastPosX = CreatureState.CentreOf(at.X),
            LastPosY = CreatureState.CentreOf(at.Y),
            OriginTileIndex = (at.Y * Width) + at.X,
            DroppedTick = CurrentTick.Value, // the counter starts at zero (0x00487711)

            // The database is a parameter for this one line: a creature spawned at
            // zero health dies on the tick it arrives, so there is deliberately no
            // overload that leaves health out.
            Health = database.CreatureById(creatureTypeId).Hp,

            GetPaid = PaydayRules.TooCloseToPayday(this, database, ownerPlayerId),

            // 0x0048790C, a word. A table without variable 203 seeds 0, which never fires.
            CasinoGamblingDuration = database.Variables.TryGet(VariableTypeId.GamblingDelayBeforeBecomingFearlessSeconds,
                out int fearlessDelay) ? (ushort)fearlessDelay : 0,
        };

        _creatures.Add(creature);
        _creaturesByOwnership.Insert(0, creature);
        if (_creatureIdentityRandom is not null)
            CreatureIdentityRules.Initialize(this, creature, _creatureIdentityRandom);
        // After GetPaid and identity assignment, as in the original constructor.
        PaydayRules.ArmOnGain(this, database, creature);
        creature.LinkWorld = this;
        Relink(creature);
        return creature;
    }

    internal void BindCreatureIdentityRandom(SimRandom random)
    {
        _creatureIdentityRandom = random;
        foreach (CreatureState creature in _creatures)
            CreatureIdentityRules.Initialize(this, creature, random);
    }

    /// <summary>
    /// Takes a creature out of the world, and says whether it was there.
    /// <para>Not death but the end of it: dying in DK2 is a process — a creature
    /// collapses at zero health, lies rescuable for
    /// <c>CreatureDyingStateDurationSeconds</c>, and only then dies (Q99,
    /// CONFIRMED, ADR-0051). <see cref="Systems.CreatureHealthSystem"/> runs that
    /// process and calls this at the end of it, and for a corpse when it rots away
    /// (a corpse is kept as the creature, with no state id: ADR-0206).</para>
    /// <para>Ids are never reused: <c>_nextCreatureId</c> only climbs and a
    /// snapshot restores the counter rather than deriving it.</para></summary>
    public bool RemoveCreature(int id)
    {
        for (int i = 0; i < _creatures.Count; i++)
        {
            if (_creatures[i].Id != id) continue;
            // The death bookkeeping runs for every freed creature (0x004B7A36), so a follower that
            // leaves, is sacrificed or is removed any other way lowers its leader's count.
            PossessionRules.CreatureFreed(this, _creatures[i]);
            // Creature_DeathBookkeeping clears the first world slot holding it (0x00488396..0x004883CC).
            for (int s = 0; s < HeroSlotCount; s++)
                if (HeroSlotArray[s].CreatureId == id) { HeroSlotArray[s] = default; break; }
            _creaturesByOwnership.Remove(_creatures[i]);
            _creatures[i].LinkWorld = null;
            _creatures[i].TileLink = ThingTileLink.None;
            _creatures.RemoveAt(i);
            // DK2's death handler unlinks the dead from every engagement list it was on; a
            // creature that was fighting it notices the empty target on its next fight process.
            foreach (CreatureState other in _creatures) Combat.CombatRules.Unregister(other, id);
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
    /// Every object standing in the level, in the order it was placed. One list
    /// for every kind, because DK2's object is one type with a tagged union rather
    /// than a family of types (<see cref="ObjectState"/>, ADR-0054,
    /// docs/research/objects.md §7): a mechanic often has to ask what is on a tile
    /// without knowing what it is looking for, as an imp choosing by
    /// <c>PickUpPriority</c> does.
    /// <para>Order is behaviour, as on <see cref="Creatures"/> — two beds freed on
    /// one tick are claimed in this order. Never sorted, never reordered.</para>
    /// </summary>
    public IReadOnlyList<ObjectState> Objects => _objects;

    /// <summary>
    /// Puts an object on a tile. Takes a definition id rather than a definition so
    /// the lookup is the validation: it throws rather than placing a nameless
    /// object every later rule would have to tolerate.
    /// </summary>
    public ObjectState SpawnObject(
        GameDatabase database, byte objectTypeId, PlayerId ownerPlayerId, int tileIndex)
    {
        if ((uint)tileIndex >= (uint)_tiles.Length)
            throw new ArgumentOutOfRangeException(
                nameof(tileIndex), tileIndex, "Object tile is outside the map.");

        // Refused rather than placed: an unknown object would answer "not a bed,
        // not gold, not food" to every rule that asks a type flag, which is a
        // silent wrong answer rather than a loud one.
        if (database.ObjectById(objectTypeId) is null)
            throw new KeyNotFoundException(
                $"No object definition with id {objectTypeId}.");

        var instance = new ObjectState
        {
            Id = _nextObjectId++,
            ObjectTypeId = objectTypeId,
            OwnerPlayerId = ownerPlayerId.Value,
            TileIndex = tileIndex,
            Health = database.ObjectById(objectTypeId)!.Hp,
            ChickenCanEscape = objectTypeId == HatcheryRules.ChickenObjectId,
            ProcessState = objectTypeId is HatcheryRules.ChickenObjectId or RatRules.RatObjectId ? database.ObjectById(objectTypeId)!.StartState : (byte)0,
        };

        if (objectTypeId is HatcheryRules.ChickenObjectId or HatcheryRules.EggObjectId or RatRules.RatObjectId)
        {
            instance.PosX = CreatureState.CentreOf(tileIndex % Width);
            instance.PosY = CreatureState.CentreOf(tileIndex / Width);
        }

        _objects.Add(instance);
        instance.LinkWorld = this;
        Relink(instance);
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
            _objects[i].LinkWorld = null;
            _objects[i].TileLink = ThingTileLink.None;
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

    /// <summary>
    /// The level's action points in THINGS-file order. The original walks its
    /// collection linearly for several queries, so this is never sorted by id.
    /// </summary>
    public IReadOnlyList<ActionPointState> ActionPoints => _actionPoints;
    public TileHighlightState TileHighlights { get; } = new();

    internal void AddActionPoint(ActionPointState actionPoint) => _actionPoints.Add(actionPoint);

    /// <summary>Shots in flight, in creation order (shots.md). Ids climb like creatures' and are
    /// never reused.</summary>
    public IReadOnlyList<ShotState> Shots => _shots;

    public int NextShotId { get => _nextShotId; internal set => _nextShotId = value; }

    /// <summary>The next <see cref="TileState.SlabOrder"/> to hand out. It only climbs, so a
    /// stamped tile always comes after every tile stamped before it.</summary>
    public int NextSlabOrder { get; internal set; } = 1;
    internal long NextRoomListPosition { get; set; } = 1;

    /// <summary>The next <see cref="CreatureState.HeldPlaceSerial"/>: a room's list takes each claim at
    /// its head, so the highest serial is the head.</summary>
    public long NextPlaceClaim { get; internal set; } = 1;

    /// <summary>The next <see cref="TileState.TagOrder"/>.</summary>
    public int NextTagOrder { get; internal set; } = 1;

    /// <summary>DK2's game mode byte <c>0x0075AD4B</c>; 1, the campaign, unless the host says otherwise.</summary>
    public byte GameMode { get; set; } = TransferCreatureRules.CampaignMode;

    /// <summary>Profile writes made during play, for the host (<see cref="CampaignCarry"/>).</summary>
    public CampaignCarry Carry;

    /// <summary>The player this machine plays, DK2's world slot <c>+0x264</c>: the campaign win
    /// runs its finale for this player only (<c>0x004BEDC0</c>). The host sets it; a level starts
    /// with the first keeper.</summary>
    public PlayerId LocalPlayerId { get; set; } = new(DK2.Data.Model.PlayerIds.FirstKeeper);

    /// <summary>Called once by <see cref="EndGame"/> with the verdict it wrote; the simulation runs
    /// the win sequence from it, inside the same call as DK2's <c>0x004BE630</c> does.</summary>
    internal Action<PlayerId, byte>? GameEnded;

    /// <summary>Kind-11 pool insertion order and retained middle-slot holders, independent per keeper.</summary>
    public Dictionary<(PlayerId Player, TilePos Tile), SecretDoorTaskEntry> SecretDoorEntries { get; } = [];

    internal SecretDoorTaskEntry SecretDoorEntry(PlayerId player, TilePos tile)
    {
        if (!SecretDoorEntries.TryGetValue((player, tile), out SecretDoorTaskEntry? entry))
            SecretDoorEntries.Add((player, tile), entry = new SecretDoorTaskEntry { Order = NextTagOrder++ });
        return entry;
    }

    internal void RemoveSecretDoorEntry(PlayerId player, TilePos tile)
    {
        Tile(tile).SecretDoorTasks = Tile(tile).SecretDoorTasks.Without(player);
        SecretDoorEntries.Remove((player, tile));
    }

    internal List<ShotState> RawShots => _shots;

    public ShotState? ShotById(int id)
    {
        foreach (ShotState s in _shots)
            if (s.Id == id) return s;
        return null;
    }

    /// <summary>A new shot at a position, with the next id; the caller fills the rest.</summary>
    public ShotState SpawnShot(byte shotTypeId, PlayerId ownerPlayerId, int x, int y)
    {
        var shot = new ShotState { Id = _nextShotId++, ShotTypeId = shotTypeId, OwnerPlayerId = ownerPlayerId.Value, PosX = x, PosY = y };
        _shots.Add(shot);
        shot.LinkWorld = this;
        Relink(shot);
        return shot;
    }

    public bool RemoveShot(int id)
    {
        for (int i = 0; i < _shots.Count; i++)
        {
            if (_shots[i].Id != id) continue;
            _shots[i].LinkWorld = null;
            _shots[i].TileLink = ThingTileLink.None;
            _shots.RemoveAt(i);
            return true;
        }
        return false;
    }

    /// <summary>The standing doors, the level's in THINGS-file order and then those placed in play.</summary>
    public IReadOnlyList<DoorState> Doors => _doors.All;

    /// <summary>Adds a door with the next id, which <see cref="DoorState.Id"/> keeps for its life.</summary>
    internal void AddDoor(DoorState door)
    {
        door.TileLink = NewLink(Contains(door.X, door.Y) ? (door.Y * Width) + door.X : -1);
        _doors.Add(door);
    }

    /// <summary>The standing door with this id, or null once it is gone.</summary>
    public DoorState? DoorById(int id) => _doors.ById(id);

    /// <summary>The standing door on a tile, or null. One door to a tile, as placement allows.</summary>
    public DoorState? DoorAt(int x, int y) => _doors.At(x, y);

    /// <summary>Takes a dead door out of the world (DK2's removal <c>0x004B7BD0</c>).</summary>
    internal void RemoveDoor(DoorState door) => _doors.Remove(door);

    /// <summary>Navigation bytes a writer other than the terrain left on a tile, which outlive the
    /// writer until the tile's terrain is set again (<c>Tile_SetTerrain</c> <c>0x0044FC40</c> writes
    /// the terrain's byte on every call). Two writers: a door's removal leaves 0 over water or lava
    /// (<c>0x004A66F0(0)</c>, Q600), and the Heart's builder leaves 3 on its chain tile 12
    /// (<c>0x004DB700</c>, Q601). Ours ends a stamp when the terrain id changes
    /// (<see cref="PruneNavStamps"/>); a <c>Tile_SetTerrain</c> that keeps the id ends DK2's too.</summary>
    public IReadOnlyList<NavStamp> NavStamps => _navStamps;

    internal void Stamp(int tile, byte navByte) => _navStamps.Add(new NavStamp(tile, _tiles[tile].TerrainId, navByte));

    /// <summary>Drops the stamps whose tile's terrain has changed since.</summary>
    internal void PruneNavStamps()
    {
        // Runs every turn: an index loop, not RemoveAll, so a standing stamp allocates nothing.
        for (int i = _navStamps.Count - 1; i >= 0; i--)
            if (_tiles[_navStamps[i].Tile].TerrainId != _navStamps[i].TerrainId) _navStamps.RemoveAt(i);
    }

    /// <summary>Bumped by every add and removal, so a per-tile cache of doors knows when to
    /// rebuild. Derived, not state: neither hashed nor saved.</summary>
    public int DoorsVersion => _doors.Version;

    /// <summary>The standing traps, the level's in THINGS-file order and then those placed in play.</summary>
    public IReadOnlyList<TrapState> Traps => _traps.All;

    internal void AddTrap(TrapState trap)
    {
        trap.TileLink = NewLink(Contains(trap.X, trap.Y) ? (trap.Y * Width) + trap.X : -1);
        _traps.Add(trap);
    }

    /// <summary>The standing trap with this id, or null.</summary>
    public TrapState? TrapById(int id) => _traps.ById(id);

    /// <summary>The first trap of <paramref name="owner"/> on the tile, or null: <c>0x00453700(3, owner)</c>
    /// over the tile's chain. A tile holds one trap.</summary>
    public TrapState? TrapAt(int x, int y, PlayerId owner) => _traps.At(x, y, owner);

    internal void RemoveTrap(TrapState trap) => _traps.Remove(trap);

    /// <summary>The last tile-chain link given (<see cref="CreatureState.ChainOrder"/>).</summary>
    internal int NextChainOrder { get => _nextChainOrder; set => _nextChainOrder = value; }

    /// <summary>The last mixed-class link order given (<see cref="ThingTileLink.Order"/>).</summary>
    internal int NextTileLinkOrder { get => _nextTileLinkOrder; set => _nextTileLinkOrder = value; }

    /// <summary>The tile whose mixed-class chain holds a creature: its position's tile while on the
    /// map, -1 while held or carried (layer 2, <c>0x004B4F60</c>).</summary>
    private int ChainTileOf(CreatureState creature) =>
        creature.InHand || creature.CarriedByCreatureId >= 0 ? -1 : TileIndexAt(creature.PosX, creature.PosY);

    private int ChainTileOf(ObjectState thing) =>
        thing.InHand || thing.CarriedByCreatureId >= 0 || (uint)thing.TileIndex >= (uint)_tiles.Length ? -1 : thing.TileIndex;

    // DK2 takes the tile as the position's bits 12 and up (0x004B505C); off the map is off the chain here.
    private int TileIndexAt(int x, int y) => Contains(x >> 12, y >> 12) ? ((y >> 12) * Width) + (x >> 12) : -1;

    private ThingTileLink NewLink(int tile) => tile < 0 ? ThingTileLink.None : new(tile, ++_nextTileLinkOrder);

    // 0x004B5020: a thing that changes tile moves to the head of the new tile's chain; one that does
    // not keeps its place. Leaving the map (hand, carrier) unlinks; returning links at the head.
    private ThingTileLink Moved(ThingTileLink link, int tile) => tile == link.TileIndex ? link : NewLink(tile);

    /// <summary>Keeps the creature's <see cref="CreatureState.TileLink"/> current; its position, hand
    /// and carry setters call this, so no caller has to. A diagonal crossing written PosX then PosY
    /// relinks at each write and takes two orders (one if the corner tile is off the map), so merging
    /// or reordering a caller's X and Y writes can change the hashed <see cref="ThingTileLink.Order"/>
    /// values; the chain's relative order is unaffected.</summary>
    internal void Relink(CreatureState creature)
    {
        if (ReferenceEquals(creature.LinkWorld, this)) creature.TileLink = Moved(creature.TileLink, ChainTileOf(creature));
    }

    internal void Relink(ObjectState thing)
    {
        if (ReferenceEquals(thing.LinkWorld, this)) thing.TileLink = Moved(thing.TileLink, ChainTileOf(thing));
    }

    internal void Relink(ShotState shot)
    {
        if (ReferenceEquals(shot.LinkWorld, this)) shot.TileLink = Moved(shot.TileLink, TileIndexAt(shot.PosX, shot.PosY));
    }

    /// <summary>A creature that dies leaves the chain (<c>0x0048846F</c>) and its body is a new thing
    /// constructed on layer 0 (<c>0x004B0F9B</c>); ours keeps the creature as the body (ADR-0206), so
    /// it is linked afresh at the head of its tile.</summary>
    internal void RelinkAsBody(CreatureState creature)
    {
        if (ReferenceEquals(creature.LinkWorld, this)) creature.TileLink = NewLink(ChainTileOf(creature));
    }

    /// <summary>Restores a loaded thing's ownership without relinking: its link was saved.</summary>
    internal void AttachLoaded(CreatureState creature) => creature.LinkWorld = this;

    internal void AttachLoaded(ObjectState thing) => thing.LinkWorld = this;

    internal void AttachLoaded(ShotState shot) => shot.LinkWorld = this;

    /// <summary>The last door and trap ids given, restored by a snapshot so ids stay unique.</summary>
    internal int NextDoorId { get => _doors.NextId; set => _doors.NextId = value; }

    internal int NextTrapId { get => _traps.NextId; set => _traps.NextId = value; }

    /// <summary>The level's unspawned hero-party records in THINGS-file order.</summary>
    public IReadOnlyList<HeroPartyState> HeroParties => _heroParties;

    internal void AddHeroParty(HeroPartyState party) => _heroParties.Add(party);

    /// <summary>The level's effect generators in THINGS-file order. No behaviour exists yet.</summary>
    public IReadOnlyList<EffectGeneratorState> EffectGenerators => _effectGenerators;

    internal void AddEffectGenerator(EffectGeneratorState generator) => _effectGenerators.Add(generator);

    /// <summary>Persistent offering histories, ordered by their Temple room anchor.</summary>
    public IReadOnlyList<TempleSacrificeState> TempleSacrifices => _templeSacrifices;

    internal TempleSacrificeState TempleSacrificeAt(int anchorTileIndex, PlayerId ownerPlayerId)
    {
        int index = 0;
        while (index < _templeSacrifices.Count
               && _templeSacrifices[index].AnchorTileIndex < anchorTileIndex)
            index++;

        if (index < _templeSacrifices.Count
            && _templeSacrifices[index].AnchorTileIndex == anchorTileIndex)
        {
            TempleSacrificeState existing = _templeSacrifices[index];
            if (existing.OwnerPlayerId != ownerPlayerId)
                throw new InvalidOperationException("A Temple anchor cannot belong to two players.");
            return existing;
        }

        var created = new TempleSacrificeState(anchorTileIndex, ownerPlayerId);
        _templeSacrifices.Insert(index, created);
        return created;
    }

    /// <summary>Persistent decomposition tallies, ordered by their Graveyard room anchor.</summary>
    public IReadOnlyList<GraveyardState> Graveyards => _graveyards;

    internal GraveyardState GraveyardAt(int anchorTileIndex, PlayerId ownerPlayerId)
    {
        int index = 0;
        while (index < _graveyards.Count && _graveyards[index].AnchorTileIndex < anchorTileIndex)
            index++;

        if (index < _graveyards.Count && _graveyards[index].AnchorTileIndex == anchorTileIndex)
        {
            GraveyardState existing = _graveyards[index];
            if (existing.OwnerPlayerId != ownerPlayerId)
                throw new InvalidOperationException("A Graveyard anchor cannot belong to two players.");
            return existing;
        }

        var created = new GraveyardState(anchorTileIndex, ownerPlayerId);
        _graveyards.Insert(index, created);
        return created;
    }

    public bool HasPlayer(PlayerId playerId)
    {
        foreach (PlayerState p in _players)
            if (p.PlayerId == playerId) return true;
        return false;
    }

    /// <summary>
    /// Builds the initial world for a level: every tile takes its terrain, owner
    /// and flags from the MAP record and its health from that terrain type's
    /// <see cref="TerrainDef.StartingHealth"/>.
    /// <para>Not implemented: starting gold and mana. The level's PLAYERS record
    /// carries them and the parser reads them, but nothing wires them into a world
    /// yet, so callers that need them set them explicitly and scenarios say so.
    /// Gold has to go somewhere real — a caller deposits it with
    /// <see cref="GoldStore.Deposit"/>, which refuses outright if the player owns
    /// no treasury.</para>
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
                // The loader sets every tile through Tile_SetTerrain (0x0051EC60 -> 0x0044FC40),
                // which owns a non-ownable terrain to the neutral player (0x0044FDBF, Q514).
                tiles[(y * map.Width) + x] = new TileState
                {
                    TerrainId = source.TerrainId,
                    OwnerPlayerId = terrain.IsOwnable ? source.PlayerId : Data.Model.PlayerIds.Neutral,
                    Flags = source.Flags,
                    Health = terrain.StartingHealth,
                };
            }
        }

        PlayerState[] players = playerIds
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new PlayerState
            {
                PlayerId = id,
                HeartHealth = Data.Model.PlayerIds.IsKeeper(id)
                    ? database[VariableTypeId.DungeonHeartObjectHealth]
                    : 0,
                Availabilities = database.Variables.AvailabilitiesOf(id)
                    .Select(a => new AvailabilityState((AvailabilityKind)a.Kind, a.TypeId, a.Value))
                    .ToList(),
            })
            .ToArray();

        var world = new WorldState(map.Width, map.Height, tiles, players, fogOfWarEnabled);
        RoomLists.Initialize(world, database);

        // The terrain loader explores a tile for its owner, independently of sight.
        foreach (PlayerState player in players)
            for (int i = 0; i < tiles.Length; i++)
                if (tiles[i].OwnerPlayerId == player.PlayerId)
                    tiles[i].SetExploredBy(player.PlayerId);

        // The loader inserts a map room's tiles one by one (0x004C5C30), so the Prison's slot +0x09
        // sees it at one tile and turns Activation on (0x004E2B30, SlabChain).
        foreach (RoomInstance room in RoomIndex.Build(world, database))
            if (room.Definition.TypeId == RoomTypeId.Prison)
                JailBreakRules.DoorOf(world, room) |= JailBreakRules.Activated;

        return world;
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
    internal List<ActionPointState> RawActionPoints => _actionPoints;
    internal List<DoorState> RawDoors => _doors.Raw;
    internal List<NavStamp> RawNavStamps => _navStamps;
    internal List<TrapState> RawTraps => _traps.Raw;
    internal List<HeroPartyState> RawHeroParties => _heroParties;
    internal List<EffectGeneratorState> RawEffectGenerators => _effectGenerators;
    internal List<TempleSacrificeState> RawTempleSacrifices => _templeSacrifices;
    internal List<GraveyardState> RawGraveyards => _graveyards;

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
        foreach (TremorState tremor in TremorSlots) tremor.HashInto(ref hash);
        foreach (HeroSlot slot in HeroSlotArray) slot.HashInto(ref hash);
        hash.Add(RoomBorderCursorList.Count);
        foreach (RoomBorderCursor cursor in RoomBorderCursorList) cursor.HashInto(ref hash);
        hash.Add(_tiles.Length);
        foreach (TileState t in _tiles) t.HashInto(ref hash);
        hash.Add(NextSlabOrder);
        hash.Add(NextRoomListPosition);
        hash.Add(NextPlaceClaim);
        hash.Add(NextTagOrder);
        hash.Add(GameMode);
        Carry.HashInto(ref hash);
        hash.Add(LocalPlayerId.Value);
        hash.Add(SecretDoorEntries.Count);
        foreach (var pair in SecretDoorEntries.OrderBy(e => e.Key.Player.Value)
            .ThenBy(e => e.Key.Tile.Y).ThenBy(e => e.Key.Tile.X))
        {
            hash.Add(pair.Key.Player.Value);
            hash.Add(pair.Key.Tile.X);
            hash.Add(pair.Key.Tile.Y);
            hash.Add(pair.Value.Order);
            foreach (int holder in pair.Value.Holders) hash.Add(holder);
        }
        hash.Add(_players.Length);
        foreach (PlayerState p in _players) p.HashInto(ref hash);
        foreach (RoomBuildWaveState? wave in RoomBuildWaveSlots)
        {
            hash.Add(wave.HasValue ? (byte)1 : (byte)0);
            if (wave is RoomBuildWaveState active) active.HashInto(ref hash);
        }
        hash.Add(_pendingWork.Count);
        foreach (TileWorkOrder o in _pendingWork) o.HashInto(ref hash);

        // The id counter is hashed too: two worlds holding identical creatures but
        // disagreeing about the next id diverge the moment either one spawns.
        hash.Add(_creatures.Count);
        hash.Add(_nextCreatureId);
        foreach (CreatureState c in _creatures) c.HashInto(ref hash);
        foreach (CreatureState c in _creaturesByOwnership) hash.Add(c.Id);

        hash.Add(_objects.Count);
        hash.Add(_nextObjectId);
        foreach (ObjectState o in _objects) o.HashInto(ref hash);

        hash.Add(_shots.Count);
        hash.Add(_nextShotId);
        foreach (ShotState s in _shots) s.HashInto(ref hash);

        hash.Add(_actionPoints.Count);
        foreach (ActionPointState actionPoint in _actionPoints) actionPoint.HashInto(ref hash);
        TileHighlights.HashInto(ref hash);

        hash.Add(_doors.All.Count);
        hash.Add(_doors.NextId);
        foreach (DoorState door in _doors.All) door.HashInto(ref hash);
        hash.Add(_navStamps.Count);
        foreach (NavStamp stamp in _navStamps)
        {
            hash.Add(stamp.Tile);
            hash.Add(stamp.TerrainId);
            hash.Add(stamp.Byte);
        }

        hash.Add(_traps.All.Count);
        hash.Add(_traps.NextId);
        hash.Add(_nextChainOrder);
        hash.Add(_nextTileLinkOrder);
        foreach (TrapState trap in _traps.All) trap.HashInto(ref hash);

        hash.Add(_heroParties.Count);
        foreach (HeroPartyState party in _heroParties) party.HashInto(ref hash);

        hash.Add(_effectGenerators.Count);
        foreach (EffectGeneratorState generator in _effectGenerators) generator.HashInto(ref hash);

        hash.Add(_templeSacrifices.Count);
        foreach (TempleSacrificeState temple in _templeSacrifices) temple.HashInto(ref hash);

        hash.Add(_graveyards.Count);
        foreach (GraveyardState graveyard in _graveyards) graveyard.HashInto(ref hash);

        // The trigger banks are level state, not level data: an action record
        // writes a flag and a later condition reads it, so two worlds that
        // disagree about a flag diverge as soon as that condition is evaluated.
        Triggers.HashInto(ref hash);
        Messages.HashInto(ref hash);
    }
}

/// <summary>One of <see cref="WorldState.NavStamps"/>: the byte left on a tile and the terrain it
/// was left over.</summary>
public readonly record struct NavStamp(int Tile, byte TerrainId, byte Byte);
