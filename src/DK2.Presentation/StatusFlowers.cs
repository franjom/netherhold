// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;

namespace DK2.Presentation;

/// <summary>The ring's colour: the owner's keeper palette entry (0x006E5058, as ClassicKeeperPalette).</summary>
public readonly record struct StatusFlowerColour(byte Red, byte Green, byte Blue);

/// <summary>One creature's status flower: what the renderer needs to draw it.</summary>
/// <param name="Colour">The colour this frame: the owner's, cycled for a neutral creature and
/// scaled by the blink (0x005956E0).</param>
/// <param name="Height">Lift above the creature's position (Fp32 tiles), <see cref="StatusFlowerRules.Height"/>.</param>
/// <param name="Blink">Health below a sixth, or inside the health-change window (0x005956E0).</param>
/// <param name="Ring">The petal ring texture's number n, <c>GUI\moods\H-0n</c> (0x00596EB0).</param>
/// <param name="Centre">The centre texture under <c>GUI\moods\</c> (0x00596590).</param>
/// <param name="ExperienceArcs">Draw the centre at half colour with a full-colour wedge of
/// <paramref name="Experience"/> (0x005966B0); otherwise draw it plain at full colour.</param>
/// <param name="Experience">The wedge's share of a turn, 0 to just under 1. Drawn clockwise from the
/// top as the owner's captures show; the native angle-to-screen mapping is unread.</param>
public readonly record struct StatusFlower(int CreatureId, int Level, PlayerId Owner, StatusFlowerColour Colour,
    Fp32 Height, bool Blink, int Ring, string Centre, bool ExperienceArcs, float Experience);

/// <summary>What 0x0048E830 reads about one creature, as our state answers it.</summary>
/// <param name="Never">One of the predicate's refusals: <see cref="StatusFlowerRules.NeverShows"/>.</param>
/// <param name="InWindow">Before the hover or health-change stamp (native <c>+0x38</c>, 0x004B34F0).</param>
public readonly record struct StatusFlowerFacts(bool Own, bool Never, bool ViewerPossessing, bool Hovered,
    bool Dying, bool InCombat, CreatureMood Mood, bool BelowSixthHealth, bool InWindow);

/// <summary>Whether a creature shows its flower, Creature_ShowsStatusFlower (0x0048E830), and where
/// it sits (0x005955C0); docs/research/status-flowers.md §2.
/// <para>Left out, with no counterpart in our state: the viewer's global gate (player <c>+0x76</c>
/// bits 0x400000/0x40, mode 0x0075AD4B), the trigger window <c>+0x4B7</c> and the removal countdown
/// <c>+0x4B3</c>. The draw bits (<c>+0x6C</c>/<c>+0xF3</c>) are the adapter's: it draws only
/// creatures its scene shows.</para></summary>
// The rule order is CONFIRMED (0x0048E830); mapping +0x3E6 bit 0x200000 to our dying states is inferred.
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/status-flowers.md")]
public static class StatusFlowerRules
{
    /// <summary>¾ s: <c>(ticks-per-second·3)&gt;&gt;2</c>, the stamp both windows add to the turn.</summary>
    public const int WindowTicks = (SimulationTiming.TicksPerSecond * 3) >> 2;

    /// <summary>Lift per level, ×10 (raw Fp32): <c>level·0x600/10</c>, 0.15 tile a level.</summary>
    public const int LevelLiftRawTimesTen = 0x600;

    /// <summary>State 0x110 (272), refused beside 0xC1 (joining, 193). Its meaning is unread.</summary>
    public const int UnreadRefusedState = 0x110;

    /// <summary>The predicate in its native order: refusals, then possession (hover id only), then
    /// dying (<c>+0x3E6</c> bit 21) and foreign owners always, then the own creature's reasons.</summary>
    public static bool Shows(in StatusFlowerFacts facts)
    {
        if (facts.Never) return false;
        if (facts.ViewerPossessing) return facts.Hovered;
        if (facts.Dying || !facts.Own) return true;
        return facts.InCombat || facts.Mood is CreatureMood.Unhappy or CreatureMood.Angry
            || facts.BelowSixthHealth || facts.InWindow;
    }

