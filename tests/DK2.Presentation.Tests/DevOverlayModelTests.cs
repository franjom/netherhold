// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

public sealed class DevOverlayModelTests
{
    private static string Text(DevOverlayModel model, string label)
    {
        for (int i = 0; i < model.RowCount; i++)
        {
            if (model.Kind(i) == DevOverlayRowKind.Row && model.Label(i) == label)
            {
                return model.Value(i).ToString();
            }
        }

        throw new InvalidOperationException("No row " + label);
    }

    private static string[] Headings(DevOverlayModel model)
    {
        var headings = new List<string>();
        for (int i = 0; i < model.RowCount; i++)
        {
            if (model.Kind(i) == DevOverlayRowKind.Heading)
            {
                headings.Add(model.Label(i));
            }
        }

        return [.. headings];
    }

    [Fact]
    public void TheTextRefreshesFourTimesASecond()
    {
        var model = new DevOverlayModel();
        Assert.True(model.EndFrame(0.1, -1));   // the first frame shows at once
        Assert.False(model.EndFrame(0.1, -1));
        Assert.False(model.EndFrame(0.1, -1));
        Assert.True(model.EndFrame(0.1, -1));
    }

    [Fact]
    public void PerformanceShowsTheLastFullSecond()
    {
        var model = new DevOverlayModel();
        for (int i = 0; i < 7; i++)
        {
            model.AddTurns(i % 2, 1.0);
            model.AddPart(FrameTelemetryPart.Present, 2.0);
            model.AddPart(FrameTelemetryPart.Lights, 1.0);
            model.AddPart(FrameTelemetryPart.Hud, 0.5);
            model.AddPart(FrameTelemetryPart.Sound, 0.25);
            model.EndFrame(i == 3 ? 0.25 : 0.125, 2.0);
        }
        model.EndFrame(0.05, 4.0);   // the next window's first frame: the shown numbers stay the full window's
        model.Compose(new DevOverlayFacts { Scene = DevOverlayScene.InGame, GpuMeasured = true, GameSpeed = 4, Level = "Level1" });
        Assert.Equal(["Performance", "Game", "Hovered tile", "Determinism"], Headings(model));
        Assert.Equal("7", Text(model, "Frames per second"));
        Assert.Equal("50.0 ms now, 142.9 average, 250.0 worst", Text(model, "Frame time"));
        Assert.Equal("4.00 ms now, 2.00 average", Text(model, "GPU time"));
        Assert.Equal("turns 1.00, world 3.00, HUD 0.50, other 0.25 ms", Text(model, "CPU time"));
        Assert.Equal("3.0 of 4.0 wanted", Text(model, "Turns per second"));
    }

    [Fact]
    public void GameRowsSayPlainWhatIsHappening()
    {
        var model = new DevOverlayModel();
        model.EndFrame(0.25, -1);
        model.Compose(new DevOverlayFacts
        {
            Scene = DevOverlayScene.InGame, Level = "Level7", Turn = new GameTick(1234), GameSpeed = 8, Paused = true,
            CameraPath = true, DrawCalls = 812, Objects = 301, VideoMemoryMegabytes = 455.6,
        });
        Assert.Equal("not measured", Text(model, "GPU time"));
        Assert.Equal("812, 301 objects, 456 MB video memory", Text(model, "Draw calls"));
        Assert.Equal("Level7", Text(model, "Level"));
        Assert.Equal("1234", Text(model, "Turn"));
        Assert.Equal("8, paused", Text(model, "Game speed"));
        Assert.Equal("0.0 of 0.0 wanted", Text(model, "Turns per second"));
        Assert.Equal("playing", Text(model, "Camera path"));
        Assert.Equal("none (the pointer is over the HUD or off the map)", Text(model, "Tile"));
        Assert.Equal("press F4 to compute it", Text(model, "State hash"));
        Assert.Equal(DevOverlayRowKind.Hint, model.Kind(model.RowCount - 1));
    }

    [Fact]
    public void TheHashShowsInHexWithItsTurnUntilCleared()
    {
        var model = new DevOverlayModel();
        model.SetHash(0x4C47C91DFEA7F649, new GameTick(400));
        model.Compose(new DevOverlayFacts { Scene = DevOverlayScene.InGame });
        Assert.Equal("4C47C91DFEA7F649 at turn 400", Text(model, "State hash"));
        model.SetHash(0x1, new GameTick(0));
        model.Compose(new DevOverlayFacts { Scene = DevOverlayScene.InGame });
        Assert.Equal("0000000000000001 at turn 0", Text(model, "State hash"));
        model.ClearHash();
        model.Compose(new DevOverlayFacts { Scene = DevOverlayScene.InGame });
        Assert.False(model.HashKnown);
    }

