// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;

namespace DK2.Tools;

public static class KwdRewriteCommand
{
    public static int Run(string[] args,TextWriter stdout,TextWriter stderr)
    {
        if(args.Length!=2) { stderr.WriteLine("usage: dk2tools kwd rewrite <file|dir> <outdir>"); return 2; }
        try
        {
            var result=KwdRewriteAudit.Run(args[0],args[1]);
            foreach(var file in result.Files) stdout.WriteLine(Format(file));
            int different=result.Files.Count(f=>f.Difference is not null);
            stdout.WriteLine($"{result.Files.Count} files; {result.Files.Sum(f=>f.Chunks)} chunks; " +
                $"{result.Files.Sum(f=>f.Records)} records; {different} different");
            return different==0 ? 0 : 1;
        }
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException)
        { stderr.WriteLine($"kwd rewrite: {e.Message}"); return 2; }
    }

    public static string Format(KwdRewriteAudit.FileResult file)
    {
        if(file.Difference is not { } d) return $"{file.RelativePath}: identical";
        return $"{file.RelativePath}: first difference at byte {d.Offset} (0x{d.Offset:X}), " +
            $"chunk {d.ChunkIndex?.ToString() ?? "-"} ({d.ChunkType?.ToString() ?? "-"}), " +
            $"record {d.RecordIndex?.ToString() ?? "-"}, field {d.Field}: " +
            $"{d.Expected?.ToString("X2") ?? "EOF"} -> {d.Actual?.ToString("X2") ?? "EOF"}";
    }
}