    /// <summary>The refusals we can map: an own creature whose definition is Horny (<c>+0x686</c>
    /// bit 0x4000), states 0xC1 and 0x110 (<c>+0x25E</c>), in a hand (<c>+0x3E6</c> bit 0), and a
    /// creature with no state, our model's corpse; how DK2's dead body answers the overlay is unread.</summary>
    public static bool NeverShows(CreatureDef definition, CreatureState creature, PlayerId viewer) =>
        (creature.OwnerPlayerId == viewer && (definition.Flags & CreatureFlags.IsHorny) != 0)
        || creature.Dk2StateId is PortalRules.JoiningState or UnreadRefusedState
        || HandRules.IsHeld(creature) || !CreatureStateMachine.HasState(creature);

    /// <summary>0x0048E830's health test, <c>health &lt; max·100/600</c> in unsigned arithmetic.</summary>
    public static bool BelowSixthHealth(int health, int maxHealth) =>
        health < (int)((uint)maxHealth * 100u / 600u);

    /// <summary>The petal ring, <c>H-0n</c>: entry <c>clamp(1 − trunc(−4·health/max), 0, 5)</c> of the
    /// array the loader fills with H-05 first (0x00596850), so six petals at full health, one fewer
    /// per quarter lost, and one petal while dying (entry 0, 0x00596EB0).</summary>
    public static int Ring(bool dying, int health, int maxHealth)
    {
        // A zero maximum divides to an indefinite value whose low dword __ftol returns: 0.
        int entry = dying ? 0 : maxHealth <= 0 ? 1
            : Math.Clamp(1 - (int)(health / (double)maxHealth * -4.0), 0, 5);
        return 5 - entry;
    }

    /// <summary>The level-mode wedge (0x0048EA40): <c>(exp·4096/needed)/2 &amp; 0x7FF</c> of a
    /// 0x800 turn, so the share of the way to the next level, wrapping past it; none when nothing
    /// is needed.</summary>
    public static float ExperienceShare(int experience, int needed)
    {
        if (needed <= 0) return 0;
        long fixedShare = ((long)experience << 12) / needed;
        return ((fixedShare / 2) & 0x7FF) / 2048f;
    }

    /// <summary>One blink step (0x005956E0): <paramref name="untilMs"/> rolls 333 ms ahead when
    /// passed; <c>u = (until − now)·100/333</c> runs 100 down to 0, and a channel is
    /// <c>c·u·2/100</c> below 51, else <c>c·(100 − u)·2/100</c>: dark, full at the middle, dark.</summary>
    public static StatusFlowerColour Blinked(StatusFlowerColour colour, long nowMs, long untilMs)
    {
        uint u = (uint)((untilMs * 100 - nowMs * 100) / CycleMs);
        uint k = u < 0x33 ? u : 100 - u;
        return new StatusFlowerColour((byte)(colour.Red * k * 2 / 100), (byte)(colour.Green * k * 2 / 100),
            (byte)(colour.Blue * k * 2 / 100));
    }

    /// <summary>The overlay's clock step, 0x14D milliseconds (0x005956E0, 0x00596410).</summary>
    public const int CycleMs = 0x14D;

    /// <summary>The flower's lift: the body's height plus <c>level·0x600/10</c>. The sleeping lift
    /// (category 15: +0x580 off a bed slot, +0x280 or +0x680 on one, by an unread global) and the
    /// +0x80 when 0x00491B40 ≠ -1 are left out. That vtable <c>+0x40</c> is the definition's
    /// <c>Height</c> is assumed.</summary>
    public static Fp32 Height(Fp32 bodyHeight, int level) =>
        new(bodyHeight.Raw + (int)((uint)level * LevelLiftRawTimesTen / 10u));
}

