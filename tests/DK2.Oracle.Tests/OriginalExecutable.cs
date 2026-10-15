// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using DK2.TestSupport;

namespace DK2.Oracle.Tests;

/// <summary>
/// The reference install's <c>DKII.EXE</c>, read in place at test time. Nothing read here is
/// stored in the repository (ADR-0046): a test compares the bytes against our C# and keeps only
/// the verdict.
///
/// <para>Every address in docs/research/ is for v1.70, MD5 <see cref="ExpectedMd5"/>; another
/// build would put different code at them, so a mismatch fails instead of comparing garbage.</para>
/// </summary>
internal sealed class OriginalExecutable
{
    public const string ExpectedMd5 = "2f3bbc21eae8c3aa468136baa303036d";

    private static readonly Lazy<OriginalExecutable> Shared = new(() => new OriginalExecutable(
        System.IO.Path.Combine(Dk2InstallGate.Path, "DKII.EXE")));

    private readonly byte[] _file;
    private readonly SectionHeader[] _sections;

    public static OriginalExecutable Load() => Shared.Value;

    public uint ImageBase { get; }

    private OriginalExecutable(string path)
    {
        _file = File.ReadAllBytes(path);
        string md5 = Convert.ToHexStringLower(MD5.HashData(_file));
        Assert.True(md5 == ExpectedMd5,
            $"{path} has MD5 {md5}; the addresses read in docs/research/ are for v1.70 ({ExpectedMd5}).");

        using var reader = new PEReader(new MemoryStream(_file));
        PEHeaders headers = reader.PEHeaders;
        ImageBase = (uint)headers.PEHeader!.ImageBase;
        _sections = [.. headers.SectionHeaders];
    }

    /// <summary>The bytes the loader puts at <paramref name="address"/>, read from the file.
    /// Fails when the span is not file-backed (uninitialised data is built at run time, so it
    /// cannot be compared this way).</summary>
    public ReadOnlySpan<byte> At(uint address, int length)
    {
        uint rva = address - ImageBase;
        foreach (SectionHeader section in _sections)
        {
            uint start = (uint)section.VirtualAddress;
            if (rva < start || rva >= start + (uint)Math.Max(section.VirtualSize, section.SizeOfRawData)) continue;
            uint offset = rva - start;
            Assert.True(offset + length <= section.SizeOfRawData,
                $"0x{address:X8}+{length} runs past {section.Name}'s file-backed bytes: it is filled at run time.");
            return _file.AsSpan(section.PointerToRawData + (int)offset, length);
        }
        Assert.Fail($"0x{address:X8} is in no section of DKII.EXE.");
        return default;
    }

    public ushort UInt16(uint address) => BitConverter.ToUInt16(At(address, 2));

    public int Int32(uint address) => BitConverter.ToInt32(At(address, 4));
}
