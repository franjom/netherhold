// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation;

/// <summary>The effect generators' update, <c>0x004B1B60</c> (vtable <c>0x0066D814</c> slot <c>+0x14</c>), and
/// the generator every level start adds (<c>0x0050C201..0x0050C27A</c>), ADR-0648.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hero-parties-and-effect-generators.md")]
public static class EffectGeneratorRules
{
    /// <summary>The level-start generator's one effect, in slot 0.</summary>
    public const ushort LevelStartEffectId = 90;

    private const int SlotCount = 4;

    /// <summary><c>Level_InitTriggersAndStart</c> adds a generator after the THINGS records unless world
    /// <c>+0x7239</c> bit 0x40 (a restore) is set: corner (0x1800, 0x1800, 0x2000), the map's width and
    /// height less two, frequency 1, effect 90 in slot 0. Its id is the number of generators already
    /// linked (the length of list <c>+0x16</c>).</summary>
    internal static void InitializeLevelStart(WorldState world) =>
        world.AddEffectGenerator(new EffectGeneratorState
        {
            // DK2 gives it no THINGS tile; X and Y stay zero and nothing reads them.
            Unknown_0010 = (ushort)(world.Width - 2), Unknown_0012 = (ushort)(world.Height - 2),
            EffectIds = [LevelStartEffectId, 0, 0, 0],
            Frequency = 1,
            GeneratorId = (byte)world.EffectGenerators.Count,
            Unknown_001E = new byte[EffectGeneratorState.Unknown001ELength],
            PosX = 0x1800, PosY = 0x1800, PosZ = 0x2000,
        });

    /// <summary>One generator's turn. Once the turn is past its due turn: <c>random(4)</c> picks a slot
    /// (EffectGenerator.cpp line 173), and an empty one ends the turn without restamping. Then the
    /// point is the corner plus <c>random(width &lt;&lt; 12)</c> and <c>random(height &lt;&lt; 12)</c> (lines 179,
    /// 180); on a tile whose nav byte has the solid bit (world vt <c>+0x20C</c>, <c>0x005090C0</c>) the turn
    /// ends, again without restamping. Otherwise <c>random(0x800)</c> is the heading (line 188), the effect is requested for the neutral player and
    /// the generator is due again <c>Frequency</c> seconds on.</summary>
    internal static void Tick(SimulationContext context, EffectGeneratorState generator)
    {
        uint turn = unchecked((uint)(context.Tick.Value + 1));
        if (generator.NextDueTurn >= turn) return;
        int slot = context.Rng.NextInt(SlotCount);
        ushort effect = slot < generator.EffectIds.Count ? generator.EffectIds[slot] : (ushort)0;
        if (effect == 0) return;
        int x = generator.PosX + context.Rng.NextInt(generator.Unknown_0010 << 12);
        int y = generator.PosY + context.Rng.NextInt(generator.Unknown_0012 << 12);
        var tile = new TilePos(x >> 12, y >> 12);
        if (!context.World.Contains(tile) || NavBytes.IsSolid(context.Navigation.NavByte(tile.X, tile.Y))) return;
        var heading = new Dk2Angle((ushort)context.Rng.NextInt(0x800));
        context.RequestEffect("effect generator", new ViewEffectRequest(new EffectId(effect),
            new PlayerId(PlayerIds.Neutral), new FpPosition3(new Fp32(x), new Fp32(y), new Fp32(generator.PosZ)), heading));
        generator.NextDueTurn = unchecked(turn + (uint)(generator.Frequency * SimulationTiming.OriginalTicksPerGameSecond));
    }
}