/// <summary>The centre's modes, creature <c>+0x388</c> (0x00596410).</summary>
public enum StatusFlowerMode : byte { Level = 0, Mood = 1, AngerReason = 2, Job = 3, Status = 4 }

/// <summary>What the centre cycle (0x00596410) and icon choice (0x00596590) read, as our state
/// answers it.</summary>
/// <param name="Hero">The owner is DK2's hero player (the id at 0x006F254C, the player whose
/// <c>+0x10FC</c> is 1), taken to be our player 1.</param>
/// <param name="MoodBand">Creature <c>+0x28A</c>: 0 content, 1 unhappy, 2 angry.</param>
/// <param name="Status">The status icon's name, or null (0x00597270).</param>
/// <param name="Job">The job icon's name, or null for none (0x00596FC0).</param>
public readonly record struct StatusFlowerCentreFacts(bool Hero, bool Imp, int StateId, int MoodBand,
    AngerBucket LargestAnger, string? Status, string? Job, int Level);

/// <summary>The flower's centre icon: which mode follows which every 333 ms, and the texture each
/// mode draws. docs/research/status-flowers.md §2.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/status-flowers.md")]
public static class StatusFlowerCentre
{
    private static readonly string[] Moods = ["SM-Happy", "SM-Sad", "SM-Angry"];

    private static readonly string[] Levels =
        ["SL-01", "SL-02", "SL-03", "SL-04", "SL-05", "SL-06", "SL-07", "SL-08", "SL-09", "SL-10"];

    // 0x006BE510, indexed by the largest anger counter (0x004871B0).
    private static readonly string[] AngerReasons =
        ["SJ-Mistreat", "SJ-Hungry", "SJ-Rest", "SJ-Busy", "SJ-PayDay", "SJ-Mistreat"];

    /// <summary>The level badge, <c>SL-01</c>..<c>SL-10</c>.</summary>
    public static string LevelIcon(int level) => Levels[Math.Clamp(level, 1, 10) - 1];

    /// <summary>The mode after <paramref name="mode"/> (0x00596410). From the level: a hero shows
    /// its status or stays; others show a status, else an Imp shows its job unless idle-like
    /// (states 6–12, 0x31, 0x32), else the mood when unhappy or angry, else the job. The mood is
    /// followed by the anger reason (or the job when content again); the rest return to the level.</summary>
    public static StatusFlowerMode Next(StatusFlowerMode mode, in StatusFlowerCentreFacts facts) => mode switch
    {
        StatusFlowerMode.Level when facts.Hero =>
            facts.Status != null ? StatusFlowerMode.Status : StatusFlowerMode.Level,
        StatusFlowerMode.Level when facts.Status != null => StatusFlowerMode.Status,
        StatusFlowerMode.Level when facts.Imp =>
            facts.StateId is >= 6 and <= 12 or 0x31 or 0x32 ? StatusFlowerMode.Level : StatusFlowerMode.Job,
        StatusFlowerMode.Level => facts.MoodBand != 0 ? StatusFlowerMode.Mood : StatusFlowerMode.Job,
        StatusFlowerMode.Mood => facts.MoodBand == 0 ? StatusFlowerMode.Job : StatusFlowerMode.AngerReason,
        _ => StatusFlowerMode.Level,
    };

    /// <summary>The texture for <paramref name="mode"/> (0x00596590) and whether the experience
    /// wedge is drawn: only when the mode is the level once chosen, so a status mode with no
    /// status draws the badge plain. A job mode with no job falls back to the level (0x00596FC0
    /// resets the mode).</summary>
    public static (string Icon, StatusFlowerMode Mode) Icon(StatusFlowerMode mode, in StatusFlowerCentreFacts facts) =>
        mode switch
        {
            StatusFlowerMode.Mood => (Moods[Math.Clamp(facts.MoodBand, 0, 2)], mode),
            StatusFlowerMode.AngerReason => (facts.MoodBand != 0 ? AngerReasons[(int)facts.LargestAnger] : AngerReasons[0], mode),
            StatusFlowerMode.Job => facts.Job != null ? (facts.Job, mode) : (LevelIcon(facts.Level), StatusFlowerMode.Level),
            StatusFlowerMode.Status => (facts.Status ?? LevelIcon(facts.Level), mode),
            _ => (LevelIcon(facts.Level), StatusFlowerMode.Level),
        };

