// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Data.Save;

namespace DK2.Data.Tests;

public sealed class SavedSessionFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dk2-session-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RoundTripsHeaderSnapshotAndProfile()
    {
        var session = Sample();
        string path = SavedSessionFile.PathForName(_directory, "First save");
        SavedSessionFile.Save(path, session);
        SavedSession back = SavedSessionFile.Load(path);
        Assert.Equal(session.Header, back.Header);
        Assert.Equal(session.Snapshot, back.Snapshot);
        Assert.Equal(CampaignProfileFile.Write(session.Profile), CampaignProfileFile.Write(back.Profile));
    }

    [Fact]
    public void ListingReadsOnlyHeadersAndIgnoresCorruptFiles()
    {
        Assert.Empty(SavedSessionFile.List(_directory));
        string path = SavedSessionFile.PathForName(_directory, "First save");
        SavedSessionFile.Save(path, Sample());
        using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            SavedSessionHeader header = SavedSessionFile.ReadHeader(stream);
            stream.SetLength(stream.Position);
            Assert.Equal(Sample().Header, header);
        }
        File.WriteAllBytes(Path.Combine(_directory, "corrupt.dk2g"), new byte[] { 1, 2, 3 });
        SavedSessionEntry row = Assert.Single(SavedSessionFile.List(_directory));
        Assert.Equal("First save", row.Header.DisplayName);
        Assert.Throws<InvalidDataException>(() => SavedSessionFile.Load(path));
    }

    [Fact]
    public void RejectsUnsupportedVersionAndTruncatedPayload()
    {
        string path = SavedSessionFile.PathForName(_directory, "save");
        SavedSessionFile.Save(path, Sample());
        byte[] data = File.ReadAllBytes(path);
        File.WriteAllBytes(path, data[..^1]);
        Assert.Throws<InvalidDataException>(() => SavedSessionFile.Load(path));
        data[4] = 99;
        File.WriteAllBytes(path, data);
        Assert.Throws<InvalidDataException>(() => SavedSessionFile.Load(path));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("")]
    [InlineData("a/b")]
    public void SaveNameCannotEscapeItsDirectory(string name)
        => Assert.Throws<ArgumentException>(() => SavedSessionFile.PathForName(_directory, name));

    private static SavedSession Sample() => new(new SavedSessionHeader("Level1", 1, 26,
        new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc), "First save"),
        new byte[] { 1, 2, 3 }, new CampaignProfile { LevelNumber = 2 });

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
