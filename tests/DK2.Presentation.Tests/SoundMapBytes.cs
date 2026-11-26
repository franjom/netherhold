// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Data.Media;

namespace DK2.Presentation.Tests;

/// <summary>Small SFX and bank maps written in the shipped layout (<see cref="SfxMap.Read"/>, <see cref="BankMap.Read"/>), so the
/// player's rules are tested without an install.</summary>
internal static class SoundMapBytes
{
    /// <summary>A group: its id, priority (group word <c>+4</c>), flag word (<c>+8</c>: <c>0x08</c> loops, <c>0x200</c> is head-relative)
    /// and items.</summary>
    public sealed record Group(uint Id, uint Priority, ushort Flags, params Item[] Items);

    /// <summary>An item: its distance maximum in mixer units (float <c>+0x22</c>), its samples (one-based entry, one-based archive,
    /// cumulative weight), its links (one-based target, inclusive intensity range) and its volume percent (byte <c>+0x0C</c>).</summary>
    public sealed record Item(float Maximum, (uint Entry, uint Archive, uint Weight)[] Samples,
        (uint Target, byte Minimum, byte Maximum)[]? Links = null, byte Volume = 100);

    public static SoundMaps Maps(string archive, params Group[] groups) => new(Bank(archive), Sfx(groups));

    public static SfxMap Sfx(params Group[] groups)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            Header(writer, 0xE9612C00, 0xB00009B4);
            writer.Write(1u);
            writer.Write((uint)groups.Length);
            writer.Write(new byte[20]);
            foreach (Group group in groups)
            {
                writer.Write(group.Id);
                writer.Write((uint)group.Items.Length);
                writer.Write(0u);
                writer.Write(group.Priority);
                writer.Write(group.Flags);
                writer.Write((ushort)0);
            }
            foreach (Group group in groups)
            {
                foreach (Item item in group.Items)
                {
                    writer.Write((uint)item.Samples.Length);
                    writer.Write((uint)(item.Links?.Length ?? 0));
                    byte[] unknown = new byte[34];
                    unknown[4] = item.Volume;
                    BitConverter.GetBytes(item.Maximum).CopyTo(unknown, 0x22 - 8);
                    writer.Write(unknown);
                }
                foreach (Item item in group.Items)
                {
                    foreach ((uint entry, uint archive, uint weight) in item.Samples)
                    {
                        writer.Write(entry);
                        writer.Write(weight);
                        writer.Write(0u);
                        writer.Write(archive);
                    }
                    foreach ((uint target, byte minimum, byte maximum) in item.Links ?? [])
                    {
                        writer.Write(target);
                        writer.Write((ushort)0);
                        writer.Write(minimum);
                        writer.Write(maximum);
                    }
                }
            }
        }
        return SfxMap.Read(stream.ToArray());
    }

    public static BankMap Bank(params string[] archives)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            Header(writer, 0xE9612C01, 0xA00009B4);
            writer.Write((uint)archives.Length);
            foreach (string _ in archives) writer.Write(new byte[11]);
            foreach (string archive in archives)
            {
                writer.Write((uint)archive.Length);
                writer.Write(Encoding.ASCII.GetBytes(archive));
            }
        }
        return BankMap.Read(stream.ToArray());
    }

    private static void Header(BinaryWriter writer, uint first, uint third)
    {
        writer.Write(first);
        writer.Write(0x11D231D0u);
        writer.Write(third);
        writer.Write(0x03F293C9u);
        writer.Write(new byte[8]);
    }
}
