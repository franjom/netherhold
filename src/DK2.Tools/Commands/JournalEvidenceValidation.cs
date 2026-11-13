// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Simulation;

namespace DK2.Tools.Commands;

/// <summary>Checks v1 record uniqueness before a diagnostic trusts the permissive journal reader's result.</summary>
internal static class JournalEvidenceValidation
{
    internal static void Check(byte[] bytes, InputJournal journal)
    {
        // InputJournal.Read has already checked the format/header and decoded every whole command.
        // Its v1 recovery behavior accepts a cut final record, but also hides duplicates and trailing bytes.
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        reader.ReadUInt32();
        reader.ReadInt32();
        reader.ReadUInt64();
        int headerCount = reader.ReadInt32();
        if (headerCount < 0) throw new InvalidDataException("Negative journal header count.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < headerCount; i++)
        {
            string key = reader.ReadString();
            reader.ReadString();
            if (!keys.Add(key)) throw new InvalidDataException("Duplicate journal header key.");
        }
        var frames = new HashSet<long>();
        var hashes = new HashSet<long>();
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            try
            {
                byte kind = reader.ReadByte();
                long tick = reader.ReadInt64();
                switch (kind)
                {
                    case 1:
                        int count = reader.ReadInt32();
                        if (count < 0) throw new InvalidDataException("Negative journal command count.");
                        for (int i = 0; i < count; i++) CommandCodec.Read(reader);
                        if (!frames.Add(tick)) throw new InvalidDataException($"Duplicate journal frame at tick {tick}.");
                        break;
                    case 2:
                        reader.ReadUInt64();
                        if (!hashes.Add(tick)) throw new InvalidDataException($"Duplicate journal hash at tick {tick}.");
                        break;
                    case 3:
                        reader.ReadString();
                        if (reader.BaseStream.Position != reader.BaseStream.Length)
                            throw new InvalidDataException("Journal contains trailing bytes after its end record.");
                        return;
                    default:
                        throw new InvalidDataException($"Unknown journal record kind {kind}.");
                }
            }
            catch (EndOfStreamException) when (journal.Truncated)
            {
                return;
            }
        }
    }
}