    /// <summary>The status icon (0x00597270) for a creature not on the hero side, first match:
    /// unconscious (dying), stunned, afraid (not fearless and <c>CurrentFear</c> over the scaled
    /// <c>Fear</c>). Left out: Leaving (<c>+0x3E6</c> bit 14, unmapped) and S-Gem (carrying object
    /// type 0x1C, 0x0048B5C0). The hero side's list (player <c>+0x2EC</c> slots giving Fighting,
    /// Prison or Torture) is unread, so a hero has none.</summary>
    public static string? Status(bool hero, bool dying, bool stunned, bool fearless, int currentFear, int fearThreshold)
    {
        if (hero) return null;
        if (dying) return "SJ-Unconscious";
        if (stunned) return "SJ-Stunned";
        if (!fearless && fearThreshold < currentFear) return "ST-Fear";
        return null;
    }

    /// <summary>The job icon (0x00596FC0): No_Go within 5 s of a drop on a room, else by resolved
    /// category, worker states by id; null where DK2 falls back to the level.</summary>
    public static string? Job(bool droppedOnARoomRecently, CreatureStateCategory category, int stateId)
    {
        if (droppedOnARoomRecently) return "SJ-No_Go";
        return category switch
        {
            CreatureStateCategory.WorkerJob => stateId switch
            {
                0xD or 0xE or 0x19 or 0x1A or 0x27 => "SJ-Dig",
                >= 0xF and <= 0x14 => "SJ-Claim",
                0x15 or 0x16 => "SJ-Reinforce",
                >= 0x1B and <= 0x1D => "SJ-Take_Gold",
                0x1E or 0x1F or 0x21 => "SJ-Library",
                0x20 or (>= 0x22 and <= 0x25) => "SJ-Take_Crate",
                0x28 or 0x29 => "SJ-Graveyard",
                (>= 0x2A and <= 0x2C) or 0x2E => "SJ-Prison",
                0x2D => "SJ-Temple",
                _ => null,
            },
            CreatureStateCategory.Research => "SJ-Library",
            CreatureStateCategory.Manufacture => "SJ-Workshop",
            CreatureStateCategory.Eating => "SJ-Hungry",
            CreatureStateCategory.Sleeping => "SJ-Rest",
            CreatureStateCategory.Sulking => "SM-Sad",
            CreatureStateCategory.GuardPost => "SJ-Guarding",
            CreatureStateCategory.Temple => "SJ-Temple",
            CreatureStateCategory.Training => "SJ-Training",
            CreatureStateCategory.Torture => "SJ-Torture",
            CreatureStateCategory.CombatPit => stateId == 0x8F ? "SJ-Watching" : "SJ-Fighting",
            CreatureStateCategory.Casino => "SJ-Casino",
            CreatureStateCategory.Payday => "SJ-PayDay",
            _ => null,
        };
    }

    /// <summary>The largest of the six anger counters, ties to the lowest index (0x004871B0).</summary>
    public static AngerBucket LargestAnger(in CreatureAnger anger)
    {
        AngerBucket best = 0;
        for (var bucket = AngerBucket.Company; bucket <= AngerBucket.Treatment; bucket++)
            if (anger[bucket] > anger[best]) best = bucket;
        return best;
    }
}

