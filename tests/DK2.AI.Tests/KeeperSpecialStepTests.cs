// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.AI.Tests;

/// <summary>The specials step against <c>0x00500520</c> (computer-keeper-management.md §3): the
/// keeper's own <c>TypeSpecial</c> objects on its own tiles that no creature holds, the last of the
/// walk kept, and only then its id tested.</summary>
public class KeeperSpecialStepTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();
    private const byte Keeper = KeeperWorld.Keeper;
    private static readonly TilePos Own = new(14, 15), OwnToo = new(16, 15), Unclaimed = new(5, 5);

    private static ObjectState Box(WorldState world, byte type, TilePos at, byte owner = Keeper) =>
        world.SpawnObject(Db, type, owner, (at.Y * world.Width) + at.X);

    private static List<PlayerCommand> Run(WorldState world)
    {
        var commands = new List<PlayerCommand>();
        KeeperSpecialStep.Run(new KeeperView(Db, world, Keeper), commands);
        return commands;
    }

    [Fact]
    public void Special_00500520_AnOwnBoxOnAnOwnTileIsActivatedWithNoTarget()
    {
        WorldState world = KeeperWorld.World(Db);
        ObjectState box = Box(world, KeeperWorld.IncreaseLevelObjectId, Own);

        ActivateSpecialCommand command = Assert.IsType<ActivateSpecialCommand>(Assert.Single(Run(world)));
        Assert.Equal(box.Id, command.ObjectId);
        Assert.Equal(0, command.TargetId);
        Assert.Equal(new PlayerId(Keeper), command.PlayerId);
    }

    [Fact]
    public void Special_00500520_ABoxOffTheKeepersLandAnotherOwnersOrAHeldOneIsNotUsed()
    {
        WorldState world = KeeperWorld.World(Db);
        Box(world, KeeperWorld.IncreaseLevelObjectId, Unclaimed);
        Box(world, KeeperWorld.IncreaseLevelObjectId, Own, owner: 0);
        Box(world, KeeperWorld.IncreaseLevelObjectId, OwnToo).CarriedByCreatureId = 1;

        Assert.Empty(Run(world));
    }

    [Fact]
    public void Special_005005C2_AnExcludedLastBoxSuppressesAnEarlierUsableOne()
    {
        // The walk runs newest-owned first and keeps the last qualifying box: the older 127 wins
        // the scan and its id fails the switch, so the newer 117 is not used either.
        WorldState world = KeeperWorld.World(Db);
        Box(world, KeeperWorld.KillCreaturesObjectId, Own);
        Box(world, KeeperWorld.IncreaseLevelObjectId, OwnToo);

        Assert.Empty(Run(world));
    }

    [Fact]
    public void Special_005005C2_AnExcludedNewerBoxLeavesTheOlderUsableOneKept()
    {
        WorldState world = KeeperWorld.World(Db);
        ObjectState older = Box(world, KeeperWorld.IncreaseLevelObjectId, Own);
        Box(world, KeeperWorld.KillCreaturesObjectId, OwnToo);

        ActivateSpecialCommand command = Assert.IsType<ActivateSpecialCommand>(Assert.Single(Run(world)));
        Assert.Equal(older.Id, command.ObjectId);
    }

    [Fact]
    public void Special_00500DEF_TheKeeperUsesItEveryTickWithNoAllowance()
    {
        WorldState world = KeeperWorld.World(Db);
        ObjectState box = Box(world, KeeperWorld.IncreaseLevelObjectId, Own);
        var ai = new KeeperAI(Keeper, KeeperWorld.Parameters(speed: 0), new SimRandom(5));

        IReadOnlyList<PlayerCommand> commands = ai.Think(Db, world);

        Assert.Contains(commands, c => c is ActivateSpecialCommand a && a.ObjectId == box.Id);
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(7, false)]
    [InlineData(116, false)]
    [InlineData(117, true)]
    [InlineData(126, true)]
    [InlineData(127, false)]
    [InlineData(128, true)]
    [InlineData(129, true)]
    [InlineData(140, false)]
    public void Special_005005C2_TheSwitchTakesEight117To126128And129(byte id, bool usable) =>
        Assert.Equal(usable, KeeperSpecialStep.Usable(id));
}
