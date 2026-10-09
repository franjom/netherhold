// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using Xunit;

namespace DK2.AI.Tests;

/// <summary>The personality setter <c>0x004F8C60</c> (KEEPER_AI.md §3.2): the nine table rows, the
/// level-name remap at <c>0x006A30A0</c>, the computed room list, the skill cap and the fixed fields.</summary>
public class KeeperAIPersonalityTests
{
    private static readonly GameDatabase Db = KeeperWorld.Database();

    private static KeeperAIParameters Of(KeeperPersonality p, string map = "Anywhere") =>
        KeeperAIPersonality.Parameters(p, map, Db, 80, 64);

    [Fact]
    public void EachRowGivesTheTablesValues()
    {
        KeeperPersonality[] all = Enum.GetValues<KeeperPersonality>();
        // KEEPER_AI.md §3.2's table, a row per personality.
        Assert.Equal([25, 16, 9, 25, 16, 12, 25, 16, 12], all.Select(p => (int)Of(p).DefaultRoomSize));
        Assert.Equal([99, 70, 30, 99, 50, 25, 99, 50, 25], all.Select(p => (int)Of(p).TrapAndDoorUsage));
        Assert.Equal([15, 10, 5, 15, 15, 15, 4, 4, 4], all.Select(p => (int)Of(p).IAMinimumCreatures));
        Assert.Equal([20, 125, 0, 100, 100, 100, 200, 200, 200], all.Select(p => (int)Of(p).IAThreatSuperiority));
        Assert.Equal([2000, 1500, 1000, 2150, 2000, 2500, 2000, 2500, 3500], all.Select(p => (int)Of(p).BACallToArmsValue));
        Assert.Equal([20, 15, 10, 15, 10, 5, 12, 8, 4], all.Select(p => (int)Of(p).MaximumImps));
        Assert.Equal([1, 1, 2, 0, 0, 2, 0, 1, 2], all.Select(p => (int)Of(p).CallToArmsUsage));
        Assert.Equal([0, 2, 1, 0, 2, 1, 0, 3, 1], all.Select(p => (int)Of(p).ImprisonedCreatureAction));
        Assert.Equal([false, false, false, false, false, false, false, true, true], all.Select(p => Of(p).NeverAttack));
        Assert.Equal([0, 15, 30, 0, 15, 30, 0, 15, 30], all.Select(p => (int)Of(p).MinimumRandomTimeBeforePlacingReseachedRoom));
        Assert.Equal([true, false, false, false, false, false, false, false, false], all.Select(p => Of(p).AutomaticallyBuildBiggerLairWhenPortalTakenOver));
        Assert.Equal([2, 1, 0, 1, 1, 1, 1, 1, 1], all.Select(p => (int)Of(p).TilesLeftBetweenRooms));
        Assert.Equal([false, false, true, false, false, true, false, true, true], all.Select(p => Of(p).PlaceRoomsNextToIdeal));
        Assert.Equal([1, 0, 0, 1, 0, 2, 2, 2, 0], all.Select(p => (int)Of(p).CorridorStyle));
        Assert.Equal([0, 0, 1, 0, 0, 1, 0, 0, 1], all.Select(p => (int)Of(p).BuildNewRoomWhenMoreSpaceRequired));
        Assert.Equal([25, 20, 40, 25, 20, 15, 15, 14, 13], all.Select(p => (int)Of(p).NeutralRoomRangeFromHeart));
        Assert.Equal([0, 0, 1, 0, 0, 1, 0, 2, 1], all.Select(p => (int)Of(p).DoorPreference));
        Assert.Equal([8, 5, 0, 8, 5, 2, 0, 0, 0], all.Select(p => (int)Of(p).IACreatureSkillLevel));
        Assert.Equal([2, 0, 3, 2, 1, 3, 2, 1, 3], all.Select(p => (int)Of(p).IAFirstEnd));
        Assert.Equal([3, 3, 2, 0, 1, 2, 0, 3, 2], all.Select(p => (int)Of(p).IAFirstStart));
        Assert.Equal([2, 1, 1, 2, 1, 1, 1, 1, 1], all.Select(p => (int)Of(p).BreachAtXPoints));
        Assert.Equal([false, false, false, false, false, true, true, true, true], all.Select(p => Of(p).IAOnlyAttackAttackers));
        Assert.Equal([125, 100, 50, 125, 110, 90, 150, 150, 150], all.Select(p => (int)Of(p).BAThreatSuperiority));
        Assert.Equal([2, 2, 0, 2, 2, 2, 2, 2, 2], all.Select(p => (int)Of(p).AttackingVsDefensiveSpellPreference));
        Assert.Equal([5, 5, 0, 15, 8, 25, 15, 20, 25], all.Select(p => (int)Of(p).ImprisonPercentage));
        Assert.Equal([10, 10, 10, 20, 20, 20, 30, 30, 30], all.Select(p => (int)Of(p).BARetreatHealth));
        Assert.Equal([1, 1, 1, 2, 2, 2, 5, 5, 5], all.Select(p => (int)Of(p).RemoveCallToArmsEnemyCreatures));
        Assert.Equal([10, 5, 1, 10, 10, 10, 4, 4, 4], all.Select(p => (int)Of(p).RemoveCallToArmsOwnCreatures));
        Assert.Equal([true, false, false, true, false, false, true, false, false], all.Select(p => Of(p).UnhappyCreaturesHappy));
        Assert.Equal([true, true, false, true, true, false, true, true, false], all.Select(p => Of(p).AngryCreaturesHappy));
        Assert.Equal([false, false, true, false, false, true, false, false, true], all.Select(p => Of(p).AngryCreaturesDisposeOf));
        Assert.Equal([1, 1, 0, 1, 1, 0, 1, 1, 0], all.Select(p => (int)Of(p).AngryDisposalMethod));
        Assert.Equal([2, 1, 2, 2, 1, 2, 2, 1, 1], all.Select(p => (int)Of(p).UseSightOfEvil));
        Assert.Equal([true, false, false, true, false, false, true, false, false], all.Select(p => Of(p).LightningInWater));
        Assert.Equal([0, 1, 2, 0, 1, 2, 0, 1, 2], all.Select(p => (int)Of(p).ProbabilityOfMovingCreatureForResearch));
        Assert.Equal([9000, 6000, 6000, 9000, 6000, 6000, 9000, 6000, 6000], all.Select(p => (int)Of(p).TendancyMoney));
        Assert.Equal([10, 8, 15, 8, 5, 2, 5, 4, 3], all.Select(p => (int)Of(p).SpecialRange));
        Assert.Equal([90, 50, 20, 90, 50, 20, 90, 50, 20], all.Select(p => (int)Of(p).SpecialFindChance));
        Assert.Equal([15, 25, 35, 15, 20, 25, 15, 15, 15], all.Select(p => (int)Of(p).TilesPerImpRatio));
        Assert.Equal([50, 50, 50, 20, 20, 20, 15, 15, 15], all.Select(p => (int)Of(p).Openness));
        Assert.All(all, p =>
        {
            KeeperAIParameters k = Of(p);
            Assert.Equal(1, k.TendancySpeed);
            Assert.Equal(240, k.RemoveCallToArmsAfter);
            Assert.Equal(90, k.WaitBeforeAttackingAgain);
            Assert.Equal(100, k.StartingMana);
        });
    }

