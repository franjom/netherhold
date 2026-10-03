// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Kwd;
using DK2.Data.Model;
using DK2.TestSupport;

namespace DK2.Data.Tests;

public class KwdTypedRecordsTests
{
    [InstallFact]
    public void EveryShippedRecordUsesItsTypedWriterAndEveryFileIsIdentical()
    {
        string editor=Path.Combine(Dk2InstallGate.Path,"Data","editor");
        string[] files=Directory.GetFiles(editor,"*.kwd").Concat(Directory.GetFiles(Path.Combine(editor,"maps"),"*.kwd"))
            .Concat(Directory.GetFiles(Path.Combine(editor,"maps"),"*.kld")).ToArray();
        int count=0; var types=new HashSet<KwdRecordType>();
        foreach(string file in files)
        {
            var chunks=KwdReader.ReadFile(file);
            KwdChunk[] rewritten=chunks.Select(chunk=>
            {
                types.Add(chunk.Type); count+=KwdTypedRecords.Read(chunk).Count;
                return KwdTypedRecords.Rewrite(chunk);
            }).ToArray();
            Assert.Equal(File.ReadAllBytes(file),KwdWriter.ToBytes(rewritten));
        }
        Assert.Equal(408,files.Length); Assert.Equal(371126,count); Assert.Equal(17,types.Count);
    }
}
