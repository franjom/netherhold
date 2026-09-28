// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DK2.Harness;

/// <summary>Starting resources for one player. Mana goes straight onto the player; gold has to be
/// stored somewhere, so it is deposited into the player's treasury and dungeon-heart tiles, and a
/// run whose player has nowhere to put it says so rather than conjuring a gold counter
/// (<see cref="DK2.Simulation.GoldStore"/>).</summary>
public sealed class ScenarioPlayer
{
    public byte Id { get; set; }
    public int Gold { get; set; }
    public int Mana { get; set; }
}

/// <summary>One block of room tiles laid down before the run starts.
/// <para><b>Placement is the level author's, not a player's.</b> Nothing is charged for, no ground
/// rule is applied beyond the room's own placeable-on flags, and no imp builds it — the same
/// standing <see cref="ScenarioCreature"/> has. A scenario that wants to watch a keeper pay for a
/// room uses a <c>build</c> command instead.</para></summary>
public sealed class ScenarioRoom
{
    /// <summary>A ROOMS record name, e.g. <c>Treasury</c>.</summary>
    public string Type { get; set; } = "";

    /// <summary>Top-left corner, as <c>[x, y]</c>.</summary>
    public List<int> At { get; set; } = [];

    /// <summary>Extent in tiles, as <c>[w, h]</c>. Defaults to a single tile.</summary>
    public List<int> Size { get; set; } = [];

    /// <summary>Whose room it is.</summary>
    public byte Player { get; set; }
}

/// <summary>One creature placed on the map before the run starts.
/// <para><b>Placement is the harness's, not DK2's.</b> The original's imps come out of a dungeon
/// heart or a spell and cost mana; none of that machinery exists, so a scenario that wants workers
/// says where they stand and the report says that is what happened.</para></summary>
public sealed class ScenarioCreature
{
    /// <summary>A CREATURES record name, e.g. <c>Imp</c>.</summary>
    public string Type { get; set; } = "";

    /// <summary>Where it starts, as <c>[x, y]</c>.</summary>
    public List<int> At { get; set; } = [];

    /// <summary>Whose creature it is.</summary>
    public byte Player { get; set; }

    /// <summary>Health to start with. Omit it for the record's full <c>Hp</c>.
    /// <para>Here because nothing in a scenario can hurt a creature — no combat, no command that
    /// wounds one — so the only way to observe anything downstream of damage is to place a creature
    /// already wounded. <c>health: 0</c> is how a run watches a creature lie on the floor for
    /// <c>CreatureDyingStateDurationSeconds</c> and then die (Q99).</para>
    /// <para><b>Nullable rather than defaulting 0 to "full"</b>: null says "the record's" and every
    /// number including 0 says itself. A small positive value is <i>not</i> a route to the dying
    /// state — with no damage anywhere a creature at 1 health stays at 1, and below
    /// <c>CreatureSleepsWhenBelowPercentHealth</c> it goes to bed and heals back to
    /// full.</para></summary>
    public int? Health { get; set; }

    /// <summary>Helpings of food owed, for the same reason <see cref="Health"/> exists: nothing in
    /// a scenario can make a creature hungry in a reasonable number of ticks.
    /// <para><b>And for the prison it is the only way.</b> A Goblin's <c>HungerRate</c> is 180 game
    /// seconds and a cell turns it to bone in 83, so no prisoner ever gets hungry <i>in</i> a
    /// prison; what makes the mechanic reachable is that <c>HungerNeed</c> accumulates over a
    /// creature's whole life and is paid off only by eating, so anything dragged in after a few
    /// minutes on the map is already hungry (docs/research/prison.md). This field is that history,
    /// stated rather than simulated; null says "not hungry".</para></summary>
    public int? Hunger { get; set; }
}

/// <summary>One object placed on the map before tick 0 — a chicken, a pile of gold.
/// <para>Here because a keeper's hand can now hold one (ADR-0075) but nothing in a scenario can
/// lay one: a hatchery lays chickens on its own schedule, and waiting for one is not the thing
/// under test when the thing under test is what happens after it is dropped.</para></summary>
public sealed class ScenarioObject
{
    /// <summary>An OBJECTS record name, e.g. <c>Chicken</c>.</summary>
    public string Type { get; set; } = "";

    /// <summary>Where, as <c>[x, y]</c>.</summary>
    public List<int> At { get; set; } = [];

    /// <summary>Whose it is.</summary>
    public byte Player { get; set; }
}

/// <summary>Initial conditions, applied in this order: the shipped level's own THINGS when
/// <see cref="PlacedThings"/> is set, then rooms, then gold and mana, then creatures, then objects.
/// Rooms come before gold because gold needs a treasury to go into.</summary>
public sealed class ScenarioSetup
{
    public List<ScenarioPlayer> Players { get; set; } = [];

