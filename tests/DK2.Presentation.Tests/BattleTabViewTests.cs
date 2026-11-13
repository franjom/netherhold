// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art.Textures;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>Menu 43, the Battle tab (docs/research/battle-tab.md).</summary>
public sealed class BattleTabViewTests
{
    private static readonly PlayerId Me = new(PlayerIds.FirstKeeper);
    private static readonly PlayerId Rival = new(PlayerIds.FirstKeeper + 1);

    [Fact]
    public void TheBladesAndTheAlarmCycleFourFramesAndTheCancelTwo()
    {
        // 0x00412AF3..0x00412BA7: phase = real ms / 150; art + (phase & 3), the cancel art + ((phase >> 1) & 1).
        Assert.Equal(0, BattleTabView.Phase(149));
        Assert.Equal(1, BattleTabView.Phase(150));
        string[] blades = Enumerable.Range(0, 5).Select(p => BattleTabView.Art(BattleTabButton.Battle, p)).ToArray();
        Assert.Equal(["GUI\\Icons\\a-fight-00", "GUI\\Icons\\a-fight-01", "GUI\\Icons\\a-fight-02", "GUI\\Icons\\a-fight-03",
            "GUI\\Icons\\a-fight-00"], blades);
        Assert.Equal("GUI\\Icons\\a-alarm-03", BattleTabView.Art(BattleTabButton.Alarm, 7));
        string[] cancel = Enumerable.Range(0, 5).Select(p => BattleTabView.Art(BattleTabButton.CallToArms, p)).ToArray();
        Assert.Equal(["GUI\\Spells\\call_to_arms-kill", "GUI\\Spells\\call_to_arms-kill", "GUI\\Spells\\call_to_arms-kill-2",
            "GUI\\Spells\\call_to_arms-kill-2", "GUI\\Spells\\call_to_arms-kill"], cancel);
    }

    [Fact]
    public void TheButtonsKeepTheirStaticPlaces()
    {
        Assert.Equal([0, 136, 264], new[] { BattleTabButton.Battle, BattleTabButton.CallToArms, BattleTabButton.Alarm }
            .Select(BattleTabView.LocalX));
    }

    [Fact]
    public void TheBladesShowOnlyForALiveBattleHoldingTheViewersCreature()
    {
        // 0x004269D0 through 0x00505250 from index -1.
        BattleViewSnapshot[] others = [new(new(0), new(5, 5), PlayerMask.None.With(Rival), 3)];
        Assert.False(BattleTabView.BattleShows(others, Me));
        BattleViewSnapshot[] emptied = [new(new(1), new(5, 5), PlayerMask.None.With(Me), 0)];
        Assert.False(BattleTabView.BattleShows(emptied, Me));
        BattleViewSnapshot[] mixed = [others[0], new(new(4), new(9, 9), PlayerMask.None.With(Me).With(Rival), 2)];
        Assert.True(BattleTabView.BattleShows(mixed, Me));
    }

    [InstallFact]
    public void TheCancelShowsWhileTheFlagIsActivatedOrRecallingAndNotHeld()
    {
        // 0x004C00D0: player +0x76 bit 2, a live flag, +0x146 bit 0x10 clear, process state 13 or 14.
        (GameDatabase db, WorldState world) = Level1();
        ObjectState flag = world.SpawnObject(db, CallToArmsRules.ObjectTypeId, Me, world.TileIndex(new TilePos(12, 14)));
        world.Player(Me).CallToArms = new CallToArmsState(true, flag.Id, 12, 14, 0);
        var shown = new Dictionary<byte, bool>();
        foreach (byte state in new[] { CallToArmsRules.Activating, CallToArmsRules.Activated, CallToArmsRules.Recalling,
            CallToArmsRules.Releasing, CallToArmsRules.Fading, CallToArmsRules.Faded })
        {
            flag.ProcessState = state;
            shown[state] = BattleTabView.CallToArmsShows(world, Me);
        }
        Assert.Equal([false, true, true, false, false, false], shown.Values);
        Assert.False(BattleTabView.CallToArmsShows(world, Rival));

        flag.ProcessState = CallToArmsRules.Recalling;
        flag.InHand = true;
        Assert.False(BattleTabView.CallToArmsShows(world, Me));
        flag.InHand = false;
        world.Player(Me).CallToArms = world.Player(Me).CallToArms with { On = false };
        Assert.False(BattleTabView.CallToArmsShows(world, Me));
        Assert.Null(BattleTabView.CallToArmsTile(world, Me));
        world.Player(Me).CallToArms = world.Player(Me).CallToArms with { On = true };
        Assert.Equal(new TilePos(12, 14), BattleTabView.CallToArmsTile(world, Me));
        world.RemoveObject(flag.Id);
        Assert.False(BattleTabView.CallToArmsShows(world, Me));
        Assert.Equal(new TilePos(12, 14), BattleTabView.CallToArmsTile(world, Me));   // 0x00426830 tests only the bit
    }

