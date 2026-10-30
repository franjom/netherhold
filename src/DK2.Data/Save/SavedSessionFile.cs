// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Data.Campaign;

namespace DK2.Data.Save;

/// <summary>Our versioned game file: header, exact simulation checkpoint, campaign profile.</summary>
public static class SavedSessionFile
{
    private const uint Magic = 0x47324B44; // DK2G
    private const uint Version = 1;
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
            int snapshotLength = reader.ReadInt32(), profileLength = reader.ReadInt32();
            if (snapshotLength <= 0 || profileLength <= 0
                || (long)snapshotLength + profileLength != stream.Length - stream.Position)
                throw new InvalidDataException("Invalid saved-session payload lengths.");
            byte[] snapshot = reader.ReadBytes(snapshotLength);
            byte[] profile = reader.ReadBytes(profileLength);
            return new SavedSession(header, snapshot, CampaignProfileFile.Read(profile));
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("Truncated saved session.", ex); }
    }

    public static SavedSessionHeader ReadHeader(Stream stream)
    {
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic || reader.ReadUInt32() != Version)
                throw new InvalidDataException("Unsupported saved-session signature or version.");
            string level = ReadText(reader);
            byte mode = reader.ReadByte(); ushort page = reader.ReadUInt16();
            var time = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            var header = new SavedSessionHeader(level, mode, page, time, ReadText(reader));
            Validate(header);
            return header;
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("Truncated saved-session header.", ex); }
        catch (ArgumentException ex) { throw new InvalidDataException("Invalid saved-session header.", ex); }
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
        int length = reader.ReadInt32();
        if (length < 0 || length > 512) throw new InvalidDataException("Invalid saved-session text length.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return new UTF8Encoding(false, true).GetString(bytes);
    }
}
