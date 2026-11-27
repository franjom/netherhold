// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>Per-frame and per-tick allocations of presentation code both adapters run (plan step 1c). Each test warms
/// the path, then measures a second call as <see cref="BitmapTextTests"/> does; the resolvers hand back strings made
/// beforehand, as the string tables do.</summary>
public sealed class PresentationAllocationTests
{
    private static long Allocated(Action action)
    {
        action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // The strings the HUD resolves, made once, the way a loaded table holds them.
    private static readonly Dictionary<uint, string> Strings = new Dictionary<uint, string>();
    private static string Text(uint id)
    {
        lock (Strings)
        {
            if (!Strings.TryGetValue(id, out string? value))
            {
                Strings[id] = value = "text " + id + " %19 %20 %27 %28";
            }

            return value;
        }
    }

    private sealed class Art : IBottomPanelArt
    {
        public string? Icon(BottomPanelChoiceKey key)
        {
            return null;
        }

        public string Text(uint id)
        {
            return PresentationAllocationTests.Text(id);
        }
    }

    [Fact]
    public void AHoveredFrontEndLabelDoesNotAllocateOnEachFrame()
    {
        var session = new FrontEndSession(path => Enumerable.Range(0, path == FrontEndPath.Intro ? 1 : 30)
            .Select(i => new KcsFrame(i, 0, -3, new KcsRotation(1, 0, 0, 0, 1, 0, 0, 0, 1), 1, 20000)).ToArray());
        session.Update(TimeSpan.Zero); session.Update(TimeSpan.Zero);
        using var menu = new FrontEndMenuController(session, static _ => new FrontEndRect(0, 0, 10, 10));
        var pointer = new FrontEndPointer(1, 1, false);
        menu.UpdatePointer(pointer);
        FrontEndWidget hovered = menu.Widgets.First(w => menu.State(w).Hovered);
        Assert.Equal(FrontEndDrawKind.Label, hovered.Draw);

        Assert.Equal(0, Allocated(() => menu.UpdatePointer(pointer)));
    }

    [Fact]
    public void TheBottomPanelsHoverTextDoesNotAllocateWhileATabIsHovered()
    {
        var hud = new BottomPanelHud(new Art(),
            new BottomPanelView(new GameDatabase([], [], [], new GlobalVariables()), PlayerIds.FirstKeeper, (_, name) => name));
        const float wide = 1920f * 16 / 9;
        var frame = new HudFrame();
        hud.Build(wide, 0, frame);
        HudRect tab = BottomPanelLayout.Tab((int)BottomPanelCategory.Rooms);
        hud.PointerMove(tab.X + 10, tab.Y + 10);
        Assert.NotEmpty(hud.HoverText.TooltipName);
        long sum = 0;

        Assert.Equal(0, Allocated(() => sum += hud.HoverText.TooltipName.Length));
        Assert.True(sum > 0);
    }

    [InstallFact]
    public void TheMessageTabsHoverTextDoesNotAllocateOverTheObjectiveButtonOrATab()
    {
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(database, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        world.Messages.Post(world, PlayerIds.FirstKeeper, PlayerMessageKind.HeartAttacked, 0, 0);
        var view = new MessageTabsView(PlayerIds.FirstKeeper);
        view.Capture(world, 1000); view.Capture(world, 1640);
        var font = new BitmapFont(10, [new BitmapGlyph('A', 2, 2, 0, 0, 3, [15, 15, 15, 15])]);
        string message = "message";
        var hud = new MessageTabsHud(new Art(), view, _ => font, _ => message, _ => "AA");
        var frame = new HudFrame();
        hud.Build(new HudScreen(1920, 1080), 1920, new HudVisibility(true, true), collapsed: false, default, default, frame);

        hud.PointerMove(1112 + 1, 1460 + 1);
        Assert.Equal(Text(2163), hud.HoverText.TooltipName);
        long sum = 0;
        Assert.Equal(0, Allocated(() => sum += hud.HoverText.TooltipName.Length));

        hud.PointerLeave();
        view.Hovered = 0;
        Assert.Equal(message, hud.HoverText.TopStatus);
        Assert.Equal(0, Allocated(() => sum += hud.HoverText.TopStatus.Length));
        Assert.True(sum > 0);
    }

    [Fact]
    public void TheResourceHoverIsKeptWhileOnlyTheTickChanges()
    {
        var hover = new HudHoverText(Text);
        var sources = new Sources(KeeperResourceKind.Heart);
        var facts = new KeeperResourceSnapshot(new GoldAmount(5), new ManaAmount(5), new ManaPerSecond(1), new ManaPerSecond(1), new HudGaugeFraction(60, 100), new HudGaugeFraction(1, 2), new GoldAmount(40), new GameTick(10),
            default, default, 100, default, new TilePos(3, 3));
        hover.Capture(sources, facts);
        long sum = 0;
        long tick = 10;

        Assert.Equal(0, Allocated(() =>
        {
            facts = facts with { Tick = new GameTick(++tick), Payday = new HudGaugeFraction(1000 - (int)tick, 1000) };
            sum += hover.Capture(sources, facts).TopStatus.Length;
        }));
        Assert.True(sum > 0);
        // What the text is built from still rebuilds it.
        string first = hover.Capture(sources, facts).TopStatus;
        Assert.NotEqual(first, hover.Capture(sources, facts with { HeartHealth = new HudGaugeFraction(70, 100) }).TopStatus);
        sources.Kind = KeeperResourceKind.Payday;
        string payday = hover.Capture(sources, facts).TopStatus;
        Assert.NotEqual(payday, hover.Capture(sources, facts with { WageBill = new GoldAmount(41) }).TopStatus);
        Assert.Equal(0, Allocated(() =>
        {
            facts = facts with { Tick = new GameTick(++tick), Payday = new HudGaugeFraction(1000 - (int)tick, 1000) };
            sum += hover.Capture(sources, facts).TopStatus.Length;
        }));
    }

    private sealed class Sources(KeeperResourceKind kind) : IHudHoverSources
    {
        public KeeperResourceKind Kind = kind;
        public int OverviewTextId()
        {
            return 0;
        }

        public CreatureOverviewType? OverviewColumn()
        {
            return null;
        }

        public HoverTextSnapshot CreatureTypeText(CreatureOverviewType column)
        {
            return HoverTextSnapshot.Empty;
        }

        public bool OverMessageTabs()
        {
            return false;
        }

        public HoverTextSnapshot MessageTabsText()
        {
            return HoverTextSnapshot.Empty;
        }

        public bool OverBattleTab()
        {
            return false;
        }

        public HoverTextSnapshot BattleTabText()
        {
            return HoverTextSnapshot.Empty;
        }

        public bool OverBottomPanel()
        {
            return false;
        }

        public HoverTextSnapshot BottomPanelText()
        {
            return HoverTextSnapshot.Empty;
        }

        public ClassicHudAction MinimapControl()
        {
            return ClassicHudAction.None;
        }

        public KeeperResourceKind Resource()
        {
            return Kind;
        }
    }

    [InstallFact]
    public void ACreatureTypeHoverTextIsKeptForTheSameDefinition()
    {
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        CreatureDef creature = database.Creatures.Values.First(c => c.DescriptionStringId != 0);
        var text = new WorldHoverText(database, Text, PlayerIds.FirstKeeper);
        HoverTextSnapshot first = text.CreatureType(creature);
        Assert.NotEmpty(first.TopStatus);
        long sum = 0;

        Assert.Equal(0, Allocated(() => sum += text.CreatureType(creature).TopStatus.Length));
        Assert.True(sum > 0);
        CreatureDef other = database.Creatures.Values.First(c => c.DescriptionStringId != 0 && c.JobClass != creature.JobClass);
        Assert.NotEqual(first, text.CreatureType(other));
        Assert.Equal(first, text.CreatureType(creature));
    }

    [InstallFact]
    public void TheMusicObserverAllocatesNothingBeyondTheWorldsOwnedCreaturesIterators()
    {
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(database, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [1, 3, 4], fogOfWarEnabled: false);
        byte knight = database.Creatures.Values.Single(c => c.Name == "Knight").CreatureId;
        // Two separate fights, so two battles hold members, and the viewer is in one of them.
        for (int fight = 0; fight < 2; fight++)
        {
            CreatureState hero = world.SpawnCreature(database, knight, new PlayerId(1), new TilePos(10 + (world.Width - 22) * fight, 10 + (world.Height - 22) * fight));
            CreatureState keeper = world.SpawnCreature(database, knight, new PlayerId((byte)(fight == 0 ? 3 : 4)), new TilePos(11 + (world.Width - 22) * fight, 10 + (world.Height - 22) * fight));
            hero.Dk2StateId = keeper.Dk2StateId = 199;
            hero.FightTargetId = keeper.Id; keeper.FightTargetId = hero.Id;
            hero.FightTargetKind = keeper.FightTargetKind = FightTargetKind.Creature;
        }
        var observer = new MusicWorldObserver(new PlayerId(3), new MusicDirector());
        long tick = 100;
        observer.Capture(world, database, new GameTick(tick++));
        Assert.Equal(4, observer.Battles.Sum(b => b.MemberCount));
        observer.Capture(world, database, new GameTick(tick++));

        // The iterators WorldState.OwnedCreatures hands out, one a present player: a simulation call, not ours to change here.
        long iterators = Allocated(() =>
        {
            for (byte owner = 0; owner < 8; owner++)
            {
                if (world.HasPlayer(new PlayerId(owner)))
                {
                    foreach (CreatureState creature in world.OwnedCreatures(new PlayerId(owner)))
                    {
                        _ = creature;
                    }
                }
            }
        });
        Assert.True(iterators > 0);

        long before = GC.GetAllocatedBytesForCurrentThread();
        observer.Capture(world, database, new GameTick(tick++));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before <= iterators,
            $"{GC.GetAllocatedBytesForCurrentThread() - before} bytes against {iterators} for the iterators");
        Assert.Equal(4, observer.Battles.Sum(b => b.MemberCount));
    }

    [InstallFact]
    public void TheMinimapMarkersDoNotAllocateOnACapturedTick()
    {
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(database, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper, PlayerIds.Good], fogOfWarEnabled: false);
        CreatureDef imp = database.Creatures.Values.Single(c => c.Name == "Imp");
        for (int i = 0; i < 6; i++)
        {
            world.SpawnCreature(database, imp.CreatureId, PlayerIds.FirstKeeper, new TilePos(10 + i, 10));
            world.SpawnCreature(database, imp.CreatureId, PlayerIds.Good, new TilePos(10 + i, 12));
        }
        var simulation = new GameSimulation(database, world, seed: 7, []);
        var view = new WorldView(database, PlayerIds.FirstKeeper);
        var palette = new MinimapPalette(new uint[37]);
        var markers = new List<MinimapMarker>();
        view.CaptureMinimapMarkers(world, palette, markers);
        Assert.NotNull(view.MinimapHeartTile);
        Assert.True(markers.Count > 12, $"{world.Width}x{world.Height} {markers.Count} {view.MinimapHeartTile}");
        simulation.Tick();

        Assert.Equal(0, Allocated(() => view.CaptureMinimapMarkers(world, palette, markers)));
    }