    /// <summary>Room tiles to lay down before tick 0.</summary>
    public List<ScenarioRoom> Rooms { get; set; } = [];

    /// <summary>Creatures to place before tick 0.</summary>
    public List<ScenarioCreature> Creatures { get; set; } = [];

    /// <summary>Objects on the floor before tick 0.</summary>
    public List<ScenarioObject> Objects { get; set; } = [];

    /// <summary>Places the level's own THINGS records — the creatures, objects and rooms the
    /// shipped map carries — before tick 0. Opt-in and off by default, because turning it on for an
    /// existing scenario changes what that scenario is measuring. Only meaningful with a
    /// <c>campaign:</c> map; a synthetic map has no THINGS file.</summary>
    public bool PlacedThings { get; set; }
}

/// <summary>A player command scheduled for one tick. Real input: it goes through the same
/// <see cref="DK2.Simulation.InputFrame"/> the game would use.</summary>
public sealed class ScenarioCommand
{
    public int AtTick { get; set; }
    public byte Player { get; set; }

    /// <summary>Tiles to mark for digging, as <c>[[x, y], …]</c>.</summary>
    public List<List<int>> Tag { get; set; } = [];

    /// <summary>Tiles to unmark, as <c>[[x, y], …]</c>.</summary>
    public List<List<int>> Untag { get; set; } = [];

    /// <summary>A room to build and pay for, as a real player command. Unlike
    /// <see cref="ScenarioRoom"/> this goes through the ground rules and the treasury.</summary>
    public ScenarioBuild? Build { get; set; }

    /// <summary>Tiles to sell the rooms on, as <c>[[x, y], …]</c>, in the order each refund is
    /// banked.</summary>
    public List<List<int>> Sell { get; set; } = [];

    /// <summary>Casts Create Imp: buy one imp at the dungeon heart for mana — the one scenario
    /// command that spends a resource rather than earning or moving one.</summary>
    public bool CreateImp { get; set; }

    /// <summary>The id of a creature to pick up into this player's hand, or 0 for none. Creature
    /// ids are assigned in the order <c>setup.creatures</c> lists them, starting at 1, so a
    /// scenario can name one without knowing anything about the world it is about to
    /// build.</summary>
    public int PickUp { get; set; }

    /// <summary>An object id for the keeper's hand to lift (ADR-0075).</summary>
    public int PickUpObject { get; set; }

    /// <summary>The id of a creature to slap, or 0 for none.</summary>
    public int Slap { get; set; }

    /// <summary>Where to put down whatever the hand is holding, as <c>[x, y]</c>. No creature is
    /// named because the hand chooses: DK2 puts down what it picked up last, and letting a scenario
    /// name one would hide that.</summary>
    public List<int> Drop { get; set; } = [];

    /// <summary>The creature under the cursor as the drop happens, by id, or 0 for none. A chicken
    /// or gold dropped onto a creature feeds or pays it on the spot (ADR-0217).</summary>
    public int DropOnto { get; set; }
}

/// <summary>A room a player asks to build, at a tick, and pays for.</summary>
public sealed class ScenarioBuild
{
    /// <summary>A ROOMS record name, e.g. <c>Treasury</c>.</summary>
    public string Type { get; set; } = "";

    /// <summary>The tiles to build on, as <c>[[x, y], …]</c>, in the order they are charged.</summary>
    public List<List<int>> Tiles { get; set; } = [];
}

/// <summary>A work application injected straight onto the world's queue at a given tick.
/// <para><b>A harness affordance, not a player command and not DK2.</b> In the original a working
/// imp produces these; a scenario that wants to watch a tile get dug stands in for one. Every run
/// that uses this says so under "Not answered by this run", because a dig timed by a scenario file
/// tells you nothing about how long DK2 takes to dig.</para></summary>
public sealed class ScenarioWork
{
    public int AtTick { get; set; }
    public byte Player { get; set; }

    /// <summary>A <see cref="DK2.Simulation.TileWorkKind"/> name.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Target tile, as <c>[x, y]</c>.</summary>
    public List<int> At { get; set; } = [];

    /// <summary>How many applications to queue on that tick. Defaults to one.</summary>
    public int Repeat { get; set; } = 1;

    /// <summary>Repeat the same injection every tick until this one, inclusive.</summary>
    public int UntilTick { get; set; } = -1;
}

