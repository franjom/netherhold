// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>
/// The tile pass asks only the tiles whose inputs changed (<see cref="ScenePresenter.IncrementalTiles"/>);
/// it must push exactly what asking every tile on every change pushes. Played on a campaign level
/// with fog, digging round the heart, a tile written between ticks and a Sight region opening and
/// closing, both presenters watching the same world.
/// </summary>
public class IncrementalTilePassTests
{
    [InstallFact]
    public void TheIncrementalTilePassPushesWhatTheFullPassPushes()
    {
        string install = Dk2InstallGate.Path;
        const string level = "Level6a";
        GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, level);
        GameSimulation simulation = CampaignSimulationFactory.Create(database,
            GameDatabaseLoader.LoadCampaignMap(install, level)!, GameDatabaseLoader.LoadCampaignLevel(install, level)!,
            GameDatabaseLoader.LoadCampaignPlayers(install, level)!, GameDatabaseLoader.LoadCampaignThings(install, level)!,
            GameDatabaseLoader.LoadCampaignTriggers(install, level)!, 1, fogOfWarEnabled: true);
        WorldState world = simulation.World;
        var keeper = new PlayerId(PlayerIds.FirstKeeper);

        var incrementalView = new WorldView(database, keeper);
        var fullView = new WorldView(database, keeper);
        var incremental = new Recorder();
        var full = new Recorder();
        var incrementalPresenter = new ScenePresenter(incrementalView, AssetProviderChain.Standard(), incremental,
            new TorchLayout(database));
        var fullPresenter = new ScenePresenter(fullView, AssetProviderChain.Standard(), full,
            new TorchLayout(database)) { IncrementalTiles = false };

        int partial = 0;
        void Present(string when)
        {
            incrementalPresenter.Present(world);
            fullPresenter.Present(world);
            Assert.True(full.Calls.SequenceEqual(incremental.Calls) && full.Pieces.SequenceEqual(incremental.Pieces),
                $"{when}: the full pass pushed {full.Calls.Count} calls, the incremental {incremental.Calls.Count}");
            if (incrementalPresenter.TilesAsked > 0 && incrementalPresenter.TilesAsked < world.TileCount) partial++;
            full.Calls.Clear();
            incremental.Calls.Clear();
            full.Pieces.Clear();
            incremental.Pieces.Clear();
        }

        Present("first");
        TilePos heart = Heart(world, database);
        var tagged = new List<TilePos>();
        for (int y = Math.Max(0, heart.Y - 10); y <= Math.Min(world.Height - 1, heart.Y + 10); y++)
            for (int x = Math.Max(0, heart.X - 10); x <= Math.Min(world.Width - 1, heart.X + 10); x++)
                if (database.TerrainById(world.Tile(x, y).TerrainId).IsSolid) tagged.Add(new TilePos(x, y));
        int pushed = 0;
        for (int t = 0; t < 400; t++)
        {
            var input = t == 2
                ? new InputFrame([new TagTilesCommand(keeper, tagged, true)])
                : InputFrame.Empty;
            simulation.Tick(input);
            Present($"tick {t}");
            pushed += incrementalPresenter.TilesPushed;
            Present($"tick {t}, again");
            Assert.Equal(0, incrementalPresenter.TilesPushed);

            if (t == 100)
            {
                // Written between ticks, as a tool would: the room facts stay the tick's until the next.
                ref TileState tile = ref world.Tile(heart.X + 3, heart.Y);
                tile.OwnerPlayerId = tile.OwnerPlayerId == 1 ? (byte)PlayerIds.FirstKeeper : (byte)1;
                Present("a tile written between ticks");
                // The next tick finds the room facts the write changed, with no tile written in it.
                simulation.Tick();
                Present("the tick after the write");
            }
            if (t == 200)
            {
                foreach (WorldView view in new[] { incrementalView, fullView })
                    view.SetSightRegion(new SightRegion(world.CurrentTick, new TilePos(heart.X + 12, heart.Y), 40, 6,
                        world.Width, world.Height));
                Present("a Sight region opens");
            }
        }
        Assert.True(pushed > 100, $"the run pushed {pushed} tiles, too few to compare much");
        Assert.True(partial > 50, $"only {partial} passes asked part of the map, so the incremental pass was barely tried");
    }

    private static TilePos Heart(WorldState world, GameDatabase database)
    {
        byte heart = database.Room(RoomTypeId.DungeonHeart).TerrainId;
        for (int i = 0; i < world.TileCount; i++)
            if (world.TileAtIndex(i).TerrainId == heart && world.TileAtIndex(i).OwnerPlayerId == PlayerIds.FirstKeeper)
                return new TilePos((i % world.Width) + 2, (i / world.Width) + 2);
        throw new InvalidOperationException("no heart");
    }

    private sealed class Recorder : IRenderTarget
    {
        public readonly List<string> Calls = [];
        public readonly List<(RoomWalls, TerrainPieces, PieceSet, PieceSet)> Pieces = [];

        private void Tile(string kind, int i, in TileView t)
        {
            Pieces.Add((t.Walls, t.Pieces, t.RoomFloor, t.Bed));
            Calls.Add($"{kind} {i} {t.Ground} {t.Furniture} {t.OwnerPlayerId} {t.Tagged} {t.RedHighlighted} {t.GoldHeld} "
                + $"{t.HealthPermille} {t.Explored} {t.Scanned} {t.PointLight} {t.SightFogOverlay} "
                + $"{t.SightFogOpacity} {t.HasDoor} {t.ExtraRoomPiece} {(t.RoomKeys is null ? "-" : string.Join(",", t.RoomKeys.All()))} "
                + $"{t.Surface} {t.AmbientTint} {t.QuarterAmbientTint}");
        }

        public void CreateTile(int i, in TileView t, in ResolvedAsset g, in ResolvedAsset f) => Tile("create-tile", i, t);
        public void UpdateTile(int i, in TileView t, in ResolvedAsset g, in ResolvedAsset f) => Tile("update-tile", i, t);
        public void CreateCreature(int id, in CreatureView c, in ResolvedAsset m, in ResolvedAsset a) { }
        public void UpdateCreature(int id, in CreatureView c, in ResolvedAsset m, in ResolvedAsset a) { }
        public void RemoveCreature(int id) { }
        public void CreateObject(int id, in ObjectView o, in ResolvedAsset m) { }
        public void UpdateObject(int id, in ObjectView o, in ResolvedAsset m) { }
        public void RemoveObject(int id) { }
        public void CreateStructure(StructureId id, in StructureView s, in ResolvedAsset m) { }
        public void UpdateStructure(StructureId id, in StructureView s, in ResolvedAsset m) { }
        public void RemoveStructure(StructureId id) { }
        public void CreateTorch(int i, in TorchView t, in ResolvedAsset m) => Calls.Add($"create-torch {i}");
        public void UpdateTorch(int i, in TorchView t, in ResolvedAsset m) => Calls.Add($"update-torch {i}");
        public void RemoveTorch(int i) => Calls.Add($"remove-torch {i}");
        public void Flush() => Calls.Add("flush");
    }
}