    [InstallFact]
    public void TheAlarmShowsWhileAnAlarmTrapOfTheViewerIsInsideItsRechargeDeadline()
    {
        // 0x00426B90: shot 13 and 0x004AA2F0 (turn <= +0xFF, unsigned, inclusive).
        (GameDatabase db, WorldState world) = Level1();
        TrapDef alarm = db.Traps.Values.Single(t => t.Name == "Alarm Trap");
        Assert.Equal(BattleTabView.AlarmShotTypeId, alarm.ShotTypeId);
        TrapDef sentry = db.Traps.Values.Single(t => t.Name == "Sentry Trap");
        RoomRules.Place(db, ref world.Tile(20, 10), db.Room(RoomTypeId.Treasury), Me);   // pays for the blueprints
        world.Tile(20, 10).GoldHeld = 1_000_000;
        TrapState mine = Place(world, db, 14, Me, alarm.TrapId);
        TrapState other = Place(world, db, 16, Me, sentry.TrapId);
        mine.RechargeAt = 100;
        other.RechargeAt = 500;
        Assert.True(BattleTabView.AlarmShows(world.Traps, db, Me, new GameTick(100)));
        Assert.False(BattleTabView.AlarmShows(world.Traps, db, Me, new GameTick(101)));   // the sentry's deadline does not count
        Assert.False(BattleTabView.AlarmShows(world.Traps, db, Rival, new GameTick(50)));

        var entries = new List<ThingNavigationEntry>();
        BattleTabView.TrapEntries(world.Traps, db, new GameTick(100), entries);
        Assert.Equal([other.Id, mine.Id], entries.Select(e => e.Id));   // newest first
        Assert.Equal([false, true], entries.Select(e => e.Alarm));
        Assert.Equal([false, true], entries.Select(e => e.Ringing));
        Assert.Equal(new TilePos(14, 10), new ThingNavigation(Me).NextAlarm(entries));
    }

    [InstallFact]
    public void EveryArtTheMenuDrawsIsInTheEngineTextures()
    {
        using EngineTextureArchive archive = EngineTextureArchive.OpenInstall(Dk2InstallGate.Path);
        foreach (string name in BattleTabView.AllArt)
            { var image = archive.Decode(name); Assert.True(image.Width > 0, $"{name} {image.Width}x{image.Height}"); }
    }

    private static (GameDatabase, WorldState) Level1()
    {
        GameDatabase db = GameDatabaseLoader.Load(Dk2InstallGate.Path);
        var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(Dk2InstallGate.Path, "Level1")!,
            [PlayerIds.FirstKeeper], fogOfWarEnabled: false);
        return (db, world);
    }

    private static TrapState Place(WorldState world, GameDatabase db, int x, PlayerId owner, byte trapId)
    {
        byte path = db.Terrain.Values.Single(t => t.Name == "Claimed Path").TerrainId;
        world.Tile(x, 10).TerrainId = path;
        world.Tile(x, 10).OwnerPlayerId = owner.Value;
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceTrap(world, db, new TilePos(x, 10), owner, trapId));
        return world.TrapAt(x, 10, owner)!;
    }
}
