// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// The Dungeon Heart room's opening sequence, the first branch of its per-turn update
/// (<c>0x004D9A60</c>; docs/research/heart-intro.md). It starts at turn
/// <c>TimeBeforeDungeonHeartConstructionBegins × 4 + 1</c> (shipped: turn 1) and lasts nine seconds.
/// Before it, while the start is later than turn 1, every Heart asks for the plug lid (223) each
/// turn. At its start the <em>local player's</em> Heart asks for the Start Level effect (225): the
/// plug, its light and static, then its explosion and pieces. Seven seconds in, that Heart asks for
/// the arches' sparkle ring (314) and its Arches and Big Steps, created invisible for the local
/// player (<c>0x004DB700</c>), fade in over two seconds. Until the sequence ends the room neither
/// regenerates nor notices a missing Heart. The local player is the viewer, which the simulation
/// does not know: the view keeps the owner-only requests (<see cref="IsViewerOnly"/>) for it.
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/heart-intro.md")]
public static class HeartIntroRules
{
    /// <summary>Heart Plug Static Lit (<c>0xDF</c>): the plug lid, drawn each turn before the start.</summary>
    public const ushort PlugStaticLitEffectId = 223;

    /// <summary>Start Level Effect (<c>0xE1</c>): the plug, then its light, static, explosion and pieces.</summary>
    public const ushort StartLevelEffectId = 225;

    /// <summary>Heart Arches fade in effect (<c>0x13A</c>): the ring of sparkles as the arches appear.</summary>
    public const ushort ArchesFadeInEffectId = 314;

    /// <summary>Seconds after the start at which the arches begin to fade in and 314 is asked for.</summary>
    public const int FadeInStartSeconds = 7;

    /// <summary>Seconds after the start at which the arches are opaque and the sequence ends.</summary>
    public const int SequenceSeconds = 9;

    /// <summary>Alpha gained per turn by the Arches and Big Steps during the fade: 255 after eight turns.</summary>
    public const int AlphaPerTurn = 32;

    /// <summary>The effects are asked for at the Heart's tile centre, one tile up (<c>z = 0x1000</c>), the ring floor.</summary>
    public const int RequestHeight = Fp32.One;

    /// <summary>The turn the sequence starts: <c>TimeBeforeDungeonHeartConstructionBegins × ticks per second + 1</c>.</summary>
    public static uint StartTurn(GameDatabase database) =>
        (uint)(ConstructionDelaySeconds(database) * SimulationTiming.TicksPerSecond + 1);

    /// <summary>Variable 218, shipped as 0; a table without it (a test fixture) reads as the shipped value.</summary>
    private static int ConstructionDelaySeconds(GameDatabase database) =>
        database.Variables.TryGet(VariableTypeId.TimeBeforeDungeonHeartConstructionBegins, out int seconds) ? seconds : 0;

    /// <summary>True once the ordinary room frame (regeneration, ruin) runs: strictly after start + 9 s.</summary>
    public static bool SequenceOver(uint now, GameDatabase database) =>
        StartTurn(database) + (uint)(SequenceSeconds * SimulationTiming.TicksPerSecond) < now;

    /// <summary>The requests DK2 makes only for the local player's Heart (<c>0x004D9D2E</c>, <c>0x004D9E3B</c>).</summary>
    public static bool IsViewerOnly(ushort effectId) => effectId is StartLevelEffectId or ArchesFadeInEffectId;

    /// <summary>The alpha of the local player's Arches and Big Steps at <paramref name="now"/>: 0 from
    /// their creation, then <see cref="AlphaPerTurn"/> a turn from seven seconds after the start, 255
    /// from nine (<c>0x004D9CA0</c>). Another player's are always opaque.</summary>
    public static byte DecorationAlpha(uint now, GameDatabase database)
    {
        long fadeStart = StartTurn(database) + FadeInStartSeconds * SimulationTiming.TicksPerSecond;
        if (now < fadeStart) return 0;
        return (byte)Math.Min(255, (now - fadeStart) * AlphaPerTurn);
    }

    /// <summary>One turn of the sequence for <paramref name="owner"/>'s Heart.</summary>
    public static void RoomFrame(SimulationContext context, PlayerId owner)
    {
        GameDatabase database = context.Database;
        uint now = (uint)context.Tick.Value;
        uint start = StartTurn(database);
        if (now < start)
        {
            if (start > 1) Request(context, owner, PlugStaticLitEffectId);
            return;
        }
        if (SequenceOver(now, database)) return;
        uint elapsed = now - start;
        if (elapsed == 0) Request(context, owner, StartLevelEffectId);
        else if (elapsed == FadeInStartSeconds * SimulationTiming.TicksPerSecond) Request(context, owner, ArchesFadeInEffectId);
    }

    private static void Request(SimulationContext context, PlayerId owner, ushort effectId)
    {
        (ushort x, ushort y) = ManaRemovalLifecycle.HeartTile(context.World, context.Database, owner);
        context.RequestEffect("HeartIntro", ViewEffectRequest.AtTileCentre(
            new EffectId(effectId), owner, new TilePos(x, y), new Fp32(RequestHeight)));
    }
}