    /// <summary>The heart is read from the room sweep instead of a pass over every tile; both must give the same tile on the
    /// shipped levels, for every keeper that owns heart terrain.</summary>
    [InstallFact]
    public void TheMinimapHeartTileMatchesAPassOverEveryTileOnTheShippedLevels()
    {
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        byte heart = database.Rooms.Values.First(r => r.TypeId == RoomTypeId.DungeonHeart).TerrainId;
        int checkedHearts = 0;
        foreach (string level in new[] { "Level1", "Level2", "Level3", "Level4", "Level5", "Level6", "Level7", "Level8", "Level9", "Level10" })
        {
            LevelMap? map = GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, level);
            if (map is null)
            {
                continue;
            }

            byte[] keepers = [PlayerIds.FirstKeeper, (byte)(PlayerIds.FirstKeeper + 1), (byte)(PlayerIds.FirstKeeper + 2), PlayerIds.Good];
            var world = WorldState.FromMap(database, map, keepers, fogOfWarEnabled: false);
            foreach (byte viewer in keepers)
            {
                int minX = world.Width, minY = world.Height, maxX = -1, maxY = -1;
                for (int y = 0; y < world.Height; y++)
                {
                    for (int x = 0; x < world.Width; x++)
                    {
                        if (world.Tile(x, y).OwnerPlayerId == viewer && world.Tile(x, y).TerrainId == heart)
                        { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
                    }
                }

                var view = new WorldView(database, new PlayerId(viewer));
                view.CaptureMinimapMarkers(world, new MinimapPalette(new uint[37]), new List<MinimapMarker>());
                if (maxX < 0) { Assert.Equal(new TilePos(FearRules.NoHeartTile, FearRules.NoHeartTile), view.MinimapHeartTile); continue; }
                Assert.Equal(new TilePos((minX + maxX) / 2, (minY + maxY) / 2), view.MinimapHeartTile);
                checkedHearts++;
            }
        }
        Assert.True(checkedHearts > 0);
    }

