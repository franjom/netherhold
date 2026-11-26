// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>What a possession changes in the drawn scene (<see cref="PossessionScene"/>).</summary>
public sealed class PossessionSceneTests
{
    private const int Width = 8, Height = 6;
    private static readonly PlayerId Viewer = new(PlayerIds.FirstKeeper);

    private static (GameDatabase Database, WorldState World) Staged()
    {
        TerrainDef rock = new()
        {
            Name = "rock", TerrainId = 2, Flags = TerrainFlags.Solid, StartingHealth = 1, MaxHealth = 1, GoldValue = 0,
            ManaGain = 0, MaxManaGain = 0, Damage = 0, MaxHealthTypeTerrainId = 2, DestroyedTypeTerrainId = 2,
            Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        };
        var variables = new GlobalVariables();
        foreach (VariableTypeId id in Enum.GetValues<VariableTypeId>())
        {
            variables.Misc[id] = new MiscVariable(id, 0, 0, 0);
        }

        var database = new GameDatabase([rock], [], [], variables);
        var map = new LevelMap(Width, Height, Enumerable.Repeat(new MapTile(2, PlayerIds.Unowned, 0, 0), Width * Height).ToArray());
        return (database, WorldState.FromMap(database, map, [PlayerIds.FirstKeeper]));
    }

    [Fact]
    public void TheLatchIsSetWhileTheViewerPossessesACreature()
    {
        (_, WorldState world) = Staged();

        Assert.False(PossessionScene.Latched(world, Viewer));
        world.Player(Viewer).PossessedCreatureId = 7;
        Assert.True(PossessionScene.Latched(world, Viewer));
    }

    [Fact]
    public void TheLatchGoesToTheViewAndTheLatticeAndComesOffWithThePossession()
    {
        (GameDatabase database, WorldState world) = Staged();
        var view = new WorldView(database, Viewer);
        var lattice = new TerrainLattice(database, world, Viewer);
        int version = view.PreviewVersion;

        PossessionScene.Latch(world, Viewer, view, lattice, terrain: null);
        Assert.False(lattice.Possession);
        Assert.Equal(version, view.PreviewVersion);

        world.Player(Viewer).PossessedCreatureId = 7;
        PossessionScene.Latch(world, Viewer, view, lattice, terrain: null);
        Assert.True(lattice.Possession);
        // The view presents every tile again on the change, and only on it.
        Assert.Equal(version + 1, view.PreviewVersion);
        PossessionScene.Latch(world, Viewer, view, lattice, terrain: null);
        Assert.Equal(version + 1, view.PreviewVersion);

        world.Player(Viewer).PossessedCreatureId = 0;
        PossessionScene.Latch(world, Viewer, view, lattice, terrain: null);
        Assert.False(lattice.Possession);
        Assert.Equal(version + 2, view.PreviewVersion);
    }

    [Fact]
    public void AHostWithoutALatticeStillLatchesItsView()
    {
        (GameDatabase database, WorldState world) = Staged();
        var view = new WorldView(database, Viewer);
        int version = view.PreviewVersion;
        world.Player(Viewer).PossessedCreatureId = 7;

        PossessionScene.Latch(world, Viewer, view, lattice: null, terrain: null);

        Assert.Equal(version + 1, view.PreviewVersion);
    }

    [Fact]
    public void LegacyModelOnlyCallersRetainTheirSettledRoofGate()
    {
        Assert.False(PossessionScene.RoofShown((PossessionSession?)null));
        Assert.False(PossessionScene.RoofShown(0));
        Assert.True(PossessionScene.RoofShown(12));
    }
}
