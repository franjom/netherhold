// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Presentation.Tests;

public sealed class MinimapTileClassifierTests
{
    [Fact]
    public void HudInputBlocksGlobalToolsAndCancelsAnUnfinishedWorldDrag()
    {
        var input = new PlayerInputTranslator();
        var at = new TilePos(0, 0);
        var snapshot = new PlayerInputSnapshot(false, excavation: new ExcavationTarget(at, false));
        var create = new PlayerIntent(PlayerIds.FirstKeeper, PlayerToolMode.CreateImp, null,
            PointerButtonTransition.Pressed, PointerOverHud: true);
        Assert.Empty(input.Translate(create, snapshot).All);
        var press = new PlayerIntent(PlayerIds.FirstKeeper, PlayerToolMode.Hand, at, PointerButtonTransition.Pressed);
        Assert.Empty(input.Translate(press, snapshot).All);
        Assert.NotNull(input.DragFrom(PlayerToolMode.Hand));
        Assert.Empty(input.Translate(press with { PointerTile = null, PrimaryButton = PointerButtonTransition.Released,
            PointerOverHud = true }, snapshot).All);
        Assert.Null(input.DragFrom(PlayerToolMode.Hand));
        Assert.Empty(input.Translate(press with { PrimaryButton = PointerButtonTransition.Released }, snapshot).All);
    }

    private static TerrainDef Terrain(TerrainFlags flags = 0, ushort gold = 0) => new()
    {
        Name = "Test", TerrainId = 1, Flags = flags, StartingHealth = 10, MaxHealth = 10,
        GoldValue = gold, ManaGain = 0, MaxManaGain = 0, Damage = 0,
        MaxHealthTypeTerrainId = 1, DestroyedTypeTerrainId = 1, Depth = new Fp32(0),
        NameStringId = 0, TooltipStringId = 0,
    };

    private sealed class FloorGrid : INavByteGrid
    {
        public int Width => 1;
        public int Height => 1;
        public byte NavByte(int x, int y) => NavBytes.Floor;
    }

