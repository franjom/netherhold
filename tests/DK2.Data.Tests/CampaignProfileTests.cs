// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class CampaignProfileTests
{
    [Fact]
    public void ProfileRoundTripKeepsEveryCampaignFieldAndRejectsOtherVersions()
    {
        var profile = new CampaignProfile
        {
            LevelNumber = 30, PetDungeonLevelNumber = 7, TransferCreatureId = 13, TransferCreatureLevel = 9,
            SecretLevelNumber = 5, IsUserPetDungeon = true, LastPetDungeonEvilRating = uint.MaxValue,
            PlayerLevelStatus = Enumerable.Range(0, 40).Select(i => (CampaignLevelStatus)(i % 4)).ToArray(),
            TotalEvilRating = Values(40, 20), LevelAttempts = Values(40, 30),
            SecretLevels = Values(16, 40), SecretLevelsCompleted = Values(16, 50),
            SpecialLevelsCompleted = Values(16, 60), PetDungeonLevelsCompleted = Values(7, 70),
            PlayerStatistics = Values(136, 80), PlayerStatisticsValid = Values(8, 90), PlayerValues = Values(8, 100),
        };
        byte[] bytes = CampaignProfileFile.Write(profile);
        CampaignProfile restored = CampaignProfileFile.Read(bytes);
        Assert.Equal(bytes, CampaignProfileFile.Write(restored));
        Assert.Equal((byte)30, restored.LevelNumber); Assert.Equal((byte)7, restored.PetDungeonLevelNumber);
        Assert.Equal((byte)13, restored.TransferCreatureId); Assert.Equal((byte)9, restored.TransferCreatureLevel);
        Assert.Equal(5u, restored.SecretLevelNumber); Assert.True(restored.IsUserPetDungeon);
        Assert.Equal(uint.MaxValue, restored.LastPetDungeonEvilRating);
        Assert.Equal(139u, restored.PlayerStatistics[59]);
        Assert.Equal(CampaignLevelStatus.ClosedBranch, restored.PlayerLevelStatus[7]);
        Assert.Throws<InvalidDataException>(() => CampaignProfileFile.Read(bytes[..^1]));
        bytes[4] = 99;
        Assert.Throws<InvalidDataException>(() => CampaignProfileFile.Read(bytes));
        Assert.Throws<InvalidDataException>(() => CampaignProfileFile.Write(profile with { LevelAttempts = new uint[39] }));
    }

    [Fact]
    public void ImportUsesNativeArrayWidthsAndDoesNotPersistAnExistingLocalProfileAgain()
    {
        byte[] statuses = new byte[160]; statuses[0] = 1; statuses[20] = 1; statuses[24] = 3;
        byte[] attempts = new byte[160]; BinaryPrimitives.WriteUInt32LittleEndian(attempts.AsSpan(156), 17);
        var values = new Dictionary<string, object>
        {
            ["Version Number"] = 1, ["Level Number"] = 6, ["MPD Level Number"] = 3,
            ["Transfer Creature ID"] = 13, ["Transfer Creature Level"] = 4,
            ["Player Level Status"] = statuses, ["Level Attempts"] = attempts,
            ["Secret Levels"] = new byte[64], ["Total Evil Rating"] = new byte[160],
        };
        CampaignProfile imported = CampaignRegistryImport.Read(values);
        Assert.Equal(CampaignLevelStatus.Won, imported.PlayerLevelStatus[5]);
        Assert.Equal(CampaignLevelStatus.ClosedBranch, imported.PlayerLevelStatus[6]);
        Assert.Equal(17u, imported.LevelAttempts[39]); Assert.Equal((byte)13, imported.TransferCreatureId);
        Assert.Equal(1u, imported.PetDungeonLevelsCompleted[0]);
        Assert.Equal(0u, imported.SecretLevels[4]);
        values["Level Attempts"] = new byte[4];
        Assert.Throws<InvalidDataException>(() => CampaignRegistryImport.Read(values));

        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".profile");
        try
        {
            CampaignProfile first = CampaignProfileFile.LoadOrCreate(path, () => imported);
            CampaignProfile second = CampaignProfileFile.LoadOrCreate(path, () => throw new Exception("second import"));
            Assert.Equal((byte)6, first.LevelNumber); Assert.Equal((byte)6, second.LevelNumber);
            CampaignProfileFile.Save(path, second with { LevelNumber = 8 });
            Assert.Equal((byte)8, CampaignProfileFile.LoadOrCreate(path).LevelNumber);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingOrUnsupportedRegistryVersionGivesDefaultsWithoutInferringProgress()
    {
        CampaignProfile defaults = CampaignRegistryImport.Read(new Dictionary<string, object> { ["Version Number"] = 9 });
        Assert.Equal((byte)1, defaults.LevelNumber);
        Assert.Equal((byte)1, defaults.PetDungeonLevelNumber);
        Assert.All(defaults.PlayerLevelStatus, value => Assert.Equal(CampaignLevelStatus.Locked, value));
        Assert.Equal(new uint[] { 1, 0, 0, 0, 0, 0, 0 }, defaults.PetDungeonLevelsCompleted);
        Assert.All(defaults.TotalEvilRating, value => Assert.Equal(0u, value));
    }

    private static uint[] Values(int count, int first) => Enumerable.Range(first, count).Select(i => (uint)i).ToArray();

    [InstallFact]
    public void InstalledRegistryImportMatchesManagedReadAndLeavesTheKeyUntouched()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(InstalledCampaignRegistry.PlayerKeyPath, writable: false);
        if (key is null) return;
        var before = new Dictionary<string, object>();
        foreach (string name in key.GetValueNames()) before.Add(name, key.GetValue(name)!);
        var importValues = before.Where(pair => pair.Value is byte[] || pair.Value is int)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        // DK2 can leave binary bytes under a registry type that managed GetValue cannot decode.
        var rawValues = new Dictionary<string, byte[]>();
        foreach (string name in before.Keys)
            if (before[name] is null)
            {
                byte[] raw = RawRegistryValue(name);
                rawValues.Add(name, raw); importValues[name] = raw;
            }
        CampaignProfile expected = CampaignRegistryImport.Read(importValues);
        CampaignProfile actual = InstalledCampaignRegistry.ReadCurrentUser()!;
        Assert.Equal(CampaignProfileFile.Write(expected), CampaignProfileFile.Write(actual));
        Assert.Equal(before.Keys.OrderBy(name => name), key.GetValueNames().OrderBy(name => name));
        foreach (string name in before.Keys)
        {
            object? after = key.GetValue(name);
            if (before[name] is byte[] bytes) Assert.Equal(bytes, Assert.IsType<byte[]>(after));
            else Assert.Equal(before[name], after);
        }
        foreach (var pair in rawValues) Assert.Equal(pair.Value, RawRegistryValue(pair.Key));
    }

    private static byte[] RawRegistryValue(string name)
    {
        uint size = 0;
        var root = new IntPtr(unchecked((int)0x80000001));
        int error = RegGetValue(root, InstalledCampaignRegistry.PlayerKeyPath, name, 0xffff, out _, null, ref size);
        if (error != 0) throw new Win32Exception(error);
        byte[] bytes = new byte[size];
        error = RegGetValue(root, InstalledCampaignRegistry.PlayerKeyPath, name, 0xffff, out _, bytes, ref size);
        if (error != 0) throw new Win32Exception(error);
        return bytes;
    }

    [DllImport("advapi32.dll", EntryPoint = "RegGetValueW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegGetValue(IntPtr root, string path, string name, uint flags,
        out uint type, [Out] byte[]? data, ref uint size);
}