    [InstallFact]
    public void TheMinimapHeartTileIsTheBoundingBoxCentreOfTheViewersHeartTilesWhereverTheyAre()
    {
        GameDatabase database = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        byte heart = database.Rooms.Values.First(r => r.TypeId == RoomTypeId.DungeonHeart).TerrainId;
        byte floor = database.Terrain.Values.First(t => !t.IsSolid && !t.IsWater && !t.IsLava).TerrainId;
        MapTile[] tiles = [.. Enumerable.Repeat(new MapTile(floor, PlayerIds.FirstKeeper, 0, 0), 100)];
        // Two pieces of the viewer's heart terrain apart, one of the rival's, and the viewer's own tile beside it.
        foreach ((int x, int y, byte owner) in new[] { (2, 1, PlayerIds.FirstKeeper), (3, 1, PlayerIds.FirstKeeper),
            (7, 6, PlayerIds.FirstKeeper), (7, 8, PlayerIds.Good), (8, 8, PlayerIds.Good) })
        {
            tiles[x + y * 10] = new MapTile(heart, owner, 0, 0);
        }

        var world = WorldState.FromMap(database, new LevelMap(10, 10, tiles), [PlayerIds.FirstKeeper, PlayerIds.Good]);
        var view = new WorldView(database, PlayerIds.FirstKeeper);
        view.CaptureMinimapMarkers(world, new MinimapPalette(new uint[37]), new List<MinimapMarker>());
        Assert.Equal(new TilePos((2 + 7) / 2, (1 + 6) / 2), view.MinimapHeartTile);
    }
}
