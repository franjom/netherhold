// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data;
using DK2.Data.Media;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;
namespace DK2.Presentation.Tests;
public sealed class Level6aTortureSpeechTests
{
    private static readonly PlayerId Keeper = PlayerIds.FirstKeeper;
    [InstallFact]
    public void ShippedOpeningDeathKeepsItsNotificationsAndScriptedSceneWithoutGenericSpeech()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level6a");
        GameSimulation simulation = CampaignSimulationFactory.Create(db,
            GameDatabaseLoader.LoadCampaignMap(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignLevel(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignPlayers(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignThings(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignTriggers(install, "Level6a")!, 1);
        var messages = new MessageTabsView(Keeper, db);
        var events = new ListEventSink();
        var deferred = new DeferredEventSink(new Level6aTortureSpeech(db, messages, events));
        simulation.ObserveWith(deferred);
        for (int tick = 0; tick < 400; tick++)
        {
            simulation.Tick();
            deferred.Replay();
            messages.Capture(simulation.World, tick * 250);
            if (events.Events.Any(e => e.ScriptedView is { Kind: ScriptedViewKind.Speech, Value: 23 }))
            {
                break;
            }
        }
        TortureDeathMessage[] notices = [.. events.Events.Where(e => e.TortureMessage.HasValue).Select(e => e.TortureMessage!.Value)];
        Assert.Equal(new[] { PlayerMessageKind.InformationLearned, PlayerMessageKind.InformationReveal },
            notices.Select(n => n.Message.Kind));
        Assert.All(notices, n =>
        {
            Assert.Equal(new KwdTriggerId(24), n.Trigger);
            Assert.Equal(new CreatureTypeId(25), n.CreatureType);
            Assert.Contains(simulation.World.Messages.Entries, m => m.Id == n.Message.Id);
            Assert.Contains(messages.Tabs, t => t.MessageId == n.Message.Id);
        });
        Assert.DoesNotContain(messages.Speech.Pending, r => r.Category == SoundCategory.Mentor && r.Id.Value is 379 or 380);
        Assert.Contains(events.Events, e => e.ScriptedView is { Kind: ScriptedViewKind.CameraPath, Value: 231 });
        Assert.Contains(events.Events, e => e.ScriptedView is { Kind: ScriptedViewKind.Speech, Value: 23 });
    }
    [InstallTheory]
    [InlineData("Level6a", (ushort)0, (byte)25)]
    [InlineData("Level6b", (ushort)24, (byte)25)]
    [InlineData("Level6a", (ushort)24, (byte)16)]
    public void OtherDeathsKeepTheGenericSpeech(string level, ushort root, byte kind)
    {
        (GameDatabase? db, WorldState? world) = Fixture(level);
        var messages = new MessageTabsView(Keeper, db);
        var events = new ListEventSink();
        var sink = new Level6aTortureSpeech(db, messages, events);
        Award(db, world, sink, new(root), new(kind));
        messages.Capture(world, 0);
        Assert.Equal(new uint[] { 380, 379 }, messages.Speech.Pending.Select(r => r.Id.Value));
        Assert.Equal(2, messages.Tabs.Count);
        Assert.Equal(2, events.Events.Count);
    }
    [InstallFact]
    public void ExistingNotificationsAreNotReattributedToTheOpeningGuard()
    {
        (GameDatabase? db, WorldState? world) = Fixture("Level6a");
        var messages = new MessageTabsView(Keeper, db);
        var events = new ListEventSink();
        var sink = new Level6aTortureSpeech(db, messages, events);
        Award(db, world, sink, KwdTriggerId.None, new(25));
        Award(db, world, sink, new(24), new(25));
        Assert.Equal(2, events.Events.Count);
        Assert.Equal(2, world.Messages.Entries.Count);
        messages.Capture(world, 0);
        Assert.Equal(new uint[] { 380, 379 }, messages.Speech.Pending.Select(r => r.Id.Value));
    }
    [InstallFact]
    public void AnotherKeeperWithoutThisTutorialKeepsTheGenericSpeech()
    {
        (GameDatabase? db, WorldState? world) = Fixture("Level6a");
        var other = new PlayerId(4);
        var messages = new MessageTabsView(other, db);
        var sink = new Level6aTortureSpeech(db, messages, NullEventSink.Instance);
        TortureInformationRules.Award(world, db, new SimRandom(1), db.CreatureById(25), PlayerIds.Good, other,
            message => sink.Emit(new SimEvent(world.CurrentTick, "Torture", "death_message", "")
            { TortureMessage = new(message, new CreatureId(50), new CreatureTypeId(25), new KwdTriggerId(24)) }));
        messages.Capture(world, 0);
        Assert.Equal(new uint[] { 380, 379 }, messages.Speech.Pending.Select(r => r.Id.Value));
    }
    private static void Award(GameDatabase db, WorldState world, ISimulationEventSink sink,
        KwdTriggerId root, CreatureTypeId type) => TortureInformationRules.Award(world, db, new SimRandom(1),
            db.CreatureById(type.Value), PlayerIds.Good, Keeper, message => sink.Emit(
                new SimEvent(world.CurrentTick, "Torture", "death_message", "")
                { TortureMessage = new(message, new CreatureId(50), type, root) }));
    private static (GameDatabase, WorldState) Fixture(string level)
    {
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(Dk2InstallGate.Path, level);
        byte floor = db.Terrain.Values.Single(t => t.Name == "Claimed Path").TerrainId;
        var map = new LevelMap(5, 5, [.. Enumerable.Repeat(new MapTile(floor, Keeper.Value, 0, 0), 25)]);
        return (db, WorldState.FromMap(db, map, [PlayerIds.Neutral, PlayerIds.Good, Keeper.Value, 4], fogOfWarEnabled: false));
    }
}