/// <summary>The flowers to draw this frame, from the settled world plus the two ¾ s windows, which
/// live here rather than in simulated state (status-flowers.md §3: nothing simulated reads native
/// <c>+0x38</c>). Keyed by creature id; reset when the world instance changes.</summary>
public sealed class StatusFlowers
{
    private readonly GameDatabase _database;
    private readonly PlayerId _viewer;
    private readonly List<StatusFlower> _shown = new();
    private readonly Dictionary<int, GameTick> _hoverUntil = new();
    private readonly Dictionary<int, HealthWatch> _health = new();
    private readonly List<int> _stale = new();
    private readonly Dictionary<int, (StatusFlowerMode Mode, long Until)> _centre = new();
    private WorldState? _world;
    private int _capture;
    private long _blinkUntil, _paletteUntil;
    private byte _paletteIndex;

    private struct HealthWatch
    {
        public int Health;
        public GameTick Until;
        public int SeenAt;
    }

    public StatusFlowers(GameDatabase database, PlayerId viewingPlayer)
    {
        _database = database ?? throw new System.ArgumentNullException(nameof(database));
        _viewer = viewingPlayer;
    }

    public IReadOnlyList<StatusFlower> Shown => _shown;

    /// <summary>Call once a frame with the settled world. <paramref name="hovered"/> is the Hand's
    /// hover target, stamped like Presentation_Update's 0x0043B7F1: shown while
    /// <c>turn &lt; last hovered turn + 3</c>. A health change seen at turn T shows until T + 3.
    /// <paramref name="milliseconds"/> is the real-time clock DK2's overlay reads
    /// (Clock_GetMilliseconds in 0x005953D0) for the blink, the neutral palette and the centre cycle.</summary>
    public IReadOnlyList<StatusFlower> Capture(WorldState world, GameTick turn, CreatureQueryTarget hovered,
        long milliseconds = 0)
    {
        if (!ReferenceEquals(world, _world))
        {
            _hoverUntil.Clear(); _health.Clear(); _centre.Clear(); _world = world;
        }
        // 0x005956E0's renderer-wide timers: the blink phase and the neutral palette step.
        if (_blinkUntil < milliseconds) _blinkUntil = milliseconds + StatusFlowerRules.CycleMs;
        if (_paletteUntil < milliseconds)
        {
            if (++_paletteIndex > 7) _paletteIndex = 1;
            _paletteUntil = milliseconds + StatusFlowerRules.CycleMs;
        }
        _shown.Clear();
        _capture++;
        var until = new GameTick(turn.Value + StatusFlowerRules.WindowTicks);
        if (hovered != CreatureQueryTarget.None) _hoverUntil[hovered.Value] = until;
        bool possessing = world.Player(_viewer).PossessedCreatureId != 0;
        foreach (CreatureState creature in world.Creatures)
        {
            bool healthWindow = turn < Observe(creature, until);
            CreatureDef definition = _database.CreatureById(creature.CreatureTypeId);
            bool never = StatusFlowerRules.NeverShows(definition, creature, _viewer);
            bool belowSixth = !never && StatusFlowerRules.BelowSixthHealth(creature.Health,
                CreatureHealthRules.MaxHealth(_database, definition, creature));
            bool hoverWindow = _hoverUntil.TryGetValue(creature.Id, out GameTick hoverUntil) && turn < hoverUntil;
            var facts = new StatusFlowerFacts(
                Own: creature.OwnerPlayerId == _viewer,
                Never: never,
                ViewerPossessing: possessing,
                Hovered: creature.Id == hovered.Value,
                Dying: creature.Dk2StateId is CreatureDeathRules.FallingState or CreatureDeathRules.DyingState
                    || CreatureDeathRules.HasCollapsed(creature),
                InCombat: !never && NeedRules.ResolvedCategory(creature) == CreatureStateCategory.Combat,
                Mood: creature.Mood,
                BelowSixthHealth: belowSixth,
                InWindow: healthWindow || hoverWindow);
            if (!StatusFlowerRules.Shows(facts)) continue;
            bool blink = belowSixth || healthWindow;
            bool neutral = creature.OwnerPlayerId == PlayerIds.Neutral;
            var (red, green, blue) = ClassicKeeperPalette.Colour(
                new PlayerId(neutral ? Math.Max(_paletteIndex, (byte)1) : creature.OwnerPlayerId));
            var colour = new StatusFlowerColour(red, green, blue);
            if (blink && !neutral) colour = StatusFlowerRules.Blinked(colour, milliseconds, _blinkUntil);
            int maxHealth = CreatureHealthRules.MaxHealth(_database, definition, creature);
            var (icon, arcs) = Centre(creature, definition, turn, milliseconds, facts.Dying);
            _shown.Add(new StatusFlower(creature.Id, creature.Level, new PlayerId(creature.OwnerPlayerId),
                colour, StatusFlowerRules.Height(definition.Height, creature.Level), blink,
                StatusFlowerRules.Ring(facts.Dying, creature.Health, maxHealth), icon, arcs,
                StatusFlowerRules.ExperienceShare(creature.ExperiencePoints,
                    CreatureLevelRules.ExperienceForNextLevel(_database, definition, creature.Level))));
        }
        Prune(turn);
        return _shown;
    }

