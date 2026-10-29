// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using DK2.Data.Media;
using DK2.TestSupport;

namespace DK2.Data.Tests;

[Collection("MPEG I/O")]
public class MpegAudioDecoderTests
{
    [InstallFact]
    public void FrontEndLayerOneDecodesAll68Frames()
    {
        PcmClip clip = MpegAudioDecoder.Decode(Payload("FrontEndHD.sdt"));
        Assert.Equal(22050, clip.SampleRate);
        Assert.Equal(1, clip.Channels);
        Assert.Equal(68 * 384, clip.Samples.Length);
        Assert.Contains(clip.Samples, sample => sample != 0);
    }

    [InstallFact]
    public void MentorLayerTwoDecodesAll51Frames()
    {
        PcmClip clip = MpegAudioDecoder.Decode(Payload("speech_mentorHD.sdt"));
        Assert.Equal(22050, clip.SampleRate);
        Assert.Equal(1, clip.Channels);
        Assert.Equal(51 * 1152, clip.Samples.Length);
        Assert.Contains(clip.Samples, sample => sample != 0);
    }

    [InstallFact]
    public void DecodeCreatesNoFilesInInstallOrTemporaryDirectory()
    {
        byte[] payload = Payload("FrontEndHD.sdt");
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DK2.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string temp = Path.Combine(root.FullName, "build", "mpeg-io", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        string? oldTemp = Environment.GetEnvironmentVariable("TEMP"), oldTmp = Environment.GetEnvironmentVariable("TMP");
        try
        {
            Environment.SetEnvironmentVariable("TEMP", temp); Environment.SetEnvironmentVariable("TMP", temp);
            Assert.Equal(Path.GetFullPath(temp).TrimEnd(Path.DirectorySeparatorChar), Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
            using var install = new FileSystemWatcher(Dk2InstallGate.Path) { IncludeSubdirectories = true };
            using var temporary = new FileSystemWatcher(temp) { IncludeSubdirectories = true };
            var written = new System.Collections.Concurrent.ConcurrentBag<string>();
            FileSystemEventHandler changed = (_, e) => written.Add(e.FullPath);
            install.Created += changed; install.Changed += changed;
            temporary.Created += changed; temporary.Changed += changed;
            install.EnableRaisingEvents = temporary.EnableRaisingEvents = true;
            MpegAudioDecoder.Decode(payload);
            SpinWait.SpinUntil(() => !written.IsEmpty, TimeSpan.FromMilliseconds(50));
            Assert.Empty(written);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEMP", oldTemp); Environment.SetEnvironmentVariable("TMP", oldTmp);
        }
    }

    [Fact]
    public void InvalidAudioIsRejected() =>
        Assert.Throws<InvalidDataException>(() => MpegAudioDecoder.Decode(new byte[4]));

    private static byte[] Payload(string name)
    {
        string path = Directory.EnumerateFiles(Path.Combine(Dk2InstallGate.Path, "Data", "Sound", "SFX"),
            "*", SearchOption.AllDirectories).Single(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase));
        byte[] bank = File.ReadAllBytes(path);
        int offset = BinaryPrimitives.ReadInt32LittleEndian(bank.AsSpan(4));
        int header = BinaryPrimitives.ReadInt32LittleEndian(bank.AsSpan(offset));
        int size = BinaryPrimitives.ReadInt32LittleEndian(bank.AsSpan(offset + 4));
        return bank.AsSpan(offset + header, size).ToArray();
    }
}

[CollectionDefinition("MPEG I/O", DisableParallelization = true)]
public sealed class MpegIoCollection;
