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

public sealed class TutorialNotificationsTests
{
    [InstallFact]
    public void AScriptedMessageTabsWithItsLevelTextAndExpiresAfterFiftySeconds()
    {
        var (db, world) = LevelOneWorld();
        var sim = new GameSimulation(db, world, 1, [], PlayerTree(Action(24,
            new() { ["SpeechId"] = 4, ["SpeakOnly"] = 0 })));
        sim.Tick();
        var view = new MessageTabsView(PlayerIds.FirstKeeper, db);
        view.Capture(world, 1000);
        view.OpenTab(0);
        PlayerMessage message = Assert.IsType<PlayerMessage>(view.Open);
        Assert.Equal(-4, view.Reference(message));
        Assert.StartsWith("Observe.  Red flowers", Text(view.Reference(message)));
        view.CloseOpen();
        Assert.Null(view.Open);
        view.OpenTab(0);
        Assert.NotNull(view.Open);

        var clock = new GameSimulation(db, world, 1, []);
        clock.Run(199);
        view.Capture(world, 2000);
        Assert.Single(view.Tabs);
        clock.Tick();
        view.Capture(world, 3000);
        Assert.Empty(view.Tabs);
        Assert.Null(view.Open);
    }

    [InstallFact]
    public void LevelOnesFirstGoblinPostsItsIntroductionAndOnlyOnceAcrossARestore()
    {
        var (db, world) = LevelOneWorld();
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
                if (world.Tile(x, y).TerrainId == db.Room(RoomTypeId.Portal).TerrainId)
                    world.Tile(x, y).OwnerPlayerId = PlayerIds.FirstKeeper;
        TriggerTree tree = PlayerTree(Action(28, new() { ["CreatureId"] = 7, ["Level"] = 1 }));
        var sim = new GameSimulation(db, world, 1, [], tree);
        var sink = new ListEventSink(); sim.ObserveWith(sink);
        var disable = Action(45, new() { ["Available"] = 0 });
        new TriggerExecutor(new TriggerTree([disable], disable.TriggerId), world.Triggers, world, db).Run(1);
        sim.Tick();

        PlayerMessage intro = Assert.Single(world.Messages.Entries, m => (uint)m.Kind == 14);
        Assert.Equal(54, intro.Data2);
        Assert.Equal(world.Creatures.Last().Id, intro.Data1);
        var view = new MessageTabsView(PlayerIds.FirstKeeper, db);
        view.Capture(world, 1000);
        Assert.Equal(-54, view.Reference(intro));
        Assert.StartsWith("Look well.  A Goblin", Text(view.Reference(intro)));
        ScriptedViewRequest speech = Assert.Single(sink.Events, e => e.ScriptedView is not null)
            .ScriptedView!.Value;
        Assert.Equal((ScriptedViewKind.Speech, 54, true), (speech.Kind, speech.Value, speech.Flag));

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, [], tree);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        sink.Clear(); restored.ObserveWith(sink);
        restored.Tick();
        Assert.Equal(2, restored.World.Creatures.Count(c => c.CreatureTypeId == 7));
        Assert.Single(restored.World.Messages.Entries, m => (uint)m.Kind == 14);
        Assert.DoesNotContain(sink.Events, e => e.ScriptedView is not null);
    }

    [InstallFact]
    public void TheShippedFirstGoblinCameraSequenceRequestsItsOwnIntroduction()
    {
        var (db, world) = LevelOneWorld();
        foreach (var room in RoomIndex.Build(world, db))
            if (room.Definition.TypeId == RoomTypeId.Portal)
                foreach (int tile in room.TileIndices) world.TileAtIndex(tile).OwnerPlayerId = PlayerIds.FirstKeeper;
        var things = GameDatabaseLoader.LoadCampaignThings(Dk2InstallGate.Path, "Level1")!;
        ThingProjection.Place(world, db, things.OfType<ActionPointThing>());
        var records = GameDatabaseLoader.LoadCampaignTriggers(Dk2InstallGate.Path, "Level1")!;
        var tree = new TriggerTree(records, 0, playerRoots: [new TriggerPlayerRoot(PlayerIds.FirstKeeper, 65526)]);
        var sim = new GameSimulation(db, world, 1, [], tree);
        var sink = new ListEventSink(); sim.ObserveWith(sink);
        sim.Tick();

        Assert.Contains(sink.Events, e => e.ScriptedView is { Kind: ScriptedViewKind.Speech, Value: 54, Flag: true });
        Assert.Contains(sink.Events, e => e.ScriptedView is { Kind: ScriptedViewKind.CameraPath, Value: 61 });
        Assert.DoesNotContain(sink.Events, e => e.ScriptedView is { Kind: ScriptedViewKind.Speech, Value: 4 });
    }

    [InstallFact]
    public void LevelOnesFlowerTutorialShowsEveryImpWithoutHoverAndSurvivesSaving()
    {
        var (db, world) = LevelOneWorld();
        string install = Dk2InstallGate.Path;
        List<ThingRecord> things = GameDatabaseLoader.LoadCampaignThings(install, "Level1")!;
        ThingProjection.Place(world, db, things);
        var tree = new TriggerTree(GameDatabaseLoader.LoadCampaignTriggers(install, "Level1")!,
            db.Level!.TriggerId, things.OfType<HeroPartyThing>(),
            GameDatabaseLoader.LoadCampaignPlayers(install, "Level1")!.Select(p => new TriggerPlayerRoot(p.PlayerId, p.TriggerId)));
        var sim = new GameSimulation(db, world, 1, [], tree);
        sim.Run(230);
        var flowers = new StatusFlowers(db, PlayerIds.FirstKeeper);
        int[] imps = world.Creatures.Where(c => c.OwnerPlayerId == PlayerIds.FirstKeeper).Select(c => c.Id).ToArray();
        Assert.Equal(4, imps.Length);
        Assert.DoesNotContain(flowers.Capture(world, world.CurrentTick, CreatureQueryTarget.None), f => imps.Contains(f.CreatureId));
        world.Triggers.Write(71, FlagWriteMode.Assign, 1);
        sim.Tick();
        Assert.All(imps, id => Assert.Contains(flowers.Capture(world, world.CurrentTick, CreatureQueryTarget.None), f => f.CreatureId == id));

        var restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), db, []);
        Assert.Equal(sim.StateHash(), restored.StateHash());
        var restoredFlowers = new StatusFlowers(db, PlayerIds.FirstKeeper);
        Assert.All(imps, id => Assert.Contains(restoredFlowers.Capture(restored.World, new GameTick(471), CreatureQueryTarget.None), f => f.CreatureId == id));
        Assert.DoesNotContain(restoredFlowers.Capture(restored.World, new GameTick(472), CreatureQueryTarget.None), f => imps.Contains(f.CreatureId));
    }

    private static (GameDatabase, WorldState) LevelOneWorld()
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, "Level1");
        return (db, WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!, [0, 1, 2, 3]));
    }

    private static TriggerRecord Action(byte type, Dictionary<string, long> parameters) =>
        new ActionTriggerRecord(new byte[16]) { Body = new byte[8], TriggerId = 0xFFFF, Type = type, Parameters = parameters };

    private static TriggerTree PlayerTree(TriggerRecord action) =>
        new([action], 0, playerRoots: [new TriggerPlayerRoot(PlayerIds.FirstKeeper, action.TriggerId)]);

    private static string Text(int reference)
    {
        var table = Dk2StringTable.Open(Path.Combine(Dk2InstallGate.Path, "Data", "Text", "Default", "LEVEL1.str"));
        Assert.True(table.TryResolve((uint)(-1 - reference), out string? value));
        return value!;
    }
}
