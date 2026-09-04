// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DK2.Harness;

/// <summary>
/// Starting resources for one player.
///
/// Mana goes straight onto the player. Gold does not: it has to be stored
/// somewhere, so it is deposited into the player's treasury and dungeon-heart
/// tiles, and a run whose player has nowhere to put it says so rather than
/// conjuring a gold counter (<see cref="DK2.Simulation.GoldStore"/>).
/// </summary>
public sealed class ScenarioPlayer
{
    public byte Id { get; set; }
    public int Gold { get; set; }
    public int Mana { get; set; }
}

/// <summary>
/// One block of room tiles laid down before the run starts.
///
/// <b>Placement is the level author's, not a player's.</b> Nothing is charged
/// for, no ground rule is applied beyond the room's own placeable-on flags, and
/// no imp builds it — the same standing that <see cref="ScenarioCreature"/> has.
/// A scenario that wants to watch a keeper pay for a room uses a
/// <c>build</c> command instead.
/// </summary>
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

/// <summary>
/// One creature placed on the map before the run starts.
///
/// <b>Placement is the harness's, not DK2's.</b> The original's imps come out of
/// a dungeon heart or a spell and cost mana; none of that machinery exists, so a
/// scenario that wants workers says where they stand and the report says that is
/// what happened.
/// </summary>
public sealed class ScenarioCreature
{
    /// <summary>A CREATURES record name, e.g. <c>Imp</c>.</summary>
    public string Type { get; set; } = "";

    /// <summary>Where it starts, as <c>[x, y]</c>.</summary>
    public List<int> At { get; set; } = [];

    /// <summary>Whose creature it is.</summary>
    public byte Player { get; set; }

    /// <summary>
    /// Health to start with. Omit it for the record's full <c>Hp</c>.
    ///
    /// <para>Here because nothing in a scenario can hurt a creature — there is
    /// no combat and no command that wounds one — so the only way to observe
    /// anything downstream of damage is to place a creature already wounded.
    /// <c>health: 0</c> is how a run watches a creature lie on the floor for
    /// <c>CreatureDyingStateDurationSeconds</c> and then die (Q99).</para>
    ///
    /// <para><b>Nullable rather than defaulting 0 to "full"</b>, which is what
    /// it did first and was wrong in both directions. Treating 0 as "full"
    /// meant a scenario could not ask for a collapse at all; treating a plain
    /// <c>int</c>'s 0 as zero health would mean a scenario that omitted the
    /// field was silently about death. Null says "the record's", and every
    /// number including 0 says itself.</para>
    ///
    /// <para>Note that a small positive value is <i>not</i> a way to reach the
    /// dying state: with no damage anywhere in the simulation, a creature at 1
    /// health simply stays at 1 — and if it is below
    /// <c>CreatureSleepsWhenBelowPercentHealth</c> it goes to bed and heals back
    /// to full. That is exactly what the first version of the collapse scenario
    /// did instead of collapsing.</para>
    /// </summary>
    public int? Health { get; set; }
}

/// <summary>
/// Initial conditions, applied in the order listed here: rooms, then gold and
/// mana, then creatures. Rooms come first because gold needs a treasury to go
/// into.
/// </summary>
public sealed class ScenarioSetup
{
    public List<ScenarioPlayer> Players { get; set; } = [];

    /// <summary>Room tiles to lay down before tick 0.</summary>
    public List<ScenarioRoom> Rooms { get; set; } = [];

    /// <summary>Creatures to place before tick 0.</summary>
    public List<ScenarioCreature> Creatures { get; set; } = [];
}

/// <summary>
/// A player command scheduled for one tick. Real input: it goes through the
/// same <see cref="DK2.Simulation.InputFrame"/> the game would use.
/// </summary>
public sealed class ScenarioCommand
{
    public int AtTick { get; set; }
    public byte Player { get; set; }

    /// <summary>Tiles to mark for digging, as <c>[[x, y], …]</c>.</summary>
    public List<List<int>> Tag { get; set; } = [];

    /// <summary>Tiles to unmark, as <c>[[x, y], …]</c>.</summary>
    public List<List<int>> Untag { get; set; } = [];

    /// <summary>
    /// A room to build and pay for, as a real player command. Unlike
    /// <see cref="ScenarioRoom"/> this goes through the ground rules and the
    /// treasury.
    /// </summary>
    public ScenarioBuild? Build { get; set; }

    /// <summary>
    /// Tiles to sell the rooms on, as <c>[[x, y], …]</c>, in the order each
    /// refund is banked.
    /// </summary>
    public List<List<int>> Sell { get; set; } = [];

    /// <summary>
    /// Casts Create Imp: buy one imp at the dungeon heart for mana. The first
    /// scenario command that SPENDS a resource rather than earning or moving
    /// one.
    /// </summary>
    public bool CreateImp { get; set; }
}

/// <summary>A room a player asks to build, at a tick, and pays for.</summary>
public sealed class ScenarioBuild
{
    /// <summary>A ROOMS record name, e.g. <c>Treasury</c>.</summary>
    public string Type { get; set; } = "";

    /// <summary>The tiles to build on, as <c>[[x, y], …]</c>, in the order they are charged.</summary>
    public List<List<int>> Tiles { get; set; } = [];
}

/// <summary>
/// A work application injected straight onto the world's queue at a given tick.
///
/// <b>This is a harness affordance, not a player command and not DK2.</b> In the
/// original a working imp produces these; no creature exists yet, so a scenario
/// that wants to watch a tile get dug has to stand in for one. Every run that
/// uses this says so under "Not answered by this run", because a dig timed by a
/// scenario file tells you nothing about how long DK2 takes to dig.
/// </summary>
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

/// <summary>
/// A declarative experiment: the same file is meant to eventually drive both
/// our simulation and the original game (DK2_REIMPLEMENTATION_PLAN.md 6.2).
///
/// Loading is deliberately strict about its own ignorance: any key it does not
/// recognise, and any key it recognises but cannot yet act on, is reported by
/// name. A scenario that quietly does less than it says would poison the
/// regression corpus, which is the one artifact this project cannot afford to
/// have lying to it.
/// </summary>
public sealed class Scenario
{
    public string Id { get; set; } = "";

    /// <summary>What this scenario is trying to find out. Prose, for humans.</summary>
    public string Question { get; set; } = "";

    /// <summary>
    /// Either <c>campaign:&lt;level&gt;</c> to load a shipped map, or
    /// <c>flat_WxH</c> for a synthetic open dungeon.
    /// </summary>
    public string Map { get; set; } = "";

    public ulong Seed { get; set; }

    public int DurationTicks { get; set; }

    public ScenarioSetup Setup { get; set; } = new();

    /// <summary>Player commands, scheduled by tick.</summary>
    public List<ScenarioCommand> Commands { get; set; } = [];

    /// <summary>Harness-injected tile work, standing in for a creature that does not exist yet.</summary>
    public List<ScenarioWork> Work { get; set; } = [];

    /// <summary>What the run should record. Unrecognised entries are reported.</summary>
    public List<string> Observe { get; set; } = [];

    /// <summary>Top-level keys in the file that this build does not understand.</summary>
    [YamlIgnore]
    public IReadOnlyList<string> UnknownKeys { get; private set; } = [];

    [YamlIgnore]
    public string SourcePath { get; private set; } = "";

    private static readonly string[] KnownKeys =
        ["id", "question", "map", "seed", "duration_ticks", "setup", "commands", "work", "observe"];

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
