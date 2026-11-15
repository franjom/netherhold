// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.ObjectModel;
using DK2.Data.Model;
using DK2.Simulation.Players;

namespace DK2.AI.Players;

/// <summary>
/// The campaign player's strategy parameters (campaign player plan, task 5). Every value is test
/// policy for this controller, not a claim about DK2's own keepers. <see cref="WorkerTypes"/> is
/// public definition data (which creature types dig), supplied by the host from the database.
/// Call <see cref="Validate"/> after setting init values; <see cref="CampaignPlayer"/> does.
/// </summary>
public sealed record CampaignPlayerOptions
{
    private readonly HashSet<CreatureTypeId> _workers;
    private HashSet<ObjectTypeId>? _specials;

    public CampaignPlayerOptions(PlayerRunId runId, PlayerId player, IEnumerable<CreatureTypeId> workerTypes)
    {
        if (!runId.IsValid) throw new ArgumentException("The player needs a run id.", nameof(runId));
        RunId = runId;
        Player = player;
        _workers = new HashSet<CreatureTypeId>(workerTypes ?? throw new ArgumentNullException(nameof(workerTypes)));
        WorkerTypes = new ReadOnlyCollection<CreatureTypeId>([.. _workers]);
    }

    public PlayerRunId RunId { get; }
    public PlayerId Player { get; }

    /// <summary>Creature types that dig and claim; never picked up to fight.</summary>
    public IReadOnlyList<CreatureTypeId> WorkerTypes { get; }

    public bool IsWorker(CreatureTypeId type) => _workers.Contains(type);

    /// <summary>Special box types to activate; null activates any offered special.</summary>
    public IReadOnlyList<ObjectTypeId>? SpecialTypes
    {
        get => _specials is null ? null : new ReadOnlyCollection<ObjectTypeId>([.. _specials]);
        init => _specials = value is null ? null : new HashSet<ObjectTypeId>(value);
    }

    public bool AllowsSpecial(ObjectTypeId type) => _specials is null || _specials.Contains(type);

    /// <summary>Gold one Treasury tile holds (definition data, the host's <c>MaxGoldPerTreasuryTile</c>);
    /// null when unknown, and then no Treasury grows for want of room.</summary>
    public int? GoldPerTreasuryTile { get; init; }

    /// <summary>Gold an own Dungeon Heart holds (definition data from the host); null counts none.</summary>
    public int? HeartGold { get; init; }

    /// <summary>The level a Combat Pit fighter stops gaining at (<c>CombatPitMaxExperienceLevel</c>, variable 192,
    /// 8 as shipped; combat-pit.md section 7): definition data the host reads off the level's variables.</summary>
    public int CombatPitMaxLevel { get; init; } = 8;

    /// <summary>The room facts (stage 3 plan, Task 3a): definition data generated from the game's rules,
    /// loaded by the host before tick 0. <see cref="KeeperKnowledge.None"/> sizes every room by
    /// <see cref="RoomSide"/> alone, as before Task 3a.</summary>
    public KeeperKnowledge Knowledge { get; init; } = KeeperKnowledge.None;

    /// <summary>The strategy's <see cref="PlayerStrategy.AttractionTargets"/> at the start.</summary>
    public IReadOnlyDictionary<RoomTypeId, int> AttractionTargets { get; init; } = new Dictionary<RoomTypeId, int>();

    /// <summary>The strategy's <see cref="PlayerStrategy.SlotTargets"/> at the start.</summary>
    public IReadOnlyDictionary<RoomTypeId, int> SlotTargets { get; init; } = new Dictionary<RoomTypeId, int>();

    /// <summary>The strategy's <see cref="PlayerStrategy.TileTargets"/> at the start.</summary>
    public IReadOnlyDictionary<RoomTypeId, int> TileTargets { get; init; } = new Dictionary<RoomTypeId, int>();

    /// <summary>Ticks between ordinary decisions.</summary>
    public int AdvanceTicks { get; init; } = 4;

    /// <summary>Rooms to keep, in the order they are built.</summary>
    public IReadOnlyList<RoomTypeId> Rooms { get; init; } = [RoomTypeId.Treasury, RoomTypeId.Lair, RoomTypeId.Hatchery];

    /// <summary>A room is built as a square of at least this side; <see cref="Knowledge"/> and the targets raise it (Task 3a).</summary>
    public int RoomSide { get; init; } = 3;

    /// <summary>A build the observation still does not show after this long is given up for another square.</summary>
    public int PendingRoomTicks { get; init; } = 400;

    /// <summary>At most this many of the player's own dig marks stand at once.</summary>
    public int MaxOutstandingTags { get; init; } = 8;

    /// <summary>After a drop, no fighter is lifted again for this long.</summary>
    public int DropCooldownTicks { get; init; } = 40;

    /// <summary>A fighter already this close (tiles) to the enemy is left to fight.</summary>
    public int EngagedTiles { get; init; } = 4;

    /// <summary>With no gain for this long while work is pending, try another way.</summary>
    public int StallWindowTicks { get; init; } = 1200;

    /// <summary>Tries after a stall, without a gain between them, before the controller reports a limitation.</summary>
    public int RecoveryAttempts { get; init; } = 3;

    /// <summary>At most this many actions per decision (the host's per-decision bound).</summary>
    public int MaxActionsPerDecision { get; init; } = 4;

    /// <summary>The strategy a new <see cref="CampaignPlayer"/> starts with: these options' rooms, room
    /// side, mark bound and advance, gold-first digging, engaging fighters and specials on, then the
    /// <see cref="Playbook"/>'s rooms and values.</summary>
    public PlayerStrategy Strategy
    {
        get
        {
            var strategy = new PlayerStrategy
            {
                Rooms = Rooms,
                RoomSide = RoomSide,
                MaxOutstandingTags = MaxOutstandingTags,
                AdvanceTicks = AdvanceTicks,
                AttractionTargets = AttractionTargets,
                SlotTargets = SlotTargets,
                TileTargets = TileTargets,
            };
            return Playbook?.Apply(strategy) ?? strategy;
        }
    }

    /// <summary>The level's playbook (stage 3 plan, Task 3b), applied over these options' strategy; null for none.
    /// Definition data, loaded and reviewed by the host before tick 0.</summary>
    public Playbook? Playbook { get; init; }

    public void Validate()
    {
        Positive(AdvanceTicks, nameof(AdvanceTicks));
        Positive(RoomSide, nameof(RoomSide));
        Positive(PendingRoomTicks, nameof(PendingRoomTicks));
        Positive(MaxOutstandingTags, nameof(MaxOutstandingTags));
        Positive(StallWindowTicks, nameof(StallWindowTicks));
        Positive(MaxActionsPerDecision, nameof(MaxActionsPerDecision));
        if (DropCooldownTicks < 0) throw new ArgumentOutOfRangeException(nameof(DropCooldownTicks));
        if (EngagedTiles < 0) throw new ArgumentOutOfRangeException(nameof(EngagedTiles));
        if (RecoveryAttempts < 0) throw new ArgumentOutOfRangeException(nameof(RecoveryAttempts));
        if (Rooms is null) throw new ArgumentNullException(nameof(Rooms));
        if (GoldPerTreasuryTile is <= 0) throw new ArgumentOutOfRangeException(nameof(GoldPerTreasuryTile));
        if (HeartGold is < 0) throw new ArgumentOutOfRangeException(nameof(HeartGold));
        if (Knowledge is null) throw new ArgumentNullException(nameof(Knowledge));

        static void Positive(int value, string name)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(name, value, "Must be positive.");
        }
    }
}