    [Fact]
    public void TheFrontEndShowsPerformanceAndItsPage()
    {
        var model = new DevOverlayModel();
        model.Compose(new DevOverlayFacts { Scene = DevOverlayScene.FrontEnd, Page = FrontEndPage.CampaignMap });
        Assert.Equal(["Performance", "Front end"], Headings(model));
        Assert.Equal("CampaignMap", Text(model, "Page"));
        Assert.Throws<InvalidOperationException>(() => Text(model, "CPU time"));
        model.Compose(new DevOverlayFacts { Scene = DevOverlayScene.FrontEnd });
        Assert.Equal("starting", Text(model, "Page"));
    }

    [Theory]
    [InlineData(4, false, 4.0)]
    [InlineData(8, false, 8.0)]
    [InlineData(1, false, 1.0)]
    [InlineData(16, true, 0.0)]
    public void TheWantedRateFollowsTheGameSpeed(int speed, bool paused, double expected)
    {
        Assert.Equal(expected, DevOverlayModel.TargetTurnsPerSecond(speed, paused));
    }

    [Fact]
    public void AValuePastItsBufferIsCutNotThrown()
    {
        var model = new DevOverlayModel();
        model.Compose(new DevOverlayFacts { Scene = DevOverlayScene.InGame, Level = new string('L', 200) });
        Assert.Equal(DevOverlayModel.ValueCapacity, Text(model, "Level").Length);
    }

    [InstallFact]
    public void TheHoveredTileNamesItsTerrainOwnerAndRoom()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        RoomRules.Place(db, ref world.Tile(10, 10), db.Room(RoomTypeId.Library), PlayerIds.FirstKeeper);
        ref TileState rock = ref world.Tile(11, 10);
        rock.TerrainId = 1; rock.OwnerPlayerId = PlayerIds.Unowned;
        var model = new DevOverlayModel();
        int asked = 0;
        model.UsePlayerNames(id => { asked++; return "Keeper " + id; });
        var facts = new DevOverlayFacts
        {
            Scene = DevOverlayScene.InGame, World = world, Database = db, HoverTile = new TilePos(10, 10),
        };
        model.Compose(facts);
        Assert.Equal("10, 10", Text(model, "Tile"));
        Assert.Equal(db.TerrainById(world.Tile(10, 10).TerrainId).Name, Text(model, "Terrain"));
        Assert.Equal("Keeper 3 (player 3)", Text(model, "Owner"));
        Assert.Equal(db.Room(RoomTypeId.Library).Name, Text(model, "Room"));
        model.Compose(facts);
        Assert.Equal(1, asked);
        facts.HoverTile = new TilePos(11, 10);
        model.Compose(facts);
        Assert.Equal("nobody", Text(model, "Owner"));
        Assert.Equal("none", Text(model, "Room"));
    }

    [Fact]
    public void ARunningOverlayAllocatesNothing()
    {
        var model = new DevOverlayModel();
        model.UsePlayerNames(id => "Keeper " + id);
        var facts = new DevOverlayFacts
        {
            Scene = DevOverlayScene.InGame, Level = "Level1", Turn = new GameTick(77), GameSpeed = 4, GpuMeasured = true,
        };
        model.SetHash(123, new GameTick(70));
        // Warm: the first composition may compile and cache.
        model.EndFrame(0.3, 1);
        model.Compose(facts);
        var frontEnd = new DevOverlayFacts { Scene = DevOverlayScene.FrontEnd, Page = FrontEndPage.Main };
        model.Compose(frontEnd);
        Run(model, ref facts, frontEnd);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Run(model, ref facts, frontEnd);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void Run(DevOverlayModel model, ref DevOverlayFacts facts, in DevOverlayFacts frontEnd)
    {
        for (int i = 0; i < 100; i++)
        {
            model.AddTurns(1, 0.5);
            model.AddPart(FrameTelemetryPart.Hud, 0.25);
            if (model.EndFrame(0.1, 1.5))
            {
                model.Compose(facts);
            }

            facts.Turn = new GameTick(77 + i);
        }
        model.Compose(frontEnd);
    }
}
