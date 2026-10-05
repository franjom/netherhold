// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI.Tests;

/// <summary>The keeper's make-happy remedy <c>0x004F9710</c> and the walk's 17/18 arms
/// (computer-keeper-management.md §13, ADR-0353).</summary>
public class KeeperMoodRemediesTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;

    private static KeeperAIParameters Happy(bool unhappy = true, bool angry = true) =>
        KeeperWorld.Parameters() with { UnhappyCreaturesHappy = unhappy, AngryCreaturesHappy = angry };

    private static (WorldState World, CreatureState Creature) Moody(AngerBucket largest, int amount = 20000)
    {
        WorldState world = KeeperWorld.World(Db);
        CreatureState creature = world.SpawnCreature(Db, KeeperWorld.MoodyCreature, Keeper, new TilePos(14, 14));
        creature.Anger[largest] = amount;
        return (world, creature);
    }

    private static void Paint(WorldState world, byte floor, int x0, int y0)
    {
        for (int y = y0; y < y0 + 3; y++)
            for (int x = x0; x < x0 + 3; x++)
                KeeperWorld.Set(world, Db, new TilePos(x, y), floor, Keeper);
    }

    private static (bool Handled, List<PlayerCommand> Commands, KeeperView View) Remedy(
        WorldState world, PlayerMessage message, KeeperAIParameters? p = null, KeeperAIState? state = null)
    {
        var commands = new List<PlayerCommand>();
        var view = new KeeperView(Db, world, Keeper);
        var remedies = new KeeperMoodRemedies(view, state ?? new KeeperAIState(), p ?? Happy(), new SimRandom(7), commands);
        bool handled = message.Kind == PlayerMessageKind.CreatureAngry ? remedies.Angry(message) : remedies.Unhappy(message);
        return (handled, commands, view);
    }

    private static PlayerMessage Post(WorldState world, PlayerMessageKind kind, AngerComplaint complaint, int creature) =>
        world.Messages.Post(world, Keeper, kind, (int)complaint, creature)!.Value;

    [Fact]
    public void ARetainedHungerMessageWhileLairAngerIsLargestMovesTheCreatureToTheLair()
    {
        var (world, creature) = Moody(AngerBucket.Lair);
        creature.Anger.Hunger = 19000;
        Paint(world, KeeperWorld.LairFloor, 10, 10);
        Paint(world, KeeperWorld.HatcheryFloor, 18, 10);
        world.SpawnObject(Db, HatcheryRules.ChickenObjectId, Keeper, 10 * world.Width + 18);

        var (handled, commands, view) = Remedy(world, Post(world, PlayerMessageKind.CreatureUnhappy, AngerComplaint.NoFood, creature.Id));

        Assert.True(handled);
        KeeperRoomView lair = Assert.Single(view.OwnRooms, r => r.Definition.TypeId == RoomTypeId.Lair);
        Assert.Equal<PlayerCommand>([new PickUpCreatureCommand(Keeper, creature.Id), new DropCommand(Keeper, view.PositionOf(lair))], commands);
    }

    [Fact]
    public void EqualHungerAndLairAngerFeedTheHatcherysListHead_OursTakesTheHeadAsTheHighestIdChicken()
    {
        var (world, creature) = Moody(AngerBucket.Lair);
        creature.Anger.Hunger = 20000;
        Paint(world, KeeperWorld.LairFloor, 10, 10);
        Paint(world, KeeperWorld.HatcheryFloor, 18, 10);
        ObjectState older = world.SpawnObject(Db, HatcheryRules.ChickenObjectId, Keeper, 10 * world.Width + 18);
        ObjectState newer = world.SpawnObject(Db, HatcheryRules.ChickenObjectId, Keeper, 11 * world.Width + 19);

        var (handled, commands, _) = Remedy(world, Post(world, PlayerMessageKind.CreatureUnhappy, AngerComplaint.NoLair, creature.Id));

        Assert.True(handled);
        Assert.True(newer.Id > older.Id);
        Assert.Equal<PlayerCommand>([new PickUpObjectCommand(Keeper, newer.Id), new DropCommand(Keeper, creature.Tile, creature.Id)], commands);
    }

    [Theory]
    [InlineData(AngerBucket.Company)]
    [InlineData(AngerBucket.Pay)]
    [InlineData(AngerBucket.Treatment)]
    public void BucketsZeroFourAndFivePayTheWageMintedIntoTheHand(AngerBucket bucket)
    {
        var (world, creature) = Moody(bucket);
        Paint(world, KeeperWorld.TreasuryFloor, 10, 10);
        world.Tile(10, 10).GoldHeld = 500;

        var (handled, commands, _) = Remedy(world, Post(world, PlayerMessageKind.CreatureUnhappy, AngerComplaint.Money, creature.Id));

        Assert.True(handled);
        Assert.Equal<PlayerCommand>([new MintWageCommand(Keeper, creature.Id), new DropCommand(Keeper, creature.Tile, creature.Id)], commands);
    }

    [Fact]
    public void PayWithNoTreasuryFailsAndIsHandledBelowThreeEventTasks()
    {
        var (world, creature) = Moody(AngerBucket.Pay);
        var (handled, commands, _) = Remedy(world, Post(world, PlayerMessageKind.CreatureUnhappy, AngerComplaint.Money, creature.Id));

        Assert.True(handled);
        Assert.Empty(commands);
    }

    [Fact]
    public void AFailedRemedyWithThreeEventTasksQueuedStaysUnhandledForRetry()
    {
        var (world, creature) = Moody(AngerBucket.Pay);
        var state = new KeeperAIState();
        for (int i = 0; i < 3; i++) Assert.True(state.Responses.TryReceive(() => new KeeperThreatResponse(1, new TilePos(i, 1))));

        var (handled, commands, _) = Remedy(world, Post(world, PlayerMessageKind.CreatureUnhappy, AngerComplaint.Money, creature.Id), state: state);

        Assert.False(handled);
        Assert.Empty(commands);
    }

    [Fact]
    public void WorkAngerWalksTheHappyTableAndSkipsAJobWhoseRoomIsMissing()
    {
        var (world, creature) = Moody(AngerBucket.Work);
        Paint(world, KeeperWorld.TrainingFloor, 10, 10);

        var (handled, commands, view) = Remedy(world, Post(world, PlayerMessageKind.CreatureUnhappy, AngerComplaint.NoWork, creature.Id));

        Assert.True(handled);
        KeeperRoomView training = Assert.Single(view.OwnRooms, r => r.Definition.TypeId == RoomTypeId.TrainingRoom);
        Assert.Equal<PlayerCommand>([new PickUpCreatureCommand(Keeper, creature.Id), new DropCommand(Keeper, view.PositionOf(training))], commands);
    }

    [Fact]
    public void AJobRoomMoveWaitsForTheDroppedCounterOnceTheGameIsPastThreeHundredTurns()
    {
        var (world, creature) = Moody(AngerBucket.Work);
        Paint(world, KeeperWorld.TrainingFloor, 10, 10);
        var sim = new GameSimulation(Db, world, seed: 1, []);
        sim.Run(400);
        creature.DroppedTick = world.CurrentTick.Value - 100;

        var (handled, commands, view) = Remedy(world, Post(world, PlayerMessageKind.CreatureUnhappy, AngerComplaint.NoWork, creature.Id));

        Assert.True(view.Creatures.Single(c => c.Id == creature.Id).LastDroppedCounter <= KeeperMoodRemedies.DroppedGraceTurns);
        Assert.True(handled);
        Assert.Empty(commands);
    }

    [Fact]
    public void AContentOrMissingCreatureIsHandledWithoutARemedy()
    {
        var (world, creature) = Moody(AngerBucket.Lair, amount: 100);
        Paint(world, KeeperWorld.LairFloor, 10, 10);
        var (content, commands, _) = Remedy(world, Post(world, PlayerMessageKind.CreatureUnhappy, AngerComplaint.NoLair, creature.Id));
        var (missing, more, _) = Remedy(world, Post(world, PlayerMessageKind.CreatureAngry, AngerComplaint.NoLair, 999));

        Assert.True(content && missing);
        Assert.Empty(commands);
        Assert.Empty(more);
    }

    [Fact]
    public void WithoutUnhappyCreaturesHappyA17IsHandledAndNothingMoves()
    {
        var (world, creature) = Moody(AngerBucket.Lair);
        Paint(world, KeeperWorld.LairFloor, 10, 10);
        var (handled, commands, _) = Remedy(world, Post(world, PlayerMessageKind.CreatureUnhappy, AngerComplaint.NoLair, creature.Id),
            Happy(unhappy: false));

        Assert.True(handled);
        Assert.Empty(commands);
    }

    [Fact]
    public void An18IsMadeHappyUnderAngryCreaturesHappy()
    {
        var (world, creature) = Moody(AngerBucket.Lair, amount: 40000);
        Paint(world, KeeperWorld.LairFloor, 10, 10);
        var (handled, commands, _) = Remedy(world, Post(world, PlayerMessageKind.CreatureAngry, AngerComplaint.NoLair, creature.Id),
            Happy(unhappy: false));

        Assert.True(handled);
        Assert.IsType<PickUpCreatureCommand>(commands[0]);
    }

    private static KeeperAIParameters Disposing(byte method) =>
        KeeperWorld.Parameters() with { AngryCreaturesDisposeOf = true, AngryDisposalMethod = method, AngryCreaturesHappy = true };

    [Fact]
    public void DisposalMethodOneDropsTheAngryCreatureOnATempleFloorSlot()
    {
        var (world, creature) = Moody(AngerBucket.Lair, amount: 40000);
        Paint(world, KeeperWorld.TempleFloor, 10, 10);
        var (handled, commands, _) = Remedy(world, Post(world, PlayerMessageKind.CreatureAngry, AngerComplaint.NoLair, creature.Id),
            Disposing(1));

        Assert.True(handled);
        Assert.Equal<PlayerCommand>([new PickUpCreatureCommand(Keeper, creature.Id), new DropCommand(Keeper, new TilePos(11, 11))], commands);
    }

    [Fact]
    public void DisposalMethodZeroStillTakesATempleFirstAtItsPosition()
    {
        var (world, creature) = Moody(AngerBucket.Lair, amount: 40000);
        Paint(world, KeeperWorld.TempleFloor, 10, 10);
        Paint(world, KeeperWorld.PortalFloor, 20, 10);
        var (handled, commands, view) = Remedy(world, Post(world, PlayerMessageKind.CreatureAngry, AngerComplaint.NoLair, creature.Id),
            Disposing(0));

        Assert.True(handled);
        KeeperRoomView temple = Assert.Single(view.OwnRooms, r => r.Definition.TypeId == RoomTypeId.Temple);
        Assert.Equal(new DropCommand(Keeper, view.PositionOf(temple)), commands[^1]);
    }

    [Theory]
    [InlineData(KeeperWorld.PortalFloor, RoomTypeId.Portal)]
    [InlineData(KeeperWorld.HeroPortalFloor, RoomTypeId.HeroPortal)]
    public void WithoutATempleTheCreatureIsDroppedAtAPortalOrElseAHeroPortal(byte floor, RoomTypeId type)
    {
        var (world, creature) = Moody(AngerBucket.Lair, amount: 40000);
        Paint(world, floor, 20, 10);
        var (handled, commands, view) = Remedy(world, Post(world, PlayerMessageKind.CreatureAngry, AngerComplaint.NoLair, creature.Id),
            Disposing(1));

        Assert.True(handled);
        KeeperRoomView portal = Assert.Single(view.OwnRooms, r => r.Definition.TypeId == type);
        Assert.Equal<PlayerCommand>([new PickUpCreatureCommand(Keeper, creature.Id), new DropCommand(Keeper, view.PositionOf(portal))], commands);
    }

    [Fact]
    public void WithNowhereToDisposeAtThe18StaysUnhandledForRetry()
    {
        var (world, creature) = Moody(AngerBucket.Lair, amount: 40000);
        var (handled, commands, _) = Remedy(world, Post(world, PlayerMessageKind.CreatureAngry, AngerComplaint.NoLair, creature.Id),
            Disposing(1));

        Assert.False(handled);
        Assert.Empty(commands);
    }

    [Fact]
    public void ADisposalWhoseLiftIsRefusedStaysUnhandled()
    {
        var (world, creature) = Moody(AngerBucket.Lair, amount: 40000);
        Paint(world, KeeperWorld.PortalFloor, 20, 10);
        creature.InHand = true;
        var (handled, commands, _) = Remedy(world, Post(world, PlayerMessageKind.CreatureAngry, AngerComplaint.NoLair, creature.Id),
            Disposing(0));

        Assert.False(handled);
        Assert.Empty(commands);
    }

    [Fact]
    public void TheWalkAcknowledgesAHandled17AndRetriesAnUnhandledOne()
    {
        var (world, creature) = Moody(AngerBucket.Pay);
        PlayerMessage message = Post(world, PlayerMessageKind.CreatureUnhappy, AngerComplaint.Money, creature.Id);
        var state = new KeeperAIState();
        for (int i = 0; i < 3; i++) state.Responses.TryReceive(() => new KeeperThreatResponse(1, new TilePos(i, 1)));

        var commands = new List<PlayerCommand>();
        var view = new KeeperView(Db, world, Keeper);
        var rng = new SimRandom(7);
        new KeeperThreatResponder(view, state, Happy(), rng, commands,
            mood: new KeeperMoodRemedies(view, state, Happy(), rng, commands)).HandleMessages();
        Assert.DoesNotContain(commands, c => c is AcknowledgePlayerMessageCommand);

        var fresh = new KeeperAIState();
        commands.Clear();
        new KeeperThreatResponder(view, fresh, Happy(), rng, commands,
            mood: new KeeperMoodRemedies(view, fresh, Happy(), rng, commands)).HandleMessages();
        Assert.Contains(new AcknowledgePlayerMessageCommand(Keeper, message.Id), commands);
    }
}
