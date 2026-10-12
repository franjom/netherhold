// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Data.Kwd;
using DK2.Data.Model;

namespace DK2.Art;

/// <summary>A named art-resource field from a KWD definition record.</summary>
public sealed record Dk2ArtResource(string Name, byte Type)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    /// <summary>A KMF animation's (type 6) playback rate, the descriptor's word at <c>+0x48</c>;
    /// 0 for any other type.</summary>
    public ushort FramesPerSecond { get; init; }

    /// <summary>A KMF animation's frame count, the descriptor's word at <c>+0x44</c>; 0 for any
    /// other type.</summary>
    public ushort FrameCount { get; init; }
    /// <summary>The type-6 descriptor's word at +0x4C, used to normalize movement rates.</summary>
    public ushort MovementScale { get; init; }
    public CreatureAnimationFlags AnimationFlags { get; init; }
    public byte StartSegmentFrames { get; init; }
    public byte EndSegmentFrames { get; init; }
}

/// <summary>
/// Reads only the art-resource names the converter and the classic look need. Gameplay
/// definitions remain owned by DK2.Data.Model; this view is art only.
/// </summary>
public sealed class Dk2ArtCatalogue
{
    private const int ArtResourceSize = 84;
    private const int ArtTypeOffset = 80;
    private const int ArtFrameCountOffset = 0x44;
    private const int ArtFramesPerSecondOffset = 0x48;
    private const byte AnimationArtType = 6;

    public required IReadOnlyDictionary<byte, IReadOnlyList<Dk2ArtResource>> Terrains { get; init; }
    public required IReadOnlyDictionary<byte, IReadOnlyList<Dk2ArtResource>> Rooms { get; init; }
    public required IReadOnlyDictionary<byte, IReadOnlyList<Dk2ArtResource>> Creatures { get; init; }
    public IReadOnlyDictionary<byte, Dk2ArtResource> CreatureCorpses { get; init; } =
        new Dictionary<byte, Dk2ArtResource>();
    public required IReadOnlyDictionary<byte, IReadOnlyList<Dk2ArtResource>> Objects { get; init; }
    public IReadOnlyDictionary<byte, IReadOnlyList<Dk2ArtResource>> Doors { get; init; } =
        new Dictionary<byte, IReadOnlyList<Dk2ArtResource>>();
    public IReadOnlyDictionary<byte, IReadOnlyList<Dk2ArtResource>> Traps { get; init; } =
        new Dictionary<byte, IReadOnlyList<Dk2ArtResource>>();

    public IReadOnlyDictionary<byte, Dk2ArtResource> DoorLocks { get; init; } =
        new Dictionary<byte, Dk2ArtResource>();

    public static Dk2ArtCatalogue Load(string installDirectory)
    {
        string editor = Path.Combine(installDirectory, "Data", "editor");
        var objects = Read(Path.Combine(editor, "Objects.kwd"), KwdRecordType.Objects,
            894, idOffset: 0x35A, artOffset: 0x20, artCount: 9);
        return new Dk2ArtCatalogue
        {
            Terrains = Read(Path.Combine(editor, "Terrain.kwd"), KwdRecordType.Terrain,
                552, idOffset: 0x1D6, artOffset: 0x20, artCount: 4),
            Rooms = Read(Path.Combine(editor, "Rooms.kwd"), KwdRecordType.Rooms,
                1055, idOffset: 0x392, artOffset: 0x20, artCount: 10),
            // CREATURES begins with one unrelated art resource. These 36 are
            // WALK through NULL_4 in the format's fixed animation-slot order.
            Creatures = Read(Path.Combine(editor, "Creatures.kwd"), KwdRecordType.Creatures,
                5449, idOffset: 0xF4B, artOffset: 0x20 + ArtResourceSize, artCount: 36),
            CreatureCorpses = Read(Path.Combine(editor, "Creatures.kwd"), KwdRecordType.Creatures,
                5449, idOffset: 0xF4B, artOffset: 0x148B, artCount: 1)
                .ToDictionary(pair => pair.Key, pair => pair.Value[0]),
            Objects = objects,
            DoorLocks = ReadDoorLocks(Path.Combine(editor, "Doors.kwd"), objects),
            Doors = Read(Path.Combine(editor, "Doors.kwd"), KwdRecordType.Doors,
                616, idOffset: 0x244, artOffset: 0x20, artCount: 6),
            Traps = Read(Path.Combine(editor, "Traps.kwd"), KwdRecordType.Traps,
                579, idOffset: 0x20C, artOffset: 0x20, artCount: 5),
        };
    }

