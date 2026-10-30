// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Core;
using DK2.Data.Campaign;

namespace DK2.Data.Save;

/// <summary>Our versioned game file: header, exact simulation checkpoint, campaign profile.</summary>
public static class SavedSessionFile
{
    private const uint Magic = 0x47324B44; // DK2G
    private const uint Version = 2;
    public const string Extension = ".dk2g";

    public static string PathForName(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name is "." or ".."
            || name.Any(c => c < ' ' || "<>:\"/\\|?*".Contains(c)) || name.EndsWith(".", StringComparison.Ordinal))
            throw new ArgumentException("A save needs a file name without a path or reserved characters.", nameof(name));
        return Path.Combine(directory, name.Trim() + Extension);
    }

    public static void Save(string path, SavedSession session)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        Validate(session.Header);
        if (session.Snapshot is null || session.Snapshot.Length == 0)
            throw new ArgumentException("A save needs a simulation snapshot.", nameof(session));
        byte[] profile = CampaignProfileFile.Write(session.Profile);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic); writer.Write(Version);
            WriteText(writer, session.Header.LevelName);
            writer.Write(session.Header.GameMode); writer.Write(session.Header.ReturnPageId);
            writer.Write(session.Header.SavedAtUtc.Ticks);
            WriteText(writer, session.Header.DisplayName);
            writer.Write(session.Snapshot.Length); writer.Write(profile.Length);
            writer.Write(PayloadChecksum.Compute(session.Snapshot)); writer.Write(PayloadChecksum.Compute(profile));
            writer.Write(session.Snapshot); writer.Write(profile);
        }
        ProfileFileStorage.Save(path, stream.ToArray());
    }

    public static SavedSession Load(string path)
    {
        using var stream = File.OpenRead(path);
        try
        {
            SavedSessionHeader header = ReadHeader(stream);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            int snapshotLength = SaveReader.Count(reader, "snapshot bytes", 1);
            int profileLength = SaveReader.Count(reader, "profile bytes", 1);
            ulong snapshotChecksum = reader.ReadUInt64(), profileChecksum = reader.ReadUInt64();
            long payloadLength = (long)snapshotLength + profileLength;
            if (snapshotLength == 0 || profileLength == 0)
                throw SaveReader.Error(reader, SaveIncompatibleReason.OutOfRange, "A saved-session payload is empty");
            if (payloadLength > SaveReader.Remaining(reader))
                throw SaveReader.Error(reader, SaveIncompatibleReason.Truncated, "Truncated saved-session payload");
            if (payloadLength != SaveReader.Remaining(reader))
                throw SaveReader.Error(reader, SaveIncompatibleReason.Corrupt, "Invalid saved-session payload lengths");
            byte[] snapshot = SaveReader.Bytes(reader, snapshotLength, "snapshot");
            byte[] profile = SaveReader.Bytes(reader, profileLength, "profile");
            if (PayloadChecksum.Compute(snapshot) != snapshotChecksum || PayloadChecksum.Compute(profile) != profileChecksum)
                throw SaveReader.Error(reader, SaveIncompatibleReason.ChecksumMismatch, "The saved-session payload checksum does not match");
            return new SavedSession(header, snapshot, CampaignProfileFile.Read(profile));
        }
        catch (Exception ex) when (SaveReader.IsMalformed(ex)) { throw SaveReader.Wrap(ex, stream.Position); }
    }

    public static SavedSessionHeader ReadHeader(Stream stream)
    {
        if (!stream.CanSeek)
            throw new SaveIncompatibleException(SaveIncompatibleReason.Corrupt, "A saved session requires a seekable stream.");
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic)
                throw SaveReader.Error(reader, SaveIncompatibleReason.NotASave, "Not a DK2 saved session");
            uint version = reader.ReadUInt32();
            if (version != Version)
                throw SaveReader.Error(reader, SaveIncompatibleReason.UnsupportedVersion,
                    $"Saved-session version {version} is not supported (this build reads {Version})");
            string level = ReadText(reader);
            byte mode = reader.ReadByte(); ushort page = reader.ReadUInt16();
            var time = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            var header = new SavedSessionHeader(level, mode, page, time, ReadText(reader));
            Validate(header);
            return header;
        }
        catch (Exception ex) when (SaveReader.IsMalformed(ex)) { throw SaveReader.Wrap(ex, stream.Position); }
    }

    public static IReadOnlyList<SavedSessionEntry> List(string directory)
    {
        if (!Directory.Exists(directory)) return Array.Empty<SavedSessionEntry>();
        var entries = new List<SavedSessionEntry>();
        foreach (string path in Directory.EnumerateFiles(directory, "*" + Extension))
        {
            try
            {
                using var stream = File.OpenRead(path);
                entries.Add(new SavedSessionEntry(path, ReadHeader(stream)));
            }
            catch (InvalidDataException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return entries.OrderBy(e => e.Header.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void Validate(SavedSessionHeader header)
    {
        if (header is null || string.IsNullOrEmpty(header.LevelName) || header.LevelName.Length > 128
            || !header.LevelName.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')
            || header.GameMode != 1 || header.SavedAtUtc.Kind != DateTimeKind.Utc
            || string.IsNullOrWhiteSpace(header.DisplayName) || header.DisplayName.Length > 128)
            throw new ArgumentException("Invalid saved-session header.", nameof(header));
    }

    private static void WriteText(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length); writer.Write(bytes);
    }
    private static string ReadText(BinaryReader reader)
    {
        int length = SaveReader.Count(reader, "saved-session text bytes", 1, 512);
        byte[] bytes = SaveReader.Bytes(reader, length, "saved-session text");
        return new UTF8Encoding(false, true).GetString(bytes);
    }
}
