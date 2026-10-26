// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>The campaign win (campaign-win-sequence.md): dispatch, the Reaper finale 228-234, the
/// celebration and the continuation.</summary>
public sealed class FinaleTests
{
    private const byte Keeper = 3;

    private static (GameSimulation Sim, ObjectState Gem, ListEventSink Events) Setup(bool withGem = true, GameMode mode = GameMode.Campaign)
    {
        (GameDatabase basis, _, _) = SummonHornyTests.Setup();
        var gemDefinition = new ObjectDef
        {
            Name = "Portal Gem", ObjectId = TriggerExecutor.PortalGemObjectId, Flags = ObjectFlags.CanBePickedUp,
            Hp = 1, ManaValue = 0, PickUpPriority = 0, NameStringId = 0, TooltipStringId = 0,
            RoomCapacity = 0, StartState = 0, Width = new Fp32(1024), Height = new Fp32(512),
            Mass = new Fp32(4), Speed = new Fp32(0),
        };
        var db = new GameDatabase(basis.Terrain.Values, basis.Rooms.Values, basis.Creatures.Values, basis.Variables,
            basis.KeeperSpells.Values, objects: basis.Objects.Values.Append(gemDefinition), shots: basis.Shots.Values);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [PlayerIds.Good, PlayerIds.Neutral, Keeper], gameMode: mode);
        RoomDef heart = db.Room(RoomTypeId.DungeonHeart);
        for (int y = 3; y < 8; y++)
            for (int x = 3; x < 8; x++)
                RoomRules.Place(db, ref world.Tile(x, y), heart, Keeper);
        for (int y = 1; y < 19; y++)
            for (int x = 1; x < 19; x++)
            {
                if (world.Tile(x, y).TerrainId != heart.TerrainId)
                {
                    world.Tile(x, y).TerrainId = Dk2TerrainFixture.ClaimedPath;
                    world.Tile(x, y).OwnerPlayerId = Keeper;
                }
                world.Tile(x, y).SetExploredBy(Keeper);
            }
        ObjectState gem = withGem
            ? world.SpawnObject(db, TriggerExecutor.PortalGemObjectId, PlayerIds.Good, 12 * world.Width + 12)
            : null!;
        if (withGem) (gem.PosX, gem.PosY) = (CreatureState.CentreOf(12), CreatureState.CentreOf(12));
        var sim = new GameSimulation(db, world, 11, SimulationSystems.Default());
        var events = new ListEventSink();
        sim.ObserveWith(events);
        return (sim, gem, events);
    }

    private static CreatureState Reaper(GameSimulation sim) =>
        sim.World.Creatures.Single(c => sim.Database.CreatureById(c.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsHorny));

    private static void Win(GameSimulation sim) =>
        Assert.True(sim.World.EndGame(sim.Database, new PlayerId(Keeper), PlayerStatus.Won));

    [Fact]
    public void ACampaignWinMakesAHiddenReaperAtALandingFiveTilesFromTheGem()
    {
        (GameSimulation sim, ObjectState gem, ListEventSink events) = Setup();
        Win(sim);

        ref PlayerState player = ref sim.World.Player(Keeper);
        CreatureState reaper = Reaper(sim);
        Assert.Equal(FinaleRules.VanishState, reaper.Dk2StateId);
        Assert.True(reaper.ReaperHidden);
        Assert.Equal(1, reaper.Level);
        Assert.Equal(FinaleRules.SequenceStarted | FinaleRules.ReaperCreated, player.Finale.Flags);
        Assert.Equal((12, 12, gem.Id), (player.Finale.GemTileX, player.Finale.GemTileY, player.Finale.GemObjectId));
        int dx = (player.Finale.PointX >> 12) - 12, dy = (player.Finale.PointY >> 12) - 12;
        Assert.True(dx * dx + dy * dy >= 25, $"landing {player.Finale.PointX:X} {player.Finale.PointY:X}");
        Assert.Equal(0x800, player.Finale.PointX & 0xFFF);
        Assert.Equal((player.Finale.PointX, player.Finale.PointY), (reaper.PosX, reaper.PosY));
        Assert.Contains(events.Events, e => e.ScriptedView is { Kind: ScriptedViewKind.CameraZoom, Value: 0x5800, Value2: 1000 });
        Assert.Contains(events.Events, e => e.ScriptedView is { Kind: ScriptedViewKind.Letterbox, Flag: true });
    }

    [Fact]
    public void TheReaperAppearsWalksToTheGemTakesItAndOpensTheWinScreen()
    {
        (GameSimulation sim, ObjectState gem, ListEventSink events) = Setup();
        Win(sim);
        CreatureState reaper = Reaper(sim);
        var seen = new List<int>();
        for (int t = 0; t < 600 && !events.Events.Any(e => e.ScriptedView?.Kind == ScriptedViewKind.WinScreen); t++)
        {
            sim.Tick(InputFrame.Empty);
            if (seen.Count == 0 || seen[^1] != reaper.Dk2StateId) seen.Add(reaper.Dk2StateId);
        }

        Assert.Equal(new[] { 228, 229, 230, 231, 232, 234, 233 }, seen);
        Assert.Null(sim.World.ObjectById(gem.Id));
        Assert.False(reaper.ReaperHidden);
        Assert.Equal(byte.MaxValue, reaper.ReaperAlpha);
        Assert.Equal(new TilePos(12, 12), reaper.Tile);
        Assert.Contains(events.Events, e => e.EffectRequest?.EffectId == new EffectId(258));
        Assert.Contains(events.Events, e => e.ScriptedView is { Kind: ScriptedViewKind.CameraFollow, Value: 0 });
        long pickup = sim.World.Player(Keeper).Finale.PickupTick;
        Assert.Equal(pickup + 13 * 4 + 2, events.Events.First(e => e.ScriptedView?.Kind == ScriptedViewKind.WinScreen).Tick.Value);

        sim.Tick(InputFrame.Empty);
        Assert.Equal(FinaleRules.PickUpGemState, reaper.Dk2StateId);
        Assert.Single(events.Events, e => e.ScriptedView?.Kind == ScriptedViewKind.WinScreen);
    }

    [Fact]
    public void WithNoGemTheWinOnlyRaisesTheLetterbox()
    {
        (GameSimulation sim, _, ListEventSink events) = Setup(withGem: false);
        Win(sim);
        Assert.DoesNotContain(sim.World.Creatures, c => sim.Database.CreatureById(c.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsHorny));
        Assert.Equal(0u, sim.World.Player(Keeper).Finale.Flags);
        Assert.Single(events.Events, e => e.ScriptedView is not null);
        Assert.Contains(events.Events, e => e.ScriptedView is { Kind: ScriptedViewKind.Letterbox, Flag: true });
    }

    [Fact]
    public void AWinForAnotherThanTheLocalPlayerOrALossRunsNothing()
    {
        (GameSimulation sim, _, _) = Setup();
        sim.World.LocalPlayerId = new PlayerId(PlayerIds.Good);
        Win(sim);
        Assert.Equal(0u, sim.World.Player(Keeper).Finale.Flags);
        Assert.Empty(sim.World.Creatures);

        (GameSimulation lost, _, _) = Setup();
        Assert.True(lost.World.EndGame(lost.Database, new PlayerId(Keeper), PlayerStatus.Lost));
        Assert.Equal(0u, lost.World.Player(Keeper).Finale.Flags);
        Assert.Empty(lost.World.Creatures);
    }

    /// <summary>0x004C0120 kicks to state 1 only a creature that is no Reaper, not in the hand and
    /// held by no prison or pit (+0x3CB); a possessed one is kicked too.</summary>
    [Fact]
    public void ContinuingKicksFreeCreaturesButNotPrisonersOrTheHand()
    {
        (GameSimulation sim, _, _) = Setup();
        var keeper = new PlayerId(Keeper);
        CreatureState free = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId, keeper, new TilePos(14, 14));
        CreatureState prisoner = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId, keeper, new TilePos(15, 14));
        CreatureState possessed = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId, keeper, new TilePos(16, 14));
        Win(sim);
        foreach (CreatureState c in new[] { free, prisoner, possessed }) c.Dk2StateId = FinaleRules.CelebrateState;
        prisoner.PrisonOwnerPlayerId = PlayerIds.Good;
        sim.World.Player(Keeper).PossessedCreatureId = possessed.Id;
        // The kick lands before the turn's systems, which may move a kicked creature on from 1.
        sim.Tick(InputFrame.Of(new ContinueAfterResultCommand(keeper)));
        Assert.NotEqual(FinaleRules.CelebrateState, free.Dk2StateId);
        Assert.NotEqual(FinaleRules.CelebrateState, possessed.Dk2StateId);
        Assert.Equal(FinaleRules.CelebrateState, prisoner.Dk2StateId);
    }

    [Fact]
    public void APossessingKeepersFireworksWaitFourteenTurnsAndOnlyTheLocalPlayerRevealsTheMap()
    {
        (GameSimulation sim, _, _) = Setup(mode: GameMode.MyPetDungeon);
        sim.World.Player(Keeper).PossessedCreatureId = 99;
        long won = sim.World.CurrentTick.Value;
        Win(sim);
        Assert.Equal(won + 14, sim.World.Player(Keeper).Finale.FireworksTick);

        FinaleRules.RevealMap(sim.World, new PlayerId(PlayerIds.Good));
        Assert.False(sim.World.Tile(0, 0).IsExploredBy(new PlayerId(PlayerIds.Good)));
    }

    [Fact]
    public void APetDungeonWinCelebratesWithFireworksSixTurnsOn()
    {
        (GameSimulation sim, _, ListEventSink events) = Setup(mode: GameMode.MyPetDungeon);
        CreatureState goblin = sim.World.SpawnCreature(sim.Database, Dk2TerrainFixture.GoblinCreatureId, new PlayerId(Keeper), new TilePos(14, 14));
        long won = sim.World.CurrentTick.Value;
        Win(sim);

        Assert.Equal(FinaleRules.CelebrateState, goblin.Dk2StateId);
        Assert.Equal(FinaleRules.SequenceStarted, sim.World.Player(Keeper).Finale.Flags);
        Assert.Equal(won + 6, sim.World.Player(Keeper).Finale.FireworksTick);
        Assert.Contains(events.Events, e => e.ScriptedView?.Kind == ScriptedViewKind.HideGui);
        for (int t = 0; t < 8; t++) sim.Tick(InputFrame.Empty);
        Assert.Single(events.Events, e => e.EffectRequest?.EffectId == new EffectId(320));
        Assert.Equal(0, sim.World.Player(Keeper).Finale.FireworksTick);
    }

    [Fact]
    public void ContinuingReopensPlaySendsTheReaperAwayAndRevealsTheMap()
    {
        (GameSimulation sim, _, ListEventSink events) = Setup();
        Win(sim);
        CreatureState reaper = Reaper(sim);
        sim.Tick(InputFrame.Of(new ContinueAfterResultCommand(new PlayerId(Keeper)), new RevealMapCommand(new PlayerId(Keeper))));

        ref PlayerState player = ref sim.World.Player(Keeper);
        Assert.Equal(PlayerStatus.Playing, player.Status);
        Assert.Equal(FinaleRules.ResultContinued | FinaleRules.Continued,
            player.Finale.Flags & (FinaleRules.ResultContinued | FinaleRules.Continued | FinaleRules.ContinuedFromConquered));
        Assert.Equal(ReaperRules.DepartingState, reaper.Dk2StateId);
        Assert.Contains(events.Events, e => e.ScriptedView is { Kind: ScriptedViewKind.Letterbox, Value: 2, Flag: false });
        Assert.True(sim.World.Tile(0, 0).IsExploredBy(new PlayerId(Keeper)));
    }

    [Fact]
    public void TheFinaleFieldsAreHashedAndSaved()
    {
        (GameSimulation sim, _, _) = Setup();
        Win(sim);
        for (int t = 0; t < 3; t++) sim.Tick(InputFrame.Empty);
        ulong before = sim.StateHash();
        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(sim), sim.Database, SimulationSystems.Default());
        Assert.Equal(sim.World.Player(Keeper).Finale, restored.World.Player(Keeper).Finale);
        Assert.Equal(before, restored.StateHash());

        sim.World.Player(Keeper).Finale.PickupStage ^= 1;
        Assert.NotEqual(before, sim.StateHash());
        sim.World.Player(Keeper).Finale.PickupStage ^= 1;
        Reaper(sim).ReaperHidden ^= true;
        Assert.NotEqual(before, sim.StateHash());
        Reaper(sim).ReaperHidden ^= true;
        sim.World.Player(Keeper).Finale.CameraGlideEndTick++;
        Assert.NotEqual(before, sim.StateHash());
        sim.World.Player(Keeper).Finale.CameraGlideEndTick--;
        sim.World.LocalPlayerId = new PlayerId(PlayerIds.Good);
        Assert.NotEqual(before, sim.StateHash());
    }
}
