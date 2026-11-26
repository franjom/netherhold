// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

/// <summary>The engine-free full map's backdrop, room icon and legend (ADR-0805).</summary>
public sealed class FullMapHudTests
{
    private static readonly HudScreen FullHd = new(1920, 1080);
    private static readonly BitmapFont Font = new(10, [new BitmapGlyph('A', 2, 2, 0, 0, 3, [15, 15, 15, 15])]);

    private static MinimapPalette Palette() =>
        new(Enumerable.Range(0, 37).Select(i => (uint)(i * 0x010203)).ToArray());

    /// <summary>An 8x8 rock map with a 3x3 treasury from <paramref name="first"/> (2..4 by default) owned by player 2;
    /// keepers 2 and 3, no fog.</summary>
    private static (GameDatabase, WorldState) Map(int first = 2)
    {
        TerrainDef Terrain(byte id, TerrainFlags flags) => new()
        {
            Name = id == 27 ? "Fog Of War" : id == 38 ? "Claim Scan" : "test", TerrainId = id, Flags = flags,
            StartingHealth = 1, MaxHealth = 1, GoldValue = 0, ManaGain = 0, MaxManaGain = 0, Damage = 0,
            MaxHealthTypeTerrainId = id, DestroyedTypeTerrainId = id, Depth = new Fp32(0), NameStringId = 0, TooltipStringId = 0,
        };
        var room = new RoomDef
        {
            Name = "test", RoomId = (byte)RoomTypeId.Treasury, TerrainId = 12, Flags = RoomFlags.PlaceableOnLand,
            Construction = TileConstruction.ThreeByThree, Cost = 0, ReturnPercentage = 0,
            CreatedCreatureId = 0, ObjectIds = [], HealthGain = 0, CeilingHeight = new Fp32(0),
            RecommendedSizeX = 3, RecommendedSizeY = 3, OrderInEditor = 0, NameStringId = 0, TooltipStringId = 0,
        };
        var variables = new GlobalVariables();
        variables.Misc[VariableTypeId.DungeonHeartObjectHealth] = new(VariableTypeId.DungeonHeartObjectHealth, 100, 0, 0);
        var db = new GameDatabase([Terrain(2, TerrainFlags.Solid), Terrain(12, TerrainFlags.Room | TerrainFlags.Ownable | TerrainFlags.Attackable),
            Terrain(27, TerrainFlags.Solid), Terrain(38, TerrainFlags.Solid)], [room], [], variables);
        MapTile[] cells = Enumerable.Repeat(new MapTile(2, 2, 0, 0), 64).ToArray();
        for (int y = first; y < first + 3; y++)
        {
            for (int x = first; x < first + 3; x++)
            {
                cells[y * 8 + x] = new MapTile(12, 2, 0, 0);
            }
        }

        return (db, WorldState.FromMap(db, new LevelMap(8, 8, cells), [2, 3], fogOfWarEnabled: false));
    }

    private static (HudDraw[] Back, HudDraw[] Front) Build(FullMapHud hud)
    {
        HudFrame back = new(), front = new();
        hud.Build(FullHd, back, front);
        return (back.Draws.ToArray(), front.Draws.ToArray());
    }

    private static HudRect Pixels(float x, float y, float w, float h) =>
        new(FullHd.Virtual(x), FullHd.Virtual(y), FullHd.Virtual(w), FullHd.Virtual(h));

    [Fact]
    public void HiddenItDrawsNothingAndShownItBlacksOutTheScreenUnderTheMap()
    {
        var hud = new FullMapHud(Palette(), 8, 8);
        Assert.Empty(Build(hud).Back);
        hud.SetVisible(true);
        (HudDraw[] back, HudDraw[] front) = Build(hud);
        HudDraw fill = Assert.Single(back);
        Assert.Equal(HudDrawKind.Fill, fill.Kind);
        Assert.Equal(new HudColor(0, 0, 0), fill.Tint);
        Assert.Equal(new HudRect(0, 0, FullHd.VirtualWidth, 1920), fill.Rect);
        Assert.Empty(front);
    }

    [Fact]
    public void TheHoveredRoomShowsItsIconOverItsCentreAndTheKeepersAreListedInTheirColours()
    {
        (GameDatabase db, WorldState world) = Map();
        var hud = new FullMapHud(Palette(), 8, 8);
        hud.SetLegend(_ => Font, _ => "AA", roomIcons: true);
        hud.SetVisible(true);
        hud.Capture(world, db, new PlayerId(3), new TilePos(2, 3));
        HudDraw[] front = Build(hud).Front;
        Assert.Equal(2, front.Length);
        // The map fits 864 pixels square at (267, 74); the 96x72 icon centres on the room's centre field (3, 3),
        // that tile's top-left corner: the marker carries no half-tile offset.
        Assert.Equal("gui\\rooms\\treasury", front[0].Image.Path);
        Assert.Equal(Pixels(543, 362, 96, 72), front[0].Rect);
        // Keeper 3 heads the legend at (474, 274) of 640x480, in palette entry 26 + 3.
        Assert.Equal("AA", front[1].Image.Text!.Text);
        Assert.Equal(Pixels(1422, 616, 5, 10), front[1].Rect);
        uint colour = Palette().Colours[29];
        Assert.Equal(new HudColor((byte)(colour >> 16), (byte)(colour >> 8), (byte)colour), front[1].Tint);

        hud.Capture(world, db, new PlayerId(3), new TilePos(6, 6));   // rock: no room
        Assert.DoesNotContain(Build(hud).Front, d => d.Image.Path != null);
        hud.SetLegend(null, null, roomIcons: false);
        hud.Capture(world, db, new PlayerId(3), new TilePos(2, 3));
        Assert.Empty(Build(hud).Front);
    }

