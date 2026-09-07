// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

/// <summary>
/// The PLAYERS(180) reader against the shipped install. The counts here were
/// measured independently by a raw Python sweep of the same 66 files before this
/// parser existed (docs/KEEPER_AI.md §3.1); the parser has to reproduce them.
/// </summary>
public class KwdPlayersTests
{
    private static string Maps()
    {
        string maps = Path.Combine(Dk2InstallGate.Path, "Data", "editor", "maps");
        Assert.True(Directory.Exists(maps), $"the install has no {maps}");
        return maps;
    }

    [InstallFact]
    public void EveryShippedPlayersFileDividesInto205ByteRecords()
    {
        string maps = Maps();

        string[] files = Directory.GetFiles(maps, "*Players.kld");
        Assert.Equal(66, files.Length);

        int players = 0, aiFlagged = 0, keepers = 0;
        foreach (string file in files)
        {
            List<PlayerDef> defs = KwdPlayers.Parse(KwdReader.ReadFile(file));
            Assert.InRange(defs.Count, 3, 7);
            Assert.Contains(defs, p => p.PlayerId == PlayerIds.Good);
            Assert.Contains(defs, p => p.PlayerId == PlayerIds.Neutral);
            Assert.All(defs, p =>
            {
                Assert.False(string.IsNullOrWhiteSpace(p.Name));
                Assert.Equal(KwdPlayers.BlockSize, p.Ai.RawBlock.Length);
                Assert.Equal(15, p.Ai.RoomList.Count);
                Assert.True(p.Ai.MaxBuildAreaMinX <= p.Ai.MaxBuildAreaMaxX);
                Assert.True(p.Ai.MaxBuildAreaMinY <= p.Ai.MaxBuildAreaMaxY);
            });
            players += defs.Count;
            aiFlagged += defs.Count(p => p.HasAiFlag);
            keepers += defs.Count(p => p.IsComputerKeeper);
        }

        Assert.Equal(274, players);
        Assert.Equal(121, aiFlagged);
        Assert.Equal(23, keepers);
    }

    [InstallFact]
    public void AiRowsCarryTheConstantsTheSweepFound()
    {
        string maps = Maps();

        var ai = Directory.GetFiles(maps, "*Players.kld")
            .SelectMany(f => KwdPlayers.Parse(KwdReader.ReadFile(f)))
            .Where(p => p.HasAiFlag)
            .ToList();

        Assert.All(ai, p =>
        {
            Assert.Equal(100, p.Ai.ManaValue);
            Assert.Equal(100, p.Ai.GoldValue);
            Assert.Equal(100, p.Ai.StartingMana);
            Assert.Contains(p.Ai.TendancySpeed, new byte[] { 1, 2, 4 });
            Assert.Contains(p.Ai.Openness, new byte[] { 15, 20, 50, 100 });
        });
        Assert.Equal(
            new byte[] { 0, 2, 4, 5, 6, 8 },
            ai.Select(p => p.Ai.EditorAiType).Distinct().OrderBy(v => v));
    }

    [InstallFact]
    public void GormagonAndAsmodeusReadAsTheBinaryLoadsThem()
    {
        string install = Dk2InstallGate.Path;

        PlayerDef gormagon = Assert.Single(
            GameDatabaseLoader.LoadCampaignPlayers(install, "level7")!,
            p => p.Name == "Gormagon");
        Assert.Equal(4, gormagon.PlayerId);
        Assert.True(gormagon.IsComputerKeeper);
        Assert.Equal(2000, gormagon.StartingGold);
        Assert.Equal(1, gormagon.Ai.TendancySpeed);
        Assert.Equal(9, gormagon.Ai.DefaultRoomSize);
        Assert.Equal(4, gormagon.Ai.MaximumImps);
        Assert.Equal(40, gormagon.Ai.TilesPerImpRatio);
        Assert.Equal(3, gormagon.Ai.IAMinimumCreatures);
        Assert.Equal(1000, gormagon.Ai.TendancyMoney);
        Assert.Equal(
            new byte[] { 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
            gormagon.Ai.RoomList);

        PlayerDef asmodeus = Assert.Single(
            GameDatabaseLoader.LoadCampaignPlayers(install, "Level10")!,
            p => p.Name == "Asmodeus");
        Assert.Equal(10000, asmodeus.StartingGold);
        Assert.Equal(30, asmodeus.Ai.IAMinimumCreatures);
        Assert.Equal(10, asmodeus.Ai.IACreatureSkillLevel);
        Assert.Equal(100, asmodeus.Ai.IAThreatSuperiority);
        Assert.Equal(6, asmodeus.Ai.MaximumImps);
        Assert.Equal(30, asmodeus.Ai.TilesPerImpRatio);
        Assert.Equal(1, asmodeus.Ai.TendancyMoney);
        Assert.False(asmodeus.Ai.IAOnlyAttackAttackers);
        Assert.False(asmodeus.Ai.NeverAttack);
    }

    [InstallFact]
    public void Level17KeepersCarryTheGarbageHighByteAsTheGameSeesIt()
    {
        string install = Dk2InstallGate.Path;

        var keepers = GameDatabaseLoader.LoadCampaignPlayers(install, "Level17")!
            .Where(p => p.IsComputerKeeper)
            .ToList();

        Assert.Equal(2, keepers.Count);
        // The editor wrote a u16; the game reads its low byte, which is 232 here.
        Assert.All(keepers, k => Assert.Equal(232, k.Ai.TilesPerImpRatio));
    }

    [Fact]
    public void RejectsAChunkThatDoesNotDivideIntoRecords()
    {
        var chunk = new KwdChunk
        {
            Type = KwdRecordType.Players,
            ItemCount = 2,
            Content = new byte[KwdPlayers.RecordSize * 2 - 1],
            HeaderBlock = [],
        };
        Assert.Throws<InvalidDataException>(() => KwdPlayers.Parse([chunk]));
    }
}
