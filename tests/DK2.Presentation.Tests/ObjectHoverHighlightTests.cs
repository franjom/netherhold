// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The Hand's hover target as DK2 draws it (Presentation_Update 0x0043BE40;
/// gold-hover-and-sparkle.md): the hover texture of the gold arts, the red pass, and which
/// drawn objects may be picked.</summary>
public sealed class ObjectHoverHighlightTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)] // Gold_Spill / Gold_Spill_HIGHLIGHT: the phase 0xFFF draws the second
    [InlineData(4, 3)]
    public void TheForcedPhaseDrawsTheLastTexture(int textureCount, int expected) =>
        Assert.Equal(expected, ObjectHoverHighlight.HoverTextureIndex(textureCount));

    [InstallFact]
    public void TheHoverIdTakesOwnPickableObjectsAndLooseGoldOnOwnLand()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        PlayerId me = PlayerIds.FirstKeeper;
        byte rival = (byte)(me.Value + 1);
        ObjectDef gold = db.ObjectById(1)!, store = db.ObjectById(3)!, chicken = db.ObjectById(9)!, heart = db.ObjectById(13)!;
        Assert.True(HoverTint.ObjectPublished(gold, 1, 0, me.Value, me));       // a neutral pile on my tile
        Assert.False(HoverTint.ObjectPublished(gold, 1, 0, rival, me));         // ... on a rival's
        Assert.True(HoverTint.ObjectPublished(store, 3, me.Value, me.Value, me)); // my store's chest
        Assert.False(HoverTint.ObjectPublished(store, 3, rival, me.Value, me)); // type 3 has no tile rule
        Assert.True(HoverTint.ObjectPublished(chicken, 9, me.Value, me.Value, me));
        Assert.False(HoverTint.ObjectPublished(heart, 13, me.Value, me.Value, me)); // neither flag
    }

    [Fact]
    public void TheRedPassSkipsBlendedMaterialsUnlessFlag0x400_BinaryAndOwner()
    {
        Assert.True(HoverTint.DrawsOverlay(blended: false, flag0x400: false));
        Assert.False(HoverTint.DrawsOverlay(blended: true, flag0x400: false)); // the spill cards, wings
        Assert.True(HoverTint.DrawsOverlay(blended: true, flag0x400: true));   // mode 1, flags 0x600
        Assert.Equal(8, HoverTint.FlashMode1Type);
    }

    /// <summary>The hover arm's mode 1 (0x0043BEF8..0x0043BF1A): a door or trap of type 8 only.
    /// Four structures in a carved corridor of Level 1, each placed as the keeper places them.</summary>
    [InstallFact]
    public void OnlyTheFearTrapAndTheHeroPortcullisCoverBlendedMaterials()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        PlayerId me = PlayerIds.FirstKeeper;
        byte wall = db.Terrain.Values.Single(t => t.Name == "Reinforced Wall").TerrainId;
        byte path = db.Terrain.Values.Single(t => t.Name == "Claimed Path").TerrainId;
        for (int x = 9; x <= 19; x++)
            for (int y = 9; y <= 11; y++)
            {
                world.Tile(x, y).TerrainId = y == 10 ? path : wall;
                world.Tile(x, y).OwnerPlayerId = me.Value;
            }
        RoomRules.Place(db, ref world.Tile(20, 10), db.Room(RoomTypeId.Treasury), me);
        world.Tile(20, 10).GoldHeld = 1_000_000; // pays for all four

        byte ordinaryDoor = db.Doors.Keys.First(id => id != HoverTint.FlashMode1Type);
        byte ordinaryTrap = db.Traps.Keys.First(id => id != HoverTint.FlashMode1Type);
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceDoor(world, db, new TilePos(10, 10), me, HoverTint.FlashMode1Type));
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceDoor(world, db, new TilePos(12, 10), me, ordinaryDoor));
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceTrap(world, db, new TilePos(14, 10), me, HoverTint.FlashMode1Type));
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceTrap(world, db, new TilePos(16, 10), me, ordinaryTrap));
        Assert.Equal("Hero Portcullis", db.Doors[HoverTint.FlashMode1Type].Name);
        Assert.Equal("Fear Trap", db.Traps[HoverTint.FlashMode1Type].Name);

        bool Covers(WorldHoverKind kind, int id) => HoverTint.CoversBlended(world, new WorldHoverHit(kind, new TilePos(10, 10), id));
        Assert.True(Covers(WorldHoverKind.Door, world.Doors.Single(d => d.DoorId == HoverTint.FlashMode1Type).Id));
        Assert.False(Covers(WorldHoverKind.Door, world.Doors.Single(d => d.DoorId == ordinaryDoor).Id));
        Assert.True(Covers(WorldHoverKind.Trap, world.Traps.Single(t => t.TrapId == HoverTint.FlashMode1Type).Id));
        Assert.False(Covers(WorldHoverKind.Trap, world.Traps.Single(t => t.TrapId == ordinaryTrap).Id));
        Assert.False(Covers(WorldHoverKind.Creature, world.Creatures.FirstOrDefault()?.Id ?? 1));
    }

    [Fact]
    public void SimulationObjectsAndOnlyTheStorePileDecorationArePicked()
    {
        Assert.True(ObjectHoverHighlight.IsHoverTarget(7, 49));
        Assert.True(ObjectHoverHighlight.IsHoverTarget(-1, ObjectHoverHighlight.StorePileObjectId));
        Assert.False(ObjectHoverHighlight.IsHoverTarget(-2, 13)); // the Heart, a decoration
        Assert.False(ObjectHoverHighlight.IsHoverTarget(-21, 76)); // a room pillar
    }

    [InstallFact]
    public void OnlyTheThreeGoldObjectsCarryTheHoverTextureArtFlag()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        byte[] flagged = db.Objects.Values.Where(ObjectHoverHighlight.UsesHoverTexture)
            .Select(o => o.ObjectId).OrderBy(id => id).ToArray();
        Assert.Equal(new byte[] { 1, 2, 3 }, flagged);
    }

    [InstallFact]
    public void AStoreTilesGoldIsAPickableDecorationThatReadsAsItsGold()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        RoomRules.Place(db, ref world.Tile(10, 10), db.Room(RoomTypeId.Treasury), PlayerIds.FirstKeeper);
        RoomRules.Place(db, ref world.Tile(11, 10), db.Room(RoomTypeId.Treasury), PlayerIds.FirstKeeper);
        world.Tile(10, 10).GoldHeld = 2500;

        ObjectView pile = new WorldView(db).Decorations(world)
            .Single(o => o.ObjectTypeId == ObjectHoverHighlight.StorePileObjectId);
        Assert.True(WorldView.IsStorePile(pile.Id));
        Assert.Equal(world.TileIndex(new TilePos(10, 10)), WorldView.DecorationTileIndex(pile.Id));
        Assert.True(pile.HoverTexture);
        Assert.True(ObjectHoverHighlight.IsHoverTarget(pile.Id, pile.ObjectTypeId));

        var strings = Dk2StringTable.Open(Path.Combine(install, "Data", "Text", "Default", "Text.str"));
        var text = new WorldHoverText(db, strings.Resolve, PlayerIds.FirstKeeper);
        // The runner keeps the cursor tile beside the picked id; here the neighbour.
        HoverTextSnapshot hover = text.Capture(world, new(WorldHoverKind.Object, new TilePos(11, 10), pile.Id));
        Assert.Equal("2500 Gold", hover.TopStatus.Trim()); // string 2543 is " %73 Gold"
        Assert.Equal(strings.Resolve(db.ObjectById(ObjectHoverHighlight.StorePileObjectId)!.NameStringId), hover.TooltipName);
    }

    [InstallFact]
    public void AFloorPileIsASimulationObjectWithTheHoverTexture()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.Load(install);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        ObjectState floorPile = world.SpawnObject(db, 1, PlayerIds.Neutral, world.TileIndex(new TilePos(12, 12)));
        floorPile.GoldHeld = 300;
        var view = new WorldView(db);
        ObjectView shown = view.Objects(world).Single(o => o.Id == floorPile.Id);
        Assert.True(shown.HoverTexture);
        Assert.DoesNotContain(view.Decorations(world), o => o.HoverTexture && o.ObjectTypeId != ObjectHoverHighlight.StorePileObjectId);
    }
}
