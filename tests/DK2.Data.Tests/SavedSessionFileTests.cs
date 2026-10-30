// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
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
    public void EveryPayloadByteIsProtectedBeforeParsing()
    {
        string path = SavedSessionFile.PathForName(_directory, "save");
        SavedSessionFile.Save(path, Sample());
        byte[] data = File.ReadAllBytes(path);
        Assert.Equal(2u, BitConverter.ToUInt32(data, 4));
        using var stream = new MemoryStream(data);
        SavedSessionFile.ReadHeader(stream);
        int payload = (int)stream.Position + 8 + 16;
        for (int offset = payload; offset < data.Length; offset++)
        {
            data[offset] ^= 1;
            File.WriteAllBytes(path, data);
            var error = Assert.Throws<SaveIncompatibleException>(() => SavedSessionFile.Load(path));
            Assert.Equal(SaveIncompatibleReason.ChecksumMismatch, error.Reason);
            data[offset] ^= 1;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(40)]
    public void TruncatedHeaderHasAReason(int length)
    {
        using var stream = new MemoryStream(SavedBytes()[..length]);
        var error = Assert.Throws<SaveIncompatibleException>(() => SavedSessionFile.ReadHeader(stream));
        Assert.Equal(SaveIncompatibleReason.Truncated, error.Reason);
    }

    [Fact]
    public void VersionOneIsRefusedClearly()
    {
        byte[] data = SavedBytes();
        data[4] = 1;
        using var stream = new MemoryStream(data);
        var error = Assert.Throws<SaveIncompatibleException>(() => SavedSessionFile.ReadHeader(stream));
        Assert.Equal(SaveIncompatibleReason.UnsupportedVersion, error.Reason);
        Assert.Contains("version 1", error.Message);
    }

    [Fact]
    public void InvalidHeaderUtf8HasAReasonAndInnerException()
    {
        byte[] data = SavedBytes();
        data[12] = 0xFF;
        using var stream = new MemoryStream(data);
        var error = Assert.Throws<SaveIncompatibleException>(() => SavedSessionFile.ReadHeader(stream));
        Assert.Equal(SaveIncompatibleReason.Corrupt, error.Reason);
        Assert.IsType<System.Text.DecoderFallbackException>(error.InnerException);
    }

    [Fact]
    public void InvalidProfileWithMatchingChecksumHasAReason()
    {
        string path = SavedSessionFile.PathForName(_directory, "save");
        byte[] data = SavedBytes();
        using var stream = new MemoryStream(data);
        SavedSessionFile.ReadHeader(stream);
        int lengths = (int)stream.Position;
        int profile = lengths + 24 + BitConverter.ToInt32(data, lengths);
        data[profile] ^= 1;
        BitConverter.GetBytes(PayloadChecksum.Compute(data.AsSpan(profile))).CopyTo(data, lengths + 16);
        File.WriteAllBytes(path, data);
        var error = Assert.Throws<SaveIncompatibleException>(() => SavedSessionFile.Load(path));
        Assert.Equal(SaveIncompatibleReason.Corrupt, error.Reason);
        Assert.IsType<InvalidDataException>(error.InnerException);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(-1)]
    public void NonSeekableHeaderHasOneSaveException(int length)
    {
        byte[] bytes = length < 0 ? SavedBytes() : new byte[length];
        using var stream = new NonSeekableStream(bytes);
        var error = Assert.Throws<SaveIncompatibleException>(() => SavedSessionFile.ReadHeader(stream));
        Assert.Equal(SaveIncompatibleReason.Corrupt, error.Reason);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }

    private byte[] SavedBytes()
    {
        string path = SavedSessionFile.PathForName(_directory, "save");
        SavedSessionFile.Save(path, Sample());
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void ListingReadsOnlyHeadersAndListsUnreadableFilesWithTheirReason()
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

        IReadOnlyList<SavedSessionEntry> rows = SavedSessionFile.List(_directory);

        Assert.Equal(["corrupt", "First save"], rows.Select(r => r.DisplayName));
        Assert.False(rows[0].Readable);
        Assert.Null(rows[0].Header);
        Assert.Equal(SaveIncompatibleReason.Truncated, rows[0].Refusal!.Reason);
        Assert.True(rows[1].Readable);
        Assert.Null(rows[1].Refusal);
        Assert.Throws<SaveIncompatibleException>(() => SavedSessionFile.Load(path));
    }

    [Fact]
    public void ListingShowsAVersionOneSaveAsRefusedRatherThanHidingIt()
    {
        byte[] data = SavedBytes();
        data[4] = 1;
        File.WriteAllBytes(SavedSessionFile.PathForName(_directory, "save"), data);

        SavedSessionEntry row = Assert.Single(SavedSessionFile.List(_directory));

        Assert.Equal("save", row.DisplayName);
        Assert.Equal(SaveIncompatibleReason.UnsupportedVersion, row.Refusal!.Reason);
        Assert.Contains("version 1", row.Refusal.Message);
    }

    [Fact]
    public void RejectsUnsupportedVersionAndTruncatedPayload()
    {
        string path = SavedSessionFile.PathForName(_directory, "save");
        SavedSessionFile.Save(path, Sample());
        byte[] data = File.ReadAllBytes(path);
        File.WriteAllBytes(path, data[..^1]);
        Assert.Equal(SaveIncompatibleReason.Truncated,
            Assert.Throws<SaveIncompatibleException>(() => SavedSessionFile.Load(path)).Reason);
        data[4] = 99;
        File.WriteAllBytes(path, data);
        Assert.Throws<SaveIncompatibleException>(() => SavedSessionFile.Load(path));
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