    [Theory]
    [InlineData(KeeperPersonality.MasterKeeper, "Spiral", KeeperPersonality.Conqueror)]
    [InlineData(KeeperPersonality.Psychotic, "Spiral", KeeperPersonality.Psychotic)]
    [InlineData(KeeperPersonality.Paranoid, "OnSlaught", KeeperPersonality.MasterKeeper)]
    [InlineData(KeeperPersonality.Guardian, "Clover", KeeperPersonality.Psychotic)]
    [InlineData(KeeperPersonality.MasterKeeper, "spiral", KeeperPersonality.MasterKeeper)] // case-sensitive
    [InlineData(KeeperPersonality.Idiot, "level17", KeeperPersonality.Idiot)]
    public void TheLevelNameRemapsSomePersonalities(KeeperPersonality chosen, string map, KeeperPersonality used)
    {
        Assert.Equal(used, KeeperAIPersonality.Remap(chosen, map));
        Assert.Equal(Of(used).DefaultRoomSize, Of(chosen, map).DefaultRoomSize);
        Assert.Equal((byte)chosen, Of(chosen, map).EditorAiType);
    }

    [Fact]
    public void TheRoomListIsTheBuildableRoomsAndPortalsInIdOrder_AShortfallIsZeroWhereDk2Faults()
    {
        // The test database has fewer than fifteen; past the last definition DK2 finds none.
        byte[] list = KeeperAIPersonality.RoomList(Db);
        byte[] expected = Db.Rooms.Values.OrderBy(r => r.RoomId)
            .Where(r => r.Flags.HasFlag(RoomFlags.Buildable) || r.RoomId is (byte)RoomTypeId.Portal or (byte)RoomTypeId.HeroPortal)
            .Select(r => r.RoomId).ToArray();
        Assert.Equal(expected, list.Take(expected.Length));
        Assert.All(list.Skip(expected.Length), id => Assert.Equal(0, id));
        Assert.Equal(KeeperAIPersonality.RoomListLength, list.Length);
    }

    [Fact]
    public void TheBuildAreaIsTheMapLessItsEdgeAndEveryFeatureIsOn()
    {
        KeeperAIParameters k = Of(KeeperPersonality.Greyman);
        Assert.Equal((1, 1, 79, 63), ((int)k.MaxBuildAreaMinX, (int)k.MaxBuildAreaMinY, (int)k.MaxBuildAreaMaxX, (int)k.MaxBuildAreaMaxY));
        Assert.Equal(0xFF, k.Flags);
    }

    [Fact]
    public void AKeeperRunsOnEveryPersonalityAndTheStreamIsDeterministic()
    {
        foreach (KeeperPersonality p in Enum.GetValues<KeeperPersonality>())
        {
            string Run()
            {
                var world = KeeperWorld.World(Db);
                var keeper = new KeeperAI(KeeperWorld.Keeper, KeeperAIPersonality.Parameters(p, "Anywhere", Db, world.Width, world.Height),
                    new SimRandom(11));
                var log = new List<string>();
                for (int t = 0; t < 30; t++)
                {
                    foreach (var c in keeper.Think(Db, world)) log.Add(c.ToString());
                    KeeperWorld.DigWhatIsTagged(world, Db, KeeperWorld.Keeper);
                }
                return string.Join("\n", log);
            }
            Assert.Equal(Run(), Run());
        }
    }
}
