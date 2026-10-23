// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>How a loose level gem is drawn: object process state 50 (<c>0x004A3C30</c>), the
/// Portal Gem's start state (objects.md §8). Each turn it bobs about height <c>0x1400</c>, spins
/// <c>0x40</c>, and once a game second, while not carried, creates its definition's idle effect
/// (<see cref="ObjectDef.IdleEffectId"/>, 297 for the Portal Gem) at its position, tied to it.
/// <para>Ours derives the bob and spin from the turn number in the view, since nothing in the
/// simulation reads a gem's height or heading; DK2 moves the object itself. Not reproduced: the
/// <c>0x80</c>-a-turn limit on the climb toward the target, which only shows as the gem rises in the
/// first two seconds after it is dropped, and the spin's starting heading.</para></summary>
public static class LevelGemIdle
{
    /// <summary>The object process state <c>0x004A3C30</c> is registered for (<c>0x0049EC51</c>).</summary>
    public const byte ProcessState = 50;

    /// <summary>The height the bob centres on, a quarter tile above ordinary floor.</summary>
    public const int BaseHeight = 0x1400;

    /// <summary>Heading added each turn (<c>+0xF4</c>), a full turn in 32 turns.</summary>
    public const int SpinPerTurn = 0x40;

    // 0x006C1488 is a 2048-entry sine table scaled by 65536; the process reads entry
    // (turn & 15) * 128 and shifts it right 8, rounding toward zero. These are those sixteen values.
    private static readonly short[] Bob =
        { 0, 97, 181, 236, 256, 236, 181, 97, 0, -97, -181, -236, -256, -236, -181, -97 };

    /// <summary>DK2's turn number during our current tick: <c>CurrentTick + 1</c>, the convention of
    /// <c>0x00517490</c> (ADR-0119) that <c>HandRules</c> and the job system use. The bob, spin and
    /// effect cadence below all take this turn.</summary>
    public static long Turn(WorldState world) =>
        (world ?? throw new ArgumentNullException(nameof(world))).CurrentTick.Value + 1;

    /// <summary>Whether a definition runs this process: it starts in state 50.</summary>
    public static bool Applies(ObjectDef? definition) => definition is { StartState: ProcessState };

    /// <summary>The height the process moves the object to on turn <paramref name="turn"/>.</summary>
    public static int Height(long turn) => BaseHeight + Bob[(int)(turn & 15)];

    /// <summary>The heading after <paramref name="turn"/> turns of spin from <paramref name="start"/>.</summary>
    public static int Heading(int start, long turn) => (int)((start + turn * SpinPerTurn) & 0x7FF);

    /// <summary>Whether the idle effect is created on this turn: <c>turn % DAT_006F2590 == 0</c>,
    /// the turns in a game second.</summary>
    public static bool EmitsOn(long turn) => turn % SimulationTiming.TicksPerSecond == 0;
}

/// <summary>Collects the idle effects loose level gems create (<see cref="LevelGemIdle"/>), once
/// for every turn crossed since the last call, so a frame that runs several turns misses none.</summary>
public sealed class LevelGemIdleEffects
{
    private readonly GameDatabase _database;
    private long _lastTurn = -1;

    public LevelGemIdleEffects(GameDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Adds to <paramref name="into"/> each effect created on the turns after the last call
    /// up to the world's current one (<see cref="LevelGemIdle.Turn"/>). Untied: ours places the effect at the gem's centre and lets
    /// it go, where DK2 ties it to the object (<c>+0x133</c>); the gem stays put while loose.</summary>
    public void Collect(WorldState world, List<ViewEffectRequest> into)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        if (into is null) throw new ArgumentNullException(nameof(into));
        long now = LevelGemIdle.Turn(world);
        long from = _lastTurn < 0 || _lastTurn > now ? now : _lastTurn + 1;
        _lastTurn = now;
        for (long turn = from; turn <= now; turn++)
        {
            if (!LevelGemIdle.EmitsOn(turn)) continue;
            IReadOnlyList<ObjectState> objects = world.Objects;
            for (int i = 0; i < objects.Count; i++)
            {
                ObjectState thing = objects[i];
                if (thing.InHand || thing.CarriedByCreatureId >= 0) continue;
                ObjectDef? definition = _database.ObjectById(thing.ObjectTypeId);
                if (!LevelGemIdle.Applies(definition) || definition!.IdleEffectId == 0) continue;
                var tile = new TilePos(thing.TileIndex % world.Width, thing.TileIndex / world.Width);
                into.Add(ViewEffectRequest.AtTileCentre(new EffectId(definition.IdleEffectId),
                    new PlayerId(thing.OwnerPlayerId), tile, new Fp32(LevelGemIdle.Height(turn))));
            }
        }
    }
}
