// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

/// <summary>
/// The action-point walker and the seven condition types its dispatcher owns.
/// Rectangles are read off <see cref="TestWorld.Map"/>: an impenetrable border, a ring of
/// rock — the fixture's only <see cref="TerrainFlags.Taggable"/> terrain — and dirt path
/// inside, owned by player 1. Each root's child is one flag write, so "flag 9 is 1" reads
/// as "the condition held".
/// </summary>
public class TriggerAreaConditionsTests
{
    /// <summary>Trigger id of the flag write every root here descends into: action id 2,
    /// which the walker sees as the negative <c>-2</c>.</summary>
    private const ushort Child = unchecked((ushort)-2);

    private const byte Marker = 9;

    [Fact]
    public void ATreeHangingOffAnActionPointIsWalkedEveryTurn()
    {
        TriggerState state = Run(
            Point(4, root: 1, 5, 5, 6, 6),
            Area(1, 41, new() { ["PlayerId"] = 1, ["TerrainId"] = TestWorld.DirtPath }));

        Assert.Equal(1, state.Read(Marker));
    }

    [Fact]
    public void AnActionPointWithNoRootIsNotWalked()
    {
        TriggerState state = Run(
            Point(4, root: 0, 5, 5, 6, 6),
            Area(1, 41, new() { ["PlayerId"] = 1, ["TerrainId"] = TestWorld.DirtPath }));

        Assert.Equal(0, state.Read(Marker));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    public void Type62CountsTilesTaggedByTheNamedPlayer(int value, int expected)
    {
        // Rock at (1,5) and (1,6); exactly one of them tagged.
        TriggerState state = Run(
            Point(4, root: 1, 1, 5, 1, 6),
            Area(1, 62, new() { ["Comparison"] = 3, ["PlayerId"] = 1, ["Value"] = value }),
            world => world.Tile(1, 5).SetTaggedBy(1, true));

        Assert.Equal(expected, state.Read(Marker));
    }

    [Fact]
    public void ATagBelongingToAnotherPlayerDoesNotCount()
    {
        TriggerState state = Run(
            Point(4, root: 1, 1, 5, 1, 6),
            Area(1, 62, new() { ["Comparison"] = 3, ["PlayerId"] = 2, ["Value"] = 0 }),
            world => world.Tile(1, 5).SetTaggedBy(1, true));

        Assert.Equal(1, state.Read(Marker));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    public void Type69AsksWhetherEveryTaggableTileInTheRectangleIsTagged(int tagged, int expected)
    {
        // (1,5)..(1,7) is three rock tiles, and rock is the fixture's only taggable terrain.
        TriggerState state = Run(
            Point(4, root: 1, 1, 5, 1, 7),
            Area(1, 69, new() { ["PlayerId"] = 1 }),
            world =>
            {
                for (int y = 5; y < 5 + tagged; y++) world.Tile(1, y).SetTaggedBy(1, true);
            });

        Assert.Equal(expected, state.Read(Marker));
    }

    [Theory]
    [InlineData((byte)40)]
    [InlineData((byte)42)]
    public void Types40And42CountTilesMatchingAnOwnerAndATerrainType(byte type)
    {
        TriggerState state = Run(
            Point(4, root: 1, 5, 5, 6, 6),
            Area(1, type, new()
            {
                ["Comparison"] = 3, ["PlayerId"] = 1,
                ["TerrainId"] = TestWorld.DirtPath, ["Value"] = 4,
            }));

        Assert.Equal(1, state.Read(Marker));
    }

    /// <summary>
    /// The quirk: with a terrain type named, ownership is decided by the owner byte alone
    /// and terrain that cannot be owned at all still counts; drop the terrain type and the
    /// <see cref="TerrainFlags.Ownable"/> test appears. Both branches are DK2's
    /// (<c>FUN_004B07E0</c>), and rock is not ownable in this fixture.
    /// </summary>
    [Theory]
    [InlineData(TestWorld.Rock, 1)]
    [InlineData((byte)0, 0)]
    public void OwnershipIsTestedForOwnableTerrainOnlyWhenNoTerrainTypeIsNamed(
        byte terrain, int expected)
    {
        TriggerState state = Run(
            Point(4, root: 1, 1, 5, 1, 5),
            Area(1, 40, new()
            {
                ["Comparison"] = 3, ["PlayerId"] = 1, ["TerrainId"] = terrain, ["Value"] = 1,
            }),
            world => world.Tile(1, 5).OwnerPlayerId = 1);

        Assert.Equal(expected, state.Read(Marker));
    }

    /// <summary>Both selectors zero returns the rectangle's own size without reading a
    /// tile, so a rectangle hanging off the map still answers with its full area.</summary>
    [Fact]
    public void NamingNeitherAnOwnerNorATerrainReturnsTheWholeRectangle()
    {
        TriggerState state = Run(
            Point(4, root: 1, 22, 18, 25, 21),
            Area(1, 40, new()
            {
                ["Comparison"] = 3, ["PlayerId"] = 0, ["TerrainId"] = 0, ["Value"] = 16,
            }));

        Assert.Equal(1, state.Read(Marker));
    }

    [Theory]
    [InlineData(TestWorld.DirtPath, 1)]
    [InlineData(TestWorld.Rock, 0)]
    public void Type41AsksWhetherTheWholeRectangleMatches(byte terrain, int expected)
    {
        TriggerState state = Run(
            Point(4, root: 1, 5, 5, 6, 6),
            Area(1, 41, new() { ["PlayerId"] = 1, ["TerrainId"] = terrain }));

        Assert.Equal(expected, state.Read(Marker));
    }

    [Theory]
    [InlineData((byte)39)]
    [InlineData((byte)70)]
    public void TheTwoAreaTypesThatCountThingsAreDeclinedRatherThanTakenAsFalse(byte type)
    {
        TriggerRunResult result = Execute(
            Point(4, root: 1, 5, 5, 6, 6),
            Area(1, type, new() { ["TargetId"] = 1, ["TargetType"] = 1, ["PlayerId"] = 1 }),
            arrange: null,
            new TriggerState());

        Assert.Contains(result.Blocked, b => b.Tag == 213 && b.Type == type);
    }

    /// <summary>A type the area dispatcher does not own reaches the world evaluator with
    /// the world as <c>this</c>, so a flag test means in an action point's tree exactly
    /// what it means anywhere (<c>0x004B0EC9</c>).</summary>
    [Fact]
    public void ATypeTheAreaDispatcherDoesNotOwnIsDecidedByTheWorldEvaluator()
    {
        TriggerState state = Run(
            Point(4, root: 1, 5, 5, 6, 6),
            Area(1, 1, new()
            {
                ["Comparison"] = 3, ["FlagId"] = 0, ["Flag"] = 1,
                ["IndirectFlagId"] = 0, ["Value"] = 0,
            }));

        Assert.Equal(1, state.Read(Marker));
    }

    /// <summary>And the converse: an area-owned type reached by the <b>world</b> walker
    /// has no rectangle to measure over and no world arm to fall back on, so it is
    /// declined rather than answered.</summary>
    [Fact]
    public void AnAreaOwnedTypeInTheWorldTreeIsDeclined()
    {
        var state = new TriggerState();
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        var tree = new TriggerTree(
            [
                Area(1, 62, new() { ["Comparison"] = 3, ["PlayerId"] = 1, ["Value"] = 0 }),
                FlagWrite(),
            ],
            rootTriggerId: 1);

        TriggerRunResult result = new TriggerExecutor(tree, state, world, database).Run(1);

        Assert.Equal(0, state.Read(Marker));
        Assert.Contains(result.Blocked, b => b.Type == 62);
    }

    /// <summary>
    /// The repeat argument the frame passes is the <b>root record's own</b> byte, cached
    /// on the point when the root was attached (<c>0x004B0C90</c>). Inherited 1 retires a
    /// successful top-level action, so the root's repeat decides whether an action beside
    /// it is one-shot — its next sibling being the only place the inherited value shows.
    /// </summary>
    [Theory]
    [InlineData((byte)1, 1)]
    [InlineData((byte)2, 3)]
    public void TheRepeatArgumentComesFromTheRootRecordNotTheActionPoint(byte repeat, int expected)
    {
        // Rock over dirt path, so the root fails and the walk carries on to its sibling.
        TriggerRecord root = Area(
            1, 41, new() { ["PlayerId"] = 1, ["TerrainId"] = TestWorld.Rock },
            next: Child, repeat: repeat);

        var state = new TriggerState();
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        ThingProjection.Place(world, database, [Point(4, root: 1, 5, 5, 6, 6)]);
        var executor = new TriggerExecutor(
            new TriggerTree([root, FlagWrite(mode: 0x12)], rootTriggerId: 0),
            state, world, database);

        for (int turn = 1; turn <= 3; turn++) executor.Run(turn);

        Assert.Equal(expected, state.Read(Marker));
    }

    private static TriggerState Run(
        ActionPointThing point, TriggerRecord root, Action<WorldState>? arrange = null)
    {
        var state = new TriggerState();
        Execute(point, root, arrange, state);
        return state;
    }

    private static TriggerRunResult Execute(
        ActionPointThing point, TriggerRecord root, Action<WorldState>? arrange, TriggerState state)
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);
        ThingProjection.Place(world, database, [point]);
        arrange?.Invoke(world);

        // World root zero, so nothing but the action point is walked: these tests are
        // about the area walker, and a world root would add records to the census.
        var tree = new TriggerTree([root, FlagWrite()], rootTriggerId: 0);
        return new TriggerExecutor(tree, state, world, database).Run(1);
    }

    private static ActionPointThing Point(byte id, ushort root, int x0, int y0, int x1, int y1) =>
        new(new byte[56])
        {
            StartX = x0, StartY = y0, EndX = x1, EndY = y1,
            ActionPointId = id, TriggerId = root, Name = "fixture",
        };

    private static TriggerRecord Area(
        ushort id,
        byte type,
        Dictionary<string, long> parameters,
        ushort next = 0,
        byte repeat = 1) =>
        new GenericTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = id, Type = type, RepeatCount = repeat,
            ChildTriggerId = next == 0 ? Child : (ushort)0, NextTriggerId = next,
            Parameters = parameters,
        };

    /// <summary>The tag-214 marker write. Mode <c>0x0A</c> assigns a literal 1, <c>0x12</c>
    /// adds one, which counts turns for the repeat test.</summary>
    private static TriggerRecord FlagWrite(byte mode = 0x0A) =>
        new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = Child, Type = 7,
            Parameters = new Dictionary<string, long>
            {
                ["FlagId"] = Marker, ["Flag"] = mode, ["Value"] = 1,
            },
        };
}