    private static IReadOnlyDictionary<byte, Dk2ArtResource> ReadDoorLocks(string path,
        IReadOnlyDictionary<byte, IReadOnlyList<Dk2ArtResource>> objects)
    {
        var result = new Dictionary<byte, Dk2ArtResource>();
        foreach (KwdChunk chunk in KwdReader.ReadFile(path).Where(chunk => chunk.Type == KwdRecordType.Doors))
        {
            if (chunk.ItemCount == 0) continue;
            if (chunk.Content.Length != chunk.ItemCount * 616)
                throw new InvalidDataException("Unexpected door record size.");
            for (int at = 0; at < chunk.Content.Length; at += 616)
            {
                byte objectId = chunk.Content[at + 0x247];
                if (objectId != 0 && objects.TryGetValue(objectId, out var resources))
                    result.Add(chunk.Content[at + 0x244], resources[0]);
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<byte, IReadOnlyList<Dk2ArtResource>> Read(
        string path, KwdRecordType type, int expectedStride, int idOffset,
        int artOffset, int artCount)
    {
        var result = new Dictionary<byte, IReadOnlyList<Dk2ArtResource>>();
        foreach (KwdChunk chunk in KwdReader.ReadFile(path).Where(chunk => chunk.Type == type))
        {
            if (chunk.ItemCount == 0) continue;
            int stride = chunk.Content.Length / chunk.ItemCount;
            if (stride != expectedStride || stride * chunk.ItemCount != chunk.Content.Length)
                throw new InvalidDataException(
                    $"{type} record size is {stride}, expected {expectedStride}.");

            for (int i = 0; i < chunk.ItemCount; i++)
            {
                int record = i * stride;
                var resources = new Dk2ArtResource[artCount];
                for (int art = 0; art < artCount; art++)
                {
                    int offset = record + artOffset + art * ArtResourceSize;
                    byte artType = chunk.Content[offset + ArtTypeOffset];
                    resources[art] = new Dk2ArtResource(ReadAscii(chunk.Content.AsSpan(offset, 64)), artType)
                    {
                        FramesPerSecond = artType == AnimationArtType
                            ? BitConverter.ToUInt16(chunk.Content, offset + ArtFramesPerSecondOffset)
                            : (ushort)0,
                        FrameCount = artType == AnimationArtType
                            ? BitConverter.ToUInt16(chunk.Content, offset + ArtFrameCountOffset)
                            : (ushort)0,
                        AnimationFlags = artType == AnimationArtType
                            ? (CreatureAnimationFlags)BitConverter.ToUInt32(chunk.Content, offset + 0x40)
                            : CreatureAnimationFlags.None,
                        StartSegmentFrames = artType == AnimationArtType ? chunk.Content[offset + 0x51] : (byte)0,
                        EndSegmentFrames = artType == AnimationArtType ? chunk.Content[offset + 0x52] : (byte)0,
                        MovementScale = artType == AnimationArtType
                            ? BitConverter.ToUInt16(chunk.Content, offset + 0x4C) : (ushort)0,
                    };
                }
                byte id = chunk.Content[record + idOffset];
                if (!result.TryAdd(id, resources))
                    throw new InvalidDataException($"Duplicate {type} id {id} in '{path}'.");
            }
        }
        return result;
    }

    private static string ReadAscii(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? bytes : bytes[..end]);
    }
}
