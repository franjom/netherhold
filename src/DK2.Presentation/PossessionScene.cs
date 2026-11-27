// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>What a possession changes in the drawn scene, which a host applies each frame around its
/// present: the possession cameras' latch before it, the roof after it (ADR-0805).</summary>
public static class PossessionScene
{
    /// <summary>The possession cameras' latch as the hosts set it: while the viewer possesses a creature.</summary>
    public static bool Latched(WorldState world, PlayerId viewer)
    {
        return world.Player(viewer).PossessedCreatureId != 0;
    }

    /// <summary>Gives the latch to the view, whose change presents every tile again, and to the
    /// lattice, which rests the walls at full height (<see cref="TerrainLattice.Possession"/>). Flipping
    /// the lattice forgets every cell, which the terrain's thread-pool build may be reading: it is
    /// joined first. Call before the present.</summary>
    public static void Latch(WorldState world, PlayerId viewer, WorldView view, TerrainLattice? lattice,
        TerrainChunkScheduler? terrain)
    {
        bool latched = Latched(world, viewer);
        view.SetPossessionView(latched);
        if (lattice == null || lattice.Possession == latched)
        {
            return;
        }

        terrain?.JoinBuild();
        lattice.Possession = latched;
    }

    /// <summary>The roof's frame flag is set when either end of a camera move is a possession
    /// mode (0x0044AB60); it does not wait for the creature model to disappear on landing.</summary>
    public static bool RoofShown(PossessionSession? session)
    {
        return session is { CameraHeld: true };
    }

    /// <summary>The legacy model-only gate for hosts that do not supply their camera session.</summary>
    public static bool RoofShown(int hiddenCreatureId)
    {
        return hiddenCreatureId != 0;
    }
}