/// <summary>One behavioural claim a run must satisfy — the thing that turns a scenario
/// from a description into a regression test.
/// <para><b><c>because</c> is mandatory and is the whole point.</b> An expectation with no
/// stated source is a guess pinned by a green check, which is worse than no check: it buys
/// silence. Say where the claim comes from — an owner observation and its date, an ADR, a
/// research note — or do not write the expectation.</para></summary>
public sealed class ScenarioExpectation
{
    /// <summary>The <see cref="DK2.Core.SimEvent.Kind"/> to look for.</summary>
    public string Event { get; set; } = "";

    /// <summary>Substring the event's detail must contain. Optional.</summary>
    public string Detail { get; set; } = "";

    /// <summary>The exact tick it must occur on. Optional.</summary>
    public int? AtTick { get; set; }

    /// <summary>Exactly how many must match. Omitted means at least one.</summary>
    public int? Count { get; set; }

    /// <summary>Invert: no event may match. Refutations are evidence too.</summary>
    public bool Absent { get; set; }

    /// <summary>Where the claim comes from. Required; see the type summary.</summary>
    public string Because { get; set; } = "";

    /// <summary>A one-line rendering for a failure message and a report.</summary>
    public override string ToString()
    {
        string what = Absent ? "no " + Event : Event;
        if (Detail.Length > 0) what += $" containing \"{Detail}\"";
        if (AtTick is int tick) what += $" at tick {tick}";
        if (Count is int n) what += $" exactly {n} time(s)";
        return what;
    }
}

/// <summary>A declarative experiment: the same file is meant to eventually drive both our
/// simulation and the original game (DK2_REIMPLEMENTATION_PLAN.md §6.2).
/// <para>Loading is deliberately strict about its own ignorance: any key it does not recognise,
/// and any key it recognises but cannot yet act on, is reported by name. A scenario that quietly
/// does less than it says would poison the regression corpus.</para></summary>
public sealed class Scenario
{
    public string Id { get; set; } = "";

    /// <summary>What this scenario is trying to find out. Prose, for humans.</summary>
    public string Question { get; set; } = "";

    /// <summary>Either <c>campaign:&lt;level&gt;</c> to load a shipped map, or <c>flat_WxH</c> for
    /// a synthetic open dungeon.</summary>
    public string Map { get; set; } = "";

    public ulong Seed { get; set; }

    public int DurationTicks { get; set; }

    /// <summary>Enables DK2's player-specific explored-tile state.</summary>
    public bool FogOfWar { get; set; }

    /// <summary>The keeper whose final text view is rendered.</summary>
    public byte ViewingPlayer { get; set; } = 1;

    public ScenarioSetup Setup { get; set; } = new();

    /// <summary>Player commands, scheduled by tick.</summary>
    public List<ScenarioCommand> Commands { get; set; } = [];

    /// <summary>Computer keepers driven by the campaign PLAYERS records after each tick.</summary>
    public List<byte> ComputerKeepers { get; set; } = [];

    /// <summary>Harness-injected tile work, standing in for a creature that does not exist yet.</summary>
    public List<ScenarioWork> Work { get; set; } = [];

    /// <summary>What this run must show for the behaviour it was written for to hold.
    /// Empty means the scenario still only describes; see <see cref="ScenarioExpectation"/>.</summary>
    public List<ScenarioExpectation> Expect { get; set; } = [];

    /// <summary>What the run should record. Unrecognised entries are reported.</summary>
    public List<string> Observe { get; set; } = [];

    /// <summary>Top-level keys in the file that this build does not understand.</summary>
    [YamlIgnore]
    public IReadOnlyList<string> UnknownKeys { get; private set; } = [];

    [YamlIgnore]
    public string SourcePath { get; private set; } = "";

    private static readonly string[] KnownKeys =
        ["id", "question", "map", "seed", "duration_ticks", "fog_of_war", "viewing_player", "setup", "commands", "computer_keepers", "work", "observe", "expect"];

    public static Scenario Load(string path)
    {
        string text = File.ReadAllText(path);

        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        Scenario scenario = deserializer.Deserialize<Scenario>(text)
            ?? throw new InvalidDataException($"'{path}' is empty or not a scenario.");

        // Second pass purely to find keys we silently dropped above.
        var raw = deserializer.Deserialize<Dictionary<string, object>>(text) ?? [];
        scenario.UnknownKeys = raw.Keys
            .Where(k => !KnownKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        scenario.SourcePath = path;
        scenario.Validate();
        return scenario;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidDataException($"'{SourcePath}': scenario needs an id.");
        if (string.IsNullOrWhiteSpace(Map))
            throw new InvalidDataException($"'{SourcePath}': scenario '{Id}' needs a map.");
        if (DurationTicks <= 0)
            throw new InvalidDataException($"'{SourcePath}': scenario '{Id}' needs a positive duration_ticks.");
    }
}
