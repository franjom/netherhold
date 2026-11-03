// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The neutral player's frame (<c>Player_Tick</c> <c>0x004B9EC0</c> returns through
/// <c>Player_GenerateAmbientEffect</c> <c>0x004BB130</c> for the neutral tag): every turn it draws an
/// interior tile from the world stream, <c>random(width - 2) + 1</c> then <c>random(height - 2) + 1</c>
/// (<c>0x004BB156</c>, <c>0x004BB181</c>), and on a Lava tile (world vt <c>+0x2C0</c>) draws
/// <c>random(1000)</c>: 0 to 3 pick effects 316 to 319 on the floor, anything else effect 302 at height
/// 0x1000; then <c>random(0x800)</c> is the heading. Those draws are read whole; that the effect's creation
/// through the presentation interface draws nothing more from the world stream is STRONGLY INFERRED
/// (neutral-ambient-effect.md), so a lava hit costs four draws and a miss two (ADR-0646).
/// <para>DK2 runs it at the neutral player's place in the active list. Ours runs it after the keepers'
/// frames and verdicts, which is that place when the PLAYERS records list the neutral player before
/// every keeper, as the level files do (the active list is their reverse). The heroes' frame after it
/// draws nothing.</para></summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/neutral-ambient-effect.md")]
public sealed class NeutralAmbientEffectSystem : ISimulationSystem
{
    private const int LavaRareRange = 1000;
    private const int RareEffectCount = 4;
    private const int FirstRareEffect = 316;
    private const int CommonEffect = 302;
    private static readonly Fp32 CommonEffectHeight = new(0x1000);

    public string Name => "neutral ambient";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        if (!IsActive(world)) return;
        int x = context.Rng.NextInt(world.Width - 2) + 1;
        int y = context.Rng.NextInt(world.Height - 2) + 1;
        TerrainDef? lava = context.Database.LavaTerrain;
        if (lava is null || world.Tile(new TilePos(x, y)).TerrainId != lava.TerrainId) return;

        int roll = context.Rng.NextInt(LavaRareRange);
        (int effect, Fp32 height) = roll < RareEffectCount
            ? (FirstRareEffect + roll, new Fp32(0))
            : (CommonEffect, CommonEffectHeight);
        var heading = new Dk2Angle((ushort)(context.Rng.NextInt(0x800) & 0x7FF));
        context.RequestEffect(Name, ViewEffectRequest.AtTileCentre(new EffectId((ushort)effect),
            new PlayerId(PlayerIds.Neutral), new TilePos(x, y), height, heading));
    }

    /// <summary><c>0x004C2310</c> runs the frames of the active list only.</summary>
    private static bool IsActive(WorldState world)
    {
        foreach (PlayerId id in world.ActivePlayerOrder)
            if (id.Value == PlayerIds.Neutral) return true;
        return false;
    }
}
