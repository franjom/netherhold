// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.Text;
using DK2.Data.Wad;

namespace DK2.Data.Tests;

public class FrontEndPlumbingTests
{
    [Fact]
    public void EntryIndexReachesBothCopiesOfADuplicateName()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[0x58 + 80]);
                byte[] name = Encoding.ASCII.GetBytes("same.png\0");
                writer.Write(name);
                writer.Write(new byte[] { 11, 22 });
                stream.Position = 0;
                writer.Write(0x42465744u); writer.Write(2u);
                stream.Position = 0x48;
                writer.Write(2u); writer.Write(0xA8u); writer.Write((uint)name.Length);
                for (int i = 0; i < 2; i++)
                {
                    stream.Position = 0x58 + i * 40;
                    writer.Write(0u); writer.Write(0xA8u); writer.Write((uint)name.Length);
                    writer.Write((uint)(0xA8 + name.Length + i)); writer.Write(1u);
                    writer.Write(0u); writer.Write(1u);
                }
            }
            using var wad = WadArchive.Open(path);
            Assert.Equal(new byte[] { 11 }, wad.Extract(0));
            Assert.Equal(new byte[] { 22 }, wad.Extract(1));
            Assert.Equal(new byte[] { 22 }, wad.Extract("same.png"));
            Assert.Throws<ArgumentOutOfRangeException>(() => wad.Extract(-1));
        }
        finally { File.Delete(path); }
    }
}