    [Fact]
    public void ATileOnTheMapsBorderShowsNoRoomEvenWhereOneIs()
    {
        // The treasury covers 0..2: its tiles on row and column 0 are on the border.
        (GameDatabase db, WorldState world) = Map(first: 0);
        var hud = new FullMapHud(Palette(), 8, 8);
        hud.SetLegend(null, null, roomIcons: true);
        hud.SetVisible(true);
        hud.Capture(world, db, new PlayerId(3), new TilePos(1, 1));
        Assert.Single(Build(hud).Front);
        foreach (TilePos border in new[] { new TilePos(0, 1), new TilePos(1, 0), new TilePos(0, 0) })
        {
            hud.Capture(world, db, new PlayerId(3), border);
            Assert.Empty(Build(hud).Front);
        }
    }

    [Fact]
    public void TheLegendIsSetAgainInTheFontForANewScreenHeight()
    {
        (GameDatabase db, WorldState world) = Map();
        var asked = new List<int>();
        var hud = new FullMapHud(Palette(), 8, 8);
        hud.SetLegend(height => { asked.Add(height); return Font; }, _ => "AA", roomIcons: false);
        hud.SetVisible(true);
        hud.Capture(world, db, new PlayerId(3), default);
        Build(hud); Build(hud);
        HudFrame back = new(), front = new();
        hud.Build(new HudScreen(1024, 768), back, front);
        Assert.Equal([1080, 768], asked);
        // 1024 * 474 / 640 and 768 * 274 / 480, in 768's virtual units.
        var small = new HudScreen(1024, 768);
        Assert.Equal(new HudRect(small.Virtual(758), small.Virtual(438), small.Virtual(5), small.Virtual(10)), Assert.Single(front.Draws.ToArray()).Rect);
    }

    [Fact]
    public void APixelResolvesToTheTileUnderItOnTheFittedMap()
    {
        var hud = new FullMapHud(Palette(), 8, 8);
        Assert.Equal(new TilePos(0, 0), hud.ResolvePointer(1920, 1080, new(267, 74)));
        Assert.Equal(new TilePos(7, 7), hud.ResolvePointer(1920, 1080, new(267 + 863, 74 + 863)));
    }

    [Fact]
    public void ALeftPressGoesToTheTileARightPressClosesAndTheLeftWins()
    {
        Assert.Equal(FullMapPress.None, FullMapHud.Press(primary: false, secondary: false));
        Assert.Equal(FullMapPress.GoToTile, FullMapHud.Press(primary: true, secondary: false));
        Assert.Equal(FullMapPress.Close, FullMapHud.Press(primary: false, secondary: true));
        Assert.Equal(FullMapPress.GoToTile, FullMapHud.Press(primary: true, secondary: true));
        Assert.Equal(ClassicHudAction.None, FullMapHud.Action(FullMapPress.None));
        Assert.Equal(ClassicHudAction.ToggleFullMap, FullMapHud.Action(FullMapPress.Close));
        Assert.Equal(ClassicHudAction.ToggleFullMap, FullMapHud.Action(FullMapPress.GoToTile));
    }

    private sealed class FloorGrid : DK2.Pathfinding.INavByteGrid
    {
        public int Width => 8;
        public int Height => 8;
        public byte NavByte(int x, int y) => 0;
    }

    [Fact]
    public void TheSharedMinimapCaptureReadsTheGridAndMarkersOncePerCaptureKey()
    {
        (GameDatabase db, WorldState world) = Map();
        var minimap = new MinimapHud(Palette(), 8, 8);
        var view = new WorldView(db, new PlayerId(3));
        Assert.True(minimap.Capture(world, view, new FloorGrid(), out _));
        Assert.Equal(new MinimapColourClass(26 + 2), minimap.Grid[new TilePos(3, 3)]);   // player 2's treasury
        Assert.NotEmpty(minimap.Markers);                                                 // the viewer's Heart ring
        Assert.False(minimap.Capture(world, view, new FloorGrid(), out _));              // same tick: nothing to read
        (_, WorldState next) = Map();
        Assert.True(minimap.Capture(next, view, new FloorGrid(), out _));                // a new key (another world, as a new tick)
    }

    [Fact]
    public void RoomArtIsTheSameStringEachTimeAndNoneForAnUnlistedRoom()
    {
        Assert.Equal("gui\\rooms\\treasury", ClassicFullMapMarkers.RoomArt(RoomTypeId.Treasury));
        Assert.Same(ClassicFullMapMarkers.RoomArt(RoomTypeId.Treasury), ClassicFullMapMarkers.RoomArt(RoomTypeId.Treasury));
        Assert.Null(ClassicFullMapMarkers.RoomArt((RoomTypeId)0));
        for (byte unlisted = 18; unlisted <= 20; unlisted++)
        {
            Assert.Null(ClassicFullMapMarkers.RoomArt((RoomTypeId)unlisted));
        }

        Assert.Equal("gui\\rooms\\Bridge_Wooden", ClassicFullMapMarkers.RoomArt((RoomTypeId)22));
        Assert.Null(ClassicFullMapMarkers.RoomArt((RoomTypeId)200));
    }
}
