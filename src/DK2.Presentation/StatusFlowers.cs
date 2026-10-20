// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The ring's colour: the owner's keeper palette entry (0x006E5058, as ClassicKeeperPalette).</summary>
public readonly record struct StatusFlowerColour(byte Red, byte Green, byte Blue);

/// <summary>One creature's status flower: what the renderer needs to draw it.</summary>
/// <param name="Height">Lift above the creature's position (Fp32 tiles), <see cref="StatusFlowerRules.Height"/>.</param>
/// <param name="Blink">Health below a sixth, or inside the health-change window (0x005956E0).</param>
public readonly record struct StatusFlower(int CreatureId, int Level, PlayerId Owner, StatusFlowerColour Colour,
    Fp32 Height, bool Blink);

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

    /// <summary>The flower's lift: the body's height plus <c>level·0x600/10</c>. The sleeping lift
    /// (category 15: +0x580 off a bed slot, +0x280 or +0x680 on one, by an unread global) and the
    /// +0x80 when 0x00491B40 ≠ -1 are left out. That vtable <c>+0x40</c> is the definition's
    /// <c>Height</c> is assumed.</summary>
    public static Fp32 Height(Fp32 bodyHeight, int level) =>
        new(bodyHeight.Raw + (int)((uint)level * LevelLiftRawTimesTen / 10u));
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
    private WorldState? _world;
    private int _capture;

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
    /// <c>turn &lt; last hovered turn + 3</c>. A health change seen at turn T shows until T + 3.</summary>
    public IReadOnlyList<StatusFlower> Capture(WorldState world, GameTick turn, CreatureQueryTarget hovered)
    {
        if (!ReferenceEquals(world, _world))
        {
            _hoverUntil.Clear(); _health.Clear(); _world = world;
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
            var (red, green, blue) = ClassicKeeperPalette.Colour(new PlayerId(creature.OwnerPlayerId));
            _shown.Add(new StatusFlower(creature.Id, creature.Level, new PlayerId(creature.OwnerPlayerId),
                new StatusFlowerColour(red, green, blue), StatusFlowerRules.Height(definition.Height, creature.Level),
                Blink: belowSixth || healthWindow));
        }
        Prune(turn);
        return _shown;
    }

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
    }
}