    /// <summary>Steps this creature's centre cycle when its 333 ms are up and picks the icon.</summary>
    private (string Icon, bool Arcs) Centre(CreatureState creature, CreatureDef definition, GameTick turn,
        long milliseconds, bool dying)
    {
        bool hero = creature.OwnerPlayerId == PlayerIds.Good;
        var facts = new StatusFlowerCentreFacts(
            Hero: hero,
            Imp: creature.CreatureTypeId == ImpTypeId,
            StateId: creature.Dk2StateId,
            MoodBand: (int)creature.Mood,
            LargestAnger: StatusFlowerCentre.LargestAnger(creature.Anger),
            Status: StatusFlowerCentre.Status(hero, dying, StatusEffectRules.IsStunned(creature),
                (definition.Flags & CreatureFlags.IsFearless) != 0, creature.CurrentFear,
                FearRules.FearThreshold(_database, definition, creature.Level)),
            Job: StatusFlowerCentre.Job(creature.DroppedOnARoom
                    && HandRules.LastDroppedCounter(creature, turn.Value) < SimulationTiming.TicksPerSecond * 5,
                NeedRules.ResolvedCategory(creature), creature.Dk2StateId),
            Level: creature.Level);
        (StatusFlowerMode mode, long until) = _centre.TryGetValue(creature.Id, out var held) ? held : (StatusFlowerMode.Level, 0L);
        if (until < milliseconds)
        {
            mode = StatusFlowerCentre.Next(mode, facts);
            until = milliseconds + StatusFlowerRules.CycleMs;
        }
        var (icon, drawn) = StatusFlowerCentre.Icon(mode, facts);
        _centre[creature.Id] = (drawn, until);
        return (icon, drawn == StatusFlowerMode.Level);
    }

    // DK2's creature type 1 (creature +0x36F == 1).
    private const int ImpTypeId = 1;

    /// <summary>Records this capture's health and returns the end of the health-change window.</summary>
    private GameTick Observe(CreatureState creature, GameTick until)
    {
        bool known = _health.TryGetValue(creature.Id, out HealthWatch watch);
        if (known && watch.Health != creature.Health) watch.Until = until;
        watch.Health = creature.Health;
        watch.SeenAt = _capture;
        _health[creature.Id] = watch;
        return watch.Until;
    }

    private void Prune(GameTick turn)
    {
        _stale.Clear();
        foreach (KeyValuePair<int, HealthWatch> entry in _health)
            if (entry.Value.SeenAt != _capture) _stale.Add(entry.Key);
        foreach (int id in _stale) _health.Remove(id);
        _stale.Clear();
        foreach (KeyValuePair<int, GameTick> entry in _hoverUntil)
            if (entry.Value <= turn) _stale.Add(entry.Key);
        foreach (int id in _stale) _hoverUntil.Remove(id);
        _stale.Clear();
        foreach (int id in _centre.Keys)
            if (!_health.ContainsKey(id)) _stale.Add(id);
        foreach (int id in _stale) _centre.Remove(id);
    }
}
