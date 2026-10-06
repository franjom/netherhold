// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Assets;
using DK2.Convert;
using DK2.Convert.Audio;
using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Data.Wad;
using static DK2.Tools.Commands.CommandHelpers;

namespace DK2.Tools.Commands;

internal static class AssetCommands
{
    internal static int RunWhere(string[] args)
    {
        string? dir = Dk2Install.Find();
        if (dir is null)
        {
            Console.Error.WriteLine("DK2 installation not found (set DK2_DIR).");
            return 2;
        }
        Console.WriteLine(dir);
        return 0;
    }

    internal static int RunConvert(string[] args)
    {
        int outOption = Array.IndexOf(args, "--out");
        if (outOption < 0 || outOption + 1 >= args.Length || outOption + 2 != args.Length)
        {
            Console.Error.WriteLine("usage: dk2tools convert --out converted-assets/");
            return 2;
        }

        string output = RequireConvertedOutput(args[outOption + 1]);
        string install = Dk2Install.Require();
        GameDatabase database = GameDatabaseLoader.Load(install);
        IReadOnlyList<AssetConversionResult> results =
            AssetConverter.ConvertAll(install, database, output);
        FileAssetProvider provider = FileAssetProvider.Converted(output);
        foreach (AssetConversionResult result in results)
        {
            if (result.Produced && !provider.TryResolve(result.Reference, out _))
                throw new InvalidOperationException(
                    $"Converter reported '{result.Reference.Key}' but FileAssetProvider cannot resolve it.");
            string status = result.Produced ? "PRODUCED" : "MISSING ";
            string destination = result.OutputPath is null
                ? result.Detail
                : $"{Path.GetRelativePath(output, result.OutputPath)} ({result.Detail})";
            Console.WriteLine($"{status} {result.Reference.Key} -> {destination}");
        }
        int produced = results.Count(result => result.Produced);
        Console.WriteLine($"-- produced {produced}, could not produce {results.Count - produced}, " +
                          $"catalogue total {results.Count}");
        return 0;
    }

    internal static int RunWadList(string[] args)
    {
        using var wad = WadArchive.Open(ResolveData(args[2]));
        foreach (WadEntry e in wad.Entries)
            Console.WriteLine($"{e.UncompressedSize,10} {(e.IsCompressed ? "C" : " ")} {e.Name}");
        Console.WriteLine($"-- {wad.Entries.Count} entries");
        return 0;
    }

    internal static int RunWadExtract(string[] args)
    {
        using var wad = WadArchive.Open(ResolveData(args[2]));
        string entryName = args[3];
        string outPath = args.Length > 4 ? args[4] : Path.GetFileName(entryName.Replace('\\', '/'));
        byte[] data = wad.Extract(entryName);
        File.WriteAllBytes(outPath, data);
        Console.WriteLine($"{entryName} -> {outPath} ({data.Length} bytes)");
        return 0;
    }

    internal static int RunSdtList(string[] args)
    {
        SdtArchive sdt = SdtArchive.Open(ResolveData(args[2]));
        foreach (SdtEntry entry in sdt.Entries)
        {
            string channels = entry.ChannelCount is int count ? $"{count}ch" : "?ch";
            string rate = entry.PayloadSampleRate is ushort payloadRate
                && payloadRate != entry.SampleRate
                ? $"{entry.SampleRate}Hz(stream {payloadRate}Hz)"
                : $"{entry.SampleRate}Hz";
            Console.WriteLine($"{entry.Index,4} {entry.StoredSize,10} {rate,-23} " +
                              $"{entry.BitsPerSample,2}bit {channels,-3} {entry.Codec,-11} {entry.Name}");
        }
        Console.WriteLine($"-- {sdt.Entries.Count} entries");
        return 0;
    }

    internal static int RunSdtExtract(string[] args)
    {
        if (args.Length == 5 && args[3] != "--out")
        {
            Console.Error.WriteLine(
                "usage: dk2tools sdt extract <file.sdt> [--out converted-assets/audio]");
            return 2;
        }
        string output = RequireConvertedOutput(args.Length == 5
            ? args[4] : Path.Combine("converted-assets", "audio"));
        Directory.CreateDirectory(output);
        SdtArchive sdt = SdtArchive.Open(ResolveData(args[2]));
        int produced = 0;
        foreach (SdtEntry entry in sdt.Entries)
        {
            if (entry.Codec == SdtCodec.Empty)
            {
                Console.WriteLine($"SKIPPED  {entry.Index,4} {entry.Name} (empty placeholder)");
                continue;
            }
            string destination = Path.Combine(output, sdt.OutputFileName(entry));
            byte[] bytes = sdt.Extract(entry);
            File.WriteAllBytes(destination, bytes);
            string format = entry.Codec switch
            {
                SdtCodec.Pcm => "PCM in WAV",
                SdtCodec.MpegLayer1 => "raw MPEG Audio Layer I stream",
                SdtCodec.MpegLayer2 => "raw MPEG Audio Layer II stream",
                SdtCodec.MpegLayer3 => "raw MPEG Audio Layer III stream",
                _ => $"raw undecoded type {entry.Type}",
            };
            Console.WriteLine($"EXTRACTED {entry.Index,4} {entry.Name} -> " +
                              $"{Path.GetRelativePath(output, destination)} ({format}, {bytes.Length} bytes)");
            produced++;
        }
        Console.WriteLine($"-- extracted {produced}, skipped {sdt.Entries.Count - produced}");
        return 0;
    }

    internal static int RunStrList(string[] args)
    {
        Dk2StringTable table = Dk2StringTable.Open(ResolveText(args[2]));
        foreach (Dk2StringEntry entry in table.Entries)
            Console.WriteLine($"{entry.Id,6} {EscapeForOneLine(entry.Value)}");
        Console.WriteLine($"-- {table.Entries.Count} entries, file id {table.FileId}");
        return 0;
    }

    internal static int RunStrGet(string[] args)
    {
        if (!uint.TryParse(args[3], out uint id))
        {
            Console.Error.WriteLine($"invalid string id: '{args[3]}'");
            return 2;
        }
        Dk2StringTable table = Dk2StringTable.Open(ResolveText(args[2]));
        if (!table.TryResolve(id, out string value))
        {
            Console.Error.WriteLine($"string id {id} is not in this table");
            return 2;
        }
        Console.WriteLine(value);
        return 0;
    }
}