    [Fact]
    public void WorldViewUsesViewerFogAndQueuedTagPreviewsWithoutMutatingTheWorld()
    {
        var database = new GameDatabase([Terrain(TerrainFlags.Solid | TerrainFlags.Taggable)], [], [], new GlobalVariables());
        var world = WorldState.FromMap(database, new LevelMap(1, 1, [new MapTile(1, PlayerIds.Neutral, 0, 0)]),
            [PlayerIds.Good, PlayerIds.Neutral], fogOfWarEnabled: true);
        var view = new WorldView(database, PlayerIds.FirstKeeper);
        var at = new TilePos(0, 0);
        var navigation = new FloorGrid();
        world.Tile(at).ClearExploredBy(PlayerIds.FirstKeeper);
        world.Tile(at).SetTaggedBy(new PlayerId(4), true);
        Assert.Equal(MinimapColourClass.Unknown, view.MinimapColourAt(world, navigation, at));
        view.QueueTagPreview(TagTilesCommand.Tag(PlayerIds.FirstKeeper, at), new GameTick(1));
        Assert.Equal(MinimapColourClass.Tagged, view.MinimapColourAt(world, navigation, at));
        Assert.False(world.Tile(at).IsTaggedBy(PlayerIds.FirstKeeper));
        view.AcknowledgeTagPreviews(new GameTick(1));
        Assert.Equal(MinimapColourClass.Unknown, view.MinimapColourAt(world, navigation, at));
        world.Tile(at).SetExploredBy(PlayerIds.FirstKeeper);
        Assert.Equal(new MinimapColourClass(4), view.MinimapColourAt(world, navigation, at));
        world.Tile(at).ClearExploredBy(PlayerIds.FirstKeeper);
        var withoutFog = WorldState.FromMap(database,
            new LevelMap(1, 1, [new MapTile(1, PlayerIds.Neutral, 0, 0)]), [PlayerIds.Good]);
        Assert.Equal(new MinimapColourClass(4), view.MinimapColourAt(withoutFog, navigation, at));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RoomTypeId.Portal)]
    [InlineData(RoomTypeId.DungeonHeart)]
    public void HiddenRoomsAndHeartsDoNotLeakThroughTheTileColours(RoomTypeId? room)
    {
        var facts = new MinimapTileFacts(Terrain(), room, new PlayerId(4), default);
        Assert.Equal(MinimapColourClass.Unknown, MinimapTileClassifier.Classify(facts));
        Assert.Equal(MinimapColourClass.Unknown, MinimapTileClassifier.Classify(
            facts with { Flags = MinimapTileFlags.Scanned }));
        Assert.Equal(MinimapColourClass.Tagged, MinimapTileClassifier.Classify(
            facts with { Flags = MinimapTileFlags.DigHighlighted }));
    }

    [Theory]
    [InlineData(TerrainFlags.Water, 9)]
    [InlineData(TerrainFlags.Lava, 11)]
    [InlineData(TerrainFlags.Solid, 4)]
    public void ScannedRevealTerrainUsesItsColourWithoutBeingExplored(TerrainFlags flags, ushort expected)
    {
        var facts = new MinimapTileFacts(Terrain(flags | TerrainFlags.RevealThroughFogOfWar),
            Room: null, Owner: PlayerIds.Neutral, Flags: MinimapTileFlags.Scanned);
        Assert.Equal(new MinimapColourClass(expected), MinimapTileClassifier.Classify(facts));
        Assert.Equal(MinimapColourClass.Unknown, MinimapTileClassifier.Classify(facts with { Flags = default }));
    }

    [Theory]
    [InlineData(1, 27, 18, 36)]
    [InlineData(3, 29, 20, 38)]
    [InlineData(7, 33, 24, 42)]
    public void OwnedColoursUseTheOwnerNumberAndNavigationBlockedBit(byte owner,
        ushort floor, ushort wall, ushort heart)
    {
        var facts = new MinimapTileFacts(Terrain(), Room: null, Owner: new PlayerId(owner),
            Flags: MinimapTileFlags.Explored);
        Assert.Equal(new MinimapColourClass(floor), MinimapTileClassifier.Classify(facts));
        Assert.Equal(new MinimapColourClass(wall), MinimapTileClassifier.Classify(
            facts with { Flags = facts.Flags | MinimapTileFlags.NavigationSolid }));
        Assert.Equal(new MinimapColourClass(heart), MinimapTileClassifier.Classify(
            facts with { Room = RoomTypeId.DungeonHeart, Flags = facts.Flags | MinimapTileFlags.NavigationSolid }));
        Assert.Equal(new MinimapColourClass(floor), MinimapTileClassifier.Classify(
            facts with { Terrain = Terrain(TerrainFlags.Solid) }));
    }

    [Fact]
    public void NeutralRoomColourPrecedesTerrainAndNeutralPathUsesTheLoaderClaimBit()
    {
        var facts = new MinimapTileFacts(Terrain(TerrainFlags.Water), RoomTypeId.Portal,
            Owner: PlayerIds.Neutral, Flags: MinimapTileFlags.Explored);
        Assert.Equal(new MinimapColourClass(28), MinimapTileClassifier.Classify(facts));
        facts = facts with { Room = null, Terrain = Terrain() };
        Assert.Equal(new MinimapColourClass(0), MinimapTileClassifier.Classify(facts));
        facts = facts with { Terrain = facts.Terrain with { MaxHealth = 20, MaxHealthTypeTerrainId = 2 } };
        Assert.Equal(new MinimapColourClass(2), MinimapTileClassifier.Classify(facts));
        Assert.Equal(new MinimapColourClass(2), MinimapTileClassifier.Classify(facts with { Owner = PlayerIds.Unowned }));
    }

    [Theory]
    [InlineData(TerrainFlags.Solid, 0, 4)]
    [InlineData(TerrainFlags.Solid | TerrainFlags.Impenetrable, 0, 1)]
    [InlineData(TerrainFlags.Solid, 100, 7)]
    [InlineData(TerrainFlags.Solid | TerrainFlags.Impenetrable, 100, 5)]
    [InlineData(TerrainFlags.Water | TerrainFlags.Lava, 0, 9)]
    [InlineData(TerrainFlags.Lava, 0, 11)]
    public void NeutralTerrainFollowsTheNativeBranchOrder(TerrainFlags flags, ushort gold, ushort expected)
    {
        var facts = new MinimapTileFacts(Terrain(flags, gold), Room: null,
            Owner: PlayerIds.Neutral, Flags: MinimapTileFlags.Explored);
        Assert.Equal(new MinimapColourClass(expected), MinimapTileClassifier.Classify(facts));
    }
}
